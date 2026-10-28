using System.Net;
using System.Net.Http.Json;
using TeachWell.Application;
using Microsoft.Extensions.Configuration;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class ContentCheckUnitTests
{
    private static CheckReport Element(string type, string text, ContentEvidence? evidence = null, string status = "Published") =>
        DeterministicChecks.Run(type, status, evidence ?? new ContentEvidence(1, [], [], [], [], type == "assignment" ? new(true, 10, true) : null), null, text);
    private static string[] Codes(CheckReport report) => report.Findings.Select(f => f.CheckCode).Where(c => !CheckRegistry.InitialCodes.Contains(c)).ToArray();
    private static string Repeat(string sentence, int times) => string.Join(" ", Enumerable.Repeat(sentence, times));

    [Fact] public void Text_measures_count_words_sentences_and_estimate_grade()
    {
        var stats = TextStats.Of("Welcome to the course\nRead the syllabus. Then post an introduction!\n\n- Item one");
        Assert.Equal((13, 4, 3), (stats.Words, stats.Sentences, stats.Lines.Count)); // blank lines are dropped
        Assert.Equal((1, 2, 2), (TextStats.SyllablesIn("cat"), TextStats.SyllablesIn("table"), TextStats.SyllablesIn("making")));
        var simple = TextStats.Of(Repeat("The cat sat on the mat.", 30)).Grade!.Value;
        var dense = TextStats.Of(Repeat("Institutional accreditation necessitates comprehensive documentation of organizational effectiveness and continuous improvement initiatives.", 10)).Grade!.Value;
        Assert.True(simple < 3 && dense > 16, $"simple {simple}, dense {dense}");
        Assert.Null(TextStats.Of("").Grade);
        Assert.Equal(8, TextStats.ReadingMinutes(1501, 200));
    }

    [Fact] public void Bloom_strips_lead_ins_classifies_verbs_and_finds_vague_phrases()
    {
        Assert.Equal("design a responsive page", Bloom.Statement("Students will be able to design a responsive page"));
        Assert.Equal((Bloom.Create, Bloom.Understand, Bloom.Remember), (Bloom.LevelOf("Students will be able to design a page")!, Bloom.LevelOf("Explain the box model")!, Bloom.LevelOf("list HTML elements")!));
        Assert.Null(Bloom.LevelOf("Understand CSS"));
        string[] vague = ["understand", "be familiar with"];
        Assert.Equal("understand", Bloom.VaguePhrase("Learners will understand CSS layout", vague));
        Assert.Equal("be familiar with", Bloom.VaguePhrase("Be familiar with Git", vague));
        Assert.Null(Bloom.VaguePhrase("Understanding check: explain grid", vague)); // whole words only
        Assert.True(Bloom.IsLowerOrder(Bloom.Understand) && !Bloom.IsLowerOrder(Bloom.Apply));
    }

    [Fact] public void Long_dense_pages_report_length_structure_paragraph_and_reading_level()
    {
        var text = Repeat("Institutional accreditation necessitates comprehensive documentation of organizational effectiveness.", 200);
        var codes = Codes(Element("page", text));
        Assert.Equal([ContentChecks.PageTooLong, ContentChecks.LongNoHeadings, ContentChecks.ParagraphTooLong, ContentChecks.ReadingLevel], codes);
        // With headings and short paragraphs, only length and reading level remain.
        var structured = string.Join("\n", Enumerable.Range(0, 20).Select(_ => Repeat("Institutional accreditation necessitates comprehensive documentation of organizational effectiveness.", 10)));
        var withHeadings = new ContentEvidence(1, [], [new(0, 2, "Section")], [], []);
        Assert.Equal([ContentChecks.PageTooLong, ContentChecks.ReadingLevel], Codes(Element("page", structured, withHeadings)));
        Assert.Contains("an estimate for English text", Element("page", text).Findings.Single(f => f.CheckCode == ContentChecks.ReadingLevel).Evidence);
    }

    [Fact] public void Empty_pages_and_thin_assignments_are_flagged_but_unpublished_drafts_are_not()
    {
        Assert.Equal([ContentChecks.PageNearlyEmpty], Codes(Element("page", "TBA")));
        Assert.Empty(Codes(Element("page", "TBA", status: "Unpublished")));
        var noPoints = new ContentEvidence(1, [], [], [], [], new(true, 0, true));
        Assert.Equal([ContentChecks.InstructionsShort, ContentChecks.PointsMissing], Codes(Element("assignment", "Submit your reflection.", noPoints)));
        var unknown = Element("assignment", Repeat("Write a detailed plan for the project and explain each step.", 6), new ContentEvidence(1, [], [], [], []));
        Assert.Empty(Codes(unknown));
        Assert.Contains("Assignment points were not captured, so points were not checked.", unknown.Limitations);
    }

    [Fact] public void Typed_lists_and_colour_only_instructions_are_found_line_by_line()
    {
        var report = Element("page", "Before you start\n- Read chapter 2\n- Watch the video\n1. Not a run on its own\nComplete the items in red.\nA real list item\nAnother");
        Assert.Equal(["LIST_TYPED_AS_TEXT:1", "COLOR_ONLY_REFERENCE:4"], report.Findings.Where(f => !CheckRegistry.InitialCodes.Contains(f.CheckCode) && f.CheckCode != ContentChecks.PageNearlyEmpty).Select(f => f.Fingerprint));
        Assert.Contains("\"in red\"", report.Findings.Single(f => f.CheckCode == ContentChecks.ColorOnly).Description);
        Assert.Empty(Codes(Element("page", Repeat("Read the Red Book, which is on reserve in the library for this course.", 3))));
    }

    [Fact] public void Text_checks_report_a_limitation_instead_of_passing_when_no_text_is_supplied()
    {
        var report = DeterministicChecks.Run("page", "Published", new ContentEvidence(1, [], [], [], []));
        Assert.Empty(report.Findings);
        Assert.Contains(report.Limitations, l => l.Contains("length, reading-level and wording checks were not run"));
    }
}

