using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure;

// SystemAdmin audit viewer (ADR 014 amendment). The audit table has no tenant column, so an entry belongs to a tenant when its actor is a
// user of that tenant, or when it is a system entry (actor Guid.Empty) recorded against the tenant itself. Read-only; newest first.
public sealed class AuditService(AppDbContext db) : IAuditService
{
    public const int PageSize = 100;
    public const string SystemName = "TeachWell (automatic)";

    public async Task<ServiceResult<AuditPageDto>> ListAsync(CurrentUser user, AuditQuery query, CancellationToken ct)
    {
        if (!user.Roles.Contains(Roles.Admin)) return ServiceResult<AuditPageDto>.NotFound();
        if (query.From is { } f && query.To is { } t && f > t) return ServiceResult<AuditPageDto>.Invalid("from", "The start date must be on or before the end date.");
        var people = db.Users.Where(u => u.TenantId == user.TenantId).Select(u => u.Id);
        var scoped = db.AuditLogs.AsNoTracking().Where(a => people.Contains(a.ActorId) || (a.ActorId == Guid.Empty && a.EntityId == user.TenantId));
        var actions = await scoped.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);

        var filtered = scoped;
        if (!string.IsNullOrWhiteSpace(query.Action)) filtered = filtered.Where(a => a.Action == query.Action);
        if (query.ActorId is { } actor) filtered = filtered.Where(a => a.ActorId == actor);
        if (query.From is { } from) filtered = filtered.Where(a => a.Timestamp >= DateTime.SpecifyKind(from.Date, DateTimeKind.Utc));
        if (query.To is { } to) filtered = filtered.Where(a => a.Timestamp < DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc));
        if (query.Before is { } before) filtered = filtered.Where(a => a.Timestamp < DateTime.SpecifyKind(before, DateTimeKind.Utc));
        var page = await filtered.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id).Take(PageSize + 1).ToListAsync(ct);
        var more = page.Count > PageSize;
        page = page.Take(PageSize).ToList();

        // Readable labels for the people and courses an entry refers to; anything else shows its id only.
        var userIds = page.Select(a => a.ActorId).Concat(page.Where(a => a.SubjectUserId is not null).Select(a => a.SubjectUserId!.Value)).Concat(page.Select(a => a.EntityId)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.TenantId == user.TenantId).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var entityIds = page.Select(a => a.EntityId).Distinct().ToList();
        var courses = await db.Courses.AsNoTracking().Where(c => entityIds.Contains(c.Id) && c.TenantId == user.TenantId).ToDictionaryAsync(c => c.Id, c => $"{c.Code} · {c.Title}", ct);
        string? Label(Guid id) => courses.TryGetValue(id, out var c) ? c : names.TryGetValue(id, out var n) ? n : id == user.TenantId ? "This college" : null;

        var items = page.Select(a => new AuditEntryDto(a.Id, a.Timestamp, a.ActorId, a.ActorId == Guid.Empty ? SystemName : names.GetValueOrDefault(a.ActorId, "Unknown person"),
            a.Action, a.EntityId, Label(a.EntityId), a.SubjectUserId is { } s ? names.GetValueOrDefault(s, "Unknown person") : null)).ToList();
        return ServiceResult<AuditPageDto>.Ok(new(items, more ? page[^1].Timestamp : null, actions));
    }
}
