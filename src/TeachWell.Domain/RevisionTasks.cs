namespace TeachWell.Domain;

public static class TaskVocabulary
{
    public const string Draft = "Draft", NeedsRevision = "NeedsRevision", InProgress = "InProgress", ReadyForReview = "ReadyForReview",
        ChangesRequested = "ChangesRequested", Blocked = "Blocked", Withdrawn = "Withdrawn", NotApplicable = "NotApplicable", Resolved = "Resolved";
    public static readonly string[] All = [Draft, NeedsRevision, InProgress, ReadyForReview, ChangesRequested, Blocked, Withdrawn, NotApplicable, Resolved];
    public static readonly string[] Terminal = [Withdrawn, NotApplicable, Resolved];
    // A task in Draft is being prepared by CTL and is not yet visible to the assignee.
    public static readonly string[] AssigneeVisible = [NeedsRevision, InProgress, ReadyForReview, ChangesRequested, Blocked, Withdrawn, NotApplicable, Resolved];
    public static readonly string[] Priorities = ReviewVocabulary.Severities;
    // A status any blockable state can enter, and the only state Resume (a non-status sentinel) leaves from.
    public static readonly string[] Blockable = [NeedsRevision, InProgress, ReadyForReview, ChangesRequested];
    public const string ResumeAction = "Resume";

    public const string Creator = "Creator", Assignee = "Assignee", Either = "Either";
    public sealed record Transition(string To, string Actor, bool ReasonRequired);
    // CTL authorization for "Creator" transitions is course-level (any CTLReviewer with course access), matching reviews and sharing.
    private static readonly Dictionary<string, Transition[]> Edges = new()
    {
        [Draft] = [new(NeedsRevision, Creator, false), new(Withdrawn, Creator, true)],
        [NeedsRevision] = [new(InProgress, Assignee, false), new(Withdrawn, Creator, true), new(NotApplicable, Creator, true)],
        [InProgress] = [new(ReadyForReview, Assignee, false), new(Withdrawn, Creator, true), new(NotApplicable, Creator, true)],
        [ReadyForReview] = [new(Resolved, Creator, false), new(ChangesRequested, Creator, true)],
        [ChangesRequested] = [new(InProgress, Assignee, false), new(ReadyForReview, Assignee, false), new(Withdrawn, Creator, true), new(NotApplicable, Creator, true)],
    };
    public static Transition[] From(string status) => Edges.TryGetValue(status, out var t) ? t : [];
}

public class RevisionTask
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CourseId { get; set; }
    public Guid ElementId { get; set; }
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public Guid AssigneeId { get; set; }
    public Guid ReviewerId { get; set; }
    public string Priority { get; set; } = "Medium";
    public DateTime? DueAt { get; set; }
    public string Status { get; set; } = TaskVocabulary.Draft;
    public string? BlockedFromStatus { get; set; }
    public Guid SourceSnapshotId { get; set; }
    public Guid? ReadyForReviewSnapshotId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public List<RevisionTaskFinding> TaskFindings { get; set; } = [];
    public List<TaskComment> Comments { get; set; } = [];
    public List<TaskEvent> Events { get; set; } = [];
}
public class RevisionTaskFinding
{
    public Guid TaskId { get; set; }
    public Guid FindingId { get; set; }
}
public class TaskComment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
// Append-only, like ElementSnapshot: a task's history must not be rewritten after the fact.
public sealed class TaskEvent
{
    private TaskEvent() { }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid ActorId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = "";
    public string? Reason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public static TaskEvent Create(Guid taskId, Guid actorId, string? fromStatus, string toStatus, string? reason) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, ActorId = actorId, FromStatus = fromStatus, ToStatus = toStatus, Reason = reason, CreatedAt = DateTime.UtcNow
    };
}

public sealed record DiffLine(string Kind, string Text); // Kind: Same, Added, Removed

// A minimal LCS-based line diff. No HTML is diffed; only plain text snapshots.
public static class TextDiff
{
    public static IReadOnlyList<DiffLine> Compute(string oldText, string newText)
    {
        var a = oldText.Length == 0 ? [] : oldText.Replace("\r\n", "\n").Split('\n');
        var b = newText.Length == 0 ? [] : newText.Replace("\r\n", "\n").Split('\n');
        var n = a.Length; var m = b.Length;
        var table = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
        var result = new List<DiffLine>(); int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { result.Add(new("Same", a[x])); x++; y++; }
            else if (table[x + 1, y] >= table[x, y + 1]) { result.Add(new("Removed", a[x])); x++; }
            else { result.Add(new("Added", b[y])); y++; }
        }
        while (x < n) { result.Add(new("Removed", a[x])); x++; }
        while (y < m) { result.Add(new("Added", b[y])); y++; }
        return result;
    }
}
