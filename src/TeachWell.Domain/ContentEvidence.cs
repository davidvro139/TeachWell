using System.Text.Json;

namespace TeachWell.Domain;

// Structural facts extracted from sanitized HTML at capture time. Contains no markup, scripts or full URLs.
public sealed record ContentEvidence(
    int Version,
    IReadOnlyList<ImageEvidence> Images,
    IReadOnlyList<HeadingEvidence> Headings,
    IReadOnlyList<LinkEvidence> Links,
    IReadOnlyList<TableEvidence> Tables,
    AssignmentEvidence? Assignment = null,
    bool Truncated = false)
{
    public const int CurrentVersion = 1;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string Serialize() => JsonSerializer.Serialize(this, Json);
    public static ContentEvidence? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        // Course-structure snapshots also store JSON evidence; anything without page-evidence lists is not page evidence.
        try { return JsonSerializer.Deserialize<ContentEvidence>(json, Json) is { Images: not null, Headings: not null, Links: not null, Tables: not null } e ? e : null; }
        catch (JsonException) { return null; }
    }
}
public sealed record ImageEvidence(int Index, string? Alt, bool Decorative);
public sealed record HeadingEvidence(int Index, int Level, string Text);
// Destination is a kind (Internal, External, Email, Anchor, Invalid); only an external host name is kept.
public sealed record LinkEvidence(int Index, string Text, string Destination, string? Host = null);
public sealed record TableEvidence(int Index, bool HasHeaderCells, int Rows);
// DueAt (UTC) was added with ADR 011 slice 3; evidence captured earlier has HasDueDate but no DueAt.
public sealed record AssignmentEvidence(bool? HasRubric, decimal? PointsPossible, bool? HasDueDate, DateTime? DueAt = null);
