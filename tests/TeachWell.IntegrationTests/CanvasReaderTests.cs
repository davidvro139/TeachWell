using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TeachWell.Application;
using TeachWell.Infrastructure.Canvas;
using Xunit;

public class CanvasReaderTests
{
    private static CanvasHostPolicy Policy() => new(new CanvasIntegrationOptions(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Features:CanvasPAT"] = "true", ["Canvas:AllowedHosts:0"] = "canvas.example.test", ["Canvas:EncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }).Build()));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
    private static HttpResponseMessage Json(string value, string? next = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value) };
        if (next is not null) response.Headers.Add("Link", $"<{next}>; rel=\"next\"");
        return response;
    }
    [Fact] public async Task Course_listing_follows_opaque_links_and_filters_student_enrollments()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(r => {
            calls.Add(r.RequestUri!.AbsoluteUri); Assert.Equal("Bearer", r.Headers.Authorization!.Scheme);
            return calls.Count == 1 ? Json("[{\"id\":42,\"name\":\"Teaching\",\"course_code\":\"T42\",\"enrollments\":[{\"type\":\"teacher\",\"enrollment_state\":\"active\"}]}]", "https://canvas.example.test/api/v1/courses?opaque=cursor-value") :
                Json("[{\"id\":43,\"name\":\"Not a teaching course\",\"course_code\":\"S43\",\"enrollments\":[{\"type\":\"student\"}]}]");
        }));
        var result = await new CanvasPatClient(http, Policy()).ListCoursesAsync(new("https://canvas.example.test/"), "fictional-test-input", default);
        Assert.Equal(CanvasError.None, result.Error); Assert.Equal("42", Assert.Single(result.Courses).Id); Assert.Equal(2, calls.Count); Assert.Contains("opaque=cursor-value", calls[1]);
    }
    [Theory]
    [InlineData("https://evil.example.test/api/v1/courses?next=2")]
    [InlineData("https://canvas.example.test/api/v1/users?next=2")]
    [InlineData("http://canvas.example.test/api/v1/courses?next=2")]
    [InlineData("https://canvas.example.test/api/v1/courses?access_token=do-not-send")]
    [InlineData("https://credentials@canvas.example.test/api/v1/courses?next=2")]
    public async Task Unsafe_next_link_is_never_requested(string next)
    {
        var calls = 0; using var http = new HttpClient(new Handler(_ => { calls++; return Json("[]", next); }));
        var result = await new CanvasPatClient(http, Policy()).ListCoursesAsync(new("https://canvas.example.test/"), "fictional-test-input", default);
        Assert.Equal(CanvasError.RedirectBlocked, result.Error); Assert.Equal(1, calls); Assert.Empty(result.Courses);
    }
    [Fact] public async Task Pagination_cycle_is_bounded_and_does_not_return_partial_results()
    {
        using var http = new HttpClient(new Handler(_ => Json("[]", "https://canvas.example.test/api/v1/courses?enrollment_type=teacher&enrollment_state=active&per_page=50")));
        var result = await new CanvasPatClient(http, Policy()).ListCoursesAsync(new("https://canvas.example.test/"), "fictional-test-input", default);
        Assert.Equal(CanvasError.LimitExceeded, result.Error); Assert.Empty(result.Courses);
    }
    [Fact] public async Task Throttle_retry_is_bounded_and_honors_a_short_retry_after()
    {
        var calls = 0; using var http = new HttpClient(new Handler(_ => {
            calls++; if (calls == 2) return Json("[]");
            var response = new HttpResponseMessage((HttpStatusCode)429); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return response;
        }));
        var result = await new CanvasPatClient(http, Policy()).ListCoursesAsync(new("https://canvas.example.test/"), "fictional-test-input", default);
        Assert.Equal(CanvasError.None, result.Error); Assert.Equal(2, calls);
    }
    [Fact] public async Task Course_read_normalizes_html_and_repeated_module_placements_without_fetching_external_urls()
    {
        var urls = new List<string>();
        using var http = new HttpClient(new Handler(r => {
            var path = r.RequestUri!.AbsolutePath; urls.Add(path);
            return path switch {
                "/api/v1/courses/42" => Json("{\"id\":42,\"name\":\"Fictional course\",\"course_code\":\"TEST\",\"enrollments\":[{\"type\":\"teacher\",\"enrollment_state\":\"active\"}]}"),
                "/api/v1/courses/42/pages" => Json("[{\"page_id\":2,\"url\":\"orientation\",\"title\":\"Orientation\"}]"),
                "/api/v1/courses/42/pages/2" => Json("{\"page_id\":2,\"body\":\"<h1>Welcome</h1><script>stealSecret()</script><iframe src='https://evil.example.test'></iframe><p onclick='bad()'>Read &amp; reflect</p>\",\"html_url\":\"javascript:bad()\"}"),
                "/api/v1/courses/42/assignments" => Json("[{\"id\":3,\"name\":\"Practice\",\"description\":\"<p>Explain a choice</p>\",\"published\":false}]"),
                "/api/v1/courses/42/quizzes" or "/api/v1/courses/42/discussion_topics" => Json("[]"),
                "/api/v1/courses/42/modules" => Json("[{\"id\":1,\"name\":\"Start\",\"position\":1}]"),
                "/api/v1/courses/42/modules/1/items" => Json("[{\"id\":4,\"title\":\"Read\",\"type\":\"Page\",\"page_url\":\"orientation\",\"position\":1},{\"id\":5,\"title\":\"Read again\",\"type\":\"Page\",\"page_url\":\"orientation\",\"position\":2},{\"id\":6,\"title\":\"External\",\"type\":\"ExternalUrl\",\"external_url\":\"http://169.254.169.254\",\"position\":3}]"),
                _ => throw new InvalidOperationException("Unexpected request path")
            };
        }));
        var result = await new CanvasPatClient(http, Policy()).ReadCourseAsync(new("https://canvas.example.test/"), "fictional-test-input", "42", default);
        Assert.Equal(CanvasError.None, result.Error); Assert.NotNull(result.Data);
        var page = Assert.Single(result.Data.Elements, e => e.Type == "page"); Assert.Contains("Read & reflect", page.PlainText); Assert.DoesNotContain("stealSecret", page.PlainText); Assert.DoesNotContain("<", page.PlainText);
        Assert.Equal("Unknown", page.Status);
        Assert.Equal("Unpublished", Assert.Single(result.Data.Elements, e => e.Type == "assignment").Status);
        Assert.Equal(2, result.Data.Elements.Count(e => e.TargetId == "2")); Assert.Single(result.Data.Warnings); Assert.Equal(8, urls.Count);
        Assert.All(result.Data.Elements, e => Assert.StartsWith("https://canvas.example.test/", e.SourceUrl));
    }
    [Fact] public async Task Course_read_imports_quizzes_with_questions_discussions_new_quizzes_and_external_tools()
    {
        var urls = new List<string>();
        using var http = new HttpClient(new Handler(r => {
            var path = r.RequestUri!.AbsolutePath; urls.Add(path);
            return path switch {
                "/api/v1/courses/42" => Json("{\"id\":42,\"name\":\"Fictional course\",\"course_code\":\"TEST\",\"enrollments\":[{\"type\":\"teacher\",\"enrollment_state\":\"active\"}]}"),
                "/api/v1/courses/42/pages" => Json("[]"),
                "/api/v1/courses/42/assignments" => Json("""
                    [{"id":3,"name":"Lab in CodeGrade","description":"<p>Submit in CodeGrade.</p>","published":true,"points_possible":10,"submission_types":["external_tool"],
                      "external_tool_tag_attributes":{"url":"https://app.codegra.de/lti/launch"}},
                     {"id":4,"name":"Unit 1 New Quiz","description":null,"published":true,"points_possible":20,"submission_types":["external_tool"],"is_quiz_lti_assignment":true}]
                    """),
                "/api/v1/courses/42/quizzes" => Json("""
                    [{"id":7,"title":"Week 1 survey","description":"<p>How did week 1 go?</p>","quiz_type":"survey","points_possible":0,"allowed_attempts":1,"time_limit":null,"published":false,"due_at":"2026-11-02T06:59:00Z"}]
                    """),
                "/api/v1/courses/42/quizzes/7/questions" => Json("""
                    [{"id":70,"question_text":"<p>Explain the pace.</p>","question_type":"essay_question"},{"id":71,"question_text":"<p>Pick a chart</p><img src=\"x.png\">","question_type":"multiple_choice_question"}]
                    """),
                "/api/v1/courses/42/discussion_topics" => Json("""
                    [{"id":9,"title":"Introduce yourself","message":"<p>Say hello.</p>","published":true,"assignment":{"points_possible":5,"due_at":"2026-11-03T06:59:00Z"}},
                     {"id":10,"title":"Questions board","message":"<p>Ask anything.</p>","published":true,"assignment":null}]
                    """),
                "/api/v1/courses/42/modules" => Json("[{\"id\":1,\"name\":\"Week 1\",\"position\":1}]"),
                "/api/v1/courses/42/modules/1/items" => Json("""
                    [{"id":20,"title":"Learn","type":"SubHeader","position":1},{"id":21,"title":"Week 1 survey","type":"Quiz","content_id":7,"position":2},
                     {"id":22,"title":"Introduce yourself","type":"Discussion","content_id":9,"position":3},{"id":23,"title":"Unit 1 New Quiz","type":"Assignment","content_id":4,"position":4},
                     {"id":24,"title":"Slides","type":"File","content_id":99,"position":5}]
                    """),
                _ => throw new InvalidOperationException("Unexpected request path " + path)
            };
        }));
        var result = await new CanvasPatClient(http, Policy()).ReadCourseAsync(new("https://canvas.example.test/"), "fictional-test-input", "42", default);
        Assert.Equal(CanvasError.None, result.Error);
        var data = result.Data!;
        var survey = data.Elements.Single(e => e.Id == "7");
        Assert.Equal(("quiz", "Unpublished"), (survey.Type, survey.Status));
        Assert.StartsWith("Ungraded survey · 2 questions · 1 essay, 1 multiple choice · 0 points · 1 attempt", survey.PlainText);
        Assert.Contains("Explain the pace.", survey.PlainText);
        Assert.DoesNotContain("not fetched", survey.PlainText);
        Assert.Contains(survey.Evidence!.Images, i => i.Alt is null);
        var graded = data.Elements.Single(e => e.Id == "9");
        Assert.Equal(("discussion", 5m), (graded.Type, graded.Evidence!.Assignment!.PointsPossible!.Value));
        Assert.StartsWith("Graded discussion · 5 points", graded.PlainText);
        Assert.Null(data.Elements.Single(e => e.Id == "10").Evidence!.Assignment);
        var newQuiz = data.Elements.Single(e => e.Id == "4");
        Assert.Equal("quiz", newQuiz.Type);
        Assert.Contains("New Quizzes tool", newQuiz.PlainText);
        Assert.StartsWith("Submitted in an external tool (app.codegra.de)", data.Elements.Single(e => e.Id == "3").PlainText);
        var items = data.Elements.Where(e => e.Type == "moduleItem").ToDictionary(e => e.Id);
        Assert.Equal(TeachWell.Domain.ReviewVocabulary.TextHeaderStatus, items["20"].Status);
        Assert.Equal([("quiz", "7"), ("discussion", "9"), ("quiz", "4")], new[] { items["21"], items["22"], items["23"] }.Select(i => (i.TargetType!, i.TargetId!)));
        Assert.Contains("course file", items["24"].Warning);
        Assert.True(CourseDataApplier.IsConsistent(data));
    }

    [Fact] public async Task Quiz_questions_stop_at_the_request_budget_and_say_which_were_not_fetched()
    {
        // 200 quizzes: listing them fits the budget, but one question request per quiz cannot.
        var quizzes = string.Join(",", Enumerable.Range(1, 200).Select(i => $"{{\"id\":{i},\"title\":\"Quiz {i}\",\"quiz_type\":\"assignment\",\"published\":true}}"));
        using var http = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath switch {
            "/api/v1/courses/42" => Json("{\"id\":42,\"name\":\"Fictional course\",\"course_code\":\"TEST\",\"enrollments\":[{\"type\":\"teacher\",\"enrollment_state\":\"active\"}]}"),
            "/api/v1/courses/42/quizzes" => Json($"[{quizzes}]"),
            var p when p.EndsWith("/questions") => Json("[{\"question_text\":\"<p>Q</p>\",\"question_type\":\"essay_question\"}]"),
            _ => Json("[]")
        }));
        var result = await new CanvasPatClient(http, Policy()).ReadCourseAsync(new("https://canvas.example.test/"), "fictional-test-input", "42", default);
        Assert.Equal(CanvasError.None, result.Error);
        var data = result.Data!;
        Assert.Equal(200, data.Elements.Count(e => e.Type == "quiz"));
        Assert.Contains(data.Warnings, w => w.StartsWith("Questions were not fetched for ") && w.Contains("their settings and instructions were imported"));
        Assert.Contains(data.Elements, e => e.Type == "quiz" && e.PlainText.Contains("1 question"));
        Assert.Contains(data.Elements, e => e.Type == "quiz" && e.PlainText.Contains("Questions were not fetched from Canvas for this quiz."));
    }

    [Theory][InlineData("student")][InlineData("teacher")]
    public async Task Missing_teacher_enrollment_or_active_state_blocks_course_content_requests(string role)
    {
        var calls = 0; using var http = new HttpClient(new Handler(_ => { calls++; return Json(JsonSerializer.Serialize(new { id = 42, name = "Other", course_code = "O", enrollments = new[] { new { type = role } } })); }));
        var result = await new CanvasPatClient(http, Policy()).ReadCourseAsync(new("https://canvas.example.test/"), "fictional-test-input", "42", default);
        Assert.Equal(CanvasError.CourseNotAuthorized, result.Error); Assert.Null(result.Data); Assert.Equal(1, calls);
    }
    [Fact] public void Malicious_html_becomes_inert_text_with_readable_paragraphs()
    {
        var text = CanvasText.FromHtml("<h2>Goal</h2><p>Build &amp; test.</p><style>bad</style><svg onload='bad()'>bad</svg><img src='http://169.254.169.254' onerror='bad()'><a href='javascript:bad()'>Safe label</a>");
        Assert.Contains("Goal", text); Assert.Contains("Build & test.", text); Assert.Contains("Safe label", text); Assert.DoesNotContain("bad", text); Assert.DoesNotContain("169.254", text); Assert.Contains(Environment.NewLine, text);
    }
}
