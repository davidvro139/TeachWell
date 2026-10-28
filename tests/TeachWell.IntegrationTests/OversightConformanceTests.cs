using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

// The college-wide WCAG rollup on oversight (ADR 018 amendment).
public class OversightConformanceTests
{
    private const string Web101 = "30000000-0000-0000-0000-000000000001";

    [Fact] public async Task Open_accessibility_work_rolls_up_to_criteria_and_agrees_with_the_courses_own_report()
    {
        using var f = new Factory(); using var academic = ApiTests.Client(f); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(academic, "academic"); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");

        // Guide records are written when a guide is built, so build them before reading the rollup.
        Assert.Equal(HttpStatusCode.OK, (await ctl.PostAsJsonAsync("/api/v1/oversight/refresh", new { })).StatusCode);
        var view = (await academic.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!;

        // Every mapped criterion appears, in criterion order, whether or not any course has open work for it.
        Assert.Equal(Wcag.Criteria.Select(c => c.Number), view.Conformance.Select(c => c.Number));
        Assert.All(view.Conformance, c =>
        {
            Assert.NotEmpty(c.Checks);
            Assert.All(c.Checks, code => Assert.NotNull(CheckRegistry.Find(code)));
            // A course is counted once per criterion however many of its checks are open, so this always holds.
            Assert.True(c.CoursesAffected <= view.CoursesChecked, $"{c.Number}: {c.CoursesAffected} of {view.CoursesChecked}");
            Assert.True(c.OpenPlaces >= c.CoursesAffected);
        });
        // The demo courses have seeded accessibility issues, so at least one criterion carries work.
        Assert.Contains(view.Conformance, c => c.CoursesAffected > 0 && c.OpenPlaces > 0);

        // The rollup is the same arithmetic as the per-course report, so WEB 101's own figures sit inside the totals.
        var report = (await owner.GetFromJsonAsync<CourseConformanceDto>($"/api/v1/courses/{Web101}/conformance"))!;
        foreach (var criterion in report.Criteria.Where(c => c.OpenPlaces > 0))
        {
            var row = view.Conformance.Single(c => c.Number == criterion.Number);
            Assert.True(row.OpenPlaces >= criterion.OpenPlaces, $"{criterion.Number}: college {row.OpenPlaces} < course {criterion.OpenPlaces}");
            Assert.True(row.CoursesAffected >= 1);
        }

        // The scope says what the rollup cannot show, which is the difference between it and the per-course report.
        Assert.Contains(view.Scope.Caveats, c => c.Contains("not distinguished from one where that criterion's checks did not run"));
        Assert.Contains(view.Scope.Caveats, c => c.Contains($"out of {Conformance.ReferenceCriteria}"));
    }

    [Fact] public async Task Fixing_a_courses_places_lowers_the_college_figure()
    {
        using var f = new Factory(); using var academic = ApiTests.Client(f); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(academic, "academic"); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        Assert.Equal(HttpStatusCode.OK, (await ctl.PostAsJsonAsync("/api/v1/oversight/refresh", new { })).StatusCode);

        var before = (await academic.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!.Conformance;
        var target = before.First(c => c.OpenPlaces > 0);

        // Mark every open place for that criterion on WEB 101 as done.
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Web101}/guide"))!;
        var keys = guide.Steps.Where(s => s.WcagCriteria.Any(w => w.Number == target.Number))
            .SelectMany(s => s.Locations.Where(l => l.Status == GuideVocabulary.Open).Select(l => l.Key)).ToArray();
        Assert.NotEmpty(keys);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/courses/{Web101}/guide/progress", new { keys, status = GuideVocabulary.Done })).StatusCode);

        var after = (await academic.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!.Conformance.Single(c => c.Number == target.Number);
        Assert.Equal(target.OpenPlaces - keys.Length, after.OpenPlaces);
    }

    [Fact] public async Task The_export_carries_the_rollup_with_its_scope_and_caveats()
    {
        using var f = new Factory(); using var academic = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(academic, "academic"); await ApiTests.Login(ctl, "reviewer");
        Assert.Equal(HttpStatusCode.OK, (await ctl.PostAsJsonAsync("/api/v1/oversight/refresh", new { })).StatusCode);

        var response = await academic.GetAsync("/api/v1/oversight/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Success criterion,Name,Level,Courses with open work", csv);
        foreach (var criterion in Wcag.Criteria) Assert.Contains(criterion.Number, csv);
        Assert.Contains("Courses checked", csv);
        Assert.Contains("Generated (UTC)", csv);
        // The caveats travel with the figures, so a downloaded row cannot be read as a conformance claim.
        Assert.Contains("Caveat", csv);
        Assert.Contains("not accreditation, compliance or certification", csv);
    }

    [Fact] public async Task A_course_never_checked_contributes_nothing_rather_than_counting_as_clean()
    {
        using var f = new Factory(); using var academic = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(academic, "academic");

        // No guide has been built, so no course has a record: the rollup is all zeros, and says so by its course count.
        var view = (await academic.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!;
        Assert.Equal(0, view.CoursesChecked);
        Assert.True(view.Courses > 0);
        Assert.All(view.Conformance, c => Assert.Equal((0, 0), (c.CoursesAffected, c.OpenPlaces)));

        using var scope = f.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().CourseGuideRecords.CountAsync());
    }
}
