namespace TeachWell.Domain;

// Projects a course's improvement guide onto WCAG success criteria (ROADMAP item 1).
//
// This is a pure projection of what the guide already computed, not a second analysis. A criterion's state is derived
// from the same places the instructor sees on the guide, so the two can never disagree, and no new data is stored.
//
// What this is not: a conformance claim. It reports what TeachWell's automated checks found about the criteria they
// relate to, on the content captured at the stated date. Several criteria have no automated check at all, and no
// automated check can establish that a criterion is satisfied — only that nothing it looks for was found. The report
// states this every time it is rendered (Conformance.Caveat).

// What one check contributed to the guide. Ran is false when the applied standard set does not enable the check, or
// when the snapshot lacked the evidence it needs: either way the criterion is under-covered, not satisfied.
public sealed record CheckOutcome(string Code, bool Ran, int OpenPlaces = 0, int DonePlaces = 0, int NotApplicablePlaces = 0);

// One success criterion's state. Checks lists every check TeachWell relates to this criterion; ChecksRan says how many
// of them actually ran, so a "Met" row with ChecksRan below Checks.Count is weaker evidence than one where they match.
public sealed record CriterionState(WcagCriterion Criterion, IReadOnlyList<string> Checks, int ChecksRan,
    int OpenPlaces, int DonePlaces, int NotApplicablePlaces, string State)
{
    public bool FullyCovered => ChecksRan == Checks.Count;
}

public static class Conformance
{
    // NoFindings deliberately avoids the word "met": the checks that ran found nothing, which is not the same as
    // the criterion being satisfied. NotChecked means no related check ran, so the criterion says nothing either way.
    public const string NoFindings = "NoFindings", Findings = "Findings", NotChecked = "NotChecked";

    public const string Caveat =
        "This report summarises what TeachWell's automated checks found about the WCAG 2.1 success criteria they relate to, " +
        "on the course content captured at the date shown. It is not a conformance claim or an accessibility certification. " +
        "Automated checks can show that an issue was found; they cannot show that a criterion is satisfied. Criteria with no " +
        "automated check are listed as out of scope and need a manual review.";

    // In criterion order. Every criterion TeachWell maps a check to appears, including ones whose checks did not run.
    public static IReadOnlyList<CriterionState> Evaluate(IEnumerable<CheckOutcome> outcomes)
    {
        var byCode = outcomes.GroupBy(o => o.Code).ToDictionary(g => g.Key, g => g.First());
        return [.. Wcag.Criteria.Select(criterion =>
        {
            var checks = Wcag.Checks(criterion);
            var ran = checks.Select(byCode.GetValueOrDefault).OfType<CheckOutcome>().Where(o => o.Ran).ToList();
            var open = ran.Sum(o => o.OpenPlaces);
            return new CriterionState(criterion, checks, ran.Count, open, ran.Sum(o => o.DonePlaces), ran.Sum(o => o.NotApplicablePlaces),
                ran.Count == 0 ? NotChecked : open > 0 ? Findings : NoFindings);
        })];
    }

    // WCAG 2.1 has 50 success criteria at Levels A and AA combined (30 at A, 20 at AA). Stated in the report so a
    // reader cannot mistake the criteria listed for the whole of the standard.
    public const int ReferenceCriteria = 50;
    public static int OutOfScope => ReferenceCriteria - Wcag.Criteria.Count;
}
