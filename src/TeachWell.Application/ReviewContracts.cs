using System.Text.RegularExpressions;
using TeachWell.Domain;

namespace TeachWell.Application;

public enum ResultStatus { Ok, NotFound, Invalid, Conflict }
public sealed record ServiceResult<T>(ResultStatus Status, T? Value = default, IDictionary<string, string[]>? Errors = null, string? Message = null)
{
    public static ServiceResult<T> Ok(T value) => new(ResultStatus.Ok, value);
    public static ServiceResult<T> NotFound() => new(ResultStatus.NotFound);
    public static ServiceResult<T> Invalid(IDictionary<string, string[]> errors) => new(ResultStatus.Invalid, Errors: errors);
    public static ServiceResult<T> Invalid(string field, string message) => new(ResultStatus.Invalid, Errors: new Dictionary<string, string[]> { [field] = [message] });
    public static ServiceResult<T> Conflict(string message) => new(ResultStatus.Conflict, Message: message);
}

public record CriterionDto(Guid Id, string Code, string Title, string Description, string Category, string[] ElementTypes, string ReviewMethod,
    string EvidenceGuidance, string[] RatingAnchors, int Weight, bool Required, string[] AutomatedChecks);
// ElementTypes is the union of what the version's criteria apply to, so the UI can offer only versions that fit a page, assignment or course review.
public record RubricVersionSummaryDto(Guid Id, int VersionNumber, string Status, DateTime CreatedAt, DateTime? PublishedAt, int CriterionCount, string[] ElementTypes);
public record RubricProfileDto(Guid Id, string Name, string DeliveryMethod, IReadOnlyList<RubricVersionSummaryDto> Versions);
public record RubricVersionDto(Guid Id, Guid ProfileId, string ProfileName, int VersionNumber, string Status, Guid Revision, DateTime CreatedAt,
    DateTime? PublishedAt, string? PublishedByName, IReadOnlyList<CriterionDto> Criteria);
public record CriterionInput(string? Code, string? Title, string? Description, string? Category, string[]? ElementTypes, string? ReviewMethod,
    string? EvidenceGuidance, string[]? RatingAnchors, int Weight, bool Required, string[]? AutomatedChecks);
public record CreateRubricInput(string? Name, string? DeliveryMethod);
public record UpdateRubricDraftInput(Guid Revision, CriterionInput[]? Criteria);
public record RevisionInput(Guid Revision);

public record SnapshotDto(Guid Id, DateTime FetchedAt, string ContentHash, string Title, string PlainText, string? SourceUrl, string SourceStatus,
    bool HasStructuralEvidence, int Images, int Headings, int Links, int Tables, string? PreviewHtml = null);
public record EvaluationDto(Guid CriterionId, string Outcome, int? Rating, string Comment, string Evidence, DateTime? UpdatedAt);
// Subject* identify the module or item a course-level finding concerns; SubjectSourceUrl is its Canvas location when one was imported.
public record FindingDto(Guid Id, Guid? CriterionId, string Source, string? CheckCode, string Title, string Description, string Severity,
    string Recommendation, string Evidence, string Status, DateTime CreatedAt, Guid? SubjectElementId = null, string? SubjectTitle = null, string? SubjectSourceUrl = null);
public record ReviewDto(Guid Id, Guid CourseId, string CourseTitle, Guid ElementId, string ElementType, string ElementTitle, string Status, Guid Revision,
    string ReviewerName, DateTime StartedAt, DateTime? ChecksRunAt, DateTime? PublishedAt, string Summary, SnapshotDto Snapshot, bool IsLatestSnapshot,
    string? SourceOrigin, RubricVersionDto Rubric, IReadOnlyList<EvaluationDto> Evaluations, IReadOnlyList<FindingDto> Findings, ScoreSummary Score,
    IReadOnlyList<string> CheckLimitations, bool CanEdit, CourseOutlineDto? Outline = null,
    // The standard-set version pinned when the review started, and its guidance for each automated check among the findings.
    AppliedStandardsDto? Standards = null, IReadOnlyDictionary<string, CheckGuidance>? Guidance = null);
