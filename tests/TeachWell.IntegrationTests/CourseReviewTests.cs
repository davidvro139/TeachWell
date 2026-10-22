using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class CourseReviewTests
{
    private const string SharedCourse = "30000000-0000-0000-0000-000000000001", PrivateCourse = "30000000-0000-0000-0000-000000000002";

    // A course-level rubric is not part of the baseline seed, so each test authors and publishes one through the API.
    private static async Task<Guid> PublishCourseRubric(HttpClient ctl)
    {
        var created = await ctl.PostAsJsonAsync("/api/v1/rubrics", new { name = "Course structure (test)", deliveryMethod = "Any" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<RubricVersionDto>())!;
        string[] anchors = ["None.", "Partial.", "Meets.", "Exceeds."];
        var criteria = new object[] {
            new { code = "CRS-A", title = "Clear and paced structure", description = "", category = "Course structure and navigation", elementTypes = new[] { "course" }, reviewMethod = "Assisted",
                evidenceGuidance = "", ratingAnchors = anchors, weight = 2, required = true, automatedChecks = new[] { CourseChecks.StartMissing, CourseChecks.DueDateMissing } },
            new { code = "CRS-B", title = "Assignments are aligned", description = "", category = "Objectives and competencies", elementTypes = new[] { "course" }, reviewMethod = "Assisted",
                evidenceGuidance = "", ratingAnchors = anchors, weight = 1, required = true, automatedChecks = new[] { CourseChecks.Unaligned, CourseChecks.CompetenciesMissing } },
            new { code = "CRS-C", title = "Visual design is consistent", description = "", category = "Course structure and navigation", elementTypes = new[] { "course" }, reviewMethod = "Manual",
                evidenceGuidance = "", ratingAnchors = anchors, weight = 1, required = false, automatedChecks = Array.Empty<string>() } };
        var path = $"/api/v1/rubrics/{draft.ProfileId}/versions/{draft.Id}";
        var saved = (await (await ctl.PutAsJsonAsync(path, new { revision = draft.Revision, criteria })).Content.ReadFromJsonAsync<RubricVersionDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await ctl.PostAsJsonAsync(path + "/publish", new { revision = saved.Revision })).StatusCode);
        return draft.Id;
    }
    private static Task<HttpResponseMessage> StartCourse(HttpClient c, string course, Guid version) =>
        c.PostAsJsonAsync($"/api/v1/courses/{course}/course-reviews", new { rubricVersionId = version });

    [Fact] public async Task Reviewer_reviews_course_structure_and_course_findings_point_at_the_item_they_concern()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var courseRubric = await PublishCourseRubric(ctl);
        var rubrics = (await ctl.GetFromJsonAsync<RubricProfileDto[]>("/api/v1/rubrics"))!;
        Assert.Equal(["course"], rubrics.Single(p => p.Name == "Course structure (test)").Versions[0].ElementTypes);
        Assert.Equal(["page", "assignment"], rubrics.Single(p => p.Id == DemoSeed.RubricProfileId).Versions[0].ElementTypes);

        // A page/assignment rubric has nothing to evaluate at course level.
        Assert.Equal(HttpStatusCode.BadRequest, (await StartCourse(ctl, SharedCourse, DemoSeed.RubricVersionId)).StatusCode);
        var review = await ReviewTests.Ok(await StartCourse(ctl, SharedCourse, courseRubric));
        Assert.Equal(("course", "Course structure", 3), (review.ElementType, review.ElementTitle, review.Evaluations.Count));
        var outline = Assert.IsType<CourseOutlineDto>(review.Outline);
        var module = Assert.Single(outline.Modules);
        Assert.Equal(("Start here", 2), (module.Title, module.Items.Count));
        Assert.Contains("Module · Start here", review.Snapshot.PlainText);
        Assert.All(module.Items, i => Assert.NotNull(i.SnapshotId));
        Assert.Equal(HttpStatusCode.Conflict, (await StartCourse(ctl, SharedCourse, courseRubric)).StatusCode);
        // The synthetic course element is reviewable only through the course route and never appears as course content.
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync($"/api/v1/elements/{review.ElementId}/reviews", new { rubricVersionId = courseRubric })).StatusCode);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/courses/{SharedCourse}");
        Assert.DoesNotContain(detail.GetProperty("elements").EnumerateArray(), e => e.GetProperty("type").GetString() == "course");
        Assert.Equal(3, (await owner.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=Instructor"))!.Single(c => c.Id == Guid.Parse(SharedCourse)).ElementCount);

        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        Assert.Equal(new HashSet<string?> { CourseChecks.DueDateMissing, CourseChecks.CompetenciesMissing }, review.Findings.Select(x => x.CheckCode).ToHashSet());
        var assignment = await ReviewTests.ElementId(ctl, SharedCourse, "assignment");
        var due = review.Findings.Single(x => x.CheckCode == CourseChecks.DueDateMissing);
        Assert.Equal((assignment, "Your first accessible page"), (due.SubjectElementId, due.SubjectTitle));
        Assert.Null(due.SubjectSourceUrl); // fictional demo content has no Canvas location
        Assert.NotNull(due.CriterionId);

        // Every page captured with the structure can be previewed from the review; nothing else can.
        var page = module.Items.Single(i => i.Type == "page");
        var preview = await ctl.GetFromJsonAsync<PreviewDto>($"/api/v1/reviews/{review.Id}/previews/{page.SnapshotId}");
        Assert.Contains("<h2>Welcome</h2>", preview!.PreviewHtml);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/reviews/{review.Id}/previews/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/reviews/{review.Id}/previews/{page.SnapshotId}")).StatusCode); // draft is private

        foreach (var finding in review.Findings)
            review = await ReviewTests.Ok(await ctl.PatchAsJsonAsync($"/api/v1/findings/{finding.Id}", new { revision = review.Revision, status = "Confirmed" }));
        review = await ReviewTests.Ok(await ctl.PutAsJsonAsync($"/api/v1/reviews/{review.Id}/criteria/{ReviewTests.Criterion(review, "CRS-A")}",
            new { revision = review.Revision, outcome = "Rated", rating = 2, comment = "", evidence = "" }));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision, summary = "Set due dates; define competencies." }));

        var shared = (await owner.GetFromJsonAsync<ReviewSummaryDto[]>($"/api/v1/courses/{SharedCourse}/reviews"))!;
        Assert.Equal("course", Assert.Single(shared).ElementType);
        // A course-level review is not a review of a page or assignment, so it never counts toward item coverage (which once read 150%).
        var quality = (await owner.GetFromJsonAsync<CourseQualityReportDto>($"/api/v1/reports/course/{SharedCourse}/quality"))!;
        Assert.Equal((0, 0m), (quality.ElementsWithPublishedReview, quality.ReviewedCoveragePercent));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/reviews/{review.Id}/previews/{page.SnapshotId}")).StatusCode);

        // Course findings become ordinary revision tasks; structure cannot be "simulated" as a text edit.
        var task = (await (await ctl.PostAsJsonAsync("/api/v1/revision-tasks", new { courseId = SharedCourse, title = "Set a due date", priority = "Medium", findingIds = new[] { due.Id } }))
            .Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.Equal(("course", "Alex Morgan"), (task.ElementType, task.AssigneeName));
        task = (await (await ctl.PostAsJsonAsync($"/api/v1/revision-tasks/{task.Id}/transition", new { revision = task.Revision, toStatus = "NeedsRevision" })).Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/revision-tasks/{task.Id}/simulate-update", new { revision = task.Revision, plainText = "x" })).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains("CourseStructureElementCreated", await db.AuditLogs.Select(a => a.Action).ToListAsync());
    }

    [Fact] public async Task Course_review_requires_the_reviewer_role_and_course_access()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var courseRubric = await PublishCourseRubric(ctl);
        Assert.Equal(HttpStatusCode.Forbidden, (await StartCourse(owner, SharedCourse, courseRubric)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StartCourse(ctl, PrivateCourse, courseRubric)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StartCourse(ctl, Guid.NewGuid().ToString(), courseRubric)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await StartCourse(ctl, SharedCourse, Guid.NewGuid())).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Elements.AnyAsync(e => e.Type == ReviewVocabulary.CourseType && e.CourseId == Guid.Parse(PrivateCourse)));
    }

    [Fact] public async Task Element_review_shows_a_sanitized_preview_and_cannot_be_used_to_read_other_snapshots()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var page = await ReviewTests.ElementId(ctl, SharedCourse, "page");
        var review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{page}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        var html = review.Snapshot.PreviewHtml!;
        Assert.Contains("<h2>Welcome</h2>", html);
        Assert.Contains("alt text is a file name: welcome-banner.png", html);
        Assert.Contains("[empty heading]", html);
        foreach (var forbidden in new[] { "href", "src=", "schedule.example.org/web101", "<a " }) Assert.DoesNotContain(forbidden, html);
        Assert.Equal(HttpStatusCode.OK, (await ctl.GetAsync($"/api/v1/reviews/{review.Id}/previews/{review.Snapshot.Id}")).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var other = await db.ElementSnapshots.Where(s => db.Elements.Any(e => e.Id == s.ElementId && e.CourseId == Guid.Parse(PrivateCourse))).Select(s => s.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/reviews/{review.Id}/previews/{other}")).StatusCode);
    }
}
