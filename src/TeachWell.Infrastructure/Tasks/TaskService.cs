using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Tasks;

public sealed class TaskService(AppDbContext db) : ITaskService
{
    private const string Stale = "This task changed since you opened it. Reload it and try again.";
    private const string NotAvailable = "This transition is not available from the task's current status.";
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);
    // PostgreSQL's timestamptz columns require a UTC-kind DateTime; a client that sent an unspecified-kind value is treated as already UTC rather than rejected.
    private static DateTime? AsUtc(DateTime? value) => value is { } v ? v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));

    // Drafts are visible only to CTL reviewers with course access; the assignee sees a task once it leaves Draft.
    private async Task<(RevisionTask? Task, bool IsReviewerAuthority, bool IsAssignee)> Visible(CurrentUser user, Guid taskId, bool tracking, CancellationToken ct)
    {
        var tasks = tracking ? db.RevisionTasks : db.RevisionTasks.AsNoTracking();
        var task = await tasks.AsSplitQuery().Include(t => t.TaskFindings).Include(t => t.Comments).Include(t => t.Events)
            .SingleOrDefaultAsync(t => t.Id == taskId && t.TenantId == user.TenantId, ct);
        if (task is null) return (null, false, false);
        var hasCourseAccess = await Accessible(user).AnyAsync(c => c.Id == task.CourseId, ct);
        if (!hasCourseAccess) return (null, false, false);
        var isReviewerAuthority = IsReviewer(user);
        var isAssignee = task.AssigneeId == user.Id;
        if (!isReviewerAuthority && !isAssignee) return (null, false, false);
        if (isAssignee && !isReviewerAuthority && task.Status == TaskVocabulary.Draft) return (null, false, false);
        return (task, isReviewerAuthority, isAssignee);
    }

    public async Task<ServiceResult<IReadOnlyList<TaskSummaryDto>>> ListForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Accessible(user).SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return ServiceResult<IReadOnlyList<TaskSummaryDto>>.NotFound();
        var isOwner = course.OwnerId == user.Id;
        var rows = await db.RevisionTasks.AsNoTracking().Where(t => t.CourseId == courseId && (!isOwner || TaskVocabulary.AssigneeVisible.Contains(t.Status)))
            .Include(t => t.TaskFindings).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<TaskSummaryDto>>.Ok(await ToSummariesAsync(rows, user, ct));
    }

    public async Task<IReadOnlyList<TaskSummaryDto>> ListMineAsync(CurrentUser user, string? status, CancellationToken ct)
    {
        var rows = await db.RevisionTasks.AsNoTracking().Where(t => t.TenantId == user.TenantId && (t.AssigneeId == user.Id || t.ReviewerId == user.Id))
            .Include(t => t.TaskFindings).ToListAsync(ct);
        var visible = rows.Where(t => t.ReviewerId == user.Id || TaskVocabulary.AssigneeVisible.Contains(t.Status));
        if (!string.IsNullOrEmpty(status)) visible = visible.Where(t => t.Status == status);
        return await ToSummariesAsync(visible.ToList(), user, ct);
    }

    private async Task<IReadOnlyList<TaskSummaryDto>> ToSummariesAsync(List<RevisionTask> rows, CurrentUser user, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var elementIds = rows.Select(t => t.ElementId).Distinct().ToList();
        var elements = await db.Elements.AsNoTracking().Where(e => elementIds.Contains(e.Id)).Select(e => new { e.Id, e.Type, e.Title }).ToDictionaryAsync(e => e.Id, ct);
        var assigneeIds = rows.Select(t => t.AssigneeId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => assigneeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var latestByElement = await db.ElementSnapshots.AsNoTracking().Where(s => elementIds.Contains(s.ElementId)).GroupBy(s => s.ElementId)
            .Select(g => new { ElementId = g.Key, LatestId = g.OrderByDescending(s => s.FetchedAt).Select(s => s.Id).First() }).ToDictionaryAsync(g => g.ElementId, g => g.LatestId, ct);
        return rows.OrderByDescending(t => t.CreatedAt).Select(t => new TaskSummaryDto(t.Id, t.ElementId, elements[t.ElementId].Type, elements[t.ElementId].Title, t.Title, t.Priority,
            t.Status, names.GetValueOrDefault(t.AssigneeId, "Unknown"), t.DueAt, t.CreatedAt, t.TaskFindings.Count, latestByElement[t.ElementId] != t.SourceSnapshotId,
            t.AssigneeId == user.Id || t.ReviewerId == user.Id)).ToList();
    }

    public async Task<ServiceResult<TaskDto>> GetAsync(CurrentUser user, Guid taskId, CancellationToken ct)
    {
        var (task, isReviewerAuthority, isAssignee) = await Visible(user, taskId, false, ct);
        return task is null ? ServiceResult<TaskDto>.NotFound() : ServiceResult<TaskDto>.Ok(await Dto(task, user, isReviewerAuthority, isAssignee, ct));
    }

    public async Task<ServiceResult<TaskDto>> CreateAsync(CurrentUser user, CreateTaskInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<TaskDto>.NotFound();
        var course = await Accessible(user).SingleOrDefaultAsync(c => c.Id == input.CourseId, ct);
        if (course is null) return ServiceResult<TaskDto>.NotFound();
        var errors = TaskValidation.Create(input.Title, input.Instructions, input.Priority, input.FindingIds);
        var findings = input.FindingIds is not { Length: > 0 } ? [] : await (from f in db.ReviewFindings
            join r in db.Reviews on f.ReviewId equals r.Id
            where input.FindingIds.Contains(f.Id) && r.CourseId == input.CourseId && f.Status == ReviewVocabulary.Confirmed && r.Status == ReviewVocabulary.Published
            select new { f.Id, r.ElementId }).ToListAsync(ct);
        if (input.FindingIds is { Length: > 0 } && findings.Count != input.FindingIds.Length)
            errors["findingIds"] = ["Every finding must be Confirmed, from a Published review, and in this course."];
        var elementIds = findings.Select(f => f.ElementId).Distinct().ToList();
        if (elementIds.Count > 1) errors["findingIds"] = ["All findings in one task must belong to the same page or assignment."];
        if (errors.Count > 0) return ServiceResult<TaskDto>.Invalid(errors);
        var elementId = elementIds[0];
        var openFinding = await db.RevisionTaskFindings.Where(tf => findings.Select(f => f.Id).Contains(tf.FindingId))
            .Join(db.RevisionTasks, tf => tf.TaskId, t => t.Id, (tf, t) => t).AnyAsync(t => !TaskVocabulary.Terminal.Contains(t.Status), ct);
        if (openFinding) return ServiceResult<TaskDto>.Invalid("findingIds", "One or more findings are already tracked by an open revision task.");
        var sourceSnapshotId = await db.ElementSnapshots.AsNoTracking().Where(s => s.ElementId == elementId).OrderByDescending(s => s.FetchedAt).Select(s => s.Id).FirstAsync(ct);
        var task = new RevisionTask
        {
            Id = Guid.NewGuid(), TenantId = user.TenantId, CourseId = input.CourseId, ElementId = elementId, Title = input.Title!.Trim(),
            Instructions = input.Instructions?.Trim() ?? "", AssigneeId = course.OwnerId, ReviewerId = user.Id, Priority = input.Priority!, DueAt = AsUtc(input.DueAt),
            SourceSnapshotId = sourceSnapshotId
        };
        task.TaskFindings = findings.Select(f => new RevisionTaskFinding { TaskId = task.Id, FindingId = f.Id }).ToList();
        task.Events.Add(TaskEvent.Create(task.Id, user.Id, null, TaskVocabulary.Draft, null));
        db.RevisionTasks.Add(task);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = task.Id, SubjectUserId = course.OwnerId, Action = "RevisionTaskCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<TaskDto>.Ok(await Dto(task, user, true, false, ct));
    }

    public async Task<ServiceResult<TaskDto>> TransitionAsync(CurrentUser user, Guid taskId, TaskTransitionInput input, CancellationToken ct)
    {
        var (task, isReviewerAuthority, isAssignee) = await Visible(user, taskId, true, ct);
        if (task is null) return ServiceResult<TaskDto>.NotFound();
        if (task.Revision != input.Revision) return ServiceResult<TaskDto>.Conflict(Stale);
        if (TaskVocabulary.Terminal.Contains(task.Status)) return ServiceResult<TaskDto>.Conflict($"This task is already {task.Status} and cannot change further.");
        var from = task.Status;
        string to; bool reasonRequired;
        if (input.ToStatus == TaskVocabulary.ResumeAction)
        {
            if (from != TaskVocabulary.Blocked) return ServiceResult<TaskDto>.Invalid("toStatus", NotAvailable);
            to = task.BlockedFromStatus!; reasonRequired = false;
        }
        else if (input.ToStatus == TaskVocabulary.Blocked)
        {
            if (!TaskVocabulary.Blockable.Contains(from)) return ServiceResult<TaskDto>.Invalid("toStatus", NotAvailable);
            to = TaskVocabulary.Blocked; reasonRequired = true;
        }
        else
        {
            var edge = TaskVocabulary.From(from).FirstOrDefault(t => t.To == input.ToStatus);
            if (edge is null) return ServiceResult<TaskDto>.Invalid("toStatus", NotAvailable);
            var allowed = edge.Actor switch { TaskVocabulary.Creator => isReviewerAuthority, TaskVocabulary.Assignee => isAssignee, _ => isReviewerAuthority || isAssignee };
            if (!allowed) return ServiceResult<TaskDto>.Invalid("toStatus", "Only the course's CTL reviewer or the assigned instructor may make this change.");
            to = edge.To; reasonRequired = edge.ReasonRequired;
        }
        var errors = TaskValidation.Transition(input.ToStatus, input.Reason, reasonRequired);
        if (errors.Count > 0) return ServiceResult<TaskDto>.Invalid(errors);
        db.Entry(task).Property(t => t.Revision).OriginalValue = input.Revision;
        task.Revision = Guid.NewGuid();
        task.BlockedFromStatus = to == TaskVocabulary.Blocked ? from : null;
        task.Status = to;
        // Course-structure tasks re-capture the structure first, so the reviewer compares against what the course looks like now.
        if (to == TaskVocabulary.ReadyForReview && await db.Elements.AnyAsync(e => e.Id == task.ElementId && e.Type == ReviewVocabulary.CourseType, ct))
        {
            var courseElement = await db.Elements.Include(e => e.Snapshots).SingleAsync(e => e.Id == task.ElementId, ct);
            await CourseStructureCapture.CaptureAsync(db, courseElement, ct);
            await db.SaveChangesAsync(ct);
        }
        if (to == TaskVocabulary.ReadyForReview) task.ReadyForReviewSnapshotId = await db.ElementSnapshots.AsNoTracking()
            .Where(s => s.ElementId == task.ElementId).OrderByDescending(s => s.FetchedAt).Select(s => s.Id).FirstAsync(ct);
        if (to == TaskVocabulary.Resolved) task.ResolvedAt = DateTime.UtcNow;
        task.Events.Add(TaskEvent.Create(task.Id, user.Id, from, to, input.Reason?.Trim()));
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = task.Id, Action = "RevisionTask" + to });
        await db.SaveChangesAsync(ct);
        return ServiceResult<TaskDto>.Ok(await Dto(task, user, isReviewerAuthority, isAssignee, ct));
    }

    public async Task<ServiceResult<TaskDto>> CommentAsync(CurrentUser user, Guid taskId, TaskCommentInput input, CancellationToken ct)
    {
        var errors = TaskValidation.Comment(input.Body);
        if (errors.Count > 0) return ServiceResult<TaskDto>.Invalid(errors);
        var (task, isReviewerAuthority, isAssignee) = await Visible(user, taskId, true, ct);
        if (task is null) return ServiceResult<TaskDto>.NotFound();
        task.Comments.Add(new() { Id = Guid.NewGuid(), TaskId = task.Id, AuthorId = user.Id, Body = input.Body!.Trim() });
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = task.Id, Action = "RevisionTaskCommentAdded" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<TaskDto>.Ok(await Dto(task, user, isReviewerAuthority, isAssignee, ct));
    }

    public async Task<ServiceResult<ComparisonDto>> GetComparisonAsync(CurrentUser user, Guid taskId, CancellationToken ct)
    {
        var (task, _, _) = await Visible(user, taskId, false, ct);
        if (task is null) return ServiceResult<ComparisonDto>.NotFound();
        var source = await db.ElementSnapshots.AsNoTracking().SingleAsync(s => s.Id == task.SourceSnapshotId, ct);
        var latest = await db.ElementSnapshots.AsNoTracking().Where(s => s.ElementId == task.ElementId).OrderByDescending(s => s.FetchedAt).FirstAsync(ct);
        var changes = new List<string>();
        if (source.Title != latest.Title) changes.Add($"Title changed from \"{source.Title}\" to \"{latest.Title}\".");
        if (source.SourceStatus != latest.SourceStatus) changes.Add($"Status changed from {source.SourceStatus} to {latest.SourceStatus}.");
        if (source.SourceUrl != latest.SourceUrl) changes.Add("Source link changed.");
        return ServiceResult<ComparisonDto>.Ok(new(ToSnapshotDto(source), ToSnapshotDto(latest), source.Id != latest.Id, TextDiff.Compute(source.PlainText, latest.PlainText), changes));
    }

    public async Task<ServiceResult<TaskDto>> SimulateUpdateAsync(CurrentUser user, Guid taskId, SimulateUpdateInput input, CancellationToken ct)
    {
        if ((input.PlainText?.Length ?? 0) is 0 or > 10000) return ServiceResult<TaskDto>.Invalid("plainText", "Enter the updated text (1–10,000 characters).");
        var (task, isReviewerAuthority, isAssignee) = await Visible(user, taskId, true, ct);
        if (task is null) return ServiceResult<TaskDto>.NotFound();
        if (!isAssignee) return ServiceResult<TaskDto>.NotFound();
        if (task.Revision != input.Revision) return ServiceResult<TaskDto>.Conflict(Stale);
        if (TaskVocabulary.Terminal.Contains(task.Status)) return ServiceResult<TaskDto>.Conflict($"This task is already {task.Status} and cannot change further.");
        var course = await db.Courses.AsNoTracking().Where(c => c.Id == task.CourseId).Select(c => c.SourceType).SingleAsync(ct);
        if (course != "Demo")
            return ServiceResult<TaskDto>.Invalid("course", "Simulated updates are available only for fictional demo courses. Re-import this course from Canvas to capture a real edit.");
        var element = await db.Elements.Include(e => e.Snapshots).SingleAsync(e => e.Id == task.ElementId, ct);
        if (element.Type == ReviewVocabulary.CourseType)
            return ServiceResult<TaskDto>.Invalid("element", "Course structure is captured from the course's modules and items, not edited as text. Change those, then submit the task for review.");
        // A text-only simulated edit carries no sanitized HTML, so prior structural evidence no longer describes the content; checks will report it as unknown rather than reuse stale evidence.
        element.PlainText = input.PlainText!.Trim(); element.EvidenceJson = null; element.PreviewHtml = null;
        element.Snapshots.Add(ElementSnapshot.Capture(element, DateTime.UtcNow));
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = element.Id, Action = "DemoContentSimulatedUpdate" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<TaskDto>.Ok(await Dto(task, user, isReviewerAuthority, isAssignee, ct));
    }

    private static SnapshotDto ToSnapshotDto(ElementSnapshot s)
    {
        var evidence = ContentEvidence.Parse(s.EvidenceJson);
        return new(s.Id, s.FetchedAt, s.ContentHash, s.Title, s.PlainText, s.SourceUrl, s.SourceStatus, evidence is not null,
            evidence?.Images.Count ?? 0, evidence?.Headings.Count ?? 0, evidence?.Links.Count ?? 0, evidence?.Tables.Count ?? 0);
    }

    private static readonly Dictionary<(string From, string To), string> Labels = new()
    {
        [(TaskVocabulary.Draft, TaskVocabulary.NeedsRevision)] = "Assign to instructor",
        [(TaskVocabulary.Draft, TaskVocabulary.Withdrawn)] = "Withdraw draft",
        [(TaskVocabulary.NeedsRevision, TaskVocabulary.InProgress)] = "Start work",
        [(TaskVocabulary.NeedsRevision, TaskVocabulary.Withdrawn)] = "Withdraw task",
        [(TaskVocabulary.NeedsRevision, TaskVocabulary.NotApplicable)] = "Mark not applicable",
        [(TaskVocabulary.InProgress, TaskVocabulary.ReadyForReview)] = "Submit for review",
        [(TaskVocabulary.InProgress, TaskVocabulary.Withdrawn)] = "Withdraw task",
        [(TaskVocabulary.InProgress, TaskVocabulary.NotApplicable)] = "Mark not applicable",
        [(TaskVocabulary.ReadyForReview, TaskVocabulary.Resolved)] = "Resolve task",
        [(TaskVocabulary.ReadyForReview, TaskVocabulary.ChangesRequested)] = "Request changes",
        [(TaskVocabulary.ChangesRequested, TaskVocabulary.InProgress)] = "Resume work",
        [(TaskVocabulary.ChangesRequested, TaskVocabulary.ReadyForReview)] = "Resubmit for review",
        [(TaskVocabulary.ChangesRequested, TaskVocabulary.Withdrawn)] = "Withdraw task",
        [(TaskVocabulary.ChangesRequested, TaskVocabulary.NotApplicable)] = "Mark not applicable",
    };

    private async Task<TaskDto> Dto(RevisionTask task, CurrentUser user, bool isReviewerAuthority, bool isAssignee, CancellationToken ct)
    {
        var course = await db.Courses.AsNoTracking().Where(c => c.Id == task.CourseId).Select(c => new { c.Title, c.SourceType }).SingleAsync(ct);
        var element = await db.Elements.AsNoTracking().SingleAsync(e => e.Id == task.ElementId, ct);
        var source = await db.ElementSnapshots.AsNoTracking().SingleAsync(s => s.Id == task.SourceSnapshotId, ct);
        var currentSnapshotId = await db.ElementSnapshots.AsNoTracking().Where(s => s.ElementId == task.ElementId).OrderByDescending(s => s.FetchedAt).Select(s => s.Id).FirstAsync(ct);
        var names = await db.Users.AsNoTracking().Where(u => u.Id == task.AssigneeId || u.Id == task.ReviewerId || task.Comments.Select(c => c.AuthorId).Contains(u.Id) ||
            task.Events.Select(e => e.ActorId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var findingIds = task.TaskFindings.Select(f => f.FindingId).ToList();
        var findings = await (from f in db.ReviewFindings.AsNoTracking()
            where findingIds.Contains(f.Id)
            join c in db.RubricCriteria.AsNoTracking() on f.CriterionId equals c.Id into criteria
            from criterion in criteria.DefaultIfEmpty()
            select new TaskFindingSummaryDto(f.Id, f.ReviewId, criterion == null ? null : criterion.Code, f.Title, f.Severity, f.Recommendation, f.Evidence)).ToListAsync(ct);
        var actions = new List<TaskActionDto>();
        if (!TaskVocabulary.Terminal.Contains(task.Status))
        {
            if (task.Status == TaskVocabulary.Blocked)
            {
                if (isReviewerAuthority || isAssignee) actions.Add(new(TaskVocabulary.ResumeAction, $"Resume ({task.BlockedFromStatus})", false));
            }
            else
            {
                foreach (var edge in TaskVocabulary.From(task.Status))
                {
                    var allowed = edge.Actor switch { TaskVocabulary.Creator => isReviewerAuthority, TaskVocabulary.Assignee => isAssignee, _ => isReviewerAuthority || isAssignee };
                    if (allowed) actions.Add(new(edge.To, Labels.GetValueOrDefault((task.Status, edge.To), edge.To), edge.ReasonRequired));
                }
                if ((isReviewerAuthority || isAssignee) && TaskVocabulary.Blockable.Contains(task.Status)) actions.Add(new(TaskVocabulary.Blocked, "Mark blocked", true));
            }
        }
        return new(task.Id, task.CourseId, course.Title, task.ElementId, element.Type, element.Title, element.SourceUrl, task.Title, task.Instructions, task.Priority,
            task.Status, task.BlockedFromStatus, task.Revision, task.AssigneeId, names.GetValueOrDefault(task.AssigneeId, "Unknown"), names.GetValueOrDefault(task.ReviewerId, "Unknown"),
            task.CreatedAt, task.DueAt, task.ResolvedAt, ToSnapshotDto(source), currentSnapshotId, currentSnapshotId != task.SourceSnapshotId, course.SourceType == "Demo",
            findings, task.Comments.OrderBy(c => c.CreatedAt).Select(c => new TaskCommentDto(c.Id, names.GetValueOrDefault(c.AuthorId, "Unknown"), c.Body, c.CreatedAt)).ToList(),
            task.Events.OrderBy(e => e.CreatedAt).Select(e => new TaskEventDto(e.Id, names.GetValueOrDefault(e.ActorId, "Unknown"), e.FromStatus, e.ToStatus, e.Reason, e.CreatedAt)).ToList(),
            actions, isReviewerAuthority || isAssignee);
    }
}
