using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class TaskTests
{
    private const string SharedCourse = "30000000-0000-0000-0000-000000000001", PrivateCourse = "30000000-0000-0000-0000-000000000002";
    internal static readonly Guid OwnerId = Guid.Parse("20000000-0000-0000-0000-000000000001"), ReviewerId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    // Reviews a course element end to end, confirms every automated proposal, rates one criterion, and publishes.
    // Returns the element id and the confirmed finding ids available to build a revision task from.
    internal static async Task<(Guid ElementId, Guid[] FindingIds)> PublishedFindings(HttpClient ctl, string course, string elementType)
    {
        var elementId = await ReviewTests.ElementId(ctl, course, elementType);
        var review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{elementId}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        foreach (var finding in review.Findings)
            review = await ReviewTests.Ok(await ctl.PatchAsJsonAsync($"/api/v1/findings/{finding.Id}", new { revision = review.Revision, status = "Confirmed" }));
        review = await ReviewTests.Ok(await ctl.PutAsJsonAsync($"/api/v1/reviews/{review.Id}/criteria/{review.Rubric.Criteria[0].Id}",
            new { revision = review.Revision, outcome = "Rated", rating = 2, comment = "", evidence = "" }));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision }));
        return (elementId, review.Findings.Select(f => f.Id).ToArray());
    }
    internal static async Task<TaskDto> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }
    internal static Task<HttpResponseMessage> Transition(HttpClient c, TaskDto t, string toStatus, string? reason = null) =>
        c.PostAsJsonAsync($"/api/v1/revision-tasks/{t.Id}/transition", new { revision = t.Revision, toStatus, reason });
    internal static Task<HttpResponseMessage> Create(HttpClient c, string course, Guid[] findingIds, string priority = "High") =>
        c.PostAsJsonAsync("/api/v1/revision-tasks", new { courseId = Guid.Parse(course), title = "Fix accessibility issues", instructions = "Please address the findings below.", priority, findingIds });

    [Fact] public async Task Create_normalizes_a_timezone_naive_due_date_to_utc_instead_of_failing()
    {
        // Regression: a browser date input sends a timezone-naive string (no trailing Z/offset), which deserializes
        // as DateTimeKind.Unspecified. PostgreSQL's timestamptz columns reject that Kind outright unless the server normalizes it first.
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var (_, findingIds) = await PublishedFindings(ctl, SharedCourse, "page");
        var task = await Ok(await ctl.PostAsJsonAsync("/api/v1/revision-tasks",
            new { courseId = Guid.Parse(SharedCourse), title = "x", instructions = "x", priority = "Low", dueAt = "2026-11-20", findingIds = findingIds[..1] }));
        Assert.Equal(DateTimeKind.Utc, task.DueAt!.Value.Kind);
    }

    [Fact] public async Task Full_task_lifecycle_runs_from_review_to_resolution_with_demo_simulated_update_and_audit_trail()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var (elementId, findingIds) = await PublishedFindings(ctl, SharedCourse, "assignment");
        Assert.True(findingIds.Length >= 2);

        var created = await Ok(await Create(ctl, SharedCourse, findingIds[..2]));
        Assert.Equal(("Draft", 2, elementId), (created.Status, created.Findings.Count, created.ElementId));
        // The assignee is derived from the course owner; it is not accepted as client input.
        Assert.Equal((OwnerId, "Alex Morgan"), (created.AssigneeId, created.AssigneeName));
        Assert.Equal(["NeedsRevision", "Withdrawn"], created.AvailableActions.Select(a => a.ToStatus).OrderBy(x => x));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/revision-tasks/{created.Id}")).StatusCode);
        Assert.DoesNotContain(created.Id, (await owner.GetFromJsonAsync<TaskSummaryDto[]>("/api/v1/revision-tasks"))!.Select(t => t.Id));
        Assert.Contains(created.Id, (await ctl.GetFromJsonAsync<TaskSummaryDto[]>("/api/v1/revision-tasks"))!.Select(t => t.Id));
        Assert.Contains(created.Id, (await ctl.GetFromJsonAsync<TaskSummaryDto[]>($"/api/v1/courses/{SharedCourse}/revision-tasks"))!.Select(t => t.Id));
        Assert.DoesNotContain(created.Id, (await owner.GetFromJsonAsync<TaskSummaryDto[]>($"/api/v1/courses/{SharedCourse}/revision-tasks"))!.Select(t => t.Id));

        var assigned = await Ok(await Transition(ctl, created, "NeedsRevision"));
        Assert.Equal("NeedsRevision", assigned.Status);
        var visible = await Ok(await owner.GetAsync($"/api/v1/revision-tasks/{assigned.Id}"));
        Assert.Contains("InProgress", visible.AvailableActions.Select(a => a.ToStatus));
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(owner, visible, "Withdrawn", "not allowed")).StatusCode);

        var inProgress = await Ok(await Transition(owner, visible, "InProgress"));
        Assert.Equal("InProgress", inProgress.Status);
        inProgress = await Ok(await owner.PostAsJsonAsync($"/api/v1/revision-tasks/{inProgress.Id}/comments", new { body = "Working on the alt text now." }));
        Assert.Single(inProgress.Comments);
        inProgress = await Ok(await ctl.PostAsJsonAsync($"/api/v1/revision-tasks/{inProgress.Id}/comments", new { body = "Thanks — ping me when ready." }));
        Assert.Equal(2, inProgress.Comments.Count);
        Assert.Equal("Alex Morgan", inProgress.Comments[0].AuthorName);
        Assert.Equal("Jordan Lee", inProgress.Comments[1].AuthorName);

        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PostAsJsonAsync($"/api/v1/revision-tasks/{inProgress.Id}/simulate-update",
            new { revision = inProgress.Revision, plainText = "Reviewer cannot simulate the instructor's edit." })).StatusCode);
        var updated = await Ok(await owner.PostAsJsonAsync($"/api/v1/revision-tasks/{inProgress.Id}/simulate-update",
            new { revision = inProgress.Revision, plainText = "Added descriptive alt text to every image and header cells to the points table." }));
        Assert.True(updated.SnapshotChanged);
        Assert.NotEqual(updated.SourceSnapshot.Id, updated.CurrentSnapshotId);

        var ready = await Ok(await Transition(owner, updated, "ReadyForReview"));
        Assert.Equal("ReadyForReview", ready.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(owner, ready, "Resolved")).StatusCode);

        var comparison = (await (await ctl.GetAsync($"/api/v1/revision-tasks/{ready.Id}/comparison")).Content.ReadFromJsonAsync<ComparisonDto>())!;
        Assert.True(comparison.Changed);
        Assert.Contains(comparison.TextDiff, d => d.Kind == "Added" && d.Text.Contains("descriptive alt text"));
        Assert.Contains(comparison.TextDiff, d => d.Kind == "Removed");

        var resolved = await Ok(await Transition(ctl, ready, "Resolved"));
        Assert.Equal("Resolved", resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Empty(resolved.AvailableActions);
        Assert.Equal(HttpStatusCode.Conflict, (await Transition(owner, resolved, "InProgress")).StatusCode);
        // History survives resolution: a closed task can still receive a closing comment.
        var afterClose = await Ok(await owner.PostAsJsonAsync($"/api/v1/revision-tasks/{resolved.Id}/comments", new { body = "Thanks for confirming!" }));
        Assert.Equal(3, afterClose.Comments.Count);

        Assert.Equal(
            [(null as string, "Draft"), ("Draft", "NeedsRevision"), ("NeedsRevision", "InProgress"), ("InProgress", "ReadyForReview"), ("ReadyForReview", "Resolved")],
            afterClose.Events.Select(e => (e.FromStatus, e.ToStatus)));

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();
        foreach (var action in new[] { "RevisionTaskCreated", "RevisionTaskNeedsRevision", "RevisionTaskInProgress", "RevisionTaskReadyForReview", "RevisionTaskResolved", "RevisionTaskCommentAdded", "DemoContentSimulatedUpdate" })
            Assert.Contains(action, actions);
        var events = await db.TaskEvents.Where(e => e.TaskId == resolved.Id).ToListAsync();
        var first = events.First();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { db.TaskEvents.Remove(first); await db.SaveChangesAsync(); });
        db.ChangeTracker.Clear();
        if (db.Database.IsNpgsql())
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"TaskEvents\" WHERE \"Id\" = {0}", first.Id));
    }

    [Fact] public async Task Transitions_enforce_reason_requirements_terminal_lock_and_blocked_resume()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var (_, findingIds) = await PublishedFindings(ctl, SharedCourse, "page");
        var task = await Ok(await Create(ctl, SharedCourse, findingIds[..1]));

        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(ctl, task, "Withdrawn")).StatusCode); // reason required
        var withdrawn = await Ok(await Transition(ctl, task, "Withdrawn", "Superseded by a clearer task."));
        Assert.Equal("Withdrawn", withdrawn.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Transition(ctl, withdrawn, "NeedsRevision")).StatusCode);

        var second = await Ok(await Create(ctl, SharedCourse, findingIds[..1]));
        second = await Ok(await Transition(ctl, second, "NeedsRevision"));
        second = await Ok(await Transition(owner, second, "InProgress"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(owner, second, "Blocked")).StatusCode); // reason required
        var blocked = await Ok(await Transition(owner, second, "Blocked", "Waiting on the Canvas admin to restore edit access."));
        Assert.Equal(("Blocked", "InProgress"), (blocked.Status, blocked.BlockedFromStatus));
        Assert.Equal(["Resume"], blocked.AvailableActions.Select(a => a.ToStatus));
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(ctl, blocked, "InProgress")).StatusCode); // can't skip Resume
        var resumed = await Ok(await Transition(ctl, blocked, "Resume"));
        Assert.Equal((("InProgress"), (string?)null), (resumed.Status, resumed.BlockedFromStatus));
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(ctl, resumed, "Resume")).StatusCode); // not blocked

        var ready = await Ok(await Transition(owner, resumed, "ReadyForReview"));
        var changesRequested = await Ok(await Transition(ctl, ready, "ChangesRequested", "The table still has no header cells."));
        Assert.Equal("ChangesRequested", changesRequested.Status);
        // Spec allows resubmitting directly without passing back through InProgress.
        var resubmitted = await Ok(await Transition(owner, changesRequested, "ReadyForReview"));
        Assert.Equal("ReadyForReview", resubmitted.Status);
    }

    [Fact] public async Task Create_validates_role_course_access_assignee_and_finding_eligibility()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f); using var admin = ApiTests.Client(f); using var anonymous = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(admin, "admin");
        var (pageId, pageFindings) = await PublishedFindings(ctl, SharedCourse, "page");
        var (_, assignmentFindings) = await PublishedFindings(ctl, SharedCourse, "assignment");

        Assert.Equal(HttpStatusCode.Forbidden, (await Create(owner, SharedCourse, pageFindings[..1])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(admin, SharedCourse, pageFindings[..1])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(anonymous, SharedCourse, pageFindings[..1])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(ctl, PrivateCourse, pageFindings[..1])).StatusCode); // not shared with this reviewer
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(ctl, SharedCourse, [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(ctl, SharedCourse, [Guid.NewGuid()])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(ctl, SharedCourse, [.. pageFindings[..1], .. assignmentFindings[..1]])).StatusCode); // mixed elements

        // An empty body resolves to an all-default CourseId, which matches no accessible course (checked before field validation, like review start).
        var rejectedProposal = await ctl.PostAsJsonAsync("/api/v1/revision-tasks", new { });
        Assert.Equal(HttpStatusCode.NotFound, rejectedProposal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(ctl, SharedCourse, pageFindings[..1], priority: "Severe")).StatusCode);

        var task = await Ok(await Create(ctl, SharedCourse, pageFindings[..1]));
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(ctl, SharedCourse, pageFindings[..1])).StatusCode); // already tracked by this open task
        var withdrawn = await Ok(await Transition(ctl, task, "Withdrawn", "starting over"));
        Assert.Equal("Withdrawn", withdrawn.Status);
        // Once the prior task is terminal, its findings can be re-grouped into a new one.
        Assert.Equal(HttpStatusCode.OK, (await Create(ctl, SharedCourse, pageFindings[..1])).StatusCode);
    }

    [Fact] public async Task Simulate_update_is_demo_only_and_requires_current_revision()
    {
        using var f = new Factory(true); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var connectResponse = await owner.PostAsJsonAsync("/api/v1/canvas/connections", new { baseUrl = "https://canvas.example.test", token = Guid.NewGuid().ToString("N") });
        var connectionId = (await connectResponse.Content.ReadFromJsonAsync<CanvasConnectionDto>())!.Id;
        // A missing rubric and a missing image alt give the audit something detectable to confirm.
        var evidence = new ContentEvidence(1, [new ImageEvidence(0, null, false)], [], [], [], new AssignmentEvidence(false, 10, true));
        f.Canvas.CourseData = new(new("77", "Imported course", "IMP 77"),
            [new("9", "assignment", "Imported assignment", "Original text.", "https://canvas.example.test/courses/77/assignments/9", "Published", Evidence: evidence)], []);
        var imported = (await (await owner.PostAsJsonAsync("/api/v1/courses/import", new { connectionId, sourceCourseId = "77" })).Content.ReadFromJsonAsync<CanvasImportResult>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/v1/courses/{imported.CourseId}/shares", new { reviewerId = ReviewerId })).StatusCode);

        var (_, findingIds) = await PublishedFindings(ctl, imported.CourseId!.Value.ToString(), "assignment");
        var task = await Ok(await Create(ctl, imported.CourseId!.Value.ToString(), findingIds[..1]));
        task = await Ok(await Transition(ctl, task, "NeedsRevision"));
        var rejected = await owner.PostAsJsonAsync($"/api/v1/revision-tasks/{task.Id}/simulate-update", new { revision = task.Revision, plainText = "Fixed in Canvas directly." });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); // this is a Canvas-origin course, not a demo course

        // A demo course rejects a simulated update against a stale revision instead (409, checked before the demo-only rule applies).
        var (_, demoFindingIds) = await PublishedFindings(ctl, SharedCourse, "page");
        var demoTask = await Ok(await Create(ctl, SharedCourse, demoFindingIds[..1]));
        demoTask = await Ok(await Transition(ctl, demoTask, "NeedsRevision"));
        var stale = await owner.PostAsJsonAsync($"/api/v1/revision-tasks/{demoTask.Id}/simulate-update", new { revision = Guid.NewGuid(), plainText = "x" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var accepted = await Ok(await owner.PostAsJsonAsync($"/api/v1/revision-tasks/{demoTask.Id}/simulate-update", new { revision = demoTask.Revision, plainText = "Fixed in the demo course." }));
        Assert.True(accepted.SnapshotChanged);
    }
}
