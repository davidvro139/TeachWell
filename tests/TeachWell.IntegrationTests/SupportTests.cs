using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Infrastructure;
using Xunit;

public class SupportTests
{
    private const string SharedCourse = "30000000-0000-0000-0000-000000000001", PrivateCourse = "30000000-0000-0000-0000-000000000002";
    private static async Task<SupportTicketDto> OkTicket(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SupportTicketDto>())!;
    }
    private static async Task<KnowledgeArticleDto> OkArticle(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<KnowledgeArticleDto>())!;
    }
    private static async Task<WorkshopDto> OkWorkshop(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkshopDto>())!;
    }
    private static Task<HttpResponseMessage> CreateTicket(HttpClient c, object input) => c.PostAsJsonAsync("/api/v1/support/tickets", input);
    private static Task<HttpResponseMessage> Transition(HttpClient c, SupportTicketDto t, string toStatus, string? reason = null) =>
        c.PostAsJsonAsync($"/api/v1/support/tickets/{t.Id}/transition", new { revision = t.Revision, toStatus, reason });

    [Fact] public async Task Instructor_requests_help_from_a_task_ctl_triages_responds_recommends_and_closes_while_task_stays_independent()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var (_, findingIds) = await TaskTests.PublishedFindings(ctl, SharedCourse, "assignment");
        var task = await TaskTests.Ok(await TaskTests.Create(ctl, SharedCourse, findingIds[..1]));
        task = await TaskTests.Ok(await TaskTests.Transition(ctl, task, "NeedsRevision"));

        var ticket = await OkTicket(await CreateTicket(owner, new
        {
            revisionTaskId = task.Id, category = "Accessibility", title = "Need help with alt text wording",
            description = "Not sure what good alt text looks like for this image.", priority = "Medium"
        }));
        Assert.Equal(("New", task.ElementTitle, task.Id), (ticket.Status, ticket.LinkedCourse!.ElementTitle, ticket.LinkedCourse.RevisionTaskId));

        var queue = await ctl.GetFromJsonAsync<SupportTicketSummaryDto[]>("/api/v1/support/tickets?scope=queue");
        Assert.Contains(queue!, t => t.Id == ticket.Id);

        ticket = await OkTicket(await Transition(ctl, ticket, "Triaged"));
        ticket = await OkTicket(await Transition(ctl, ticket, "InProgress"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(ctl, ticket, "WaitingOnInstructor")).StatusCode); // reason required
        ticket = await OkTicket(await Transition(ctl, ticket, "WaitingOnInstructor", "Can you share the current alt text?"));

        ticket = await OkTicket(await owner.PostAsJsonAsync($"/api/v1/support/tickets/{ticket.Id}/comments", new { body = "It currently just says image1.png." }));
        Assert.Single(ticket.Comments);

        ticket = await OkTicket(await Transition(ctl, ticket, "InProgress"));
        ticket = await OkTicket(await ctl.PatchAsJsonAsync($"/api/v1/support/tickets/{ticket.Id}/recommendation",
            new { revision = ticket.Revision, articleId = DemoSeed.AltTextArticleId, workshopId = DemoSeed.RubricWorkshopId }));
        Assert.Equal(("Writing useful image alt text", "Writing clear assignment rubrics"), (ticket.RecommendedArticle!.Title, ticket.RecommendedWorkshop!.Title));

        ticket = await OkTicket(await Transition(ctl, ticket, "Resolved", "Shared the job aid; owner will update the image."));
        ticket = await OkTicket(await Transition(ctl, ticket, "Closed"));
        Assert.NotNull(ticket.ClosedAt);

        // Closing the ticket never touches the independent revision task.
        var unaffectedTask = await TaskTests.Ok(await owner.GetAsync($"/api/v1/revision-tasks/{task.Id}"));
        Assert.Equal("NeedsRevision", unaffectedTask.Status);

        var reopened = await OkTicket(await Transition(owner, ticket, "InProgress", "Still seeing the placeholder text."));
        Assert.Null(reopened.ClosedAt);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();
        foreach (var action in new[] { "SupportTicketCreated", "SupportTicketTriaged", "SupportTicketInProgress", "SupportTicketWaitingOnInstructor",
            "SupportTicketCommentAdded", "SupportTicketRecommendationUpdated", "SupportTicketResolved", "SupportTicketClosed" })
            Assert.Contains(action, actions);
        var events = await db.SupportEvents.Where(e => e.TicketId == ticket.Id).ToListAsync();
        var first = events.First();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { db.SupportEvents.Remove(first); await db.SaveChangesAsync(); });
        db.ChangeTracker.Clear();
        if (db.Database.IsNpgsql())
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"SupportEvents\" WHERE \"Id\" = {0}", first.Id));
    }

    [Fact] public async Task Create_validates_role_fields_and_reference_consistency()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f); using var admin = ApiTests.Client(f); using var anonymous = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(admin, "admin");
        var basic = new { category = "Other", title = "Need help", description = "A general question.", priority = "Low" };

        Assert.Equal(HttpStatusCode.Forbidden, (await CreateTicket(admin, basic)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateTicket(anonymous, basic)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateTicket(owner, new { category = "Unknown", title = "x", description = "x", priority = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateTicket(owner, new { category = "Other", title = "", description = "x", priority = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateTicket(owner, new { category = "Other", title = "x", description = "", priority = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateTicket(owner, new { category = "Other", title = "x", description = "x", priority = "Severe" })).StatusCode);

        // Reviewer has Review access to SharedCourse only, not PrivateCourse.
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateTicket(ctl, new { category = "Other", title = "x", description = "x", priority = "Low", courseId = Guid.Parse(PrivateCourse) })).StatusCode);

        var elementInPrivateCourse = await ReviewTests.ElementId(owner, PrivateCourse, "page");
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateTicket(owner,
            new { category = "Other", title = "x", description = "x", priority = "Low", courseId = Guid.Parse(SharedCourse), elementId = elementInPrivateCourse })).StatusCode);

        // A Draft revision task is visible only to the CTL reviewer who created it, not to the course owner.
        var (_, findingIds) = await TaskTests.PublishedFindings(ctl, SharedCourse, "page");
        var draftTask = await TaskTests.Ok(await TaskTests.Create(ctl, SharedCourse, findingIds[..1]));
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateTicket(owner, new { category = "Other", title = "x", description = "x", priority = "Low", revisionTaskId = draftTask.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateTicket(ctl, new { category = "Other", title = "x", description = "x", priority = "Low", revisionTaskId = draftTask.Id })).StatusCode);
    }

    [Fact] public async Task Triage_queue_is_tenant_wide_and_distinct_from_the_callers_own_queue()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var created = await OkTicket(await CreateTicket(owner, new { category = "Other", title = "General question", description = "How do modules work?", priority = "Low" }));

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/support/tickets?scope=queue")).StatusCode);
        var queue = await ctl.GetFromJsonAsync<SupportTicketSummaryDto[]>("/api/v1/support/tickets?scope=queue");
        Assert.Contains(queue!, t => t.Id == created.Id && !t.IsMine);

        var ownerMine = await owner.GetFromJsonAsync<SupportTicketSummaryDto[]>("/api/v1/support/tickets");
        Assert.Contains(ownerMine!, t => t.Id == created.Id && t.IsMine);
        var ctlMine = await ctl.GetFromJsonAsync<SupportTicketSummaryDto[]>("/api/v1/support/tickets");
        Assert.DoesNotContain(ctlMine!, t => t.Id == created.Id);

        // Not assigned and not the requester, but still visible via the tenant-wide triage queue.
        Assert.Equal(HttpStatusCode.OK, (await ctl.GetAsync($"/api/v1/support/tickets/{created.Id}")).StatusCode);
    }

    [Fact] public async Task Linked_course_details_are_redacted_once_the_viewing_ctl_loses_course_access()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var created = await OkTicket(await CreateTicket(owner, new { category = "Accessibility", title = "x", description = "x", priority = "Low", courseId = Guid.Parse(SharedCourse) }));
        Assert.NotNull((await OkTicket(await ctl.GetAsync($"/api/v1/support/tickets/{created.Id}"))).LinkedCourse);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/courses/{SharedCourse}/shares/{TaskTests.ReviewerId}")).StatusCode);

        var afterRevoke = await OkTicket(await ctl.GetAsync($"/api/v1/support/tickets/{created.Id}"));
        Assert.Null(afterRevoke.LinkedCourse);
    }

    [Fact] public async Task Assignment_requires_an_active_ctl_reviewer_in_tenant()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var created = await OkTicket(await CreateTicket(owner, new { category = "Other", title = "x", description = "x", priority = "Low" }));

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PatchAsJsonAsync($"/api/v1/support/tickets/{created.Id}/assignment", new { revision = created.Revision, assigneeId = TaskTests.ReviewerId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PatchAsJsonAsync($"/api/v1/support/tickets/{created.Id}/assignment", new { revision = created.Revision, assigneeId = TaskTests.OwnerId })).StatusCode); // not a CTL reviewer
        var assigned = await OkTicket(await ctl.PatchAsJsonAsync($"/api/v1/support/tickets/{created.Id}/assignment", new { revision = created.Revision, assigneeId = TaskTests.ReviewerId }));
        Assert.Equal("Jordan Lee", assigned.AssigneeName);
        var unassigned = await OkTicket(await ctl.PatchAsJsonAsync($"/api/v1/support/tickets/{created.Id}/assignment", new { revision = assigned.Revision, assigneeId = (Guid?)null }));
        Assert.Null(unassigned.AssigneeId);
    }

    [Fact] public async Task Knowledge_articles_are_created_as_drafts_visible_only_to_ctl_until_published_and_keep_a_stable_slug()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/knowledge", new { title = "x", body = "x" })).StatusCode);
        var created = await OkArticle(await ctl.PostAsJsonAsync("/api/v1/knowledge", new { title = "Writing useful image alt text", body = "Draft body.", tags = new[] { "accessibility" } }));
        Assert.Equal(("Draft", "writing-useful-image-alt-text-2"), (created.Status, created.Slug)); // the seeded article already owns the base slug

        Assert.DoesNotContain((await owner.GetFromJsonAsync<KnowledgeArticleSummaryDto[]>("/api/v1/knowledge"))!, a => a.Id == created.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/knowledge/{created.Slug}")).StatusCode);
        Assert.Contains((await ctl.GetFromJsonAsync<KnowledgeArticleSummaryDto[]>("/api/v1/knowledge"))!, a => a.Id == created.Id);

        var published = await OkArticle(await ctl.PutAsJsonAsync($"/api/v1/knowledge/{created.Id}",
            new { revision = created.Revision, title = "Writing useful image alt text (updated)", body = "Published body.", tags = new[] { "accessibility", "images" }, status = "Published" }));
        Assert.Equal(("Published", created.Slug, 2), (published.Status, published.Slug, published.Version)); // the slug is stable across the title edit
        Assert.Contains((await owner.GetFromJsonAsync<KnowledgeArticleSummaryDto[]>("/api/v1/knowledge"))!, a => a.Id == created.Id);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/knowledge/{created.Slug}")).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PutAsJsonAsync($"/api/v1/knowledge/{created.Id}", new { revision = created.Revision, title = "x", body = "x", tags = Array.Empty<string>(), status = "Draft" })).StatusCode);
    }

    [Fact] public async Task Create_normalizes_a_timezone_naive_datetime_to_utc_instead_of_failing()
    {
        // Regression: a browser datetime-local input sends a timezone-naive string (no trailing Z/offset), which
        // deserializes as DateTimeKind.Unspecified. PostgreSQL's timestamptz columns reject that Kind outright
        // unless the server normalizes it first.
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var workshop = await OkWorkshop(await ctl.PostAsJsonAsync("/api/v1/workshops",
            new { title = "Naive datetime regression", description = "x", startsAt = "2026-11-15T09:30:00", endsAt = "2026-11-15T10:30:00", modality = "Online", capacity = (int?)null }));
        Assert.Equal(DateTimeKind.Utc, workshop.StartsAt.Kind);
    }

    [Fact] public async Task Workshop_registration_respects_capacity_waitlists_and_promotes_on_cancellation()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/workshops",
            new { title = "x", description = "x", startsAt = DateTime.UtcNow.AddDays(1), endsAt = DateTime.UtcNow.AddDays(1).AddHours(1), modality = "Online", capacity = 1 })).StatusCode);
        var startsAt = DateTime.UtcNow.AddDays(7);
        var created = await OkWorkshop(await ctl.PostAsJsonAsync("/api/v1/workshops",
            new { title = "Accessible media captions", description = "x", startsAt, endsAt = startsAt.AddHours(1), modality = "Online", capacity = 1 }));
        Assert.Empty(created.Registrations);

        var ownerView = await OkWorkshop(await owner.PostAsJsonAsync($"/api/v1/workshops/{created.Id}/register", new { }));
        Assert.Equal(("Registered", 1, 0), (ownerView.MyStatus, ownerView.RegisteredCount, ownerView.WaitlistedCount));
        Assert.Empty(ownerView.Registrations); // an instructor's own view never includes the full registrant list

        // Jordan Lee registers in their Instructor capacity, after the one seat is already taken.
        var ctlView = await OkWorkshop(await ctl.PostAsJsonAsync($"/api/v1/workshops/{created.Id}/register", new { }));
        Assert.Equal(("Waitlisted", 1, 1), (ctlView.MyStatus, ctlView.RegisteredCount, ctlView.WaitlistedCount));
        Assert.Equal(["Registered", "Waitlisted"], ctlView.Registrations.Select(r => r.Status));

        var afterCancel = await OkWorkshop(await owner.DeleteAsync($"/api/v1/workshops/{created.Id}/register"));
        Assert.Null(afterCancel.MyStatus);
        var promoted = (await ctl.GetFromJsonAsync<WorkshopDto[]>("/api/v1/workshops"))!.Single(w => w.Id == created.Id);
        Assert.Equal(("Registered", 1, 0), (promoted.MyStatus, promoted.RegisteredCount, promoted.WaitlistedCount));
        Assert.Equal(["Registered"], promoted.Registrations.Select(r => r.Status)); // the cancelled row is dropped from the active roster
    }
}
