using TeachWell.Domain;

namespace TeachWell.Application;

// Null unless the current viewer currently has access to the linked course; access can be revoked after a ticket is filed.
public record TicketCourseLinkDto(Guid CourseId, string CourseTitle, string CourseCode, Guid? ElementId, string? ElementTitle, Guid? RevisionTaskId, string? RevisionTaskTitle);
public record ResourceRefDto(Guid Id, string Title, string? Slug);
public record SupportCommentDto(Guid Id, string AuthorName, string Body, DateTime CreatedAt);
public record SupportEventDto(Guid Id, string ActorName, string? FromStatus, string ToStatus, string? Reason, DateTime CreatedAt);
public record TicketActionDto(string ToStatus, string Label, bool ReasonRequired);
public record SupportTicketDto(Guid Id, Guid RequesterId, string RequesterName, Guid? AssigneeId, string? AssigneeName, string Category,
    string Priority, string Status, string Title, string Description, DateTime CreatedAt, DateTime? ClosedAt, Guid Revision,
    TicketCourseLinkDto? LinkedCourse, ResourceRefDto? RecommendedArticle, ResourceRefDto? RecommendedWorkshop,
    IReadOnlyList<SupportCommentDto> Comments, IReadOnlyList<SupportEventDto> Events, IReadOnlyList<TicketActionDto> AvailableActions,
    bool CanComment, bool CanAssign, bool CanRecommend);
public record SupportTicketSummaryDto(Guid Id, string Title, string Category, string Priority, string Status, string RequesterName,
    string? AssigneeName, DateTime CreatedAt, bool HasCourseLink, bool IsMine);

// Origin is null for the support page and tasks, or "Guide" when sent from an improvement-guide step (counted in oversight).
public record CreateTicketInput(Guid? CourseId, Guid? ElementId, Guid? RevisionTaskId, string? Category, string? Title, string? Description, string? Priority,
    string? Origin = null);
public record TicketTransitionInput(Guid Revision, string? ToStatus, string? Reason);
public record TicketCommentInput(string? Body);
public record TicketAssignInput(Guid Revision, Guid? AssigneeId);
public record TicketRecommendInput(Guid Revision, Guid? ArticleId, Guid? WorkshopId);

