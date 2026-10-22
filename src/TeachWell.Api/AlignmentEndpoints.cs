using System.Text;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

public static class AlignmentEndpoints
{
    public static void MapAlignment(this RouteGroupBuilder api)
    {
        var read = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        read.MapGet("/courses/{id:guid}/competencies", (Guid id, ICompetencyService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.ListAsync(u.User, id, ct)));
        read.MapGet("/reports/course/{id:guid}/alignment", (Guid id, IReportService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.AlignmentAsync(u.User, id, ct)));
        read.MapGet("/reports/course/{id:guid}/quality", (Guid id, IReportService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.QualityAsync(u.User, id, ct)));
        read.MapGet("/reports/revisions", async (string? format, IReportService s, Actor u, CancellationToken ct) =>
        {
            var report = await s.RevisionsAsync(u.User, ct);
            return format == "csv" ? RevisionsCsv(report) : Results.Ok(report);
        });

        // Course owners manage their own course's competency metadata; CTL can read it (above) but not author it.
        var owner = api.MapGroup("/courses/{id:guid}/competencies").RequireAuthorization(p => p.RequireRole(Roles.Instructor)).AddEndpointFilter(Actor.Require);
        owner.MapPost("", (Guid id, CreateCompetencyInput input, ICompetencyService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateAsync(u.User, id, input, ct)));
        // Paste-and-approve: "parse" proposes competencies from pasted outcomes (saves nothing); "bulk" saves the approved list at once.
        owner.MapPost("/parse", (Guid id, OutcomePasteInput input, ICompetencyService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.ProposeAsync(u.User, id, input, ct)));
        owner.MapPost("/bulk", (Guid id, BulkCompetencyInput input, ICompetencyService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateManyAsync(u.User, id, input, ct)));
        // The mapping grid saves many map/unmap changes in one request.
        owner.MapPut("/mappings", (Guid id, MappingChangesInput input, ICompetencyService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.ApplyMappingsAsync(u.User, id, input, ct)));
        owner.MapPut("/{competencyId:guid}", (Guid id, Guid competencyId, UpdateCompetencyInput input, ICompetencyService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.UpdateAsync(u.User, id, competencyId, input, ct)));
        owner.MapDelete("/{competencyId:guid}", async (Guid id, Guid competencyId, ICompetencyService s, Actor u, CancellationToken ct) =>
        {
            var result = await s.DeleteAsync(u.User, id, competencyId, ct);
            return result.Status == ResultStatus.Ok ? Results.NoContent() : Results.NotFound();
        });
        owner.MapPost("/{competencyId:guid}/mappings", (Guid id, Guid competencyId, MappingInput input, ICompetencyService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.MapAsync(u.User, id, competencyId, input.ElementId, ct)));
        owner.MapDelete("/{competencyId:guid}/mappings/{elementId:guid}", (Guid id, Guid competencyId, Guid elementId, ICompetencyService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.UnmapAsync(u.User, id, competencyId, elementId, ct)));

        // CTL-only overview is role-gated at the route, matching revision task creation and course sharing (403 for the wrong role, not inferred from a 404).
        var ctl = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        ctl.MapGet("/reports/ctl/overview", async (IReportService s, Actor u, CancellationToken ct) => Results.Ok(await s.CtlOverviewAsync(u.User, ct)));
    }

    private static IResult RevisionsCsv(RevisionReportDto report)
    {
        var csv = new Csv("Title", "Course", "Status", "Priority", "DaysOpen");
        foreach (var t in report.OpenTasks) csv.Row(t.Title, t.CourseTitle, t.Status, t.Priority, t.DaysOpen.ToString());
        return Results.Text(csv.ToString(), "text/csv");
    }

    public sealed record MappingInput(Guid ElementId);
}
