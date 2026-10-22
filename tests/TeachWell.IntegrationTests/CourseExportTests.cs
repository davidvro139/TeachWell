using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using TeachWell.Infrastructure.Canvas;
using Xunit;

public class CourseExportReaderTests
{
    private static CanvasCourseData Read(byte[] export, string? url = null) => CanvasExportReader.ResolveLinks(CanvasExportReader.Read(new MemoryStream(export)), url is null ? null : new Uri(url));
    private static string Rejected(byte[] export) => Assert.Throws<CourseExportException>(() => CanvasExportReader.Read(new MemoryStream(export))).Message;

    [Fact] public void Reads_course_structure_content_and_metadata_from_a_fictional_export()
    {
        var data = Read(new ExportFixture().Build(), "https://canvas.example.test/courses/4242");
        Assert.Equal(("g0fictional0course0export0001", "Teaching Statistics with Real Data", "STAT 110"), (data.Course.Id, data.Course.Title, data.Course.Code));
        var welcome = data.Elements.Single(e => e.Id == "page-welcome");
        Assert.Equal(("page", "Welcome", "Published", "https://canvas.example.test/courses/4242/pages/welcome"), (welcome.Type, welcome.Title, welcome.Status, welcome.SourceUrl));
        Assert.Contains("alt text is a file name: banner.png", welcome.PreviewHtml);
        Assert.Equal("IMG_ALT_FILENAME", DeterministicChecks.Run("page", welcome.Status, welcome.Evidence).Findings.Single().CheckCode);
        Assert.Equal("Unpublished", data.Elements.Single(e => e.Id == "page-syllabus").Status);
        var project = data.Elements.Single(e => e.Id == "asg-project");
        Assert.Equal(new AssignmentEvidence(true, 25m, false), project.Evidence!.Assignment);
        Assert.Equal("https://canvas.example.test/courses/4242/assignments", project.SourceUrl);
        var modules = data.Elements.Where(e => e.Type == "module").ToList();
        Assert.Equal([("Start here", 1, "Published"), ("Week 1: Describing data", 2, "Unpublished")], modules.Select(m => (m.Title, m.Position, m.Status)));
        var items = data.Elements.Where(e => e.Type == "moduleItem").ToDictionary(e => e.Id);
        Assert.Equal(("mod-start", "page", "page-welcome"), (items["item-welcome"].ParentModuleId, items["item-welcome"].TargetType, items["item-welcome"].TargetId));
        Assert.Equal(("asg-project", "Unavailable"), (items["item-project"].TargetId ?? "", items["item-quiz"].Status));
        Assert.NotNull(items["item-quiz"].Warning);
        Assert.Contains(data.Warnings, w => w.Contains("1 quiz, 1 discussion, 1 course file (1 KB of documents and media)"));
        Assert.True(CourseDataApplier.IsConsistent(data));
        // Without a Canvas course address nothing links out, and the import says so.
        var offline = Read(new ExportFixture().Build());
        Assert.All(offline.Elements, e => Assert.Null(e.SourceUrl));
        Assert.Contains(offline.Warnings, w => w.Contains("No Canvas course address"));
    }

    [Fact] public void Rejects_malformed_and_hostile_archives_with_clear_messages()
    {
        Assert.Contains("not a valid course export", Rejected(Encoding.UTF8.GetBytes("plain text, not a zip")));
        Assert.Contains("no imsmanifest.xml", Rejected(ExportFixture.Zip([("readme.txt", "x"u8.ToArray())])));
        Assert.Contains("unsafe or invalid path", Rejected(ExportFixture.Zip([("imsmanifest.xml", "<manifest/>"u8.ToArray()), ("../escape.html", "x"u8.ToArray())])));
        Assert.Contains("unsafe or invalid path", Rejected(ExportFixture.Zip([("C:/windows/x.html", "x"u8.ToArray())])));
        // XXE and entity expansion: any document type declaration is refused, so no entity is ever resolved.
        var xxe = """<?xml version="1.0"?><!DOCTYPE m [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><manifest identifier="&x;"/>""";
        Assert.Contains("document type declaration", Rejected(ExportFixture.Zip([("imsmanifest.xml", Encoding.UTF8.GetBytes(xxe))])));
        // A compression bomb in anything TeachWell reads (XML or HTML) is refused before it is opened.
        var bomb = ExportFixture.Zip([("imsmanifest.xml", Encoding.UTF8.GetBytes(ExportFixture.Manifest("c1", ""))), ("wiki_content/padding.html", new byte[20_000_000])]);
        Assert.Contains("implausible compression ratio", Rejected(bomb));
        Assert.Contains("more than 20,000 files", Rejected(ExportFixture.Zip(Enumerable.Range(0, 20_001).Select(i => ($"web_resources/{i}.txt", Array.Empty<byte>())))));
        Assert.Contains("does not identify its course", Rejected(ExportFixture.Zip([("imsmanifest.xml", "<manifest/>"u8.ToArray())])));
    }

