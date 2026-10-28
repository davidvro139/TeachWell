namespace TeachWell.Application;

// Pasting the syllabus (ADR 019 amendment). The college's syllabi live in Simple Syllabus, an LTI tool whose content is
// not in Canvas exports or the Canvas API (ADR 008), so the course owner pastes the text, exactly as they already paste
// learning outcomes. TeachWell then runs the syllabus checks against it.

// Source is "Canvas" for an imported syllabus, "Pasted" for one the owner supplied, or "None" when the course has
// neither. Words is what the checks actually measured, so a paste that arrived mangled is visible as a number.
public record CourseSyllabusDto(Guid CourseId, string Source, int Words, string? Text, DateTime? SuppliedAt,
    string? SuppliedByName, DateTime? ImportedAt, bool CanEdit, string? Note);

public record SyllabusTextInput(string? Text);

public interface ISyllabusService
{
    // Any viewer with course access reads it; only the owner pastes or clears (others get 404, as the guide does).
    Task<ServiceResult<CourseSyllabusDto>> GetAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<ServiceResult<CourseSyllabusDto>> SaveAsync(CurrentUser user, Guid courseId, SyllabusTextInput input, CancellationToken ct);
    // Clears a pasted syllabus and hands the element back to the importer.
    Task<ServiceResult<CourseSyllabusDto>> ClearAsync(CurrentUser user, Guid courseId, CancellationToken ct);
}
