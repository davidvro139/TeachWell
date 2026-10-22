using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class ReadinessUnitTests
{
    private static Endorsement Endorsed(DateTime validUntil, Dictionary<Guid, Guid> pinned) =>
        new() { StandardsName = "Online course standards", StandardsVersionNumber = 2, EndorsedAt = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc), ValidUntil = validUntil,
            SnapshotIdsJson = System.Text.Json.JsonSerializer.Serialize(pinned) };

    [Fact] public void Endorsement_is_worded_as_a_dated_ctl_endorsement_against_a_named_version()
    {
        Assert.Equal("CTL-endorsed against Online course standards v2 on October 19, 2026", Endorsed(DateTime.MaxValue, []).Wording);
    }

    [Fact] public void Recheck_is_due_after_the_validity_period_or_a_substantial_change_never_otherwise()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var pinned = new Dictionary<Guid, Guid> { [a] = Guid.NewGuid(), [b] = Guid.NewGuid(), [c] = Guid.NewGuid(), [d] = Guid.NewGuid() };
        var now = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var e = Endorsed(now.AddDays(1), pinned);
        Assert.Empty(ReadinessRecheck.Reasons(e, pinned, now, 25));
        // One of four items changed: 25%, which meets the threshold. Below it, nothing is due.
        var changed = new Dictionary<Guid, Guid>(pinned) { [a] = Guid.NewGuid() };
        Assert.Contains("1 of 4 items", Assert.Single(ReadinessRecheck.Reasons(e, changed, now, 25)));
        Assert.Empty(ReadinessRecheck.Reasons(e, changed, now, 26));
        // A removed item and an added one both count as changes.
        var reshaped = new Dictionary<Guid, Guid>(pinned) { [Guid.NewGuid()] = Guid.NewGuid() };
        reshaped.Remove(b);
        Assert.Equal((2, 5), ReadinessRecheck.Compare(pinned, reshaped));
        Assert.Contains("validity period ended", Assert.Single(ReadinessRecheck.Reasons(e, pinned, now.AddDays(2), 25)));
    }
}