    [Fact] public void Classic_quizzes_and_surveys_are_imported_with_their_questions_and_text_headers_are_marked()
    {
        var data = Read(new ExportFixture { IncludeImportableQuiz = true }.Build());
        var quiz = data.Elements.Single(e => e.Type == ReviewVocabulary.QuizType);
        Assert.Equal(("quiz-2", "Week 1 check-in", "Unpublished"), (quiz.Id, quiz.Title, quiz.Status));
        Assert.StartsWith("Ungraded survey · 2 questions · 1 essay, 1 multiple choice · 0 points · 1 attempt", quiz.PlainText);
        Assert.Contains("Tell us how the first week went.", quiz.PlainText);
        Assert.Contains("Explain how the pace of the first week felt.", quiz.PlainText);
        // Questions run through the same evidence capture as pages: the chart image has no alt text.
        Assert.Contains(quiz.Evidence!.Images, i => i.Alt is null);
        Assert.Equal((0m, true, new DateTime(2026, 11, 2, 6, 59, 0, DateTimeKind.Utc)), (quiz.Evidence.Assignment!.PointsPossible!.Value, quiz.Evidence.Assignment.HasDueDate!.Value, quiz.Evidence.Assignment.DueAt!.Value));
        var items = data.Elements.Where(e => e.Type == "moduleItem").ToDictionary(e => e.Id);
        Assert.Equal((ReviewVocabulary.QuizType, "quiz-2", null), (items["item-quiz2"].TargetType, items["item-quiz2"].TargetId, items["item-quiz2"].Warning));
        Assert.Equal((ReviewVocabulary.TextHeaderStatus, (string?)null), (items["item-header"].Status, items["item-header"].Warning));
        // The quiz whose settings are missing from the export says exactly that, and only it is counted as skipped.
        Assert.Equal("This module item is a quiz whose settings are not in the export, so it was not imported.", items["item-quiz"].Warning);
        Assert.Contains(data.Warnings, w => w.Contains("1 quiz, 1 discussion"));
        Assert.True(CourseDataApplier.IsConsistent(data));
    }

    [Fact] public void Discussions_announcements_external_tools_and_new_quizzes_are_handled_distinctly()
    {
        var data = Read(new ExportFixture { IncludeActivities = true }.Build());
        var discussion = data.Elements.Single(e => e.Id == "disc-2");
        Assert.Equal((ReviewVocabulary.DiscussionType, "Introduce yourself", "Published"), (discussion.Type, discussion.Title, discussion.Status));
        Assert.StartsWith("Graded discussion · 5 points", discussion.PlainText);
        Assert.Contains("Say hello and share one goal.", discussion.PlainText);
        Assert.Equal((5m, new DateTime(2026, 11, 3, 6, 59, 0, DateTimeKind.Utc)), (discussion.Evidence!.Assignment!.PointsPossible!.Value, discussion.Evidence.Assignment.DueAt!.Value));
        // Announcements are messages, not course content: counted, not imported.
        Assert.DoesNotContain(data.Elements, e => e.Id == "ann-1");
        Assert.Contains(data.Warnings, w => w.Contains("1 quiz, 1 discussion, 1 announcement, 1 course file"));
        Assert.StartsWith("Submitted in an external tool (app.codegra.de)", data.Elements.Single(e => e.Id == "asg-tool").PlainText);
        var newQuiz = data.Elements.Single(e => e.Id == "asg-nq");
        Assert.Equal(ReviewVocabulary.QuizType, newQuiz.Type);
        Assert.StartsWith("New Quiz · 20 points", newQuiz.PlainText);
        Assert.Contains("New Quizzes tool", newQuiz.PlainText);
        var items = data.Elements.Where(e => e.Type == "moduleItem").ToDictionary(e => e.Id);
        Assert.Equal((ReviewVocabulary.DiscussionType, "disc-2"), (items["item-disc2"].TargetType!, items["item-disc2"].TargetId!));
        Assert.Equal((ReviewVocabulary.QuizType, "asg-nq"), (items["item-nq"].TargetType!, items["item-nq"].TargetId!));
        Assert.True(CourseDataApplier.IsConsistent(data));
    }

