using TeachWell.Domain;

namespace TeachWell.Application;

// Checking an Ally report export against TeachWell's reader (ADR 020 slice 1).
//
// This is a format check, not an import. It parses an uploaded export, says what the reader understood, and stores
// nothing: no findings, no scores, no file. Its purpose is to answer the one question slice 1 cannot answer on its
// own — does a real export match the documentation the reader was built from? — before any schema is designed
// around the answer.

// One issue column the reader recognised, with how much of it the file actually carries.
public record AllyIssueFoundDto(string Code, string Description, string Severity, bool PureGain, string? Overlap, string? Note, int Rows, int Places);

// Kind is "Courses" for the institutional report's Courses file, or "Items" for one course's report.
// Courses is present only for a Courses file, where course ids can be matched against imported courses.
public record AllyReportCheckDto(string Kind, string FileName, int Rows,
    IReadOnlyList<string> MissingColumns, IReadOnlyList<string> UnknownColumns, IReadOnlyList<string> Notes,
    IReadOnlyList<AllyIssueFoundDto> Issues, int IssueColumnsRecognised, int IssueColumnsKnown,
    ReconciliationSummary? Courses, IReadOnlyList<string> UnmatchedCourseIds, string Caveat);

public interface IAllyCheckService
{
    // CTL only (enforced by the route). Reads the stream, reports, and persists nothing.
    Task<ServiceResult<AllyReportCheckDto>> CheckAsync(CurrentUser user, Stream file, string fileName, CancellationToken ct);
}
