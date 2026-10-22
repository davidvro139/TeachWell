using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class OutcomeListParserTests
{
    private const string Outcomes = """
        Implement common HTML tags in a functional coding format to create a Web site using current standards and technologies.
        Demonstrate the ability to upload and publish a web page on a web server.
        Investigate web scripting and the HTML DOM / Manipulate the DOM using web scripts.
        Use web hosting service to deploy a website.
        Explore best practices in modern responsive website design.
        """;

    [Theory]
    [InlineData("Student Learning Outcomes\n")]
    [InlineData("")]
    public void Simple_syllabus_paste_with_or_without_its_heading_gives_five_outcomes(string heading)
    {
        var result = OutcomeListParser.Parse(heading + Outcomes);
        Assert.Equal(5, result.Outcomes.Count);
        Assert.All(result.Outcomes, o => Assert.Null(o.Code));
        Assert.Equal("Use web hosting service to deploy a website.", result.Outcomes[3].Title);
        Assert.Equal(heading.Length > 0 ? ["Student Learning Outcomes"] : [], result.Ignored);
    }

    [Fact] public void Bullets_numbering_codes_lead_ins_wrapped_lines_and_repeats_are_handled()
    {
        var result = OutcomeListParser.Parse("""
            Course Learning Outcomes:
            Upon successful completion of this course, students will be able to:
            • Explain the box model
            2) Build a responsive layout
            SLO 3: Evaluate an accessible page
            CLO-4 - Debug JavaScript errors
            using the browser's developer tools.
            - Explain the box model
            """);
        Assert.Equal([(null, "Explain the box model"), (null, "Build a responsive layout"), ("SLO-3", "Evaluate an accessible page"),
            ("CLO-4", "Debug JavaScript errors using the browser's developer tools.")], result.Outcomes.Select(o => (o.Code, o.Title)));
        Assert.Contains("Course Learning Outcomes:", result.Ignored);
        Assert.Contains(result.Ignored, i => i.StartsWith("Upon successful completion"));
        Assert.Contains(result.Ignored, i => i.EndsWith("(repeated)"));
        Assert.Equal("LO1-3", OutcomeListParser.NormalizeCode("lo1.3"));
    }
}

public class OutcomeImportTests
{
    private const string Course = "30000000-0000-0000-0000-000000000001";