public class CourseDesignCheckUnitTests
{
    private static CourseItemEvidence Page(string title, int words = 100) => new(Guid.NewGuid(), "page", title, "Published", Guid.NewGuid(), null, null, false, words);
    private static CourseItemEvidence Assignment(string title, int words = 100, DateTime? due = null) =>
        new(Guid.NewGuid(), "assignment", title, "Published", Guid.NewGuid(), due is not null, true, true, words, 10, due);
    private static ModuleEvidence Module(string title, params CourseItemEvidence[] items) => new(Guid.NewGuid(), title, "Published", 0, items);
    private static CourseStructureEvidence Course(IReadOnlyList<ModuleEvidence> modules, IReadOnlyList<OutcomeEvidence>? outcomes = null) =>
        new(CourseStructureEvidence.CurrentVersion, CourseStructureEvidence.KindName, modules, [], outcomes?.Count ?? 1, false, outcomes ?? [new(Guid.NewGuid(), "C1", "Build a page", "", 1)]);
    private static List<ProposedFinding> New(CheckReport report) => report.Findings.Where(f => !CheckRegistry.InitialCodes.Contains(f.CheckCode)).ToList();

    [Fact] public void Modules_without_an_overview_or_an_assessment_are_flagged_after_the_orientation_module()
    {
        var reading = Module("Week 2: Data", Page("Reading list"), Page("Notes"));
        var report = CourseChecks.Run(Course([Module("Start here", Page("Welcome")), Module("Week 1: Basics", Page("Week 1 overview"), Assignment("Lab 1")), reading]));
        Assert.Equal([(CourseDesignChecks.ModuleNoOverview, reading.ElementId), (CourseDesignChecks.ModuleNoAssessment, reading.ElementId)],
            New(report).Where(f => f.CheckCode is CourseDesignChecks.ModuleNoOverview or CourseDesignChecks.ModuleNoAssessment).Select(f => (f.CheckCode, f.SubjectElementId!.Value)));
    }

