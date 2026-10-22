using TeachWell.Domain;

namespace TeachWell.Application;

// Phase 10 (ADR 013). Guide is null for viewers without course access (the tenant-wide CTL queue shows only course metadata).
public record ReadinessRequestDto(Guid Id, Guid CourseId, string CourseCode, string CourseTitle, string RequestedByName, DateTime RequestedAt, string Note,
    string Status, string StandardsName, int? StandardsVersionNumber, DateTime? ContentAsOf, string? ReviewerName, bool TakenByMe, DateTime? TakenAt,
    DateTime? DecidedAt, string DecisionSummary, Guid Revision, ReadinessGuideSummary? Guide);
public record EndorsementDto(Guid Id, string Wording, string StandardsName, int? StandardsVersionNumber, DateTime? ContentAsOf, string ReviewerName,
    DateTime EndorsedAt, DateTime ValidUntil, string Summary, bool RecheckDue, IReadOnlyList<string> RecheckReasons);
// Everything the course page needs. Blockers explain why the owner cannot request yet; CanDecide is true for the reviewer who took the
// active request; Requirements lists what the pinned standard-set version needs before endorsing, and HasQualifyingReview says all are met.
public record CourseReadinessDto(Guid CourseId, bool IsOwner, bool CanRequest, IReadOnlyList<string> Blockers, int RequiredOpenSteps,
    ReadinessRequestDto? Active, ReadinessRequestDto? LastDecision, EndorsementDto? Endorsement, IReadOnlyList<ReadinessRequestDto> History,
    bool CanDecide, bool HasQualifyingReview, IReadOnlyList<ReadinessRequirementDto> Requirements);
// One condition for endorsing, from the standard-set version pinned on the request (shown to the deciding reviewer).
public record ReadinessRequirementDto(string Label, bool Met, string Detail);

public record ReadinessRequestInput(string? Note);

// In-app notifications. Read is per notification; Unread counts all of the caller's unread ones.
public record NotificationDto(Guid Id, string Kind, string Title, string Body, Guid? CourseId, DateTime CreatedAt, bool Read);
public record NotificationListDto(int Unread, IReadOnlyList<NotificationDto> Items);
public interface INotificationService
{
    Task<NotificationListDto> ListAsync(CurrentUser user, CancellationToken ct);
    // Null marks all of the caller's notifications read.
    Task<ServiceResult<NotificationListDto>> MarkReadAsync(CurrentUser user, Guid? notificationId, CancellationToken ct);
}
public record ReadinessRevisionInput(Guid Revision);
public record ReadinessDecisionInput(Guid Revision, string? Decision, string? Summary);

public interface IReadinessService
{
    Task<ServiceResult<CourseReadinessDto>> GetForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<ServiceResult<CourseReadinessDto>> RequestAsync(CurrentUser user, Guid courseId, ReadinessRequestInput input, CancellationToken ct);
    Task<ServiceResult<CourseReadinessDto>> WithdrawAsync(CurrentUser user, Guid requestId, ReadinessRevisionInput input, CancellationToken ct);
    Task<IReadOnlyList<ReadinessRequestDto>> QueueAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<ReadinessRequestDto>> TakeAsync(CurrentUser user, Guid requestId, ReadinessRevisionInput input, CancellationToken ct);
    Task<ServiceResult<CourseReadinessDto>> DecideAsync(CurrentUser user, Guid requestId, ReadinessDecisionInput input, CancellationToken ct);
}
