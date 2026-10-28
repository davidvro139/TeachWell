using System.Text.RegularExpressions;

namespace TeachWell.Domain;

// The review engine (ADR 011). Every automated check is a registered rule that declares its aspect, default severity and
// configurable parameters. A CheckProfile supplies per-check settings; the engine runs only enabled checks, so the same
// snapshot under the same profile always yields the same proposals.

// The five aspects the instructor improvement guide groups steps by (Phase 9).
public static class ReviewAspects
{
    public const string Navigation = "GettingStartedAndNavigation", Outcomes = "OutcomesAndAlignment", Assessment = "AssessmentAndFeedback",
        Content = "ContentAndCognitiveLoad", Accessibility = "Accessibility";
    public static readonly string[] All = [Navigation, Outcomes, Assessment, Content, Accessibility];
}

public enum CheckTarget { Element, Course }
public enum CheckParameterKind { Number, WordList }

// A configurable setting a check reads. Numbers are bounded by Min/Max; word lists by MaxItems and MaxItemLength.
public sealed record CheckParameter(string Key, string Label, CheckParameterKind Kind, decimal? DefaultNumber = null, IReadOnlyList<string>? DefaultWords = null,
    decimal? Min = null, decimal? Max = null, int MaxItems = 100, int MaxItemLength = 60)
{
    public static CheckParameter Number(string key, string label, decimal value, decimal min, decimal max) => new(key, label, CheckParameterKind.Number, value, Min: min, Max: max);
    public static CheckParameter Words(string key, string label, params string[] value) => new(key, label, CheckParameterKind.WordList, DefaultWords: value);
}

public sealed record CheckSettings(bool Enabled, string Severity, IReadOnlyDictionary<string, decimal> Numbers, IReadOnlyDictionary<string, IReadOnlyList<string>> Lists)
{
    public decimal Number(string key) => Numbers[key];
    public IReadOnlyList<string> Words(string key) => Lists[key];
}

public abstract class ReviewCheck
{
    public abstract string Code { get; }
    public abstract string Name { get; }
    public abstract string Aspect { get; }
    public abstract CheckTarget Target { get; }
    public abstract string DefaultSeverity { get; }
    public virtual IReadOnlyList<CheckParameter> Parameters => [];
    // Default "why / how to fix / example" text; standard sets carry their own editable copy.
    public virtual CheckGuidance Guidance => DefaultGuidance.For(Code);
    // The WCAG success criteria this check relates to, or empty when it relates to none (ROADMAP item 1). A fact about
    // the check, not a CTL setting, so it stays out of published standard-set versions.
    public virtual IReadOnlyList<WcagCriterion> WcagCriteria => Wcag.For(Code);
    // Findings are ordered by Stage, then Position (where in the document), then registry order, which keeps
    // proposals in reading order rather than grouped by check.
    public abstract int Stage { get; }

    public CheckSettings Defaults() => new(true, DefaultSeverity,
        Parameters.Where(p => p.Kind == CheckParameterKind.Number).ToDictionary(p => p.Key, p => p.DefaultNumber!.Value),
        Parameters.Where(p => p.Kind == CheckParameterKind.WordList).ToDictionary(p => p.Key, p => p.DefaultWords!));

    public List<string> Validate(CheckSettings s)
    {
        var problems = new List<string>();
        if (!ReviewVocabulary.Severities.Contains(s.Severity)) problems.Add($"Severity must be one of {string.Join(", ", ReviewVocabulary.Severities)}.");
        var known = Parameters.ToDictionary(p => p.Key);
        foreach (var name in s.Numbers.Keys.Concat(s.Lists.Keys).Where(n => !known.ContainsKey(n))) problems.Add($"Unknown setting \"{name}\".");
        foreach (var p in Parameters)
        {
            if (p.Kind == CheckParameterKind.Number)
            {
                if (!s.Numbers.TryGetValue(p.Key, out var value)) problems.Add($"{p.Label} is required.");
                else if (value < p.Min || value > p.Max) problems.Add($"{p.Label} must be between {p.Min} and {p.Max}.");
            }
            else if (!s.Lists.TryGetValue(p.Key, out var words)) problems.Add($"{p.Label} is required.");
            else if (words.Count > p.MaxItems) problems.Add($"{p.Label} may contain at most {p.MaxItems} entries.");
            else if (words.Any(w => string.IsNullOrWhiteSpace(w) || w.Trim().Length > p.MaxItemLength)) problems.Add($"Each {p.Label} entry must be 1–{p.MaxItemLength} characters.");
        }
        return problems;
    }