    [Fact] public void Quizzes_count_as_assessments_and_text_headers_are_not_counted_as_content()
    {
        CourseItemEvidence Header(string title) => new(Guid.NewGuid(), "moduleItem", title, ReviewVocabulary.TextHeaderStatus, null, null, null, false);
        var quiz = new CourseItemEvidence(Guid.NewGuid(), ReviewVocabulary.QuizType, "Week 2 quiz", "Published", null, false, null, false, 80, 5);
        // "Overview" as a text header introduces the module; the quiz is its assessment; headers do not make it crowded.
        var week2 = Module("Week 2: Data", [Header("Overview"), Page("Reading list"), quiz, .. Enumerable.Range(1, 14).Select(i => Header($"Part {i}"))]);
        var report = New(CourseChecks.Run(Course([Module("Start here", Page("Welcome")), week2])));
        Assert.DoesNotContain(report, f => f.CheckCode is CourseDesignChecks.ModuleNoOverview or CourseDesignChecks.ModuleNoAssessment or CourseDesignChecks.ModuleTooManyItems);
        var due = Assert.Single(CourseChecks.Run(Course([Module("Start here", Page("Welcome")), week2])).Findings, f => f.CheckCode == CourseChecks.DueDateMissing);
        Assert.Equal(("Quiz has no due date", quiz.ElementId), (due.Title, due.SubjectElementId!.Value));
    }

    [Fact] public void Graded_discussions_count_as_assessments_and_ungraded_ones_do_not()
    {
        var graded = new CourseItemEvidence(Guid.NewGuid(), ReviewVocabulary.DiscussionType, "Introduce yourself", "Published", null, true, false, true, 60, 5, new DateTime(2026, 11, 3, 6, 59, 0, DateTimeKind.Utc));
        var open = new CourseItemEvidence(Guid.NewGuid(), ReviewVocabulary.DiscussionType, "Questions board", "Published", null, null, null, false, 20);
        Assert.True(graded.IsAssessed); Assert.False(open.IsAssessed);
        var week1 = Module("Week 1: A", Page("Week 1 overview"), graded);
        var week2 = Module("Week 2: B", Page("Week 2 overview"), open);
        var report = CourseChecks.Run(Course([Module("Start here", Page("Welcome")), week1, week2]));
        // Week 1 has its assessment (the graded discussion); Week 2's ungraded discussion is not one, and is not asked for a due date or alignment.
        Assert.Equal([week2.ElementId], New(report).Where(f => f.CheckCode == CourseDesignChecks.ModuleNoAssessment).Select(f => f.SubjectElementId!.Value));
        Assert.DoesNotContain(report.Findings, f => f.SubjectElementId == open.ElementId);
        // The graded discussion is the only assessment, worth all the points, so there is no low-stakes practice before it.
        Assert.Contains(New(report), f => f.CheckCode == CourseDesignChecks.EarlyPracticeMissing);
    }

    [Fact] public void Crowded_modules_are_flagged_against_the_configured_maximum()
    {
        var crowded = Module("Week 1: Everything", [Page("Week 1 overview"), .. Enumerable.Range(1, 15).Select(i => Assignment($"Task {i}"))]);
        Assert.Contains(New(CourseChecks.Run(Course([crowded]))), f => f.CheckCode == CourseDesignChecks.ModuleTooManyItems && f.SubjectElementId == crowded.ElementId);
        var relaxed = CourseDesignChecks.Rules.Single(r => r.Code == CourseDesignChecks.ModuleTooManyItems).Defaults() with { Numbers = new Dictionary<string, decimal> { [CourseDesignChecks.MaxItems] = 20 } };
        Assert.DoesNotContain(New(CourseChecks.Run(Course([crowded]), new CheckProfile(new Dictionary<string, CheckSettings> { [CourseDesignChecks.ModuleTooManyItems] = relaxed }))),
            f => f.CheckCode == CourseDesignChecks.ModuleTooManyItems);
    }

