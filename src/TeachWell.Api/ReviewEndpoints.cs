using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

public static class ReviewEndpoints
{
    internal const string Conflict = "This record changed at the same time. Reload it and try again.";

    public static void MapReviews(this RouteGroupBuilder api)
    {
        var rubrics = api.MapGroup("/rubrics").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        rubrics.MapGet("", (IRubricService s, Actor u, CancellationToken ct) => s.ListAsync(u.User, ct));
        rubrics.MapPost("", (CreateRubricInput input, IRubricService s, Actor u, CancellationToken ct) => Run(() => s.CreateAsync(u.User, input, ct)));
        rubrics.MapGet("/{id:guid}/versions/{versionId:guid}", (Guid id, Guid versionId, IRubricService s, Actor u, CancellationToken ct) => Run(() => s.GetVersionAsync(u.User, id, versionId, ct)));
        rubrics.MapPost("/{id:guid}/versions", (Guid id, IRubricService s, Actor u, CancellationToken ct) => Run(() => s.CreateDraftAsync(u.User, id, ct)));
        rubrics.MapPut("/{id:guid}/versions/{versionId:guid}", (Guid id, Guid versionId, UpdateRubricDraftInput input, IRubricService s, Actor u, CancellationToken ct) =>
            Run(() => s.UpdateDraftAsync(u.User, id, versionId, input, ct)));
        rubrics.MapPost("/{id:guid}/versions/{versionId:guid}/publish", (Guid id, Guid versionId, RevisionInput input, IRubricService s, Actor u, CancellationToken ct) =>
            Run(() => s.PublishAsync(u.User, id, versionId, input.Revision, ct)));

        var read = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        read.MapGet("/courses/{id:guid}/reviews", (Guid id, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.ListForCourseAsync(u.User, id, ct)));
        read.MapGet("/reviews/{id:guid}", (Guid id, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.GetAsync(u.User, id, ct)));
        read.MapGet("/reviews/{id:guid}/previews/{snapshotId:guid}", (Guid id, Guid snapshotId, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.GetPreviewAsync(u.User, id, snapshotId, ct)));

        var write = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        write.MapPost("/courses/{id:guid}/course-reviews", (Guid id, StartReviewInput input, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.StartCourseReviewAsync(u.User, id, input, ct)));
        write.MapPost("/elements/{id:guid}/reviews", (Guid id, StartReviewInput input, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.StartAsync(u.User, id, input, ct)));
        write.MapPut("/reviews/{id:guid}/criteria/{criterionId:guid}", (Guid id, Guid criterionId, EvaluationInput input, IReviewService s, Actor u, CancellationToken ct) =>
            Run(() => s.EvaluateAsync(u.User, id, criterionId, input, ct)));
        write.MapPost("/reviews/{id:guid}/audit", (Guid id, RevisionInput input, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.RunChecksAsync(u.User, id, input.Revision, ct)));
        write.MapPost("/reviews/{id:guid}/findings", (Guid id, FindingInput input, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.AddFindingAsync(u.User, id, input, ct)));
        write.MapPatch("/findings/{id:guid}", (Guid id, FindingUpdateInput input, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.UpdateFindingAsync(u.User, id, input, ct)));
        write.MapPost("/reviews/{id:guid}/publish", (Guid id, PublishReviewInput input, IReviewService s, Actor u, CancellationToken ct) => Run(() => s.PublishAsync(u.User, id, input, ct)));
    }

    internal static async Task<IResult> Run<T>(Func<Task<ServiceResult<T>>> action)
    {
        try
        {
            var result = await action();
            return result.Status switch
            {
                ResultStatus.Ok => Results.Ok(result.Value),
                ResultStatus.NotFound => Results.NotFound(),
                ResultStatus.Invalid => Results.ValidationProblem(result.Errors!),
                _ => Results.Problem(statusCode: 409, title: result.Message)
            };
        }
        catch (DbUpdateException) { return Results.Problem(statusCode: 409, title: Conflict); }
    }
}

// Binds the active local user; the endpoint filter turns a deactivated or missing account into 401.
public sealed record Actor(CurrentUser? Current)
{
    public CurrentUser User => Current ?? throw new InvalidOperationException("Actor filter was not applied.");
    public static async ValueTask<Actor> BindAsync(HttpContext context) =>
        new(await context.RequestServices.GetRequiredService<IIdentityProvider>().GetUserAsync(context.User));
    public static async ValueTask<object?> Require(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.Arguments.OfType<Actor>().FirstOrDefault() is { Current: not null } ? await next(context) : Results.Unauthorized();
}
