using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class GuideUnitTests
{
    private static GuideIssue Issue(string code, string severity, int? module, bool open = true, string? key = null) =>
        new(key ?? Guid.NewGuid().ToString(), new(code, code, "t", "d", severity, "r", "e"), null, module, open);

    [Fact] public void Every_check_has_an_effort_estimate_and_every_area_an_introduction()
    {
        Assert.All(CheckRegistry.All, c => Assert.True(GuideCatalog.Entries.ContainsKey(c.Code), c.Code));
        Assert.Equal(ReviewAspects.All.Order(), GuideCatalog.Aspects.Keys.Order());
    }

    [Fact] public void Same_check_in_many_places_becomes_one_step_ranked_by_severity_reach_position_and_ease()
    {
        var steps = ImprovementGuide.Build([
            // Five quick image fixes in the first module outrank one long high-severity rewrite late in the course.
            .. Enumerable.Range(0, 5).Select(_ => Issue(DeterministicChecks.ImageAltMissing, "High", 0)),
            Issue(ContentChecks.PageTooLong, "Low", 6),
            Issue(CourseChecks.StartMissing, "Medium", null),
        ], _ => false);
        Assert.Equal([DeterministicChecks.ImageAltMissing, CourseChecks.StartMissing, ContentChecks.PageTooLong], steps.Select(s => s.CheckCode));
        var images = steps[0];
        Assert.Equal((5, 15, true), (images.OpenCount, images.OpenMinutes, images.QuickWin));
        Assert.False(steps[2].QuickWin); // 30 minutes and low severity
    }

    [Fact] public void Required_steps_come_first_and_finished_steps_last()
    {
        var steps = ImprovementGuide.Build([
            Issue(DeterministicChecks.LinkEmpty, "High", 0),
            Issue(CourseChecks.ModuleNaming, "Low", 3),
            Issue(DeterministicChecks.ImageAltMissing, "Critical", 0, open: false),
        ], code => code == CourseChecks.ModuleNaming);
        Assert.Equal([CourseChecks.ModuleNaming, DeterministicChecks.LinkEmpty, DeterministicChecks.ImageAltMissing], steps.Select(s => s.CheckCode));
        Assert.Equal((0, 0.0), (steps[2].OpenCount, steps[2].Priority));
    }

    [Fact] public void Strengths_are_only_claimed_where_there_was_something_to_check()
    {
        var none = new GuideCoverage(Pages: 2, Assessments: 0, Images: 0, Links: 4, Headings: 3, Tables: 0, Modules: 1, Outcomes: 0, Items: 2);
        var strengths = ImprovementGuide.Strengths([DeterministicChecks.ImageAltMissing, DeterministicChecks.LinkVague, DeterministicChecks.LinkEmpty, DeterministicChecks.RubricMissing],
            [DeterministicChecks.LinkEmpty], none).Select(s => s.CheckCode);
        // No images and no assignments: nothing to claim. Empty links were found: not a strength.
        Assert.Equal([DeterministicChecks.LinkVague], strengths);
    }
}

public class GuideTests
{
    private const string Course = "30000000-0000-0000-0000-000000000001", Unshared = "30000000-0000-0000-0000-000000000002";

    [Fact] public async Task Owner_gets_a_ranked_guide_without_a_reviewer_and_records_progress()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.True(guide.CanMark);
        Assert.Equal(ReviewAspects.All, guide.Aspects.Select(a => a.Aspect));
        Assert.Equal(Enumerable.Range(1, guide.Steps.Count), guide.Steps.Select(s => s.Rank));
        // WEB 101 has an assignment and no competencies yet.
        var step = Assert.Single(guide.Steps, s => s.CheckCode == CourseChecks.CompetenciesMissing);
        Assert.False(string.IsNullOrWhiteSpace(step.Guidance.Why));
        var key = Assert.Single(step.Locations).Key;
        Assert.Equal(("Whole course", GuideVocabulary.Open), (step.Locations[0].Where, step.Locations[0].Status));

