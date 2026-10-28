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
    bool Truncated = false,
    // Version 2 (ROADMAP item 5a). Null means media was not captured, which is not the same as a page having no media:
    // snapshots taken before version 2 report a limitation rather than passing the media checks silently.
    IReadOnlyList<MediaEvidence>? Media = null)
{
    public const int CurrentVersion = 2;
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
// A video or audio player on the page (ROADMAP item 5a). Captured from markup that is then stripped and never rendered,
// so only facts are kept: no src, no markup, and an external host name at most, exactly as LinkEvidence does.
//
// Kind is Video or Audio for an HTML player whose markup states what it is, Embed for an iframe or object recognised
// as a media player, and Tool for one that is not.
//
// HasCaptionTrack is true or false only when the markup can settle it — an HTML player either carries a captions or
// subtitles <track> or it does not. For an Embed it is null: whether a YouTube or Canvas Studio video is captioned is
// a property of the hosted media, not of the page, and guessing from the URL would be wrong.
public sealed record MediaEvidence(int Index, string Kind, string? Provider, string? Title, bool? HasCaptionTrack);

public static class MediaKinds
{
    public const string Video = "Video", Audio = "Audio", Embed = "Embed";
    // An embedded iframe or object that is not recognised as a media player: an LTI launch (this college reaches its
    // syllabi through Simple Syllabus that way, see ADR 008), a document viewer, a form or a widget. Recorded so a
    // reviewer knows something on the page was not inspected, but never asked about captions: most are not media at
    // all, and a captions step on every embedded tool would be noise that teaches instructors to ignore the guide.
    public const string Tool = "Tool";
    // Kinds that are actually video or audio, as opposed to an uninspected embedded tool.
    public static bool IsPlayer(string kind) => kind is Video or Audio or Embed;
}
// DueAt (UTC) was added with ADR 011 slice 3; evidence captured earlier has HasDueDate but no DueAt.
public sealed record AssignmentEvidence(bool? HasRubric, decimal? PointsPossible, bool? HasDueDate, DateTime? DueAt = null);
