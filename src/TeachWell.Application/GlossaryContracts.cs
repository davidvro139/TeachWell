namespace TeachWell.Application;

public record GlossaryLinkDto(string Label, string Source, string Url);
public record GlossaryEntryDto(string TermId, string Term, string Definition, string[] Phrases, string[] Tags, GlossaryLinkDto[] Links, bool Hidden,
    DateTime UpdatedAt, string UpdatedByName, Guid Revision);
public record GlossaryLinkInput(string? Label, string? Source, string? Url);
/// <summary>Revision is the entry's current revision when changing an existing entry, and null when creating one.</summary>
public record GlossaryEntryInput(string? Term, string? Definition, string[]? Phrases, string[]? Tags, GlossaryLinkInput[]? Links, bool Hidden, Guid? Revision);

public interface IGlossaryService
{
    Task<IReadOnlyList<GlossaryEntryDto>> ListAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<GlossaryEntryDto>> SaveAsync(CurrentUser user, string termId, GlossaryEntryInput input, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<GlossaryEntryDto>>> ResetAsync(CurrentUser user, string termId, CancellationToken ct);
}
