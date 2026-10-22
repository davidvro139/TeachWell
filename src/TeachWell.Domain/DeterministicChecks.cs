using System.Text.RegularExpressions;

namespace TeachWell.Domain;

public sealed record ProposedFinding(string CheckCode, string Fingerprint, string Title, string Description, string Severity, string Recommendation, string Evidence,
    Guid? SubjectElementId = null);
public sealed record CheckReport(IReadOnlyList<ProposedFinding> Findings, IReadOnlyList<string> Limitations);

// Page and assignment checks: pure functions over an immutable snapshot, run by ReviewEngine under a CheckProfile.
public static partial class DeterministicChecks
{
    public const string ImageAltMissing = "IMG_ALT_MISSING", ImageAltFilename = "IMG_ALT_FILENAME", ImageAltEmpty = "IMG_ALT_EMPTY",
        HeadingEmpty = "HEADING_EMPTY", HeadingSkipped = "HEADING_SKIPPED_LEVEL", LinkVague = "LINK_VAGUE", LinkEmpty = "LINK_EMPTY",
        TableHeaders = "TABLE_HEADERS_MISSING", RubricMissing = "ASSIGNMENT_RUBRIC_MISSING", ItemUnavailable = "ITEM_UNAVAILABLE", ItemUnpublished = "ITEM_UNPUBLISHED";
    public const string VaguePhrases = "phrases";

    public static readonly IReadOnlyList<ElementCheck> Rules =
        [new Unavailable(), new Unpublished(), new AltMissing(), new AltFilename(), new AltEmpty(), new EmptyHeading(), new SkippedHeading(),
         new EmptyLink(), new VagueLink(), new MissingTableHeaders(), new MissingRubric(), .. ContentChecks.Rules];
    // Every check code a rubric criterion may reference, element and course.
    public static readonly string[] All = [.. Rules.Select(r => r.Code), .. CourseChecks.All];

    public static CheckReport Run(string elementType, string sourceStatus, ContentEvidence? evidence, CheckProfile? profile = null, string? text = null) =>
        ReviewEngine.RunElement(elementType, sourceStatus, evidence, profile, text);

    private const int Availability = 0, Images = 1, Headings = 2, Links = 3, Tables = 4, AssignmentMetadata = 5;
    private const string UnknownStatus = "Canvas did not report a publication state, so availability was not checked.";

