using TeachWell.Domain;

namespace TeachWell.Application;

// The accessibility conformance report (ROADMAP item 1): a projection of a course's improvement guide onto the WCAG 2.1
// success criteria TeachWell's checks relate to, for the college's own documentation of its remediation work.
//
// It stores nothing and computes nothing new. Every figure comes from the guide the instructor already sees, so the
// report cannot contradict it. State is one of Conformance.Findings, NoFindings or NotChecked — never "compliant".

// Checks lists every check related to the criterion; ChecksRan says how many of them actually ran on this course. A
// criterion whose checks did not all run is weaker evidence, which FullyCovered makes explicit.
public record ConformanceCriterionDto(string Number, string Name, string Level, string Reference, string State,
    IReadOnlyList<string> Checks, int ChecksRan, bool FullyCovered, int OpenPlaces, int DonePlaces, int NotApplicablePlaces);

// AsOf is when the report was produced; ContentAsOf is when the course content it describes was captured. They differ,
// and both matter to a reader deciding how current the report is.
public record CourseConformanceDto(Guid CourseId, string CourseTitle, string CourseCode, string Standard,
    DateTime AsOf, DateTime? ContentAsOf, AppliedStandardsDto Standards, int CheckedItems,
    int CriteriaInScope, int CriteriaOutOfScope, int CriteriaWithFindings, int CriteriaWithNoFindings, int CriteriaNotChecked,
    int OpenPlaces, int DonePlaces, int NotApplicablePlaces,
    IReadOnlyList<ConformanceCriterionDto> Criteria, string? Endorsement, DateTime? EndorsedAt,
    string Caveat, IReadOnlyList<string> Limitations);

public interface IConformanceService
{
    // Requires course access, exactly as the guide does: this reads the guide and adds no new permission.
    Task<ServiceResult<CourseConformanceDto>> GetAsync(CurrentUser user, Guid courseId, CancellationToken ct);
}
