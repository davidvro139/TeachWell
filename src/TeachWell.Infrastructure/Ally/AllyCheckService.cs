using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Ally;

// Checks an uploaded Ally report export against TeachWell's reader (ADR 020 slice 1).
//
// Deliberately read-only. Nothing from the file reaches the database: this exists so a real export can be compared
// with the documentation the reader was built from, which is the one assumption slice 1 could not test.
public sealed class AllyCheckService(AppDbContext db) : IAllyCheckService
{
    public const string Caveat =
        "This is a format check, not an import. Nothing from the file was saved: no scores, no findings, and no copy of the file itself. " +
        "It reports what TeachWell's reader understood, so that a real export can be compared with the documentation the reader was built " +
        "from before anything is stored. Columns it does not recognise are listed rather than ignored silently.";

    public async Task<ServiceResult<AllyReportCheckDto>> CheckAsync(CurrentUser user, Stream file, string fileName, CancellationToken ct)
    {
        // The two reports are told apart by their columns. Courses is tried first: its "Course id" column is the
        // distinctive one, and an item report is rejected by that reader with a message naming the right file.
        AllyReport<AllyCourseRow>? courses = null;
        AllyReport<AllyItemRow>? items = null;
        try
        {
            courses = AllyReportReader.ReadCourses(file);
        }
        catch (AllyReadException)
        {
            if (!file.CanSeek) return ServiceResult<AllyReportCheckDto>.Invalid("file", "The upload could not be re-read. Try again.");
            file.Position = 0;
            try { items = AllyReportReader.ReadItems(file); }
            catch (AllyReadException e) { return ServiceResult<AllyReportCheckDto>.Invalid("file", e.Message); }
        }

        var issueRows = courses is not null
            ? courses.Rows.Select(r => r.Issues).ToList()
            : items!.Rows.Select(r => r.Issues).ToList();

        var issues = AllyIssues.All
            .Select(issue => (issue, Rows: issueRows.Count(r => r.ContainsKey(issue.Code)), Places: issueRows.Sum(r => r.GetValueOrDefault(issue.Code))))
            .Where(x => x.Rows > 0)
            .OrderByDescending(x => x.Places).ThenBy(x => x.issue.Code, StringComparer.Ordinal)
            .Select(x => new AllyIssueFoundDto(x.issue.Code, x.issue.Description, AllyIssues.SeverityOf(x.issue.Severity),
                x.issue.PureGain, x.issue.Overlap, x.issue.Note, x.Rows, x.Places))
            .ToList();

        var report = courses is not null
            ? new { Kind = "Courses", courses.MissingColumns, courses.UnknownIssueColumns, courses.Notes, courses.MissingIssueColumns, Rows = courses.Rows.Count }
            : new { Kind = "Items", items!.MissingColumns, items.UnknownIssueColumns, items.Notes, items.MissingIssueColumns, Rows = items.Rows.Count };

        // Only a Courses file can be matched: an item report carries no course identity of its own.
        ReconciliationSummary? summary = null;
        var unmatched = new List<string>();
        if (courses is not null)
        {
            var keys = await db.Courses.AsNoTracking().Where(c => c.TenantId == user.TenantId)
                .Select(c => new { c.Id, c.SourceCourseId, c.Code }).ToListAsync(ct);
            var (matches, s) = AllyReconciliation.MatchCourses(courses.Rows.Select(r => r.CourseId),
                keys.Select(k => new CourseKey(k.Id, k.SourceCourseId, k.Code)));
            summary = s;
            unmatched = matches.Where(m => m.CourseId is null).Select(m => m.AllyCourseId).Take(25).ToList();
        }

        // How much of the catalogue this file actually carries, which is the headline answer to "does the reader
        // understand this file": a recognised count well below the known count means the columns differ.
        var recognised = AllyIssues.All.Count - report.MissingIssueColumns.Count;

        return ServiceResult<AllyReportCheckDto>.Ok(new AllyReportCheckDto(report.Kind, fileName, report.Rows,
            report.MissingColumns, report.UnknownIssueColumns, report.Notes, issues,
            recognised, AllyIssues.All.Count, summary, unmatched, Caveat));
    }
}
