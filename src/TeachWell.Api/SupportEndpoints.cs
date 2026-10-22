using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

public static class SupportEndpoints
{
    public static void MapSupport(this RouteGroupBuilder api)
    {
        var read = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        // scope=mine (default) is the caller's own tickets; scope=queue is the tenant-wide CTL triage queue. One route, like
        // /courses?role=, with an inline role check for the restricted branch — matching how GET /courses already Forbids
        // a role mismatch rather than deferring to the service layer.
        read.MapGet("/support/tickets", async (string? scope, string? status, ISupportService s, Actor u, CancellationToken ct) =>
        {
            if (scope != "queue") return Results.Ok(await s.ListMineAsync(u.User, status, ct));
            return u.User.Roles.Contains(Roles.Reviewer) ? await ReviewEndpoints.Run(() => s.ListQueueAsync(u.User, status, ct)) : Results.Forbid();
        });
        read.MapGet("/support/tickets/{id:guid}", (Guid id, ISupportService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetAsync(u.User, id, ct)));
        read.MapPost("/support/tickets", (CreateTicketInput input, ISupportService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateAsync(u.User, input, ct)));
        read.MapPost("/support/tickets/{id:guid}/transition", (Guid id, TicketTransitionInput input, ISupportService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.TransitionAsync(u.User, id, input, ct)));
        read.MapPost("/support/tickets/{id:guid}/comments", (Guid id, TicketCommentInput input, ISupportService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.CommentAsync(u.User, id, input, ct)));
        read.MapGet("/knowledge", async (IKnowledgeService s, Actor u, CancellationToken ct) => Results.Ok(await s.ListAsync(u.User, ct)));
        read.MapGet("/knowledge/{slug}", (string slug, IKnowledgeService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetAsync(u.User, slug, ct)));
        // The college's changes to the built-in glossary; the web app merges them with its own terms (ADR 015).
        read.MapGet("/glossary", async (IGlossaryService s, Actor u, CancellationToken ct) => Results.Ok(await s.ListAsync(u.User, ct)));
        read.MapGet("/workshops", async (IWorkshopService s, Actor u, CancellationToken ct) => Results.Ok(await s.ListAsync(u.User, ct)));
        read.MapPost("/workshops/{id:guid}/register", (Guid id, IWorkshopService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.RegisterAsync(u.User, id, ct)));
        read.MapDelete("/workshops/{id:guid}/register", (Guid id, IWorkshopService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CancelRegistrationAsync(u.User, id, ct)));

        // CTL-only actions get their own role-gated group, consistent with revision task creation and course sharing: a role
        // mismatch is rejected by the authorization middleware (403) before reaching the service, not inferred from a 404.
        var ctl = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        ctl.MapPatch("/support/tickets/{id:guid}/assignment", (Guid id, TicketAssignInput input, ISupportService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.AssignAsync(u.User, id, input, ct)));
        ctl.MapPatch("/support/tickets/{id:guid}/recommendation", (Guid id, TicketRecommendInput input, ISupportService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.RecommendAsync(u.User, id, input, ct)));
        ctl.MapPost("/knowledge", (CreateArticleInput input, IKnowledgeService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateAsync(u.User, input, ct)));
        ctl.MapPut("/knowledge/{id:guid}", (Guid id, UpdateArticleInput input, IKnowledgeService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.UpdateAsync(u.User, id, input, ct)));
        ctl.MapPut("/glossary/{termId}", (string termId, GlossaryEntryInput input, IGlossaryService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.SaveAsync(u.User, termId, input, ct)));
        ctl.MapDelete("/glossary/{termId}", (string termId, IGlossaryService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.ResetAsync(u.User, termId, ct)));
        ctl.MapPost("/workshops", (CreateWorkshopInput input, IWorkshopService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateAsync(u.User, input, ct)));
    }
}
