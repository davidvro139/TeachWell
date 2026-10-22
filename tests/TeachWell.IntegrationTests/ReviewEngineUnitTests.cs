using TeachWell.Domain;
using Xunit;

public class ReviewEngineUnitTests
{
    private static CheckSettings With(string code, Func<CheckSettings, CheckSettings> change) => change(CheckRegistry.Find(code)!.Defaults());
    // Overrides on top of the checks that predate ADR 011 slice 3, so later checks do not add findings to these scenarios.
    private static CheckProfile Profile(params (string Code, CheckSettings Settings)[] settings)
    {
        var all = StandardSetContent.InitialDefaults().ToDictionary(c => c.Code, c => c.ToSettings());
        foreach (var check in CheckRegistry.All.Where(c => !all.ContainsKey(c.Code))) all[check.Code] = check.Defaults() with { Enabled = false };
        foreach (var (code, value) in settings) all[code] = value;
        return new(all);
    }
    private static ContentEvidence Links(params string[] texts) => new(1, [], [], texts.Select((t, i) => new LinkEvidence(i, t, "Internal")).ToList(), []);
    private static CourseStructureEvidence Course(params string[] moduleTitles) => new(CourseStructureEvidence.CurrentVersion, CourseStructureEvidence.KindName,
        moduleTitles.Select((t, i) => new ModuleEvidence(Guid.NewGuid(), t, "Published", i, [new CourseItemEvidence(Guid.NewGuid(), "page", "Page " + i, "Published", null, null, null, false)])).ToList(), [], 1);

