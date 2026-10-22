using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;
using Xunit;

public class ReviewUnitTests
{
    // These tests cover the checks that predate ADR 011 slice 3, so they run under exactly those checks.
    private static readonly CheckProfile Initial = StandardSetContent.ToProfile(StandardSetContent.InitialDefaults());
    private static RubricCriterion Criterion(string category, int weight = 1, bool required = false) =>
        new() { Id = Guid.NewGuid(), Category = category, Weight = weight, Required = required };
    private static CriterionEvaluation Eval(RubricCriterion c, string outcome, int? rating = null) => new() { CriterionId = c.Id, Outcome = outcome, Rating = rating };

    [Fact] public void Score_excludes_not_applicable_and_never_treats_not_reviewed_as_zero()
    {
        var a = Criterion("Accessibility and UDL", 2, required: true); var b = Criterion("Accessibility and UDL"); var c = Criterion("Technical quality", required: true); var d = Criterion("Technical quality");
        var score = ReviewScoring.Compute([a, b, c, d], [Eval(a, ReviewVocabulary.Rated, 3), Eval(b, ReviewVocabulary.Rated, 0), Eval(c, ReviewVocabulary.NotReviewed), Eval(d, ReviewVocabulary.NotApplicable)]);
        // (3*2 + 0*1) / (3*2 + 3*1) = 66.7%; c and d do not enter the denominator.
        Assert.Equal(66.7m, score.Percent);
        Assert.Equal((4, 2, 1, 1), (score.Applicable, score.Rated, score.NotApplicable, score.NotReviewed));
        Assert.Equal(75m, score.CoveragePercent);
        Assert.Equal(1, score.RequiredNotReviewed);
        Assert.Equal(0, score.RequiredBelowExpectations);
        var technical = score.Categories.Single(x => x.Category == "Technical quality");
        Assert.Null(technical.Percent);
        Assert.Equal("Accessibility and UDL", score.Categories[0].Category);
    }
    [Fact] public void Unrated_review_has_no_score_and_zero_coverage()
    {
        var a = Criterion("Assessment and feedback", required: true);
        var score = ReviewScoring.Compute([a], []);
        Assert.Null(score.Percent); Assert.Equal(0m, score.CoveragePercent); Assert.Equal(1, score.NotReviewed); Assert.Equal(1, score.RequiredNotReviewed);
        var low = ReviewScoring.Compute([a], [Eval(a, ReviewVocabulary.Rated, 1)]);
        Assert.Equal(1, low.RequiredBelowExpectations); Assert.Equal(33.3m, low.Percent);
    }
    [Fact] public void Evidence_extraction_distinguishes_missing_and_empty_alt_and_keeps_no_urls_or_scripts()
    {
        var (text, evidence) = CanvasText.Normalize(
            "<h2>Intro</h2><script>alert(1)</script><img src='a.png'><img src='b.png' alt=''><img src='c.png' alt='Chart of results' role='presentation'>" +
            "<h4></h4><a href='https://other.example.org/path?verifier=secret'>click here</a><a href='/courses/1/pages/x'>Week 1 overview</a><a href='#top'><img alt='Back to top'></a>" +
            "<a href='javascript:alert(1)'>bad</a><table><tr><td>A</td></tr><tr><td>B</td></tr></table>",
            new Uri("https://canvas.example.test/"));
        Assert.DoesNotContain("alert", text);
        Assert.Equal([null, "", "Chart of results", "Back to top"], evidence.Images.Select(i => i.Alt));
        Assert.True(evidence.Images[2].Decorative);
        Assert.Equal([(2, "Intro"), (4, "")], evidence.Headings.Select(h => (h.Level, h.Text)));
        Assert.Equal(["External", "Internal", "Anchor", "Invalid"], evidence.Links.Select(l => l.Destination));
        Assert.Equal("other.example.org", evidence.Links[0].Host);
        Assert.Equal("Back to top", evidence.Links[2].Text);
        Assert.DoesNotContain("secret", evidence.Serialize());
        Assert.False(Assert.Single(evidence.Tables).HasHeaderCells);
    }
    [Fact] public void Checks_are_deterministic_and_cover_detectable_issues()
    {
        var evidence = new ContentEvidence(1,
            [new(0, null, false), new(1, "IMG_2041.JPG", false), new(2, "", false), new(3, null, true), new(4, "A labelled diagram", false)],
            [new(0, 2, "Intro"), new(1, 4, "Details"), new(2, 3, " ")],
            [new(0, "Click here.", "External", "example.org"), new(1, "", "Internal"), new(2, "https://example.org/a", "External", "example.org"), new(3, "Project rubric", "Internal")],
            [new(0, false, 3), new(1, true, 3), new(2, false, 1)],
            new(false, 10, true));
        var first = DeterministicChecks.Run("assignment", "Unpublished", evidence, Initial);
        var second = DeterministicChecks.Run("assignment", "Unpublished", evidence, Initial);
        Assert.Equal(first.Findings, second.Findings);
        Assert.Equal(
            ["ITEM_UNPUBLISHED", "IMG_ALT_MISSING:0", "IMG_ALT_FILENAME:1", "IMG_ALT_EMPTY:2", "HEADING_SKIPPED_LEVEL:1", "HEADING_EMPTY:2", "LINK_VAGUE:0", "LINK_EMPTY:1", "LINK_VAGUE:2", "TABLE_HEADERS_MISSING:0", "ASSIGNMENT_RUBRIC_MISSING"],
            first.Findings.Select(f => f.Fingerprint));
        Assert.All(first.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.Evidence)));
        Assert.Empty(first.Limitations);
    }
    [Fact] public void Checks_report_limitations_instead_of_passing_without_evidence()
    {
        var legacy = DeterministicChecks.Run("page", "Published", null, Initial);
        Assert.Empty(legacy.Findings);
        Assert.Contains(legacy.Limitations, l => l.Contains("no structural evidence"));
        var unknown = DeterministicChecks.Run("assignment", "Unknown", new ContentEvidence(1, [], [], [], [], null, Truncated: true), Initial);
        Assert.Equal(3, unknown.Limitations.Count);
        Assert.Equal("ITEM_UNAVAILABLE", Assert.Single(DeterministicChecks.Run("page", "Removed", null, Initial).Findings).CheckCode);
    }
    [Fact] public void Snapshot_hash_is_stable_for_legacy_content_and_changes_with_evidence()
    {
        var element = new CourseElement { Id = Guid.NewGuid(), Type = "page", Title = "T", PlainText = "Body", SourceStatus = "Published" };
        var legacy = ElementSnapshot.Capture(element, DateTime.UtcNow);
        Assert.Equal(legacy.ContentHash, ElementSnapshot.Capture(element, DateTime.UtcNow.AddMinutes(1)).ContentHash);
        element.EvidenceJson = new ContentEvidence(1, [new(0, null, false)], [], [], []).Serialize();
        var withEvidence = ElementSnapshot.Capture(element, DateTime.UtcNow);
        Assert.NotEqual(legacy.ContentHash, withEvidence.ContentHash);
        Assert.Equal(element.EvidenceJson, withEvidence.EvidenceJson);
    }
}
