using TeachWell.Domain;

namespace TeachWell.Application;

public record TaskFindingSummaryDto(Guid FindingId, Guid ReviewId, string? CriterionCode, string Title, string Severity, string Recommendation, string Evidence);
public record TaskCommentDto(Guid Id, string AuthorName, string Body, DateTime CreatedAt);
public record TaskEventDto(Guid Id, string ActorName, string? FromStatus, string ToStatus, string? Reason, DateTime CreatedAt);
public record TaskActionDto(string ToStatus, string Label, bool ReasonRequired);
public record TaskDto(Guid Id, Guid CourseId, string CourseTitle, Guid ElementId, string ElementType, string ElementTitle, string? ElementSourceUrl,
    string Title, string Instructions, string Priority, string Status, string? BlockedFromStatus, Guid Revision, Guid AssigneeId, string AssigneeName,
    string ReviewerName, DateTime CreatedAt, DateTime? DueAt, DateTime? ResolvedAt, SnapshotDto SourceSnapshot, Guid CurrentSnapshotId, bool SnapshotChanged,
    bool IsDemoCourse, IReadOnlyList<TaskFindingSummaryDto> Findings, IReadOnlyList<TaskCommentDto> Comments, IReadOnlyList<TaskEventDto> Events,
    IReadOnlyList<TaskActionDto> AvailableActions, bool CanComment);
public record TaskSummaryDto(Guid Id, Guid ElementId, string ElementType, string ElementTitle, string Title, string Priority, string Status,
    string AssigneeName, DateTime? DueAt, DateTime CreatedAt, int FindingCount, bool SnapshotChanged, bool IsMine);
public record ComparisonDto(SnapshotDto Source, SnapshotDto Latest, bool Changed, IReadOnlyList<DiffLine> TextDiff, IReadOnlyList<string> MetadataChanges);

// AssigneeId is not accepted from the client: in this phase the only valid assignee is the course owner, so the server derives it.
public record CreateTaskInput(Guid CourseId, string? Title, string? Instructions, string? Priority, DateTime? DueAt, Guid[]? FindingIds);
public record TaskTransitionInput(Guid Revision, string? ToStatus, string? Reason);
// No revision check: comments are additive and should not conflict with concurrent status changes or other comments.
public record TaskCommentInput(string? Body);
public record SimulateUpdateInput(Guid Revision, string? PlainText);

public interface ITaskService
{
    Task<ServiceResult<IReadOnlyList<TaskSummaryDto>>> ListForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<IReadOnlyList<TaskSummaryDto>> ListMineAsync(CurrentUser user, string? status, CancellationToken ct);
    Task<ServiceResult<TaskDto>> GetAsync(CurrentUser user, Guid taskId, CancellationToken ct);
    Task<ServiceResult<TaskDto>> CreateAsync(CurrentUser user, CreateTaskInput input, CancellationToken ct);
    Task<ServiceResult<TaskDto>> TransitionAsync(CurrentUser user, Guid taskId, TaskTransitionInput input, CancellationToken ct);
    Task<ServiceResult<TaskDto>> CommentAsync(CurrentUser user, Guid taskId, TaskCommentInput input, CancellationToken ct);
    Task<ServiceResult<ComparisonDto>> GetComparisonAsync(CurrentUser user, Guid taskId, CancellationToken ct);
    // Demo courses have no live Canvas connection; this simulates an instructor's out-of-band edit so the review-task-resolve loop can be exercised offline.
    Task<ServiceResult<TaskDto>> SimulateUpdateAsync(CurrentUser user, Guid taskId, SimulateUpdateInput input, CancellationToken ct);
}

public static class TaskValidation
{
    public static Dictionary<string, string[]> Create(string? title, string? instructions, string? priority, Guid[]? findingIds)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) errors["title"] = ["Enter a title of 1–200 characters."];
        if ((instructions?.Length ?? 0) > 4000) errors["instructions"] = ["Instructions are limited to 4,000 characters."];
        if (priority is null || !TaskVocabulary.Priorities.Contains(priority)) errors["priority"] = ["Choose Low, Medium, High or Critical."];
        if (findingIds is not { Length: > 0 }) errors["findingIds"] = ["Choose at least one confirmed finding."];
        else if (findingIds.Distinct().Count() != findingIds.Length) errors["findingIds"] = ["Findings must be listed only once."];
        return errors;
    }
    public static Dictionary<string, string[]> Transition(string? toStatus, string? reason, bool reasonRequired)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(toStatus)) errors["toStatus"] = ["Choose a target status."];
        if (reasonRequired && string.IsNullOrWhiteSpace(reason)) errors["reason"] = ["This transition requires a short reason."];
        if ((reason?.Length ?? 0) > 2000) errors["reason"] = ["Reason is limited to 2,000 characters."];
        return errors;
    }
    public static Dictionary<string, string[]> Comment(string? body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000) errors["body"] = ["Enter a comment of 1–4,000 characters."];
        return errors;
    }
}
