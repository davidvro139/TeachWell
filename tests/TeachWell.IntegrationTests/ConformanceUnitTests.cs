using TeachWell.Domain;
using Xunit;

// The WCAG criterion mapping and the conformance projection (ROADMAP item 1). Pure functions, no database.
public class ConformanceUnitTests
{
    [Fact] public void Every_accessibility_check_maps_to_at_least_one_criterion_and_no_other_check_claims_one()
    {
        // The guard that stops a new accessibility check silently dropping out of the conformance report.
        Assert.All(CheckRegistry.All.Where(c => c.Aspect == ReviewAspects.Accessibility), c =>
            Assert.NotEmpty(c.WcagCriteria));
        // Course design, workload, pacing and alignment relate to no success criterion, and must not pretend to.
        Assert.All(CheckRegistry.All.Where(c => c.Aspect != ReviewAspects.Accessibility), c =>
            Assert.Empty(c.WcagCriteria));
    }

    [Fact] public void Mapped_criteria_are_all_in_the_catalogue_and_every_catalogued_criterion_has_a_check()
    {
        Assert.All(CheckRegistry.All.SelectMany(c => c.WcagCriteria), c => Assert.Same(c, Wcag.Find(c.Number)));
        Assert.All(Wcag.Criteria, c => Assert.NotEmpty(Wcag.Checks(c)));
        Assert.All(Wcag.Criteria, c => Assert.Contains(c.Level, new[] { "A", "AA" }));
        // Criteria are listed in numeric order, which is the order the report renders them in.
        Assert.Equal(Wcag.Criteria.Select(c => c.Number).Order(StringComparer.Ordinal), Wcag.Criteria.Select(c => c.Number));
    }

    [Fact] public void A_check_with_no_mapping_returns_an_empty_list_rather_than_throwing()
    {
        Assert.Empty(Wcag.For(CourseDesignChecks.WorkloadImbalance));
        Assert.Empty(Wcag.For("NO_SUCH_CHECK"));
        Assert.Null(Wcag.Find("9.9.9"));
    }

    [Fact] public void A_criterion_whose_checks_did_not_run_is_not_checked_rather_than_satisfied()
    {
        // Nothing ran at all: every criterion is NotChecked, and none is reported as clean.
        var states = Conformance.Evaluate([]);
        Assert.Equal(Wcag.Criteria.Count, states.Count);
        Assert.All(states, s => Assert.Equal(Conformance.NotChecked, s.State));
        Assert.All(states, s => Assert.Equal(0, s.ChecksRan));
        Assert.DoesNotContain(Conformance.NoFindings, states.Select(s => s.State));

        // A check present but not run counts the same as one absent.
        var notRun = Conformance.Evaluate([new CheckOutcome(DeterministicChecks.ImageAltMissing, false, OpenPlaces: 3)]);
        var alt = notRun.Single(s => s.Criterion == Wcag.NonTextContent);
        Assert.Equal(Conformance.NotChecked, alt.State);
        Assert.Equal(0, alt.OpenPlaces);
    }

    [Fact] public void Open_places_make_a_criterion_report_findings_and_fixing_them_clears_it()
    {
        var criterion = Wcag.LinkPurpose;
        var checks = Wcag.Checks(criterion);
        Assert.Equal([DeterministicChecks.LinkEmpty, DeterministicChecks.LinkVague], checks.Order(StringComparer.Ordinal));

        var withFindings = Conformance.Evaluate([
            new CheckOutcome(DeterministicChecks.LinkEmpty, true, OpenPlaces: 2, DonePlaces: 1),
            new CheckOutcome(DeterministicChecks.LinkVague, true)]);
        var row = withFindings.Single(s => s.Criterion == criterion);
        Assert.Equal(Conformance.Findings, row.State);
        Assert.Equal(2, row.OpenPlaces);
        Assert.Equal(1, row.DonePlaces);
        Assert.Equal(2, row.ChecksRan);
        Assert.True(row.FullyCovered);

        // Marking the remaining places Done leaves no open places, so the criterion reports no findings.
        var cleared = Conformance.Evaluate([
            new CheckOutcome(DeterministicChecks.LinkEmpty, true, DonePlaces: 3),
            new CheckOutcome(DeterministicChecks.LinkVague, true)]);
        Assert.Equal(Conformance.NoFindings, cleared.Single(s => s.Criterion == criterion).State);
    }

    [Fact] public void Not_applicable_places_do_not_count_as_findings_but_are_reported_separately()
    {
        var states = Conformance.Evaluate([new CheckOutcome(ContentChecks.ColorOnly, true, NotApplicablePlaces: 4)]);
        var row = states.Single(s => s.Criterion == Wcag.UseOfColor);
        Assert.Equal(Conformance.NoFindings, row.State);
        Assert.Equal(0, row.OpenPlaces);
        Assert.Equal(4, row.NotApplicablePlaces);
    }

    [Fact] public void A_partly_covered_criterion_says_so_even_when_the_checks_that_ran_found_nothing()
    {
        // 1.3.1 relates to four checks; only one of them ran here.
        var criterion = Wcag.InfoAndRelationships;
        Assert.Equal(4, Wcag.Checks(criterion).Count);
        var row = Conformance.Evaluate([new CheckOutcome(DeterministicChecks.TableHeaders, true)]).Single(s => s.Criterion == criterion);
        Assert.Equal(Conformance.NoFindings, row.State);
        Assert.Equal(1, row.ChecksRan);
        Assert.False(row.FullyCovered);
    }

    [Fact] public void The_report_states_how_much_of_the_standard_it_does_not_cover()
    {
        // The scope statement must never imply the mapped criteria are the whole standard.
        Assert.Equal(Conformance.ReferenceCriteria - Wcag.Criteria.Count, Conformance.OutOfScope);
        Assert.True(Conformance.OutOfScope > 0);
        Assert.Contains("not a conformance claim", Conformance.Caveat);
        Assert.DoesNotContain("compliant", Conformance.Caveat);
    }
}
