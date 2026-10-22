using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;
using Xunit;

public class CourseReviewUnitTests
{
    // These tests cover the checks that predate ADR 011 slice 3, so they run under exactly those checks.
    private static readonly CheckProfile Initial = StandardSetContent.ToProfile(StandardSetContent.InitialDefaults());
    private static CourseItemEvidence Page(string title) => new(Guid.NewGuid(), "page", title, "Published", Guid.NewGuid(), null, null, false);
    private static CourseItemEvidence Assignment(string title, bool? due, bool aligned) => new(Guid.NewGuid(), "assignment", title, "Published", Guid.NewGuid(), due, true, aligned);
    private static ModuleEvidence Module(string title, params CourseItemEvidence[] items) => new(Guid.NewGuid(), title, "Published", 0, items);
    private static CourseStructureEvidence Structure(IReadOnlyList<ModuleEvidence> modules, IReadOnlyList<CourseItemEvidence>? unplaced = null, int competencies = 1) =>
        new(CourseStructureEvidence.CurrentVersion, CourseStructureEvidence.KindName, modules, unplaced ?? [], competencies);

    [Fact] public void Course_checks_find_structure_alignment_and_pacing_issues_and_point_at_their_subject()
    {
        var late = Assignment("Chart project", due: false, aligned: false);
        var empty = Module("Data storytelling");
        var loose = Page("Old syllabus copy");
        var structure = Structure([Module("Start here", Page("Welcome")), Module("Week 2: Datasets", late), Module("Week 3: Charts", Page("Reading charts")), empty], [loose]);
        var report = CourseChecks.Run(structure, Initial);
        Assert.Equal(new[] { CourseChecks.ModuleEmpty, CourseChecks.ModuleNaming, CourseChecks.ContentUnplaced, CourseChecks.DueDateMissing, CourseChecks.Unaligned },
            report.Findings.Select(f => f.CheckCode));
        Assert.Equal(empty.ElementId, report.Findings.Single(f => f.CheckCode == CourseChecks.ModuleEmpty).SubjectElementId);
        Assert.Equal(empty.ElementId, report.Findings.Single(f => f.CheckCode == CourseChecks.ModuleNaming).SubjectElementId);
        Assert.Equal(loose.ElementId, report.Findings.Single(f => f.CheckCode == CourseChecks.ContentUnplaced).SubjectElementId);
        Assert.Equal(late.ElementId, report.Findings.Single(f => f.CheckCode == CourseChecks.DueDateMissing).SubjectElementId);
        Assert.All(report.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.Evidence)));
        // Pure: the same snapshot always yields the same proposals.
        Assert.Equal(report.Findings.Select(f => f.Fingerprint), CourseChecks.Run(structure, Initial).Findings.Select(f => f.Fingerprint));
    }

    [Fact] public void Course_without_modules_or_competencies_reports_course_level_gaps_instead_of_per_assignment_alignment()
    {
        var report = CourseChecks.Run(Structure([], [Assignment("Essay", due: null, aligned: false)], competencies: 0), Initial);
        Assert.Equal(new[] { CourseChecks.StartMissing, CourseChecks.ContentUnplaced, CourseChecks.CompetenciesMissing }, report.Findings.Select(f => f.CheckCode));
        Assert.Contains(report.Limitations, l => l.Contains("Due-date metadata was not captured for 1"));
        Assert.Contains(CourseChecks.Run(null, Initial).Limitations, l => l.Contains("no course-structure evidence"));
    }

    [Fact] public void Naming_pattern_compares_modules_after_the_orientation_module()
    {
        Assert.Equal(("week", "unit", "none"), (CourseChecks.NamingPattern("Week 3: Charts"), CourseChecks.NamingPattern("unit 2 - Intro"), CourseChecks.NamingPattern("Data storytelling")));
        var consistent = Structure([Module("Getting started", Page("Read me")), Module("Week 1: A", Page("x")), Module("Week 2: B", Page("y"))]);
        Assert.Empty(CourseChecks.Run(consistent, Initial).Findings);
    }

    [Fact] public void Page_and_course_evidence_parsers_reject_each_others_json()
    {
        var course = Structure([Module("Start here", Page("Welcome"))]).Serialize();
        var page = new ContentEvidence(ContentEvidence.CurrentVersion, [], [], [], []).Serialize();
        Assert.Null(ContentEvidence.Parse(course));
        Assert.Null(CourseStructureEvidence.Parse(page));
        Assert.NotNull(CourseStructureEvidence.Parse(course));
        Assert.NotNull(ContentEvidence.Parse(page));
    }

    [Fact] public void Preview_re_serializes_only_allowlisted_structure_and_never_emits_urls_scripts_or_styles()
    {
        var html = CanvasText.Preview(
            "<h2 style='color:red' onclick='steal()'>Welcome</h2><script>alert(1)</script><iframe src='https://evil.example'></iframe>" +
            "<p class='x'>Hi <b>there</b> &lt;script&gt; <span style='font-size:40px'>big</span></p>" +
            "<img src='https://canvas.example.test/files/1?verifier=secret'><img src='a.png' alt='chart.png'><img src='b.png' alt='A bar chart of results'>" +
            "<h3></h3><a href='https://other.example.org/path?verifier=secret'>Reading list</a><a href='javascript:alert(1)'></a>" +
            "<table><tr><th scope='col' colspan='2' onmouseover='x()'>Head</th></tr><tr><td rowspan='abc'>A</td><td>B</td></tr></table>",
            new Uri("https://canvas.example.test/"))!;
        foreach (var forbidden in new[] { "script>", "alert", "onclick", "onmouseover", "style=", "iframe", "src=", "href", "verifier", "secret", "class=\"x\"", "steal" })
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
        // Colour survives only as a fixed palette class; the style attribute itself never does.
        Assert.Contains("<h2 class=\"pv-fg-red\">Welcome</h2>", html);
        Assert.Contains("<p>Hi <strong>there</strong> &lt;script&gt; <span class=\"pv-large\">big</span></p>", html); // a 40px span keeps "large text", nothing else
        Assert.Contains("Image — no alt text", html);
        Assert.Contains("alt text is a file name: chart.png", html);
        Assert.Contains("Image: A bar chart of results", html);
        Assert.Contains("<h3 class=\"pv-flag\">[empty heading]</h3>", html);
        Assert.Contains("title=\"Link (external: other.example.org)\">Reading list</span>", html);
        Assert.Contains("[link with no text]", html);
        Assert.Contains("<th colspan=\"2\" scope=\"col\">Head</th>", html);
        Assert.Contains("<td>A</td>", html);
        Assert.Null(CanvasText.Preview(""));
    }

    [Fact] public void Preview_keeps_code_examples_callout_boxes_and_heading_rules()
    {
        var html = CanvasText.Preview(
            "<pre style='background: #f4f4f4; padding: 14px; border: 1px solid #ddd; overflow-x: auto;'><code>&lt;p&gt;Hello&lt;/p&gt;\n  let x = 1;</code></pre>" +
            "<div style='background: #eef6ff; border: 1px solid #cfe3ff; padding: 12px 16px;'>Tip</div>" +
            "<div style='background: #f7f7f7; border-left: 6px solid #b22222; padding: 22px 24px;'>Warning</div>" +
            "<div style='background: #f7f7f7; border-left: 4px solid #999; padding: 12px 16px;'>Note</div>" +
            "<h2 style='border-bottom: 2px solid #b22222;'>Section</h2>" +
            "<p>Run <span style='font-family: Consolas, monospace'>npm install</span>, press <kbd>Ctrl</kbd>, see <span style='font-size: 24px'>big</span> and <span style='font-size: 14pt'>normal</span>." +
            " <code style='background: none 0% 0% / auto repeat scroll padding-box border-box rgba(0, 0, 0, 0)'>transparent</code></p>")!;
        Assert.Contains("<pre class=\"pv-bg-gray\"><code>&lt;p&gt;Hello&lt;/p&gt;\n  let x = 1;</code></pre>", html); // whitespace kept exactly
        Assert.Contains("<div class=\"pv-bg-blue pv-boxed pv-box\">Tip</div>", html);
        Assert.Contains("<div class=\"pv-bg-gray pv-accent-red pv-box\">Warning</div>", html);
        Assert.Contains("<div class=\"pv-bg-gray pv-accent-gray pv-box\">Note</div>", html);
        Assert.Contains("<h2 class=\"pv-rule pv-rule-red\">Section</h2>", html);
        Assert.Contains("<span class=\"pv-mono\">npm install</span>", html);
        Assert.Contains("<code>Ctrl</code>", html);
        Assert.Contains("<span class=\"pv-large\">big</span> and normal.", html);
        Assert.Contains("<code>transparent</code>", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact] public void Preview_keeps_canvas_formatting_as_palette_classes_and_drops_everything_else()
    {
        var html = CanvasText.Preview(
            "<p style='text-align: center; color: #c0392b'>Centered red</p>" +
            "<p>Note: <span style='background-color: rgb(241, 196, 15)'>highlighted</span> <span>plain span</span> <span style='font-size: 30px'>big</span></p>" +
            "<p style='color: black; background-color: #ffffff'>Default colours</p>" +
            "<p><span style='color: navy; font-weight: bold; font-style: italic; text-decoration: underline line-through'>styled</span> <font color='green'>old font</font></p>" +
            "<center>Legacy centre</center><p><mark>marked</mark></p><p align='right'>Right</p>" +
            "<p style='color: red; background: url(javascript:alert(1)); behavior: url(x.htc)' class='evil' id='x'>Hostile</p>")!;
        Assert.Contains("<p class=\"pv-align-center pv-fg-red\">Centered red</p>", html);
        Assert.Contains("<span class=\"pv-bg-yellow\">highlighted</span> plain span <span class=\"pv-large\">big</span>", html);
        Assert.Contains("<p>Default colours</p>", html);
        Assert.Contains("<span class=\"pv-bold pv-italic pv-underline pv-strike pv-fg-blue\">styled</span>", html);
        Assert.Contains("<span class=\"pv-fg-green\">old font</span>", html);
        Assert.Contains("<div class=\"pv-align-center\">Legacy centre</div>", html);
        Assert.Contains("<mark class=\"pv-bg-yellow\">marked</mark>", html);
        Assert.Contains("<p class=\"pv-align-right\">Right</p>", html);
        Assert.Contains("<p class=\"pv-fg-red\">Hostile</p>", html);
        foreach (var forbidden in new[] { "style=", "url(", "javascript", "behavior", "evil", "id=" })
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
        // Every class emitted belongs to TeachWell's own vocabulary.
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html, "class=\"([^\"]*)\""))
            Assert.All(m.Groups[1].Value.Split(' '), c => Assert.StartsWith("pv-", c));
    }
}
