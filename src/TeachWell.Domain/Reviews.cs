namespace TeachWell.Domain;

public static class ReviewVocabulary
{
    public const string Draft = "Draft", Published = "Published";
    public const string Rated = "Rated", NotApplicable = "NotApplicable", NotReviewed = "NotReviewed";
    public const string Proposed = "Proposed", Confirmed = "Confirmed", Rejected = "Rejected";
    public static readonly string[] Outcomes = [Rated, NotApplicable, NotReviewed];
    public static readonly string[] Severities = ["Low", "Medium", "High", "Critical"];
    public static readonly string[] Methods = ["Manual", "Automated", "Assisted"];
    // Only element types with imported text and evidence are reviewable element by element.
    public const string QuizType = "quiz", DiscussionType = "discussion";
    public static readonly string[] ElementTypes = ["page", "assignment", QuizType, DiscussionType];
    // Course content items (as opposed to modules and module items).
    public static bool IsContent(string type) => ElementTypes.Contains(type);
    // Graded or practice work students submit. Quizzes are reviewed against a rubric's assignment criteria.
    public static readonly string[] AssessmentTypes = ["assignment", QuizType];
    public static bool IsAssessment(string type) => type is "assignment" or QuizType;
    // A discussion is an assessment only when it is graded (it then carries points and assignment metadata).
    public static bool IsAssessment(string type, bool graded) => IsAssessment(type) || type == DiscussionType && graded;
    // A Canvas module text header: a label inside a module, not content.
    public const string TextHeaderStatus = "TextHeader";
    // A course-level review targets one synthetic "course" element whose snapshots capture the course's structure.
    public const string CourseType = "course";
    public static readonly string[] CriterionTargets = ["page", "assignment", CourseType];
    public static readonly string[] Categories = [
        "Course structure and navigation", "Objectives and competencies", "Assessment and feedback", "Instructional content and scaffolding",
        "Engagement and active learning", "Accessibility and UDL", "Multimedia and copyright", "Student support and communication",
        "Technical quality", "Continuous improvement"];
}

public class RubricProfile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string DeliveryMethod { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RubricVersion> Versions { get; set; } = [];
}
// Published versions and their criteria are immutable; edits require a new draft version.
public class RubricVersion
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = ReviewVocabulary.Draft;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public List<RubricCriterion> Criteria { get; set; } = [];
}
public class RubricCriterion
{
    public Guid Id { get; set; }
    public Guid VersionId { get; set; }
    public int Position { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string ElementTypesJson { get; set; } = "[]";
    public string ReviewMethod { get; set; } = "Manual";
    public string EvidenceGuidance { get; set; } = "";
    // Exactly four anchors describing ratings 0 through 3.
    public string RatingAnchorsJson { get; set; } = "[]";
    public int Weight { get; set; } = 1;
    public bool Required { get; set; }
    public string AutomatedChecksJson { get; set; } = "[]";
}

// A review is pinned to one immutable snapshot and one published rubric version, so results are reproducible.
public class ElementReview
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CourseId { get; set; }
    public Guid ElementId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid RubricVersionId { get; set; }
    // The published standard-set version selected for the course when the review started; its settings drive the automated
    // checks for the life of the review. Null for reviews started before standard sets existed (built-in defaults apply).
    public Guid? StandardSetVersionId { get; set; }
    public Guid ReviewerId { get; set; }
    public string Status { get; set; } = ReviewVocabulary.Draft;
    public string Summary { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ChecksRunAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public List<CriterionEvaluation> Evaluations { get; set; } = [];
    public List<ReviewFinding> Findings { get; set; } = [];
}
public class CriterionEvaluation
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Guid CriterionId { get; set; }
    public string Outcome { get; set; } = ReviewVocabulary.NotReviewed;
    public int? Rating { get; set; }
    public string Comment { get; set; } = "";
    public string Evidence { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
}
public class ReviewFinding
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Guid? CriterionId { get; set; }
    public string Source { get; set; } = "Manual";
    public string? CheckCode { get; set; }
    public string? Fingerprint { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "Medium";
    public string Recommendation { get; set; } = "";
    public string Evidence { get; set; } = "";
    // The specific module or item a course-level finding concerns, so it can link to that place in Canvas.
    public Guid? SubjectElementId { get; set; }
    public string Status { get; set; } = ReviewVocabulary.Confirmed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public sealed record CategoryScore(string Category, int Applicable, int Rated, int NotApplicable, decimal? Percent);
public sealed record ScoreSummary(int Applicable, int Rated, int NotApplicable, int NotReviewed, decimal CoveragePercent, decimal? Percent,
    int RequiredBelowExpectations, int RequiredNotReviewed, IReadOnlyList<CategoryScore> Categories);

public static class ReviewScoring
{
    // N/A is excluded from the denominator; Not reviewed is excluded from the score and reported as missing coverage, never as zero.
    public static ScoreSummary Compute(IReadOnlyList<RubricCriterion> applicable, IReadOnlyList<CriterionEvaluation> evaluations)
    {
        var byCriterion = evaluations.ToDictionary(e => e.CriterionId);
        CriterionEvaluation Of(RubricCriterion c) => byCriterion.TryGetValue(c.Id, out var e) ? e : new() { CriterionId = c.Id };
        static decimal? Percent(IEnumerable<(RubricCriterion C, CriterionEvaluation E)> rows)
        {
            var rated = rows.Where(r => r.E.Outcome == ReviewVocabulary.Rated && r.E.Rating is not null).ToList();
            var possible = rated.Sum(r => 3m * r.C.Weight);
            return possible == 0 ? null : Math.Round(rated.Sum(r => (decimal)r.E.Rating!.Value * r.C.Weight) * 100m / possible, 1);
        }
        var rows = applicable.Select(c => (C: c, E: Of(c))).ToList();
        int Count(string outcome) => rows.Count(r => r.E.Outcome == outcome);
        var categories = rows.GroupBy(r => r.C.Category).OrderBy(g => Array.IndexOf(ReviewVocabulary.Categories, g.Key)).ThenBy(g => g.Key)
            .Select(g => new CategoryScore(g.Key, g.Count(), g.Count(r => r.E.Outcome == ReviewVocabulary.Rated), g.Count(r => r.E.Outcome == ReviewVocabulary.NotApplicable), Percent(g))).ToList();
        var decided = Count(ReviewVocabulary.Rated) + Count(ReviewVocabulary.NotApplicable);
        return new(rows.Count, Count(ReviewVocabulary.Rated), Count(ReviewVocabulary.NotApplicable), Count(ReviewVocabulary.NotReviewed),
            rows.Count == 0 ? 0 : Math.Round(decided * 100m / rows.Count, 1), Percent(rows),
            rows.Count(r => r.C.Required && r.E.Outcome == ReviewVocabulary.Rated && r.E.Rating < 2),
            rows.Count(r => r.C.Required && r.E.Outcome == ReviewVocabulary.NotReviewed), categories);
    }
}