    [Fact] public void Workload_estimates_flag_modules_far_from_the_median()
    {
        var light = Module("Week 2: Short", Page("Week 2 overview", 100));
        var heavy = Module("Week 4: Huge", Page("Week 4 overview", 30000), Assignment("Essay"));
        var report = CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview", 2000), Assignment("A1")), light, Module("Week 3: C", Page("Week 3 overview", 2000), Assignment("C1")), heavy]));
        var flagged = New(report).Where(f => f.CheckCode == CourseDesignChecks.WorkloadImbalance).ToList();
        Assert.Equal([(light.ElementId, "Module is much lighter than the others"), (heavy.ElementId, "Module is much heavier than the others")], flagged.Select(f => (f.SubjectElementId!.Value, f.Title)));
        Assert.Contains(report.Limitations, l => l.StartsWith("Workload is estimated"));
        Assert.DoesNotContain(New(CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview")), Module("Week 2: B", Page("Week 2 overview"))]))),
            f => f.CheckCode == CourseDesignChecks.WorkloadImbalance); // fewer than three modules: nothing to compare
    }

    [Fact] public void Several_assignments_due_the_same_day_are_flagged_once_per_day()
    {
        var day = new DateTime(2026, 11, 2, 6, 59, 0, DateTimeKind.Utc);
        var report = CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), Assignment("Quiz", due: day), Assignment("Lab", due: day.AddHours(1)), Assignment("Post", due: day.AddHours(2)), Assignment("Next", due: day.AddDays(1)))]));
        var cluster = Assert.Single(New(report), f => f.CheckCode == CourseDesignChecks.DueSameDay);
        Assert.Equal("ASSIGNMENTS_DUE_SAME_DAY:2026-11-02", cluster.Fingerprint);
        Assert.Contains("\"Quiz\"; \"Lab\"; \"Post\"", cluster.Evidence);
        var undated = new CourseItemEvidence(Guid.NewGuid(), "assignment", "Old import", "Published", null, true, true, true, 50, 10, null);
        Assert.Contains(CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), undated)])).Limitations, l => l.Contains("Due dates were not captured for 1"));
    }

    [Fact] public void Modules_whose_deadlines_run_backwards_are_flagged_against_the_previous_dated_module()
    {
        var october = new DateTime(2026, 10, 5, 23, 59, 0, DateTimeKind.Utc);
        ModuleEvidence Week(string title, DateTime? due) => Module(title, Page($"{title} overview"), Assignment($"{title} quiz", due: due));

        // Week 3 is due a week before Week 2, so students meet its deadline first.
        var backwards = Week("Week 3", october);
        var report = CourseChecks.Run(Course([Week("Week 1", october.AddDays(-7)), Week("Week 2", october.AddDays(7)), backwards]));
        var finding = Assert.Single(New(report), f => f.CheckCode == CourseDesignChecks.ModuleDueOutOfOrder);
        Assert.Equal(backwards.ElementId, finding.SubjectElementId);
        Assert.Contains("7 day(s) before", finding.Description);
        Assert.Contains("earliest due 2026-10-05", finding.Evidence);

        // Deadlines that advance with the modules are not flagged.
        Assert.DoesNotContain(New(CourseChecks.Run(Course([Week("Week 1", october), Week("Week 2", october.AddDays(7)), Week("Week 3", october.AddDays(14))]))),
            f => f.CheckCode == CourseDesignChecks.ModuleDueOutOfOrder);

        // An undated module drops out of the chain rather than breaking it: Week 3 is still compared with Week 1.
        var afterGap = Week("Week 3", october.AddDays(-14));
        Assert.Contains(New(CourseChecks.Run(Course([Week("Week 1", october), Week("Week 2", null), afterGap]))),
            f => f.CheckCode == CourseDesignChecks.ModuleDueOutOfOrder && f.SubjectElementId == afterGap.ElementId);

        // Fewer than two dated modules: nothing to compare.
        Assert.DoesNotContain(New(CourseChecks.Run(Course([Week("Week 1", october), Week("Week 2", null)]))), f => f.CheckCode == CourseDesignChecks.ModuleDueOutOfOrder);

        // The tolerance lets a module overlap the previous one by a configured number of days.
        var tolerant = CourseDesignChecks.Rules.Single(r => r.Code == CourseDesignChecks.ModuleDueOutOfOrder).Defaults()
            with { Numbers = new Dictionary<string, decimal> { [CourseDesignChecks.ToleranceDays] = 10 } };
        Assert.DoesNotContain(New(CourseChecks.Run(Course([Week("Week 1", october.AddDays(-7)), Week("Week 2", october.AddDays(7)), Week("Week 3", october)]),
            new CheckProfile(new Dictionary<string, CheckSettings> { [CourseDesignChecks.ModuleDueOutOfOrder] = tolerant }))), f => f.CheckCode == CourseDesignChecks.ModuleDueOutOfOrder);
    }

    [Fact] public void Outcomes_are_checked_for_vague_verbs_assessment_and_cognitive_level()
    {
        var outcomes = new OutcomeEvidence[] { new(Guid.NewGuid(), "C1", "Understand CSS layout", "", 1), new(Guid.NewGuid(), "C2", "Students will be able to explain the box model", "", 0),
            new(Guid.NewGuid(), "C3", "List common HTML elements", "", 2) };
        var report = CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), Assignment("Lab"))], outcomes));
        var found = New(report).Select(f => (f.CheckCode, f.Evidence.Split(':')[0])).ToList();
        Assert.Contains((CourseDesignChecks.OutcomeVagueVerb, "C1"), found);
        Assert.Contains((CourseDesignChecks.OutcomeNotAssessed, "C2"), found);
        var lower = Assert.Single(New(report), f => f.CheckCode == CourseDesignChecks.OutcomesLowerOrder);
        Assert.Contains("C2: Understand (\"explain\"); C3: Remember (\"list\")", lower.Evidence);
        var higher = outcomes.Append(new(Guid.NewGuid(), "C4", "Design an accessible page", "", 1)).ToList();
        Assert.DoesNotContain(New(CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), Assignment("Lab"))], higher))), f => f.CheckCode == CourseDesignChecks.OutcomesLowerOrder);
    }

    [Fact] public void Deadlines_are_grouped_by_day_in_the_institution_time_zone()
    {
        var denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        // 11:00, 13:00 and 23:59 on November 2 in Denver; the last is already November 3 in UTC.
        var course = Course([Module("Week 1: A", Page("Week 1 overview"), Assignment("Quiz", due: new DateTime(2026, 11, 2, 18, 0, 0, DateTimeKind.Utc)),
            Assignment("Lab", due: new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc)), Assignment("Post", due: new DateTime(2026, 11, 3, 6, 59, 0, DateTimeKind.Utc)))]);
        Assert.DoesNotContain(New(CourseChecks.Run(course)), f => f.CheckCode == CourseDesignChecks.DueSameDay);
        var local = Assert.Single(New(CourseChecks.Run(course, null, denver)), f => f.CheckCode == CourseDesignChecks.DueSameDay);
        Assert.Equal("ASSIGNMENTS_DUE_SAME_DAY:2026-11-02", local.Fingerprint);
        Assert.Contains("(America/Denver)", local.Evidence);
    }

    [Fact] public void Institution_time_zone_setting_defaults_to_utc_and_rejects_unknown_zones()
    {
        IConfiguration Config(string? zone) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Institution:TimeZone"] = zone }).Build();
        Assert.Equal(TimeZoneInfo.Utc, new InstitutionSettings(Config(null)).TimeZone);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById("America/Denver").BaseUtcOffset, new InstitutionSettings(Config("America/Denver")).TimeZone.BaseUtcOffset);
        Assert.Contains("Mars/Olympus_Mons", Assert.Throws<InvalidOperationException>(() => new InstitutionSettings(Config("Mars/Olympus_Mons"))).Message);
    }

    [Fact] public void Courses_without_early_low_stakes_work_are_flagged()
    {
        CourseItemEvidence Graded(string title, decimal points) => new(Guid.NewGuid(), "assignment", title, "Published", null, true, true, true, 100, points);
        var week1 = Module("Week 1: A", Page("Week 1 overview"), Graded("Project A", 50));
        var heavy = New(CourseChecks.Run(Course([week1, Module("Week 2: B", Page("Week 2 overview"), Graded("Project B", 50))])));
        var early = Assert.Single(heavy, f => f.CheckCode == CourseDesignChecks.EarlyPracticeMissing);
        Assert.Equal(week1.ElementId, early.SubjectElementId);
        Assert.Contains("\"Project A\" 50 pts (50%)", early.Evidence);
        // A 5-point quiz in Week 1 (under 10% of 105 points) is the low-stakes practice the check looks for.
        Assert.DoesNotContain(New(CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), Graded("Quiz", 5), Graded("Project A", 50)),
            Module("Week 2: B", Page("Week 2 overview"), Graded("Project B", 50))]))), f => f.CheckCode == CourseDesignChecks.EarlyPracticeMissing);
        var late = New(CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview")), Module("Week 2: B", Page("Week 2 overview")), Module("Week 3: C", Page("Week 3 overview"), Graded("Final", 100))])));
        Assert.Contains("contain no assignment", Assert.Single(late, f => f.CheckCode == CourseDesignChecks.EarlyPracticeMissing).Description);
        var unknown = CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), new(Guid.NewGuid(), "assignment", "Old", "Published", null, true, true, true, 100))]));
        Assert.Contains(unknown.Limitations, l => l.Contains("Points were not captured for 1"));
    }

    [Fact] public void Higher_order_outcomes_assessed_only_at_lower_levels_are_flagged()
    {
        Assert.Equal((Bloom.Create, (string?)null), (Bloom.HighestLevelIn("Explain the idea, then design a layout")!, Bloom.HighestLevelIn("Submit it on time")));
        CourseItemEvidence Leveled(string title, string? level) => new(Guid.NewGuid(), "assignment", title, "Published", null, true, true, true, 100, 10, null, level);
        var explain = Leveled("Explain three principles", Bloom.Understand);
        var build = Leveled("Build a page", Bloom.Create);
        var vague = Leveled("Submit your work", null);
        OutcomeEvidence Outcome(string code, string title, params CourseItemEvidence[] mapped) => new(Guid.NewGuid(), code, title, "", mapped.Length, mapped.Select(m => m.ElementId).ToList());
        var outcomes = new[] { Outcome("C1", "Design an accessible page", explain), Outcome("C2", "Design a responsive layout", explain, build),
            Outcome("C3", "Evaluate a page's accessibility", vague), Outcome("C4", "Explain the box model", explain) };
        var report = CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), explain, build, vague)], outcomes));
        var mismatch = Assert.Single(New(report), f => f.CheckCode == CourseDesignChecks.OutcomeAssessmentMismatch);
        Assert.StartsWith("C1 asks students to design (Create), but its mapped assessments ask for no more than Understand", mismatch.Description);
        Assert.Contains(report.Limitations, l => l.StartsWith("1 outcome(s) could not be compared"));
        var older = CourseChecks.Run(Course([Module("Week 1: A", Page("Week 1 overview"), explain)], [new(Guid.NewGuid(), "C1", "Design a page", "", 1)]));
        Assert.Contains(older.Limitations, l => l.Contains("predates outcome and assessment level comparison"));
    }

    [Fact] public void Version_one_structure_snapshots_report_limitations_instead_of_passing()
    {
        var old = new CourseStructureEvidence(1, CourseStructureEvidence.KindName,
            [new(Guid.NewGuid(), "Week 1: A", "Published", 0, [new(Guid.NewGuid(), "page", "Week 1 overview", "Published", null, null, null, false)]),
             new(Guid.NewGuid(), "Week 2: B", "Published", 1, [new(Guid.NewGuid(), "page", "Week 2 overview", "Published", null, null, null, false)]),
             new(Guid.NewGuid(), "Week 3: C", "Published", 2, [new(Guid.NewGuid(), "page", "Week 3 overview", "Published", null, null, null, false)])], [], 1);
        var report = CourseChecks.Run(old);
        Assert.DoesNotContain(report.Findings, f => f.CheckCode is CourseDesignChecks.WorkloadImbalance or CourseDesignChecks.OutcomeVagueVerb or CourseDesignChecks.OutcomeNotAssessed);
        Assert.Contains(report.Limitations, l => l.Contains("predates workload estimates"));
        Assert.Contains(report.Limitations, l => l.Contains("predates outcome checks"));
    }
}

