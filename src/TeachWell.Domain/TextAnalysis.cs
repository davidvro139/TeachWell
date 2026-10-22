using System.Text.RegularExpressions;

namespace TeachWell.Domain;

// Deterministic text measures over a snapshot's stored plain text. In that text every block element (paragraph, heading,
// list item, table row, line break) starts a new line, so a line approximates a paragraph. All measures are estimates for
// English prose and are labelled as such wherever they are shown.
public sealed partial record TextStats(int Words, int Sentences, int Syllables, IReadOnlyList<string> Lines)
{
    public static TextStats Of(string? text)
    {
        var lines = Split(text);
        int words = 0, sentences = 0, syllables = 0;
        foreach (var line in lines)
        {
            var lineWords = WordPattern().Matches(line).Select(m => m.Value).ToList();
            if (lineWords.Count == 0) continue;
            words += lineWords.Count;
            syllables += lineWords.Sum(SyllablesIn);
            // A heading or list item without terminal punctuation still ends a sentence.
            sentences += Math.Max(1, SentenceEnd().Matches(line).Count);
        }
        return new(words, sentences, syllables, lines);
    }

    public static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static int CountWords(string? text) => string.IsNullOrEmpty(text) ? 0 : WordPattern().Count(text);

    // Flesch–Kincaid grade level; null when there is too little text to estimate.
    public decimal? Grade => Words == 0 || Sentences == 0 ? null
        : Math.Round((decimal)(0.39 * Words / Sentences + 11.8 * Syllables / Words - 15.59), 1);

    // Minutes to read at the given speed, rounded up.
    public static int ReadingMinutes(int words, decimal wordsPerMinute) => wordsPerMinute <= 0 ? 0 : (int)Math.Ceiling(words / wordsPerMinute);

    // A common English heuristic: count vowel groups after dropping a silent final "e"/"es"/"ed"; every word has at least one.
    public static int SyllablesIn(string word)
    {
        var w = word.ToLowerInvariant();
        if (!w.Any(char.IsLetter)) return 1;
        if (w.Length <= 3) return 1;
        w = SilentEnding().Replace(w, "");
        w = w.StartsWith('y') ? w[1..] : w;
        return Math.Max(1, VowelGroup().Count(w));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")] private static partial Regex WordPattern();
    [GeneratedRegex(@"[.!?]+(?=\s|$)")] private static partial Regex SentenceEnd();
    [GeneratedRegex(@"(?:[^laeiouy]es|ed|[^laeiouy]e)$")] private static partial Regex SilentEnding();
    [GeneratedRegex(@"[aeiouy]{1,2}")] private static partial Regex VowelGroup();
}

// Bloom's revised taxonomy as a reference lens (REVIEW_FRAMEWORK.md §2). TeachWell's own verb grouping; verbs appear at
// more than one level in published lists, so each is assigned its most common level here, and the result is a suggestion.
public static partial class Bloom
{
    public const string Remember = "Remember", Understand = "Understand", Apply = "Apply", Analyze = "Analyze", Evaluate = "Evaluate", Create = "Create";
    public static readonly string[] Levels = [Remember, Understand, Apply, Analyze, Evaluate, Create];
    public static bool IsLowerOrder(string level) => level is Remember or Understand;

    private static readonly Dictionary<string, string> Verbs = new(StringComparer.OrdinalIgnoreCase);
    static Bloom()
    {
        void Add(string level, params string[] verbs) { foreach (var v in verbs) Verbs[v] = level; }
        Add(Remember, "define", "list", "name", "recall", "identify", "label", "state", "match", "memorize", "repeat", "record", "locate", "recognize", "select", "cite");
        Add(Understand, "explain", "describe", "summarize", "summarise", "classify", "interpret", "paraphrase", "discuss", "illustrate", "restate", "translate", "report", "estimate");
        Add(Apply, "apply", "use", "demonstrate", "implement", "solve", "execute", "calculate", "compute", "operate", "perform", "practice", "practise", "prepare", "show", "sketch", "configure", "deploy");
        Add(Analyze, "analyze", "analyse", "compare", "contrast", "differentiate", "distinguish", "examine", "investigate", "categorize", "organize", "deconstruct", "debug", "diagnose", "test", "infer", "troubleshoot");
        Add(Evaluate, "evaluate", "assess", "critique", "judge", "justify", "defend", "argue", "appraise", "recommend", "prioritize", "validate", "rank", "verify", "reflect");
        Add(Create, "create", "design", "develop", "build", "compose", "construct", "produce", "plan", "formulate", "generate", "invent", "author", "write", "program", "devise", "propose");
    }

    // Suggested measurable replacements for a vague verb, by level.
    public static readonly IReadOnlyDictionary<string, string[]> Examples = new Dictionary<string, string[]>
    {
        [Remember] = ["identify", "list", "define"], [Understand] = ["explain", "summarize", "classify"], [Apply] = ["apply", "demonstrate", "solve"],
        [Analyze] = ["compare", "analyze", "debug"], [Evaluate] = ["evaluate", "justify", "critique"], [Create] = ["design", "build", "compose"]
    };

    // Removes a lead-in such as "Students will be able to" so the statement starts at its verb.
    public static string Statement(string outcome) => LeadIn().Replace(outcome.Trim(), "").TrimStart();

    public static string? FirstVerb(string outcome) => FirstWord().Match(Statement(outcome)) is { Success: true } m ? m.Value.ToLowerInvariant() : null;

    public static string? LevelOf(string outcome) => FirstVerb(outcome) is { } verb && Verbs.TryGetValue(verb, out var level) ? level : null;
    public static int Rank(string level) => Array.IndexOf(Levels, level);

    // The highest level among any taxonomy verbs in a task's title and instructions. Deliberately generous: a stray higher verb
    // raises the estimate, so comparisons built on it under- rather than over-report mismatches. Null when no verb is recognised.
    public static string? HighestLevelIn(string? text) => string.IsNullOrWhiteSpace(text) ? null
        : Word().Matches(text).Select(m => Verbs.GetValueOrDefault(m.Value)).OfType<string>().MaxBy(Rank);

    // The vague phrase the statement opens with, if any; phrases are matched literally and as whole words.
    public static string? VaguePhrase(string outcome, IEnumerable<string> phrases)
    {
        var statement = Statement(outcome);
        return phrases.Select(p => p.Trim()).Where(p => p.Length > 0).OrderByDescending(p => p.Length)
            .FirstOrDefault(p => statement.StartsWith(p, StringComparison.OrdinalIgnoreCase) && (statement.Length == p.Length || !char.IsLetterOrDigit(statement[p.Length])));
    }

    [GeneratedRegex(@"^(?:(?:the\s+)?(?:students?|learners?|participants?|you)\s+(?:will|should|can|must)\s+(?:be\s+able\s+to\s+)?|(?:be\s+)?able\s+to\s+|to\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex LeadIn();
    [GeneratedRegex(@"^[\p{L}]+")] private static partial Regex FirstWord();
    [GeneratedRegex(@"\p{L}+")] private static partial Regex Word();
}
