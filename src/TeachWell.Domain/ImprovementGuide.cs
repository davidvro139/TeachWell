namespace TeachWell.Domain;

// The instructor improvement guide (ADR 011 slices 4–5). Pure functions: the same issues, progress and weights always give the
// same ranked steps, so the guide is explainable and testable. Nothing here produces a score or a pass/fail.

// What one finding means for the guide: where it is, and whether the instructor still has it open.
public sealed record GuideIssue(string Key, ProposedFinding Finding, Guid? ElementId, int? ModuleIndex, bool Open);

// The same check across many places becomes one step.
public sealed record GuideStep(string CheckCode, string Aspect, string Severity, bool Required, IReadOnlyList<GuideIssue> Issues,
    int MinutesEach, int OpenMinutes, double Priority, bool QuickWin)
{
    public int OpenCount => Issues.Count(i => i.Open);
}

// How much of each kind of thing the course has, so a strength is only claimed where there was something to check.
public sealed record GuideCoverage(int Pages, int Assessments, int Images, int Links, int Headings, int Tables, int Modules, int Outcomes, int Items);

// Priority = severity × reach × position × ease. Defaults are TeachWell's; they are deliberately simple so the order can be explained.
public sealed record GuideWeights(double Low = 1, double Medium = 2, double High = 3, double Critical = 4,
    double EarlyModuleBonus = 0.5, int QuickWinMinutes = 15)
{
    public static readonly GuideWeights Default = new();
    public double Severity(string severity) => severity switch { "Critical" => Critical, "High" => High, "Medium" => Medium, _ => Low };
}

public static class ImprovementGuide
{
    public static IReadOnlyList<GuideStep> Build(IEnumerable<GuideIssue> issues, Func<string, bool> required, GuideWeights? weights = null)
    {
        weights ??= GuideWeights.Default;
        var order = CheckRegistry.All.Select((c, i) => (c.Code, i)).ToDictionary(x => x.Code, x => x.i);
        var steps = issues.GroupBy(i => i.Finding.CheckCode).Select(g =>
        {
            var check = CheckRegistry.Find(g.Key);
            var list = g.ToList();
            var open = list.Where(i => i.Open).ToList();
            // A step's severity is its most severe place (a standard set can change a check's severity between versions).
            var severity = list.Select(i => i.Finding.Severity).MaxBy(s => weights.Severity(s))!;
            var each = GuideCatalog.MinutesFor(g.Key);
            var minutes = each * open.Count;
            double priority = 0;
            if (open.Count > 0)
            {
                var reach = 1 + Math.Log2(open.Count);
                // Problems students meet first matter more: the first module gets the full bonus, later ones less.
                var position = open.Min(i => i.ModuleIndex) is { } first ? 1 + weights.EarlyModuleBonus / (1 + first) : 1;
                var ease = minutes <= weights.QuickWinMinutes ? 1.5 : minutes <= 60 ? 1.0 : 0.7;
                priority = Math.Round(weights.Severity(severity) * reach * position * ease, 4);
            }
            return new GuideStep(g.Key, check?.Aspect ?? ReviewAspects.Content, severity, required(g.Key), list, each, minutes, priority,
                open.Count > 0 && minutes <= weights.QuickWinMinutes && weights.Severity(severity) >= weights.Medium);
        });
        // Required by the college's standards first, then priority; ties break on registry order, so the order is stable.
        return steps.OrderByDescending(s => s.OpenCount > 0).ThenByDescending(s => s.Required && s.OpenCount > 0).ThenByDescending(s => s.Priority)
            .ThenBy(s => order.GetValueOrDefault(s.CheckCode, int.MaxValue)).ThenBy(s => s.CheckCode, StringComparer.Ordinal).ToList();
    }

    // Enabled checks that found nothing, where the course has something for them to look at. Described, never scored.
    public static IReadOnlyList<(string CheckCode, string Aspect, string Statement)> Strengths(IEnumerable<string> enabled, IEnumerable<string> found, GuideCoverage coverage)
    {
        var hit = found.ToHashSet();
        return enabled.Where(code => !hit.Contains(code)).Select(code => (code, GuideCatalog.Find(code)))
            .Where(x => x.Item2 is { Strength: not null } e && e.Applies(coverage) && CheckRegistry.Find(x.code) is not null)
            .Select(x => (x.code, CheckRegistry.Find(x.code)!.Aspect, x.Item2!.Strength!)).ToList();
    }
}

