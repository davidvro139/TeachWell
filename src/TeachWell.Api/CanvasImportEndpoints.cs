using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;

namespace TeachWell.Api;

public static class CanvasImportEndpoints
{
    public static void MapCanvasImports(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/canvas").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer));
        group.MapGet("/connections/{id:guid}/courses", async (Guid id, HttpContext c, IIdentityProvider identity, ICanvasImportService service, CanvasIntegrationOptions options, CancellationToken ct) =>
        {
            var user = await identity.GetUserAsync(c.User);
            if (user is null) return Results.Unauthorized();
            if (!user.Roles.Any(r => r is Roles.Instructor or Roles.Reviewer)) return Results.Forbid();
            if (!options.Enabled) return CanvasEndpoints.Disabled();
            try
            {
                var result = await service.ListCoursesAsync(user, id, ct);
                if (result.NotFound) return Results.NotFound();
                return result.Result.Error == CanvasError.None ? Results.Ok(result.Result.Courses) : CanvasEndpoints.Error(result.Result.Error);
            }
            catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 409, title: "The connection changed. Refresh and try again."); }
        }).RequireRateLimiting("canvas");
        api.MapPost("/courses/import", async (ImportRequest input, HttpContext c, IIdentityProvider identity, ICanvasImportService service, CanvasIntegrationOptions options, CancellationToken ct) =>
        {
            var user = await identity.GetUserAsync(c.User);
            if (user is null) return Results.Unauthorized();
            if (!user.Roles.Any(r => r is Roles.Instructor or Roles.Reviewer)) return Results.Forbid();
            if (!options.Enabled) return CanvasEndpoints.Disabled();
            if (input.ConnectionId == Guid.Empty || string.IsNullOrEmpty(input.SourceCourseId) || input.SourceCourseId.Length > 80 || input.SourceCourseId[0] == '0' || input.SourceCourseId.Any(ch => ch is < '0' or > '9'))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["course"] = ["Choose a connection and a positive Canvas course ID."] });
            try
            {
                var result = await service.ImportAsync(user, input.ConnectionId, input.SourceCourseId, ct);
                if (result.NotFound) return Results.NotFound();
                return result.Error == CanvasError.None ? Results.Ok(result) : CanvasEndpoints.Error(result.Error);
            }
            catch (DbUpdateException) { return Results.Problem(statusCode: 409, title: "The course or connection changed. Refresh and try importing again."); }
        }).RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).RequireRateLimiting("canvas");
    }
    public sealed record ImportRequest(Guid ConnectionId, string SourceCourseId);
}
