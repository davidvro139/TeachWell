using System.Security.Cryptography;
using System.Text;
using TeachWell.Application;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CanvasTokenProtector(CanvasIntegrationOptions options) : ICanvasTokenProtector
{
    private static byte[] Context(Guid tenant, Guid owner, Guid connection) => Encoding.UTF8.GetBytes($"TeachWell.CanvasPAT.v1:{tenant:D}:{owner:D}:{connection:D}");

    public string Protect(string token, Guid tenantId, Guid ownerId, Guid connectionId)
    {
        var key = options.GetKey();
        var plaintext = Encoding.UTF8.GetBytes(token);
        try
        {
            var result = new byte[1 + 12 + 16 + plaintext.Length];
            result[0] = 1;
            RandomNumberGenerator.Fill(result.AsSpan(1, 12));
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(1, 12), plaintext, result.AsSpan(29), result.AsSpan(13, 16), Context(tenantId, ownerId, connectionId));
            return Convert.ToBase64String(result);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }

    public string Unprotect(string ciphertext, Guid tenantId, Guid ownerId, Guid connectionId)
    {
        var data = Convert.FromBase64String(ciphertext);
        if (data.Length < 30 || data[0] != 1) throw new CryptographicException("Unsupported encrypted credential.");
        var key = options.GetKey();
        var plaintext = new byte[data.Length - 29];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(data.AsSpan(1, 12), data.AsSpan(29), data.AsSpan(13, 16), plaintext, Context(tenantId, ownerId, connectionId));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }
}
