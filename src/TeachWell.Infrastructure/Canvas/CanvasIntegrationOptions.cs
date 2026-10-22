using Microsoft.Extensions.Configuration;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CanvasIntegrationOptions
{
    public bool Enabled { get; }
    public string[] AllowedHosts { get; }
    private readonly string? encryptionKey;

    public CanvasIntegrationOptions(IConfiguration config)
    {
        Enabled = config.GetValue<bool>("Features:CanvasPAT");
        AllowedHosts = config.GetSection("Canvas:AllowedHosts").Get<string[]>() ?? [];
        AllowedHosts = AllowedHosts.Select(h => h.Trim().ToLowerInvariant()).Distinct().ToArray();
        encryptionKey = config["Canvas:EncryptionKey"];
        if (!Enabled) return;
        if (AllowedHosts.Length == 0 || AllowedHosts.Any(h => Uri.CheckHostName(h) != UriHostNameType.Dns || h.Contains('*') || h.EndsWith('.') || h.Length > 253))
            throw new InvalidOperationException("Canvas requires an explicit list of DNS hostnames without wildcards, URLs or IP addresses.");
        _ = GetKey();
    }

    public byte[] GetKey()
    {
        byte[] key;
        try { key = Convert.FromBase64String(encryptionKey ?? ""); }
        catch (FormatException) { throw new InvalidOperationException("Canvas:EncryptionKey must be a deployment-managed base64-encoded 32-byte key."); }
        if (key.Length != 32) throw new InvalidOperationException("Canvas:EncryptionKey must be a deployment-managed base64-encoded 32-byte key.");
        return key;
    }
}