public class ReadinessTests
{
    private const string Shared = "30000000-0000-0000-0000-000000000001", Unshared = "30000000-0000-0000-0000-000000000002";
    private static Task<HttpResponseMessage> Request(HttpClient c, string course, string note = "") => c.PostAsJsonAsync($"/api/v1/courses/{course}/readiness", new { note });
    private static async Task<T> Ok<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact] public async Task Instructor_requests_ctl_requests_changes_then_endorses_after_a_review()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var state = await owner.GetFromJsonAsync<CourseReadinessDto>($"/api/v1/courses/{Unshared}/readiness");
        Assert.True(state!.IsOwner && state.CanRequest);
        // A reviewer is not the owner and cannot request; DES 120 is not shared with them, so it is 404 either way.
        Assert.Equal(HttpStatusCode.NotFound, (await Request(ctl, Unshared)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/courses/{Unshared}/readiness")).StatusCode);

        Assert.Equal("NotRequested", (await owner.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=Instructor"))!.Single(c => c.Code == "DES 120").Readiness);
        state = await Ok<CourseReadinessDto>(await Request(owner, Unshared, "Ready for a look before spring."));
        // The course card's badge and the CTL's notification follow the request.
        Assert.Equal(("Requested", "Requested"), (await owner.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=Instructor"))!
            .Where(c => c.Code == "DES 120").Select(c => (c.Readiness, c.ReadinessLabel)).Single());
        var inbox = (await ctl.GetFromJsonAsync<NotificationListDto>("/api/v1/notifications"))!;
        Assert.Equal((1, NotificationVocabulary.ReadinessRequested), (inbox.Unread, inbox.Items[0].Kind));
        Assert.Contains("DES 120", inbox.Items[0].Title);
        Assert.Equal((ReadinessVocabulary.Requested, false), (state.Active!.Status, state.CanRequest));
        Assert.Equal(HttpStatusCode.Conflict, (await Request(owner, Unshared)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/readiness/queue")).StatusCode);

        // The tenant-wide queue shows the request without course detail until the reviewer takes it, which shares the course with them.
        var queued = Assert.Single((await ctl.GetFromJsonAsync<ReadinessRequestDto[]>("/api/v1/readiness/queue"))!);
        Assert.Equal(("DES 120", "Ready for a look before spring.", null), (queued.CourseCode, queued.Note, queued.Guide));
        var taken = await Ok<ReadinessRequestDto>(await ctl.PostAsJsonAsync($"/api/v1/readiness/{queued.Id}/take", new { revision = queued.Revision }));
        Assert.True(taken.TakenByMe);
        Assert.Equal(NotificationVocabulary.ReadinessTaken, (await owner.GetFromJsonAsync<NotificationListDto>("/api/v1/notifications"))!.Items[0].Kind);
        Assert.NotNull(taken.Guide);
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PostAsJsonAsync($"/api/v1/readiness/{queued.Id}/take", new { revision = taken.Revision })).StatusCode);
        var review = await ctl.GetFromJsonAsync<CourseReadinessDto>($"/api/v1/courses/{Unshared}/readiness");
        Assert.True(review!.CanDecide);
        Assert.False(review.HasQualifyingReview);

        Task<HttpResponseMessage> Decide(Guid revision, string decision, string summary) =>
            ctl.PostAsJsonAsync($"/api/v1/readiness/{queued.Id}/decide", new { revision, decision, summary });
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(taken.Revision, "Approved", "x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(taken.Revision, ReadinessVocabulary.ChangesRequested, " ")).StatusCode);
        // Endorsing needs a review published since the request.
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(taken.Revision, ReadinessVocabulary.Endorsed, "Looks good.")).StatusCode);
        state = await Ok<CourseReadinessDto>(await Decide(taken.Revision, ReadinessVocabulary.ChangesRequested, "Add due dates to every assignment."));
        Assert.Equal((null, ReadinessVocabulary.ChangesRequested, "Add due dates to every assignment."), (state.Active, state.LastDecision!.Status, state.LastDecision.DecisionSummary));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/readiness/{queued.Id}/withdraw", new { revision = state.LastDecision.Revision })).StatusCode);
        inbox = (await owner.GetFromJsonAsync<NotificationListDto>("/api/v1/notifications"))!;
        Assert.Equal((2, NotificationVocabulary.ReadinessChangesRequested), (inbox.Unread, inbox.Items[0].Kind));
        Assert.Contains("Add due dates to every assignment.", inbox.Items[0].Body);
        // Notifications are personal: someone else's is 404; reading one, then all, clears the count.
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PostAsJsonAsync($"/api/v1/notifications/{inbox.Items[0].Id}/read", new { })).StatusCode);
        Assert.Equal(1, (await Ok<NotificationListDto>(await owner.PostAsJsonAsync($"/api/v1/notifications/{inbox.Items[0].Id}/read", new { }))).Unread);
        Assert.Equal(0, (await Ok<NotificationListDto>(await owner.PostAsJsonAsync("/api/v1/notifications/read-all", new { }))).Unread);

        // Second round: the instructor asks again, the reviewer publishes a review and endorses.
        state = await Ok<CourseReadinessDto>(await Request(owner, Unshared));
        var second = state.Active!;
        second = await Ok<ReadinessRequestDto>(await ctl.PostAsJsonAsync($"/api/v1/readiness/{second.Id}/take", new { revision = second.Revision }));
        await TaskTests.PublishedFindings(ctl, Unshared, "assignment");
        state = await Ok<CourseReadinessDto>(await ctl.PostAsJsonAsync($"/api/v1/readiness/{second.Id}/decide",
            new { revision = second.Revision, decision = ReadinessVocabulary.Endorsed, summary = "Clear structure and assessments; alt text fixed." }));
        var endorsement = state.Endorsement!;
        Assert.Equal(NotificationVocabulary.ReadinessEndorsed, (await owner.GetFromJsonAsync<NotificationListDto>("/api/v1/notifications"))!.Items[0].Kind);
        Assert.Equal("Endorsed", (await owner.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=Instructor"))!.Single(c => c.Code == "DES 120").Readiness);
        // Pinned to the seeded college-wide set's v1, the version that applied when the instructor asked.
        Assert.Matches(@"^CTL-endorsed against College-wide .+ v1 on [A-Z][a-z]+ \d{1,2}, \d{4}$", endorsement.Wording);
        Assert.False(endorsement.RecheckDue);
        Assert.True(endorsement.ValidUntil > DateTime.UtcNow.AddMonths(35));
        Assert.Equal(endorsement.Id, (await owner.GetFromJsonAsync<CourseReadinessDto>($"/api/v1/courses/{Unshared}/readiness"))!.Endorsement!.Id);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Endorsements.SingleAsync();
        saved.Summary = "Edited later";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        // On PostgreSQL, triggers refuse the same changes made with direct SQL.
        if (db.Database.IsNpgsql())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"Endorsements\" SET \"Summary\" = 'Edited later'"));
            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"ReadinessRequests\" WHERE \"Status\" = 'ChangesRequested'"));
        }
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "CourseSharedForReadinessReview"));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "ReadinessEndorsed"));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "ReadinessChangesRequested"));
    }

    [Fact] public async Task Required_guide_steps_block_a_request_until_resolved_or_justified_and_requests_can_be_withdrawn()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor");
        // A published Online standard set that requires competencies, and WEB 101 classified as Online.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var checks = StandardSetContent.Defaults().Select(c => c.Code == CourseChecks.CompetenciesMissing ? c with { Required = true } : c).ToList();
            var author = TaskTests.ReviewerId;
            db.StandardSets.Add(new StandardSet { Id = Guid.NewGuid(), TenantId = DemoSeed.Tenant, Name = "Online test standards", DeliveryMode = "Online",
                ScopeKey = StandardSetVocabulary.ScopeKey("Online", null), CreatedBy = author,
                Versions = [new() { Id = Guid.NewGuid(), VersionNumber = 1, CreatedBy = author, ChecksJson = StandardSetContent.Serialize(checks),
                    Status = ReviewVocabulary.Published, PublishedAt = DateTime.UtcNow, PublishedBy = author }] });
            (await db.Courses.SingleAsync(c => c.Id == Guid.Parse(Shared))).DeliveryMode = "Online";
            await db.SaveChangesAsync();
        }
        var state = (await owner.GetFromJsonAsync<CourseReadinessDto>($"/api/v1/courses/{Shared}/readiness"))!;
        Assert.Equal((false, 1), (state.CanRequest, state.RequiredOpenSteps));
        Assert.Contains("No course competencies are defined", Assert.Single(state.Blockers));
        Assert.Equal(HttpStatusCode.BadRequest, (await Request(owner, Shared)).StatusCode);

        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Shared}/guide"))!;
        var key = guide.Steps.Single(s => s.CheckCode == CourseChecks.CompetenciesMissing).Locations[0].Key;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/courses/{Shared}/guide/progress",
            new { keys = new[] { key }, status = "NotApplicable", reason = "Outcomes are in Simple Syllabus; pasting them next term." })).StatusCode);
        state = await Ok<CourseReadinessDto>(await Request(owner, Shared));
        var request = state.Active!;
        // The request pins the standards and the instructor's justification.
        Assert.Equal(("Online test standards", 1), (request.StandardsName, request.StandardsVersionNumber));
        Assert.Equal("Outcomes are in Simple Syllabus; pasting them next term.", Assert.Single(request.Guide!.Reasons).Reason);

        state = await Ok<CourseReadinessDto>(await owner.PostAsJsonAsync($"/api/v1/readiness/{request.Id}/withdraw", new { revision = request.Revision }));
        Assert.Null(state.Active);
        Assert.Equal(ReadinessVocabulary.Withdrawn, state.History[0].Status);
        Assert.True(state.CanRequest);
    }

    [Fact] public async Task A_standard_set_version_says_which_reviews_an_endorsement_needs()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        // The CTL publishes an Online set whose endorsements need reviews of two different items using the seeded rubric.
        var draft = await Ok<StandardSetVersionDto>(await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "Online endorsement test", deliveryMode = "Online" }));
        Assert.Equal(EndorsementRequirements.Baseline, draft.Endorsement);
        Task<HttpResponseMessage> Save(object endorsement) => ctl.PutAsJsonAsync($"/api/v1/standard-sets/{draft.SetId}/versions/{draft.Id}",
            new { revision = draft.Revision, notes = "", checks = draft.Checks, endorsement });
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(new { courseReview = false, rubricProfileId = (Guid?)null, minimumItemReviews = 21 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(new { courseReview = false, rubricProfileId = Guid.NewGuid(), minimumItemReviews = 1 })).StatusCode);
        var saved = await Ok<StandardSetVersionDto>(await Save(new { courseReview = false, rubricProfileId = DemoSeed.RubricProfileId, minimumItemReviews = 2 }));
        Assert.Equal(new EndorsementRequirements(false, DemoSeed.RubricProfileId, 2), saved.Endorsement);
        var published = await Ok<StandardSetVersionDto>(await ctl.PostAsJsonAsync($"/api/v1/standard-sets/{saved.SetId}/versions/{saved.Id}/publish", new { revision = saved.Revision }));
        // A new draft starts from the published requirements.
        var next = await Ok<StandardSetVersionDto>(await ctl.PostAsync($"/api/v1/standard-sets/{published.SetId}/versions", null));
        Assert.Equal(published.Endorsement, next.Endorsement);

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses.SingleAsync(c => c.Id == Guid.Parse(Shared))).DeliveryMode = "Online";
            await db.SaveChangesAsync();
        }
        var request = (await Ok<CourseReadinessDto>(await Request(owner, Shared))).Active!;
        Assert.Equal(("Online endorsement test", 1), (request.StandardsName, request.StandardsVersionNumber));
        await ctl.PostAsJsonAsync($"/api/v1/readiness/{request.Id}/take", new { revision = request.Revision });
        await TaskTests.PublishedFindings(ctl, Shared, "assignment");
        var state = (await ctl.GetFromJsonAsync<CourseReadinessDto>($"/api/v1/courses/{Shared}/readiness"))!;
        Assert.False(state.HasQualifyingReview);
        Assert.Equal([true, false], state.Requirements.Select(r => r.Met));
        Assert.Equal("1 so far", state.Requirements[1].Detail);
        var refused = await ctl.PostAsJsonAsync($"/api/v1/readiness/{request.Id}/decide", new { revision = state.Active!.Revision, decision = "Endorsed", summary = "Looks ready." });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("at least 2 different items", await refused.Content.ReadAsStringAsync());

        await TaskTests.PublishedFindings(ctl, Shared, "page");
        state = (await ctl.GetFromJsonAsync<CourseReadinessDto>($"/api/v1/courses/{Shared}/readiness"))!;
        Assert.True(state.HasQualifyingReview);
        state = await Ok<CourseReadinessDto>(await ctl.PostAsJsonAsync($"/api/v1/readiness/{request.Id}/decide", new { revision = state.Active!.Revision, decision = "Endorsed", summary = "Two items reviewed against the rubric." }));
        Assert.StartsWith("CTL-endorsed against Online endorsement test v1", state.Endorsement!.Wording);
    }
}
