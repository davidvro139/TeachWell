using System.Text;
using TeachWell.Domain;

// Fictional Ally report exports, built to Anthology's documented Canvas export columns (ADR 020).
//
// These are written from the documentation, not copied from a real export, which is the whole reason the reader is
// tolerant and reports what it did not recognise. Nothing here is real institutional data.
public static class AllyFixtures
{
    // Every issue column Ally documents, in catalogue order, with the severity suffix the documentation shows in the
    // header itself ("AlternativeText:2"). That is the form a real export is expected to use.
    private static readonly string[] IssueColumns = [.. AllyIssues.All.Select(i => $"{i.Code}:{(int)i.Severity}")];

    public static byte[] Courses(params (string Id, string Code, string Name, int Students, decimal Overall, (string Issue, int Count)[] Issues)[] rows)
    {
        var header = new[] { "Term id", "Term name", "Department id", "Department name", "Course id", "Course code", "Course name", "Course url",
            "Number of students", "Observed deleted on", "Last checked on", "Ally enabled", "Total files", "Total WYSIWYG",
            "Overall score", "Files score", "WYSIWYG score", "pdf", "image", "html-page", "presentation", "document", "other",
            "application/x-announcement", "application/x-assignment", "application/x-discussion-topic",
            "application/x-quiz", "application/x-syllabus", "application/x-page" }.Concat(IssueColumns);

        var text = new StringBuilder();
        text.Append('﻿'); // Ally's exports carry a byte-order mark
        Line(text, header);
        foreach (var row in rows)
        {
            var counts = row.Issues.ToDictionary(i => i.Issue, i => i.Count, StringComparer.OrdinalIgnoreCase);
            Line(text, new[] { "t1", "Fall 2026", "d1", "Computing", row.Id, row.Code, row.Name, $"https://canvas.example.test/courses/{row.Id}",
                $"{row.Students}", "", "2026-10-20T08:00:00Z", "true", "12", "9",
                $"{row.Overall}", "61", "88", "7", "2", "9", "1", "2", "0", "1", "3", "0", "0", "1", "2" }
                .Concat(AllyIssues.All.Select(i => counts.TryGetValue(i.Code, out var n) ? $"{n}" : "0")));
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static byte[] Items(params (string Id, string Name, string Mime, decimal Score, string Url, string Library, (string Issue, int Count)[] Issues)[] rows)
    {
        var header = new[] { "Id", "Name", "Mime type", "Score", "Deleted at", "Library reference", "Url" }.Concat(IssueColumns);
        var text = new StringBuilder();
        text.Append('﻿');
        Line(text, header);
        foreach (var row in rows)
        {
            var counts = row.Issues.ToDictionary(i => i.Issue, i => i.Count, StringComparer.OrdinalIgnoreCase);
            Line(text, new[] { row.Id, row.Name, row.Mime, $"{row.Score}", "", row.Library, row.Url }
                .Concat(AllyIssues.All.Select(i => counts.TryGetValue(i.Code, out var n) ? $"{n}" : "0")));
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    // RFC 4180: a value holding a comma, quote or newline is quoted, and quotes inside it are doubled.
    private static void Line(StringBuilder text, IEnumerable<string> values)
    {
        text.Append(string.Join(',', values.Select(v =>
            v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v))).Append("\r\n");
    }

    public static Stream Stream(byte[] bytes) => new MemoryStream(bytes);
}
