using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reports;

public sealed class ReportService(AppDbContext db) : IReportService
{
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);
    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));
    private static readonly string[] StandardCaveats =
        ["Figures describe what has been recorded in TeachWell as of the generation time below; they are not a compliance, accreditation or accessibility certification.",
         "Not-reviewed and unmapped items are reported as missing coverage, never as a failing score."];

    public async Task<ServiceResult<AlignmentReportDto>> AlignmentAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Accessible(user).Where(c => c.Id == courseId).Select(c => c.Title).SingleOrDefaultAsync(ct);
        if (course is null) return ServiceResult<AlignmentReportDto>.NotFound();
        // Assignments and quizzes, plus graded discussions (those carry assignment metadata in their evidence).
        var assessments = (await db.Elements.AsNoTracking()
            .Where(e => e.CourseId == courseId && (e.Type == "assignment" || e.Type == ReviewVocabulary.QuizType || e.Type == ReviewVocabulary.DiscussionType) && e.SourceStatus != "Removed")
            .Select(e => new { e.Id, e.Title, e.Type, e.EvidenceJson }).ToListAsync(ct))
            .Where(e => e.Type != ReviewVocabulary.DiscussionType || ContentEvidence.Parse(e.EvidenceJson)?.Assignment is not null)
            .Select(e => new { e.Id, e.Title }).ToList();
        var competencies = await db.CourseCompetencies.AsNoTracking().AsSplitQuery().Include(c => c.Mappings)
            .Where(c => c.CourseId == courseId).OrderBy(c => c.Code).ToListAsync(ct);
        var assessmentIds = assessments.Select(a => a.Id).ToHashSet();
        var mapped = competencies.Select(c => new MappedCompetencyDto(c.Id, c.Code, c.Title,
            c.Mappings.Where(m => assessmentIds.Contains(m.ElementId)).Select(m => assessments.Single(a => a.Id == m.ElementId).Title).ToList())).ToList();
        var mappedAssessmentIds = competencies.SelectMany(c => c.Mappings.Select(m => m.ElementId)).Where(assessmentIds.Contains).Distinct().ToHashSet();
        var orphans = mapped.Where(c => c.ElementTitles.Count == 0).Select(c => new OrphanCompetencyDto(c.Id, c.Code, c.Title)).ToList();
        var unmapped = assessments.Where(a => !mappedAssessmentIds.Contains(a.Id)).Select(a => new UnmappedAssessmentDto(a.Id, a.Title)).ToList();
        var scope = new ReportScope($"{course} · competency alignment", DateTime.UtcNow,
            [.. StandardCaveats, "Coverage reflects mappings the course owner has recorded; it is not a verification that the evidence is adequate.", "Covers only this course's currently captured assignments."]);
        return ServiceResult<AlignmentReportDto>.Ok(new(scope, competencies.Count, assessments.Count, mappedAssessmentIds.Count,
            assessments.Count == 0 ? null : Math.Round(mappedAssessmentIds.Count * 100m / assessments.Count, 1), mapped, orphans, unmapped));
    }

    public async Task<ServiceResult<CourseQualityReportDto>> QualityAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Accessible(user).Where(c => c.Id == courseId).Select(c => c.Title).SingleOrDefaultAsync(ct);
        if (course is null) return ServiceResult<CourseQualityReportDto>.NotFound();
        var reviewable = await db.Elements.AsNoTracking().Where(e => e.CourseId == courseId && ReviewVocabulary.ElementTypes.Contains(e.Type) && e.SourceStatus != "Removed")
            .Select(e => e.Id).ToListAsync(ct);
        var published = await db.Reviews.AsNoTracking().Where(r => r.CourseId == courseId && r.Status == ReviewVocabulary.Published)
            .Select(r => new { r.Id, r.ElementId }).ToListAsync(ct);
        var (percents, categoryRows) = await ScoreRowsAsync(published.Select(p => p.Id).ToList(), ct);
        var categories = categoryRows.GroupBy(r => r.Category).Select(g => new CategoryAverageDto(g.Key, Average(g.ToList()), g.Count(r => r.Outcome == ReviewVocabulary.Rated))).ToList();
        var outstanding = await OutstandingFindingsAsync([courseId], ct);
        var scope = new ReportScope($"{course} · course quality", DateTime.UtcNow,
            [.. StandardCaveats, "Averages combine every published review of this course; a reviewed element may have more than one published review over time."]);
        // Coverage counts reviewed pages and assignments only: a course-level review (or a review of an item since removed) is not one of them.
        var reviewed = published.Select(p => p.ElementId).Where(reviewable.Contains).Distinct().Count();
        return ServiceResult<CourseQualityReportDto>.Ok(new(scope, reviewable.Count, reviewed,
            reviewable.Count == 0 ? null : Math.Round(reviewed * 100m / reviewable.Count, 1),
            percents.Count == 0 ? null : Math.Round(percents.Average(), 1), categories, outstanding));
    }

    public async Task<CtlOverviewReportDto> CtlOverviewAsync(CurrentUser user, CancellationToken ct)
    {
        var courseIds = await Accessible(user).Select(c => c.Id).ToListAsync(ct);
        var publishedCount = await db.Reviews.CountAsync(r => courseIds.Contains(r.CourseId) && r.Status == ReviewVocabulary.Published, ct);
        var outstanding = await OutstandingFindingsAsync(courseIds, ct);
        var tasks = await db.RevisionTasks.AsNoTracking().Where(t => courseIds.Contains(t.CourseId)).Select(t => new { t.Status, t.CreatedAt }).ToListAsync(ct);
        var byStatus = tasks.GroupBy(t => t.Status).Select(g => new TaskStatusCountDto(g.Key, g.Count(), Math.Round((decimal)g.Average(t => (DateTime.UtcNow - t.CreatedAt).TotalDays), 1))).ToList();
        // Ordering by a property of the projected DTO cannot be translated together with the preceding GroupBy/Select; materialize first.
        var themes = (await db.SupportTickets.AsNoTracking().Where(t => t.TenantId == user.TenantId).GroupBy(t => t.Category)
            .Select(g => new SupportThemeDto(g.Key, g.Count())).ToListAsync(ct)).OrderByDescending(t => t.Count).ToList();
        var scope = new ReportScope("CTL overview · courses you can access", DateTime.UtcNow,
            [.. StandardCaveats, "Support themes are tenant-wide, matching the shared triage queue; review and task figures cover only courses you currently have access to."]);
        return new(scope, courseIds.Count, publishedCount, outstanding.Count, byStatus, themes);
    }

    public async Task<RevisionReportDto> RevisionsAsync(CurrentUser user, CancellationToken ct)
    {
        var tasks = await db.RevisionTasks.AsNoTracking().Where(t => t.TenantId == user.TenantId && (t.AssigneeId == user.Id || t.ReviewerId == user.Id))
            .Select(t => new { t.Id, t.Title, t.CourseId, t.Status, t.Priority, t.CreatedAt }).ToListAsync(ct);
        var courseTitles = await db.Courses.AsNoTracking().Where(c => tasks.Select(t => t.CourseId).Contains(c.Id)).Select(c => new { c.Id, c.Title }).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var byStatus = tasks.GroupBy(t => t.Status).Select(g => new TaskStatusCountDto(g.Key, g.Count(), Math.Round((decimal)g.Average(t => (DateTime.UtcNow - t.CreatedAt).TotalDays), 1))).ToList();
        var open = tasks.Where(t => !TaskVocabulary.Terminal.Contains(t.Status)).OrderByDescending(t => DateTime.UtcNow - t.CreatedAt)
            .Select(t => new AgingTaskDto(t.Id, t.Title, courseTitles.GetValueOrDefault(t.CourseId, "Unknown course"), t.Status, (int)(DateTime.UtcNow - t.CreatedAt).TotalDays, t.Priority)).ToList();
        var scope = new ReportScope("Your revision tasks", DateTime.UtcNow, [.. StandardCaveats, "Covers tasks you are assigned or that you opened as a CTL reviewer."]);
        return new(scope, byStatus, open);
    }

    // Shared by the course quality report and the CTL overview: a Confirmed finding with no Resolved task is outstanding.
    private async Task<List<OutstandingFindingDto>> OutstandingFindingsAsync(List<Guid> courseIds, CancellationToken ct)
    {
        var rows = await (from f in db.ReviewFindings.AsNoTracking()
            join r in db.Reviews.AsNoTracking() on f.ReviewId equals r.Id
            where courseIds.Contains(r.CourseId) && f.Status == ReviewVocabulary.Confirmed && r.Status == ReviewVocabulary.Published
            select new { f.Id, f.ReviewId, r.ElementId, f.Title, f.Severity, f.CreatedAt }).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var findingIds = rows.Select(r => r.Id).ToList();
        var taskStatusByFinding = await (from tf in db.RevisionTaskFindings.AsNoTracking()
            join t in db.RevisionTasks.AsNoTracking() on tf.TaskId equals t.Id
            where findingIds.Contains(tf.FindingId)
            select new { tf.FindingId, t.Status }).ToListAsync(ct);
        var elementTitles = await db.Elements.AsNoTracking().Where(e => rows.Select(r => r.ElementId).Distinct().Contains(e.Id)).Select(e => new { e.Id, e.Title }).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        var result = new List<OutstandingFindingDto>();
        foreach (var row in rows)
        {
            var statuses = taskStatusByFinding.Where(t => t.FindingId == row.Id).Select(t => t.Status).ToList();
            if (statuses.Contains(TaskVocabulary.Resolved)) continue; // addressed; not outstanding
            result.Add(new(row.Id, row.ReviewId, row.ElementId, elementTitles.GetValueOrDefault(row.ElementId, "Unknown element"), row.Title, row.Severity, row.CreatedAt,
                statuses.Any(s => !TaskVocabulary.Terminal.Contains(s))));
        }
        return result;
    }

    // Loads every rated criterion-evaluation row across a set of published reviews, with the criterion's category and weight,
    // for category aggregation; and each review's own overall percent, computed the same way ReviewWorkspace shows it.
    private async Task<(List<decimal> Percents, List<(string Category, int? Rating, int Weight, string Outcome)> Rows)> ScoreRowsAsync(List<Guid> reviewIds, CancellationToken ct)
    {
        if (reviewIds.Count == 0) return ([], []);
        var reviews = await db.Reviews.AsNoTracking().AsSplitQuery().Include(r => r.Evaluations).Where(r => reviewIds.Contains(r.Id)).ToListAsync(ct);
        var versionIds = reviews.Select(r => r.RubricVersionId).Distinct().ToList();
        var criteria = await db.RubricCriteria.AsNoTracking().Where(c => versionIds.Contains(c.VersionId)).ToListAsync(ct);
        var percents = new List<decimal>(); var rows = new List<(string, int?, int, string)>();
        foreach (var review in reviews)
        {
            var applicable = criteria.Where(c => c.VersionId == review.RubricVersionId && review.Evaluations.Any(e => e.CriterionId == c.Id)).ToList();
            var score = ReviewScoring.Compute(applicable, review.Evaluations);
            if (score.Percent is { } p) percents.Add(p);
            foreach (var c in applicable)
            {
                var evaluation = review.Evaluations.Single(e => e.CriterionId == c.Id);
                rows.Add((c.Category, evaluation.Rating, c.Weight, evaluation.Outcome));
            }
        }
        return (percents, rows);
    }
    private static decimal? Average(List<(string Category, int? Rating, int Weight, string Outcome)> rows)
    {
        var rated = rows.Where(r => r.Outcome == ReviewVocabulary.Rated && r.Rating is not null).ToList();
        var possible = rated.Sum(r => 3m * r.Weight);
        return possible == 0 ? null : Math.Round(rated.Sum(r => (decimal)r.Rating!.Value * r.Weight) * 100m / possible, 1);
    }
}
