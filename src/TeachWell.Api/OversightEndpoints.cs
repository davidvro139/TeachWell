using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Api;

// Phase 11 (ADR 014): oversight for CTL reviewers and Academic administrators; role management for SystemAdmin.
public static class OversightEndpoints
{
    public static void MapOversight(this RouteGroupBuilder api)
    {
        var view = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer, Roles.AcademicAdmin)).AddEndpointFilter(Actor.Require);
        view.MapGet("/oversight", (string? department, string? program, IOversightService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.GetAsync(u.User, department, program, ct)));
        // The course table as CSV, with the same filters; figures only, no names.
        view.MapGet("/oversight/export", async (string? department, string? program, IOversightService s, Actor u, CancellationToken ct) =>
            await s.GetAsync(u.User, department, program, ct) is { Status: ResultStatus.Ok, Value: { } o } ? OversightCsv(o) : Results.NotFound());

        var ctl = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        ctl.MapPost("/oversight/refresh", async (IOversightService s, Actor u, CancellationToken ct) =>
            await s.RefreshAsync(u.User, ct) is { Status: ResultStatus.Ok } r ? Results.Ok(new { checkedCourses = r.Value }) : Results.NotFound());

        var admin = api.MapGroup("").RequireAuthorization(p => p.RequireRole(Roles.Admin)).AddEndpointFilter(Actor.Require);
        admin.MapGet("/admin/users", (IAdminUserService s, Actor u, CancellationToken ct) => s.ListAsync(u.User, ct));
        admin.MapGet("/admin/audit", (string? action, Guid? actorId, DateTime? from, DateTime? to, DateTime? before, IAuditService s, Actor u, CancellationToken ct) =>
            ReviewEndpoints.Run(() => s.ListAsync(u.User, new AuditQuery(action, actorId, from, to, before), ct)));
        admin.MapPut("/admin/users/{userId:guid}/roles", (Guid userId, RoleChangeInput input, IAdminUserService s, PublicDemoOptions demo, Actor u, CancellationToken ct) =>
            demo.Enabled ? Task.FromResult(PublicDemoOptions.Disabled("Role changes are")) : ReviewEndpoints.Run(() => s.ChangeRoleAsync(u.User, userId, input, ct)));
    }

    private static IResult OversightCsv(OversightDto o)
    {
        var csv = new Csv("Course code", "Course title", "Department", "Program", "Delivery mode", "Readiness", "Open guide steps", "Required open steps",
            "Last checked (UTC)", "Endorsed on (UTC)", "Endorsement current until (UTC)");
        static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";
        foreach (var c in o.CourseRows)
            csv.Row(c.Code, c.Title, c.Department ?? "", c.Program ?? "", c.DeliveryMode ?? "", c.ReadinessLabel, c.OpenSteps?.ToString() ?? "",
                c.RequiredOpenSteps?.ToString() ?? "", c.LastChecked?.ToString("yyyy-MM-dd HH:mm") ?? "", Date(c.EndorsedAt), Date(c.EndorsementValidUntil));
        // The WCAG rollup follows the course rows in the same file, under its own header, with every caveat beneath it
        // so a downloaded figure cannot be circulated as a conformance claim (ADR 018 amendment).
        csv.Row();
        csv.Row("Success criterion", "Name", "Level", "Courses with open work", "Open places", "Checks");
        foreach (var c in o.Conformance)
            csv.Row(c.Number, c.Name, c.Level, $"{c.CoursesAffected}", $"{c.OpenPlaces}", string.Join(" ", c.Checks));
        csv.Row();
        csv.Row("Scope", o.Scope.Description);
        csv.Row("Courses in scope", $"{o.Courses}");
        csv.Row("Courses checked", $"{o.CoursesChecked}");
        csv.Row("Generated (UTC)", o.Scope.GeneratedAt.ToString("u"));
        foreach (var caveat in o.Scope.Caveats) csv.Row("Caveat", caveat);

        var scope = string.Join("-", new[] { o.Department, o.Program }.OfType<string>().Select(Csv.Slug));
        return csv.File($"teachwell-oversight-{(scope.Length == 0 ? "college" : scope)}-{o.Scope.GeneratedAt:yyyy-MM-dd}.csv");
    }
}
