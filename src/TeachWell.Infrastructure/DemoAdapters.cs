using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;
namespace TeachWell.Infrastructure;

public class DemoIdentityProvider(UserManager<AppUser> users) : IIdentityProvider
{
    public async Task<CurrentUser?> GetUserAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        return user is null || !user.IsActive ? null : new(user.Id, user.TenantId, user.DisplayName, user.Email!, (await users.GetRolesAsync(user)).ToArray());
    }
}
public class StoredCourseContentSource(AppDbContext db, Reviews.ReadinessOptions? readiness = null) : ICourseContentSource
{
    public async Task<IReadOnlyList<CourseSummary>> ListCoursesAsync(CurrentUser user, string role, CancellationToken ct)
    {
        var query = db.Courses.AsNoTracking().Where(c => c.TenantId == user.TenantId);
        query = role == Roles.Instructor ? query.Where(c => c.OwnerId == user.Id) : query.Where(c => c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id && a.Permission == "Review"));
        var courses = await query.OrderBy(c => c.Code).Select(c => new CourseSummary(c.Id, c.Title, c.Code, c.SourceType, c.Elements.Count(e => e.Type != ReviewVocabulary.CourseType), c.OwnerId == user.Id ? "Manage" : "Review", "", "")).ToListAsync(ct);
        var states = (await Reviews.ReadinessStates.ForCoursesAsync(db, courses.Select(c => c.Id).ToList(), (readiness ?? new()).ChangeThresholdPercent, ct)).States;
        return courses.Select(c => c with { Readiness = states[c.Id], ReadinessLabel = Reviews.ReadinessStates.Labels[states[c.Id]] }).ToList();
    }
    public Task<Course?> GetCourseAsync(CurrentUser user, Guid id, CancellationToken ct) => db.Courses.AsNoTracking()
        .AsSplitQuery().Include(c => c.Elements).ThenInclude(e => e.Snapshots.OrderByDescending(s => s.FetchedAt).Take(1)).Include(c => c.Access).FirstOrDefaultAsync(c => c.Id == id && c.TenantId == user.TenantId &&
            (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)), ct);
}