    protected CheckHit Hit(CheckSettings settings, int position, string? fingerprintSuffix, string title, string description, string recommendation, string evidence, Guid? subject = null) =>
        new(position, new(Code, fingerprintSuffix is null ? Code : $"{Code}:{fingerprintSuffix}", title, description, settings.Severity, recommendation, evidence, subject));

    protected static string Clip(string value) => value.Length <= 80 ? value : value[..77] + "...";
}

public sealed record CheckHit(int Position, ProposedFinding Finding);

public abstract class ElementCheck : ReviewCheck
{
    public override CheckTarget Target => CheckTarget.Element;
    // Checks that only need the item's publication state run even when a snapshot has no structural evidence.
    public virtual bool RequiresEvidence => true;
    // Checks over the snapshot's plain text (length, reading level, wording) need the caller to supply it.
    public virtual bool RequiresText => false;
    public abstract IEnumerable<CheckHit> Run(ElementCheckContext context, CheckSettings settings);
}

public abstract class CourseCheck : ReviewCheck
{
    public override CheckTarget Target => CheckTarget.Course;
    public abstract IEnumerable<CheckHit> Run(CourseCheckContext context, CheckSettings settings);
}

public sealed class ElementCheckContext(string elementType, string sourceStatus, ContentEvidence? evidence, string? text = null)
{
    private readonly List<string> limitations = [];
    private TextStats? stats;
    public string ElementType { get; } = elementType;
    public string SourceStatus { get; } = sourceStatus;
    public ContentEvidence Evidence => evidence ?? throw new InvalidOperationException("This check requires structural evidence.");
    public string Text => text ?? throw new InvalidOperationException("This check requires the snapshot's text.");
    public TextStats Stats => stats ??= TextStats.Of(Text);
    public IReadOnlyList<string> Limitations => limitations;
    public void Limit(string limitation) { if (!limitations.Contains(limitation)) limitations.Add(limitation); }
}

public sealed class CourseCheckContext
{
    private readonly List<string> limitations = [];
    public CourseCheckContext(CourseStructureEvidence structure, IReadOnlyList<string> startKeywords, TimeZoneInfo? timeZone = null)
    {
        Structure = structure;
        TimeZone = timeZone ?? TimeZoneInfo.Utc;
        var start = KeywordPattern(startKeywords);
        var first = structure.Modules.FirstOrDefault();
        FirstModule = first;
        HasStart = first is not null && (start.IsMatch(first.Title) || first.Items.Take(1).Any(i => start.IsMatch(i.Title)));
        // Assignments, quizzes and graded discussions: everything students submit for credit or practice.
        Assignments = structure.AllItems().Where(i => i.IsAssessed).DistinctBy(i => i.ElementId).ToList();
    }
    public CourseStructureEvidence Structure { get; }
    // The institution's time zone (Institution:TimeZone), for checks that reason about calendar days.
    public TimeZoneInfo TimeZone { get; }
    public ModuleEvidence? FirstModule { get; }
    // Whether the course opens with an orientation module; shared because naming consistency skips that module.
    public bool HasStart { get; }
    public IReadOnlyList<CourseItemEvidence> Assignments { get; }
    public IReadOnlyList<string> Limitations => limitations;
    public void Limit(string limitation) { if (!limitations.Contains(limitation)) limitations.Add(limitation); }

