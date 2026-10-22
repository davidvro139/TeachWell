using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

public static class TaskEndpoints
{
    public static void MapRevisionTasks(this RouteGroupBuilder api)
    {
        var read = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        // In this phase, only the caller's own queue (assignee or creating reviewer) is listed; a course-wide view is below.
        read.MapGet("/revision-tasks", async (string? status, ITaskService s, Actor u, CancellationToken ct) => Results.Ok(await s.ListMineAsync(u.User, status, ct)));
        read.MapGet("/courses/{id:guid}/revision-tasks", (Guid id, ITaskService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.ListForCourseAsync(u.User, id, ct)));
        read.MapGet("/revision-tasks/{id:guid}", (Guid id, ITaskService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetAsync(u.User, id, ct)));
        read.MapGet("/revision-tasks/{id:guid}/comparison", (Guid id, ITaskService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetComparisonAsync(u.User, id, ct)));
        read.MapPost("/revision-tasks/{id:guid}/transition", (Guid id, TaskTransitionInput input, ITaskService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.TransitionAsync(u.User, id, input, ct)));
        read.MapPost("/revision-tasks/{id:guid}/comments", (Guid id, TaskCommentInput input, ITaskService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.CommentAsync(u.User, id, input, ct)));
        read.MapPost("/revision-tasks/{id:guid}/simulate-update", (Guid id, SimulateUpdateInput input, ITaskService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.SimulateUpdateAsync(u.User, id, input, ct)));

        var write = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        write.MapPost("/revision-tasks", (CreateTaskInput input, ITaskService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateAsync(u.User, input, ct)));
    }
}