    private sealed class Unavailable : ElementCheck
    {
        public override string Code => ItemUnavailable; public override string Name => "Content is unavailable"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "High"; public override int Stage => Availability; public override bool RequiresEvidence => false;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            if (c.SourceStatus == "Unknown") c.Limit(UnknownStatus);
            if (c.SourceStatus is "Unavailable" or "Removed")
                yield return Hit(s, 0, null, "Content is unavailable", $"At capture time this {c.ElementType} had status {c.SourceStatus}.",
                    "Confirm the item exists in Canvas and that students can reach it, or remove references to it.", $"Snapshot source status: {c.SourceStatus}.");
        }
    }
    private sealed class Unpublished : ElementCheck
    {
        public override string Code => ItemUnpublished; public override string Name => "Content is unpublished"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Low"; public override int Stage => Availability; public override bool RequiresEvidence => false;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            if (c.SourceStatus == "Unknown") c.Limit(UnknownStatus);
            if (c.SourceStatus == "Unpublished")
                yield return Hit(s, 0, null, "Content is unpublished", $"At capture time this {c.ElementType} was unpublished in Canvas.",
                    "Confirm whether students should see this item; publish it when it is ready.", "Snapshot source status: Unpublished.");
        }
    }

    private sealed class AltMissing : ElementCheck
    {
        public override string Code => ImageAltMissing; public override string Name => "Image has no text alternative"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "High"; public override int Stage => Images;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s) =>
            c.Evidence.Images.Where(i => !i.Decorative && i.Alt is null).Select(i => Hit(s, i.Index, $"{i.Index}", "Image has no text alternative",
                $"Image {i.Index + 1} has no alt attribute, so screen-reader users receive no description.",
                "Add alt text that conveys the image's purpose, or mark the image as decorative if it adds no information.", $"Image {i.Index + 1}: alt attribute absent."));
    }
    private sealed class AltEmpty : ElementCheck
    {
        public override string Code => ImageAltEmpty; public override string Name => "Image is marked as decorative"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Low"; public override int Stage => Images;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s) =>
            c.Evidence.Images.Where(i => !i.Decorative && i.Alt is { } alt && alt.Trim().Length == 0).Select(i => Hit(s, i.Index, $"{i.Index}", "Image is marked as decorative",
                $"Image {i.Index + 1} has empty alt text, which tells assistive technology to skip it.",
                "Confirm the image is purely decorative. If it carries information, add descriptive alt text.", $"Image {i.Index + 1}: alt=\"\"."));
    }
    private sealed class AltFilename : ElementCheck
    {
        public override string Code => ImageAltFilename; public override string Name => "Image alt text is a file name"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Medium"; public override int Stage => Images;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s) =>
            c.Evidence.Images.Where(i => !i.Decorative && i.Alt is { } alt && alt.Trim().Length > 0 && FileNamePattern().IsMatch(alt.Trim())).Select(i => Hit(s, i.Index, $"{i.Index}",
                "Image alt text is a file name", $"Image {i.Index + 1}'s alt text looks like a file name rather than a description.",
                "Replace the file name with a short description of what the image shows and why it matters.", $"Image {i.Index + 1}: alt=\"{Clip(i.Alt!)}\"."));
    }

    private sealed class EmptyHeading : ElementCheck
    {
        public override string Code => HeadingEmpty; public override string Name => "Heading is empty"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Medium"; public override int Stage => Headings;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s) =>
            c.Evidence.Headings.Where(h => h.Text.Trim().Length == 0).Select(h => Hit(s, h.Index, $"{h.Index}", "Heading is empty",
                $"Heading {h.Index + 1} (level {h.Level}) contains no text.", "Remove the empty heading or add text; do not use headings for spacing.",
                $"Heading {h.Index + 1}: <h{h.Level}> with no text."));
    }
    private sealed class SkippedHeading : ElementCheck
    {
        public override string Code => HeadingSkipped; public override string Name => "Heading level is skipped"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Low"; public override int Stage => Headings;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var previous = 0;
            foreach (var h in c.Evidence.Headings)
            {
                if (previous > 0 && h.Level > previous + 1)
                    yield return Hit(s, h.Index, $"{h.Index}", "Heading level is skipped", $"Heading {h.Index + 1} jumps from level {previous} to level {h.Level}.",
                        "Use heading levels in order so the outline is predictable for screen-reader navigation.", $"Heading {h.Index + 1}: h{previous} followed by h{h.Level} (\"{Clip(h.Text)}\").");
                previous = h.Level;
            }
        }
    }

    private sealed class EmptyLink : ElementCheck
    {
        public override string Code => LinkEmpty; public override string Name => "Link has no accessible text"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "High"; public override int Stage => Links;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s) =>
            c.Evidence.Links.Where(l => l.Text.Trim().Length == 0).Select(l => Hit(s, l.Index, $"{l.Index}", "Link has no accessible text",
                $"Link {l.Index + 1} has no text or label.", "Give the link visible text that describes its destination.", $"Link {l.Index + 1}: empty text ({Destination(l)})."));
    }
    private sealed class VagueLink : ElementCheck
    {
        public override string Code => LinkVague; public override string Name => "Link text is not descriptive"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Medium"; public override int Stage => Links;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Words(VaguePhrases, "Vague link phrases", "click here", "here", "click", "read more", "more", "link", "this", "this link", "learn more", "go", "details", "info", "download")];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var vague = new HashSet<string>(s.Words(VaguePhrases).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var l in c.Evidence.Links)
            {
                var text = l.Text.Trim();
                if (text.Length > 0 && (vague.Contains(text.TrimEnd('.', '!', ':')) || UrlPattern().IsMatch(text)))
                    yield return Hit(s, l.Index, $"{l.Index}", "Link text is not descriptive", $"Link {l.Index + 1} uses text that does not describe its destination out of context.",
                        "Rewrite the link text to name the destination or action, for example \"Project 1 rubric\".", $"Link {l.Index + 1}: \"{Clip(text)}\" ({Destination(l)}).");
            }
        }
    }

    private sealed class MissingTableHeaders : ElementCheck
    {
        public override string Code => TableHeaders; public override string Name => "Table has no header cells"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Medium"; public override int Stage => Tables;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s) =>
            c.Evidence.Tables.Where(t => !t.HasHeaderCells && t.Rows > 1).Select(t => Hit(s, t.Index, $"{t.Index}", "Table has no header cells",
                $"Table {t.Index + 1} has {t.Rows} rows but no header cells.", "Mark the header row or column using table header cells, or use a list if the content is not tabular.",
                $"Table {t.Index + 1}: {t.Rows} rows, no <th> cells."));
    }

    private sealed class MissingRubric : ElementCheck
    {
        public override string Code => RubricMissing; public override string Name => "Assignment has no attached rubric"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Medium"; public override int Stage => AssignmentMetadata;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            if (c.ElementType != "assignment") yield break;
            if (c.Evidence.Assignment?.HasRubric is false)
                yield return Hit(s, 0, null, "Assignment has no attached rubric", "Canvas did not report a rubric for this assignment at capture time.",
                    "Attach a rubric or state evaluation criteria so learners know how their work is assessed.", "Assignment metadata: no rubric reported.");
            else if (c.Evidence.Assignment?.HasRubric is null)
                c.Limit("Rubric metadata was not captured for this assignment, so rubric presence was not checked.");
        }
    }

    private static string Destination(LinkEvidence link) => link.Host is null ? link.Destination.ToLowerInvariant() + " destination" : "external: " + link.Host;
    [GeneratedRegex(@"^[^\s]+\.(png|jpe?g|gif|svg|webp|bmp|tiff?)$", RegexOptions.IgnoreCase)] private static partial Regex FileNamePattern();
    [GeneratedRegex(@"^(https?://|www\.)\S+$", RegexOptions.IgnoreCase)] private static partial Regex UrlPattern();
}
