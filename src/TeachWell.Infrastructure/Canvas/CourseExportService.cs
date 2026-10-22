using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CourseExportService(AppDbContext db) : ICourseExportService
{
    public const string SourceType = "CanvasExport";

    public async Task<ServiceResult<CourseExportImportDto>> ImportAsync(CurrentUser user, Stream export, Guid? courseId, string? canvasCourseUrl, CancellationToken ct)
    {
        Uri? courseUrl = null;
        if (!string.IsNullOrWhiteSpace(canvasCourseUrl) && !TryCourseUrl(canvasCourseUrl.Trim(), out courseUrl))
            return ServiceResult<CourseExportImportDto>.Invalid("canvasCourseUrl", "Enter the course's https Canvas address, for example https://canvas.example.edu/courses/12345, or leave it blank.");

        IQueryable<Course> Loaded() => db.Courses.AsSplitQuery().Include(c => c.Elements).ThenInclude(e => e.Snapshots.OrderByDescending(s => s.FetchedAt).Take(1));
        Course? course = null;
        if (courseId is { } id)
        {
            // Refreshing a specific course: its owner (as instructor), or a CTL reviewer it is explicitly shared with.
            course = await Loaded().SingleOrDefaultAsync(c => c.Id == id && c.TenantId == user.TenantId &&
                (c.OwnerId == user.Id && user.Roles.Contains(Roles.Instructor) || user.Roles.Contains(Roles.Reviewer) && c.Access.Any(a => a.UserId == user.Id)), ct);
            if (course is null) return ServiceResult<CourseExportImportDto>.NotFound();
            if (course.SourceType != SourceType)
                return ServiceResult<CourseExportImportDto>.Invalid("courseId", "This course was not imported from a course export, so it cannot be refreshed with one.");
        }
        else if (!user.Roles.Contains(Roles.Instructor)) return ServiceResult<CourseExportImportDto>.NotFound();

        CanvasCourseData data;
        try { data = CanvasExportReader.Read(export); }
        catch (CourseExportException e) { return ServiceResult<CourseExportImportDto>.Invalid("file", e.Message); }
        catch (CanvasReadException) { return ServiceResult<CourseExportImportDto>.Invalid("file", "Content in this export exceeds TeachWell's import limits."); }
        if (!CourseDataApplier.IsConsistent(data)) return ServiceResult<CourseExportImportDto>.Invalid("file", "This export's module structure is inconsistent, so it was not imported.");
        if (course is not null && course.SourceCourseId != data.Course.Id)
            return ServiceResult<CourseExportImportDto>.Invalid("file", "This export is from a different course than the one you are refreshing.");

        // Uploading an export of a course the instructor already imported refreshes it rather than creating a duplicate.
        course ??= await Loaded().SingleOrDefaultAsync(c => c.TenantId == user.TenantId && c.OwnerId == user.Id && c.SourceType == SourceType && c.SourceCourseId == data.Course.Id, ct);
        var created = course is null;
        // Links resolve against the address given now, or the one saved with an earlier upload of this course.
        data = CanvasExportReader.ResolveLinks(data, courseUrl ?? (Uri.TryCreate(course?.SourceWebUrl, UriKind.Absolute, out var stored) ? stored : null));
        course ??= new() { Id = Guid.NewGuid(), TenantId = user.TenantId, OwnerId = user.Id, SourceType = SourceType, SourceCourseId = data.Course.Id };
        if (courseUrl is not null)
        {
            course.SourceWebUrl = courseUrl.AbsoluteUri.TrimEnd('/');
            course.SourceOrigin = courseUrl.GetLeftPart(UriPartial.Authority) + "/";
        }
        var warnings = CourseDataApplier.Apply(course, data, DateTime.UtcNow);
        if (created) db.Courses.Add(course);
        db.AuditLogs.Add(new() { ActorId = user.Id, SubjectUserId = course.OwnerId, EntityId = course.Id, Action = created ? "CourseExportImported" : "CourseExportRefreshed" });
        await db.SaveChangesAsync(ct);
        await CourseStructureCapture.RefreshIfPresentAsync(db, course.Id, ct);
        return ServiceResult<CourseExportImportDto>.Ok(new(course.Id, course.Title, course.Code, data.Elements.Count, created, warnings));
    }

    // Only an https Canvas course address is accepted; it is used to build links, and TeachWell never fetches it.
    public static bool TryCourseUrl(string value, out Uri? url)
    {
        url = null;
        if (value.Length > 500 || !Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.UserInfo)) return false;
        var segments = u.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || segments[0] != "courses" || segments[1].Length is 0 or > 20 || !segments[1].All(char.IsAsciiDigit)) return false;
        url = new Uri($"{u.GetLeftPart(UriPartial.Authority)}/courses/{segments[1]}");
        return true;
    }
}
