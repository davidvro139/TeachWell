using Microsoft.EntityFrameworkCore;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

// Each course's readiness state (ADR 013/014), shared by oversight and the course list badges. An open request wins; then the latest
// endorsement (re-check due under D2) unless changes were requested after it; then the last "changes requested"; else not requested.
public static class ReadinessStates
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [OversightVocabulary.Endorsed] = "Endorsed", [OversightVocabulary.RecheckDue] = "Endorsed · re-check due", [OversightVocabulary.InReview] = "In review",
        [OversightVocabulary.Requested] = "Requested", [OversightVocabulary.ChangesRequested] = "Changes requested", [OversightVocabulary.NotRequested] = "Not requested",
    };

    public sealed record Result(IReadOnlyDictionary<Guid, string> States, IReadOnlyList<Endorsement> Endorsements, Func<Endorsement, bool> RecheckDue);

    public static async Task<Result> ForCoursesAsync(AppDbContext db, IReadOnlyCollection<Guid> courseIds, int changeThresholdPercent, CancellationToken ct)
    {
        var ids = courseIds.ToList();
        var now = DateTime.UtcNow;
        var requests = await db.ReadinessRequests.AsNoTracking().Where(r => ids.Contains(r.CourseId))
            .Select(r => new { r.CourseId, r.Status, r.DecidedAt }).ToListAsync(ct);
        var endorsements = await db.Endorsements.AsNoTracking().Where(e => ids.Contains(e.CourseId)).ToListAsync(ct);
        var latest = endorsements.GroupBy(e => e.CourseId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.EndorsedAt)!);
        // Current snapshots are only needed for courses with an endorsement (the substantial-change rule).
        var current = await CurrentSnapshotsAsync(db, latest.Keys.ToList(), ct);
        bool Due(Endorsement e) => ReadinessRecheck.Reasons(e, current.GetValueOrDefault(e.CourseId) ?? [], now, changeThresholdPercent).Count > 0;

        var states = new Dictionary<Guid, string>();
        foreach (var id in ids)
        {
            var mine = requests.Where(r => r.CourseId == id).ToList();
            var lastChanges = mine.Where(r => r.Status == ReadinessVocabulary.ChangesRequested).MaxBy(r => r.DecidedAt);
            states[id] = mine.FirstOrDefault(r => ReadinessVocabulary.Active.Contains(r.Status)) is { } active ? active.Status
                : latest.TryGetValue(id, out var e) && (lastChanges is null || lastChanges.DecidedAt < e.EndorsedAt) ? (Due(e) ? OversightVocabulary.RecheckDue : OversightVocabulary.Endorsed)
                : lastChanges is not null ? OversightVocabulary.ChangesRequested : OversightVocabulary.NotRequested;
        }
        return new(states, endorsements, Due);
    }

    public static async Task<Dictionary<Guid, Dictionary<Guid, Guid>>> CurrentSnapshotsAsync(AppDbContext db, List<Guid> courseIds, CancellationToken ct)
    {
        if (courseIds.Count == 0) return [];
        var elements = await db.Elements.AsNoTracking().Where(e => courseIds.Contains(e.CourseId) && e.Type != ReviewVocabulary.CourseType && e.SourceStatus != "Removed")
            .Select(e => new { e.Id, e.CourseId }).ToListAsync(ct);
        var elementIds = elements.Select(e => e.Id).ToList();
        var latest = await db.ElementSnapshots.AsNoTracking().Where(s => elementIds.Contains(s.ElementId)).GroupBy(s => s.ElementId)
            .Select(g => new { ElementId = g.Key, Id = g.OrderByDescending(s => s.FetchedAt).Select(s => s.Id).First() }).ToDictionaryAsync(x => x.ElementId, x => x.Id, ct);
        return elements.Where(e => latest.ContainsKey(e.Id)).GroupBy(e => e.CourseId).ToDictionary(g => g.Key, g => g.ToDictionary(e => e.Id, e => latest[e.Id]));
    }
}
