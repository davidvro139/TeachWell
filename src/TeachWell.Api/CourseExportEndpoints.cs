using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;

namespace TeachWell.Api;

public static class CourseExportEndpoints
{
    public static void MapCourseExports(this RouteGroupBuilder api)
    {
        api.MapGet("/course-exports/limits", (CourseExportOptions options, Actor _) => Results.Ok(new { maxUploadBytes = options.MaxUploadBytes }))
            .RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require);
        // Instructors import their own courses; refreshing an existing export-based course is open to its owner or a CTL reviewer it is shared with.
        // The public demo takes no uploads: visitors' files would otherwise be stored in a database every visitor can browse.
        api.MapPost("/course-exports", (HttpRequest request, ICourseExportService s, CourseExportOptions options, PublicDemoOptions demo, Actor u, CancellationToken ct) =>
                demo.Enabled ? Task.FromResult(PublicDemoOptions.Disabled("Course export uploads are")) : Import(request, null, s, options, u, ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Instructor)).AddEndpointFilter(Actor.Require).RequireRateLimiting("canvas");
        api.MapPost("/courses/{id:guid}/course-exports", (Guid id, HttpRequest request, ICourseExportService s, CourseExportOptions options, PublicDemoOptions demo, Actor u, CancellationToken ct) =>
                demo.Enabled ? Task.FromResult(PublicDemoOptions.Disabled("Course export uploads are")) : Import(request, id, s, options, u, ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer)).AddEndpointFilter(Actor.Require).RequireRateLimiting("canvas");
    }

    // Streams the multipart upload straight to a private temporary file (deleted by the OS when closed, even if the import fails),
    // instead of buffering it in memory or through ASP.NET's form reader, so exports of hundreds of megabytes are practical.
    // The upload is never written to the database or logs; only the normalized course content is kept.
    private static async Task<IResult> Import(HttpRequest request, Guid? courseId, ICourseExportService service, CourseExportOptions options, Actor actor, CancellationToken ct)
    {
        static IResult Invalid(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
        IResult TooLarge() => Results.Problem(statusCode: 413, title: $"The course export is larger than the {options.MaxUploadMegabytes:N0} MB upload limit.");
        const long FormOverhead = 1_048_576;

        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = options.MaxUploadBytes + FormOverhead;
        if (request.ContentLength > options.MaxUploadBytes + FormOverhead) return TooLarge();
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type) || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 and <= 70 } boundary)
            return Invalid("Upload the course export as a form file named \"file\".");

        var path = Path.Combine(Path.GetTempPath(), $"teachwell-export-{Guid.NewGuid():N}.tmp");
        await using var temp = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        string? canvasCourseUrl = null;
        var received = false;
        try
        {
            var reader = new MultipartReader(boundary, request.Body);
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) continue;
                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (disposition.IsFileDisposition() && name == "file")
                {
                    if (received) return Invalid("Upload one course export at a time.");
                    var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "";
                    if (!(fileName.EndsWith(".imscc", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                        return Invalid("Upload the .imscc file Canvas produces from Settings → Export Course Content.");
                    received = true;
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await section.Body.ReadAsync(buffer, ct)) > 0)
                    {
                        if (temp.Length + read > options.MaxUploadBytes) return TooLarge();
                        await temp.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }
                else if (disposition.IsFormDisposition() && name == "canvasCourseUrl")
                {
                    using var text = new StreamReader(section.Body);
                    var chars = new char[1001];
                    var length = await text.ReadBlockAsync(chars, ct);
                    if (length > 1000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["canvasCourseUrl"] = ["The Canvas course address is too long."] });
                    canvasCourseUrl = new string(chars, 0, length);
                }
                // Any other section is skipped; the reader drains it before moving on.
            }
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge) { return TooLarge(); }
        catch (Exception e) when (e is BadHttpRequestException or InvalidDataException or IOException) { return Invalid("The upload could not be read. Try again with a single .imscc file."); }
        if (!received || temp.Length == 0) return Invalid("Choose a course export (.imscc) file to upload.");
        temp.Position = 0;
        return await ReviewEndpoints.Run(() => service.ImportAsync(actor.User, temp, courseId, canvasCourseUrl, ct));
    }
}