    [Fact] public void Large_course_media_is_listed_but_never_opened_so_it_does_not_count_against_the_limits()
    {
        // 260 MB of highly compressible "video": previously rejected as too large to expand and as a compression bomb.
        var path = Path.Combine(Path.GetTempPath(), $"teachwell-test-{Guid.NewGuid():N}.imscc");
        try
        {
            ExportFixture.WriteWithLargeEntry(path, new ExportFixture(), "web_resources/lecture.mp4", 260L * 1024 * 1024, CompressionLevel.SmallestSize);
            using (var file = File.OpenRead(path))
            {
                var data = CanvasExportReader.Read(file);
                Assert.Contains(data.Elements, e => e.Type == "page");
                Assert.Contains(data.Warnings, w => w.Contains("2 course files (260 MB of documents and media)"));
            }
            // The same bytes as a page are still refused: pages are read, so they stay within the expanded-size limit.
            ExportFixture.WriteWithLargeEntry(path, new ExportFixture(), "wiki_content/huge.html", 260L * 1024 * 1024, CompressionLevel.SmallestSize);
            using (var page = File.OpenRead(path))
                Assert.Contains("expand to more than 250 MB", Assert.Throws<CourseExportException>(() => CanvasExportReader.Read(page)).Message);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void Upload_limit_setting_defaults_to_500_megabytes_and_rejects_invalid_values()
    {
        IConfiguration Config(string? mb) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CourseExports:MaxUploadMegabytes"] = mb }).Build();
        Assert.Equal(500L * 1024 * 1024, new CourseExportOptions(Config(null)).MaxUploadBytes);
        Assert.Equal(1024, new CourseExportOptions(Config("1024")).MaxUploadMegabytes);
        foreach (var bad in new[] { "0", "2049", "lots" }) Assert.Throws<InvalidOperationException>(() => new CourseExportOptions(Config(bad)));
    }

    [Fact] public void Oversized_page_is_imported_as_unavailable_instead_of_failing_the_course()
    {
        var fixture = new ExportFixture { WelcomeBody = "<p>" + new string('a', 70_000) + "</p>" };
        var welcome = Read(fixture.Build()).Elements.Single(e => e.Id == "page-welcome");
        Assert.Equal(("Unavailable", ""), (welcome.Status, welcome.PlainText));
        Assert.Contains("60,000-character limit", welcome.Warning);
    }

    [Theory]
    [InlineData("https://canvas.example.test/courses/4242", "https://canvas.example.test/courses/4242")]
    [InlineData("https://canvas.example.test/courses/4242/modules", "https://canvas.example.test/courses/4242")]
    [InlineData("http://canvas.example.test/courses/4242", null)]
    [InlineData("https://canvas.example.test/courses/4242?x=1", null)]
    [InlineData("https://user:pw@canvas.example.test/courses/4242", null)]
    [InlineData("https://canvas.example.test/accounts/1", null)]
    [InlineData("https://canvas.example.test/courses/abc", null)]
    public void Canvas_course_address_must_be_an_https_course_url(string input, string? expected)
    {
        var ok = CourseExportService.TryCourseUrl(input, out var url);
        Assert.Equal(expected, ok ? url!.AbsoluteUri.TrimEnd('/') : null);
    }
}

public class CourseExportTests
{
    private static Task<HttpResponseMessage> Upload(HttpClient c, byte[] export, string? course = null, string? canvasUrl = null, string fileName = "course.imscc")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(export); file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", fileName);
        if (canvasUrl is not null) form.Add(new StringContent(canvasUrl), "canvasCourseUrl");
        return c.PostAsync(course is null ? "/api/v1/course-exports" : $"/api/v1/courses/{course}/course-exports", form);
    }
    private static async Task<CourseExportImportDto> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseExportImportDto>())!;
    }

    [Fact] public async Task Instructor_imports_an_export_reviews_it_and_a_newer_export_refreshes_without_rewriting_reviewed_content()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var imported = await Ok(await Upload(owner, new ExportFixture().Build(), canvasUrl: "https://canvas.example.test/courses/4242"));
        Assert.True(imported.Created);
        Assert.Equal(("Teaching Statistics with Real Data", 8), (imported.Title, imported.ElementCount));
        var course = imported.CourseId.ToString();
        var listed = (await owner.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=Instructor"))!.Single(c => c.Id == imported.CourseId);
        Assert.Equal("CanvasExport", listed.SourceType);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/courses/{course}");
        Assert.True(detail.GetProperty("permissions").GetProperty("canRefreshExport").GetBoolean());
        Assert.Equal("https://canvas.example.test/courses/4242", detail.GetProperty("sourceWebUrl").GetString());

        // Shared with CTL, the imported page is reviewable with a preview and a Canvas link like any other content.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/v1/courses/{course}/shares", new { reviewerId = "20000000-0000-0000-0000-000000000002" })).StatusCode);
        var page = detail.GetProperty("elements").EnumerateArray().First(e => e.GetProperty("title").GetString() == "Welcome" && e.GetProperty("type").GetString() == "page").GetProperty("id").GetGuid();
        var review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{page}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        Assert.Equal("https://canvas.example.test/courses/4242/pages/welcome", review.Snapshot.SourceUrl);
        Assert.Contains("banner.png", review.Snapshot.PreviewHtml);

        // A newer export of the same course refreshes it: same course, the changed page gains a snapshot, the removed page is marked Removed.
        var newer = new ExportFixture { WelcomeBody = "<h2>Welcome</h2><p>Updated introduction.</p>", IncludeSyllabusPage = false }.Build();
        var refreshed = await Ok(await Upload(owner, newer));
        Assert.Equal((imported.CourseId, false), (refreshed.CourseId, refreshed.Created));
        Assert.Contains(refreshed.Warnings, w => w.Contains("marked Removed"));
        var pinned = await ReviewTests.Ok(await ctl.GetAsync($"/api/v1/reviews/{review.Id}"));
        Assert.False(pinned.IsLatestSnapshot);
        Assert.Equal(review.Snapshot.ContentHash, pinned.Snapshot.ContentHash);
        Assert.Equal("https://canvas.example.test/courses/4242/pages/welcome", pinned.Snapshot.SourceUrl); // address kept from the first upload
        // The shared CTL reviewer can refresh it too.
        Assert.Equal(HttpStatusCode.OK, (await Upload(ctl, newer, course)).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Courses.CountAsync(c => c.SourceType == "CanvasExport"));
        Assert.Equal("Removed", (await db.Elements.SingleAsync(e => e.CourseId == imported.CourseId && e.SourceElementId == "page-syllabus")).SourceStatus);
        var actions = await db.AuditLogs.Where(a => a.EntityId == imported.CourseId).Select(a => a.Action).ToListAsync();
        Assert.Contains("CourseExportImported", actions); Assert.Contains("CourseExportRefreshed", actions);
    }

    // Larger than the 128 MB ASP.NET form limit the endpoint used to rely on; streamed from disk on both sides.
    [Fact] public async Task A_140_megabyte_media_heavy_export_uploads_and_imports()
    {
        var path = Path.Combine(Path.GetTempPath(), $"teachwell-test-{Guid.NewGuid():N}.imscc");
        try
        {
            ExportFixture.WriteWithLargeEntry(path, new ExportFixture(), "web_resources/lecture.mp4", 140L * 1024 * 1024, CompressionLevel.NoCompression);
            Assert.True(new FileInfo(path).Length > 140L * 1024 * 1024);
            using var f = new Factory(); using var owner = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(owner, "instructor");
            await using var stream = File.OpenRead(path);
            var form = new MultipartFormDataContent();
            var file = new StreamContent(stream); file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(file, "file", "big-course.imscc");
            var imported = await Ok(await owner.PostAsync("/api/v1/course-exports", form));
            Assert.Equal(("Teaching Statistics with Real Data", 8), (imported.Title, imported.ElementCount));
            Assert.Contains(imported.Warnings, w => w.Contains("2 course files (140 MB of documents and media)"));
        }
        finally { File.Delete(path); }
    }

    [Fact] public async Task Imported_quizzes_can_be_reviewed_with_assignment_criteria_and_mapped_to_competencies()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var imported = await Ok(await Upload(owner, new ExportFixture { IncludeImportableQuiz = true }.Build()));
        var course = imported.CourseId.ToString();
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/courses/{course}");
        var quizId = detail.GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("type").GetString() == "quiz").GetProperty("id").GetGuid();

        // Quizzes count as assessment evidence for competencies.
        var competency = await owner.PostAsJsonAsync($"/api/v1/courses/{course}/competencies", new { code = "C1", title = "Reflect on learning pace", description = "" });
        var competencyId = (await competency.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/courses/{course}/competencies/{competencyId}/mappings", new { elementId = quizId })).StatusCode);

        // A CTL reviewer with access reviews the quiz against the rubric's assignment criteria; checks run on its questions.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/v1/courses/{course}/shares", new { reviewerId = Guid.Parse("20000000-0000-0000-0000-000000000002") })).StatusCode);
        var review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{quizId}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        Assert.Equal("quiz", review.ElementType);
        Assert.Contains(review.Evaluations, e => e.CriterionId == ReviewTests.Criterion(review, "ASM-1"));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        Assert.Contains(review.Findings, x => x.CheckCode == DeterministicChecks.ImageAltMissing);
        Assert.Contains(review.Findings, x => x.CheckCode == DeterministicChecks.ItemUnpublished);
    }

    [Fact] public async Task Formatted_previews_are_served_in_batches_only_to_people_with_course_access()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f); using var anonymous = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        const string shared = "30000000-0000-0000-0000-000000000001", privateCourse = "30000000-0000-0000-0000-000000000002";
        var page = await ReviewTests.ElementId(owner, shared, "page");
        var otherCoursePage = await ReviewTests.ElementId(owner, privateCourse, "page");
        var previews = (await owner.GetFromJsonAsync<Dictionary<Guid, string?>>($"/api/v1/courses/{shared}/previews?ids={page},{otherCoursePage}"))!;
        Assert.Equal([page], previews.Keys); // ids from another course are not returned through this one
        Assert.Contains("<h2>Welcome</h2>", previews[page]);
        // A reviewer sees previews of a shared course, not of an unshared one; anonymous callers see nothing.
        Assert.Equal(HttpStatusCode.OK, (await ctl.GetAsync($"/api/v1/courses/{shared}/previews?ids={page}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctl.GetAsync($"/api/v1/courses/{privateCourse}/previews?ids={otherCoursePage}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/courses/{shared}/previews?ids={page}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/v1/courses/{shared}/previews?ids=not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/v1/courses/{shared}/previews?ids={string.Join(",", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()))}")).StatusCode);
    }

    [Fact] public async Task Uploads_over_the_configured_limit_are_refused_with_413_and_the_limit_is_published()
    {
        using var f = new Factory(settings: new Dictionary<string, string?> { ["CourseExports:MaxUploadMegabytes"] = "1" });
        using var owner = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(owner, "instructor");
        var limits = await owner.GetFromJsonAsync<JsonElement>("/api/v1/course-exports/limits");
        Assert.Equal(1024 * 1024, limits.GetProperty("maxUploadBytes").GetInt64());
        var tooBig = new byte[2 * 1024 * 1024]; Random.Shared.NextBytes(tooBig);
        var response = await Upload(owner, tooBig);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MB upload limit", await response.Content.ReadAsStringAsync());

        // Two exports in one request are refused rather than silently using one of them.
        var two = new MultipartFormDataContent();
        foreach (var name in new[] { "a.imscc", "b.imscc" }) two.Add(new ByteArrayContent(new ExportFixture().Build()), "file", name);
        var both = await owner.PostAsync("/api/v1/course-exports", two);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Contains("one course export at a time", await both.Content.ReadAsStringAsync());
        using var anonymous = ApiTests.Client(f);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/course-exports/limits")).StatusCode);
    }

    [Fact] public async Task Export_upload_enforces_role_ownership_course_match_and_input_validation()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        var export = new ExportFixture().Build();
        // The reviewer also holds the Instructor role, so it may import its own course; an import never targets someone else's.
        var course = (await Ok(await Upload(owner, export))).CourseId.ToString();
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(ctl, export, course)).StatusCode); // not shared
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(owner, export, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, export, "30000000-0000-0000-0000-000000000001")).StatusCode); // demo course, not an export
        var other = new ExportFixture { CourseId = "g0some0other0course" }.Build();
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, other, course)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, "not a zip"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, export, fileName: "course.exe")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, export, canvasUrl: "http://canvas.example.test/courses/1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/course-exports", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
        using var anonymous = ApiTests.Client(f); await ApiTests.Csrf(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(anonymous, export)).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(owner, export)).StatusCode); // CSRF still enforced on uploads
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Courses.CountAsync(c => c.SourceType == "CanvasExport"));
    }
}
