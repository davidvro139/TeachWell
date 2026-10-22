using System.Text.Json;

namespace TeachWell.Domain;

// Standard sets (ADR 011, Phase 8): the CTL's versioned, publishable configuration of every automated check, with the guidance
// shown to instructors. Published versions are immutable, so a result can always be traced to the exact settings that produced it.
public static class StandardSetVocabulary
{
    public const string Online = "Online", Hybrid = "Hybrid", FaceToFace = "FaceToFace";
    public static readonly string[] DeliveryModes = [Online, Hybrid, FaceToFace];
    public const int MaxNameLength = 160, MaxDepartmentLength = 120, MaxNotesLength = 2000;
    public const int MaxWhyLength = 2000, MaxHowLength = 4000, MaxExampleLength = 2000;

    // Normalized for comparison only; the stored department keeps the CTL's capitalization.
    public static string? NormalizeDepartment(string? department) => string.IsNullOrWhiteSpace(department) ? null : department.Trim();
    public static string ScopeKey(string? deliveryMode, string? department) =>
        $"{deliveryMode ?? "*"}|{NormalizeDepartment(department)?.ToUpperInvariant() ?? "*"}";
    public static string DescribeScope(string? deliveryMode, string? department) => (deliveryMode, NormalizeDepartment(department)) switch
    {
        (null, null) => "College-wide",
        (null, { } d) => $"Department: {d}",
        ({ } m, null) => $"Delivery mode: {ModeLabel(m)}",
        ({ } m, { } d) => $"{d}, {ModeLabel(m)}"
    };
    public static string ModeLabel(string mode) => mode == FaceToFace ? "Face-to-face" : mode;
}

public class StandardSet
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    // Null means "any": a set with neither is college-wide. Scope is fixed at creation.
    public string? DeliveryMode { get; set; }
    public string? Department { get; set; }
    // Unique per tenant, so at most one set claims each scope and selection is never ambiguous.
    public string ScopeKey { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<StandardSetVersion> Versions { get; set; } = [];
}

public class StandardSetVersion
{
    public Guid Id { get; set; }
    public Guid SetId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = ReviewVocabulary.Draft;
    public string Notes { get; set; } = "";
    // Every check's settings and guidance, stored whole so a published version never depends on later code defaults.
    public string ChecksJson { get; set; } = "[]";
    // Which reviews an endorsement under this version needs (ADR 013 amendment). Empty means the baseline rule only.
    public string EndorsementJson { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed record CheckGuidance(string Why, string HowToFix, string Example);

// The reviews a CTL reviewer must publish (after the readiness request) before endorsing a course under this version. On top of these,
// the deciding reviewer must always have published at least one review of the course since the request.
public sealed record EndorsementRequirements(bool CourseReview, Guid? RubricProfileId, int MinimumItemReviews)
{
    public const int MaxItemReviews = 20;
    public static readonly EndorsementRequirements Baseline = new(false, null, 0);
    public string Serialize() => JsonSerializer.Serialize(this, ContentEvidence.Json);
    public static EndorsementRequirements Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Baseline;
        try { return JsonSerializer.Deserialize<EndorsementRequirements>(json, ContentEvidence.Json) ?? Baseline; }
        catch (JsonException) { return Baseline; }
    }
}

public sealed record StandardCheckSetting(string Code, bool Enabled, string Severity, bool Required,
    IReadOnlyDictionary<string, decimal> Numbers, IReadOnlyDictionary<string, IReadOnlyList<string>> Lists, CheckGuidance Guidance)
{
    public CheckSettings ToSettings() => new(Enabled, Severity, Numbers, Lists);
}

public static class StandardSetContent
{
    // The registry's defaults: every check enabled at its default severity, with TeachWell's default guidance.
    public static List<StandardCheckSetting> Defaults() => CheckRegistry.All.Select(Default).ToList();
    // Only the checks that existed before ADR 011 slice 3, at their defaults (the seeded college-wide v1).
    public static List<StandardCheckSetting> InitialDefaults() => Defaults().Where(c => CheckRegistry.InitialCodes.Contains(c.Code)).ToList();
    public static StandardCheckSetting Default(ReviewCheck c)
    {
        var d = c.Defaults();
        return new(c.Code, true, d.Severity, false, d.Numbers, d.Lists, c.Guidance);
    }

    public static string Serialize(IEnumerable<StandardCheckSetting> checks) => JsonSerializer.Serialize(checks, ContentEvidence.Json);
    public static List<StandardCheckSetting> Parse(string json) => JsonSerializer.Deserialize<List<StandardCheckSetting>>(json, ContentEvidence.Json) ?? [];

