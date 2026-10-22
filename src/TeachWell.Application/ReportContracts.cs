namespace TeachWell.Application;

// Every report carries its own scope, generation time and caveats, so a number is never presented without the
// context needed to judge it — and never as a pass/fail compliance badge (see ADR 008).
public record ReportScope(string Description, DateTime GeneratedAt, IReadOnlyList<string> Caveats);

public record MappedCompetencyDto(Guid Id, string Code, string Title, IReadOnlyList<string> ElementTitles);
public record OrphanCompetencyDto(Guid Id, string Code, string Title);
public record UnmappedAssessmentDto(Guid ElementId, string Title);
public record AlignmentReportDto(ReportScope Scope, int TotalCompetencies, int TotalAssessments, int MappedAssessmentCount, decimal? CoveragePercent,
    IReadOnlyList<MappedCompetencyDto> Competencies, IReadOnlyList<OrphanCompetencyDto> OrphanCompetencies, IReadOnlyList<UnmappedAssessmentDto> UnmappedAssessments);

public record CategoryAverageDto(string Category, decimal? AveragePercent, int RatedReviews);
public record OutstandingFindingDto(Guid FindingId, Guid ReviewId, Guid ElementId, string ElementTitle, string Title, string Severity, DateTime CreatedAt, bool HasOpenTask);
public record CourseQualityReportDto(ReportScope Scope, int ReviewableElements, int ElementsWithPublishedReview, decimal? ReviewedCoveragePercent,
    decimal? AverageScorePercent, IReadOnlyList<CategoryAverageDto> CategoryAverages, IReadOnlyList<OutstandingFindingDto> OutstandingFindings);

public record SupportThemeDto(string Category, int Count);
public record TaskStatusCountDto(string Status, int Count, decimal? AverageAgeDays);
public record CtlOverviewReportDto(ReportScope Scope, int AccessibleCourses, int PublishedReviews, int OutstandingFindings,
    IReadOnlyList<TaskStatusCountDto> TasksByStatus, IReadOnlyList<SupportThemeDto> SupportThemes);

public record AgingTaskDto(Guid Id, string Title, string CourseTitle, string Status, int DaysOpen, string Priority);
public record RevisionReportDto(ReportScope Scope, IReadOnlyList<TaskStatusCountDto> TasksByStatus, IReadOnlyList<AgingTaskDto> OpenTasks);

public interface IReportService
{
    Task<ServiceResult<AlignmentReportDto>> AlignmentAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<ServiceResult<CourseQualityReportDto>> QualityAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    // Not course-specific; scoped to the caller's own accessible courses, and role-gated at the route (CTL-only), so neither needs a not-found path.
    Task<CtlOverviewReportDto> CtlOverviewAsync(CurrentUser user, CancellationToken ct);
    Task<RevisionReportDto> RevisionsAsync(CurrentUser user, CancellationToken ct);
}
