using System.Text.Json;

namespace TeachWell.Domain;

// Phase 10 (ADR 013): the instructor asks the CTL for a readiness review; the CTL endorses the course against a named standard-set
// version or requests changes. Endorsements are dated and re-checked, never silently revoked (D2).
public static class ReadinessVocabulary
{
    public const string Requested = "Requested", InReview = "InReview", Endorsed = "Endorsed", ChangesRequested = "ChangesRequested", Withdrawn = "Withdrawn";
    public static readonly string[] Active = [Requested, InReview];
    public static readonly string[] Decided = [Endorsed, ChangesRequested];
    public const int MaxNoteLength = 2000, MaxSummaryLength = 4000;
}

public class ReadinessRequest
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CourseId { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = "";
    // Pinned at request time (ADR 013 §2).
    public Guid? StandardSetVersionId { get; set; }
    public string StandardsName { get; set; } = "";
    public int? StandardsVersionNumber { get; set; }
    public DateTime? ContentAsOf { get; set; }
    public string SnapshotIdsJson { get; set; } = "{}";
    public string GuideSummaryJson { get; set; } = "{}";
    public string Status { get; set; } = ReadinessVocabulary.Requested;
    public Guid? ReviewerId { get; set; }
    public DateTime? TakenAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string DecisionSummary { get; set; } = "";
    public DateTime? WithdrawnAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

// Immutable once written: what was endorsed, against which standards, by whom, and until when it is considered current.
public class Endorsement
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CourseId { get; set; }
    public Guid RequestId { get; set; }
    public Guid? StandardSetVersionId { get; set; }
    public string StandardsName { get; set; } = "";
    public int? StandardsVersionNumber { get; set; }
    public DateTime? ContentAsOf { get; set; }
    public string SnapshotIdsJson { get; set; } = "{}";
    public Guid ReviewerId { get; set; }
    public DateTime EndorsedAt { get; set; } = DateTime.UtcNow;
    public DateTime ValidUntil { get; set; }
    public string Summary { get; set; } = "";

    public string Wording => $"CTL-endorsed against {StandardsName}{(StandardsVersionNumber is { } n ? $" v{n}" : "")} on {EndorsedAt:MMMM d, yyyy}";
}

// The instructor's guide progress when they asked, so the CTL sees the course as the instructor saw it.
public sealed record ReadinessReason(string Check, string Where, string Reason);
public sealed record ReadinessGuideSummary(int OpenSteps, int RequiredOpenSteps, int OpenPlaces, int DonePlaces, int NotApplicablePlaces, IReadOnlyList<ReadinessReason> Reasons)
{
    public static readonly ReadinessGuideSummary Empty = new(0, 0, 0, 0, 0, []);
    public string Serialize() => JsonSerializer.Serialize(this, ContentEvidence.Json);
    public static ReadinessGuideSummary Parse(string json)
    {
        try { return JsonSerializer.Deserialize<ReadinessGuideSummary>(json, ContentEvidence.Json) is { Reasons: not null } s ? s : Empty; }
        catch (JsonException) { return Empty; }
    }
}

public static class ReadinessRecheck
{
    public static Dictionary<Guid, Guid> ParseSnapshots(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    // How many of the course's items are new, removed or changed compared with the pinned snapshots, out of all items either side.
    public static (int Changed, int Total) Compare(IReadOnlyDictionary<Guid, Guid> pinned, IReadOnlyDictionary<Guid, Guid> current)
    {
        var all = pinned.Keys.Union(current.Keys).ToList();
        var changed = all.Count(id => !pinned.TryGetValue(id, out var p) || !current.TryGetValue(id, out var c) || p != c);
        return (changed, all.Count);
    }

    // D2: re-check is due when the validity period has passed or the course changed substantially. Never a revocation.
    public static List<string> Reasons(Endorsement e, IReadOnlyDictionary<Guid, Guid> current, DateTime now, int thresholdPercent)
    {
        var reasons = new List<string>();
        if (now >= e.ValidUntil) reasons.Add($"The endorsement's validity period ended on {e.ValidUntil:MMMM d, yyyy}.");
        var (changed, total) = Compare(ParseSnapshots(e.SnapshotIdsJson), current);
        if (total > 0 && changed * 100 >= thresholdPercent * total)
            reasons.Add($"The course has changed substantially since it was endorsed: {changed} of {total} items are new, removed or changed.");
        return reasons;
    }
}