// The pinned structure of a course-level review. Each item's SnapshotId is the page snapshot captured with the structure, previewable via GetPreviewAsync.
public record CourseOutlineDto(IReadOnlyList<OutlineModuleDto> Modules, IReadOnlyList<OutlineItemDto> Unplaced, int Competencies);
public record OutlineModuleDto(Guid ElementId, string Title, string Status, string? SourceUrl, IReadOnlyList<OutlineItemDto> Items);
public record OutlineItemDto(Guid ElementId, string Type, string Title, string Status, Guid? SnapshotId, string? SourceUrl, bool? HasDueDate, bool? HasRubric, bool Aligned);
public record PreviewDto(Guid SnapshotId, string Title, string? PreviewHtml);
public record ReviewSummaryDto(Guid Id, Guid ElementId, string ElementType, string ElementTitle, string Status, string ReviewerName, string RubricName,
    int RubricVersion, DateTime StartedAt, DateTime? PublishedAt, DateTime SnapshotFetchedAt, decimal CoveragePercent, int ConfirmedFindings, bool IsMine);
public record StartReviewInput(Guid RubricVersionId);
public record EvaluationInput(Guid Revision, string? Outcome, int? Rating, string? Comment, string? Evidence);
public record FindingInput(Guid Revision, Guid? CriterionId, string? Title, string? Description, string? Severity, string? Recommendation, string? Evidence);
public record FindingUpdateInput(Guid Revision, string? Status, string? Title, string? Description, string? Severity, string? Recommendation, string? Evidence);
public record PublishReviewInput(Guid Revision, string? Summary);

public interface IRubricService
{
    Task<IReadOnlyList<RubricProfileDto>> ListAsync(CurrentUser user, CancellationToken ct);
    Task<ServiceResult<RubricVersionDto>> GetVersionAsync(CurrentUser user, Guid profileId, Guid versionId, CancellationToken ct);
    Task<ServiceResult<RubricVersionDto>> CreateAsync(CurrentUser user, CreateRubricInput input, CancellationToken ct);
    Task<ServiceResult<RubricVersionDto>> CreateDraftAsync(CurrentUser user, Guid profileId, CancellationToken ct);
    Task<ServiceResult<RubricVersionDto>> UpdateDraftAsync(CurrentUser user, Guid profileId, Guid versionId, UpdateRubricDraftInput input, CancellationToken ct);
    Task<ServiceResult<RubricVersionDto>> PublishAsync(CurrentUser user, Guid profileId, Guid versionId, Guid revision, CancellationToken ct);
}
public interface IReviewService
{
    Task<ServiceResult<IReadOnlyList<ReviewSummaryDto>>> ListForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct);
    Task<ServiceResult<ReviewDto>> StartAsync(CurrentUser user, Guid elementId, StartReviewInput input, CancellationToken ct);
    // Captures the course's current structure as a snapshot of its synthetic course element, then starts a review pinned to it.
    Task<ServiceResult<ReviewDto>> StartCourseReviewAsync(CurrentUser user, Guid courseId, StartReviewInput input, CancellationToken ct);
    // Only snapshots this review is pinned to (its own, or those captured in its course structure) can be previewed through it.
    Task<ServiceResult<PreviewDto>> GetPreviewAsync(CurrentUser user, Guid reviewId, Guid snapshotId, CancellationToken ct);
    Task<ServiceResult<ReviewDto>> GetAsync(CurrentUser user, Guid reviewId, CancellationToken ct);
    Task<ServiceResult<ReviewDto>> EvaluateAsync(CurrentUser user, Guid reviewId, Guid criterionId, EvaluationInput input, CancellationToken ct);
    Task<ServiceResult<ReviewDto>> RunChecksAsync(CurrentUser user, Guid reviewId, Guid revision, CancellationToken ct);
    Task<ServiceResult<ReviewDto>> AddFindingAsync(CurrentUser user, Guid reviewId, FindingInput input, CancellationToken ct);
    Task<ServiceResult<ReviewDto>> UpdateFindingAsync(CurrentUser user, Guid findingId, FindingUpdateInput input, CancellationToken ct);
    Task<ServiceResult<ReviewDto>> PublishAsync(CurrentUser user, Guid reviewId, PublishReviewInput input, CancellationToken ct);
}

