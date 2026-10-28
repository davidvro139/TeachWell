using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

// The conformance report endpoint (ROADMAP item 1).
public class ConformanceTests
{
    private const string Course = "30000000-0000-0000-0000-000000000001", Unshared = "30000000-0000-0000-0000-000000000002";
    private static string Url(string id) => $"/api/v1/courses/{id}/conformance";

    [Fact] public async Task Report_projects_the_guide_onto_criteria_and_never_claims_conformance()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");

        var report = (await owner.GetFromJsonAsync<CourseConformanceDto>(Url(Course)))!;
        Assert.Equal(Wcag.Version, report.Standard);
        // Every mapped criterion appears, in criterion order, whether or not its checks ran.
        Assert.Equal(Wcag.Criteria.Select(c => c.Number), report.Criteria.Select(c => c.Number));
        Assert.Equal(Wcag.Criteria.Count, report.CriteriaInScope);
        Assert.All(report.Criteria, c => Assert.Contains(c.State, new[] { Conformance.Findings, Conformance.NoFindings, Conformance.NotChecked }));
        Assert.Equal(report.Criteria.Count, report.CriteriaWithFindings + report.CriteriaWithNoFindings + report.CriteriaNotChecked);

        // The scope is stated, and the report says how much of the standard it does not cover.
        Assert.Equal(Conformance.Caveat, report.Caveat);
        Assert.True(report.CriteriaOutOfScope > 0);
        Assert.NotEqual(default, report.AsOf);

        // Figures agree with the guide they are projected from: nothing is recomputed.
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.Equal(guide.Standards.Name, report.Standards.Name);
        Assert.Equal(guide.ContentAsOf, report.ContentAsOf);
        Assert.Equal(guide.CheckedItems, report.CheckedItems);
        foreach (var criterion in report.Criteria)
        {
            var open = guide.Steps.Where(s => Wcag.For(s.CheckCode).Any(w => w.Number == criterion.Number))
                .Sum(s => s.Locations.Count(l => l.Status == GuideVocabulary.Open));
            Assert.Equal(open, criterion.OpenPlaces);
        }

        // WEB 101's seeded accessibility issues put at least one criterion into Findings.
        Assert.Contains(report.Criteria, c => c.State == Conformance.Findings && c.OpenPlaces > 0);

        // The baseline seed's only published standards are the college-wide v1, which predates the media checks, so
        // 1.2.2 is NotChecked even though the page has media. That is ADR 011 working: a new check reaches a course
        // only through a published version. Reporting it as NotChecked rather than clean is the point of the state.
        var captions = report.Criteria.Single(c => c.Number == Wcag.Captions.Number);
        Assert.Equal(Conformance.NotChecked, captions.State);
        Assert.Equal(0, captions.ChecksRan);
        Assert.Contains(report.Limitations, l => l.Contains("no check that ran on this course"));
        // Guide steps carry their criteria too, so an instructor sees what a fix is for.
        Assert.All(guide.Steps.Where(s => s.Aspect == ReviewAspects.Accessibility), s => Assert.NotEmpty(s.WcagCriteria));
        Assert.All(guide.Steps.Where(s => s.Aspect != ReviewAspects.Accessibility), s => Assert.Empty(s.WcagCriteria));
        // Reference is a computed property: assert it survives serialization, because the guide screen renders it.
        Assert.All(guide.Steps.SelectMany(s => s.WcagCriteria), c =>
        {
            Assert.Equal($"{Wcag.Version} {c.Number} {c.Name} (Level {c.Level})", c.Reference);
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
        });
        Assert.All(report.Criteria, c => Assert.False(string.IsNullOrWhiteSpace(c.Reference)));
    }

    // The demo the app and the public demo actually serve, where the CTL has published the Online standard set.
    [Fact] public async Task Under_the_extended_demo_standards_the_captions_criterion_reports_the_seeded_media()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed();
        using (var scope = f.Services.CreateScope())
            await DemoSeed.SeedExtendedDemoDataAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
                scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Password"] = Factory.Password }).Build());
        await ApiTests.Login(owner, "instructor");

        // WEB 101 is classified Online, so it runs the Online set, which includes the checks added since v1.
        var standards = (await owner.GetFromJsonAsync<CourseStandardsDto>($"/api/v1/courses/{Course}/standards"))!;
        Assert.Equal(StandardSetVocabulary.Online, standards.DeliveryMode);

        // Its welcome page has an uncaptioned player and an embed, so both media checks fire: this covers the whole
        // path from seeded HTML through capture and the checks to the guide and the conformance report.
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.Equal([MediaChecks.CaptionsMissing, MediaChecks.CaptionsUnconfirmed],
            guide.Steps.Where(s => s.WcagCriteria.Any(w => w.Number == Wcag.Captions.Number)).Select(s => s.CheckCode).Order());

        var captions = (await owner.GetFromJsonAsync<CourseConformanceDto>(Url(Course)))!.Criteria.Single(c => c.Number == Wcag.Captions.Number);
        Assert.Equal(Conformance.Findings, captions.State);
        Assert.True(captions.FullyCovered, "both captions checks should have run");
        Assert.Equal(2, captions.OpenPlaces);
    }

    [Fact] public async Task Fixing_every_place_for_a_criterion_moves_it_out_of_findings()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor");

        var before = (await owner.GetFromJsonAsync<CourseConformanceDto>(Url(Course)))!;
        var target = before.Criteria.First(c => c.State == Conformance.Findings);
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        var keys = guide.Steps.Where(s => s.WcagCriteria.Any(w => w.Number == target.Number))
            .SelectMany(s => s.Locations.Where(l => l.Status == GuideVocabulary.Open).Select(l => l.Key)).ToArray();
        Assert.NotEmpty(keys);

        var marked = await owner.PutAsJsonAsync($"/api/v1/courses/{Course}/guide/progress", new { keys, status = GuideVocabulary.Done });
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);

        var after = (await owner.GetFromJsonAsync<CourseConformanceDto>(Url(Course)))!;
        var row = after.Criteria.Single(c => c.Number == target.Number);
        Assert.Equal(Conformance.NoFindings, row.State);
        Assert.Equal(0, row.OpenPlaces);
        Assert.Equal(target.OpenPlaces, row.DonePlaces - target.DonePlaces);
    }

    [Fact] public async Task A_shared_reviewer_reads_it_and_an_unshared_course_is_not_found()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var admin = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(admin, "academic");

        Assert.Equal(HttpStatusCode.OK, (await ctl.GetAsync(Url(Course))).StatusCode);
        // No role grants course access by itself: the course is shared for review, the other is not.
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync(Url(Unshared))).StatusCode);
        // The Academic administrator never opens course content.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Url(Course))).StatusCode);
    }

    [Fact] public async Task Export_is_a_csv_carrying_the_scope_and_both_dates()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");

        var response = await owner.GetAsync(Url(Course) + "/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Criterion,Name,Level,State", csv);
        foreach (var criterion in Wcag.Criteria) Assert.Contains(criterion.Number, csv);
        Assert.Contains("Report generated", csv);
        Assert.Contains("Content captured", csv);
        Assert.Contains("Criteria needing manual review", csv);
        Assert.Contains("not a conformance claim", csv);

        // The export carries the same course access as the report: the reviewer has no share for this course.
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync(Url(Unshared) + "/export")).StatusCode);
    }
}