// Per-check facts the guide needs beyond the engine: a rough time per place, and how to say "this is already working".
public sealed record GuideCatalogEntry(int Minutes, string? Strength, Func<GuideCoverage, bool> Applies);

public static class GuideCatalog
{
    public static int MinutesFor(string code) => Find(code)?.Minutes ?? 10;
    public static GuideCatalogEntry? Find(string code) => Entries.GetValueOrDefault(code);

    private static GuideCatalogEntry E(int minutes, string? strength, Func<GuideCoverage, bool> applies) => new(minutes, strength, applies);

    public static readonly IReadOnlyDictionary<string, GuideCatalogEntry> Entries = new Dictionary<string, GuideCatalogEntry>
    {
        [DeterministicChecks.ItemUnavailable] = E(10, "Every linked item is available.", c => c.Items > 0),
        [DeterministicChecks.ItemUnpublished] = E(2, "All course content is published.", c => c.Items > 0),
        [DeterministicChecks.ImageAltMissing] = E(3, "Every image has a text alternative or is marked decorative.", c => c.Images > 0),
        [DeterministicChecks.ImageAltFilename] = E(3, "No image uses a file name as its description.", c => c.Images > 0),
        [DeterministicChecks.ImageAltEmpty] = E(2, null, _ => false),
        [DeterministicChecks.HeadingEmpty] = E(2, "Every heading has text.", c => c.Headings > 0),
        [DeterministicChecks.HeadingSkipped] = E(3, "Heading levels follow in order.", c => c.Headings > 0),
        [DeterministicChecks.LinkVague] = E(2, "Link text says where each link goes.", c => c.Links > 0),
        [DeterministicChecks.LinkEmpty] = E(2, "Every link has readable text.", c => c.Links > 0),
        [DeterministicChecks.TableHeaders] = E(5, "Every table has header cells.", c => c.Tables > 0),
        [DeterministicChecks.RubricMissing] = E(30, "Every assignment has a rubric.", c => c.Assessments > 0),
        [CourseChecks.StartMissing] = E(45, "The course opens with a clear starting point.", c => c.Modules > 0),
        [CourseChecks.ModuleEmpty] = E(5, "No module is empty.", c => c.Modules > 0),
        [CourseChecks.ModuleNaming] = E(2, "Modules follow one naming pattern.", c => c.Modules >= 3),
        [CourseChecks.ModuleUnpublished] = E(2, "All modules are published.", c => c.Modules > 0),
        [CourseChecks.ContentUnplaced] = E(3, "All content is placed in a module.", c => c.Modules > 0),
        [CourseChecks.DueDateMissing] = E(2, "Every assessment has a due date.", c => c.Assessments > 0),
        [CourseChecks.Unaligned] = E(2, "Every assessment is mapped to a competency.", c => c.Assessments > 0 && c.Outcomes > 0),
        [CourseChecks.CompetenciesMissing] = E(20, "The course's competencies are recorded.", c => c.Outcomes > 0),
        [ContentChecks.PageTooLong] = E(30, "Pages are a manageable length.", c => c.Pages > 0),
        [ContentChecks.LongNoHeadings] = E(10, "Long pages are broken up with headings.", c => c.Pages > 0),
        [ContentChecks.ParagraphTooLong] = E(5, "Paragraphs are a readable length.", c => c.Pages > 0),
        [ContentChecks.ReadingLevel] = E(20, "Text is written at an approachable reading level.", c => c.Pages > 0),
        [ContentChecks.PageNearlyEmpty] = E(10, "No published page is empty.", c => c.Pages > 0),
        [ContentChecks.InstructionsShort] = E(15, "Assignment instructions are detailed.", c => c.Assessments > 0),
        [ContentChecks.PointsMissing] = E(2, "Every graded assessment has points.", c => c.Assessments > 0),
        [ContentChecks.ListAsText] = E(3, "Lists use real list formatting.", c => c.Pages > 0),
        [ContentChecks.ColorOnly] = E(5, "Instructions do not rely on colour alone.", c => c.Pages > 0),
        [CourseDesignChecks.ModuleNoOverview] = E(15, "Every module opens with an overview.", c => c.Modules > 0),
        [CourseDesignChecks.ModuleTooManyItems] = E(20, "Modules are a manageable size.", c => c.Modules > 0),
        [CourseDesignChecks.ModuleNoAssessment] = E(20, "Every module includes an assessment.", c => c.Modules > 0 && c.Assessments > 0),
        [CourseDesignChecks.WorkloadImbalance] = E(45, "Workload is spread evenly across modules.", c => c.Modules >= 3),
        [CourseDesignChecks.DueSameDay] = E(5, "Due dates are spread out.", c => c.Assessments >= 2),
        [CourseDesignChecks.OutcomeVagueVerb] = E(5, "Outcomes use measurable verbs.", c => c.Outcomes > 0),
        [CourseDesignChecks.OutcomeNotAssessed] = E(10, "Every outcome is assessed.", c => c.Outcomes > 0),
        [CourseDesignChecks.OutcomesLowerOrder] = E(20, "Outcomes reach beyond recall and comprehension.", c => c.Outcomes > 0),
        [CourseDesignChecks.EarlyPracticeMissing] = E(30, "Students get low-stakes practice early in the course.", c => c.Assessments > 0),
        [CourseDesignChecks.OutcomeAssessmentMismatch] = E(20, "Assessments reach the level their outcomes describe.", c => c.Outcomes > 0 && c.Assessments > 0),
    };

