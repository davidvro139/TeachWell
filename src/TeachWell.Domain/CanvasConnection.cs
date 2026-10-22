namespace TeachWell.Domain;

public class CanvasConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid OwnerId { get; set; }
    public string BaseUrl { get; set; } = "";
    public string EncryptedToken { get; set; } = "";
    public string? CanvasUserId { get; set; }
    public string Status { get; set; } = "Connected";
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid Revision { get; set; } = Guid.NewGuid();
}
