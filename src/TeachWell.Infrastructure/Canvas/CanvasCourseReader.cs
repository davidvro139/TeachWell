using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using TeachWell.Application;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CanvasReadException(CanvasError error) : Exception(error.ToString())
{
    public CanvasError Error { get; } = error;
}

public sealed partial class CanvasPatClient
{
    private sealed class ReadBudget { public int Requests; }
    private static bool NumericId(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 80 && value[0] != '0' && value.All(c => c is >= '0' and <= '9');
    private static string Id(JsonElement e, string property = "id")
    {
        if (!e.TryGetProperty(property, out var value) || value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) throw new CanvasReadException(CanvasError.UnexpectedResponse);
        var id = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString();
        return NumericId(id) ? id! : throw new CanvasReadException(CanvasError.UnexpectedResponse);
    }
    private static string Text(JsonElement e, string property, int limit = 240)
    {
        if (!e.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) throw new CanvasReadException(CanvasError.UnexpectedResponse);
        var text = value.GetString()!;
        if (text.Length > limit) throw new CanvasReadException(CanvasError.LimitExceeded);
        return text;
    }
    private static int Position(JsonElement e) => e.TryGetProperty("position", out var p) && p.TryGetInt32(out var n) ? n : 0;
    private static string Published(JsonElement e) => !e.TryGetProperty("published", out var p) ? "Unknown" : p.ValueKind switch
    {
        JsonValueKind.True => "Published", JsonValueKind.False => "Unpublished", _ => "Unknown"
    };
    // Canvas includes rubric fields only when a rubric is associated; other assignment fields are ignored.
    private static TeachWell.Domain.AssignmentEvidence AssignmentMetadata(JsonElement e) => new(
        e.TryGetProperty("rubric", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 0 ||
            e.TryGetProperty("rubric_settings", out var rs) && rs.ValueKind == JsonValueKind.Object,
        e.TryGetProperty("points_possible", out var p) && p.TryGetDecimal(out var points) ? points : null,
        !e.TryGetProperty("due_at", out var due) ? null : due.ValueKind == JsonValueKind.String,
        due.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(due.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dueAt) ? dueAt.UtcDateTime : null);
    // Leaves room under the 200-request budget for the requests that follow; question fetching stops here.
    private const int QuestionBudget = 190;
    private static string OptionalHtml(JsonElement e, string property) =>
        !e.TryGetProperty(property, out var v) || v.ValueKind == JsonValueKind.Null ? "" : Text(e, property, 60000);
    private static string? NumberText(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;
    private static (string Text, TeachWell.Domain.ContentEvidence Evidence, string? Preview) QuizContent(JsonElement quiz, Uri origin, IReadOnlyList<CanvasActivityContent.Question>? questions)
    {
        var metadata = AssignmentMetadata(quiz);
        var html = CanvasActivityContent.Quiz(CanvasActivityContent.QuizKind(quiz.TryGetProperty("quiz_type", out var qt) && qt.ValueKind == JsonValueKind.String ? qt.GetString() : null),
            OptionalHtml(quiz, "description"), questions, metadata.PointsPossible, NumberText(quiz, "allowed_attempts"), NumberText(quiz, "time_limit"),
            questions is null ? "Questions were not fetched from Canvas for this quiz." : null) ?? throw new CanvasReadException(CanvasError.LimitExceeded);
        var (text, evidence) = CanvasText.Normalize(html, origin, metadata);
        return (text, evidence, CanvasText.Preview(html, origin));
    }
    private static bool Teacher(JsonElement e) => e.TryGetProperty("enrollments", out var enrollments) && enrollments.ValueKind == JsonValueKind.Array &&
        enrollments.EnumerateArray().Any(a => a.TryGetProperty("type", out var t) && t.GetString() == "teacher" &&
            a.TryGetProperty("enrollment_state", out var s) && s.GetString() == "active");

    public async Task<CanvasCoursesResult> ListCoursesAsync(Uri origin, string token, CancellationToken ct)
    {
        try
        {
            var rows = await Collection(origin, "api/v1/courses?enrollment_type=teacher&enrollment_state=active&per_page=50", token, new(), ct);
            var courses = rows.Where(Teacher).Select(c => new CanvasCourseOption(Id(c), Text(c, "name"), Text(c, "course_code"))).ToArray();
            if (courses.Select(c => c.Id).Distinct().Count() != courses.Length) throw new CanvasReadException(CanvasError.UnexpectedResponse);
            return new(courses);
        }
        catch (CanvasReadException e) { return new([], e.Error); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new([], CanvasError.Timeout); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return new([], CanvasError.UnexpectedResponse); }
    }

    public async Task<CanvasReadResult> ReadCourseAsync(Uri origin, string token, string courseId, CancellationToken ct)
    {
        try
        {
            if (!NumericId(courseId)) return new(null, CanvasError.CourseNotAuthorized);
            var budget = new ReadBudget();
            var root = $"api/v1/courses/{courseId}";
            var (courseJson, _) = await Read(origin, new Uri(origin, root), token, budget, ct);
            using var course = courseJson;
            if (!Teacher(course.RootElement) || Id(course.RootElement) != courseId) return new(null, CanvasError.CourseNotAuthorized);
            var option = new CanvasCourseOption(courseId, Text(course.RootElement, "name"), Text(course.RootElement, "course_code"));
            var elements = new List<CanvasElementData>(); var warnings = new List<string>();
            var pageIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var pages = await Collection(origin, root + "/pages?per_page=50", token, budget, ct);
            foreach (var page in pages)
            {
                var id = Id(page, "page_id"); var slug = Text(page, "url", 500); pageIds.Add(slug, id);
                var sourceUrl = new Uri(origin, $"courses/{courseId}/pages/{Uri.EscapeDataString(slug)}").AbsoluteUri;
                try
                {
                    var (json, _) = await Read(origin, new Uri(origin, root + "/pages/" + id), token, budget, ct);
                    using (json)
                    {
                        if (Id(json.RootElement, "page_id") != id) throw new CanvasReadException(CanvasError.UnexpectedResponse);
                        var body = json.RootElement.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.Null ? "" : Text(json.RootElement, "body", 60000);
                        var (text, evidence) = CanvasText.Normalize(body, origin);
                        elements.Add(new(id, "page", Text(page, "title"), text, sourceUrl, Published(page), Evidence: evidence, PreviewHtml: CanvasText.Preview(body, origin)));
                    }
                }
                catch (CanvasReadException e) when (e.Error is CanvasError.PermissionDenied or CanvasError.ResourceNotFound)
                {
                    const string warning = "A page body was unavailable from Canvas; no complete-content claim is made.";
                    warnings.Add(warning); elements.Add(new(id, "page", Text(page, "title"), "", sourceUrl, "Unavailable", Warning: warning));
                }
            }
            var assignments = await Collection(origin, root + "/assignments?per_page=50", token, budget, ct);
            var newQuizzes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assignment in assignments)
            {
                var id = Id(assignment);
                var description = OptionalHtml(assignment, "description");
                var metadata = AssignmentMetadata(assignment);
                var toolUrl = assignment.TryGetProperty("external_tool_tag_attributes", out var tool) && tool.ValueKind == JsonValueKind.Object
                    && tool.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                var externalTool = assignment.TryGetProperty("submission_types", out var st) && st.ValueKind == JsonValueKind.Array
                    && st.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "external_tool");
                var newQuiz = assignment.TryGetProperty("is_quiz_lti_assignment", out var q) && q.ValueKind == JsonValueKind.True || externalTool && CanvasActivityContent.IsNewQuiz(toolUrl);
                if (newQuiz)
                {
                    newQuizzes.Add(id);
                    description = CanvasActivityContent.Quiz("New Quiz", description, null, metadata.PointsPossible, null, null, CanvasActivityContent.NewQuizNote) ?? description;
                }
                else if (externalTool) description = $"<p>{WebUtility.HtmlEncode(CanvasActivityContent.ExternalToolNote(toolUrl))}</p>{description}";
                var (text, evidence) = CanvasText.Normalize(description, origin, metadata);
                elements.Add(new(id, newQuiz ? TeachWell.Domain.ReviewVocabulary.QuizType : "assignment", Text(assignment, "name"), text,
                    new Uri(origin, $"courses/{courseId}/assignments/{id}").AbsoluteUri, Published(assignment), Evidence: evidence,
                    PreviewHtml: CanvasText.Preview(description, origin)));
            }

            // Classic quizzes; their questions are fetched after modules, within the request budget.
            var quizzes = new List<JsonElement>();
            try { quizzes = await Collection(origin, root + "/quizzes?per_page=50", token, budget, ct); }
            catch (CanvasReadException e) when (e.Error is CanvasError.PermissionDenied or CanvasError.ResourceNotFound)
            { warnings.Add("Classic quizzes could not be listed from Canvas (the quizzes tool may be off for this course); none were imported."); }
            foreach (var quiz in quizzes)
            {
                var id = Id(quiz);
                var (text, evidence, preview) = QuizContent(quiz, origin, null);
                elements.Add(new(id, TeachWell.Domain.ReviewVocabulary.QuizType, Text(quiz, "title"), text, new Uri(origin, $"courses/{courseId}/quizzes/{id}").AbsoluteUri,
                    Published(quiz), Evidence: evidence, PreviewHtml: preview));
            }

            // Discussion topics (Canvas excludes announcements from this list). Graded ones carry an assignment object.
            var topics = new List<JsonElement>();
            try { topics = await Collection(origin, root + "/discussion_topics?per_page=50", token, budget, ct); }
            catch (CanvasReadException e) when (e.Error is CanvasError.PermissionDenied or CanvasError.ResourceNotFound)
            { warnings.Add("Discussions could not be listed from Canvas; none were imported."); }
            foreach (var topic in topics)
            {
                var id = Id(topic);
                var graded = topic.TryGetProperty("assignment", out var a) && a.ValueKind == JsonValueKind.Object ? a : (JsonElement?)null;
                var metadata = graded is { } g ? AssignmentMetadata(g) : null;
                var html = CanvasActivityContent.Discussion(OptionalHtml(topic, "message"), metadata?.PointsPossible, graded is not null)
                    ?? throw new CanvasReadException(CanvasError.LimitExceeded);
                var (text, evidence) = CanvasText.Normalize(html, origin, metadata);
                elements.Add(new(id, TeachWell.Domain.ReviewVocabulary.DiscussionType, Text(topic, "title"), text, new Uri(origin, $"courses/{courseId}/discussion_topics/{id}").AbsoluteUri,
                    Published(topic), Evidence: evidence, PreviewHtml: CanvasText.Preview(html, origin)));
            }
            var modules = await Collection(origin, root + "/modules?per_page=50", token, budget, ct);
            foreach (var module in modules)
            {
                var id = Id(module);
                elements.Add(new(id, "module", Text(module, "name"), "", new Uri(origin, $"courses/{courseId}/modules/{id}").AbsoluteUri, Published(module), Position(module)));
                foreach (var item in await Collection(origin, root + $"/modules/{id}/items?per_page=50", token, budget, ct))
                {
                    var itemId = Id(item); var type = Text(item, "type", 40); string? targetType = null, targetId = null;
                    if (type == "Assignment") { targetId = Id(item, "content_id"); targetType = newQuizzes.Contains(targetId) ? TeachWell.Domain.ReviewVocabulary.QuizType : "assignment"; }
                    if (type == "Quiz") { targetType = TeachWell.Domain.ReviewVocabulary.QuizType; targetId = Id(item, "content_id"); }
                    if (type == "Discussion") { targetType = TeachWell.Domain.ReviewVocabulary.DiscussionType; targetId = Id(item, "content_id"); }
                    if (type == "Page") { targetType = "page"; pageIds.TryGetValue(Text(item, "page_url", 500), out targetId); }
                    var targetExists = targetId is not null && elements.Any(e => e.Type == targetType && e.Id == targetId);
                    var header = type == "SubHeader";
                    var warning = targetExists || header ? null : type switch
                    {
                        "File" => "This module item is a course file; files and media are listed but not imported.",
                        "ExternalUrl" => "This module item is an external link; external links are not fetched.",
                        "ExternalTool" => "This module item opens an external tool (LTI); external tools are not imported.",
                        _ => "This module item's target was unavailable from Canvas, so it was not imported."
                    };
                    if (warning is not null) warnings.Add(warning);
                    elements.Add(new(itemId, "moduleItem", Text(item, "title"), "", new Uri(origin, $"courses/{courseId}/modules/items/{itemId}").AbsoluteUri,
                        header ? TeachWell.Domain.ReviewVocabulary.TextHeaderStatus : warning is null ? Published(item) : "Unavailable", Position(item), id,
                        targetExists ? targetType : null, targetExists ? targetId : null, warning));
                }
            }
            // Quiz questions last: one request per quiz, only while the per-import request budget allows, so a large course still imports.
            var unfetched = new List<string>();
            foreach (var quiz in quizzes)
            {
                var id = Id(quiz);
                if (budget.Requests >= QuestionBudget) { unfetched.Add(Text(quiz, "title")); continue; }
                try
                {
                    var questions = (await Collection(origin, root + $"/quizzes/{id}/questions?per_page=50", token, budget, ct))
                        .Select(q => new CanvasActivityContent.Question(OptionalHtml(q, "question_text"),
                            CanvasActivityContent.QuestionType(q.TryGetProperty("question_type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null))).ToList();
                    var index = elements.FindIndex(e => e.Type == TeachWell.Domain.ReviewVocabulary.QuizType && e.Id == id);
                    var (text, evidence, preview) = QuizContent(quiz, origin, questions);
                    elements[index] = elements[index] with { PlainText = text, Evidence = evidence, PreviewHtml = preview };
                }
                catch (CanvasReadException e) when (e.Error is CanvasError.PermissionDenied or CanvasError.ResourceNotFound) { unfetched.Add(Text(quiz, "title")); }
            }
            if (unfetched.Count > 0)
                warnings.Add($"Questions were not fetched for {unfetched.Count} quiz(zes) ({string.Join(", ", unfetched.Take(5))}{(unfetched.Count > 5 ? ", …" : "")}); their settings and instructions were imported.");
            if (elements.Count > 3000) throw new CanvasReadException(CanvasError.LimitExceeded);
            if (elements.Select(e => (e.Type, e.Id)).Distinct().Count() != elements.Count) throw new CanvasReadException(CanvasError.UnexpectedResponse);
            return new(new(option, elements, warnings.Distinct().ToArray()));
        }
        catch (CanvasReadException e) { return new(null, e.Error); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(null, CanvasError.Timeout); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException) { return new(null, CanvasError.UnexpectedResponse); }
    }

    private async Task<List<JsonElement>> Collection(Uri origin, string path, string token, ReadBudget budget, CancellationToken ct)
    {
        var result = new List<JsonElement>(); var visited = new HashSet<string>(StringComparer.Ordinal);
        Uri? next = new(origin, path); var endpoint = next.AbsolutePath;
        while (next is not null)
        {
            if (visited.Count >= 40 || !visited.Add(next.AbsoluteUri)) throw new CanvasReadException(CanvasError.LimitExceeded);
            var (data, following) = await Read(origin, next, token, budget, ct);
            using (data)
            {
                if (data.RootElement.ValueKind != JsonValueKind.Array) throw new CanvasReadException(CanvasError.UnexpectedResponse);
                result.AddRange(data.RootElement.EnumerateArray().Select(e => e.Clone()));
                if (result.Count > 2000) throw new CanvasReadException(CanvasError.LimitExceeded);
            }
            if (following is not null && NormalizePath(following.AbsolutePath) != NormalizePath(endpoint)) throw new CanvasReadException(CanvasError.RedirectBlocked);
            next = following;
        }
        return result;
    }
    private static string NormalizePath(string path) => path.EndsWith(".json", StringComparison.Ordinal) ? path[..^5] : path;

    private async Task<(JsonDocument Data, Uri? Next)> Read(Uri origin, Uri destination, string token, ReadBudget budget, CancellationToken ct)
    {
        if (policy.ParseOrigin(origin.AbsoluteUri) is null || destination.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority) ||
            destination.UserInfo.Length > 0 || destination.Fragment.Length > 0 || Uri.UnescapeDataString(destination.Query).Contains("access_token", StringComparison.OrdinalIgnoreCase))
            throw new CanvasReadException(CanvasError.RedirectBlocked);
        for (var attempt = 0; ; attempt++)
        {
            if (++budget.Requests > 200) throw new CanvasReadException(CanvasError.LimitExceeded);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, destination);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await http.SendAsync(request, ct);
                if ((int)response.StatusCode is 429 or 503 && attempt < 2)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(1);
                    if (delay > TimeSpan.FromSeconds(3)) throw new CanvasReadException(CanvasError.RateLimited);
                    await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct); continue;
                }
                var error = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => CanvasError.InvalidToken, HttpStatusCode.Forbidden => CanvasError.PermissionDenied,
                    HttpStatusCode.NotFound => CanvasError.ResourceNotFound, (HttpStatusCode)429 => CanvasError.RateLimited,
                    _ when (int)response.StatusCode is >= 300 and < 400 => CanvasError.RedirectBlocked,
                    _ when !response.IsSuccessStatusCode => CanvasError.Unavailable, _ => CanvasError.None
                };
                if (error != CanvasError.None) throw new CanvasReadException(error);
                var next = Next(response);
                var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), new JsonDocumentOptions { MaxDepth = 48 }, ct);
                return (json, next);
            }
            catch (HttpRequestException) { throw new CanvasReadException(CanvasError.Unavailable); }
        }
    }
    private static Uri? Next(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        var header = string.Join(",", values);
        var links = Regex.Matches(header, "<(?<url>[^>]+)>\\s*(?<parameters>[^,]*)");
        var next = links.Cast<Match>().Where(m => Regex.IsMatch(m.Groups["parameters"].Value, "(?:^|;)\\s*rel\\s*=\\s*\"?next\"?(?:\\s*;|\\s*$)", RegexOptions.IgnoreCase)).ToArray();
        if (next.Length > 1 || next.Length == 0 && Regex.IsMatch(header, "\\bnext\\b", RegexOptions.IgnoreCase)) throw new CanvasReadException(CanvasError.UnexpectedResponse);
        if (next.Length == 0) return null;
        return Uri.TryCreate(next[0].Groups["url"].Value, UriKind.Absolute, out var uri) ? uri : throw new CanvasReadException(CanvasError.UnexpectedResponse);
    }
}
