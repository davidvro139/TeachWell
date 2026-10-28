namespace TeachWell.Domain;

// Which WCAG success criterion each accessibility check relates to (ROADMAP item 1).
//
// The reference is WCAG 2.1 Level AA, because that is the standard the ADA Title II rule names for public institutions.
// Every criterion referenced here is worded identically in WCAG 2.2, so the mapping holds for either version.
//
// A criterion is a fact about a check, not a CTL setting: it is not part of StandardCheckSetting and never enters a
// published version's ChecksJson. The CTL can disable a check or rewrite its guidance; it cannot reassign what the
// check relates to.
//
// Deliberately incomplete. Only checks whose aspect is Accessibility carry criteria. TeachWell's course-design checks
// (workload, pacing, alignment, module structure) relate to no success criterion, and claiming otherwise would
// overstate what a conformance report based on them can show. A check with no criterion is reported as out of scope,
// not as passing.
public sealed record WcagCriterion(string Number, string Name, string Level)
{
    public string Reference => $"WCAG 2.1 {Number} {Name} (Level {Level})";
}

public static class Wcag
{
    public const string Version = "WCAG 2.1";

    // Only the criteria TeachWell's own checks relate to. Criteria reachable only through imported third-party
    // findings (contrast, captions, document language) are added when that import is built, not before.
    public static readonly WcagCriterion NonTextContent = new("1.1.1", "Non-text Content", "A");
    public static readonly WcagCriterion Captions = new("1.2.2", "Captions (Prerecorded)", "A");
    public static readonly WcagCriterion InfoAndRelationships = new("1.3.1", "Info and Relationships", "A");
    public static readonly WcagCriterion UseOfColor = new("1.4.1", "Use of Color", "A");
    public static readonly WcagCriterion LinkPurpose = new("2.4.4", "Link Purpose (In Context)", "A");
    public static readonly WcagCriterion HeadingsAndLabels = new("2.4.6", "Headings and Labels", "AA");

    // In criterion order, which is the order a conformance report lists them in.
    public static readonly IReadOnlyList<WcagCriterion> Criteria = [NonTextContent, Captions, InfoAndRelationships, UseOfColor, LinkPurpose, HeadingsAndLabels];

    private static readonly Dictionary<string, WcagCriterion> ByNumber = Criteria.ToDictionary(c => c.Number);
    public static WcagCriterion? Find(string number) => ByNumber.GetValueOrDefault(number);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<WcagCriterion>> Map = new Dictionary<string, IReadOnlyList<WcagCriterion>>
    {
        // A missing, file-name or wrongly empty text alternative is the same failure of 1.1.1 seen three ways.
        [DeterministicChecks.ImageAltMissing] = [NonTextContent],
        [DeterministicChecks.ImageAltFilename] = [NonTextContent],
        [DeterministicChecks.ImageAltEmpty] = [NonTextContent],
        // A player with no captions track fails 1.2.2 on this page; an embed whose captions cannot be read from the
        // page relates to the same criterion, which is why the instructor is asked to confirm it rather than told.
        [MediaChecks.CaptionsMissing] = [Captions],
        [MediaChecks.CaptionsUnconfirmed] = [Captions],
        // An empty heading breaks the structure 1.3.1 requires and is also an undescriptive heading under 2.4.6.
        [DeterministicChecks.HeadingEmpty] = [InfoAndRelationships, HeadingsAndLabels],
        [DeterministicChecks.HeadingSkipped] = [InfoAndRelationships],
        [DeterministicChecks.LinkEmpty] = [LinkPurpose],
        [DeterministicChecks.LinkVague] = [LinkPurpose],
        [DeterministicChecks.TableHeaders] = [InfoAndRelationships],
        // A list typed as plain lines is not conveyed as a list, so its structure is lost to assistive technology.
        [ContentChecks.ListAsText] = [InfoAndRelationships],
        [ContentChecks.ColorOnly] = [UseOfColor],
    };

    // The criteria a check relates to, or an empty list when it relates to none. Unlike DefaultGuidance.For, this does
    // not throw: most checks legitimately have no criterion. CheckCatalogueIsComplete guards the ones that must.
    public static IReadOnlyList<WcagCriterion> For(string code) => Map.TryGetValue(code, out var criteria) ? criteria : [];

    // The checks that relate to a criterion, for the conformance report's per-criterion rows.
    public static IReadOnlyList<string> Checks(WcagCriterion criterion) =>
        [.. Map.Where(e => e.Value.Contains(criterion)).Select(e => e.Key).OrderBy(c => c, StringComparer.Ordinal)];
}
