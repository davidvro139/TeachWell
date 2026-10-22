using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure;

// SystemAdmin role management (ADR 014). SystemAdmin is never granted here, so the app cannot escalate anyone to IT administration.
// Every change is audited, and the target's security stamp is renewed so their session picks up the change at the next validation.
public sealed class AdminUserService(UserManager<AppUser> users, AppDbContext db) : IAdminUserService
{
    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(CurrentUser user, CancellationToken ct)
    {
        var all = await users.Users.AsNoTracking().Where(u => u.TenantId == user.TenantId).OrderBy(u => u.DisplayName).ToListAsync(ct);
        var result = new List<AdminUserDto>();
        foreach (var u in all) result.Add(new(u.Id, u.DisplayName, u.Email!, [.. await users.GetRolesAsync(u)]));
        return result;
    }

    public async Task<ServiceResult<AdminUserDto>> ChangeRoleAsync(CurrentUser user, Guid userId, RoleChangeInput input, CancellationToken ct)
    {
        if (!user.Roles.Contains(Roles.Admin)) return ServiceResult<AdminUserDto>.NotFound();
        if (input.Role is null || !OversightVocabulary.GrantableRoles.Contains(input.Role))
            return ServiceResult<AdminUserDto>.Invalid("role", "Choose Instructor, CTLReviewer or AcademicAdmin.");
        var target = await users.Users.SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == user.TenantId, ct);
        if (target is null) return ServiceResult<AdminUserDto>.NotFound();
        var has = await users.IsInRoleAsync(target, input.Role);
        if (has != input.Granted)
        {
            var result = input.Granted ? await users.AddToRoleAsync(target, input.Role) : await users.RemoveFromRoleAsync(target, input.Role);
            if (!result.Succeeded) return ServiceResult<AdminUserDto>.Conflict(string.Join(" ", result.Errors.Select(e => e.Description)));
            await users.UpdateSecurityStampAsync(target);
            db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = target.Id, SubjectUserId = target.Id, Action = (input.Granted ? "RoleGranted:" : "RoleRevoked:") + input.Role });
            await db.SaveChangesAsync(ct);
        }
        return ServiceResult<AdminUserDto>.Ok(new(target.Id, target.DisplayName, target.Email!, [.. await users.GetRolesAsync(target)]));
    }
}
