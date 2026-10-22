namespace TeachWell.Domain;

public static class SupportVocabulary
{
    public const string New = "New", Triaged = "Triaged", InProgress = "InProgress", WaitingOnInstructor = "WaitingOnInstructor",
        Resolved = "Resolved", Closed = "Closed";
    public static readonly string[] All = [New, Triaged, InProgress, WaitingOnInstructor, Resolved, Closed];
    public static readonly string[] Categories = ["Canvas", "Accessibility", "CourseDesign", "Multimedia", "Other"];
    public const string GuideOrigin = "Guide";
    public static readonly string[] Priorities = ReviewVocabulary.Severities;

    public const string Requester = "Requester", Responder = "Responder", Either = "Either";
    public sealed record Transition(string To, string Actor, bool ReasonRequired);
    // Responder authority is tenant-wide (any active CTLReviewer), matching the shared triage-queue model: a ticket need not
    // already be assigned, or even reference a course the responder has access to, for a CTL to triage or work it.
    private static readonly Dictionary<string, Transition[]> Edges = new()
    {
        [New] = [new(Triaged, Responder, false), new(InProgress, Responder, false)],
        [Triaged] = [new(InProgress, Responder, false)],
        [InProgress] = [new(WaitingOnInstructor, Responder, true), new(Resolved, Responder, true)],
        [WaitingOnInstructor] = [new(InProgress, Either, false), new(Resolved, Responder, true)],
        // Reopen always returns to InProgress rather than the prior status: assignment/triage context is preserved, but a
        // ticket that is reopened is, by definition, being actively looked at again, not merely re-queued.
        [Resolved] = [new(Closed, Either, false), new(InProgress, Either, true)],
        [Closed] = [new(InProgress, Either, true)],
    };
    public static Transition[] From(string status) => Edges.TryGetValue(status, out var t) ? t : [];
}

public class SupportTicket
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RequesterId { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? ElementId { get; set; }
    public Guid? RevisionTaskId { get; set; }
    public string Category { get; set; } = "Other";
    public string Priority { get; set; } = "Medium";
    // Where the request came from: "" (the support page or a task) or "Guide" (an improvement-guide step), for oversight figures.
    public string Origin { get; set; } = "";
    public string Status { get; set; } = SupportVocabulary.New;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? RecommendedArticleId { get; set; }
    public Guid? RecommendedWorkshopId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public List<SupportComment> Comments { get; set; } = [];
    public List<SupportEvent> Events { get; set; } = [];
}
public class SupportComment
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
// Append-only, like TaskEvent: a ticket's handling history must not be rewritten after the fact.
public sealed class SupportEvent
{
    private SupportEvent() { }
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid ActorId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = "";
    public string? Reason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public static SupportEvent Create(Guid ticketId, Guid actorId, string? fromStatus, string toStatus, string? reason) => new()
    {
        Id = Guid.NewGuid(), TicketId = ticketId, ActorId = actorId, FromStatus = fromStatus, ToStatus = toStatus, Reason = reason, CreatedAt = DateTime.UtcNow
    };
}

// A lightweight, editable job aid; unlike rubrics and reviews, published articles are not immutable (editorial content, not scored evidence).
public class KnowledgeArticle
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Body { get; set; } = "";
    public string TagsJson { get; set; } = "[]";
    public string Status { get; set; } = ReviewVocabulary.Draft;
    public int Version { get; set; } = 1;
    public Guid AuthorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public static class WorkshopVocabulary
{
    public const string InPerson = "InPerson", Online = "Online", Hybrid = "Hybrid";
    public static readonly string[] Modalities = [InPerson, Online, Hybrid];
    public const string Registered = "Registered", Waitlisted = "Waitlisted", Cancelled = "Cancelled";
}
public class Workshop
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Modality { get; set; } = WorkshopVocabulary.Online;
    public int? Capacity { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<WorkshopRegistration> Registrations { get; set; } = [];
}
public class WorkshopRegistration
{
    public Guid WorkshopId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = WorkshopVocabulary.Registered;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
