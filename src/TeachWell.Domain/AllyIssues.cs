namespace TeachWell.Domain;

// What Anthology Ally reports, and how it lines up with TeachWell (ROADMAP item 2, ADR 020).
//
// Ally scores every file and WYSIWYG item in every course and exposes the result as CSV exports and a reporting API.
// Its unit of work is a file; TeachWell's is a course under review by a person. The point of ingesting Ally is to
// acquire the file layer — scanned PDFs, untagged PDFs, document language, contrast, captions — without reimplementing
// it, and to spend the saved effort on the course-design work Ally has no concept of.
//
// This catalogue is built from Anthology's documented Canvas export columns, not from a real export. Every entry is
// therefore a documented expectation: the reader matches columns by name, ignores ones it does not know, and reports
// the ones it expected and did not find, so a schema that differs shows up as a reconciliation note rather than as
// silently missing findings.

// How Ally rates an issue: 1 severe, 2 major, 3 minor, in its own documentation's terms.
public enum AllySeverity { Severe = 1, Major = 2, Minor = 3 }

// One Ally issue column.
//
// Overlap is the TeachWell check that finds the same thing, or null when nothing in TeachWell does. Where they overlap,
// TeachWell's own finding wins and Ally's is not reported again, so one issue never becomes two steps. Where Ally's
// check is the weaker of the two — its HtmlLinkName only catches an empty link name, while LINK_VAGUE also catches
// "click here" — that is noted on the entry rather than left implicit.
public sealed record AllyIssue(string Code, AllySeverity Severity, string Description, string? Overlap = null, string? Note = null)
{
    // An issue Ally can report and TeachWell cannot: the reason for ingesting at all.
    public bool PureGain => Overlap is null;
}

