namespace TeachWell.Application;

// Phase 11 (ADR 014): results across the college for CTL reviewers and Academic administrators. Figures describe courses, never people:
// no instructor or reviewer names, no rankings. Course content still requires ownership or a share.
public record OversightDto(ReportScope Scope, string? Department, IReadOnlyList<string> Departments, int Courses, int CoursesChecked,
    IReadOnlyList<ReadinessStateCountDto> Readiness, IReadOnlyList<AspectIssuesDto> IssuesByAspect, IReadOnlyList<CheckIssuesDto> TopChecks,
    GuideHelpDto HelpRequests, IReadOnlyList<ProgressPointDto> Progress, IReadOnlyList<OversightCourseDto> CourseRows,
    IReadOnlyList<OversightEndorsementDto> Endorsements, bool CanRefresh, string? Program, IReadOnlyList<string> Programs,
    int AutoRecheckHours, DateTime? LastAutomaticRecheck, IReadOnlyList<ConformanceRollupDto> Conformance);
public record ReadinessStateCountDto(string State, string Label, int Courses);
public record AspectIssuesDto(string Aspect, string Label, int OpenPlaces, int CoursesAffected);
public record CheckIssuesDto(string CheckCode, string Name, string Aspect, int CoursesAffected, int OpenPlaces);
// Open accessibility work rolled up to WCAG success criteria across the courses in scope (ADR 018 amendment).
//
// It reports where work is outstanding, and deliberately cannot report the opposite. Guide records store only the open
// places per check, so a course with none for a criterion is indistinguishable from one where that criterion's checks
// never ran. CoursesAffected is therefore a floor on the work, never a count of courses that satisfy the criterion —
// the per-course conformance report is the view that draws that distinction.
public record ConformanceRollupDto(string Number, string Name, string Level, string Reference, IReadOnlyList<string> Checks,
    int CoursesAffected, int OpenPlaces);
public record GuideHelpDto(int Total, int Open, IReadOnlyList<CategoryCountDto> ByCategory);
public record CategoryCountDto(string Category, int Count);
// Totals across courses using each course's latest guide record on or before the week's end.
public record ProgressPointDto(DateTime WeekEnding, int CoursesChecked, int OpenPlaces, int DonePlaces, int NotApplicablePlaces);
public record OversightCourseDto(Guid CourseId, string Code, string Title, string? Department, string? DeliveryMode, string ReadinessState, string ReadinessLabel,
    DateTime? LastChecked, int? OpenSteps, int? RequiredOpenSteps, bool CanOpen, string? Program, DateTime? EndorsedAt, DateTime? EndorsementValidUntil);
public record OversightEndorsementDto(Guid CourseId, string CourseCode, string CourseTitle, string? Department, string Wording, DateTime EndorsedAt,
    DateTime ValidUntil, bool RecheckDue);

public interface IOversightService
{
    Task<ServiceResult<OversightDto>> GetAsync(CurrentUser user, string? department, string? program, CancellationToken ct);
    // CTL only: re-runs the improvement guide for every course in the tenant so the figures are current. Returns the number checked.
    Task<ServiceResult<int>> RefreshAsync(CurrentUser user, CancellationToken ct);
}

// SystemAdmin role management (ADR 014). Only Instructor, CTLReviewer and AcademicAdmin can be granted or revoked in the app.
public record AdminUserDto(Guid Id, string DisplayName, string Email, string[] Roles);
public record RoleChangeInput(string? Role, bool Granted);

// SystemAdmin audit viewer (ADR 014 amendment): newest first, filtered, paged by a timestamp cursor.
public record AuditQuery(string? Action, Guid? ActorId, DateTime? From, DateTime? To, DateTime? Before);
public record AuditEntryDto(Guid Id, DateTime Timestamp, Guid ActorId, string ActorName, string Action, Guid EntityId, string? EntityLabel, string? SubjectName);
public record AuditPageDto(IReadOnlyList<AuditEntryDto> Items, DateTime? NextBefore, IReadOnlyList<string> Actions);
public interface IAuditService
{
    Task<ServiceResult<AuditPageDto>> ListAsync(CurrentUser user, AuditQuery query, CancellationToken ct);
}

public interface IAdminUserService
{
    Task<IReadOnlyList<AdminUserDto>> ListAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<AdminUserDto>> ChangeRoleAsync(CurrentUser user, Guid userId, RoleChangeInput input, CancellationToken ct);
}
