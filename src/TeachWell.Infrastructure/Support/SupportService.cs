using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Support;

public sealed class SupportService(AppDbContext db) : ISupportService
{
    private const string Stale = "This ticket changed since you opened it. Reload it and try again.";
    private const string NotAvailable = "This transition is not available from the ticket's current status.";
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);
    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));

    // A ticket is visible to its requester and, matching the shared triage-queue model, to any CTL reviewer in the tenant —
    // whether or not that reviewer currently has access to any course the ticket happens to reference.
    private async Task<(SupportTicket? Ticket, bool IsResponder, bool IsRequester)> Visible(CurrentUser user, Guid ticketId, bool tracking, CancellationToken ct)
    {
        var tickets = tracking ? db.SupportTickets : db.SupportTickets.AsNoTracking();
        var ticket = await tickets.AsSplitQuery().Include(t => t.Comments).Include(t => t.Events).SingleOrDefaultAsync(t => t.Id == ticketId && t.TenantId == user.TenantId, ct);
        if (ticket is null) return (null, false, false);
        var isRequester = ticket.RequesterId == user.Id;
        var isResponder = IsReviewer(user);
        return !isRequester && !isResponder ? (null, false, false) : (ticket, isResponder, isRequester);
    }

    private async Task<bool> CanReference(CurrentUser user, Guid? courseId, Guid? elementId, Guid? revisionTaskId, CancellationToken ct)
    {
        if (revisionTaskId is { } taskId)
        {
            var task = await db.RevisionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId && t.TenantId == user.TenantId, ct);
            if (task is null || courseId is { } c0 && c0 != task.CourseId || elementId is { } e0 && e0 != task.ElementId) return false;
            if (task.Status == TaskVocabulary.Draft && task.ReviewerId != user.Id) return false;
            courseId = task.CourseId; elementId ??= task.ElementId;
        }
        if (courseId is not { } courseIdValue) return elementId is null;
        if (!await Accessible(user).AnyAsync(c => c.Id == courseIdValue, ct)) return false;
        return elementId is not { } elementIdValue || await db.Elements.AnyAsync(e => e.Id == elementIdValue && e.CourseId == courseIdValue, ct);
    }

    public async Task<IReadOnlyList<SupportTicketSummaryDto>> ListMineAsync(CurrentUser user, string? status, CancellationToken ct)
    {
        var query = db.SupportTickets.AsNoTracking().Where(t => t.TenantId == user.TenantId && (t.RequesterId == user.Id || t.AssigneeId == user.Id));
        if (!string.IsNullOrEmpty(status)) query = query.Where(t => t.Status == status);
        return await Summaries(query, user, ct);
    }

    public async Task<ServiceResult<IReadOnlyList<SupportTicketSummaryDto>>> ListQueueAsync(CurrentUser user, string? status, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<IReadOnlyList<SupportTicketSummaryDto>>.NotFound();
        var query = db.SupportTickets.AsNoTracking().Where(t => t.TenantId == user.TenantId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(t => t.Status == status);
        return ServiceResult<IReadOnlyList<SupportTicketSummaryDto>>.Ok(await Summaries(query, user, ct));
    }

    private async Task<IReadOnlyList<SupportTicketSummaryDto>> Summaries(IQueryable<SupportTicket> query, CurrentUser user, CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        var names = await UserNames(rows.SelectMany(t => new[] { (Guid?)t.RequesterId, t.AssigneeId }), ct);
        return rows.OrderByDescending(t => t.CreatedAt).Select(t => new SupportTicketSummaryDto(t.Id, t.Title, t.Category, t.Priority, t.Status,
            names.GetValueOrDefault(t.RequesterId, "Unknown"), t.AssigneeId is { } a ? names.GetValueOrDefault(a, "Unknown") : null, t.CreatedAt,
            t.CourseId is not null, t.RequesterId == user.Id || t.AssigneeId == user.Id)).ToList();
    }

    private Task<Dictionary<Guid, string>> UserNames(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var distinct = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return db.Users.AsNoTracking().Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    public async Task<ServiceResult<SupportTicketDto>> GetAsync(CurrentUser user, Guid ticketId, CancellationToken ct)
    {
        var (ticket, isResponder, isRequester) = await Visible(user, ticketId, false, ct);
        return ticket is null ? ServiceResult<SupportTicketDto>.NotFound() : ServiceResult<SupportTicketDto>.Ok(await Dto(ticket, user, isResponder, isRequester, ct));
    }

    public async Task<ServiceResult<SupportTicketDto>> CreateAsync(CurrentUser user, CreateTicketInput input, CancellationToken ct)
    {
        var errors = SupportValidation.CreateTicket(input.Category, input.Title, input.Description, input.Priority);
        if (input.Origin is not (null or "" or SupportVocabulary.GuideOrigin)) errors["origin"] = ["Leave origin empty, or use Guide for a request from the improvement guide."];
        if (!await CanReference(user, input.CourseId, input.ElementId, input.RevisionTaskId, ct))
            errors["courseId"] = ["Choose a course, element and task you currently have access to, consistent with each other."];
        if (errors.Count > 0) return ServiceResult<SupportTicketDto>.Invalid(errors);
        // CanReference resolves revisionTaskId to its real courseId/elementId; re-derive rather than trust the raw input.
        var (courseId, elementId) = await ResolvedLinks(input, ct);
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(), TenantId = user.TenantId, RequesterId = user.Id, CourseId = courseId, ElementId = elementId,
            RevisionTaskId = input.RevisionTaskId, Category = input.Category!, Priority = input.Priority!, Origin = input.Origin ?? "", Title = input.Title!.Trim(),
            Description = input.Description!.Trim()
        };
        ticket.Events.Add(SupportEvent.Create(ticket.Id, user.Id, null, SupportVocabulary.New, null));
        db.SupportTickets.Add(ticket);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = ticket.Id, Action = "SupportTicketCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<SupportTicketDto>.Ok(await Dto(ticket, user, IsReviewer(user), true, ct));
    }
    private async Task<(Guid? CourseId, Guid? ElementId)> ResolvedLinks(CreateTicketInput input, CancellationToken ct)
    {
        if (input.RevisionTaskId is { } taskId)
        {
            var task = await db.RevisionTasks.AsNoTracking().Select(t => new { t.Id, t.CourseId, t.ElementId }).SingleAsync(t => t.Id == taskId, ct);
            return (task.CourseId, task.ElementId);
        }
        return (input.CourseId, input.ElementId);
    }

    public async Task<ServiceResult<SupportTicketDto>> TransitionAsync(CurrentUser user, Guid ticketId, TicketTransitionInput input, CancellationToken ct)
    {
        var (ticket, isResponder, isRequester) = await Visible(user, ticketId, true, ct);
        if (ticket is null) return ServiceResult<SupportTicketDto>.NotFound();
        if (ticket.Revision != input.Revision) return ServiceResult<SupportTicketDto>.Conflict(Stale);
        var from = ticket.Status;
        var edge = SupportVocabulary.From(from).FirstOrDefault(t => t.To == input.ToStatus);
        if (edge is null) return ServiceResult<SupportTicketDto>.Invalid("toStatus", NotAvailable);
        var allowed = edge.Actor switch { SupportVocabulary.Requester => isRequester, SupportVocabulary.Responder => isResponder, _ => isRequester || isResponder };
        if (!allowed) return ServiceResult<SupportTicketDto>.Invalid("toStatus", "Only the requester or a CTL reviewer may make this change.");
        var errors = SupportValidation.Transition(input.ToStatus, input.Reason, edge.ReasonRequired);
        if (errors.Count > 0) return ServiceResult<SupportTicketDto>.Invalid(errors);
        db.Entry(ticket).Property(t => t.Revision).OriginalValue = input.Revision;
        ticket.Revision = Guid.NewGuid(); ticket.Status = edge.To;
        ticket.ClosedAt = edge.To == SupportVocabulary.Closed ? DateTime.UtcNow : null;
        ticket.Events.Add(SupportEvent.Create(ticket.Id, user.Id, from, edge.To, input.Reason?.Trim()));
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = ticket.Id, Action = "SupportTicket" + edge.To });
        await db.SaveChangesAsync(ct);
        return ServiceResult<SupportTicketDto>.Ok(await Dto(ticket, user, isResponder, isRequester, ct));
    }

    public async Task<ServiceResult<SupportTicketDto>> CommentAsync(CurrentUser user, Guid ticketId, TicketCommentInput input, CancellationToken ct)
    {
        var errors = SupportValidation.Comment(input.Body);
        if (errors.Count > 0) return ServiceResult<SupportTicketDto>.Invalid(errors);
        var (ticket, isResponder, isRequester) = await Visible(user, ticketId, true, ct);
        if (ticket is null) return ServiceResult<SupportTicketDto>.NotFound();
        ticket.Comments.Add(new() { Id = Guid.NewGuid(), TicketId = ticket.Id, AuthorId = user.Id, Body = input.Body!.Trim() });
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = ticket.Id, Action = "SupportTicketCommentAdded" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<SupportTicketDto>.Ok(await Dto(ticket, user, isResponder, isRequester, ct));
    }

    public async Task<ServiceResult<SupportTicketDto>> AssignAsync(CurrentUser user, Guid ticketId, TicketAssignInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<SupportTicketDto>.NotFound();
        var (ticket, isResponder, isRequester) = await Visible(user, ticketId, true, ct);
        if (ticket is null) return ServiceResult<SupportTicketDto>.NotFound();
        if (ticket.Revision != input.Revision) return ServiceResult<SupportTicketDto>.Conflict(Stale);
        if (input.AssigneeId is { } assigneeId)
        {
            var eligible = await (from u in db.Users
                join assignment in db.UserRoles on u.Id equals assignment.UserId
                join role in db.Roles on assignment.RoleId equals role.Id
                where u.TenantId == user.TenantId && u.IsActive && role.Name == Roles.Reviewer && u.Id == assigneeId
                select u.Id).AnyAsync(ct);
            if (!eligible) return ServiceResult<SupportTicketDto>.Invalid("assigneeId", "Choose an active CTL reviewer in this workspace.");
        }
        db.Entry(ticket).Property(t => t.Revision).OriginalValue = input.Revision;
        ticket.Revision = Guid.NewGuid(); ticket.AssigneeId = input.AssigneeId;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = ticket.Id, SubjectUserId = input.AssigneeId, Action = input.AssigneeId is null ? "SupportTicketUnassigned" : "SupportTicketAssigned" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<SupportTicketDto>.Ok(await Dto(ticket, user, isResponder, isRequester, ct));
    }

    public async Task<ServiceResult<SupportTicketDto>> RecommendAsync(CurrentUser user, Guid ticketId, TicketRecommendInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<SupportTicketDto>.NotFound();
        var (ticket, isResponder, isRequester) = await Visible(user, ticketId, true, ct);
        if (ticket is null) return ServiceResult<SupportTicketDto>.NotFound();
        if (ticket.Revision != input.Revision) return ServiceResult<SupportTicketDto>.Conflict(Stale);
        if (input.ArticleId is { } articleId && !await db.KnowledgeArticles.AnyAsync(a => a.Id == articleId && a.TenantId == user.TenantId, ct))
            return ServiceResult<SupportTicketDto>.Invalid("articleId", "Choose a knowledge article in this workspace.");
        if (input.WorkshopId is { } workshopId && !await db.Workshops.AnyAsync(w => w.Id == workshopId && w.TenantId == user.TenantId, ct))
            return ServiceResult<SupportTicketDto>.Invalid("workshopId", "Choose a workshop in this workspace.");
        db.Entry(ticket).Property(t => t.Revision).OriginalValue = input.Revision;
        ticket.Revision = Guid.NewGuid(); ticket.RecommendedArticleId = input.ArticleId; ticket.RecommendedWorkshopId = input.WorkshopId;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = ticket.Id, Action = "SupportTicketRecommendationUpdated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<SupportTicketDto>.Ok(await Dto(ticket, user, isResponder, isRequester, ct));
    }

    private static readonly Dictionary<(string From, string To), string> Labels = new()
    {
        [(SupportVocabulary.New, SupportVocabulary.Triaged)] = "Mark triaged",
        [(SupportVocabulary.New, SupportVocabulary.InProgress)] = "Start working",
        [(SupportVocabulary.Triaged, SupportVocabulary.InProgress)] = "Start working",
        [(SupportVocabulary.InProgress, SupportVocabulary.WaitingOnInstructor)] = "Wait for instructor",
        [(SupportVocabulary.WaitingOnInstructor, SupportVocabulary.InProgress)] = "Resume work",
        [(SupportVocabulary.InProgress, SupportVocabulary.Resolved)] = "Resolve ticket",
        [(SupportVocabulary.WaitingOnInstructor, SupportVocabulary.Resolved)] = "Resolve ticket",
        [(SupportVocabulary.Resolved, SupportVocabulary.Closed)] = "Close ticket",
        [(SupportVocabulary.Resolved, SupportVocabulary.InProgress)] = "Reopen ticket",
        [(SupportVocabulary.Closed, SupportVocabulary.InProgress)] = "Reopen ticket",
    };

    private async Task<SupportTicketDto> Dto(SupportTicket ticket, CurrentUser viewer, bool isResponder, bool isRequester, CancellationToken ct)
    {
        var names = await UserNames([ticket.RequesterId, ticket.AssigneeId, .. ticket.Comments.Select(c => (Guid?)c.AuthorId), .. ticket.Events.Select(e => (Guid?)e.ActorId)], ct);
        TicketCourseLinkDto? linked = null;
        if (ticket.CourseId is { } courseId)
        {
            var course = await Accessible(viewer).Where(c => c.Id == courseId).Select(c => new { c.Title, c.Code }).SingleOrDefaultAsync(ct);
            if (course is not null)
            {
                var elementTitle = ticket.ElementId is { } elementId ? await db.Elements.Where(e => e.Id == elementId).Select(e => e.Title).SingleOrDefaultAsync(ct) : null;
                var taskTitle = ticket.RevisionTaskId is { } taskId ? await db.RevisionTasks.Where(t => t.Id == taskId).Select(t => t.Title).SingleOrDefaultAsync(ct) : null;
                linked = new(courseId, course.Title, course.Code, ticket.ElementId, elementTitle, ticket.RevisionTaskId, taskTitle);
            }
        }
        var article = ticket.RecommendedArticleId is { } aId ? await db.KnowledgeArticles.Where(a => a.Id == aId).Select(a => new ResourceRefDto(a.Id, a.Title, a.Slug)).SingleOrDefaultAsync(ct) : null;
        var workshop = ticket.RecommendedWorkshopId is { } wId ? await db.Workshops.Where(w => w.Id == wId).Select(w => new ResourceRefDto(w.Id, w.Title, null)).SingleOrDefaultAsync(ct) : null;
        var actions = SupportVocabulary.From(ticket.Status).Where(e => e.Actor switch { SupportVocabulary.Requester => isRequester, SupportVocabulary.Responder => isResponder, _ => isRequester || isResponder })
            .Select(e => new TicketActionDto(e.To, Labels.GetValueOrDefault((ticket.Status, e.To), e.To), e.ReasonRequired)).ToList();
        return new(ticket.Id, ticket.RequesterId, names.GetValueOrDefault(ticket.RequesterId, "Unknown"), ticket.AssigneeId,
            ticket.AssigneeId is { } assignee ? names.GetValueOrDefault(assignee, "Unknown") : null, ticket.Category, ticket.Priority, ticket.Status,
            ticket.Title, ticket.Description, ticket.CreatedAt, ticket.ClosedAt, ticket.Revision, linked, article, workshop,
            ticket.Comments.OrderBy(c => c.CreatedAt).Select(c => new SupportCommentDto(c.Id, names.GetValueOrDefault(c.AuthorId, "Unknown"), c.Body, c.CreatedAt)).ToList(),
            ticket.Events.OrderBy(e => e.CreatedAt).Select(e => new SupportEventDto(e.Id, names.GetValueOrDefault(e.ActorId, "Unknown"), e.FromStatus, e.ToStatus, e.Reason, e.CreatedAt)).ToList(),
            actions, isRequester || isResponder, isResponder, isResponder);
    }
}
