using Microsoft.EntityFrameworkCore;
using TeachWell.Infrastructure;

namespace TeachWell.Api;

public static class CourseContentEndpoints
{
    public const int MaxPreviewBatch = 100;

    public static void MapCourseContent(this RouteGroupBuilder api)
    {
        // Formatted previews of course content, fetched in batches as modules are opened, so the course page does not carry every
        // preview up front. Same access rule as the course itself: owner or an explicit share, within the tenant. The HTML is the
        // allowlisted re-serialization made at import (CanvasText.Preview), never Canvas markup.
        api.MapGet("/courses/{id:guid}/previews", async (Guid id, string? ids, AppDbContext db, Actor actor, CancellationToken ct) =>
        {
            var user = actor.User;
            if (!await db.Courses.AnyAsync(c => c.Id == id && c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)), ct))
                return Results.NotFound();
            var parsed = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => Guid.TryParse(x, out var g) ? g : (Guid?)null).ToList();
            if (parsed.Count is 0 or > MaxPreviewBatch || parsed.Any(g => g is null))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = [$"Provide 1–{MaxPreviewBatch} element ids, separated by commas."] });
            var wanted = parsed.Select(g => g!.Value).Distinct().ToList();
            // Ids from other courses are simply not returned.
            var previews = await db.Elements.AsNoTracking().Where(e => e.CourseId == id && wanted.Contains(e.Id))
                .Select(e => new { e.Id, e.PreviewHtml }).ToDictionaryAsync(e => e.Id, e => e.PreviewHtml, ct);
            return Results.Ok(previews);
        }).RequireAuthorization().AddEndpointFilter(Actor.Require);
    }
}
