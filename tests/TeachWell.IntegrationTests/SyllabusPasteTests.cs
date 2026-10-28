using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using TeachWell.Infrastructure.Canvas;
using Xunit;

// Pasting the syllabus, because this college's syllabi are in Simple Syllabus (ADR 019 amendment).
public class SyllabusPasteTests
{
    private const string Course = "30000000-0000-0000-0000-000000000001", Other = "30000000-0000-0000-0000-000000000002";
    private static string Url(string id) => $"/api/v1/courses/{id}/syllabus";

    private const string Pasted =
        "STAT 110 Syllabus\n" +
        "Contact me through Canvas; office hours are Mondays 1-3pm.\n" +
        "Accessibility: students needing an accommodation should contact Disability Services.\n" +
        "Grading: the grade breakdown is 50% labs, 50% exam, on the college grading scale.\n" +
        "Late work loses 5% per day.\n" +
        "Academic integrity: submitted work must be your own work, with sources cited.";

    [Fact] public async Task The_owner_pastes_the_syllabus_and_a_reviewer_can_only_read_its_state()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");

        // The demo course has an imported Canvas syllabus to begin with.
        var before = (await owner.GetFromJsonAsync<CourseSyllabusDto>(Url(Course)))!;
        Assert.Equal("Canvas", before.Source);
        Assert.True(before.CanEdit);
        Assert.Contains("paste its text here instead", before.Note);

        var saved = await owner.PutAsJsonAsync(Url(Course), new { text = Pasted });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = (await saved.Content.ReadFromJsonAsync<CourseSyllabusDto>())!;
        Assert.Equal("Pasted", dto.Source);
        Assert.NotNull(dto.SuppliedAt);
        Assert.Equal("Alex Morgan", dto.SuppliedByName);
        Assert.True(dto.Words > 40, $"saw {dto.Words} words");
        Assert.Contains("importing the course again will not change or remove it", dto.Note);

