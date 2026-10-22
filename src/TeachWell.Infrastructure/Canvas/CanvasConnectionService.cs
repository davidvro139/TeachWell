using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CanvasConnectionService(AppDbContext db, ICanvasClient client, ICanvasTokenProtector protector) : ICanvasConnectionService
{
    private IQueryable<CanvasConnection> Owned(CurrentUser user) => db.CanvasConnections.Where(c => c.TenantId == user.TenantId && c.OwnerId == user.Id);
    private static CanvasConnectionDto Summary(CanvasConnection c) => new(c.Id, c.BaseUrl,
        c.Status != "Disconnected" && c.ExpiresAt <= DateTime.UtcNow ? "Expired" : c.Status,
        c.CanvasUserId, c.EncryptedToken.Length == 0 ? null : "Saved token", c.ExpiresAt, c.LastValidatedAt, c.LastCheckedAt, c.ErrorCode);
    private void Audit(CurrentUser user, CanvasConnection c, string action) => db.AuditLogs.Add(new()
    {
        ActorId = user.Id, SubjectUserId = user.Id, EntityId = c.Id, Action = action
    });

    public async Task<IReadOnlyList<CanvasConnectionDto>> ListAsync(CurrentUser user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await Owned(user).AsNoTracking().OrderBy(c => c.CreatedAt).Select(c => new CanvasConnectionDto(
            c.Id, c.BaseUrl, c.Status != "Disconnected" && c.ExpiresAt <= now ? "Expired" : c.Status, c.CanvasUserId,
            c.EncryptedToken == "" ? null : "Saved token", c.ExpiresAt, c.LastValidatedAt, c.LastCheckedAt, c.ErrorCode)).ToListAsync(ct);
    }

    public async Task<CanvasConnectionResult> ConnectAsync(CurrentUser user, Uri origin, string token, DateTime? expiresAt, CancellationToken ct)
    {
        var existing = await Owned(user).SingleOrDefaultAsync(c => c.BaseUrl == origin.AbsoluteUri, ct);
        var c = existing ?? new CanvasConnection { TenantId = user.TenantId, OwnerId = user.Id, BaseUrl = origin.AbsoluteUri };
        var validation = await client.ValidateConnectionAsync(origin, token, ct);
        if (validation.Error != CanvasError.None)
        {
            Audit(user, c, "CanvasConnectionRejected:" + validation.Error);
            await db.SaveChangesAsync(ct);
            return new(null, validation.Error);
        }
        c.EncryptedToken = protector.Protect(token, c.TenantId, c.OwnerId, c.Id);
        c.CanvasUserId = validation.CanvasUserId;
        c.Status = "Connected"; c.ErrorCode = null; c.ExpiresAt = expiresAt;
        c.LastCheckedAt = c.LastValidatedAt = DateTime.UtcNow;
        c.Revision = Guid.NewGuid();
        if (existing is null) db.CanvasConnections.Add(c);
        Audit(user, c, existing is null ? "CanvasConnected" : "CanvasCredentialReplaced");
        await db.SaveChangesAsync(ct);
        return new(Summary(c));
    }

    public async Task<CanvasConnectionResult> ValidateAsync(CurrentUser user, Guid id, CancellationToken ct)
    {
        var c = await Owned(user).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (c is null) return new(null, NotFound: true);
        if (c.Status == "Disconnected") return new(Summary(c), CanvasError.Disconnected);
        CanvasValidation result;
        if (c.ExpiresAt <= DateTime.UtcNow) result = new(null, CanvasError.Expired);
        else if (c.EncryptedToken.Length == 0) result = new(null, CanvasError.InvalidToken);
        else
        {
            string? token = null;
            try { token = protector.Unprotect(c.EncryptedToken, c.TenantId, c.OwnerId, c.Id); }
            catch (Exception e) when (e is CryptographicException or FormatException) { /* Redact crypto diagnostics; never log ciphertext. */ }
            result = token is null ? new(null, CanvasError.TokenUnreadable) : await client.ValidateConnectionAsync(new Uri(c.BaseUrl), token, ct);
        }
        c.LastCheckedAt = DateTime.UtcNow;
        c.ErrorCode = result.Error == CanvasError.None ? null : result.Error.ToString();
        c.Status = result.Error switch
        {
            CanvasError.None => "Connected",
            CanvasError.InvalidToken => "Invalid",
            CanvasError.Expired => "Expired",
            CanvasError.PermissionDenied => "AccessDenied",
            _ => "Unavailable"
        };
        if (result.Error == CanvasError.None) { c.LastValidatedAt = c.LastCheckedAt; c.CanvasUserId = result.CanvasUserId; }
        if (result.Error is CanvasError.InvalidToken or CanvasError.Expired) c.EncryptedToken = "";
        c.Revision = Guid.NewGuid();
        Audit(user, c, result.Error == CanvasError.None ? "CanvasValidated" : "CanvasValidationFailed:" + result.Error);
        await db.SaveChangesAsync(ct);
        return new(Summary(c), result.Error);
    }

    public async Task<bool> DisconnectAsync(CurrentUser user, Guid id, CancellationToken ct)
    {
        var c = await Owned(user).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (c is null) return false;
        if (c.Status == "Disconnected") return true;
        c.EncryptedToken = ""; c.Status = "Disconnected"; c.ErrorCode = null; c.CanvasUserId = null;
        c.Revision = Guid.NewGuid();
        Audit(user, c, "CanvasDisconnected");
        await db.SaveChangesAsync(ct);
        return true;
    }
}
