using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class StandardSetTests
{
    private const string SharedCourse = "30000000-0000-0000-0000-000000000001", PrivateCourse = "30000000-0000-0000-0000-000000000002";

    private static async Task<T> Ok<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<HttpResponseMessage> Save(HttpClient c, StandardSetVersionDto v, IEnumerable<StandardCheckSetting> checks, string notes = "") =>
        c.PutAsJsonAsync($"/api/v1/standard-sets/{v.SetId}/versions/{v.Id}", new { revision = v.Revision, notes, checks });
    private static Task<HttpResponseMessage> Publish(HttpClient c, StandardSetVersionDto v) =>
        c.PostAsJsonAsync($"/api/v1/standard-sets/{v.SetId}/versions/{v.Id}/publish", new { revision = v.Revision });
    private static List<StandardCheckSetting> Change(StandardSetVersionDto v, string code, Func<StandardCheckSetting, StandardCheckSetting> change) =>
        v.Checks.Select(c => c.Code == code ? change(c) : c).ToList();
    private static async Task<StandardSetVersionDto> CreatePublished(HttpClient c, string name, string? mode, string? department)
    {
        var draft = await Ok<StandardSetVersionDto>(await c.PostAsJsonAsync("/api/v1/standard-sets", new { name, deliveryMode = mode, department }));
        return await Ok<StandardSetVersionDto>(await Publish(c, draft));
    }
    private static async Task<AppliedStandardsDto> Applied(HttpClient c, string course) =>
        (await c.GetFromJsonAsync<CourseStandardsDto>($"/api/v1/courses/{course}/standards"))!.Applied;
    private static async Task<StandardSetVersionDto> CollegeDraft(HttpClient c) =>
        await Ok<StandardSetVersionDto>(await c.PostAsync($"/api/v1/standard-sets/{DemoSeed.CollegeStandardSetId}/versions", null));

    [Fact] public async Task Ctl_drafts_edits_and_publishes_a_version_that_is_then_immutable_and_audited()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");

        var catalog = (await ctl.GetFromJsonAsync<CheckCatalogEntryDto[]>("/api/v1/review-checks"))!;
        Assert.Equal(CheckRegistry.All.Select(c => c.Code), catalog.Select(c => c.Code));
        Assert.Contains(catalog.Single(c => c.Code == DeterministicChecks.LinkVague).Parameters, p => p.Key == DeterministicChecks.VaguePhrases && p.Kind == "WordList");
        var seeded = Assert.Single((await ctl.GetFromJsonAsync<StandardSetDto[]>("/api/v1/standard-sets"))!);
        Assert.Equal(("College-wide", "Published"), (seeded.Scope, seeded.Versions.Single().Status));

        var draft = await Ok<StandardSetVersionDto>(await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "Online standards", deliveryMode = "Online" }));
        Assert.Equal(("Draft", 1, "Delivery mode: Online"), (draft.Status, draft.VersionNumber, draft.Scope));
        Assert.Contains("College-wide course standards (demo) v1", draft.Notes);
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PostAsync($"/api/v1/standard-sets/{draft.SetId}/versions", null)).StatusCode);

        var checks = Change(draft, DeterministicChecks.ImageAltMissing, c => c with { Severity = "Critical", Required = true, Guidance = c.Guidance with { Why = "Our online students rely on screen readers." } });
        checks = checks.Select(c => c.Code == DeterministicChecks.LinkVague ? c with { Lists = new Dictionary<string, IReadOnlyList<string>> { [DeterministicChecks.VaguePhrases] = ["  click here ", "video"] } } : c).ToList();
        var saved = await Ok<StandardSetVersionDto>(await Save(ctl, draft, checks, "Stricter accessibility for online courses."));
        var alt = saved.Checks.Single(c => c.Code == DeterministicChecks.ImageAltMissing);
        Assert.Equal(("Critical", true, "Our online students rely on screen readers."), (alt.Severity, alt.Required, alt.Guidance.Why));
        Assert.Equal(["click here", "video"], saved.Checks.Single(c => c.Code == DeterministicChecks.LinkVague).Lists[DeterministicChecks.VaguePhrases]);
        Assert.Equal(HttpStatusCode.Conflict, (await Save(ctl, draft, checks)).StatusCode); // stale revision

        var published = await Ok<StandardSetVersionDto>(await Publish(ctl, saved));
        Assert.Equal(("Published", "Jordan Lee"), (published.Status, published.PublishedByName));
        Assert.Equal(HttpStatusCode.Conflict, (await Save(ctl, published, checks)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Publish(ctl, published)).StatusCode);
        var next = await Ok<StandardSetVersionDto>(await ctl.PostAsync($"/api/v1/standard-sets/{draft.SetId}/versions", null));
        Assert.Equal((2, "Critical"), (next.VersionNumber, next.Checks.Single(c => c.Code == DeterministicChecks.ImageAltMissing).Severity));

        // Instructors and anonymous callers cannot reach the library at all.
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/standard-sets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/review-checks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/standard-sets", new { name = "Mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/v1/standard-sets/{next.SetId}/versions/{next.Id}", new { revision = next.Revision, checks = next.Checks })).StatusCode);
        using var anonymous = ApiTests.Client(f);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/standard-sets")).StatusCode);
        await ApiTests.Csrf(anonymous);
        ctl.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "No CSRF" })).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(new[] { "StandardSetCreated", "StandardSetDraftUpdated", "StandardSetVersionPublished", "StandardSetVersionDrafted" },
            await db.AuditLogs.Where(a => a.Action.StartsWith("StandardSet")).OrderBy(a => a.Timestamp).Select(a => a.Action).ToArrayAsync());
        var stored = await db.StandardSetVersions.SingleAsync(v => v.Id == published.Id);
        stored.Notes = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        if (db.Database.IsNpgsql())
        {
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"StandardSetVersions\" SET \"Notes\" = 'x' WHERE \"Status\" = 'Published'"));
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"StandardSetVersions\" WHERE \"Status\" = 'Published'"));
        }
    }

    [Fact] public async Task Invalid_settings_scopes_and_names_are_rejected_with_field_errors()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "", deliveryMode = "Correspondence", department = new string('d', 121) })).StatusCode);
        // The college-wide scope is already taken by the seeded set; department matching ignores case and spacing.
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "Another college set" })).StatusCode);
        await Ok<StandardSetVersionDto>(await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "Computing", department = "Computing" }));
        Assert.Equal(HttpStatusCode.Conflict, (await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "Computing again", department = "  computing " })).StatusCode);

        var draft = await CollegeDraft(ctl);
        async Task<string> Rejected(IEnumerable<StandardCheckSetting>? checks, string notes = "")
        {
            var response = await Save(ctl, draft, checks!, notes);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        Assert.Contains("Settings are missing for", await Rejected(draft.Checks.Skip(1)));
        Assert.Contains("Unknown check", await Rejected([.. draft.Checks, draft.Checks[0] with { Code = "NOT_A_CHECK" }]));
        Assert.Contains("Severity must be", await Rejected(Change(draft, DeterministicChecks.LinkEmpty, c => c with { Severity = "Urgent" })));
        Assert.Contains("at most 100", await Rejected(Change(draft, DeterministicChecks.LinkVague,
            c => c with { Lists = new Dictionary<string, IReadOnlyList<string>> { [DeterministicChecks.VaguePhrases] = Enumerable.Range(0, 101).Select(i => $"p{i}").ToList() } })));
        Assert.Contains("must be enabled", await Rejected(Change(draft, DeterministicChecks.LinkEmpty, c => c with { Enabled = false, Required = true })));
        Assert.Contains("How to fix it", await Rejected(Change(draft, DeterministicChecks.LinkEmpty, c => c with { Guidance = c.Guidance with { HowToFix = " " } })));
        Assert.Contains("Notes are limited", await Rejected(draft.Checks, new string('n', 2001)));
        // Nothing invalid was stored.
        Assert.Equal(draft.Revision, (await Ok<StandardSetVersionDto>(await ctl.GetAsync($"/api/v1/standard-sets/{draft.SetId}/versions/{draft.Id}"))).Revision);
    }

    [Fact] public async Task The_most_specific_published_set_applies_and_only_ctl_with_course_access_classifies_courses()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");

        var college = await Applied(owner, SharedCourse);
        Assert.Equal(("College-wide course standards (demo)", 1), (college.Name, college.VersionNumber));
        Assert.Contains("no delivery mode or department recorded", college.Reason);

        await CreatePublished(ctl, "Online", "Online", null);
        await CreatePublished(ctl, "Computing", null, "Computing");
        await CreatePublished(ctl, "Computing online", "Online", "Computing");
        // A set with only a draft never applies.
        await Ok<StandardSetVersionDto>(await ctl.PostAsJsonAsync("/api/v1/standard-sets", new { name = "Hybrid (draft only)", deliveryMode = "Hybrid" }));

        async Task<string> Classify(string? mode, string? department)
        {
            var result = await Ok<CourseStandardsDto>(await ctl.PutAsJsonAsync($"/api/v1/courses/{SharedCourse}/classification", new { deliveryMode = mode, department }));
            Assert.True(result.CanClassify);
            return result.Applied.Name;
        }
        Assert.Equal("Computing online", await Classify("Online", "computing"));
        Assert.Equal("Computing", await Classify("FaceToFace", "Computing"));
        Assert.Equal("Online", await Classify("Online", "Design"));
        Assert.Equal("College-wide course standards (demo)", await Classify("Hybrid", "Design"));
        Assert.Contains("no published set targets", (await Applied(ctl, SharedCourse)).Reason);

        var seen = await owner.GetFromJsonAsync<CourseStandardsDto>($"/api/v1/courses/{SharedCourse}/standards");
        Assert.Equal(("Hybrid", "Design", false), (seen!.DeliveryMode, seen.Department, seen.CanClassify));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/v1/courses/{SharedCourse}/classification", new { deliveryMode = "Online" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PutAsJsonAsync($"/api/v1/courses/{PrivateCourse}/classification", new { deliveryMode = "Online" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/courses/{PrivateCourse}/standards")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PutAsJsonAsync($"/api/v1/courses/{SharedCourse}/classification", new { deliveryMode = "Correspondence" })).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.AuditLogs.CountAsync(a => a.Action == "CourseClassificationChanged" && a.EntityId == Guid.Parse(SharedCourse)));
        await Classify("Hybrid", "Design");
        Assert.Equal(4, await db.AuditLogs.CountAsync(a => a.Action == "CourseClassificationChanged")); // unchanged values are not re-audited
    }

    [Fact] public async Task Reviews_pin_the_standard_set_version_they_started_under_and_later_versions_change_only_new_reviews()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        var assignment = await ReviewTests.ElementId(ctl, SharedCourse, "assignment");

        var before = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{assignment}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        before = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{before.Id}/audit", new { revision = before.Revision }));
        Assert.Equal((DemoSeed.CollegeStandardVersionId, 1), (before.Standards!.VersionId, before.Standards.VersionNumber));
        Assert.Contains(before.Findings, x => x.CheckCode == DeterministicChecks.ImageAltMissing);
        Assert.Equal("Medium", before.Findings.Single(x => x.CheckCode == DeterministicChecks.TableHeaders).Severity);
        Assert.Equal(DefaultGuidance.For(DeterministicChecks.ImageAltMissing), before.Guidance![DeterministicChecks.ImageAltMissing]);

        // CTL publishes v2: image alt check off, table headers raised to High, new guidance for table headers.
        var draft = await CollegeDraft(ctl);
        var checks = Change(draft, DeterministicChecks.ImageAltMissing, c => c with { Enabled = false });
        checks = checks.Select(c => c.Code == DeterministicChecks.TableHeaders ? c with { Severity = "High", Guidance = c.Guidance with { Why = "Tables are how we publish schedules." } } : c).ToList();
        var v2 = await Ok<StandardSetVersionDto>(await Publish(ctl, await Ok<StandardSetVersionDto>(await Save(ctl, draft, checks))));

        // The earlier review keeps running under v1, exactly as before.
        var rerun = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{before.Id}/audit", new { revision = before.Revision }));
        Assert.Equal(1, rerun.Standards!.VersionNumber);
        Assert.Equal(before.Findings.Select(x => (x.Id, x.Severity)), rerun.Findings.Select(x => (x.Id, x.Severity)));

        // Publish the first review (confirm the alt-text finding, reject the rest) so the same assignment can be reviewed again.
        var review = rerun;
        foreach (var finding in rerun.Findings)
            review = await ReviewTests.Ok(await ctl.PatchAsJsonAsync($"/api/v1/findings/{finding.Id}",
                new { revision = review.Revision, status = finding.CheckCode == DeterministicChecks.ImageAltMissing ? "Confirmed" : "Rejected" }));
        review = await ReviewTests.Ok(await ctl.PutAsJsonAsync($"/api/v1/reviews/{review.Id}/criteria/{ReviewTests.Criterion(review, "ACC-1")}",
            new { revision = review.Revision, outcome = "Rated", rating = 1, comment = "", evidence = "" }));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/publish", new { revision = review.Revision, summary = "Add alt text." }));

        // The owner reads the published review with the v1 guidance for its one confirmed finding only.
        var shared = await ReviewTests.Ok(await owner.GetAsync($"/api/v1/reviews/{review.Id}"));
        Assert.Equal(1, shared.Standards!.VersionNumber);
        Assert.Equal([DeterministicChecks.ImageAltMissing], shared.Guidance!.Keys);

        // A review started now pins v2, so the disabled check proposes nothing and the new severity and guidance apply.
        var after = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{assignment}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        after = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{after.Id}/audit", new { revision = after.Revision }));
        Assert.Equal((v2.Id, 2), (after.Standards!.VersionId, after.Standards.VersionNumber));
        Assert.DoesNotContain(after.Findings, x => x.CheckCode == DeterministicChecks.ImageAltMissing);
        Assert.Equal("High", after.Findings.Single(x => x.CheckCode == DeterministicChecks.TableHeaders).Severity);
        Assert.Equal("Tables are how we publish schedules.", after.Guidance![DeterministicChecks.TableHeaders].Why);
        Assert.Equal(after.Findings.Select(x => x.CheckCode).OfType<string>().Distinct().Order(), after.Guidance.Keys.Order());

        // The published v1 review is untouched by v2.
        var reread = await ReviewTests.Ok(await owner.GetAsync($"/api/v1/reviews/{review.Id}"));
        Assert.Equal(("High", 1), (reread.Findings.Single().Severity, reread.Standards!.VersionNumber));
        Assert.Equal(DefaultGuidance.For(DeterministicChecks.ImageAltMissing).Why, reread.Guidance![DeterministicChecks.ImageAltMissing].Why);
    }

    [Fact] public async Task Preview_compares_a_draft_with_the_courses_current_standards_without_changing_anything()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var draft = await CollegeDraft(ctl);
        draft = await Ok<StandardSetVersionDto>(await Save(ctl, draft, Change(draft, DeterministicChecks.ImageAltMissing, c => c with { Enabled = false })));

        var preview = await Ok<StandardsPreviewDto>(await ctl.PostAsJsonAsync($"/api/v1/standard-sets/{draft.SetId}/versions/{draft.Id}/preview", new { courseId = SharedCourse }));
        Assert.Equal(("College-wide course standards (demo)", 2), (preview.Current.Name, preview.ElementsChecked));
        var alt = preview.Rows.Single(r => r.Code == DeterministicChecks.ImageAltMissing);
        Assert.True(alt.Current > 0);
        Assert.Equal((false, 0), (alt.Enabled, alt.Proposed));
        // The rest of the original checks are unchanged. The draft also adopted the checks added since v1, which v1 does not run.
        Assert.All(preview.Rows.Where(r => CheckRegistry.InitialCodes.Contains(r.Code) && r.Code != DeterministicChecks.ImageAltMissing), r => Assert.Equal(r.Current, r.Proposed));
        var added = preview.Rows.Where(r => !CheckRegistry.InitialCodes.Contains(r.Code)).ToList();
        Assert.Equal(CheckRegistry.All.Count - CheckRegistry.InitialCodes.Count, added.Count);
        Assert.All(added, r => Assert.Equal((true, 0), (r.Enabled, r.Current)));
        Assert.Equal(preview.CurrentTotal - alt.Current + added.Sum(r => r.Proposed), preview.ProposedTotal);

        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PostAsJsonAsync($"/api/v1/standard-sets/{draft.SetId}/versions/{draft.Id}/preview", new { courseId = PrivateCourse })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PostAsJsonAsync($"/api/v1/standard-sets/{draft.SetId}/versions/{Guid.NewGuid()}/preview", new { courseId = SharedCourse })).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Elements.AnyAsync(e => e.Type == ReviewVocabulary.CourseType)); // the structure was built for the preview, not saved
    }
}