        // One syllabus element, not two, holding the pasted text; the imported text is replaced and history is kept.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var element = await db.Elements.SingleAsync(e => e.CourseId == Guid.Parse(Course) && e.Type == ReviewVocabulary.SyllabusType);
            Assert.NotNull(element.SuppliedAt);
            Assert.Contains("Disability Services", element.PlainText);
            Assert.DoesNotContain("office hours are Tuesdays", element.PlainText);
            Assert.Null(element.SourceUrl);
            Assert.True(await db.ElementSnapshots.CountAsync(s => s.ElementId == element.Id) >= 2);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "SyllabusPasted"));
        }

        // A CTL reviewer with course access reads the state but not the text, and cannot paste.
        var seen = (await ctl.GetFromJsonAsync<CourseSyllabusDto>(Url(Course)))!;
        Assert.Equal("Pasted", seen.Source);
        Assert.False(seen.CanEdit);
        Assert.Null(seen.Text);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PutAsJsonAsync(Url(Course), new { text = Pasted })).StatusCode);
        // A course not shared with the reviewer is 404 even to read.
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync(Url(Other))).StatusCode);
    }

    [Fact] public async Task A_pasted_syllabus_survives_re_importing_the_course()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(Url(Course), new { text = Pasted })).StatusCode);

        static CanvasCourseData Import(params CanvasElementData[] elements) =>
            new(new("demo-1", "Foundations of Web Development", "WEB 101"), elements, []);

        // An import that carries its own syllabus must not overwrite what the owner supplied.
        await ApplyAsync(f, Import(new CanvasElementData(ReviewVocabulary.SyllabusSourceId, ReviewVocabulary.SyllabusType,
            "Syllabus", "A different syllabus from Canvas.", null, "Published")));
        var afterImport = (await owner.GetFromJsonAsync<CourseSyllabusDto>(Url(Course)))!;
        Assert.Equal("Pasted", afterImport.Source);
        Assert.Contains("Disability Services", afterImport.Text);

        // An import that carries none must not mark it removed either: it was never expected from Canvas.
        await ApplyAsync(f, Import());
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var element = await db.Elements.SingleAsync(e => e.CourseId == Guid.Parse(Course) && e.Type == ReviewVocabulary.SyllabusType);
            Assert.NotEqual("Removed", element.SourceStatus);
        }
        Assert.Equal("Pasted", (await owner.GetFromJsonAsync<CourseSyllabusDto>(Url(Course)))!.Source);
    }

    private static async Task ApplyAsync(Factory f, CanvasCourseData data)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await db.Courses.Include(c => c.Elements).ThenInclude(e => e.Snapshots).SingleAsync(c => c.Id == Guid.Parse(Course));
        CourseDataApplier.Apply(course, data, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    [Fact] public async Task Clearing_a_paste_hands_the_element_back_to_the_importer()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(Url(Course), new { text = Pasted })).StatusCode);

        var cleared = await owner.DeleteAsync(Url(Course));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var dto = (await cleared.Content.ReadFromJsonAsync<CourseSyllabusDto>())!;
        Assert.Equal("None", dto.Source);
        Assert.Contains("paste its text here", dto.Note);
        // Clearing twice is a 404, not a second clear.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync(Url(Course))).StatusCode);
        // A later import refills it, because provenance was handed back.
        await ApplyAsync(f, new CanvasCourseData(new("demo-1", "Foundations of Web Development", "WEB 101"),
            [new CanvasElementData(ReviewVocabulary.SyllabusSourceId, ReviewVocabulary.SyllabusType, "Syllabus", "The Canvas syllabus again.", null, "Published")], []));
        Assert.Equal("Canvas", (await owner.GetFromJsonAsync<CourseSyllabusDto>(Url(Course)))!.Source);
    }

    [Fact] public async Task Empty_and_oversized_pastes_are_rejected_and_pasted_html_is_sanitized()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor");

        foreach (var text in new[] { "", "   \n  \t ", "<p>  </p>" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(Url(Course), new { text })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(Url(Course), new { text = new string('x', 100_001) })).StatusCode);

        // A copy out of a web page arrives as HTML and goes through the same sanitizer as an import.
        var saved = await owner.PutAsJsonAsync(Url(Course), new { text = "<h2>Syllabus</h2><script>alert(1)</script><p>Late work loses 5% per day.</p><img src=\"x.png\">" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = (await saved.Content.ReadFromJsonAsync<CourseSyllabusDto>())!;
        Assert.DoesNotContain("alert", dto.Text);
        Assert.Contains("Late work loses 5%", dto.Text);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var element = await db.Elements.SingleAsync(e => e.CourseId == Guid.Parse(Course) && e.Type == ReviewVocabulary.SyllabusType);
        // Evidence is captured from pasted markup too, so the syllabus's own accessibility is checked: no alt text here.
        Assert.Equal([null], ContentEvidence.Parse(element.EvidenceJson)!.Images.Select(i => i.Alt));
    }

    [Fact] public async Task A_pasted_syllabus_is_checked_and_a_missing_one_is_stated_rather_than_passing()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f);
        await f.Seed();
        using (var scope = f.Services.CreateScope())
            await DemoSeed.SeedExtendedDemoDataAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
                scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Password"] = Factory.Password }).Build());
        await ApiTests.Login(owner, "instructor");

        // The pasted syllabus covers all five required elements, so WEB 101's open step clears.
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(Url(Course), new { text = Pasted })).StatusCode);
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.DoesNotContain(guide.Steps, s => s.CheckCode == SyllabusChecks.RequiredElementsMissing && s.OpenPlaces > 0);

        // Paste one that omits accessibility: the check runs against the pasted text and finds exactly that.
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(Url(Course),
            new { text = "Contact me at office hours. Grading: 50% labs. Late work loses 5% per day. Academic integrity: your own work." })).StatusCode);
        guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        var step = Assert.Single(guide.Steps, s => s.CheckCode == SyllabusChecks.RequiredElementsMissing);
        var place = Assert.Single(step.Locations);
        Assert.Contains("accessibility statement", place.Detail);

        // With no syllabus at all, silence would read as a clean syllabus, so the guide states the gap.
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync(Url(Course))).StatusCode);
        guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        Assert.DoesNotContain(guide.Steps, s => s.CheckCode == SyllabusChecks.RequiredElementsMissing);
        Assert.Contains(guide.Limitations, l => l.Contains("no syllabus in TeachWell") && l.Contains("Simple Syllabus"));
    }
}
