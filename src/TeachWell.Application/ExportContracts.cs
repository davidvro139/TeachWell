namespace TeachWell.Application;

public record CourseExportImportDto(Guid CourseId, string Title, string Code, int ElementCount, bool Created, IReadOnlyList<string> Warnings);

// Imports an uploaded Canvas course export (IMS Common Cartridge). Without a courseId it creates, or refreshes, the caller's own
// course with the same export identifier; with one, it refreshes that course (owner, or a CTL reviewer it is shared with).
public interface ICourseExportService
{
    Task<ServiceResult<CourseExportImportDto>> ImportAsync(CurrentUser user, Stream export, Guid? courseId, string? canvasCourseUrl, CancellationToken ct);
}