    [Fact] public void Registry_declares_every_check_once_with_a_known_aspect_severity_and_valid_defaults()
    {
        Assert.Equal(CheckRegistry.All.Count, CheckRegistry.All.Select(c => c.Code).Distinct().Count());
        Assert.Equal(DeterministicChecks.All.Order(), CheckRegistry.All.Select(c => c.Code).Order());
        Assert.All(CheckRegistry.All, c =>
        {
            Assert.Contains(c.Aspect, ReviewAspects.All);
            Assert.Contains(c.DefaultSeverity, ReviewVocabulary.Severities);
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Guidance.Why) || string.IsNullOrWhiteSpace(c.Guidance.HowToFix) || string.IsNullOrWhiteSpace(c.Guidance.Example));
        });
        Assert.Empty(StandardSetContent.Validate(StandardSetContent.Defaults()));
        Assert.Empty(CheckProfile.Validate(CheckRegistry.All.ToDictionary(c => c.Code, c => c.Defaults())));
    }

    [Fact] public void Default_profile_matches_running_without_a_profile()
    {
        var evidence = Links("click here", "", "Week 1 overview");
        Assert.Equal(DeterministicChecks.Run("page", "Published", evidence).Findings, DeterministicChecks.Run("page", "Published", evidence, CheckProfile.Default).Findings);
    }

    [Fact] public void Disabled_checks_do_not_run_and_severity_comes_from_settings()
    {
        var evidence = Links("click here", "");
        var profile = Profile((DeterministicChecks.LinkEmpty, With(DeterministicChecks.LinkEmpty, s => s with { Enabled = false })),
            (DeterministicChecks.LinkVague, With(DeterministicChecks.LinkVague, s => s with { Severity = "Critical" })));
        var finding = Assert.Single(DeterministicChecks.Run("page", "Published", evidence, profile).Findings);
        Assert.Equal((DeterministicChecks.LinkVague, "Critical"), (finding.CheckCode, finding.Severity));
    }

    [Fact] public void Word_list_parameters_change_what_is_detected()
    {
        var evidence = Links("Week 1 overview", "click here");
        var custom = With(DeterministicChecks.LinkVague, s => s with { Lists = new Dictionary<string, IReadOnlyList<string>> { [DeterministicChecks.VaguePhrases] = ["week 1 overview"] } });
        var finding = Assert.Single(DeterministicChecks.Run("page", "Published", evidence, Profile((DeterministicChecks.LinkVague, custom))).Findings);
        Assert.Equal("LINK_VAGUE:0", finding.Fingerprint);
    }

    [Fact] public void Course_keyword_and_prefix_settings_drive_start_and_naming_checks()
    {
        var course = Course("Orientación", "Semana 1: Datos", "Semana 2: Gráficos", "Week 3: Stories");
        Assert.Contains(CourseChecks.Run(course).Findings, f => f.CheckCode == CourseChecks.StartMissing);

        var start = With(CourseChecks.StartMissing, s => s with { Lists = new Dictionary<string, IReadOnlyList<string>> { [CourseChecks.StartKeywords] = ["orientación"] } });
        var prefixes = With(CourseChecks.ModuleNaming, s => s with { Lists = new Dictionary<string, IReadOnlyList<string>> { [CourseChecks.NamingPrefixes] = ["semana", "week"] } });
        var report = CourseChecks.Run(course, Profile((CourseChecks.StartMissing, start), (CourseChecks.ModuleNaming, prefixes)));
        var naming = Assert.Single(report.Findings);
        Assert.Equal(CourseChecks.ModuleNaming, naming.CheckCode);
        Assert.Contains("Week 3: Stories", naming.Description);
    }

    [Fact] public void Keywords_are_matched_literally_not_as_patterns()
    {
        var start = With(CourseChecks.StartMissing, s => s with { Lists = new Dictionary<string, IReadOnlyList<string>> { [CourseChecks.StartKeywords] = [".*"] } });
        Assert.Contains(CourseChecks.Run(Course("Week 1: Data"), Profile((CourseChecks.StartMissing, start))).Findings, f => f.CheckCode == CourseChecks.StartMissing);
    }

    [Fact] public void Validation_rejects_unknown_checks_settings_out_of_range_values_and_oversized_lists()
    {
        var vague = CheckRegistry.Find(DeterministicChecks.LinkVague)!.Defaults();
        var errors = CheckProfile.Validate(new Dictionary<string, CheckSettings>
        {
            ["NOT_A_CHECK"] = vague,
            [DeterministicChecks.LinkVague] = vague with
            {
                Severity = "Severe",
                Numbers = new Dictionary<string, decimal> { ["threshold"] = 3 },
                Lists = new Dictionary<string, IReadOnlyList<string>> { [DeterministicChecks.VaguePhrases] = Enumerable.Range(0, 101).Select(i => $"phrase {i}").ToList() }
            },
            [CourseChecks.StartMissing] = CheckRegistry.Find(CourseChecks.StartMissing)!.Defaults() with
                { Lists = new Dictionary<string, IReadOnlyList<string>> { [CourseChecks.StartKeywords] = ["ok", " ", new string('x', 61)] } },
            [CourseChecks.ModuleNaming] = CheckRegistry.Find(CourseChecks.ModuleNaming)!.Defaults() with { Lists = new Dictionary<string, IReadOnlyList<string>>() }
        });
        Assert.Equal(["Unknown check."], errors["checks.NOT_A_CHECK"]);
        var vagueErrors = errors[$"checks.{DeterministicChecks.LinkVague}"];
        Assert.Contains(vagueErrors, e => e.StartsWith("Severity must be"));
        Assert.Contains(vagueErrors, e => e.Contains("Unknown setting \"threshold\""));
        Assert.Contains(vagueErrors, e => e.Contains("at most 100"));
        Assert.Contains(errors[$"checks.{CourseChecks.StartMissing}"], e => e.Contains("1–60 characters"));
        Assert.Contains(errors[$"checks.{CourseChecks.ModuleNaming}"], e => e.EndsWith("is required."));
    }

    [Fact] public void Number_parameters_are_range_checked()
    {
        var check = new NumberCheck();
        var ok = check.Defaults();
        Assert.Equal(5m, ok.Number("max"));
        Assert.Empty(check.Validate(ok));
        Assert.Equal(["Maximum must be between 1 and 10."], check.Validate(ok with { Numbers = new Dictionary<string, decimal> { ["max"] = 0 } }));
        Assert.Equal(["Maximum is required."], check.Validate(ok with { Numbers = new Dictionary<string, decimal>() }));
    }

    // No registered check takes a number yet (thresholds arrive with the new analyzers), so a test-only check exercises the rule.
    private sealed class NumberCheck : ElementCheck
    {
        public override string Code => "TEST_NUMBER"; public override string Name => "Test"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Low"; public override int Stage => 0;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number("max", "Maximum", 5, 1, 10)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext context, CheckSettings settings) => [];
    }
}