    // Plain-language introductions to the five areas, shown in the guide's overview. TeachWell's own words.
    public static readonly IReadOnlyDictionary<string, (string Label, string Why)> Aspects = new Dictionary<string, (string, string)>
    {
        [ReviewAspects.Navigation] = ("Getting started and navigation",
            "Students decide in the first few minutes whether a course feels manageable. A clear starting point, predictable module names and content that is published and reachable let them spend their effort on learning instead of hunting for things."),
        [ReviewAspects.Outcomes] = ("Outcomes and alignment",
            "Outcomes tell students what they will be able to do. When every assessment is mapped to an outcome, and outcomes use observable verbs, students can see why each task matters and you can show the course teaches what it promises."),
        [ReviewAspects.Assessment] = ("Assessment and feedback",
            "Clear instructions, due dates, points and rubrics reduce anxiety and grading disputes. Early low-stakes practice and spread-out deadlines give students feedback before the stakes rise."),
        [ReviewAspects.Content] = ("Content and cognitive load",
            "Long unbroken pages, dense paragraphs and uneven weekly workload make material harder to take in. Headings, shorter chunks and an even pace help every student, especially those reading in a second language or on a phone."),
        [ReviewAspects.Accessibility] = ("Accessibility",
            "Students using screen readers, keyboards or magnification rely on alt text, real headings, descriptive links and table headers. Fixing these is usually quick and also makes the course easier for everyone to scan."),
    };
}

// The instructor's progress on one place in the guide, keyed by the guide's location key so it carries across re-imports while the
// same issue is still detected. A place no longer detected is resolved; its mark is kept only as history.
public class GuideProgress
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CourseId { get; set; }
    public string Key { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid SetBy { get; set; }
    public DateTime SetAt { get; set; } = DateTime.UtcNow;
}

public static class GuideVocabulary
{
    public const string Open = "Open", Done = "Done", NotApplicable = "NotApplicable";
    public const int MaxKeyLength = 300, MaxReasonLength = 500, MaxKeysPerChange = 1000;
    // A place is "{elementId}:{fingerprint}" or "course:{fingerprint}", and a fingerprint starts with its check code
    // ("CODE" or "CODE:suffix"), so the check is always the second segment.
    public static string ElementKey(Guid elementId, string fingerprint) => $"{elementId}:{fingerprint}";
    public static string CourseKey(string fingerprint) => $"course:{fingerprint}";
    public static string CheckCodeOf(string key) => key.Split(':') is { Length: >= 2 } parts ? parts[1] : "";
}

// A CTL resource (a knowledge-base article or a workshop) offered on every guide step for one check. Exactly one of ArticleId and
// WorkshopId is set. Links are tenant settings, not part of a standard-set version, because the resources themselves are editable.
public class CheckResourceLink
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string CheckCode { get; set; } = "";
    public Guid? ArticleId { get; set; }
    public Guid? WorkshopId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public const int MaxPerCheck = 5;
}
