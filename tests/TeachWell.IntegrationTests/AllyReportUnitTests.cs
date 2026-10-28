using System.Text;
using TeachWell.Domain;
using TeachWell.Infrastructure.Ally;
using Xunit;

// Reading Ally's report exports and joining them to TeachWell (ROADMAP item 2, ADR 020).
public class AllyReportUnitTests
{
    private static Stream Csv(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact] public void The_catalogue_says_what_ally_adds_and_where_the_two_tools_overlap()
    {
        Assert.Equal(AllyIssues.All.Count, AllyIssues.All.Select(i => i.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(AllyIssues.All, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
        // Every overlap names a check that exists, so a renamed check cannot leave a dangling mapping.
        Assert.All(AllyIssues.Overlaps, i => Assert.NotNull(CheckRegistry.Find(i.Overlap!)));
        // The file layer is the reason for ingesting at all, and TeachWell has no check for any of it.
        foreach (var code in new[] { "Scanned", "Tagged", "Parsable", "Security", "Title", "LanguagePresence", "Ocred" })
            Assert.True(AllyIssues.Find(code)!.PureGain, code);
        // Contrast and captions: contrast is a pure gain, captions overlap because TeachWell now checks them too.
        Assert.True(AllyIssues.Find("HtmlColorContrast")!.PureGain);
        Assert.Equal(MediaChecks.CaptionsMissing, AllyIssues.Find("HtmlCaption")!.Overlap);
        // Overlaps TeachWell wins, with the reason recorded where its check is the stronger of the two.
        Assert.Equal(DeterministicChecks.LinkEmpty, AllyIssues.Find("HtmlLinkName")!.Overlap);
        Assert.Contains("LINK_VAGUE", AllyIssues.Find("HtmlLinkName")!.Note);
        Assert.True(AllyIssues.Gains.Count > AllyIssues.Overlaps.Count, "most of what Ally reports should be new to TeachWell");
        // Severity: only "the file is unusable" becomes Critical.
        Assert.Equal("Critical", AllyIssues.SeverityOf(AllySeverity.Severe));
        Assert.Equal(("High", "Medium"), (AllyIssues.SeverityOf(AllySeverity.Major), AllyIssues.SeverityOf(AllySeverity.Minor)));
    }

    // Anthology documents the issue columns with the severity in the header: the column is "AlternativeText:2", not
    // "AlternativeText". An earlier version of this reader matched exact names and so found no issues at all in a real
    // export. Both forms are now accepted, because the documentation does not settle which the file actually uses.
    [Fact] public void Issue_columns_are_found_whether_or_not_the_header_carries_its_severity()
    {
        // The individual course report's header row, verbatim from Anthology's documentation.
        const string documented = "Id,Name,Mime type,Score,Deleted at,Library reference,Url," +
            "AlternativeText:2,Contrast:2,HtmlImageAlt:2,ImageSeizure:1,Scanned:1,Tagged:2,Title:3";
        var suffixed = AllyReportReader.ReadItems(Csv(documented + "\r\ni1,Week 1 reading.pdf,application/pdf,12,,lib-7,/courses/901/files/5,1,0,0,0,1,1,1\r\n"));

        Assert.Equal(new Dictionary<string, int> { ["AlternativeText"] = 1, ["Scanned"] = 1, ["Tagged"] = 1, ["Title"] = 1 },
            Assert.Single(suffixed.Rows).Issues);
        // The suffix is not mistaken for an unknown column, and nothing is reported as absent that is present.
        Assert.Empty(suffixed.UnknownIssueColumns);
        Assert.DoesNotContain(suffixed.Notes, n => n.Contains("none of Ally's issue columns"));

        // The same file without the suffixes reads identically.
        const string bare = "Id,Name,Mime type,Score,Deleted at,Library reference,Url," +
            "AlternativeText,Contrast,HtmlImageAlt,ImageSeizure,Scanned,Tagged,Title";
        var plain = AllyReportReader.ReadItems(Csv(bare + "\r\ni1,Week 1 reading.pdf,application/pdf,12,,lib-7,/courses/901/files/5,1,0,0,0,1,1,1\r\n"));
        Assert.Equal(suffixed.Rows[0].Issues, plain.Rows[0].Issues);
        Assert.Empty(plain.UnknownIssueColumns);
    }

    [Fact] public void A_severity_that_differs_from_the_documented_catalogue_is_reported_not_silently_accepted()
    {
        // The catalogue's severities come from documentation, so a real export disagreeing with it must be visible.
        var report = AllyReportReader.ReadItems(Csv("Id,Mime type,Scanned:3,Tagged:2\r\ni1,application/pdf,1,0\r\n"));
        Assert.Contains(report.Notes, n => n.Contains("Scanned (3 in this file, 1 in TeachWell)") && n.Contains("file's severity is the authority"));
        // A column whose severity agrees is not reported.
        Assert.DoesNotContain(report.Notes, n => n.Contains("Tagged ("));
        // The issue is still read: a severity disagreement is a note, not a reason to drop the finding.
        Assert.Equal(1, Assert.Single(report.Rows).Issues["Scanned"]);
    }

    [Fact] public void Base_names_are_taken_only_from_a_numeric_suffix()
    {
        Assert.Equal("AlternativeText", AllyTable.BaseName("AlternativeText:2"));
        Assert.Equal(2, AllyTable.SeverityIn("AlternativeText:2"));
        // A colon that is not a severity is left alone, so a column named "application/x-page" or "Time: start" survives.
        Assert.Equal("Time: start", AllyTable.BaseName("Time: start"));
        Assert.Null(AllyTable.SeverityIn("Time: start"));
        Assert.Equal("Mime type", AllyTable.BaseName("Mime type"));
        Assert.Equal("a:", AllyTable.BaseName("a:"));
        Assert.Equal(":2", AllyTable.BaseName(":2"));
    }

    [Fact] public void The_catalogue_matches_every_issue_column_anthology_documents()
    {
        // Anthology documents 38 entries in the shared issue block; LibraryReference is not an issue, so 37 are.
        Assert.Equal(37, AllyIssues.All.Count);
        string[] documented =
        [
            "AlternativeText", "Contrast", "HeadingsHigherLevel", "HeadingsPresence", "HeadingsSequential", "HeadingsStartAtOne",
            "HtmlBrokenLink", "HtmlCaption", "HtmlColorContrast", "HtmlDefinitionList", "HtmlEmptyHeading", "HtmlHasLang",
            "HtmlHeadingOrder", "HtmlHeadingsPresence", "HtmlHeadingsStart", "HtmlImageAlt", "HtmlImageRedundantAlt",
            "HtmlLabel", "HtmlLinkName", "HtmlList", "HtmlObjectAlt", "HtmlTdHasHeader", "HtmlTitle", "ImageContrast",
            "ImageDecorative", "ImageDescription", "ImageOcr", "ImageSeizure", "LanguageCorrect", "LanguagePresence",
            "Ocred", "Parsable", "Scanned", "Security", "TableHeaders", "Tagged", "Title",
        ];
        Assert.Equal(documented.Order(StringComparer.Ordinal), AllyIssues.All.Select(i => i.Code).Order(StringComparer.Ordinal));

        // The severities Anthology documents, as the suffix in each column header.
        var severities = new Dictionary<string, int>
        {
            ["ImageSeizure"] = 1, ["Parsable"] = 1, ["Scanned"] = 1, ["Security"] = 1,
            ["AlternativeText"] = 2, ["Contrast"] = 2, ["HeadingsPresence"] = 2, ["HtmlBrokenLink"] = 2, ["HtmlCaption"] = 2,
            ["HtmlColorContrast"] = 2, ["HtmlEmptyHeading"] = 2, ["HtmlHeadingsPresence"] = 2, ["HtmlHeadingsStart"] = 2,
            ["HtmlImageAlt"] = 2, ["HtmlLabel"] = 2, ["HtmlObjectAlt"] = 2, ["HtmlTdHasHeader"] = 2, ["ImageContrast"] = 2,
            ["ImageDecorative"] = 2, ["ImageDescription"] = 2, ["Ocred"] = 2, ["TableHeaders"] = 2, ["Tagged"] = 2,
            ["HeadingsHigherLevel"] = 3, ["HeadingsSequential"] = 3, ["HeadingsStartAtOne"] = 3, ["HtmlDefinitionList"] = 3,
            ["HtmlHasLang"] = 3, ["HtmlHeadingOrder"] = 3, ["HtmlImageRedundantAlt"] = 3, ["HtmlLinkName"] = 3, ["HtmlList"] = 3,
            ["HtmlTitle"] = 3, ["ImageOcr"] = 3, ["LanguageCorrect"] = 3, ["LanguagePresence"] = 3, ["Title"] = 3,
        };
        Assert.Equal(AllyIssues.All.Count, severities.Count);
        Assert.All(AllyIssues.All, i => Assert.Equal(severities[i.Code], (int)i.Severity));
    }

    [Fact] public void A_courses_report_is_read_by_column_name_with_its_scores_and_issue_counts()
    {
        var report = AllyReportReader.ReadCourses(AllyFixtures.Stream(AllyFixtures.Courses(
            ("901", "WEB 101", "Foundations of Web Development", 28, 72m, [("Scanned", 3), ("HtmlColorContrast", 5), ("Tagged", 2)]),
            ("902", "DES 120", "Designing for Everyone", 14, 95m, []))));

        Assert.Equal(2, report.Rows.Count);
        Assert.Empty(report.MissingColumns);
        Assert.Empty(report.UnknownIssueColumns);

        var web = report.Rows[0];
        Assert.Equal(("901", "WEB 101", "Fall 2026", "Computing"), (web.CourseId, web.CourseCode, web.TermName, web.DepartmentName));
        Assert.Equal((28, 72m), (web.Students, web.OverallScore));
        Assert.Equal(true, web.AllyEnabled);
        Assert.Equal(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), web.LastChecked);
        // Only flagged issues are kept, with their counts; a zero cell is absence, not a finding.
        Assert.Equal(new Dictionary<string, int> { ["Scanned"] = 3, ["HtmlColorContrast"] = 5, ["Tagged"] = 2 }, web.Issues);
        Assert.Empty(report.Rows[1].Issues);
    }

    [Fact] public void An_item_report_is_read_with_its_urls_and_library_references()
    {
        var report = AllyReportReader.ReadItems(AllyFixtures.Stream(AllyFixtures.Items(
            ("i1", "Week 1 reading.pdf", "application/pdf", 12m, "https://canvas.example.test/courses/901/files/5", "lib-7", [("Scanned", 1), ("Tagged", 1)]),
            ("i2", "Welcome and learning goals", "application/x-page", 80m, "https://canvas.example.test/courses/901/pages/welcome", "", [("HtmlImageAlt", 1)]))));

        Assert.Equal(2, report.Rows.Count);
        Assert.Equal(("i1", "application/pdf", "lib-7"), (report.Rows[0].Id, report.Rows[0].MimeType, report.Rows[0].LibraryReference));
        Assert.Equal(12m, report.Rows[0].Score);
        Assert.Null(report.Rows[1].LibraryReference);
        Assert.Equal("https://canvas.example.test/courses/901/pages/welcome", report.Rows[1].Url);
    }

    [Fact] public void The_two_report_shapes_are_told_apart_and_a_wrong_file_says_which_to_upload()
    {
        var courses = AllyFixtures.Courses(("901", "WEB 101", "x", 1, 50m, []));
        var items = AllyFixtures.Items(("i1", "x", "application/pdf", 1m, "/a", "", []));
        // Uploading each as the other is a message naming the right file, not a parse failure.
        Assert.Contains("Courses report", Assert.Throws<AllyReadException>(() => AllyReportReader.ReadItems(AllyFixtures.Stream(courses))).Message);
        Assert.Contains("Export the institution report", Assert.Throws<AllyReadException>(() => AllyReportReader.ReadCourses(AllyFixtures.Stream(items))).Message);
    }

    [Fact] public void A_file_whose_columns_differ_from_the_documentation_is_read_and_reports_what_it_could_not_find()
    {
        // The reason the reader is tolerant: this catalogue was built from documentation, not a real export.
        var report = AllyReportReader.ReadCourses(Csv(
            "Course id,Course code,Overall score,Scanned,AllyFutureRule\r\n901,WEB 101,72,3,1\r\n"));

        Assert.Single(report.Rows);
        Assert.Equal(3, report.Rows[0].Issues["Scanned"]);
        // Expected fields that are absent are named rather than silently null.
        Assert.Contains("Number of students", report.MissingColumns);
        Assert.Null(report.Rows[0].Students);
        // A rule Ally has added arrives as an unknown column and is reported, not dropped in silence.
        Assert.Equal(["AllyFutureRule"], report.UnknownIssueColumns);
        Assert.Contains(report.Notes, n => n.Contains("AllyFutureRule") && n.Contains("TeachWell needs updating"));
        Assert.Contains(report.Notes, n => n.Contains("issue columns are absent"));
    }

    [Fact] public void Malformed_and_oversized_files_are_refused_with_a_message_rather_than_an_exception()
    {
        Assert.Contains("empty", Assert.Throws<AllyReadException>(() => AllyReportReader.ReadCourses(Csv(""))).Message);
        Assert.Contains("unclosed quoted value", Assert.Throws<AllyReadException>(() => AllyReportReader.ReadCourses(Csv("Course id\r\n\"901"))).Message);
        Assert.Contains("no column names", Assert.Throws<AllyReadException>(() => AllyTable.Read(Csv(",,\r\n1,2,3"))).Message);
        Assert.Contains("not Ally's Courses report", Assert.Throws<AllyReadException>(() => AllyReportReader.ReadCourses(Csv("A,B\r\n1,2"))).Message);
    }

    [Fact] public void Quoted_values_commas_newlines_and_doubled_quotes_survive_the_reader()
    {
        var report = AllyReportReader.ReadCourses(Csv(
            "Course id,Course name,Scanned\r\n901,\"Statistics, Data and \"\"Truth\"\"\",2\r\n902,\"A name\nwith a newline\",0\r\n"));
        Assert.Equal(2, report.Rows.Count);
        Assert.Equal("Statistics, Data and \"Truth\"", report.Rows[0].CourseName);
        Assert.Equal("A name\nwith a newline", report.Rows[1].CourseName);
        Assert.Equal(2, report.Rows[0].Issues["Scanned"]);
    }

    [Fact] public void Rows_with_no_identifier_are_counted_rather_than_guessed_at()
    {
        var report = AllyReportReader.ReadCourses(Csv("Course id,Course code\r\n901,WEB 101\r\n,ORPHAN\r\n902,DES 120\r\n"));
        Assert.Equal(["901", "902"], report.Rows.Select(r => r.CourseId));
        Assert.Contains(report.Notes, n => n.Contains("1 course row had no identifier"));
    }

    // --- The join ---

    [Fact] public void Courses_join_on_the_canvas_course_id_and_unimported_courses_are_reported()
    {
        var teachWell = new[] { new CourseKey(Guid.NewGuid(), "901", "WEB 101"), new CourseKey(Guid.NewGuid(), "902", "DES 120") };
        var (matches, summary) = AllyReconciliation.MatchCourses(["901", "903", "902"], teachWell);

        Assert.Equal([AllyReconciliation.Matched, AllyReconciliation.NoSuchCourse, AllyReconciliation.Matched], matches.Select(m => m.Reason));
        Assert.Equal(teachWell[0].Id, matches[0].CourseId);
        Assert.Equal((3, 2, 1, 66), (summary.Total, summary.Matched, summary.Unmatched, summary.MatchedPercent));
        // Ally covers the whole LMS; TeachWell covers what was imported. The gap is stated, not hidden.
        Assert.Contains(summary.Notes, n => n.Contains("have not been imported into TeachWell"));
    }

    [Fact] public void Items_join_on_host_and_path_ignoring_query_strings_and_trailing_slashes()
    {
        var page = new ElementKey(Guid.NewGuid(), "page", "Welcome and learning goals", "https://canvas.example.test/courses/901/pages/welcome");
        var elements = new[] { page, new ElementKey(Guid.NewGuid(), "assignment", "Project 1", null) };

        var (matches, summary) = AllyReconciliation.MatchItems(
        [
            // Same page, with a query string and a different case and a trailing slash: still the same page.
            ("i1", "https://Canvas.Example.Test/courses/901/pages/welcome/?wrap=1", "application/x-page"),
            // A course file: not a TeachWell element, and the reason for reading Ally at all.
            ("i2", "https://canvas.example.test/courses/901/files/5", "application/pdf"),
            // A page TeachWell has not imported.
            ("i3", "https://canvas.example.test/courses/901/pages/week-9", "application/x-page"),
            ("i4", null, "application/x-assignment"),
        ], elements);

        Assert.Equal(page.Id, matches[0].ElementId);
        Assert.Equal("Page: Welcome and learning goals", matches[0].Where);
        Assert.Equal([AllyReconciliation.Matched, AllyReconciliation.NotImported, AllyReconciliation.NoMatchingUrl, AllyReconciliation.NoUrl],
            matches.Select(m => m.Reason));
        Assert.Equal((4, 1, 3), (summary.Total, summary.Matched, summary.Unmatched));
        // Files and genuine misses are reported separately, because only the second is a problem to fix.
        Assert.Contains(summary.Notes, n => n.Contains("are course files"));
        Assert.Contains(summary.Notes, n => n.Contains("does not match any imported item"));
        Assert.Contains(summary.Notes, n => n.Contains("have no address"));
    }

    [Fact] public void A_course_imported_from_an_export_file_has_no_addresses_and_the_join_says_so()
    {
        // ADR 010: an uploaded export has no Canvas addresses unless the uploader supplied the course's address.
        var elements = new[] { new ElementKey(Guid.NewGuid(), "page", "Welcome", null), new ElementKey(Guid.NewGuid(), "assignment", "Project 1", null) };
        var (matches, summary) = AllyReconciliation.MatchItems([("i1", "https://canvas.example.test/courses/901/pages/welcome", "application/x-page")], elements);

        Assert.Null(Assert.Single(matches).ElementId);
        Assert.Equal(0, summary.Matched);
        Assert.Contains(summary.Notes, n => n.Contains("No imported item in this course has a Canvas address"));
    }

    [Fact] public void Url_normalization_drops_what_must_not_be_compared_or_stored()
    {
        // Query strings can carry a Canvas verifier token: comparing them would both miss matches and keep a secret.
        Assert.Equal(AllyReconciliation.Path("https://x.test/courses/1/files/5?verifier=secret"), AllyReconciliation.Path("https://x.test/courses/1/files/5"));
        Assert.DoesNotContain("secret", AllyReconciliation.Path("https://x.test/a?verifier=secret"));
        Assert.Equal("x.test/courses/1", AllyReconciliation.Path("HTTPS://X.TEST/courses/1/#top"));
        Assert.Equal("/courses/1/pages/a", AllyReconciliation.Path("/courses/1/pages/a"));
        Assert.Null(AllyReconciliation.Path(null));
        Assert.Null(AllyReconciliation.Path(" "));
        Assert.Null(AllyReconciliation.Path("javascript:alert(1)"));
        // A protocol-relative address still resolves.
        Assert.Equal("x.test/a", AllyReconciliation.Path("//x.test/a"));
        // Percent-for-percent, a differing path is a different item.
        Assert.NotEqual(AllyReconciliation.Path("https://x.test/a"), AllyReconciliation.Path("https://x.test/b"));
    }

    [Fact] public void A_match_rate_is_floored_so_a_report_never_rounds_up_to_complete()
    {
        var summary = new ReconciliationSummary(1000, 996, 4, []);
        Assert.Equal(99, summary.MatchedPercent);
        Assert.Equal(0, new ReconciliationSummary(0, 0, 0, []).MatchedPercent);
        Assert.Equal(100, new ReconciliationSummary(5, 5, 0, []).MatchedPercent);
    }
}
