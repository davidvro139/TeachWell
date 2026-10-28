using TeachWell.Domain;

namespace TeachWell.Infrastructure.Ally;

// Reads Ally's two useful report exports (ADR 020): the institutional report's Courses report, one row per course,
// and the Individual Course report, one row per content item.
//
// Both are read tolerantly and described honestly. The reader never infers a value it did not find: an absent column
// becomes a null field and a note naming the column, so the caller can tell "Ally reported zero" from "TeachWell could
// not find that column in this file".

// One course, from the institutional report's Courses report. Scores are Ally's own 0-100 averages.
public sealed record AllyCourseRow(string CourseId, string? CourseCode, string? CourseName, string? TermName, string? DepartmentName,
    int? Students, DateTime? LastChecked, bool? AllyEnabled, int? TotalFiles, int? TotalWysiwyg,
    decimal? OverallScore, decimal? FilesScore, decimal? WysiwygScore, IReadOnlyDictionary<string, int> Issues);

// One content item, from the Individual Course report. Url is the join to a TeachWell element; LibraryReference is how
// Ally recognises the same content across courses.
public sealed record AllyItemRow(string Id, string? Name, string? MimeType, decimal? Score, string? Url,
    string? LibraryReference, DateTime? DeletedAt, IReadOnlyDictionary<string, int> Issues);

// What was read, and everything about the file that the caller should know before acting on it.
public sealed record AllyReport<T>(IReadOnlyList<T> Rows, IReadOnlyList<string> MissingColumns, IReadOnlyList<string> UnknownIssueColumns,
    IReadOnlyList<string> Notes);

public static class AllyReportReader
{
    public static class CourseColumns
    {
        public const string Id = "Course id", Code = "Course code", Name = "Course name", Term = "Term name", Department = "Department name",
            Students = "Number of students", LastChecked = "Last checked on", Enabled = "Ally enabled", TotalFiles = "Total files",
            TotalWysiwyg = "Total WYSIWYG", Overall = "Overall score", Files = "Files score", Wysiwyg = "WYSIWYG score",
            Deleted = "Observed deleted on";
        public static readonly string[] Expected = [Id, Code, Name, Term, Department, Students, LastChecked, Enabled, TotalFiles, TotalWysiwyg, Overall, Files, Wysiwyg];
    }

    public static class ItemColumns
    {
        public const string Id = "Id", Name = "Name", Mime = "Mime type", Score = "Score", Url = "Url",
            Library = "Library reference", Deleted = "Deleted at";
        public static readonly string[] Expected = [Id, Name, Mime, Score, Url];
    }

    public static AllyReport<AllyCourseRow> ReadCourses(Stream stream)
    {
        var table = AllyTable.Read(stream);
        if (!table.Has(CourseColumns.Id))
            throw new AllyReadException($"This file has no \"{CourseColumns.Id}\" column, so it is not Ally's Courses report. Export the institution report and upload its courses file.");

        var rows = new List<AllyCourseRow>();
        var skipped = 0;
        for (var i = 0; i < table.Count; i++)
        {
            var id = table.Value(i, CourseColumns.Id).Trim();
            // A row with no course id cannot be joined to anything, so it is counted rather than guessed at.
            if (id.Length == 0) { skipped++; continue; }
            rows.Add(new AllyCourseRow(id,
                Text(table.Value(i, CourseColumns.Code)), Text(table.Value(i, CourseColumns.Name)),
                Text(table.Value(i, CourseColumns.Term)), Text(table.Value(i, CourseColumns.Department)),
                table.Number(i, CourseColumns.Students), table.Date(i, CourseColumns.LastChecked),
                Flag(table, i, CourseColumns.Enabled), table.Number(i, CourseColumns.TotalFiles), table.Number(i, CourseColumns.TotalWysiwyg),
                table.Decimal(i, CourseColumns.Overall), table.Decimal(i, CourseColumns.Files), table.Decimal(i, CourseColumns.Wysiwyg),
                Issues(table, i)));
        }
        return Describe(table, rows, CourseColumns.Expected, skipped, "course");
    }

