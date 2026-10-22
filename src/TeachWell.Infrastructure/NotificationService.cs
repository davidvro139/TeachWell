using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure;

// Each person sees only their own notifications (404 for anyone else's).
public sealed class NotificationService(AppDbContext db) : INotificationService
{
    public async Task<NotificationListDto> ListAsync(CurrentUser user, CancellationToken ct)
    {
        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == user.Id && n.TenantId == user.TenantId);
        var items = await mine.OrderByDescending(n => n.CreatedAt).Take(NotificationVocabulary.ListLimit)
            .Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.CourseId, n.CreatedAt, n.ReadAt != null)).ToListAsync(ct);
        return new(await mine.CountAsync(n => n.ReadAt == null, ct), items);
    }

    public async Task<ServiceResult<NotificationListDto>> MarkReadAsync(CurrentUser user, Guid? notificationId, CancellationToken ct)
    {
        var unread = db.Notifications.Where(n => n.UserId == user.Id && n.TenantId == user.TenantId && n.ReadAt == null);
        if (notificationId is { } id)
        {
            if (!await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == user.Id, ct)) return ServiceResult<NotificationListDto>.NotFound();
            unread = unread.Where(n => n.Id == id);
        }
        var now = DateTime.UtcNow;
        foreach (var n in await unread.ToListAsync(ct)) n.ReadAt = now;
        await db.SaveChangesAsync(ct);
        return ServiceResult<NotificationListDto>.Ok(await ListAsync(user, ct));
    }

    // Adds rows to the caller's unit of work; the caller saves them with the action that caused them.
    public static void Add(AppDbContext db, Guid tenantId, IEnumerable<Guid> recipients, string kind, string title, string body, Guid? courseId, Guid? requestId)
    {
        foreach (var userId in recipients.Distinct())
            db.Notifications.Add(new()
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Kind = kind, CourseId = courseId, ReadinessRequestId = requestId,
                Title = NotificationVocabulary.Clip(title, NotificationVocabulary.MaxTitleLength), Body = NotificationVocabulary.Clip(body, NotificationVocabulary.MaxBodyLength)
            });
    }

    public static Task<List<Guid>> UsersInRoleAsync(AppDbContext db, Guid tenantId, string role, CancellationToken ct) =>
        (from ur in db.UserRoles
         join r in db.Roles on ur.RoleId equals r.Id
         join u in db.Users on ur.UserId equals u.Id
         where r.Name == role && u.TenantId == tenantId && u.IsActive
         select u.Id).ToListAsync(ct);
}