    // A check the version does not mention (one added to TeachWell after it was published) does not run under it,
    // so publishing is the only way a new check reaches instructors.
    public static CheckProfile ToProfile(IEnumerable<StandardCheckSetting> checks)
    {
        var settings = checks.ToDictionary(c => c.Code, c => c.ToSettings());
        foreach (var check in CheckRegistry.All.Where(c => !settings.ContainsKey(c.Code)))
            settings[check.Code] = check.Defaults() with { Enabled = false };
        return new(settings);
    }

    // A new draft copies its predecessor and gains any checks added since, with their defaults.
    public static List<StandardCheckSetting> WithNewChecks(IEnumerable<StandardCheckSetting> checks)
    {
        var list = checks.ToList();
        var known = list.Select(c => c.Code).ToHashSet();
        list.AddRange(CheckRegistry.All.Where(c => !known.Contains(c.Code)).Select(Default));
        return list;
    }

    public static Dictionary<string, string[]> Validate(IReadOnlyList<StandardCheckSetting>? checks)
    {
        var errors = new Dictionary<string, string[]>();
        if (checks is null) { errors["checks"] = ["Provide the settings for every check."]; return errors; }
        var codes = checks.Select(c => c?.Code).ToList();
        if (codes.Any(c => c is null) || codes.Distinct().Count() != codes.Count) errors["checks"] = ["List each check exactly once."];
        var missing = CheckRegistry.All.Select(c => c.Code).Except(codes.OfType<string>()).ToList();
        if (missing.Count > 0) errors["checks"] = [$"Settings are missing for: {string.Join(", ", missing)}."];
        foreach (var (code, problems) in CheckProfile.Validate(checks.Where(c => c?.Code is not null).DistinctBy(c => c.Code)
            .ToDictionary(c => c.Code, c => new CheckSettings(c.Enabled, c.Severity, c.Numbers ?? new Dictionary<string, decimal>(), c.Lists ?? new Dictionary<string, IReadOnlyList<string>>()))))
            errors[code] = problems;
        foreach (var c in checks.Where(c => c?.Code is not null))
        {
            var key = $"checks.{c.Code}.guidance";
            var g = c.Guidance;
            var problems = new List<string>();
            if (g is null) problems.Add("Guidance is required.");
            else
            {
                if (string.IsNullOrWhiteSpace(g.Why) || g.Why.Length > StandardSetVocabulary.MaxWhyLength) problems.Add($"\"Why it matters\" must be 1–{StandardSetVocabulary.MaxWhyLength:N0} characters.");
                if (string.IsNullOrWhiteSpace(g.HowToFix) || g.HowToFix.Length > StandardSetVocabulary.MaxHowLength) problems.Add($"\"How to fix it\" must be 1–{StandardSetVocabulary.MaxHowLength:N0} characters.");
                if ((g.Example?.Length ?? 0) > StandardSetVocabulary.MaxExampleLength) problems.Add($"\"Example\" is limited to {StandardSetVocabulary.MaxExampleLength:N0} characters.");
            }
            if (c.Required && !c.Enabled) problems.Add("A check required for readiness must be enabled.");
            if (problems.Count > 0) errors[key] = [.. problems];
        }
        return errors;
    }
}

// D1: the most specific published set applies — department + mode, then department, then mode, then college-wide.
public static class StandardSetSelection
{
    public static int? Specificity(string? setMode, string? setDepartment, string? courseMode, string? courseDepartment)
    {
        var dept = StandardSetVocabulary.NormalizeDepartment(setDepartment);
        var courseDept = StandardSetVocabulary.NormalizeDepartment(courseDepartment);
        if (setMode is not null && setMode != courseMode) return null;
        if (dept is not null && !string.Equals(dept, courseDept, StringComparison.OrdinalIgnoreCase)) return null;
        return (dept is not null ? 2 : 0) + (setMode is not null ? 1 : 0);
    }

    public static T? Pick<T>(IEnumerable<T> candidates, Func<T, (string? Mode, string? Department)> scope, string? courseMode, string? courseDepartment) where T : class =>
        candidates.Select(c => (Candidate: c, Rank: Specificity(scope(c).Mode, scope(c).Department, courseMode, courseDepartment)))
            .Where(x => x.Rank is not null).OrderByDescending(x => x.Rank).Select(x => x.Candidate).FirstOrDefault();
}
