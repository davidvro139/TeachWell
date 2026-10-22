using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

public static class CourseSharingEndpoints
{
    public static void MapCourseSharing(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/courses/{id:guid}").RequireAuthorization(p => p.RequireRole(Roles.Instructor));
        group.MapGet("/reviewers", async (Guid id, HttpContext c, IIdentityProvider identity, ICourseSharingService sharing, CancellationToken ct) =>
        {
            var actor = await identity.GetUserAsync(c.User);
            if (actor is null) return Results.Unauthorized();
            var reviewers = await sharing.ListReviewersAsync(actor, id, ct);
            return reviewers is null ? Results.NotFound() : Results.Ok(reviewers);
        });
        group.MapGet("/shares", async (Guid id, HttpContext c, IIdentityProvider identity, ICourseSharingService sharing, CancellationToken ct) =>
        {
            var actor = await identity.GetUserAsync(c.User);
            if (actor is null) return Results.Unauthorized();
            var shares = await sharing.ListSharesAsync(actor, id, ct);
            return shares is null ? Results.NotFound() : Results.Ok(shares);
        });
        group.MapPost("/shares", async (Guid id, ShareRequest input, HttpContext c, IIdentityProvider identity, ICourseSharingService sharing, CancellationToken ct) =>
        {
            var actor = await identity.GetUserAsync(c.User);
            if (actor is null) return Results.Unauthorized();
            if (input.ReviewerId == Guid.Empty) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reviewerId"] = ["Choose a reviewer."] });
            try { return ToResult(await sharing.GrantAsync(actor, id, input.ReviewerId, ct)); }
            catch (DbUpdateException) { return Results.Problem(statusCode: 409, title: "Access changed at the same time. Refresh the sharing list and try again."); }
        });
        group.MapDelete("/shares/{reviewerId:guid}", async (Guid id, Guid reviewerId, HttpContext c, IIdentityProvider identity, ICourseSharingService sharing, CancellationToken ct) =>
        {
            var actor = await identity.GetUserAsync(c.User);
            if (actor is null) return Results.Unauthorized();
            try { return ToResult(await sharing.RevokeAsync(actor, id, reviewerId, ct)); }
            catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 409, title: "Access changed at the same time. Refresh the sharing list and try again."); }
        });
    }

    private static IResult ToResult(SharingResult result) => result switch
    {
        SharingResult.Success => Results.NoContent(),
        SharingResult.NotFound => Results.NotFound(),
        _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["reviewerId"] = ["Choose an active CTL reviewer from this workspace."] })
    };

    public record ShareRequest(Guid ReviewerId);
}