    [Fact] public async Task Owner_previews_pasted_outcomes_then_saves_the_approved_ones_all_at_once()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies",
            new { code = "SLO-1", title = "Use web hosting service to deploy a website.", description = "" })).StatusCode);

        var preview = await owner.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies/parse", new
        {
            text = "Student Learning Outcomes\nImplement common HTML tags to create a Web site.\nUse web hosting service to deploy a website.\nUnderstand responsive design.\nSLO 1: Explore best practices in responsive design."
        });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var proposal = (await preview.Content.ReadFromJsonAsync<OutcomeProposalDto>())!;
        Assert.Equal(["Student Learning Outcomes"], proposal.Ignored);
        // Numbering continues after the course's SLO-1; a pasted code that is taken gets a new one.
        Assert.Equal(["SLO-2", "SLO-3", "SLO-4", "SLO-5"], proposal.Outcomes.Select(o => o.Code));
        Assert.Equal(("Apply", true), (proposal.Outcomes[0].BloomLevel!, proposal.Outcomes[0].Include));
        Assert.Equal((false, "SLO-1"), (proposal.Outcomes[1].Include, proposal.Outcomes[1].ExistingCode!));
        Assert.Equal("understand", proposal.Outcomes[2].VaguePhrase);
        Assert.Contains(proposal.Outcomes[3].Notes, n => n.Contains("SLO-1 is already used"));

        using (var scope = f.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().CourseCompetencies.CountAsync(c => c.CourseId == Guid.Parse(Course))); // a preview saves nothing

        // All or nothing: one bad row (a duplicate code) saves none of them.
        var rejected = await owner.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies/bulk", new
        {
            items = new[] { new { code = "SLO-2", title = "Implement common HTML tags to create a Web site.", description = "" }, new { code = "SLO-1", title = "Clash", description = "" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("SLO-1 is already used in this course", await rejected.Content.ReadAsStringAsync());

        var chosen = proposal.Outcomes.Where(o => o.Include).Select(o => new { code = o.Code, title = o.Title, description = "" }).ToArray();
        var saved = await owner.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies/bulk", new { items = chosen });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var list = (await owner.GetFromJsonAsync<CompetencyDto[]>($"/api/v1/courses/{Course}/competencies"))!;
        Assert.Equal(["SLO-1", "SLO-2", "SLO-4", "SLO-5"], list.Select(c => c.Code));

        // Only the owner authors competencies: the reviewer account also holds the Instructor role, so it passes the role gate and
        // is refused by the ownership check (404, as for every resource the caller cannot manage). Empty and oversized input is rejected.
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies/parse", new { text = "Explain things." })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies/bulk", new { items = new[] { new { code = "X-1", title = "T", description = "" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies/parse", new { text = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/courses/{Course}/competencies/bulk",
            new { items = Enumerable.Range(1, 101).Select(i => new { code = $"X-{i}", title = "T", description = "" }) })).StatusCode);
        using var scope2 = f.Services.CreateScope();
        Assert.Equal(4, await scope2.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.CountAsync(a => a.Action == "CompetencyCreated"));
    }

    [Fact] public async Task Mapping_grid_saves_many_changes_at_once_or_none_of_them()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        async Task<Guid> Create(string course, string code) =>
            (await (await owner.PostAsJsonAsync($"/api/v1/courses/{course}/competencies", new { code, title = code + " title", description = "" })).Content.ReadFromJsonAsync<CompetencyDto>())!.Id;
        var a = await Create(Course, "COMP-A"); var b = await Create(Course, "COMP-B");
        var elsewhere = await Create("30000000-0000-0000-0000-000000000002", "COMP-X");
        Guid assignment, page;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            assignment = await db.Elements.Where(e => e.CourseId == Guid.Parse(Course) && e.Type == "assignment").Select(e => e.Id).FirstAsync();
            page = await db.Elements.Where(e => e.CourseId == Guid.Parse(Course) && e.Type == "page").Select(e => e.Id).FirstAsync();
        }
        Task<HttpResponseMessage> Put(HttpClient client, params object[] changes) => client.PutAsJsonAsync($"/api/v1/courses/{Course}/competencies/mappings", new { changes });
        async Task<string[]> Mapped() => (await owner.GetFromJsonAsync<CompetencyDto[]>($"/api/v1/courses/{Course}/competencies"))!
            .SelectMany(c => c.Mappings.Select(m => $"{c.Code}:{m.ElementId == assignment}")).Order().ToArray();

        var saved = await Put(owner, new { competencyId = a, elementId = assignment, mapped = true }, new { competencyId = b, elementId = assignment, mapped = true });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(["COMP-A:True", "COMP-B:True"], await Mapped());

        // All or nothing: a page is not assessment evidence, and a competency from another course is refused; the valid unmap beside them is not applied.
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(owner, new { competencyId = a, elementId = assignment, mapped = false }, new { competencyId = b, elementId = page, mapped = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(owner, new { competencyId = a, elementId = assignment, mapped = false }, new { competencyId = elsewhere, elementId = assignment, mapped = true })).StatusCode);
        Assert.Equal(["COMP-A:True", "COMP-B:True"], await Mapped());

        // A change that matches the current state is a no-op; only the real change is applied and audited.
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, new { competencyId = a, elementId = assignment, mapped = false }, new { competencyId = b, elementId = assignment, mapped = true })).StatusCode);
        Assert.Equal(["COMP-B:True"], await Mapped());

        // Only the owner maps (404 for everyone else); an empty request is invalid.
        Assert.Equal(HttpStatusCode.NotFound, (await Put(ctl, new { competencyId = b, elementId = assignment, mapped = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(owner)).StatusCode);
        Assert.Equal(["COMP-B:True"], await Mapped());
        using var scope2 = f.Services.CreateScope();
        var audit = scope2.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs;
        Assert.Equal((2, 1), (await audit.CountAsync(x => x.Action == "CompetencyMapped"), await audit.CountAsync(x => x.Action == "CompetencyUnmapped")));
    }
}