    public static AllyReport<AllyItemRow> ReadItems(Stream stream)
    {
        var table = AllyTable.Read(stream);
        // Recognising the other report comes first, so the commonest mistake gets the most useful message.
        if (table.Has(CourseColumns.Id))
            throw new AllyReadException("This file looks like Ally's Courses report rather than one course's report. Upload it as the courses file instead.");
        // The item report's "Id" is generic, so the Mime type column is what identifies the shape.
        if (!table.Has(ItemColumns.Id) || !table.Has(ItemColumns.Mime))
            throw new AllyReadException($"This file has no \"{ItemColumns.Id}\" and \"{ItemColumns.Mime}\" columns, so it is not an Ally course report. Export one course's report from Ally and upload that.");

        var rows = new List<AllyItemRow>();
        var skipped = 0;
        for (var i = 0; i < table.Count; i++)
        {
            var id = table.Value(i, ItemColumns.Id).Trim();
            if (id.Length == 0) { skipped++; continue; }
            rows.Add(new AllyItemRow(id, Text(table.Value(i, ItemColumns.Name)), Text(table.Value(i, ItemColumns.Mime)),
                table.Decimal(i, ItemColumns.Score), Text(table.Value(i, ItemColumns.Url)),
                Text(table.Value(i, ItemColumns.Library)), table.Date(i, ItemColumns.Deleted), Issues(table, i)));
        }
        return Describe(table, rows, ItemColumns.Expected, skipped, "content item");
    }

    private static string? Text(string value) => value.Trim() is { Length: > 0 } trimmed ? trimmed : null;
    private static bool? Flag(AllyTable table, int row, string column) =>
        table.Has(column) ? AllyTable.Flagged(table.Value(row, column)) : null;

    // Only issue columns TeachWell's catalogue knows are read; the rest are reported as unknown so a new Ally rule is
    // visible rather than silently dropped.
    private static IReadOnlyDictionary<string, int> Issues(AllyTable table, int row)
    {
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in AllyIssues.All)
        {
            if (!table.Has(issue.Code)) continue;
            var raw = table.Value(row, issue.Code);
            if (!AllyTable.Flagged(raw)) continue;
            found[issue.Code] = int.TryParse(raw.Trim(), out var n) ? n : 1;
        }
        return found;
    }

    private static AllyReport<T> Describe<T>(AllyTable table, List<T> rows, string[] expected, int skipped, string noun)
    {
        var notes = new List<string>();
        if (skipped > 0) notes.Add($"{skipped} {(skipped == 1 ? $"{noun} row" : $"{noun} rows")} had no identifier and were not read.");

        var missing = table.Missing(expected).ToList();
        var missingIssues = table.Missing(AllyIssues.All.Select(i => i.Code));
        if (missingIssues.Count == AllyIssues.All.Count)
            notes.Add("This file has none of Ally's issue columns, so it records scores but no issues. Check that the export was not filtered.");
        else if (missingIssues.Count > 0)
            notes.Add($"{missingIssues.Count} of Ally's {AllyIssues.All.Count} documented issue columns are absent from this file, so those issues were not read: {string.Join(", ", missingIssues.Take(8))}{(missingIssues.Count > 8 ? ", …" : "")}.");

        // Headers that are neither an expected field nor a known issue column: Ally adds columns, and a new issue rule
        // arriving as an unknown column is exactly what a reviewer needs to be told about.
        var known = expected.Concat(AllyIssues.All.Select(i => i.Code))
            .Concat([AllyIssues.LibraryReference, CourseColumns.Deleted, ItemColumns.Library, ItemColumns.Deleted,
                "Term id", "Department id", "Course url", "pdf", "image", "html-page", "presentation", "document", "other",
                // The time and department rollup reports share these columns; they are not issues (ADR 020).
                "Month", "Academic year", "Number of courses"])
            .Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Compared on the base name, because an issue column arrives as "AlternativeText:2".
        var unknown = table.Headers.Select(h => AllyTable.BaseName(h)).Where(h => h.Length > 0 && !known.Contains(h))
            // Ally emits a per-content-type issue count column for each Canvas content type; those are counts, not issues.
            .Where(h => !h.StartsWith("application/x-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            notes.Add($"{unknown.Count} column(s) in this file are not in TeachWell's Ally catalogue and were ignored: {string.Join(", ", unknown.Take(8))}{(unknown.Count > 8 ? ", …" : "")}. If Ally has added a rule, TeachWell needs updating to report it.");

        // The catalogue's severities come from documentation. Where a file states its own in the column header, the two
        // are compared, so a changed severity is reported rather than quietly contradicted.
        var disagreements = AllyIssues.All
            .Select(issue => (issue, Stated: table.HeaderFor(issue.Code) is { } header ? AllyTable.SeverityIn(header) : null))
            .Where(x => x.Stated is not null && x.Stated != (int)x.issue.Severity)
            .Select(x => $"{x.issue.Code} ({x.Stated} in this file, {(int)x.issue.Severity} in TeachWell)").ToList();
        if (disagreements.Count > 0)
            notes.Add($"{disagreements.Count} issue column(s) state a different severity than TeachWell's catalogue records: {string.Join(", ", disagreements.Take(8))}{(disagreements.Count > 8 ? ", …" : "")}. The file's severity is the authority; TeachWell's catalogue needs updating.");

        return new AllyReport<T>(rows, missing, unknown, notes);
    }
}
