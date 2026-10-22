using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

public sealed class ReviewService(AppDbContext db, InstitutionSettings? institution = null) : IReviewService
{
    private readonly TimeZoneInfo timeZone = (institution ?? InstitutionSettings.Utc).TimeZone;
    private const string Stale = "This review changed since you opened it. Reload it and try again.";
    private const string Immutable = "Published reviews cannot be changed.";
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);

    // Course ACL: tenant plus ownership or an explicit share. Roles alone never grant course access.
    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));

    public async Task<ServiceResult<IReadOnlyList<ReviewSummaryDto>>> ListForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        if (!await Accessible(user).AnyAsync(c => c.Id == courseId, ct)) return ServiceResult<IReadOnlyList<ReviewSummaryDto>>.NotFound();
        var rows = await (from r in db.Reviews.AsNoTracking()
                          where r.CourseId == courseId && r.TenantId == user.TenantId && (r.Status == ReviewVocabulary.Published || r.ReviewerId == user.Id)
                          join e in db.Elements on r.ElementId equals e.Id
                          join s in db.ElementSnapshots on r.SnapshotId equals s.Id
                          join u in db.Users on r.ReviewerId equals u.Id
                          join v in db.RubricVersions on r.RubricVersionId equals v.Id
                          join p in db.RubricProfiles on v.ProfileId equals p.Id
                          select new
                          {
                              r.Id, r.ElementId, e.Type, s.Title, r.Status, u.DisplayName, p.Name, v.VersionNumber, r.StartedAt, r.PublishedAt, s.FetchedAt, r.ReviewerId,
                              Total = r.Evaluations.Count(), Decided = r.Evaluations.Count(x => x.Outcome != ReviewVocabulary.NotReviewed),
                              Confirmed = r.Findings.Count(f => f.Status == ReviewVocabulary.Confirmed)
                          }).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<ReviewSummaryDto>>.Ok(rows.OrderByDescending(r => r.StartedAt).Select(r => new ReviewSummaryDto(r.Id, r.ElementId, r.Type, r.Title, r.Status,
            r.DisplayName, r.Name, r.VersionNumber, r.StartedAt, r.PublishedAt, r.FetchedAt, r.Total == 0 ? 0 : Math.Round(r.Decided * 100m / r.Total, 1), r.Confirmed, r.ReviewerId == user.Id)).ToList());
    }

    public async Task<ServiceResult<ReviewDto>> StartAsync(CurrentUser user, Guid elementId, StartReviewInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<ReviewDto>.NotFound();
        var element = await db.Elements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == elementId && Accessible(user).Any(c => c.Id == e.CourseId), ct);
        if (element is null) return ServiceResult<ReviewDto>.NotFound();
        if (!ReviewVocabulary.ElementTypes.Contains(element.Type)) return ServiceResult<ReviewDto>.Invalid("element", "Only pages and assignments can be reviewed element by element; use a course-level review for course structure.");
        return await StartCoreAsync(user, element, input, ct);
    }

    public async Task<ServiceResult<ReviewDto>> StartCourseReviewAsync(CurrentUser user, Guid courseId, StartReviewInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<ReviewDto>.NotFound();
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return ServiceResult<ReviewDto>.NotFound();
        var element = await CourseStructureCapture.EnsureAsync(db, course, ct);
        var created = db.Entry(element).State == EntityState.Added;
        await CourseStructureCapture.CaptureAsync(db, element, ct);
        if (created) db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = element.Id, Action = "CourseStructureElementCreated" });
        await db.SaveChangesAsync(ct);
        return await StartCoreAsync(user, element, input, ct);
    }

    private async Task<ServiceResult<ReviewDto>> StartCoreAsync(CurrentUser user, CourseElement element, StartReviewInput input, CancellationToken ct)
    {
        var snapshot = await db.ElementSnapshots.AsNoTracking().Where(s => s.ElementId == element.Id).OrderByDescending(s => s.FetchedAt).FirstOrDefaultAsync(ct);
        if (snapshot is null) return ServiceResult<ReviewDto>.Invalid("element", "This element has no captured snapshot to review. Re-import the course first.");
        var version = await db.RubricVersions.AsNoTracking().Include(v => v.Criteria)
            .SingleOrDefaultAsync(v => v.Id == input.RubricVersionId && v.Status == ReviewVocabulary.Published && db.RubricProfiles.Any(p => p.Id == v.ProfileId && p.TenantId == user.TenantId), ct);
        if (version is null) return ServiceResult<ReviewDto>.Invalid("rubricVersionId", "Choose a published rubric version.");
        var applicable = version.Criteria.Where(c => ReviewMapping.AppliesTo(c, element.Type)).ToList();
        if (applicable.Count == 0) return ServiceResult<ReviewDto>.Invalid("rubricVersionId", element.Type == ReviewVocabulary.CourseType
            ? "This rubric version has no course-level criteria. Choose a version with criteria that apply to a course."
            : $"This rubric version has no criteria for a {element.Type}.");
        if (await db.Reviews.AnyAsync(r => r.ElementId == element.Id && r.ReviewerId == user.Id && r.Status == ReviewVocabulary.Draft, ct))
            return ServiceResult<ReviewDto>.Conflict("You already have a draft review of this element. Open it from the course reviews list.");
        var classification = await db.Courses.AsNoTracking().Where(c => c.Id == element.CourseId).Select(c => new { c.DeliveryMode, c.Department }).SingleAsync(ct);
        var standards = await StandardsResolver.ResolveAsync(db, user.TenantId, classification.DeliveryMode, classification.Department, ct);
        var review = new ElementReview
        {
            Id = Guid.NewGuid(), TenantId = user.TenantId, CourseId = element.CourseId, ElementId = element.Id, SnapshotId = snapshot.Id,
            RubricVersionId = version.Id, StandardSetVersionId = standards?.Version.Id, ReviewerId = user.Id
        };
        review.Evaluations = applicable.Select(c => new CriterionEvaluation { Id = Guid.NewGuid(), ReviewId = review.Id, CriterionId = c.Id }).ToList();
        db.Reviews.Add(review);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = review.Id, Action = "ElementReviewStarted" });
        await db.SaveChangesAsync(ct);
        return await GetAsync(user, review.Id, ct);
    }

    private async Task<ElementReview?> Visible(CurrentUser user, Guid reviewId, bool tracking, CancellationToken ct)
    {
        var reviews = tracking ? db.Reviews : db.Reviews.AsNoTracking();
        var review = await reviews.AsSplitQuery().Include(r => r.Evaluations).Include(r => r.Findings)
            .SingleOrDefaultAsync(r => r.Id == reviewId && r.TenantId == user.TenantId && Accessible(user).Any(c => c.Id == r.CourseId), ct);
        // Drafts are private to their author until published.
        return review is null || review.Status == ReviewVocabulary.Draft && review.ReviewerId != user.Id ? null : review;
    }

    private async Task<(ElementReview? Review, ServiceResult<ReviewDto>? Failure)> Editable(CurrentUser user, Guid reviewId, Guid revision, CancellationToken ct)
    {
        var review = await Visible(user, reviewId, true, ct);
        if (review is null || review.ReviewerId != user.Id || !IsReviewer(user)) return (null, ServiceResult<ReviewDto>.NotFound());
        if (review.Status != ReviewVocabulary.Draft) return (null, ServiceResult<ReviewDto>.Conflict(Immutable));
        if (review.Revision != revision) return (null, ServiceResult<ReviewDto>.Conflict(Stale));
        // The concurrency token makes a racing edit fail at commit rather than overwrite silently.
        db.Entry(review).Property(r => r.Revision).OriginalValue = revision;
        review.Revision = Guid.NewGuid();
        return (review, null);
    }

    public async Task<ServiceResult<ReviewDto>> GetAsync(CurrentUser user, Guid reviewId, CancellationToken ct)
    {
        var review = await Visible(user, reviewId, false, ct);
        return review is null ? ServiceResult<ReviewDto>.NotFound() : ServiceResult<ReviewDto>.Ok(await Dto(user, review, ct));
    }

    private async Task<ReviewDto> Dto(CurrentUser user, ElementReview review, CancellationToken ct)
    {
        var snapshot = await db.ElementSnapshots.AsNoTracking().SingleAsync(s => s.Id == review.SnapshotId, ct);
        var element = await db.Elements.AsNoTracking().SingleAsync(e => e.Id == review.ElementId, ct);
        var course = await db.Courses.AsNoTracking().Where(c => c.Id == review.CourseId).Select(c => new { c.Title, c.SourceOrigin, c.DeliveryMode, c.Department }).SingleAsync(ct);
        var standards = await StandardsResolver.LoadAsync(db, review.StandardSetVersionId, ct);
        var latest = await db.ElementSnapshots.AsNoTracking().Where(s => s.ElementId == review.ElementId).OrderByDescending(s => s.FetchedAt).Select(s => s.Id).FirstAsync(ct);
        var version = await db.RubricVersions.AsNoTracking().Include(v => v.Criteria).SingleAsync(v => v.Id == review.RubricVersionId, ct);
        var profile = await db.RubricProfiles.AsNoTracking().Where(p => p.Id == version.ProfileId).Select(p => p.Name).SingleAsync(ct);
        var names = await db.Users.AsNoTracking().Where(u => u.Id == review.ReviewerId || u.Id == version.PublishedBy).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var evaluated = review.Evaluations.Select(e => e.CriterionId).ToHashSet();
        var criteria = version.Criteria.Where(c => evaluated.Contains(c.Id)).OrderBy(c => c.Position).ToList();
        var evidence = ContentEvidence.Parse(snapshot.EvidenceJson);
        var structure = element.Type == ReviewVocabulary.CourseType ? CourseStructureEvidence.Parse(snapshot.EvidenceJson) : null;
        var author = review.ReviewerId == user.Id;
        // Source URLs come from the importer, which builds them from the approved Canvas origin; only links to that origin are shown.
        var related = (structure?.AllItems().Select(i => i.ElementId) ?? []).Concat(structure?.Modules.Select(m => m.ElementId) ?? [])
            .Concat(review.Findings.Where(f => f.SubjectElementId is not null).Select(f => f.SubjectElementId!.Value)).Distinct().ToList();
        var subjects = await db.Elements.AsNoTracking().Where(e => related.Contains(e.Id) && e.CourseId == review.CourseId)
            .Select(e => new { e.Id, e.Title, e.SourceUrl }).ToDictionaryAsync(e => e.Id, ct);
        string? Url(Guid? id) => id is { } v && subjects.TryGetValue(v, out var s) ? SafeUrl(s.SourceUrl, course.SourceOrigin) : null;
        var visible = review.Findings.Where(f => author || f.Status == ReviewVocabulary.Confirmed).ToList();
        var guidance = StandardsResolver.Guidance(standards);
        var outline = structure is null ? null : new CourseOutlineDto(
            structure.Modules.Select(m => new OutlineModuleDto(m.ElementId, m.Title, m.Status, Url(m.ElementId), m.Items.Select(i => OutlineItem(i, Url(i.ElementId))).ToList())).ToList(),
            structure.Unplaced.Select(i => OutlineItem(i, Url(i.ElementId))).ToList(), structure.Competencies);
        return new(review.Id, review.CourseId, course.Title, review.ElementId, element.Type, snapshot.Title, review.Status, review.Revision,
            names.GetValueOrDefault(review.ReviewerId, "Unknown reviewer"), review.StartedAt, review.ChecksRunAt, review.PublishedAt, review.Summary,
            new(snapshot.Id, snapshot.FetchedAt, snapshot.ContentHash, snapshot.Title, snapshot.PlainText, SafeUrl(snapshot.SourceUrl, course.SourceOrigin), snapshot.SourceStatus, evidence is not null,
                evidence?.Images.Count ?? 0, evidence?.Headings.Count ?? 0, evidence?.Links.Count ?? 0, evidence?.Tables.Count ?? 0, snapshot.PreviewHtml),
            latest == snapshot.Id, course.SourceOrigin,
            ReviewMapping.ToDto(version, profile, version.PublishedBy is { } p ? names.GetValueOrDefault(p) : null, criteria),
            criteria.Select(c => review.Evaluations.Single(e => e.CriterionId == c.Id)).Select(e => new EvaluationDto(e.CriterionId, e.Outcome, e.Rating, e.Comment, e.Evidence, e.UpdatedAt)).ToList(),
            // Only the author sees proposed or rejected checks; everyone else sees confirmed findings.
            visible.OrderBy(f => f.CreatedAt).ThenBy(f => f.Fingerprint)
                .Select(f => new FindingDto(f.Id, f.CriterionId, f.Source, f.CheckCode, f.Title, f.Description, f.Severity, f.Recommendation, f.Evidence, f.Status, f.CreatedAt,
                    f.SubjectElementId, f.SubjectElementId is { } sid && subjects.TryGetValue(sid, out var subject) ? subject.Title : null, Url(f.SubjectElementId))).ToList(),
            ReviewScoring.Compute(criteria, review.Evaluations),
            Checks(element.Type, snapshot, StandardsResolver.Profile(standards)).Limitations,
            author && review.Status == ReviewVocabulary.Draft && IsReviewer(user), outline,
            StandardsResolver.Describe(standards, course.DeliveryMode, course.Department, standards is null
                ? "This review started before standard sets were introduced, or when none was published, so TeachWell's built-in defaults apply."
                : "Pinned when this review started; later standard-set changes do not affect it."),
            visible.Where(f => f.CheckCode is not null).Select(f => f.CheckCode!).Distinct().Where(guidance.ContainsKey).ToDictionary(c => c, c => guidance[c]));
    }

    private static OutlineItemDto OutlineItem(CourseItemEvidence i, string? url) => new(i.ElementId, i.Type, i.Title, i.Status, i.SnapshotId, url, i.HasDueDate, i.HasRubric, i.Aligned);
    private CheckReport Checks(string elementType, ElementSnapshot snapshot, CheckProfile profile) => elementType == ReviewVocabulary.CourseType
        ? CourseChecks.Run(CourseStructureEvidence.Parse(snapshot.EvidenceJson), profile, timeZone)
        : DeterministicChecks.Run(elementType, snapshot.SourceStatus, ContentEvidence.Parse(snapshot.EvidenceJson), profile, snapshot.PlainText);

    // Defense in depth for rendered links: only https URLs on the course's own Canvas origin are returned.
    internal static string? SafeUrl(string? url, string? origin) =>
        url is not null && origin is not null && Uri.TryCreate(url, UriKind.Absolute, out var u) && Uri.TryCreate(origin, UriKind.Absolute, out var o)
            && u.Scheme == Uri.UriSchemeHttps && string.Equals(u.Host, o.Host, StringComparison.OrdinalIgnoreCase) && u.Port == o.Port ? u.AbsoluteUri : null;

    public async Task<ServiceResult<PreviewDto>> GetPreviewAsync(CurrentUser user, Guid reviewId, Guid snapshotId, CancellationToken ct)
    {
        var review = await Visible(user, reviewId, false, ct);
        if (review is null) return ServiceResult<PreviewDto>.NotFound();
        var pinned = await db.ElementSnapshots.AsNoTracking().SingleAsync(s => s.Id == review.SnapshotId, ct);
        var allowed = snapshotId == pinned.Id || CourseStructureEvidence.Parse(pinned.EvidenceJson)?.AllItems().Any(i => i.SnapshotId == snapshotId) == true;
        if (!allowed) return ServiceResult<PreviewDto>.NotFound();
        var snapshot = await db.ElementSnapshots.AsNoTracking().Where(s => s.Id == snapshotId && db.Elements.Any(e => e.Id == s.ElementId && e.CourseId == review.CourseId))
            .Select(s => new PreviewDto(s.Id, s.Title, s.PreviewHtml)).SingleOrDefaultAsync(ct);
        return snapshot is null ? ServiceResult<PreviewDto>.NotFound() : ServiceResult<PreviewDto>.Ok(snapshot);
    }

    public async Task<ServiceResult<ReviewDto>> EvaluateAsync(CurrentUser user, Guid reviewId, Guid criterionId, EvaluationInput input, CancellationToken ct)
    {
        var errors = ReviewValidation.Evaluation(input);
        var (review, failure) = await Editable(user, reviewId, input.Revision, ct);
        if (review is null) return failure!;
        var evaluation = review.Evaluations.SingleOrDefault(e => e.CriterionId == criterionId);
        if (evaluation is null) return ServiceResult<ReviewDto>.NotFound();
        if (errors.Count > 0) return ServiceResult<ReviewDto>.Invalid(errors);
        evaluation.Outcome = input.Outcome!; evaluation.Rating = input.Rating;
        evaluation.Comment = input.Comment?.Trim() ?? ""; evaluation.Evidence = input.Evidence?.Trim() ?? ""; evaluation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ServiceResult<ReviewDto>.Ok(await Dto(user, review, ct));
    }

    public async Task<ServiceResult<ReviewDto>> RunChecksAsync(CurrentUser user, Guid reviewId, Guid revision, CancellationToken ct)
    {
        var (review, failure) = await Editable(user, reviewId, revision, ct);
        if (review is null) return failure!;
        var snapshot = await db.ElementSnapshots.AsNoTracking().SingleAsync(s => s.Id == review.SnapshotId, ct);
        var type = await db.Elements.Where(e => e.Id == review.ElementId).Select(e => e.Type).SingleAsync(ct);
        var criteria = await db.RubricCriteria.AsNoTracking().Where(c => c.VersionId == review.RubricVersionId).OrderBy(c => c.Position).ToListAsync(ct);
        var evaluated = review.Evaluations.Select(e => e.CriterionId).ToHashSet();
        // Checks run under the standard-set version pinned at review start, never whatever is current now.
        var report = Checks(type, snapshot, StandardsResolver.Profile(await StandardsResolver.LoadAsync(db, review.StandardSetVersionId, ct)));
        var existing = review.Findings.Where(f => f.Fingerprint is not null).Select(f => f.Fingerprint!).ToHashSet();
        var now = DateTime.UtcNow;
        // Re-running is idempotent: proposals already confirmed or rejected keep their disposition.
        foreach (var proposal in report.Findings.Where(p => !existing.Contains(p.Fingerprint)))
            review.Findings.Add(new()
            {
                Id = Guid.NewGuid(), ReviewId = review.Id, Source = "Automated", CheckCode = proposal.CheckCode, Fingerprint = proposal.Fingerprint,
                CriterionId = criteria.FirstOrDefault(c => evaluated.Contains(c.Id) && ReviewMapping.Strings(c.AutomatedChecksJson).Contains(proposal.CheckCode))?.Id,
                Title = proposal.Title, Description = proposal.Description, Severity = proposal.Severity, Recommendation = proposal.Recommendation,
                Evidence = proposal.Evidence, SubjectElementId = proposal.SubjectElementId, Status = ReviewVocabulary.Proposed, CreatedAt = now
            });
        review.ChecksRunAt = now;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = review.Id, Action = "ElementReviewChecksRun" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<ReviewDto>.Ok(await Dto(user, review, ct));
    }

    public async Task<ServiceResult<ReviewDto>> AddFindingAsync(CurrentUser user, Guid reviewId, FindingInput input, CancellationToken ct)
    {
        var errors = ReviewValidation.Finding(input.Title, input.Description, input.Severity, input.Recommendation, input.Evidence);
        var (review, failure) = await Editable(user, reviewId, input.Revision, ct);
        if (review is null) return failure!;
        if (input.CriterionId is { } criterion && review.Evaluations.All(e => e.CriterionId != criterion)) errors["criterionId"] = ["Link the finding to a criterion in this review, or leave it unlinked."];
        if (errors.Count > 0) return ServiceResult<ReviewDto>.Invalid(errors);
        var finding = new ReviewFinding
        {
            Id = Guid.NewGuid(), ReviewId = review.Id, CriterionId = input.CriterionId, Source = "Manual", Title = input.Title!.Trim(), Description = input.Description?.Trim() ?? "",
            Severity = input.Severity!, Recommendation = input.Recommendation?.Trim() ?? "", Evidence = input.Evidence!.Trim(), Status = ReviewVocabulary.Confirmed
        };
        review.Findings.Add(finding);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = finding.Id, Action = "ReviewFindingAdded" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<ReviewDto>.Ok(await Dto(user, review, ct));
    }

    public async Task<ServiceResult<ReviewDto>> UpdateFindingAsync(CurrentUser user, Guid findingId, FindingUpdateInput input, CancellationToken ct)
    {
        var reviewId = await db.ReviewFindings.Where(f => f.Id == findingId).Select(f => (Guid?)f.ReviewId).SingleOrDefaultAsync(ct);
        if (reviewId is null) return ServiceResult<ReviewDto>.NotFound();
        var (review, failure) = await Editable(user, reviewId.Value, input.Revision, ct);
        if (review is null) return failure!;
        var finding = review.Findings.Single(f => f.Id == findingId);
        if (input.Status is not (null or ReviewVocabulary.Confirmed or ReviewVocabulary.Rejected)) return ServiceResult<ReviewDto>.Invalid("status", "Choose Confirmed or Rejected.");
        string title = input.Title ?? finding.Title, description = input.Description ?? finding.Description, severity = input.Severity ?? finding.Severity,
            recommendation = input.Recommendation ?? finding.Recommendation, evidence = input.Evidence ?? finding.Evidence;
        var errors = ReviewValidation.Finding(title, description, severity, recommendation, evidence);
        if (errors.Count > 0) return ServiceResult<ReviewDto>.Invalid(errors);
        var statusChanged = input.Status is not null && input.Status != finding.Status;
        finding.Title = title.Trim(); finding.Description = description.Trim(); finding.Severity = severity; finding.Recommendation = recommendation.Trim(); finding.Evidence = evidence.Trim();
        finding.Status = input.Status ?? finding.Status; finding.UpdatedAt = DateTime.UtcNow;
        if (statusChanged) db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = finding.Id, Action = "ReviewFinding" + finding.Status });
        await db.SaveChangesAsync(ct);
        return ServiceResult<ReviewDto>.Ok(await Dto(user, review, ct));
    }

    public async Task<ServiceResult<ReviewDto>> PublishAsync(CurrentUser user, Guid reviewId, PublishReviewInput input, CancellationToken ct)
    {
        if ((input.Summary?.Length ?? 0) > 4000) return ServiceResult<ReviewDto>.Invalid("summary", "Summary is limited to 4,000 characters.");
        var (review, failure) = await Editable(user, reviewId, input.Revision, ct);
        if (review is null) return failure!;
        if (review.Findings.Any(f => f.Status == ReviewVocabulary.Proposed)) return ServiceResult<ReviewDto>.Invalid("findings", "Confirm or reject each proposed finding before publishing.");
        if (review.Evaluations.All(e => e.Outcome == ReviewVocabulary.NotReviewed)) return ServiceResult<ReviewDto>.Invalid("evaluations", "Rate at least one criterion, or mark it not applicable, before publishing.");
        review.Status = ReviewVocabulary.Published; review.PublishedAt = DateTime.UtcNow; review.Summary = input.Summary?.Trim() ?? "";
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = review.Id, Action = "ElementReviewPublished" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<ReviewDto>.Ok(await Dto(user, review, ct));
    }
}
