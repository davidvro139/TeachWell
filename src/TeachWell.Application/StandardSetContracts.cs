using TeachWell.Domain;

namespace TeachWell.Application;

public record CheckParameterDto(string Key, string Label, string Kind, decimal? DefaultNumber, IReadOnlyList<string>? DefaultWords, decimal? Min, decimal? Max, int MaxItems, int MaxItemLength);
public record CheckCatalogEntryDto(string Code, string Name, string Aspect, string Target, string DefaultSeverity, IReadOnlyList<CheckParameterDto> Parameters, CheckGuidance DefaultGuidance);

public record StandardSetVersionSummaryDto(Guid Id, int VersionNumber, string Status, DateTime CreatedAt, DateTime? PublishedAt, int EnabledChecks, int RequiredChecks);
public record StandardSetDto(Guid Id, string Name, string? DeliveryMode, string? Department, string Scope, IReadOnlyList<StandardSetVersionSummaryDto> Versions);
public record StandardSetVersionDto(Guid Id, Guid SetId, string SetName, string Scope, int VersionNumber, string Status, Guid Revision, string Notes,
    DateTime CreatedAt, DateTime? PublishedAt, string? PublishedByName, IReadOnlyList<StandardCheckSetting> Checks, EndorsementRequirements Endorsement);
public record CreateStandardSetInput(string? Name, string? DeliveryMode, string? Department);
// Endorsement is optional: null keeps the draft's current requirements.
public record UpdateStandardSetDraftInput(Guid Revision, string? Notes, StandardCheckSetting[]? Checks, EndorsementRequirements? Endorsement = null);

// Which standard set governs a course, and why. Null set means no published set matches and built-in defaults apply.
public record AppliedStandardsDto(Guid? SetId, Guid? VersionId, string Name, int? VersionNumber, string Scope, string Reason, DateTime? PublishedAt);
public record CourseStandardsDto(Guid CourseId, string? DeliveryMode, string? Department, bool CanClassify, AppliedStandardsDto Applied, string? Program = null);
// Program does not affect which standards apply; it is for oversight filtering. Blank clears it.
public record CourseClassificationInput(string? DeliveryMode, string? Department, string? Program = null);

// Dry run of a version against one course's current content, compared with the course's currently applied standards.
public record StandardsPreviewRowDto(string Code, string Name, string Aspect, bool Enabled, int Current, int Proposed);
public record StandardsPreviewDto(Guid CourseId, string CourseTitle, AppliedStandardsDto Current, int ElementsChecked, bool CourseStructureChecked,
    int CurrentTotal, int ProposedTotal, IReadOnlyList<StandardsPreviewRowDto> Rows, IReadOnlyList<string> Limitations);
public record StandardsPreviewInput(Guid CourseId);

public interface IStandardSetService
{
    IReadOnlyList<CheckCatalogEntryDto> Catalog();
    Task<IReadOnlyList<StandardSetDto>> ListAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<StandardSetVersionDto>> GetVersionAsync(CurrentUser user, Guid setId, Guid versionId, CancellationToken ct);
    Task<ServiceResult<StandardSetVersionDto>> CreateAsync(CurrentUser user, CreateStandardSetInput input, CancellationToken ct);
    Task<ServiceResult<StandardSetVersionDto>> CreateDraftAsync(CurrentUser user, Guid setId, CancellationToken ct);
    Task<ServiceResult<StandardSetVersionDto>> UpdateDraftAsync(CurrentUser user, Guid setId, Guid versionId, UpdateStandardSetDraftInput input, CancellationToken ct);
    Task<ServiceResult<StandardSetVersionDto>> PublishAsync(CurrentUser user, Guid setId, Guid versionId, Guid revision, CancellationToken ct);
    Task<ServiceResult<StandardsPreviewDto>> PreviewAsync(CurrentUser user, Guid setId, Guid versionId, Guid courseId, CancellationToken ct);
    Task<ServiceResult<CourseStandardsDto>> GetForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<ServiceResult<CourseStandardsDto>> ClassifyCourseAsync(CurrentUser user, Guid courseId, CourseClassificationInput input, CancellationToken ct);
}
