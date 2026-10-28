using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

public static class StandardSetEndpoints
{
    public static void MapStandardSets(this RouteGroupBuilder api)
    {
        // The standards library is CTL-only; instructors read the guidance that applies to them through reviews and course standards.
        var ctl = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        // Actor is bound so the filter can reject deactivated accounts, as on every other route.
        ctl.MapGet("/review-checks", (IStandardSetService s, Actor _) => s.Catalog());
        ctl.MapGet("/standard-sets", (IStandardSetService s, Actor u, CancellationToken ct) => s.ListAsync(u.User, ct));
        ctl.MapPost("/standard-sets", (CreateStandardSetInput input, IStandardSetService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateAsync(u.User, input, ct)));
        ctl.MapGet("/standard-sets/{id:guid}/versions/{versionId:guid}", (Guid id, Guid versionId, IStandardSetService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.GetVersionAsync(u.User, id, versionId, ct)));
        ctl.MapPost("/standard-sets/{id:guid}/versions", (Guid id, IStandardSetService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.CreateDraftAsync(u.User, id, ct)));
        ctl.MapPut("/standard-sets/{id:guid}/versions/{versionId:guid}", (Guid id, Guid versionId, UpdateStandardSetDraftInput input, IStandardSetService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.UpdateDraftAsync(u.User, id, versionId, input, ct)));
        ctl.MapPost("/standard-sets/{id:guid}/versions/{versionId:guid}/publish", (Guid id, Guid versionId, RevisionInput input, IStandardSetService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.PublishAsync(u.User, id, versionId, input.Revision, ct)));
        // A dry run changes nothing, but it reads course content, so it is POST (CSRF-protected) and requires course access.
        ctl.MapPost("/standard-sets/{id:guid}/versions/{versionId:guid}/preview", (Guid id, Guid versionId, StandardsPreviewInput input, IStandardSetService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.PreviewAsync(u.User, id, versionId, input.CourseId, ct)));
        // Slice 6: which checks instructors set aside, and why, to inform the next standard-set version.
        ctl.MapGet("/standards/feedback", (IGuideService s, Actor u, CancellationToken ct) => s.FeedbackAsync(u.User, ct));
        // Knowledge-base articles and workshops offered on guide steps, per check.
        ctl.MapGet("/standards/resources", (IGuideService s, Actor u, CancellationToken ct) => s.ListResourcesAsync(u.User, ct));
        ctl.MapPost("/standards/resources", (CheckResourceInput input, IGuideService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.LinkResourceAsync(u.User, input, ct)));
        ctl.MapDelete("/standards/resources/{linkId:guid}", (Guid linkId, IGuideService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.UnlinkResourceAsync(u.User, linkId, ct)));
        ctl.MapPut("/courses/{id:guid}/classification", (Guid id, CourseClassificationInput input, IStandardSetService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.ClassifyCourseAsync(u.User, id, input, ct)));

        var read = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        read.MapGet("/courses/{id:guid}/standards", (Guid id, IStandardSetService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetForCourseAsync(u.User, id, ct)));
        // The improvement guide: anyone with course access reads it; only the owner records progress (others get 404).
        read.MapGet("/courses/{id:guid}/guide", (Guid id, IGuideService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetAsync(u.User, id, ct)));
        // The instructor's "Start here" checklist: their own courses only (ADR 016).
        read.MapGet("/getting-started", async (IGuideService s, Actor u, CancellationToken ct) => Results.Ok(await s.GettingStartedAsync(u.User, ct)));
        read.MapPut("/courses/{id:guid}/guide/progress", (Guid id, GuideProgressInput input, IGuideService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.MarkAsync(u.User, id, input, ct)));
        // The syllabus. Anyone with course access reads it; only the owner pastes or clears it (others 404), as with
        // the guide's progress marks. Pasting exists because this college's syllabi are in Simple Syllabus (ADR 019).
        read.MapGet("/courses/{id:guid}/syllabus", (Guid id, ISyllabusService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetAsync(u.User, id, ct)));
        read.MapPut("/courses/{id:guid}/syllabus", (Guid id, SyllabusTextInput input, ISyllabusService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.SaveAsync(u.User, id, input, ct)));
        read.MapDelete("/courses/{id:guid}/syllabus", (Guid id, ISyllabusService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.ClearAsync(u.User, id, ct)));

        // The accessibility conformance report: the same course access as the guide, which it is a projection of (ROADMAP item 1).
        read.MapGet("/courses/{id:guid}/conformance", (Guid id, IConformanceService s, Actor u, CancellationToken ct) => ReviewEndpoints.Run(() => s.GetAsync(u.User, id, ct)));
        read.MapGet("/courses/{id:guid}/conformance/export", async (Guid id, IConformanceService s, Actor u, CancellationToken ct) =>
            await s.GetAsync(u.User, id, ct) is { Status: ResultStatus.Ok, Value: { } r } ? ConformanceCsv(r) : Results.NotFound());
    }

    // One row per success criterion, with the report's scope and date in the header rows so a downloaded file cannot
    // be read as a bare compliance claim.
    private static IResult ConformanceCsv(CourseConformanceDto r)
    {
        var csv = new Csv("Criterion", "Name", "Level", "State", "Checks related", "Checks that ran", "Fully covered", "Open", "Fixed", "Not applicable");
        foreach (var c in r.Criteria)
            csv.Row(c.Number, c.Name, c.Level, c.State, $"{c.Checks.Count}", $"{c.ChecksRan}", c.FullyCovered ? "Yes" : "No",
                $"{c.OpenPlaces}", $"{c.DonePlaces}", $"{c.NotApplicablePlaces}");
        csv.Row();
        csv.Row("Course", $"{r.CourseCode} {r.CourseTitle}");
        csv.Row("Reference standard", r.Standard);
        csv.Row("Criteria in scope", $"{r.CriteriaInScope}");
        csv.Row("Criteria needing manual review", $"{r.CriteriaOutOfScope}");
        csv.Row("Standards applied", $"{r.Standards.Name}{(r.Standards.VersionNumber is { } v ? $" v{v}" : "")}");
        csv.Row("Content captured", r.ContentAsOf?.ToString("u") ?? "not recorded");
        csv.Row("Report generated", r.AsOf.ToString("u"));
        if (r.Endorsement is not null) csv.Row("Endorsement", r.Endorsement);
        csv.Row("Scope", Conformance.Caveat);
        foreach (var limitation in r.Limitations) csv.Row("Limitation", limitation);
        return csv.File($"conformance-{Csv.Slug(r.CourseCode)}.csv");
    }
}
