using System.Net;
using System.Net.Http.Json;
using TeachWell.Application;
using Xunit;

public class AlignmentTests
{
    private const string SharedCourse = "30000000-0000-0000-0000-000000000001", PrivateCourse = "30000000-0000-0000-0000-000000000002";

    private static async Task<CompetencyDto> OkCompetency(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompetencyDto>())!;
    }
    private static Task<HttpResponseMessage> Create(HttpClient c, string course, string code = "CO-1", string title = "Explain accessible design choices") =>
        c.PostAsJsonAsync($"/api/v1/courses/{course}/competencies", new { code, title, description = "" });
    private static async Task<Guid> AssignmentId(HttpClient c, string course) =>
        (await (await c.GetAsync($"/api/v1/courses/{course}")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
        .GetProperty("elements").EnumerateArray().First(e => e.GetProperty("type").GetString() == "assignment").GetProperty("id").GetGuid();

    [Fact] public async Task Owner_manages_competencies_and_mappings_while_ctl_can_only_read()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var assignment = await AssignmentId(owner, SharedCourse);
        var page = await ReviewTests.ElementId(owner, SharedCourse, "page");

        // Jordan Lee holds the Instructor role too, so the route-level role gate admits the request; the service's own
        // ownership check (not a course they own) is what actually rejects it, matching Reviewers_admins_and_anonymous_users_cannot_manage_sharing.
        Assert.Equal(HttpStatusCode.NotFound, (await Create(ctl, SharedCourse)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(owner, SharedCourse, code: "lowercase not allowed")).StatusCode);
        var competency = await OkCompetency(await Create(owner, SharedCourse));
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(owner, SharedCourse)).StatusCode); // duplicate code

        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PostAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{competency.Id}/mappings", new { elementId = assignment })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{competency.Id}/mappings", new { elementId = page })).StatusCode); // pages are not assessment evidence
        competency = await OkCompetency(await owner.PostAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{competency.Id}/mappings", new { elementId = assignment }));
        Assert.Single(competency.Mappings);
        // Idempotent: mapping the same element twice does not duplicate.
        competency = await OkCompetency(await owner.PostAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{competency.Id}/mappings", new { elementId = assignment }));
        Assert.Single(competency.Mappings);

        var fromCtl = (await (await ctl.GetAsync($"/api/v1/courses/{SharedCourse}/competencies")).Content.ReadFromJsonAsync<CompetencyDto[]>())!;
        Assert.Single(fromCtl);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/courses/{PrivateCourse}/competencies")).StatusCode);

        var renamed = await OkCompetency(await owner.PutAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{competency.Id}", new { revision = competency.Revision, title = "Renamed", description = "" }));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{competency.Id}", new { revision = competency.Revision, title = "Stale", description = "" })).StatusCode);

        competency = await OkCompetency(await owner.PostAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{renamed.Id}/mappings", new { elementId = assignment }));
        Assert.Single(competency.Mappings);
        // The unmap route returns the updated competency directly (200), not 204, so a disposition can be confirmed in one call.
        competency = await OkCompetency(await owner.DeleteAsync($"/api/v1/courses/{SharedCourse}/competencies/{renamed.Id}/mappings/{assignment}"));
        Assert.Empty(competency.Mappings);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/courses/{SharedCourse}/competencies/{renamed.Id}")).StatusCode);
        Assert.Empty((await (await owner.GetAsync($"/api/v1/courses/{SharedCourse}/competencies")).Content.ReadFromJsonAsync<CompetencyDto[]>())!);
    }

    [Fact] public async Task Alignment_report_identifies_orphan_competencies_and_unmapped_assessments()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var assignment = await AssignmentId(owner, SharedCourse);
        var mapped = await OkCompetency(await Create(owner, SharedCourse, "CO-1", "Mapped competency"));
        await owner.PostAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies/{mapped.Id}/mappings", new { elementId = assignment });
        await Create(owner, SharedCourse, "CO-2", "Orphan competency");

        var report = (await (await ctl.GetAsync($"/api/v1/reports/course/{SharedCourse}/alignment")).Content.ReadFromJsonAsync<AlignmentReportDto>())!;
        Assert.Equal((2, 1, 1, 100m), (report.TotalCompetencies, report.TotalAssessments, report.MappedAssessmentCount, report.CoveragePercent));
        Assert.Equal("Orphan competency", Assert.Single(report.OrphanCompetencies).Title);
        Assert.Empty(report.UnmappedAssessments);
        Assert.NotEmpty(report.Scope.Caveats);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/reports/course/{PrivateCourse}/alignment")).StatusCode);
    }

    [Fact] public async Task Quality_report_averages_scores_and_tracks_outstanding_findings_until_a_task_resolves_them()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var (_, findingIds) = await TaskTests.PublishedFindings(ctl, SharedCourse, "assignment");
        Assert.True(findingIds.Length >= 2);

        var report = (await (await owner.GetAsync($"/api/v1/reports/course/{SharedCourse}/quality")).Content.ReadFromJsonAsync<CourseQualityReportDto>())!;
        Assert.Equal((1, 50m), (report.ElementsWithPublishedReview, report.ReviewedCoveragePercent)); // 1 of 2 reviewable elements (page, assignment) reviewed
        Assert.Equal(66.7m, report.AverageScorePercent); // STR-1 (weight 1) rated 2: 2*100/3
        Assert.Equal(3, report.OutstandingFindings.Count); // every proposed finding was confirmed and none has a task yet
        Assert.All(report.OutstandingFindings, f => Assert.False(f.HasOpenTask));

        var task = await TaskTests.Ok(await TaskTests.Create(ctl, SharedCourse, [findingIds[0]]));
        var afterOpenTask = (await (await owner.GetAsync($"/api/v1/reports/course/{SharedCourse}/quality")).Content.ReadFromJsonAsync<CourseQualityReportDto>())!;
        Assert.Equal(3, afterOpenTask.OutstandingFindings.Count); // an open (non-Resolved) task does not remove it from outstanding
        Assert.True(afterOpenTask.OutstandingFindings.Single(x => x.FindingId == findingIds[0]).HasOpenTask);

        task = await TaskTests.Ok(await TaskTests.Transition(ctl, task, "NeedsRevision"));
        task = await TaskTests.Ok(await TaskTests.Transition(owner, task, "InProgress"));
        task = await TaskTests.Ok(await TaskTests.Transition(owner, task, "ReadyForReview"));
        await TaskTests.Transition(ctl, task, "Resolved");
        var afterResolved = (await (await owner.GetAsync($"/api/v1/reports/course/{SharedCourse}/quality")).Content.ReadFromJsonAsync<CourseQualityReportDto>())!;
        Assert.DoesNotContain(afterResolved.OutstandingFindings, x => x.FindingId == findingIds[0]); // addressed, no longer outstanding
        Assert.Equal(2, afterResolved.OutstandingFindings.Count);
    }

    [Fact] public async Task Ctl_overview_is_reviewer_only_and_aggregates_accessible_courses_and_tenant_wide_support_themes()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/reports/ctl/overview")).StatusCode);

        await TaskTests.PublishedFindings(ctl, SharedCourse, "page");
        await owner.PostAsJsonAsync("/api/v1/support/tickets", new { category = "Accessibility", title = "x", description = "x", priority = "Low" });
        await owner.PostAsJsonAsync("/api/v1/support/tickets", new { category = "Accessibility", title = "y", description = "y", priority = "Low" });

        var overview = (await (await ctl.GetAsync("/api/v1/reports/ctl/overview")).Content.ReadFromJsonAsync<CtlOverviewReportDto>())!;
        Assert.Equal(1, overview.AccessibleCourses); // only the explicitly shared course
        Assert.Equal(1, overview.PublishedReviews);
        Assert.Equal(2, overview.SupportThemes.Single(t => t.Category == "Accessibility").Count);
    }

    [Fact] public async Task Revisions_report_is_scoped_to_the_callers_own_tasks_and_supports_csv_export()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var (_, findingIds) = await TaskTests.PublishedFindings(ctl, SharedCourse, "page");
        var task = await TaskTests.Ok(await TaskTests.Create(ctl, SharedCourse, findingIds[..1]));
        await TaskTests.Transition(ctl, task, "NeedsRevision");

        var ownerReport = (await (await owner.GetAsync("/api/v1/reports/revisions")).Content.ReadFromJsonAsync<RevisionReportDto>())!;
        Assert.Single(ownerReport.OpenTasks);
        Assert.Equal("NeedsRevision", ownerReport.TasksByStatus.Single().Status);
        var ctlReport = (await (await ctl.GetAsync("/api/v1/reports/revisions")).Content.ReadFromJsonAsync<RevisionReportDto>())!;
        Assert.Single(ctlReport.OpenTasks); // ctl opened it as reviewer, so it is also "theirs"

        var csv = await (await owner.GetAsync("/api/v1/reports/revisions?format=csv")).Content.ReadAsStringAsync();
        Assert.StartsWith("Title,Course,Status,Priority,DaysOpen", csv);
        Assert.Contains("NeedsRevision", csv);
    }
}