    // Whole-word, case-insensitive match against any keyword; keywords are escaped, never treated as patterns.
    public static Regex KeywordPattern(IEnumerable<string> keywords)
    {
        var words = keywords.Select(k => k.Trim()).Where(k => k.Length > 0).Select(Regex.Escape).ToList();
        return words.Count == 0 ? new Regex("(?!)") : new Regex($@"\b({string.Join('|', words)})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}

// Per-check settings. Checks without an entry use their defaults, so a profile only needs to describe what it changes.
public sealed class CheckProfile(IReadOnlyDictionary<string, CheckSettings>? overrides = null)
{
    public static readonly CheckProfile Default = new();
    private readonly IReadOnlyDictionary<string, CheckSettings> overrides = overrides ?? new Dictionary<string, CheckSettings>();
    public CheckSettings For(ReviewCheck check) => overrides.TryGetValue(check.Code, out var s) ? s : check.Defaults();

    // Rejects unknown checks and parameters, out-of-range numbers, oversized lists and unknown severities.
    public static Dictionary<string, string[]> Validate(IReadOnlyDictionary<string, CheckSettings> settings)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var (code, s) in settings)
        {
            var key = $"checks.{code}";
            if (CheckRegistry.Find(code) is not { } check) { errors[key] = ["Unknown check."]; continue; }
            if (check.Validate(s) is { Count: > 0 } problems) errors[key] = [.. problems];
        }
        return errors;
    }
}

public static class CheckRegistry
{
    // Registry order is the tie-breaker for findings at the same stage and position.
    public static readonly IReadOnlyList<ReviewCheck> All = [.. DeterministicChecks.Rules, .. CourseChecks.Rules];
    private static readonly Dictionary<string, ReviewCheck> ByCode = All.ToDictionary(c => c.Code);
    // The checks that existed before ADR 011 slice 3. The seeded college-wide v1 contains exactly these, so later checks reach
    // existing courses only through a published version that includes them.
    public static readonly IReadOnlyList<string> InitialCodes =
    [
        DeterministicChecks.ImageAltMissing, DeterministicChecks.ImageAltFilename, DeterministicChecks.ImageAltEmpty, DeterministicChecks.HeadingEmpty,
        DeterministicChecks.HeadingSkipped, DeterministicChecks.LinkVague, DeterministicChecks.LinkEmpty, DeterministicChecks.TableHeaders,
        DeterministicChecks.RubricMissing, DeterministicChecks.ItemUnavailable, DeterministicChecks.ItemUnpublished,
        CourseChecks.StartMissing, CourseChecks.ModuleEmpty, CourseChecks.ModuleNaming, CourseChecks.ModuleUnpublished, CourseChecks.ContentUnplaced,
        CourseChecks.DueDateMissing, CourseChecks.Unaligned, CourseChecks.CompetenciesMissing,
    ];
    public static ReviewCheck? Find(string code) => ByCode.GetValueOrDefault(code);
}

public static class ReviewEngine
{
    public static CheckReport RunElement(string elementType, string sourceStatus, ContentEvidence? evidence, CheckProfile? profile = null, string? text = null)
    {
        profile ??= CheckProfile.Default;
        var context = new ElementCheckContext(elementType, sourceStatus, evidence, text);
        var hits = new List<(CheckHit Hit, int Order)>();
        var textSkipped = false;
        void Run(bool evidenceChecks)
        {
            foreach (var (check, order) in DeterministicChecks.Rules.Select((c, i) => (c, i)).Where(x => x.c.RequiresEvidence == evidenceChecks))
            {
                if (profile.For(check) is not { Enabled: true } settings) continue;
                if (check.RequiresText && text is null) { textSkipped = true; continue; }
                hits.AddRange(check.Run(context, settings).Select(h => (h, order)));
            }
        }
        Run(evidenceChecks: false);
        if (evidence is null)
            context.Limit("This snapshot has no structural evidence (it predates evidence capture or no HTML was returned). Image, heading, link, table and rubric checks were not run. Re-import the course to capture evidence.");
        else
        {
            if (evidence.Truncated) context.Limit("Structural evidence was truncated at the capture limit; items beyond it were not checked.");
            Run(evidenceChecks: true);
        }
        if (textSkipped) context.Limit("The snapshot's text was not available to the checks, so length, reading-level and wording checks were not run.");
        return new(Order(hits, DeterministicChecks.Rules), context.Limitations);
    }

    public static CheckReport RunCourse(CourseStructureEvidence? structure, CheckProfile? profile = null, TimeZoneInfo? timeZone = null)
    {
        profile ??= CheckProfile.Default;
        if (structure is null) return new([], ["This snapshot has no course-structure evidence, so no course-level checks were run."]);
        var context = new CourseCheckContext(structure, profile.For(CourseChecks.Start).Words(CourseChecks.StartKeywords), timeZone);
        context.Limit("Module order uses positions captured at import. Quizzes, discussions, files and external tools are not inspected.");
        if (structure.Truncated) context.Limit("The course structure was truncated at the capture limit; items beyond it were not checked.");
        var hits = new List<(CheckHit Hit, int Order)>();
        foreach (var (check, order) in CourseChecks.Rules.Select((c, i) => (c, i)))
            if (profile.For(check) is { Enabled: true } settings) hits.AddRange(check.Run(context, settings).Select(h => (h, order)));
        return new(Order(hits, CourseChecks.Rules), context.Limitations);
    }

    private static List<ProposedFinding> Order<T>(List<(CheckHit Hit, int Order)> hits, IReadOnlyList<T> rules) where T : ReviewCheck =>
        hits.OrderBy(h => rules[h.Order].Stage).ThenBy(h => h.Hit.Position).ThenBy(h => h.Order).Select(h => h.Hit.Finding).ToList();
}