public class ContentCheckTests
{
    private const string SharedCourse = "30000000-0000-0000-0000-000000000001";

    [Fact] public async Task A_new_standard_set_version_brings_the_new_checks_to_new_reviews_and_previews()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(owner, "instructor");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/courses/{SharedCourse}/competencies",
            new { code = "C1", title = "Understand semantic HTML", description = "" })).StatusCode);

        // The seeded v1 lists only the original checks; a new draft adopts every check added since.
        var draft = (await (await ctl.PostAsync($"/api/v1/standard-sets/{DemoSeed.CollegeStandardSetId}/versions", null)).Content.ReadFromJsonAsync<StandardSetVersionDto>())!;
        Assert.Equal(CheckRegistry.All.Count, draft.Checks.Count);
        var preview = (await (await ctl.PostAsJsonAsync($"/api/v1/standard-sets/{draft.SetId}/versions/{draft.Id}/preview", new { courseId = SharedCourse }))
            .Content.ReadFromJsonAsync<StandardsPreviewDto>())!;
        var rows = preview.Rows.ToDictionary(r => r.Code);
        // WEB 101's only assignment (10 of 10 points) sits in its first module, so there is no low-stakes practice before it.
        foreach (var code in new[] { CourseDesignChecks.OutcomeVagueVerb, CourseDesignChecks.OutcomeNotAssessed, ContentChecks.InstructionsShort, CourseDesignChecks.EarlyPracticeMissing })
            Assert.Equal((code, 0, 1), (code, rows[code].Current, rows[code].Proposed));
        Assert.Equal(0, rows[CourseDesignChecks.OutcomeAssessmentMismatch].Proposed); // "Understand…" is not a higher-order outcome

        var v2 = (await (await ctl.PostAsJsonAsync($"/api/v1/standard-sets/{draft.SetId}/versions/{draft.Id}/publish", new { revision = draft.Revision }))
            .Content.ReadFromJsonAsync<StandardSetVersionDto>())!;
        var assignment = await ReviewTests.ElementId(ctl, SharedCourse, "assignment");
        var review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/elements/{assignment}/reviews", new { rubricVersionId = DemoSeed.RubricVersionId }));
        review = await ReviewTests.Ok(await ctl.PostAsJsonAsync($"/api/v1/reviews/{review.Id}/audit", new { revision = review.Revision }));
        Assert.Equal(v2.VersionNumber, review.Standards!.VersionNumber);
        var shortInstructions = Assert.Single(review.Findings, x => x.CheckCode == ContentChecks.InstructionsShort);
        Assert.Contains("words of instructions", shortInstructions.Evidence);
        Assert.Equal(DefaultGuidance.For(ContentChecks.InstructionsShort).Why, review.Guidance![ContentChecks.InstructionsShort].Why);
        // Text checks ran, so no "text was not available" limitation is reported.
        Assert.DoesNotContain(review.CheckLimitations, l => l.Contains("text was not available"));
    }
}
