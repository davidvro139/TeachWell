namespace TeachWell.Domain;

// In-app notifications (ADR 013 amendment): one row per recipient, written in the same save as the action that caused it.
// No email is sent; the app has no mail service. Text is short and contains no course content beyond its code and title.
public class Notification
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? CourseId { get; set; }
    public Guid? ReadinessRequestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

public static class NotificationVocabulary
{
    public const string ReadinessRequested = "ReadinessRequested", ReadinessTaken = "ReadinessTaken", ReadinessEndorsed = "ReadinessEndorsed",
        ReadinessChangesRequested = "ReadinessChangesRequested", ReadinessWithdrawn = "ReadinessWithdrawn";
    public const int MaxTitleLength = 200, MaxBodyLength = 500, ListLimit = 50;

    public static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
