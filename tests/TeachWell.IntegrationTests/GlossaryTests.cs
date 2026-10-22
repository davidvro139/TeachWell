using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class GlossaryTests
{
    private static async Task<GlossaryEntryDto> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GlossaryEntryDto>())!;
    }
    private static Task<HttpResponseMessage> Save(HttpClient c, string termId, object input) => c.PutAsJsonAsync($"/api/v1/glossary/{termId}", input);
    private static async Task<GlossaryEntryDto[]> List(HttpClient c) => (await c.GetFromJsonAsync<GlossaryEntryDto[]>("/api/v1/glossary"))!;

    [Fact] public async Task Ctl_rewords_a_built_in_term_adds_its_own_hides_one_and_resets_while_instructors_read_the_result()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        Assert.Empty(await List(owner));

        // Reword a built-in term, adding the college's own reading.
        var blooms = await Ok(await Save(ctl, "blooms-taxonomy", new
        {
            term = "  Bloom's taxonomy  ", definition = "Our college uses the revised taxonomy in every syllabus.",
            phrases = new[] { "Bloom's taxonomy", " bloom's taxonomy ", "", "Bloom" }, tags = new[] { "blooms" },
            links = new[] { new { label = "Our outcome-writing guide", source = "Demo College CTL", url = "https://ctl.example.edu/outcomes" } }
        }));
        Assert.Equal(("Bloom's taxonomy", "Jordan Lee"), (blooms.Term, blooms.UpdatedByName));
        Assert.Equal(["Bloom's taxonomy", "Bloom"], blooms.Phrases); // trimmed, blanks and case-insensitive duplicates dropped
        Assert.Equal("https://ctl.example.edu/outcomes", Assert.Single(blooms.Links).Url);

        // A term of the college's own.
        var shell = await Ok(await Save(ctl, "course-shell", new
        {
            term = "Course shell", definition = "The empty Canvas course the registrar creates each term.", phrases = new[] { "course shell", "course shells" },
            tags = Array.Empty<string>(), links = Array.Empty<object>()
        }));
        // Hide a built-in term the college does not use.
        var hidden = await Ok(await Save(ctl, "scaffolding", new { hidden = true }));
        Assert.True(hidden.Hidden);

        var seen = await List(owner);
        Assert.Equal(["blooms-taxonomy", "course-shell", "scaffolding"], seen.Select(e => e.TermId));
        Assert.Equal("Our college uses the revised taxonomy in every syllabus.", seen[0].Definition);

        // Editing needs the current revision, so two CTL reviewers cannot silently overwrite each other.
        Assert.Equal(HttpStatusCode.Conflict, (await Save(ctl, "course-shell", new
        {
            term = "Course shell", definition = "Changed.", revision = Guid.NewGuid()
        })).StatusCode);
        shell = await Ok(await Save(ctl, "course-shell", new { term = "Course shell", definition = "Changed.", revision = shell.Revision }));
        Assert.Equal("Changed.", shell.Definition);

        // Reset returns a built-in term to its shipped wording and removes a college term.
        var reset = await ctl.DeleteAsync("/api/v1/glossary/blooms-taxonomy");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.DoesNotContain(await reset.Content.ReadFromJsonAsync<GlossaryEntryDto[]>() ?? [], e => e.TermId == "blooms-taxonomy");
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.DeleteAsync("/api/v1/glossary/blooms-taxonomy")).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Equal(3, actions.Count(a => a == "GlossaryTermSaved"));
        Assert.Contains("GlossaryTermHidden", actions);
        Assert.Contains("GlossaryTermReset", actions);
    }

    [Fact] public async Task Only_ctl_reviewers_change_the_glossary_and_other_roles_cannot_read_it()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var academic = ApiTests.Client(f); using var anonymous = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(academic, "academic");
        var input = new { term = "Course shell", definition = "The empty Canvas course." };
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(owner, "course-shell", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.DeleteAsync("/api/v1/glossary/blooms-taxonomy")).StatusCode);
        // Academic administrators see results, not the course-level help tools (ADR 014).
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.GetAsync("/api/v1/glossary")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/glossary")).StatusCode);
    }

    [Theory]
    [InlineData("Course_Shell", "Course shell", "A definition.", null, "termId")]
    [InlineData("course-shell", "", "A definition.", null, "term")]
    [InlineData("course-shell", "Course shell", "", null, "definition")]
    [InlineData("course-shell", "Course shell", "A definition.", "javascript:alert(1)", "links[0].url")]
    [InlineData("course-shell", "Course shell", "A definition.", "https://user:secret@example.edu/", "links[0].url")]
    [InlineData("course-shell", "Course shell", "A definition.", "file:///c:/secret.txt", "links[0].url")]
    [InlineData("course-shell", "Course shell", "A definition.", "/relative/path", "links[0].url")]
    public async Task Invalid_terms_and_unsafe_links_are_rejected(string termId, string term, string definition, string? url, string field)
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var links = url is null ? Array.Empty<object>() : [new { label = "Reading", source = "", url }];
        var response = await Save(ctl, termId, new { term, definition, links });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"Expected an error for {field}: {problem}");
    }

    [Fact] public async Task Phrases_too_short_to_match_safely_and_too_many_links_are_rejected()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var response = await Save(ctl, "course-shell", new
        {
            term = "Course shell", definition = "A definition.", phrases = new[] { "cs" },
            links = Enumerable.Range(0, 6).Select(i => new { label = $"Link {i}", source = "", url = $"https://example.edu/{i}" }).ToArray()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("phrases", out _));
        Assert.True(errors.TryGetProperty("links", out _));
    }

    [Fact] public async Task Another_colleges_glossary_is_never_visible_or_changed()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        var otherTenant = Guid.NewGuid();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var author = await db.Users.Select(u => u.Id).FirstAsync();
            db.GlossaryEntries.Add(new GlossaryEntry
            {
                Id = Guid.NewGuid(), TenantId = otherTenant, TermId = "course-shell", Term = "Other college's shell", Definition = "Not ours.", UpdatedBy = author
            });
            await db.SaveChangesAsync();
        }
        Assert.Empty(await List(ctl));
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.DeleteAsync("/api/v1/glossary/course-shell")).StatusCode);
        // Saving the same term id creates this college's own entry and leaves the other college's alone.
        await Ok(await Save(ctl, "course-shell", new { term = "Course shell", definition = "Ours." }));
        using var check = f.Services.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<AppDbContext>().GlossaryEntries.AsNoTracking().OrderBy(e => e.Definition).ToListAsync();
        Assert.Equal([("Not ours.", otherTenant), ("Ours.", DemoSeed.Tenant)], rows.Select(r => (r.Definition, r.TenantId)));
    }
}