public static partial class ReviewValidation
{
    public const int MaxCriteria = 60;
    public static Dictionary<string, string[]> Criteria(CriterionInput[]? criteria)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string key, string message) => errors[key] = errors.TryGetValue(key, out var m) ? [.. m, message] : [message];
        if (criteria is null) { Add("criteria", "Provide a criteria list."); return errors; }
        if (criteria.Length > MaxCriteria) Add("criteria", $"A rubric version may contain at most {MaxCriteria} criteria.");
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < criteria.Length; i++)
        {
            var c = criteria[i]; var key = $"criteria[{i}]";
            if (c is null) { Add(key, "Criterion is required."); continue; }
            if (c.Code is null || !CodePattern().IsMatch(c.Code)) Add(key + ".code", "Use 1–20 uppercase letters, digits or hyphens.");
            else if (!codes.Add(c.Code)) Add(key + ".code", "Criterion codes must be unique within a version.");
            if (string.IsNullOrWhiteSpace(c.Title) || c.Title.Length > 160) Add(key + ".title", "Enter a title of 1–160 characters.");
            if ((c.Description?.Length ?? 0) > 2000) Add(key + ".description", "Description is limited to 2,000 characters.");
            if ((c.EvidenceGuidance?.Length ?? 0) > 2000) Add(key + ".evidenceGuidance", "Evidence guidance is limited to 2,000 characters.");
            if (c.Category is null || !ReviewVocabulary.Categories.Contains(c.Category)) Add(key + ".category", "Choose a framework category.");
            if (c.ElementTypes is not { Length: > 0 } || c.ElementTypes.Any(t => !ReviewVocabulary.CriterionTargets.Contains(t)) || c.ElementTypes.Distinct().Count() != c.ElementTypes.Length)
                Add(key + ".elementTypes", "Choose one or more of: page, assignment, course.");
            if (c.ReviewMethod is null || !ReviewVocabulary.Methods.Contains(c.ReviewMethod)) Add(key + ".reviewMethod", "Choose Manual, Automated or Assisted.");
            if (c.RatingAnchors is not { Length: 4 } || c.RatingAnchors.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 500))
                Add(key + ".ratingAnchors", "Provide four rating anchors (0–3), each 1–500 characters.");
            if (c.Weight is < 1 or > 5) Add(key + ".weight", "Weight must be between 1 and 5.");
            if (c.AutomatedChecks is not null && (c.AutomatedChecks.Any(x => !DeterministicChecks.All.Contains(x)) || c.AutomatedChecks.Distinct().Count() != c.AutomatedChecks.Length))
                Add(key + ".automatedChecks", "Use known deterministic check codes without duplicates.");
            if (c.ReviewMethod is "Automated" or "Assisted" && c.AutomatedChecks is not { Length: > 0 })
                Add(key + ".automatedChecks", "Automated and assisted criteria must name at least one deterministic check.");
        }
        return errors;
    }
    public static Dictionary<string, string[]> Evaluation(EvaluationInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.Outcome is null || !ReviewVocabulary.Outcomes.Contains(input.Outcome)) errors["outcome"] = ["Choose Rated, NotApplicable or NotReviewed."];
        else if (input.Outcome == ReviewVocabulary.Rated && input.Rating is not (>= 0 and <= 3)) errors["rating"] = ["A rated criterion needs a rating from 0 to 3."];
        else if (input.Outcome != ReviewVocabulary.Rated && input.Rating is not null) errors["rating"] = ["Only rated criteria carry a numeric rating. Not reviewed is not a zero."];
        if ((input.Comment?.Length ?? 0) > 4000) errors["comment"] = ["Comment is limited to 4,000 characters."];
        if ((input.Evidence?.Length ?? 0) > 4000) errors["evidence"] = ["Evidence is limited to 4,000 characters."];
        return errors;
    }
    public static Dictionary<string, string[]> Finding(string? title, string? description, string? severity, string? recommendation, string? evidence)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) errors["title"] = ["Enter a title of 1–200 characters."];
        if ((description?.Length ?? 0) > 4000) errors["description"] = ["Description is limited to 4,000 characters."];
        if (severity is null || !ReviewVocabulary.Severities.Contains(severity)) errors["severity"] = ["Choose Low, Medium, High or Critical."];
        if ((recommendation?.Length ?? 0) > 4000) errors["recommendation"] = ["Recommendation is limited to 4,000 characters."];
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 4000) errors["evidence"] = ["Findings must cite evidence (1–4,000 characters)."];
        return errors;
    }
    [GeneratedRegex("^[A-Z0-9-]{1,20}$")] private static partial Regex CodePattern();
}