public interface ISupportService
{
    Task<IReadOnlyList<SupportTicketSummaryDto>> ListMineAsync(CurrentUser user, string? status, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<SupportTicketSummaryDto>>> ListQueueAsync(CurrentUser user, string? status, CancellationToken ct);
    Task<ServiceResult<SupportTicketDto>> GetAsync(CurrentUser user, Guid ticketId, CancellationToken ct);
    Task<ServiceResult<SupportTicketDto>> CreateAsync(CurrentUser user, CreateTicketInput input, CancellationToken ct);
    Task<ServiceResult<SupportTicketDto>> TransitionAsync(CurrentUser user, Guid ticketId, TicketTransitionInput input, CancellationToken ct);
    Task<ServiceResult<SupportTicketDto>> CommentAsync(CurrentUser user, Guid ticketId, TicketCommentInput input, CancellationToken ct);
    Task<ServiceResult<SupportTicketDto>> AssignAsync(CurrentUser user, Guid ticketId, TicketAssignInput input, CancellationToken ct);
    Task<ServiceResult<SupportTicketDto>> RecommendAsync(CurrentUser user, Guid ticketId, TicketRecommendInput input, CancellationToken ct);
}

public record KnowledgeArticleDto(Guid Id, string Title, string Slug, string Body, string[] Tags, string Status, int Version,
    string AuthorName, DateTime CreatedAt, DateTime? UpdatedAt, DateTime? PublishedAt, Guid Revision, bool CanEdit);
public record KnowledgeArticleSummaryDto(Guid Id, string Title, string Slug, string[] Tags, string Status, DateTime? PublishedAt);
public record CreateArticleInput(string? Title, string? Body, string[]? Tags);
public record UpdateArticleInput(Guid Revision, string? Title, string? Body, string[]? Tags, string? Status);

public interface IKnowledgeService
{
    Task<IReadOnlyList<KnowledgeArticleSummaryDto>> ListAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<KnowledgeArticleDto>> GetAsync(CurrentUser user, string slug, CancellationToken ct);
    Task<ServiceResult<KnowledgeArticleDto>> CreateAsync(CurrentUser user, CreateArticleInput input, CancellationToken ct);
    Task<ServiceResult<KnowledgeArticleDto>> UpdateAsync(CurrentUser user, Guid id, UpdateArticleInput input, CancellationToken ct);
}

public record WorkshopRegistrationDto(Guid UserId, string DisplayName, string Status, DateTime RegisteredAt);
public record WorkshopDto(Guid Id, string Title, string Description, DateTime StartsAt, DateTime EndsAt, string Modality, int? Capacity,
    int RegisteredCount, int WaitlistedCount, string? MyStatus, bool CanManage, IReadOnlyList<WorkshopRegistrationDto> Registrations);
public record CreateWorkshopInput(string? Title, string? Description, DateTime? StartsAt, DateTime? EndsAt, string? Modality, int? Capacity);

public interface IWorkshopService
{
    Task<IReadOnlyList<WorkshopDto>> ListAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<WorkshopDto>> CreateAsync(CurrentUser user, CreateWorkshopInput input, CancellationToken ct);
    Task<ServiceResult<WorkshopDto>> RegisterAsync(CurrentUser user, Guid workshopId, CancellationToken ct);
    Task<ServiceResult<WorkshopDto>> CancelRegistrationAsync(CurrentUser user, Guid workshopId, CancellationToken ct);
}

public static class SupportValidation
{
    public static Dictionary<string, string[]> CreateTicket(string? category, string? title, string? description, string? priority)
    {
        var errors = new Dictionary<string, string[]>();
        if (category is null || !SupportVocabulary.Categories.Contains(category)) errors["category"] = ["Choose Canvas, Accessibility, CourseDesign, Multimedia or Other."];
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) errors["title"] = ["Enter a title of 1–200 characters."];
        if (string.IsNullOrWhiteSpace(description) || description.Length > 4000) errors["description"] = ["Describe what you need help with (1–4,000 characters)."];
        if (priority is null || !SupportVocabulary.Priorities.Contains(priority)) errors["priority"] = ["Choose Low, Medium, High or Critical."];
        return errors;
    }
    public static Dictionary<string, string[]> Transition(string? toStatus, string? reason, bool reasonRequired)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(toStatus)) errors["toStatus"] = ["Choose a target status."];
        if (reasonRequired && string.IsNullOrWhiteSpace(reason)) errors["reason"] = ["This transition requires a short note."];
        if ((reason?.Length ?? 0) > 2000) errors["reason"] = ["Note is limited to 2,000 characters."];
        return errors;
    }
    public static Dictionary<string, string[]> Comment(string? body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000) errors["body"] = ["Enter a comment of 1–4,000 characters."];
        return errors;
    }
    public static Dictionary<string, string[]> Article(string? title, string? body, string[]? tags)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160) errors["title"] = ["Enter a title of 1–160 characters."];
        if (string.IsNullOrWhiteSpace(body) || body.Length > 20000) errors["body"] = ["Enter article text (1–20,000 characters)."];
        if (tags is { Length: > 10 } || (tags?.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 40) ?? false)) errors["tags"] = ["Use up to 10 tags of 1–40 characters each."];
        return errors;
    }
    public static Dictionary<string, string[]> Workshop(string? title, string? description, DateTime? startsAt, DateTime? endsAt, string? modality, int? capacity)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) errors["title"] = ["Enter a title of 1–200 characters."];
        if ((description?.Length ?? 0) > 4000) errors["description"] = ["Description is limited to 4,000 characters."];
        if (startsAt is null) errors["startsAt"] = ["Choose a start time."];
        if (endsAt is null || (startsAt is not null && endsAt <= startsAt)) errors["endsAt"] = ["Choose an end time after the start time."];
        if (modality is null || !WorkshopVocabulary.Modalities.Contains(modality)) errors["modality"] = ["Choose InPerson, Online or Hybrid."];
        if (capacity is < 1) errors["capacity"] = ["Capacity must be at least 1, or left blank for no limit."];
        return errors;
    }
}
