using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

// The Ally report format check (ADR 020 slice 1): a read-only upload that says what the reader understood.
public class AllyCheckTests
{
    private const string Url = "/api/v1/ally/report-check";

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] csv, string fileName = "courses.csv")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);
        return await client.PostAsync(Url, form);
    }

    [Fact] public async Task A_courses_report_is_read_matched_against_imported_courses_and_nothing_is_stored()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");

        // The demo courses' Canvas ids are demo-1 and demo-2, so one row matches and one does not.
        var csv = AllyFixtures.Courses(
            ("demo-1", "WEB 101", "Foundations of Web Development", 28, 72m, [("Scanned", 3), ("Tagged", 2), ("HtmlColorContrast", 5), ("HtmlImageAlt", 4)]),
            ("not-imported", "OTH 999", "A course TeachWell has never seen", 11, 40m, [("Parsable", 1)]));

        long elementsBefore, snapshotsBefore;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            elementsBefore = await db.Elements.CountAsync();
            snapshotsBefore = await db.ElementSnapshots.CountAsync();
        }

        var response = await Upload(ctl, csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var check = (await response.Content.ReadFromJsonAsync<AllyReportCheckDto>())!;

        Assert.Equal("Courses", check.Kind);
        Assert.Equal("courses.csv", check.FileName);
        Assert.Equal(2, check.Rows);
        // The fixture carries every documented issue column, so the reader should recognise all of them.
        Assert.Equal(check.IssueColumnsKnown, check.IssueColumnsRecognised);
        Assert.Equal(AllyIssues.All.Count, check.IssueColumnsKnown);
        Assert.Empty(check.UnknownColumns);
        Assert.Empty(check.MissingColumns);

        // Issues are reported with their provenance: what Ally adds, and what TeachWell already checks.
        var scanned = Assert.Single(check.Issues, i => i.Code == "Scanned");
        Assert.True(scanned.PureGain);
        Assert.Equal("Critical", scanned.Severity);
        Assert.Equal((1, 3), (scanned.Rows, scanned.Places));
        var alt = Assert.Single(check.Issues, i => i.Code == "HtmlImageAlt");
        Assert.False(alt.PureGain);
        Assert.Equal(DeterministicChecks.ImageAltMissing, alt.Overlap);
        // Issues absent from the file are not listed at all.
        Assert.DoesNotContain(check.Issues, i => i.Code == "ImageSeizure");

        // The join is reported, including the course Ally covers that TeachWell has not imported.
        Assert.NotNull(check.Courses);
        Assert.Equal((2, 1, 1, 50), (check.Courses!.Total, check.Courses.Matched, check.Courses.Unmatched, check.Courses.MatchedPercent));
        Assert.Equal(["not-imported"], check.UnmatchedCourseIds);
        Assert.Contains(check.Courses.Notes, n => n.Contains("have not been imported into TeachWell"));
        Assert.Contains("Nothing from the file was saved", check.Caveat);

        // Read-only: no element, snapshot or finding was created by the upload.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(elementsBefore, await db.Elements.CountAsync());
            Assert.Equal(snapshotsBefore, await db.ElementSnapshots.CountAsync());
        }
    }

    [Fact] public async Task One_courses_report_is_told_from_one_courses_own_report()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");

        var items = AllyFixtures.Items(
            ("i1", "Week 1 reading.pdf", "application/pdf", 12m, "https://canvas.example.test/courses/1/files/5", "lib-7", [("Scanned", 1)]),
            ("i2", "Welcome", "application/x-page", 80m, "https://canvas.example.test/courses/1/pages/welcome", "", [("HtmlImageAlt", 1)]));
        var check = (await (await Upload(ctl, items, "course-report.csv")).Content.ReadFromJsonAsync<AllyReportCheckDto>())!;

        Assert.Equal("Items", check.Kind);
        Assert.Equal(2, check.Rows);
        // An item report carries no course identity, so there is nothing to match it against.
        Assert.Null(check.Courses);
        Assert.Empty(check.UnmatchedCourseIds);
    }

    [Fact] public async Task A_file_whose_columns_differ_from_the_documentation_is_still_read_and_says_what_it_could_not_find()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");

        // The whole point of this screen: a real export that does not match the documentation must be legible.
        var csv = System.Text.Encoding.UTF8.GetBytes("Course id,Course code,Overall score,Scanned:1,AllyFutureRule\r\ndemo-1,WEB 101,72,3,1\r\n");
        var check = (await (await Upload(ctl, csv)).Content.ReadFromJsonAsync<AllyReportCheckDto>())!;

        Assert.Equal(1, check.Rows);
        Assert.Equal(3, Assert.Single(check.Issues, i => i.Code == "Scanned").Places);
        // Recognised is well below known, which is the headline answer to "does the reader understand this file".
        Assert.True(check.IssueColumnsRecognised < check.IssueColumnsKnown);
        Assert.Equal(1, check.IssueColumnsRecognised);
        Assert.Contains("AllyFutureRule", check.UnknownColumns);
        Assert.Contains("Number of students", check.MissingColumns);
        Assert.Contains(check.Notes, n => n.Contains("TeachWell needs updating"));
    }

    [Fact] public async Task Only_the_CTL_may_check_a_report_and_bad_uploads_are_refused_with_a_message()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f); using var academic = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(academic, "academic");

        var csv = AllyFixtures.Courses(("demo-1", "WEB 101", "x", 1, 50m, []));
        // Instructors and the academic administrator have no business uploading institutional reports.
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(owner, csv)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(academic, csv)).StatusCode);

        // Wrong extension, and a file that is not an Ally report at all.
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(ctl, csv, "report.xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(ctl, System.Text.Encoding.UTF8.GetBytes("A,B\r\n1,2\r\n"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(ctl, [])).StatusCode);
        // A posted form with no file at all.
        using var empty = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.BadRequest, (await ctl.PostAsync(Url, empty)).StatusCode);
    }
}
