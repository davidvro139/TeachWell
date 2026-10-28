using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reports;

// The accessibility conformance report (ROADMAP item 1).
//
// This service deliberately holds no analysis of its own. It asks IGuideService for the course's guide — which applies
// the course's standard set, enforces course access and records the oversight figures — then projects that guide onto
// WCAG success criteria. Any disagreement between the guide and this report would be a bug in the projection, not two
// opinions about the course.
public sealed class ConformanceService(AppDbContext db, IGuideService guide) : IConformanceService
{
    public async Task<ServiceResult<CourseConformanceDto>> GetAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        // The guide performs the access check: an unshared course is NotFound there and stays NotFound here.
        var result = await guide.GetAsync(user, courseId, ct);
        if (result is not { Status: ResultStatus.Ok, Value: { } g })
            return new ServiceResult<CourseConformanceDto>(result.Status, Errors: result.Errors, Message: result.Message);

        // A check appears as a step when it found something, and as a strength when it ran against content it applies
        // to and found nothing. A check in neither did not run here: it is disabled, or the course has nothing it
        // applies to, or the snapshot lacked the evidence it needs. All three mean "not checked", never "passed".
        var outcomes = g.Steps
            .Select(s => new CheckOutcome(s.CheckCode, true,
                s.Locations.Count(l => l.Status == GuideVocabulary.Open),
                s.Locations.Count(l => l.Status == GuideVocabulary.Done),
                s.Locations.Count(l => l.Status == GuideVocabulary.NotApplicable)))
            .Concat(g.Strengths.Select(s => new CheckOutcome(s.CheckCode, true)))
            .ToList();

        var states = Conformance.Evaluate(outcomes);
        var criteria = states.Select(s => new ConformanceCriterionDto(s.Criterion.Number, s.Criterion.Name, s.Criterion.Level, s.Criterion.Reference,
            s.State, s.Checks, s.ChecksRan, s.FullyCovered, s.OpenPlaces, s.DonePlaces, s.NotApplicablePlaces)).ToList();

        var course = await db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => new { c.Code }).SingleAsync(ct);
        var endorsement = await db.Endorsements.AsNoTracking().Where(e => e.CourseId == courseId)
            .OrderByDescending(e => e.EndorsedAt).FirstOrDefaultAsync(ct);

        var limitations = g.Limitations.ToList();
        if (states.Any(s => s.State == Conformance.NotChecked))
            limitations.Add("Some criteria have no check that ran on this course, either because the applied standards do not enable it or because the course has no content it applies to. Those criteria are reported as not checked and need a manual review.");
        if (states.Any(s => s is { State: not Conformance.NotChecked, FullyCovered: false }))
            limitations.Add("Some criteria were only partly covered: not every check related to them ran on this course.");

        return ServiceResult<CourseConformanceDto>.Ok(new CourseConformanceDto(courseId, g.CourseTitle, course.Code, Wcag.Version,
            DateTime.UtcNow, g.ContentAsOf, g.Standards, g.CheckedItems,
            Wcag.Criteria.Count, Conformance.OutOfScope,
            states.Count(s => s.State == Conformance.Findings), states.Count(s => s.State == Conformance.NoFindings), states.Count(s => s.State == Conformance.NotChecked),
            states.Sum(s => s.OpenPlaces), states.Sum(s => s.DonePlaces), states.Sum(s => s.NotApplicablePlaces),
            criteria, endorsement?.Wording, endorsement?.EndorsedAt, Conformance.Caveat, limitations));
    }
}