public static class AllyIssues
{
    // Issue columns, in the order Anthology documents them.
    public static readonly IReadOnlyList<AllyIssue> All =
    [
        // Files: the whole layer TeachWell skips. CanvasExportReader counts course files and reports them as not imported.
        new("Parsable", AllySeverity.Severe, "The document could not be read at all, so nothing in it is available to any student."),
        new("Scanned", AllySeverity.Severe, "The document is a scan with no text layer, so it is an image of text: unreadable by screen readers, unsearchable and unselectable."),
        new("Security", AllySeverity.Severe, "The document is password-protected or otherwise restricted, which blocks assistive technology."),
        new("Ocred", AllySeverity.Major, "The document has been through OCR, so its text is machine-read and may be wrong where the scan was poor."),
        new("Tagged", AllySeverity.Major, "The PDF has no tag structure, so it has no headings, reading order or table semantics for a screen reader."),
        new("Title", AllySeverity.Minor, "The document has no title, so it is announced and listed by its file name."),
        new("LanguagePresence", AllySeverity.Minor, "The document does not declare its language, so a screen reader may read it in the wrong voice."),
        new("LanguageCorrect", AllySeverity.Minor, "The document declares a language that does not match its text."),
        new("HeadingsPresence", AllySeverity.Major, "The document has no headings, so it cannot be navigated or skimmed."),
        new("HeadingsSequential", AllySeverity.Minor, "The document's heading levels skip, so its outline looks like a section is missing."),
        new("HeadingsHigherLevel", AllySeverity.Minor, "The document starts at a lower heading level than it should."),
        new("HeadingsStartAtOne", AllySeverity.Minor, "The document's headings do not start at the top level."),
        new("TableHeaders", AllySeverity.Major, "A table in the document has no header cells."),
        new("AlternativeText", AllySeverity.Major, "An image in the document has no text alternative."),
        new("ImageDescription", AllySeverity.Major, "An image needs a description that conveys what it shows."),
        new("ImageDecorative", AllySeverity.Major, "An image is marked decorative but appears to carry information."),
        new("ImageContrast", AllySeverity.Major, "Text inside an image does not contrast enough with its background."),
        new("Contrast", AllySeverity.Major, "Text in the document does not contrast enough with its background."),
        new("ImageSeizure", AllySeverity.Severe, "The image flashes or flickers in a way that can trigger a seizure."),
        // ImageOcr is in the documented column list but Anthology notes it is no longer populated. Kept so the reader
        // recognises the column rather than reporting it as unknown, and marked so nothing acts on it.
        new("ImageOcr", AllySeverity.Minor, "No longer populated by Ally.", Note: "Retired by Anthology; recognised but not reported."),

        // WYSIWYG content: HTML in Canvas pages, assignments, announcements, discussions, quizzes and the syllabus.
        // Some of this overlaps TeachWell's own checks, which win where they do.
        new("HtmlColorContrast", AllySeverity.Major, "Text does not contrast enough with its background."),
        new("HtmlCaption", AllySeverity.Major, "A video has no captions.", Overlap: MediaChecks.CaptionsMissing,
            Note: "TeachWell checks HTML players and asks about recognised embeds; Ally's rule covers HTML players only."),
        new("HtmlHasLang", AllySeverity.Minor, "The content does not declare its language."),
        new("HtmlTitle", AllySeverity.Minor, "The content has no title."),
        new("HtmlLabel", AllySeverity.Major, "A form field has no label."),
        new("HtmlObjectAlt", AllySeverity.Major, "An embedded object has no text alternative."),
        new("HtmlBrokenLink", AllySeverity.Major, "A link does not resolve."),
        new("HtmlDefinitionList", AllySeverity.Minor, "A definition list is malformed."),
        new("HtmlImageAlt", AllySeverity.Major, "An image has no alt text.", Overlap: DeterministicChecks.ImageAltMissing),
        new("HtmlImageRedundantAlt", AllySeverity.Minor, "An image's alt text repeats nearby text or its file name.", Overlap: DeterministicChecks.ImageAltFilename,
            Note: "Overlaps only partly: TeachWell's check is specifically alt text that is a file name."),
        new("HtmlEmptyHeading", AllySeverity.Major, "A heading contains no text.", Overlap: DeterministicChecks.HeadingEmpty),
        new("HtmlHeadingOrder", AllySeverity.Minor, "Heading levels skip.", Overlap: DeterministicChecks.HeadingSkipped),
        new("HtmlHeadingsPresence", AllySeverity.Major, "The content has no headings.",
            Note: "TeachWell's PAGE_LONG_NO_HEADINGS is close but conditional on length, so this is not treated as an overlap."),
        new("HtmlHeadingsStart", AllySeverity.Major, "Headings do not start at the top level."),
        new("HtmlLinkName", AllySeverity.Minor, "A link has no accessible name.", Overlap: DeterministicChecks.LinkEmpty,
            Note: "TeachWell is stronger here: LINK_VAGUE also catches \"click here\", which Ally's rule does not."),
        new("HtmlList", AllySeverity.Minor, "A list is not marked up as a list.", Overlap: ContentChecks.ListAsText),
        new("HtmlTdHasHeader", AllySeverity.Major, "A table cell has no associated header.", Overlap: DeterministicChecks.TableHeaders),
    ];

    private static readonly Dictionary<string, AllyIssue> ByCode = All.ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
    public static AllyIssue? Find(string code) => ByCode.GetValueOrDefault(code.Trim());

    // Issues TeachWell has no check for: what ingesting Ally actually buys.
    public static IReadOnlyList<AllyIssue> Gains => [.. All.Where(i => i.PureGain)];
    // Issues both tools find. TeachWell's own finding wins, so these are mapped and not reported twice.
    public static IReadOnlyList<AllyIssue> Overlaps => [.. All.Where(i => !i.PureGain)];

    // A column that is not an issue: Ally emits it in the same block, and it is how the same content is recognised
    // across courses, which is what makes a "fix once, fixes forty courses" worklist possible (ROADMAP item 3).
    public const string LibraryReference = "LibraryReference";

    // Ally's own severity, mapped to TeachWell's. Deliberately not generous: Ally's "severe" means the file is unusable,
    // which is the one case that becomes Critical.
    public static string SeverityOf(AllySeverity severity) => severity switch
    {
        AllySeverity.Severe => "Critical",
        AllySeverity.Major => "High",
        _ => "Medium",
    };
}
