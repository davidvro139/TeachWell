using System.Globalization;
using System.Net;
using System.Text;

namespace TeachWell.Infrastructure.Canvas;

// Builds the single HTML document TeachWell stores for a quiz or discussion, shared by the export and API readers so both describe the
// same activity the same way. The result goes through CanvasText (sanitizer, evidence, preview) like any page.
internal static class CanvasActivityContent
{
    public const int MaxChars = 60000;
    public sealed record Question(string Html, string Type);

    public static string QuizKind(string? quizType) => quizType switch
    {
        "practice_quiz" => "Practice quiz", "survey" => "Ungraded survey", "graded_survey" => "Graded survey", _ => "Graded quiz"
    };
    public static string QuestionType(string? type) => string.IsNullOrWhiteSpace(type) ? "question" : type.Replace("_question", "").Replace('_', ' ');

    // A summary line, an optional note, the instructions, then the questions in order. Questions that would exceed the character limit
    // are left out and the content says so. Returns null only when the instructions alone exceed the limit.
    public static string? Quiz(string kind, string? description, IReadOnlyList<Question>? questions, decimal? points, string? attempts, string? timeLimitMinutes, string? note = null)
    {
        var details = new List<string> { kind };
        if (questions is not null)
        {
            details.Add(questions.Count == 1 ? "1 question" : $"{questions.Count} questions");
            if (questions.Count > 0) details.Add(string.Join(", ", questions.GroupBy(q => q.Type).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}")));
        }
        if (points is { } p) details.Add($"{p.ToString("0.##", CultureInfo.InvariantCulture)} points");
        if (attempts is { Length: > 0 }) details.Add(attempts == "-1" ? "unlimited attempts" : attempts == "1" ? "1 attempt" : $"{attempts} attempts");
        if (timeLimitMinutes is { Length: > 0 } and not "0") details.Add($"{timeLimitMinutes}-minute time limit");

        var html = new StringBuilder($"<p>{WebUtility.HtmlEncode(string.Join(" · ", details))}</p>");
        if (note is not null) html.Append($"<p>{WebUtility.HtmlEncode(note)}</p>");
        html.Append(description ?? "");
        if (html.Length > MaxChars) return null;
        if (questions is not { Count: > 0 }) return html.ToString();
        html.Append("<h2>Questions</h2><ol>");
        var included = 0;
        foreach (var q in questions)
        {
            var li = $"<li>{q.Html}</li>";
            if (html.Length + li.Length + 200 > MaxChars) break;
            html.Append(li); included++;
        }
        html.Append("</ol>");
        if (included < questions.Count)
            html.Append($"<p>Only the first {included} of {questions.Count} questions fit within TeachWell's 60,000-character limit; the rest were not captured.</p>");
        return html.ToString();
    }

    // A discussion prompt with a summary line: whether it is graded (and for how many points) and published.
    public static string? Discussion(string? message, decimal? points, bool graded)
    {
        var summary = graded ? $"Graded discussion{(points is { } p ? $" · {p.ToString("0.##", CultureInfo.InvariantCulture)} points" : "")}" : "Ungraded discussion";
        var html = $"<p>{WebUtility.HtmlEncode(summary)}</p>{message ?? ""}";
        return html.Length > MaxChars ? null : html;
    }

    // Assignments submitted in an external tool: TeachWell sees only the Canvas description, so it says where the work happens.
    public static string ExternalToolNote(string? toolUrl)
    {
        var host = Uri.TryCreate(toolUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
        return host is null ? "Submitted in an external tool; TeachWell reviews only this Canvas description."
            : $"Submitted in an external tool ({host}); TeachWell reviews only this Canvas description.";
    }

    // Canvas New Quizzes run in the Quizzes LTI tool (hosts like *.quiz-lti-*.instructure.com). Their questions live in that tool and are not
    // part of course exports or the classic quiz API.
    public static bool IsNewQuiz(string? toolUrl) => Uri.TryCreate(toolUrl, UriKind.Absolute, out var uri)
        && (uri.Host.Contains("quiz-lti", StringComparison.OrdinalIgnoreCase) || uri.Host.StartsWith("quiz-api", StringComparison.OrdinalIgnoreCase));
    public const string NewQuizNote = "New Quiz: its questions are held by Canvas's New Quizzes tool and are not included in exports or the course API, so only its settings and instructions are reviewed.";
}
