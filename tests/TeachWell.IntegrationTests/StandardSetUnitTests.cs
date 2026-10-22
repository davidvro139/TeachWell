using TeachWell.Domain;
using Xunit;

public class StandardSetUnitTests
{
    [Fact] public void Selection_prefers_department_and_mode_then_department_then_mode_then_college_wide()
    {
        var sets = new (string Name, string? Mode, string? Department)[] { ("college", null, null), ("online", "Online", null), ("computing", null, "Computing"), ("both", "Online", "Computing") };
        string? Pick(string? mode, string? department) => StandardSetSelection.Pick(sets.Select(s => s.Name), n => { var s = sets.Single(x => x.Name == n); return (s.Mode, s.Department); }, mode, department);
        Assert.Equal("both", Pick("Online", " COMPUTING "));
        Assert.Equal("computing", Pick("Hybrid", "Computing"));
        Assert.Equal("online", Pick("Online", null));
        Assert.Equal("college", Pick(null, "Design"));
        Assert.Null(StandardSetSelection.Pick(sets.Skip(1).Select(s => s.Name), n => { var s = sets.Single(x => x.Name == n); return (s.Mode, s.Department); }, "Hybrid", "Design"));
    }

    [Fact] public void Scope_keys_ignore_department_case_and_whitespace()
    {
        Assert.Equal(StandardSetVocabulary.ScopeKey("Online", "Computing"), StandardSetVocabulary.ScopeKey("Online", "  computing "));
        Assert.NotEqual(StandardSetVocabulary.ScopeKey(null, "Computing"), StandardSetVocabulary.ScopeKey("Online", "Computing"));
        Assert.Equal("College-wide", StandardSetVocabulary.DescribeScope(null, " "));
        Assert.Equal("Design, Face-to-face", StandardSetVocabulary.DescribeScope("FaceToFace", "Design"));
    }

    [Fact] public void Checks_a_published_version_does_not_mention_do_not_run_under_it_but_new_drafts_gain_them()
    {
        var older = StandardSetContent.Defaults().Where(c => c.Code != DeterministicChecks.LinkVague).ToList();
        var profile = StandardSetContent.ToProfile(older);
        Assert.False(profile.For(CheckRegistry.Find(DeterministicChecks.LinkVague)!).Enabled);
        var evidence = new ContentEvidence(1, [], [], [new(0, "click here", "Internal")], []);
        Assert.Empty(DeterministicChecks.Run("page", "Published", evidence, profile).Findings);
        Assert.Single(DeterministicChecks.Run("page", "Published", evidence).Findings);
        Assert.Contains(StandardSetContent.WithNewChecks(older), c => c.Code == DeterministicChecks.LinkVague && c.Enabled);
    }

    [Fact] public void Stored_settings_round_trip_through_json()
    {
        var defaults = StandardSetContent.Defaults();
        var parsed = StandardSetContent.Parse(StandardSetContent.Serialize(defaults));
        Assert.Equal(defaults.Select(c => (c.Code, c.Enabled, c.Severity, c.Required, c.Guidance)), parsed.Select(c => (c.Code, c.Enabled, c.Severity, c.Required, c.Guidance)));
        Assert.Equal(defaults.Single(c => c.Code == DeterministicChecks.LinkVague).Lists[DeterministicChecks.VaguePhrases],
            parsed.Single(c => c.Code == DeterministicChecks.LinkVague).Lists[DeterministicChecks.VaguePhrases]);
        Assert.Empty(StandardSetContent.Validate(parsed));
    }
}
