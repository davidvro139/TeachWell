using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using TeachWell.Infrastructure.Canvas;
using Xunit;

// The syllabus's required elements, and the vocabulary decision that keeps the syllabus out of module content
// (ROADMAP items 4 and 4a, ADR 019).
public class SyllabusCheckUnitTests
{
    private static readonly CheckProfile Only = new(CheckRegistry.All.ToDictionary(
        c => c.Code, c => c.Defaults() with { Enabled = c.Code == SyllabusChecks.RequiredElementsMissing }));

    private static string[] Missing(string text) =>
        DeterministicChecks.Run(ReviewVocabulary.SyllabusType, "Published", null, Only, text).Findings.Select(f => f.Fingerprint.Split(':')[1]).ToArray();

    // A syllabus that mentions every element the defaults look for.
    private const string Complete =
        "Contact me at the address above; my office hours are Tuesdays 2-4. " +
        "Grading: the grade breakdown is 40% labs, 60% exam, on the scale below. " +
        "Late work loses 10% per day. " +
        "Academic integrity: all submitted work must be your own work. " +
        "Accessibility: students needing an accommodation should contact disability services.";

    [Fact] public void Each_required_element_the_syllabus_does_not_mention_is_a_separate_finding()
    {
        Assert.Empty(Missing(Complete));
        // Drop the accessibility sentence: exactly one element is reported, and the finding names it.
        var withoutAccess = Complete[..Complete.IndexOf("Accessibility:", StringComparison.Ordinal)];
        Assert.Equal([SyllabusChecks.Accessibility], Missing(withoutAccess));

        var bare = DeterministicChecks.Run(ReviewVocabulary.SyllabusType, "Published", null, Only, "This course covers statistics.").Findings;
        // All five elements, each its own place, so an instructor can fix and tick them one at a time.
        Assert.Equal(5, bare.Count);
        Assert.Equal(bare.Count, bare.Select(f => f.Fingerprint).Distinct().Count());
        var accessibility = Assert.Single(bare, f => f.Fingerprint.EndsWith(SyllabusChecks.Accessibility));
        Assert.Equal("Syllabus does not mention an accessibility statement", accessibility.Title);
        Assert.Contains("accommodation", accessibility.Description);
        // The evidence states what was searched for, so the instructor can see why it did not match.
        Assert.Contains("Searched the syllabus text", accessibility.Evidence);
        Assert.Contains("disability", accessibility.Evidence);
        // It asks rather than asserts: a syllabus may cover the policy in words no keyword list anticipates.
        Assert.Contains("already there in different words", accessibility.Recommendation);
    }

    [Fact] public void The_check_runs_only_on_the_syllabus()
    {
        // The same bare text on a page or an assignment produces nothing: this is not a check about every page.
        foreach (var type in new[] { "page", "assignment", ReviewVocabulary.QuizType, ReviewVocabulary.DiscussionType })
            Assert.Empty(DeterministicChecks.Run(type, "Published", null, Only, "This course covers statistics.").Findings);
    }

    [Fact] public void A_college_that_does_not_require_an_element_empties_its_keyword_list()
    {
        var relaxed = CheckRegistry.Find(SyllabusChecks.RequiredElementsMissing)!.Defaults();
        var lists = relaxed.Lists.ToDictionary(kv => kv.Key, kv => kv.Value);
        lists[SyllabusChecks.LateWork] = [];
        lists[SyllabusChecks.AcademicIntegrity] = [];
        var profile = new CheckProfile(CheckRegistry.All.ToDictionary(c => c.Code,
            c => c.Code == SyllabusChecks.RequiredElementsMissing ? relaxed with { Lists = lists } : c.Defaults() with { Enabled = false }));

        var found = DeterministicChecks.Run(ReviewVocabulary.SyllabusType, "Published", null, profile, "This course covers statistics.")
            .Findings.Select(f => f.Fingerprint.Split(':')[1]).ToArray();
        Assert.Equal([SyllabusChecks.Accessibility, SyllabusChecks.Contact, SyllabusChecks.Grading], found);

        // An empty list is still valid settings: emptying one is how a college opts out, not an error.
        Assert.Empty(CheckRegistry.Find(SyllabusChecks.RequiredElementsMissing)!.Validate(relaxed with { Lists = lists }));
    }

    [Fact] public void Without_the_syllabus_text_the_check_reports_a_limitation_instead_of_findings()
    {
        var report = DeterministicChecks.Run(ReviewVocabulary.SyllabusType, "Published", null, Only, null);
        Assert.Empty(report.Findings);
        Assert.Contains(report.Limitations, l => l.Contains("text was not available"));
    }

