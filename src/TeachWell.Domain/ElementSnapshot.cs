using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeachWell.Domain;

public sealed class ElementSnapshot
{
    private ElementSnapshot() { }
    public Guid Id { get; private set; }
    public Guid ElementId { get; private set; }
    public DateTime FetchedAt { get; private set; }
    public string ContentHash { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string PlainText { get; private set; } = "";
    public string? SourceUrl { get; private set; }
    public string SourceStatus { get; private set; } = "";
    public string MetadataJson { get; private set; } = "";
    public string? EvidenceJson { get; private set; }
    public string? PreviewHtml { get; private set; }

    public static ElementSnapshot Capture(CourseElement element, DateTime fetchedAt)
    {
        var metadata = JsonSerializer.Serialize(new { element.Type, element.SourceElementId, element.ParentElementId, element.TargetElementId, element.Position, element.SourceWarning });
        // Evidence and preview join the hash only when present, so snapshots captured before either existed keep their hashes.
        var hashInput = (element.EvidenceJson, element.PreviewHtml) switch
        {
            (null, null) => JsonSerializer.Serialize(new { element.Title, element.PlainText, element.SourceUrl, element.SourceStatus, metadata }),
            (_, null) => JsonSerializer.Serialize(new { element.Title, element.PlainText, element.SourceUrl, element.SourceStatus, metadata, evidence = element.EvidenceJson }),
            _ => JsonSerializer.Serialize(new { element.Title, element.PlainText, element.SourceUrl, element.SourceStatus, metadata, evidence = element.EvidenceJson, preview = element.PreviewHtml })
        };
        return new()
        {
            Id = Guid.NewGuid(), ElementId = element.Id, FetchedAt = fetchedAt,
            Title = element.Title, PlainText = element.PlainText, SourceUrl = element.SourceUrl,
            SourceStatus = element.SourceStatus, MetadataJson = metadata, EvidenceJson = element.EvidenceJson, PreviewHtml = element.PreviewHtml,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant()
        };
    }
}
