using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure;

public sealed class CourseSharingService(AppDbContext db) : ICourseSharingService
{
    private Task<bool> IsOwner(CurrentUser actor, Guid id, CancellationToken ct) =>
        db.Courses.AnyAsync(c => c.Id == id && c.TenantId == actor.TenantId && c.OwnerId == actor.Id, ct);

    // A directory is available only in the context of a course the actor owns.
    private IQueryable<AppUser> EligibleReviewers(CurrentUser actor) =>
        from user in db.Users
        join assignment in db.UserRoles on user.Id equals assignment.UserId
        join role in db.Roles on assignment.RoleId equals role.Id
        where user.TenantId == actor.TenantId && user.IsActive && user.Id != actor.Id && role.Name == Roles.Reviewer
        select user;

    public async Task<IReadOnlyList<ReviewerOption>?> ListReviewersAsync(CurrentUser actor, Guid courseId, CancellationToken ct)
    {
        if (!await IsOwner(actor, courseId, ct)) return null;
        return await EligibleReviewers(actor).OrderBy(u => u.DisplayName).Select(u => new ReviewerOption(u.Id, u.DisplayName)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CourseShare>?> ListSharesAsync(CurrentUser actor, Guid courseId, CancellationToken ct)
    {
        if (!await IsOwner(actor, courseId, ct)) return null;
        return await (from access in db.CourseAccess
                      join user in db.Users on access.UserId equals user.Id
                      where access.CourseId == courseId && user.TenantId == actor.TenantId
                      orderby user.DisplayName
                      select new CourseShare(user.Id, user.DisplayName, access.Permission, access.GrantedAt)).ToListAsync(ct);
    }

    public async Task<SharingResult> GrantAsync(CurrentUser actor, Guid courseId, Guid reviewerId, CancellationToken ct)
    {
        if (!await IsOwner(actor, courseId, ct)) return SharingResult.NotFound;
        if (!await EligibleReviewers(actor).AnyAsync(u => u.Id == reviewerId, ct)) return SharingResult.InvalidReviewer;
        if (await db.CourseAccess.AnyAsync(a => a.CourseId == courseId && a.UserId == reviewerId, ct)) return SharingResult.Success;
        db.CourseAccess.Add(new() { CourseId = courseId, UserId = reviewerId, GrantedBy = actor.Id });
        db.AuditLogs.Add(new() { ActorId = actor.Id, EntityId = courseId, SubjectUserId = reviewerId, Action = "CourseReviewAccessGranted" });
        // One transaction commits the permission and its audit event together.
        await db.SaveChangesAsync(ct);
        return SharingResult.Success;
    }

    public async Task<SharingResult> RevokeAsync(CurrentUser actor, Guid courseId, Guid reviewerId, CancellationToken ct)
    {
        if (!await IsOwner(actor, courseId, ct)) return SharingResult.NotFound;
        var share = await db.CourseAccess.SingleOrDefaultAsync(a => a.CourseId == courseId && a.UserId == reviewerId, ct);
        if (share is null) return SharingResult.Success;
        db.CourseAccess.Remove(share);
        db.AuditLogs.Add(new() { ActorId = actor.Id, EntityId = courseId, SubjectUserId = reviewerId, Action = "CourseReviewAccessRevoked" });
        await db.SaveChangesAsync(ct);
        return SharingResult.Success;
    }
}
