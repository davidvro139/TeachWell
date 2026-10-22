using System.Text.RegularExpressions;

namespace TeachWell.Application;

public record CompetencyMappingDto(Guid ElementId, string ElementType, string ElementTitle);
public record CompetencyDto(Guid Id, string Code, string Title, string Description, Guid Revision, IReadOnlyList<CompetencyMappingDto> Mappings);
public record CreateCompetencyInput(string? Code, string? Title, string? Description);
public record UpdateCompetencyInput(Guid Revision, string? Title, string? Description);
// Pasted outcomes, proposed as competencies for the owner to edit and approve. Nothing is saved by a proposal.
public record OutcomePasteInput(string? Text);
public record ProposedCompetencyDto(string Code, string Title, bool Include, string? BloomLevel, string? VaguePhrase, string? ExistingCode, IReadOnlyList<string> Notes);
public record OutcomeProposalDto(IReadOnlyList<ProposedCompetencyDto> Outcomes, IReadOnlyList<string> Ignored);
public record BulkCompetencyInput(CreateCompetencyInput[]? Items);
// One cell of the mapping grid: map (true) or unmap (false) an assessment to a competency.
public record MappingChange(Guid CompetencyId, Guid ElementId, bool Mapped);
public record MappingChangesInput(MappingChange[]? Changes);

public interface ICompetencyService
{
    Task<ServiceResult<IReadOnlyList<CompetencyDto>>> ListAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<ServiceResult<CompetencyDto>> CreateAsync(CurrentUser user, Guid courseId, CreateCompetencyInput input, CancellationToken ct);
    Task<ServiceResult<CompetencyDto>> UpdateAsync(CurrentUser user, Guid courseId, Guid competencyId, UpdateCompetencyInput input, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteAsync(CurrentUser user, Guid courseId, Guid competencyId, CancellationToken ct);
    // Mapping and unmapping are idempotent, like course-share grant/revoke: no concurrency token is needed for a simple relationship toggle.
    Task<ServiceResult<CompetencyDto>> MapAsync(CurrentUser user, Guid courseId, Guid competencyId, Guid elementId, CancellationToken ct);
    Task<ServiceResult<CompetencyDto>> UnmapAsync(CurrentUser user, Guid courseId, Guid competencyId, Guid elementId, CancellationToken ct);
    Task<ServiceResult<OutcomeProposalDto>> ProposeAsync(CurrentUser user, Guid courseId, OutcomePasteInput input, CancellationToken ct);
    // All or nothing: every item is validated (and codes checked against each other and the course) before any is saved.
    Task<ServiceResult<IReadOnlyList<CompetencyDto>>> CreateManyAsync(CurrentUser user, Guid courseId, BulkCompetencyInput input, CancellationToken ct);
    // Applies many map/unmap changes at once (the mapping grid). All are validated first; then all apply, idempotently, or none do.
    Task<ServiceResult<IReadOnlyList<CompetencyDto>>> ApplyMappingsAsync(CurrentUser user, Guid courseId, MappingChangesInput input, CancellationToken ct);
}

public static partial class AlignmentValidation
{
    public static Dictionary<string, string[]> Create(string? code, string? title, string? description)
    {
        var errors = new Dictionary<string, string[]>();
        if (code is null || !CodePattern().IsMatch(code)) errors["code"] = ["Use 1–20 uppercase letters, digits or hyphens."];
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) errors["title"] = ["Enter a title of 1–200 characters."];
        if ((description?.Length ?? 0) > 2000) errors["description"] = ["Description is limited to 2,000 characters."];
        return errors;
    }
    public static Dictionary<string, string[]> Update(string? title, string? description)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) errors["title"] = ["Enter a title of 1–200 characters."];
        if ((description?.Length ?? 0) > 2000) errors["description"] = ["Description is limited to 2,000 characters."];
        return errors;
    }
    [GeneratedRegex("^[A-Z0-9-]{1,20}$")] private static partial Regex CodePattern();
}
