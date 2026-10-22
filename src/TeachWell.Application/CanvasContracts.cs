namespace TeachWell.Application;

public enum CanvasError { None, InvalidToken, PermissionDenied, Unavailable, Timeout, RateLimited, UnexpectedResponse, RedirectBlocked, TokenUnreadable, Expired, Disconnected, ResourceNotFound, LimitExceeded, CourseNotAuthorized }
public record CanvasValidation(string? CanvasUserId, CanvasError Error);
public interface ICanvasClient
{
    Task<CanvasValidation> ValidateConnectionAsync(Uri origin, string token, CancellationToken ct);
    Task<CanvasCoursesResult> ListCoursesAsync(Uri origin, string token, CancellationToken ct);
    Task<CanvasReadResult> ReadCourseAsync(Uri origin, string token, string courseId, CancellationToken ct);
}
public record CanvasCourseOption(string Id, string Title, string Code);
public record CanvasCoursesResult(IReadOnlyList<CanvasCourseOption> Courses, CanvasError Error = CanvasError.None);
public record CanvasElementData(string Id, string Type, string Title, string PlainText, string? SourceUrl,
    string Status, int Position = 0, string? ParentModuleId = null, string? TargetType = null, string? TargetId = null, string? Warning = null,
    TeachWell.Domain.ContentEvidence? Evidence = null, string? PreviewHtml = null);
public record CanvasCourseData(CanvasCourseOption Course, IReadOnlyList<CanvasElementData> Elements, IReadOnlyList<string> Warnings);
public record CanvasReadResult(CanvasCourseData? Data, CanvasError Error = CanvasError.None);
public record CanvasImportResult(Guid? CourseId = null, int ElementCount = 0, IReadOnlyList<string>? Warnings = null,
    CanvasError Error = CanvasError.None, bool NotFound = false);
public interface ICanvasImportService
{
    Task<(CanvasCoursesResult Result, bool NotFound)> ListCoursesAsync(CurrentUser user, Guid connectionId, CancellationToken ct);
    Task<CanvasImportResult> ImportAsync(CurrentUser user, Guid connectionId, string sourceCourseId, CancellationToken ct);
}
public record CanvasConnectionDto(Guid Id, string BaseUrl, string Status, string? CanvasUserId, string? TokenMask,
    DateTime? ExpiresAt, DateTime? LastValidatedAt, DateTime? LastCheckedAt, string? ErrorCode);
public record CanvasConnectionResult(CanvasConnectionDto? Connection, CanvasError Error = CanvasError.None, bool NotFound = false);
public interface ICanvasTokenProtector
{
    string Protect(string token, Guid tenantId, Guid ownerId, Guid connectionId);
    string Unprotect(string ciphertext, Guid tenantId, Guid ownerId, Guid connectionId);
}
public interface ICanvasConnectionService
{
    Task<IReadOnlyList<CanvasConnectionDto>> ListAsync(CurrentUser user, CancellationToken ct);
    Task<CanvasConnectionResult> ConnectAsync(CurrentUser user, Uri origin, string token, DateTime? expiresAt, CancellationToken ct);
    Task<CanvasConnectionResult> ValidateAsync(CurrentUser user, Guid id, CancellationToken ct);
    Task<bool> DisconnectAsync(CurrentUser user, Guid id, CancellationToken ct);
}
