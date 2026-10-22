using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class ReviewTests
{
    private const string SharedCourse = "30000000-0000-0000-0000-000000000001", PrivateCourse = "30000000-0000-0000-0000-000000000002";
    private static readonly Guid RubricV1 = DemoSeed.RubricVersionId, RubricProfile = DemoSeed.RubricProfileId;

    internal static async Task<Guid> ElementId(HttpClient c, string course, string type)
    {
        var detail = await c.GetFromJsonAsync<JsonElement>($"/api/v1/courses/{course}");
        return detail.GetProperty("elements").EnumerateArray().First(e => e.GetProperty("type").GetString() == type).GetProperty("id").GetGuid();
    }
    internal static async Task<ReviewDto> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReviewDto>())!;
    }
    private static Task<HttpResponseMessage> Start(HttpClient c, Guid element, Guid? version = null) =>
        c.PostAsJsonAsync($"/api/v1/elements/{element}/reviews", new { rubricVersionId = version ?? RubricV1 });
    private static Task<HttpResponseMessage> Rate(HttpClient c, ReviewDto review, Guid criterion, string outcome, int? rating = null, string comment = "") =>
        c.PutAsJsonAsync($"/api/v1/reviews/{review.Id}/criteria/{criterion}", new { revision = review.Revision, outcome, rating, comment, evidence = "" });
    internal static Guid Criterion(ReviewDto review, string code) => review.Rubric.Criteria.Single(c => c.Code == code).Id;

    [Fact] public async Task Reviewer_reviews_assignment_publishes_evidence_based_findings_and_owner_sees_only_published_results()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var assignment = await ElementId(ctl, SharedCourse, "assignment");
        var detail = await ctl.GetFromJsonAsync<JsonElement>($"/api/v1/courses/{SharedCourse}");
        Assert.True(detail.GetProperty("permissions").GetProperty("canReview").GetBoolean());

        var review = await Ok(await Start(ctl, assignment));
        Assert.Equal("Draft", review.Status);
        Assert.True(review.CanEdit && review.IsLatestSnapshot && review.Snapshot.HasStructuralEvidence);
        // Page-only criteria are excluded; every applicable criterion starts as Not reviewed, not zero.
        Assert.Equal(10, review.Evaluations.Count);
        Assert.All(review.Evaluations, e => { Assert.Equal("NotReviewed", e.Outcome); Assert.Null(e.Rating); });
        Assert.Null(review.Score.Percent); Assert.Equal(0m, review.Score.CoveragePercent);
        Assert.Equal(HttpStatusCode.Conflict, (await Start(ctl, assignment)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<ReviewSummaryDto[]>($"/api/v1/courses/{SharedCourse}/reviews"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/reviews/{review.Id}")).StatusCode);

        review = await Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        var proposed = review.Findings.Select(x => x.CheckCode).ToHashSet();
        Assert.Equal(new HashSet<string?> { "IMG_ALT_MISSING", "TABLE_HEADERS_MISSING", "ASSIGNMENT_RUBRIC_MISSING" }, proposed);
        Assert.All(review.Findings, x => { Assert.Equal("Proposed", x.Status); Assert.NotNull(x.CriterionId); Assert.False(string.IsNullOrWhiteSpace(x.Evidence)); });
        var rerun = await Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        Assert.Equal(review.Findings.Select(x => x.Id), rerun.Findings.Select(x => x.Id));
        review = rerun;

        var rubricFinding = review.Findings.Single(x => x.CheckCode == "ASSIGNMENT_RUBRIC_MISSING");
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision })).StatusCode);
        foreach (var finding in review.Findings)
            review = await Ok(await ctl.PatchAsJsonAsync($"/api/v1/findings/{finding.Id}", new { revision = review.Revision, status = finding == rubricFinding ? "Confirmed" : finding.CheckCode == "TABLE_HEADERS_MISSING" ? "Rejected" : "Confirmed" }));
        review = await Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/findings", new {
            revision = review.Revision, criterionId = Criterion(review, "ASM-1"), title = "Evaluation criteria are not stated", description = "",
            severity = "Medium", recommendation = "List how the page and reflection are assessed.", evidence = "Instructions list deliverables but no criteria." }));
        review = await Ok(await Rate(ctl, review, Criterion(review, "ASM-1"), "Rated", 1, "Deliverables only."));
        review = await Ok(await Rate(ctl, review, Criterion(review, "ASM-2"), "Rated", 0));
        review = await Ok(await Rate(ctl, review, Criterion(review, "ENG-1"), "NotApplicable"));
        Assert.Equal(22.2m, review.Score.Percent);
        Assert.Equal(30m, review.Score.CoveragePercent);
        Assert.Equal(2, review.Score.RequiredBelowExpectations);

        review = await Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision, summary = "Clarify assessment criteria." }));
        Assert.Equal("Published", review.Status); Assert.False(review.CanEdit);
        Assert.Equal(HttpStatusCode.Conflict, (await Rate(ctl, review, Criterion(review, "STR-1"), "Rated", 3)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision })).StatusCode);

        var shared = await Ok(await owner.GetAsync($"/api/v1/reviews/{review.Id}"));
        Assert.False(shared.CanEdit);
        Assert.Equal(3, shared.Findings.Count);
        Assert.All(shared.Findings, x => Assert.Equal("Confirmed", x.Status));
        Assert.Equal(JsonSerializer.Serialize(review.Score), JsonSerializer.Serialize(shared.Score));
        var list = Assert.Single((await owner.GetFromJsonAsync<ReviewSummaryDto[]>($"/api/v1/courses/{SharedCourse}/reviews"))!);
        Assert.Equal((3, 30m, false), (list.ConfirmedFindings, list.CoveragePercent, list.IsMine));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision })).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();
        foreach (var action in new[] { "ElementReviewStarted", "ElementReviewChecksRun", "ReviewFindingConfirmed", "ReviewFindingRejected", "ReviewFindingAdded", "ElementReviewPublished" })
            Assert.Contains(action, actions);
    }

    [Fact] public async Task Review_stays_pinned_to_its_snapshot_and_published_results_are_immutable()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var page = await ElementId(ctl, SharedCourse, "page");
        var review = await Ok(await Start(ctl, page));
        review = await Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        Assert.Equal(new HashSet<string?> { "IMG_ALT_FILENAME", "HEADING_SKIPPED_LEVEL", "HEADING_EMPTY", "LINK_VAGUE" }, review.Findings.Select(x => x.CheckCode).ToHashSet());
        foreach (var finding in review.Findings)
            review = await Ok(await ctl.PatchAsJsonAsync($"/api/v1/findings/{finding.Id}", new { revision = review.Revision, status = "Confirmed" }));
        review = await Ok(await Rate(ctl, review, Criterion(review, "ACC-1"), "Rated", 1));
        review = await Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision }));
        var originalText = review.Snapshot.PlainText;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Simulate later Canvas edits captured by a refresh: a new snapshot is appended.
            var element = await db.Elements.SingleAsync(e => e.Id == page);
            element.PlainText = "Edited after review"; element.EvidenceJson = new ContentEvidence(1, [], [], [], []).Serialize();
            element.Snapshots.Add(ElementSnapshot.Capture(element, DateTime.UtcNow.AddMinutes(5)));
            await db.SaveChangesAsync();
            var evaluation = await db.CriterionEvaluations.FirstAsync(e => e.ReviewId == review.Id);
            evaluation.Comment = "tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            var stored = await db.Reviews.SingleAsync(r => r.Id == review.Id);
            stored.Summary = "tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            if (db.Database.IsNpgsql())
            {
                await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"Reviews\" SET \"Summary\" = 'x' WHERE \"Status\" = 'Published'"));
                await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"CriterionEvaluations\" SET \"Comment\" = 'x'"));
                await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"ReviewFindings\""));
                await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"RubricCriteria\" SET \"Title\" = 'x'"));
                await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"RubricVersions\" SET \"VersionNumber\" = 9"));
            }
        }
        var reread = await Ok(await ctl.GetAsync($"/api/v1/reviews/{review.Id}"));
        Assert.False(reread.IsLatestSnapshot);
        Assert.Equal(originalText, reread.Snapshot.PlainText);
        Assert.Equal(review.Findings.Select(x => (x.Id, x.Evidence)), reread.Findings.Select(x => (x.Id, x.Evidence)));
        Assert.Equal(JsonSerializer.Serialize(review.Score), JsonSerializer.Serialize(reread.Score));
        // A new review of the same element uses the newest snapshot.
        var next = await Ok(await Start(ctl, page));
        Assert.True(next.IsLatestSnapshot);
        Assert.Equal("Edited after review", next.Snapshot.PlainText);
        next = await Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{next.Id}/audit", new { revision = next.Revision }));
        Assert.Empty(next.Findings);
    }

    [Fact] public async Task Review_routes_enforce_roles_course_acl_csrf_revision_and_validation()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f); using var admin = ApiTests.Client(f); using var anonymous = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(admin, "admin");
        var sharedAssignment = await ElementId(owner, SharedCourse, "assignment");
        var privateAssignment = await ElementId(owner, PrivateCourse, "assignment");
        var module = await ElementId(owner, SharedCourse, "module");

        Assert.Equal(HttpStatusCode.Forbidden, (await Start(owner, sharedAssignment)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Start(admin, sharedAssignment)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/courses/{SharedCourse}/reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/courses/{SharedCourse}/reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Start(ctl, privateAssignment)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/courses/{PrivateCourse}/reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Start(ctl, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Start(ctl, module)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Start(ctl, sharedAssignment, Guid.NewGuid())).StatusCode);
        ctl.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Start(ctl, sharedAssignment)).StatusCode);
        await ApiTests.Csrf(ctl);

        var review = await Ok(await Start(ctl, sharedAssignment));
        var criterion = Criterion(review, "ASM-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await Rate(ctl, review, criterion, "NotReviewed", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Rate(ctl, review, criterion, "Rated", 4)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Rate(ctl, review, criterion, "Rated")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Rate(ctl, review, criterion, "Zero")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Rate(ctl, review, Guid.NewGuid(), "Rated", 2)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/findings", new { revision = review.Revision, title = "No evidence", severity = "High", evidence = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/findings", new { revision = review.Revision, title = "Bad severity", severity = "Severe", evidence = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision })).StatusCode);
        var updated = await Ok(await Rate(ctl, review, criterion, "Rated", 2));
        // The earlier revision is stale after any change.
        Assert.Equal(HttpStatusCode.Conflict, (await Rate(ctl, review, criterion, "Rated", 3)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/v1/reviews/{review.Id}/criteria/{criterion}", new { revision = updated.Revision, outcome = "Rated", rating = 3 })).StatusCode);

        // Revoking the share removes the reviewer's access to their own draft on the next request.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/courses/{SharedCourse}/shares/20000000-0000-0000-0000-000000000002")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/reviews/{review.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Rate(ctl, updated, criterion, "Rated", 3)).StatusCode);
    }

    [Fact] public async Task Rubric_versions_are_drafted_validated_published_and_then_immutable()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/rubrics")).StatusCode);
        var profile = Assert.Single((await ctl.GetFromJsonAsync<RubricProfileDto[]>("/api/v1/rubrics"))!);
        Assert.Equal(("Published", 10), (profile.Versions[0].Status, profile.Versions[0].CriterionCount));
        var v1 = (await ctl.GetFromJsonAsync<RubricVersionDto>($"/api/v1/rubrics/{RubricProfile}/versions/{RubricV1}"))!;
        Assert.All(v1.Criteria, c => Assert.Equal(4, c.RatingAnchors.Length));
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PutAsJsonAsync($"/api/v1/rubrics/{RubricProfile}/versions/{RubricV1}", new { revision = v1.Revision, criteria = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PostAsJsonAsync($"/api/v1/rubrics/{RubricProfile}/versions/{RubricV1}/publish", new { revision = v1.Revision })).StatusCode);

        var draftResponse = await ctl.PostAsJsonAsync($"/api/v1/rubrics/{RubricProfile}/versions", new { });
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        var draft = (await draftResponse.Content.ReadFromJsonAsync<RubricVersionDto>())!;
        Assert.Equal((2, "Draft", 10), (draft.VersionNumber, draft.Status, draft.Criteria.Count));
        Assert.DoesNotContain(draft.Criteria, c => v1.Criteria.Any(o => o.Id == c.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PostAsJsonAsync($"/api/v1/rubrics/{RubricProfile}/versions", new { })).StatusCode);

        object Input(CriterionDto c, string? title = null, string[]? anchors = null, string? category = null, string[]? checks = null) => new {
            c.Code, title = title ?? c.Title, c.Description, category = category ?? c.Category, c.ElementTypes, c.ReviewMethod, c.EvidenceGuidance,
            ratingAnchors = anchors ?? c.RatingAnchors, c.Weight, c.Required, automatedChecks = checks ?? c.AutomatedChecks };
        var path = $"/api/v1/rubrics/{RubricProfile}/versions/{draft.Id}";
        foreach (var invalid in new[] { Input(draft.Criteria[0], anchors: ["only one"]), Input(draft.Criteria[0], category: "Made up"), Input(draft.Criteria[0], title: ""), Input(draft.Criteria[4], checks: ["NOT_A_CHECK"]) })
            Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PutAsJsonAsync(path, new { revision = draft.Revision, criteria = new[] { invalid } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PutAsJsonAsync(path, new { revision = draft.Revision, criteria = new[] { Input(draft.Criteria[0]), Input(draft.Criteria[0]) } })).StatusCode);
        var edited = await ctl.PutAsJsonAsync(path, new { revision = draft.Revision, criteria = draft.Criteria.Select((c, i) => Input(c, title: i == 0 ? "Revised purpose criterion" : null)).Take(9).ToArray() });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var saved = (await edited.Content.ReadFromJsonAsync<RubricVersionDto>())!;
        Assert.Equal(("Revised purpose criterion", 9), (saved.Criteria[0].Title, saved.Criteria.Count));
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PutAsJsonAsync(path, new { revision = draft.Revision, criteria = Array.Empty<object>() })).StatusCode);
        var published = await ctl.PostAsJsonAsync(path + "/publish", new { revision = saved.Revision });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var v2 = (await published.Content.ReadFromJsonAsync<RubricVersionDto>())!;
        Assert.Equal(("Published", "Jordan Lee"), (v2.Status, v2.PublishedByName));
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PutAsJsonAsync(path, new { revision = v2.Revision, criteria = Array.Empty<object>() })).StatusCode);
        // The earlier version is untouched by edits to its successor.
        var original = (await ctl.GetFromJsonAsync<RubricVersionDto>($"/api/v1/rubrics/{RubricProfile}/versions/{RubricV1}"))!;
        Assert.Equal(v1.Criteria.Select(c => (c.Id, c.Title)), original.Criteria.Select(c => (c.Id, c.Title)));

        var created = await ctl.PostAsJsonAsync("/api/v1/rubrics", new { name = "Online delivery addendum", deliveryMethod = "Online" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var empty = (await created.Content.ReadFromJsonAsync<RubricVersionDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync($"/api/v1/rubrics/{empty.ProfileId}/versions/{empty.Id}/publish", new { revision = empty.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync("/api/v1/rubrics", new { name = "Online delivery addendum", deliveryMethod = "Online" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync("/api/v1/rubrics", new { name = "Other", deliveryMethod = "Anything" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Start(ctl, await ElementId(ctl, SharedCourse, "page"), empty.Id)).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();
        foreach (var action in new[] { "RubricVersionDrafted", "RubricDraftUpdated", "RubricVersionPublished", "RubricProfileCreated" }) Assert.Contains(action, actions);
    }
}
