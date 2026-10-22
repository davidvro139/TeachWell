using TeachWell.Domain;

namespace TeachWell.Application;

// The instructor improvement guide (ADR 011 slices 4–5): TeachWell's own checks, run on demand against the latest imported content,
// turned into ranked steps with guidance. It needs no reviewer; a CTL reviewer with course access sees the same guide.
public record CourseGuideDto(Guid CourseId, string CourseTitle, AppliedStandardsDto Standards, DateTime? ContentAsOf, bool CanMark, int CheckedItems,
    IReadOnlyList<GuideAspectDto> Aspects, IReadOnlyList<GuideStepDto> Steps, IReadOnlyList<GuideStrengthDto> Strengths, int ResolvedSinceMarked,
    IReadOnlyList<string> Limitations, IReadOnlyList<GuideReviewerNoteDto> ReviewerNotes);
// Counts per area: open steps, how many of those the college's standards require, and the places they cover.
public record GuideAspectDto(string Aspect, string Label, string Why, int OpenSteps, int RequiredSteps, int OpenPlaces);
public record GuideStepDto(string CheckCode, string Name, string Aspect, string Severity, bool Required, bool QuickWin, int Rank,
    int MinutesEach, int OpenMinutes, int OpenPlaces, int TotalPlaces, int Items, string Suggestion, CheckGuidance Guidance, IReadOnlyList<GuideLocationDto> Locations,
    bool ConfirmedByReviewer, IReadOnlyList<GuideResourceDto> Resources);
// A CTL resource linked to the step's check: a published article (Kind "Article", with Slug) or an upcoming workshop (Kind "Workshop",
// with its times, modality and the viewer's registration status, if any).
public record GuideResourceDto(string Kind, Guid Id, string Title, string? Slug, DateTime? StartsAt, DateTime? EndsAt, string? Modality, string? MyStatus);

// CTL management of the links. Kind is Article or Workshop; Status describes whether instructors currently see it.
public record CheckResourceLinkDto(Guid Id, string CheckCode, string CheckName, string Kind, Guid ResourceId, string Title, string Status);
public record CheckResourceInput(string? CheckCode, Guid? ArticleId, Guid? WorkshopId);
// One place a step applies to. Status is Open, Done or NotApplicable; StillDetected means it was marked Done before the latest import
// and the issue is still there. ConfirmedBy names the CTL reviewer whose published review confirmed this same issue; TaskId is an open
// revision task already tracking it.
public record GuideLocationDto(string Key, Guid? ElementId, string Where, string? Module, string Detail, string Evidence, string? SourceUrl,
    string Status, string? Reason, string? SetBy, DateTime? SetAt, bool StillDetected, string? ConfirmedBy, Guid? TaskId);
// A finding a CTL reviewer wrote by hand and confirmed in a published review, still outstanding (no resolved revision task).
public record GuideReviewerNoteDto(Guid FindingId, Guid ReviewId, string Where, string Title, string Description, string Recommendation, string Severity,
    string ReviewerName, DateTime? PublishedAt, Guid? TaskId, string? TaskStatus);

// Slice 6: how instructors respond to each check, so the CTL can tune the next standard-set version.
public record GuideFeedbackDto(ReportScope Scope, int Courses, IReadOnlyList<CheckFeedbackDto> Checks);
/// <summary>
/// The instructor's "Start here" checklist (ADR 016): what they have done so far across their own courses, and the course to continue with.
/// Only the caller's own courses and actions count.
/// </summary>
public record GettingStartedDto(bool BroughtInACourse, bool MarkedAStep, bool AskedForReadiness, bool Endorsed, Guid? CourseId, string? CourseTitle);
public record CheckFeedbackDto(string CheckCode, string Name, string Aspect, int NotApplicable, int Done, decimal? NotApplicablePercent, int Courses,
    bool TuningCandidate, IReadOnlyList<FeedbackReasonDto> Reasons, int ReasonsNotShown);
public record FeedbackReasonDto(string CourseTitle, string Reason, DateTime SetAt);
public record GuideStrengthDto(string CheckCode, string Aspect, string Statement);

// Marks places as Done or NotApplicable (a reason is required), or reopens them (Open). Keys must be places the guide currently shows.
public record GuideProgressInput(string[]? Keys, string? Status, string? Reason);

public interface IGuideService
{
    Task<ServiceResult<CourseGuideDto>> GetAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<ServiceResult<CourseGuideDto>> MarkAsync(CurrentUser user, Guid courseId, GuideProgressInput input, CancellationToken ct);
    // CTL only (enforced by the route). Counts are tenant-wide; reasons come only from courses the viewer can access.
    Task<GuideFeedbackDto> FeedbackAsync(CurrentUser user, CancellationToken ct);
    Task<GettingStartedDto> GettingStartedAsync(CurrentUser user, CancellationToken ct);
    // CTL only (enforced by the route): resources offered on guide steps.
    Task<IReadOnlyList<CheckResourceLinkDto>> ListResourcesAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<CheckResourceLinkDto>>> LinkResourceAsync(CurrentUser user, CheckResourceInput input, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<CheckResourceLinkDto>>> UnlinkResourceAsync(CurrentUser user, Guid linkId, CancellationToken ct);
    // Builds every course's guide in the tenant (no ACL: figures only, nothing returned) so oversight records are current (ADR 014).
    Task<int> RecordAllAsync(Guid tenantId, CancellationToken ct);
}