        Task<HttpResponseMessage> Mark(HttpClient c, object body) => c.PutAsJsonAsync($"/api/v1/courses/{Course}/guide/progress", body);
        // Invalid input: no reason for not-applicable, unknown status, a place the guide does not show.
        Assert.Equal(HttpStatusCode.BadRequest, (await Mark(owner, new { keys = new[] { key }, status = "NotApplicable", reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mark(owner, new { keys = new[] { key }, status = "Fixed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mark(owner, new { keys = new[] { "course:made-up" }, status = "Done" })).StatusCode);

        var marked = await Mark(owner, new { keys = new[] { key }, status = "NotApplicable", reason = "Competencies live in Simple Syllabus for now." });
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        var place = (await marked.Content.ReadFromJsonAsync<CourseGuideDto>())!.Steps.Single(s => s.CheckCode == CourseChecks.CompetenciesMissing).Locations[0];
        Assert.Equal((GuideVocabulary.NotApplicable, "Competencies live in Simple Syllabus for now."), (place.Status, place.Reason));

        // A shared CTL reviewer reads the same guide, including the reason, but cannot record progress; an unshared course is 404.
        var seen = (await ctl.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.False(seen.CanMark);
        Assert.Equal(GuideVocabulary.NotApplicable, seen.Steps.Single(s => s.CheckCode == CourseChecks.CompetenciesMissing).Locations[0].Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Mark(ctl, new { keys = new[] { key }, status = "Open" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/courses/{Unshared}/guide")).StatusCode);

        // Done before the latest import, and still found: shown as open again, flagged.
        Assert.Equal(HttpStatusCode.OK, (await Mark(owner, new { keys = new[] { key }, status = "Done" })).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Courses.SingleAsync(x => x.Id == Guid.Parse(Course));
            c.LastSyncedAt = DateTime.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        place = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!.Steps.Single(s => s.CheckCode == CourseChecks.CompetenciesMissing).Locations[0];
        Assert.Equal((GuideVocabulary.Open, true), (place.Status, place.StillDetected));

        // Fixing it (adding a competency) resolves the step; the earlier Done mark is counted as resolved.
        Assert.Equal(HttpStatusCode.OK, (await Mark(owner, new { keys = new[] { key }, status = "Done" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies", new { code = "SLO-1", title = "Build an accessible page", description = "" })).StatusCode);
        var after = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.DoesNotContain(after.Steps, s => s.CheckCode == CourseChecks.CompetenciesMissing);
        Assert.Equal(1, after.ResolvedSinceMarked);
        Assert.Contains(after.Strengths, s => s.CheckCode == CourseChecks.CompetenciesMissing);

        using var audit = f.Services.CreateScope();
        Assert.Equal(3, await audit.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.CountAsync(a => a.Action.StartsWith("GuideProgress")));
    }

    [Fact] public async Task Reviewer_findings_from_published_reviews_appear_in_the_guide()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var elementId = await ReviewTests.ElementId(ctl, Course, "assignment");
        var review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{elementId}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        foreach (var finding in review.Findings)
            review = await ReviewTests.Ok(await ctl.PatchAsJsonAsync($"/api/v1/findings/{finding.Id}", new { revision = review.Revision, status = "Confirmed" }));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/findings", new {
            revision = review.Revision, title = "Evaluation criteria are not stated", description = "", severity = "Medium",
            recommendation = "List how the page and reflection are assessed.", evidence = "Instructions list deliverables only." }));
        review = await ReviewTests.Ok(await ctl.PutAsJsonAsync($"/api/v1/reviews/{review.Id}/criteria/{review.Rubric.Criteria[0].Id}",
            new { revision = review.Revision, outcome = "Rated", rating = 2, comment = "", evidence = "" }));

        // A draft review is private to its author: nothing from it reaches the guide yet.
        var before = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.Empty(before.ReviewerNotes);
        Assert.All(before.Steps.SelectMany(s => s.Locations), l => Assert.Null(l.ConfirmedBy));

        await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision }));
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        // The reviewer's automated findings are the same issues the guide finds: they are attached to those places, not repeated.
        var rubric = guide.Steps.Single(s => s.CheckCode == DeterministicChecks.RubricMissing);
        Assert.True(rubric.ConfirmedByReviewer);
        Assert.NotNull(Assert.Single(rubric.Locations, l => l.ElementId == elementId).ConfirmedBy);
        // The reviewer's own finding cannot be detected automatically, so it is listed as a note linking back to the review.
        var note = Assert.Single(guide.ReviewerNotes);
        Assert.Equal(("Evaluation criteria are not stated", review.Id, "Assignment: Your first accessible page"), (note.Title, note.ReviewId, note.Where));
    }

    [Fact] public async Task Ctl_sees_which_checks_instructors_set_aside_with_reasons_only_from_courses_it_can_open()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        Assert.Empty((await ctl.GetFromJsonAsync<GuideFeedbackDto>("/api/v1/standards/feedback"))!.Checks);
        foreach (var (course, reason) in new[] { (Course, "Outcomes are kept in Simple Syllabus."), (Unshared, "Private reason in an unshared course.") })
        {
            var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{course}/guide"))!;
            var key = guide.Steps.Single(s => s.CheckCode == CourseChecks.CompetenciesMissing).Locations[0].Key;
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/courses/{course}/guide/progress", new { keys = new[] { key }, status = "NotApplicable", reason })).StatusCode);
        }
        var feedback = (await ctl.GetFromJsonAsync<GuideFeedbackDto>("/api/v1/standards/feedback"))!;
        Assert.Equal(2, feedback.Courses);
        var row = Assert.Single(feedback.Checks);
        Assert.Equal((CourseChecks.CompetenciesMissing, 2, 0, 100m, 2, false), (row.CheckCode, row.NotApplicable, row.Done, row.NotApplicablePercent, row.Courses, row.TuningCandidate));
        // Counts are college-wide; the reason from DES 120 (not shared with this reviewer) is withheld and only counted.
        Assert.Equal(["Outcomes are kept in Simple Syllabus."], row.Reasons.Select(r => r.Reason));
        Assert.Equal(1, row.ReasonsNotShown);
        Assert.NotEmpty(feedback.Scope.Caveats);
        // CTL only: an instructor-only account is refused by the role gate.
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/standards/feedback")).StatusCode);
    }

    [Fact] public async Task Ctl_links_articles_and_workshops_to_a_check_and_instructors_see_them_on_its_steps()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        Task<HttpResponseMessage> Link(HttpClient c, object body) => c.PostAsJsonAsync("/api/v1/standards/resources", body);
        var code = CourseChecks.CompetenciesMissing;
        Assert.Equal(HttpStatusCode.OK, (await Link(ctl, new { checkCode = code, articleId = DemoSeed.AltTextArticleId })).StatusCode);
        var linked = await Link(ctl, new { checkCode = code, workshopId = DemoSeed.RubricWorkshopId });
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var links = (await linked.Content.ReadFromJsonAsync<CheckResourceLinkDto[]>())!;
        Assert.Equal(["Article", "Workshop"], links.Select(l => l.Kind));

        // Invalid: the same resource twice, an unknown check, both or neither resource, a resource that does not exist. Instructors cannot link.
        Assert.Equal(HttpStatusCode.BadRequest, (await Link(ctl, new { checkCode = code, articleId = DemoSeed.AltTextArticleId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Link(ctl, new { checkCode = "NOPE", articleId = DemoSeed.AltTextArticleId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Link(ctl, new { checkCode = code, articleId = DemoSeed.AltTextArticleId, workshopId = DemoSeed.RubricWorkshopId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Link(ctl, new { checkCode = code })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Link(ctl, new { checkCode = code, articleId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Link(owner, new { checkCode = code, articleId = DemoSeed.AltTextArticleId })).StatusCode);

        async Task<GuideResourceDto[]> Resources() => [.. (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!.Steps.Single(s => s.CheckCode == code).Resources];
        var shown = await Resources();
        Assert.Equal(("Article", "writing-useful-image-alt-text"), (shown[0].Kind, shown[0].Slug));
        Assert.Equal(("Workshop", null), (shown[1].Kind, shown[1].MyStatus));
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/workshops/{DemoSeed.RubricWorkshopId}/register", new { })).StatusCode);
        Assert.Equal(WorkshopVocabulary.Registered, (await Resources())[1].MyStatus);

        // A draft article and a workshop that has started stay linked but are not shown to instructors.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.KnowledgeArticles.SingleAsync(a => a.Id == DemoSeed.AltTextArticleId)).Status = ReviewVocabulary.Draft;
            var w = await db.Workshops.SingleAsync(x => x.Id == DemoSeed.RubricWorkshopId);
            w.StartsAt = DateTime.UtcNow.AddHours(-1); w.EndsAt = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }
        Assert.Empty(await Resources());
        links = (await ctl.GetFromJsonAsync<CheckResourceLinkDto[]>("/api/v1/standards/resources"))!;
        Assert.All(links, l => Assert.StartsWith("Hidden", l.Status));

        Assert.Equal(HttpStatusCode.OK, (await ctl.DeleteAsync($"/api/v1/standards/resources/{links[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.DeleteAsync($"/api/v1/standards/resources/{links[0].Id}")).StatusCode);
        using var audit = f.Services.CreateScope();
        var log = audit.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs;
        Assert.Equal((2, 1), (await log.CountAsync(a => a.Action == "CheckResourceLinked"), await log.CountAsync(a => a.Action == "CheckResourceUnlinked")));
    }
}
