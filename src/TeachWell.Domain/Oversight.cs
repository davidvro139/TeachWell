using System.Text.Json;

namespace TeachWell.Domain;

// Phase 11 (ADR 014): one row per course per UTC day with the course's improvement-guide figures, written whenever the guide is built.
// Oversight aggregates the latest row per course, and the rows over time show guide progress. Figures only; no content, no people.
public class CourseGuideRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CourseId { get; set; }
    public DateTime Day { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string StandardsName { get; set; } = "";
    public int? StandardsVersionNumber { get; set; }
    public int CheckedItems { get; set; }
    public int OpenSteps { get; set; }
    public int RequiredOpenSteps { get; set; }
    public int OpenPlaces { get; set; }
    public int DonePlaces { get; set; }
    public int NotApplicablePlaces { get; set; }
    // {aspect: open places} and {check code: open places}.
    public string OpenByAspectJson { get; set; } = "{}";
    public string OpenByCheckJson { get; set; } = "{}";

    public static Dictionary<string, int> Counts(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}

public static class OversightVocabulary
{
    // Readiness states a course can be in, for the oversight summary.
    public const string Endorsed = "Endorsed", RecheckDue = "RecheckDue", InReview = "InReview", Requested = "Requested",
        ChangesRequested = "ChangesRequested", NotRequested = "NotRequested";
    public static readonly string[] States = [Endorsed, RecheckDue, InReview, Requested, ChangesRequested, NotRequested];
    // Roles a SystemAdmin may grant or revoke in the app. SystemAdmin itself is granted only by deployment (ADR 001).
    public static readonly string[] GrantableRoles = [Roles.Instructor, Roles.Reviewer, Roles.AcademicAdmin];
}
