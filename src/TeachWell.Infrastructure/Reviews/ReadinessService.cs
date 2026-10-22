using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

// Phase 10 (ADR 013): readiness requests, the CTL queue, decisions and endorsements.
public sealed class ReadinessService(AppDbContext db, IGuideService guide, ReadinessOptions? options = null) : IReadinessService
{
    private readonly ReadinessOptions options = options ?? new ReadinessOptions();
    private const string Stale = "This request changed since you opened it. Reload and try again.";
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);
    private static bool IsOwner(CurrentUser user, Course course) => course.OwnerId == user.Id && user.Roles.Contains(Roles.Instructor);
    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));

    public async Task<ServiceResult<CourseReadinessDto>> GetForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        return course is null ? ServiceResult<CourseReadinessDto>.NotFound() : ServiceResult<CourseReadinessDto>.Ok(await CourseDto(user, course, ct));
    }

    public async Task<ServiceResult<CourseReadinessDto>> RequestAsync(CurrentUser user, Guid courseId, ReadinessRequestInput input, CancellationToken ct)
    {
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null || !IsOwner(user, course)) return ServiceResult<CourseReadinessDto>.NotFound();
        var note = input.Note?.Trim() ?? "";
        if (note.Length > ReadinessVocabulary.MaxNoteLength) return ServiceResult<CourseReadinessDto>.Invalid("note", $"Keep the note to {ReadinessVocabulary.MaxNoteLength:N0} characters.");
        if (await db.ReadinessRequests.AnyAsync(r => r.CourseId == courseId && ReadinessVocabulary.Active.Contains(r.Status), ct))
            return ServiceResult<CourseReadinessDto>.Conflict("A readiness review is already requested for this course.");
        var current = (await guide.GetAsync(user, courseId, ct)).Value!;
        var required = current.Steps.Where(s => s.Required && s.OpenPlaces > 0).ToList();
        if (required.Count > 0)
            return ServiceResult<CourseReadinessDto>.Invalid("guide", $"Resolve or justify the required steps in the improvement guide first: {string.Join(", ", required.Select(s => s.Name))}.");
        var summary = Summarize(current);
        var request = new ReadinessRequest
        {
            Id = Guid.NewGuid(), TenantId = course.TenantId, CourseId = courseId, RequestedBy = user.Id, Note = note,
            StandardSetVersionId = current.Standards.VersionId, StandardsName = current.Standards.Name, StandardsVersionNumber = current.Standards.VersionNumber,
            ContentAsOf = current.ContentAsOf, SnapshotIdsJson = System.Text.Json.JsonSerializer.Serialize(await CurrentSnapshotsAsync(courseId, ct)),
            GuideSummaryJson = summary.Serialize()
        };
        db.ReadinessRequests.Add(request);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = request.Id, Action = "ReadinessRequested" });
        var reviewers = (await NotificationService.UsersInRoleAsync(db, course.TenantId, Roles.Reviewer, ct)).Where(id => id != user.Id);
        NotificationService.Add(db, course.TenantId, reviewers, NotificationVocabulary.ReadinessRequested, $"Readiness review requested: {course.Code}",
            $"{user.DisplayName} asked the CTL for a readiness review of {course.Code} · {course.Title}. Take it from the Readiness queue.", course.Id, request.Id);
        await db.SaveChangesAsync(ct);
        return ServiceResult<CourseReadinessDto>.Ok(await CourseDto(user, course, ct));
    }

    public async Task<ServiceResult<CourseReadinessDto>> WithdrawAsync(CurrentUser user, Guid requestId, ReadinessRevisionInput input, CancellationToken ct)
    {
        var request = await db.ReadinessRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.TenantId == user.TenantId, ct);
        var course = request is null ? null : await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CourseId, ct);
        if (request is null || course is null || !IsOwner(user, course)) return ServiceResult<CourseReadinessDto>.NotFound();
        if (request.Revision != input.Revision) return ServiceResult<CourseReadinessDto>.Conflict(Stale);
        if (!ReadinessVocabulary.Active.Contains(request.Status)) return ServiceResult<CourseReadinessDto>.Conflict("Only an open request can be withdrawn.");
        if (request.ReviewerId is { } reviewer)
            NotificationService.Add(db, course.TenantId, [reviewer], NotificationVocabulary.ReadinessWithdrawn, $"Readiness request withdrawn: {course.Code}",
                $"{user.DisplayName} withdrew the readiness request for {course.Code} · {course.Title}.", course.Id, request.Id);
        request.Status = ReadinessVocabulary.Withdrawn; request.WithdrawnAt = DateTime.UtcNow; request.Revision = Guid.NewGuid();
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = request.Id, Action = "ReadinessWithdrawn" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<CourseReadinessDto>.Ok(await CourseDto(user, course, ct));
    }

    // Tenant-wide, like support triage: open requests for everyone, plus what this reviewer decided recently. Guide detail needs course access.
    public async Task<IReadOnlyList<ReadinessRequestDto>> QueueAsync(CurrentUser user, CancellationToken ct)
    {
        var requests = await db.ReadinessRequests.AsNoTracking()
            .Where(r => r.TenantId == user.TenantId && (ReadinessVocabulary.Active.Contains(r.Status) || r.ReviewerId == user.Id && ReadinessVocabulary.Decided.Contains(r.Status)))
            .OrderBy(r => r.RequestedAt).ToListAsync(ct);
        var active = requests.Where(r => ReadinessVocabulary.Active.Contains(r.Status));
        var decided = requests.Where(r => !ReadinessVocabulary.Active.Contains(r.Status)).OrderByDescending(r => r.DecidedAt).Take(20);
        return await Dtos(user, [.. active, .. decided], ct);
    }

    public async Task<ServiceResult<ReadinessRequestDto>> TakeAsync(CurrentUser user, Guid requestId, ReadinessRevisionInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<ReadinessRequestDto>.NotFound();
        var request = await db.ReadinessRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.TenantId == user.TenantId, ct);
        if (request is null) return ServiceResult<ReadinessRequestDto>.NotFound();
        if (request.Revision != input.Revision) return ServiceResult<ReadinessRequestDto>.Conflict(Stale);
        if (request.Status != ReadinessVocabulary.Requested) return ServiceResult<ReadinessRequestDto>.Conflict("This request has already been taken or closed.");
        if (request.RequestedBy == user.Id) return ServiceResult<ReadinessRequestDto>.Conflict("You cannot review your own readiness request.");
        request.Status = ReadinessVocabulary.InReview; request.ReviewerId = user.Id; request.TakenAt = DateTime.UtcNow; request.Revision = Guid.NewGuid();
        var taken = await db.Courses.AsNoTracking().Where(c => c.Id == request.CourseId).Select(c => new { c.Code, c.Title }).SingleAsync(ct);
        NotificationService.Add(db, request.TenantId, [request.RequestedBy], NotificationVocabulary.ReadinessTaken, $"{taken.Code} is in review",
            $"{user.DisplayName} from the CTL is reviewing {taken.Code} · {taken.Title} for readiness.", request.CourseId, request.Id);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = request.Id, Action = "ReadinessTaken" });
        // ADR 013 §3: asking for a readiness review shares the course with the reviewer who takes it.
        if (!await db.CourseAccess.AnyAsync(a => a.CourseId == request.CourseId && a.UserId == user.Id, ct))
        {
            db.CourseAccess.Add(new() { CourseId = request.CourseId, UserId = user.Id, GrantedBy = request.RequestedBy });
            db.AuditLogs.Add(new() { ActorId = request.RequestedBy, EntityId = request.CourseId, SubjectUserId = user.Id, Action = "CourseSharedForReadinessReview" });
        }
        await db.SaveChangesAsync(ct);
        return ServiceResult<ReadinessRequestDto>.Ok((await Dtos(user, [request], ct))[0]);
    }

    public async Task<ServiceResult<CourseReadinessDto>> DecideAsync(CurrentUser user, Guid requestId, ReadinessDecisionInput input, CancellationToken ct)
    {
        var request = await db.ReadinessRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.TenantId == user.TenantId, ct);
        var course = request is null ? null : await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CourseId, ct);
        if (request is null || course is null || !IsReviewer(user) || request.ReviewerId != user.Id) return ServiceResult<CourseReadinessDto>.NotFound();
        if (request.Revision != input.Revision) return ServiceResult<CourseReadinessDto>.Conflict(Stale);
        if (request.Status != ReadinessVocabulary.InReview) return ServiceResult<CourseReadinessDto>.Conflict("Only a request in review can be decided.");
        var summary = input.Summary?.Trim() ?? "";
        if (input.Decision is not (ReadinessVocabulary.Endorsed or ReadinessVocabulary.ChangesRequested))
            return ServiceResult<CourseReadinessDto>.Invalid("decision", "Choose Endorsed or ChangesRequested.");
        if (summary.Length is 0 or > ReadinessVocabulary.MaxSummaryLength)
            return ServiceResult<CourseReadinessDto>.Invalid("summary", input.Decision == ReadinessVocabulary.Endorsed
                ? "Summarize what the endorsement rests on (1–4,000 characters)." : "Say what needs to change (1–4,000 characters).");
        var now = DateTime.UtcNow;
        if (input.Decision == ReadinessVocabulary.Endorsed)
        {
            var unmet = (await RequirementsAsync(user, request, ct)).Where(r => !r.Met).ToList();
            if (unmet.Count > 0)
                return ServiceResult<CourseReadinessDto>.Invalid("reviews", "Before endorsing: " + string.Join(" ", unmet.Select(r => $"{r.Label} ({r.Detail}).")));
            db.Endorsements.Add(new()
            {
                Id = Guid.NewGuid(), TenantId = request.TenantId, CourseId = request.CourseId, RequestId = request.Id, StandardSetVersionId = request.StandardSetVersionId,
                StandardsName = request.StandardsName, StandardsVersionNumber = request.StandardsVersionNumber, ContentAsOf = request.ContentAsOf,
                SnapshotIdsJson = request.SnapshotIdsJson, ReviewerId = user.Id, EndorsedAt = now, ValidUntil = now.AddMonths(options.ValidityMonths), Summary = summary
            });
        }
        request.Status = input.Decision; request.DecidedAt = now; request.DecisionSummary = summary; request.Revision = Guid.NewGuid();
        var endorsed = input.Decision == ReadinessVocabulary.Endorsed;
        NotificationService.Add(db, request.TenantId, [request.RequestedBy],
            endorsed ? NotificationVocabulary.ReadinessEndorsed : NotificationVocabulary.ReadinessChangesRequested,
            endorsed ? $"{course.Code} was endorsed by the CTL" : $"Changes requested for {course.Code}",
            $"{user.DisplayName}: {summary}", course.Id, request.Id);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = request.Id, Action = "Readiness" + input.Decision });
        await db.SaveChangesAsync(ct);
        return ServiceResult<CourseReadinessDto>.Ok(await CourseDto(user, course, ct));
    }

    // The instructor's guide progress at request time (ADR 013 §2).
    internal static ReadinessGuideSummary Summarize(CourseGuideDto guide)
    {
        var places = guide.Steps.SelectMany(s => s.Locations.Select(l => (Step: s, Place: l))).ToList();
        return new(guide.Steps.Count(s => s.OpenPlaces > 0), guide.Steps.Count(s => s.Required && s.OpenPlaces > 0), places.Count(p => p.Place.Status == GuideVocabulary.Open),
            places.Count(p => p.Place.Status == GuideVocabulary.Done), places.Count(p => p.Place.Status == GuideVocabulary.NotApplicable),
            places.Where(p => p.Place.Status == GuideVocabulary.NotApplicable).Select(p => new ReadinessReason(p.Step.Name, p.Place.Where, p.Place.Reason ?? "")).ToList());
    }

    // Endorsement conditions from the standard-set version pinned on the request. Reviews count when published after the request; the
    // deciding reviewer must always have published one of them.
    private async Task<List<ReadinessRequirementDto>> RequirementsAsync(CurrentUser user, ReadinessRequest request, CancellationToken ct)
    {
        var json = request.StandardSetVersionId is { } versionId
            ? await db.StandardSetVersions.AsNoTracking().Where(v => v.Id == versionId).Select(v => v.EndorsementJson).SingleOrDefaultAsync(ct) : null;
        var needed = EndorsementRequirements.Parse(json);
        var reviews = await (from r in db.Reviews.AsNoTracking()
                             join e in db.Elements.AsNoTracking() on r.ElementId equals e.Id
                             join v in db.RubricVersions.AsNoTracking() on r.RubricVersionId equals v.Id
                             where r.CourseId == request.CourseId && r.Status == ReviewVocabulary.Published && r.PublishedAt >= request.RequestedAt
                             select new { r.ReviewerId, r.ElementId, e.Type, v.ProfileId }).ToListAsync(ct);
        var rubric = needed.RubricProfileId is { } profileId
            ? await db.RubricProfiles.AsNoTracking().Where(p => p.Id == profileId).Select(p => p.Name).SingleOrDefaultAsync(ct) ?? "the chosen rubric" : null;
        var counted = reviews.Where(r => needed.RubricProfileId is null || r.ProfileId == needed.RubricProfileId).ToList();
        var using_ = rubric is null ? "" : $" using {rubric}";
        var mine = counted.Count(r => r.ReviewerId == user.Id);
        var result = new List<ReadinessRequirementDto>
        {
            new($"You have published a review of this course{using_} since the request", mine > 0, mine == 0 ? "none yet" : $"{mine} published"),
        };
        if (needed.CourseReview)
        {
            var course = counted.Count(r => r.Type == ReviewVocabulary.CourseType);
            result.Add(new($"A course-level review{using_}", course > 0, course == 0 ? "none yet" : $"{course} published"));
        }
        if (needed.MinimumItemReviews > 0)
        {
            var items = counted.Where(r => r.Type != ReviewVocabulary.CourseType).Select(r => r.ElementId).Distinct().Count();
            result.Add(new($"Reviews of at least {needed.MinimumItemReviews} {(needed.MinimumItemReviews == 1 ? "item" : "different items")} (pages, assignments, quizzes or discussions){using_}",
                items >= needed.MinimumItemReviews, $"{items} so far"));
        }
        return result;
    }

    private async Task<Dictionary<Guid, Guid>> CurrentSnapshotsAsync(Guid courseId, CancellationToken ct)
    {
        var ids = await db.Elements.AsNoTracking().Where(e => e.CourseId == courseId && e.Type != ReviewVocabulary.CourseType && e.SourceStatus != "Removed")
            .Select(e => e.Id).ToListAsync(ct);
        return await db.ElementSnapshots.AsNoTracking().Where(s => ids.Contains(s.ElementId)).GroupBy(s => s.ElementId)
            .Select(g => new { ElementId = g.Key, Id = g.OrderByDescending(s => s.FetchedAt).Select(s => s.Id).First() }).ToDictionaryAsync(x => x.ElementId, x => x.Id, ct);
    }

    private async Task<CourseReadinessDto> CourseDto(CurrentUser user, Course course, CancellationToken ct)
    {
        var owner = IsOwner(user, course);
        var requests = await db.ReadinessRequests.AsNoTracking().Where(r => r.CourseId == course.Id).OrderByDescending(r => r.RequestedAt).ToListAsync(ct);
        var dtos = await Dtos(user, requests, ct);
        var active = dtos.FirstOrDefault(r => ReadinessVocabulary.Active.Contains(r.Status));
        var lastDecision = dtos.FirstOrDefault(r => ReadinessVocabulary.Decided.Contains(r.Status));

        var blockers = new List<string>();
        var requiredOpen = 0;
        if (owner)
        {
            var current = (await guide.GetAsync(user, course.Id, ct)).Value!;
            var required = current.Steps.Where(s => s.Required && s.OpenPlaces > 0).ToList();
            requiredOpen = required.Count;
            if (active is not null) blockers.Add("A readiness review is already requested.");
            if (required.Count > 0) blockers.Add($"Resolve or justify the required steps in the improvement guide first: {string.Join(", ", required.Select(s => s.Name))}.");
        }

        EndorsementDto? endorsement = null;
        var latest = await db.Endorsements.AsNoTracking().Where(e => e.CourseId == course.Id).OrderByDescending(e => e.EndorsedAt).FirstOrDefaultAsync(ct);
        if (latest is not null)
        {
            var reasons = ReadinessRecheck.Reasons(latest, await CurrentSnapshotsAsync(course.Id, ct), DateTime.UtcNow, options.ChangeThresholdPercent);
            var reviewer = await db.Users.AsNoTracking().Where(u => u.Id == latest.ReviewerId).Select(u => u.DisplayName).SingleAsync(ct);
            endorsement = new(latest.Id, latest.Wording, latest.StandardsName, latest.StandardsVersionNumber, latest.ContentAsOf, reviewer, latest.EndorsedAt,
                latest.ValidUntil, latest.Summary, reasons.Count > 0, reasons);
        }
        var activeEntity = requests.FirstOrDefault(r => ReadinessVocabulary.Active.Contains(r.Status));
        var canDecide = activeEntity is { Status: ReadinessVocabulary.InReview } && activeEntity.ReviewerId == user.Id && IsReviewer(user);
        var requirements = canDecide ? await RequirementsAsync(user, activeEntity!, ct) : [];
        return new(course.Id, owner, owner && blockers.Count == 0, blockers, requiredOpen, active, lastDecision, endorsement, dtos, canDecide,
            canDecide && requirements.All(r => r.Met), requirements);
    }

    private async Task<List<ReadinessRequestDto>> Dtos(CurrentUser user, IReadOnlyList<ReadinessRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0) return [];
        var courseIds = requests.Select(r => r.CourseId).Distinct().ToList();
        var courses = await db.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => (c.Code, c.Title), ct);
        var visible = (await Accessible(user).Where(c => courseIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        var userIds = requests.Select(r => r.RequestedBy).Concat(requests.Where(r => r.ReviewerId is not null).Select(r => r.ReviewerId!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return requests.Select(r => new ReadinessRequestDto(r.Id, r.CourseId, courses[r.CourseId].Code, courses[r.CourseId].Title, names.GetValueOrDefault(r.RequestedBy, "Unknown"),
            r.RequestedAt, r.Note, r.Status, r.StandardsName, r.StandardsVersionNumber, r.ContentAsOf, r.ReviewerId is { } rid ? names.GetValueOrDefault(rid) : null,
            r.ReviewerId == user.Id, r.TakenAt, r.DecidedAt, r.DecisionSummary, r.Revision,
            visible.Contains(r.CourseId) ? ReadinessGuideSummary.Parse(r.GuideSummaryJson) : null)).ToList();
    }
}
