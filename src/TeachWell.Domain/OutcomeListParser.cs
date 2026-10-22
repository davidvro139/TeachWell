using System.Text.RegularExpressions;

namespace TeachWell.Domain;

// Turns a pasted list of learning outcomes (from a syllabus tool such as Simple Syllabus, a Word document or an email) into proposed
// competencies. Deterministic and conservative: anything it is unsure about is left for the person to edit before saving.
public static partial class OutcomeListParser
{
    public const int MaxChars = 20000, MaxOutcomes = 100;

    public sealed record ParsedOutcome(string? Code, string Title);
    public sealed record Result(IReadOnlyList<ParsedOutcome> Outcomes, IReadOnlyList<string> Ignored);

    public static Result Parse(string? text)
    {
        var outcomes = new List<(string? Code, string Title)>();
        var ignored = new List<string>();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = Collapse(raw);
            if (line.Length == 0) continue;
            // Section headings ("Student Learning Outcomes") and lead-ins ("Upon completion, students will be able to:") are not outcomes.
            if (Heading().IsMatch(line) || line.EndsWith(':') && line.Split(' ').Length <= 18) { ignored.Add(line); continue; }
            var marker = Marker().Match(line);
            var body = marker.Success ? line[marker.Length..].Trim() : line;
            if (body.Length == 0) continue;
            // A wrapped line (no bullet, starts in lower case) continues the previous outcome.
            if (!marker.Success && char.IsLower(body[0]) && outcomes.Count > 0)
            {
                var last = outcomes[^1];
                outcomes[^1] = (last.Code, $"{last.Title} {body}");
                continue;
            }
            string? code = null;
            if (CodePrefix().Match(body) is { Success: true } c)
            {
                code = NormalizeCode(c.Groups[1].Value);
                body = body[c.Length..].Trim();
            }
            if (body.Length == 0) continue;
            if (outcomes.Any(o => string.Equals(o.Title, body, StringComparison.OrdinalIgnoreCase))) { ignored.Add(line + " (repeated)"); continue; }
            outcomes.Add((code, body));
        }
        return new(outcomes.Take(MaxOutcomes).Select(o => new ParsedOutcome(o.Code, o.Title)).ToList(), ignored);
    }

    // "SLO 1", "clo-2", "LO1.3" → "SLO-1", "CLO-2", "LO1-3"; only characters a competency code allows remain.
    public static string NormalizeCode(string code) =>
        Regex.Replace(Regex.Replace(code.Trim().ToUpperInvariant(), @"[\s._]+", "-"), "[^A-Z0-9-]", "").Trim('-') is { Length: > 0 and <= 20 } v ? v : "";

    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex(@"^(?:(?:student|course|program|general\s+education)\s+)?(?:learning\s+)?(?:outcomes?|objectives?|competenc(?:y|ies)|goals?)\s*:?$|^(?:slos?|clos?|plos?)\s*:?$", RegexOptions.IgnoreCase)]
    private static partial Regex Heading();
    // Bullets (•, -, *, ▪ …) and list numbering ("1.", "2)", "(3)", "a.", "b)") at the start of a line.
    [GeneratedRegex(@"^(?:[•‣⁃∙▪▫●○◦■□➢➤►\-–—*·>]+|\(?\d{1,3}[.)]|\([a-zA-Z]\)|[a-zA-Z][.)](?=\s))\s*")]
    private static partial Regex Marker();
    // An outcome code such as "SLO 1:", "CLO-2 -", "LO1.3." (letters, then a number) followed by a separator.
    [GeneratedRegex(@"^([A-Za-z]{1,8}[\s_-]?\d{1,3}(?:\.\d{1,3})?)\s*[:.)–—-]\s+")]
    private static partial Regex CodePrefix();
}
