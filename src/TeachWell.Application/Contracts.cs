using TeachWell.Domain;
namespace TeachWell.Application;

public record CurrentUser(Guid Id, Guid TenantId, string DisplayName, string Email, string[] Roles);
// Readiness is the course's readiness state (ADR 013/014) for the course card's badge, with its label.
public record CourseSummary(Guid Id, string Title, string Code, string SourceType, int ElementCount, string Permission, string Readiness = "NotRequested", string ReadinessLabel = "Not requested");
public interface IIdentityProvider { Task<CurrentUser?> GetUserAsync(System.Security.Claims.ClaimsPrincipal principal); }
public interface ICourseContentSource
{
    Task<IReadOnlyList<CourseSummary>> ListCoursesAsync(CurrentUser user, string role, CancellationToken ct);
    Task<Course?> GetCourseAsync(CurrentUser user, Guid id, CancellationToken ct);
}
public record ReviewerOption(Guid Id, string DisplayName);
public record CourseShare(Guid UserId, string DisplayName, string Permission, DateTime GrantedAt);
public enum SharingResult { Success, NotFound, InvalidReviewer }
public interface ICourseSharingService
{
    Task<IReadOnlyList<ReviewerOption>?> ListReviewersAsync(CurrentUser actor, Guid courseId, CancellationToken ct);
    Task<IReadOnlyList<CourseShare>?> ListSharesAsync(CurrentUser actor, Guid courseId, CancellationToken ct);
    Task<SharingResult> GrantAsync(CurrentUser actor, Guid courseId, Guid reviewerId, CancellationToken ct);
    Task<SharingResult> RevokeAsync(CurrentUser actor, Guid courseId, Guid reviewerId, CancellationToken ct);
}
public static class CoursePolicy
{
    public static bool CanView(Course course, CurrentUser user) => course.TenantId == user.TenantId &&
        (course.OwnerId == user.Id || course.Access.Any(a => a.UserId == user.Id));
}
