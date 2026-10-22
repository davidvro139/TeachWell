using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

// Phase 10 (ADR 013). Course-level routes check course access and ownership in the service (404 otherwise); the queue, taking and
// deciding are CTL-only.
public static class ReadinessEndpoints
{
    public static void MapReadiness(this RouteGroupBuilder api)
    {
        var read = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        read.MapGet("/courses/{id:guid}/readiness", (Guid id, IReadinessService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetForCourseAsync(u.User, id, ct)));
        read.MapPost("/courses/{id:guid}/readiness", (Guid id, ReadinessRequestInput input, IReadinessService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.RequestAsync(u.User, id, input, ct)));
        read.MapPost("/readiness/{requestId:guid}/withdraw", (Guid requestId, ReadinessRevisionInput input, IReadinessService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.WithdrawAsync(u.User, requestId, input, ct)));

        // Notifications are personal and open to every signed-in role.
        var anyone = api.MapGroup("").RequireAuthorization().AddEndpointFilter(Actor.Require);
        anyone.MapGet("/notifications", (INotificationService s, Actor u, CancellationToken ct) => s.ListAsync(u.User, ct));
        anyone.MapPost("/notifications/{notificationId:guid}/read", (Guid notificationId, INotificationService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.MarkReadAsync(u.User, notificationId, ct)));
        anyone.MapPost("/notifications/read-all", (INotificationService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.MarkReadAsync(u.User, null, ct)));

        var ctl = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        ctl.MapGet("/readiness/queue", (IReadinessService s, Actor u, CancellationToken ct) => s.QueueAsync(u.User, ct));
        ctl.MapPost("/readiness/{requestId:guid}/take", (Guid requestId, ReadinessRevisionInput input, IReadinessService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.TakeAsync(u.User, requestId, input, ct)));
        ctl.MapPost("/readiness/{requestId:guid}/decide", (Guid requestId, ReadinessDecisionInput input, IReadinessService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.DecideAsync(u.User, requestId, input, ct)));
    }
}