    [Fact] public void The_syllabus_is_checked_but_is_not_module_content()
    {
        // The distinction this slice turns on. CheckedTypes decides what the element checks run against; ElementTypes
        // means "content placed in modules", and the syllabus is a course-level page.
        Assert.Contains(ReviewVocabulary.SyllabusType, ReviewVocabulary.CheckedTypes);
        Assert.True(ReviewVocabulary.IsChecked(ReviewVocabulary.SyllabusType));
        Assert.DoesNotContain(ReviewVocabulary.SyllabusType, ReviewVocabulary.ElementTypes);
        Assert.False(ReviewVocabulary.IsContent(ReviewVocabulary.SyllabusType));
        // Were it content, CONTENT_NOT_IN_MODULE would fire on every course that has a syllabus.
        Assert.DoesNotContain(ReviewVocabulary.SyllabusType, ReviewVocabulary.CriterionTargets);
        Assert.Equal("Syllabus", CourseStructureEvidence.Label(ReviewVocabulary.SyllabusType));
    }

    [Fact] public void An_export_carries_the_course_syllabus_separately_from_a_page_called_syllabus()
    {
        var fixture = new ExportFixture
        {
            CourseSyllabusBody = "<html><body><h2>STAT 110</h2><p>Grading: 40% labs, 60% exam. Late work loses 10% per day.</p>" +
                "<img src=\"seal.png\"></body></html>"
        };
        var data = CanvasExportReader.Read(new MemoryStream(fixture.Build()));

        var syllabus = Assert.Single(data.Elements, e => e.Type == ReviewVocabulary.SyllabusType);
        Assert.Equal(ReviewVocabulary.SyllabusSourceId, syllabus.Id);
        Assert.Equal("Syllabus", syllabus.Title);
        Assert.Contains("40% labs", syllabus.PlainText);
        // Its own content is checked like any page's: the image here has no alt text.
        Assert.Equal([null], syllabus.Evidence!.Images.Select(i => i.Alt));

        // The wiki page that merely happens to be called "Syllabus" is still a separate page element.
        Assert.Contains(data.Elements, e => e.Type == "page" && e.Title == "Syllabus");

        // An export with no course syllabus produces no syllabus element, rather than an empty one.
        Assert.DoesNotContain(CanvasExportReader.Read(new MemoryStream(new ExportFixture().Build())).Elements,
            e => e.Type == ReviewVocabulary.SyllabusType);
        // Nor does a syllabus file that holds only markup and whitespace.
        Assert.DoesNotContain(CanvasExportReader.Read(new MemoryStream(new ExportFixture { CourseSyllabusBody = "<html><body><p>  </p></body></html>" }.Build())).Elements,
            e => e.Type == ReviewVocabulary.SyllabusType);
    }
}

// The syllabus through the whole stack (ROADMAP item 4a, ADR 019).
public class SyllabusTests
{
    private const string Course = "30000000-0000-0000-0000-000000000001";

    [Fact] public async Task The_syllabus_is_imported_and_checked_but_never_reported_as_content_outside_a_module()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor");

        // It is imported as a course-level element with no module.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var syllabus = await db.Elements.SingleAsync(e => e.CourseId == Guid.Parse(Course) && e.Type == ReviewVocabulary.SyllabusType);
            Assert.Equal(ReviewVocabulary.SyllabusSourceId, syllabus.SourceElementId);
            Assert.Null(syllabus.ParentElementId);
            Assert.Contains("office hours", syllabus.PlainText);
        }

        // The regression this slice turns on: the syllabus has no module by design, so CONTENT_NOT_IN_MODULE — which
        // is enabled in the seeded college-wide standards — must not fire on it.
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        var unplaced = guide.Steps.SingleOrDefault(s => s.CheckCode == CourseChecks.ContentUnplaced);
        Assert.DoesNotContain("Syllabus", unplaced?.Locations.Select(l => l.Where) ?? []);

        // Its own content is still checked: the demo syllabus is reached by the element checks like any page.
        Assert.True(guide.CheckedItems >= 3, $"page, assignment and syllabus should all be checked, saw {guide.CheckedItems}");
    }

    [Fact] public async Task Under_the_extended_demo_standards_the_missing_required_element_becomes_a_guide_step()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed();
        using (var scope = f.Services.CreateScope())
            await DemoSeed.SeedExtendedDemoDataAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
                scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Password"] = Factory.Password }).Build());
        await ApiTests.Login(owner, "instructor");

        // WEB 101 is Online, so it runs the Online set, which includes the checks added since the college-wide v1.
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        var step = Assert.Single(guide.Steps, s => s.CheckCode == SyllabusChecks.RequiredElementsMissing);

        // WEB 101's syllabus covers contact, grading, late work and integrity, and omits only accessibility.
        var place = Assert.Single(step.Locations);
        Assert.Equal(GuideVocabulary.Open, place.Status);
        Assert.Equal("Syllabus: Syllabus", place.Where);
        Assert.Null(place.Module);
        Assert.Contains("accessibility statement", place.Detail);
        Assert.Empty(step.WcagCriteria); // a policy requirement, not a success criterion
    }
}
