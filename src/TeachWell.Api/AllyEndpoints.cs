using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Ally;

namespace TeachWell.Api;

// Checking an Ally report export against TeachWell's reader (ADR 020 slice 1).
//
// CTL only, and read-only: the upload is parsed in a temporary file that the OS deletes when closed, nothing from it
// is stored, and no findings are created. It exists so a real export can be compared with the documentation the
// reader was built from before slice 2 designs a schema around that format.
public static class AllyEndpoints
{
    // Ally's institutional report CSVs are small next to a course export; the whole file is read to parse it.
    private const long MaxUploadBytes = AllyTable.MaxBytes, FormOverhead = 1_048_576;

    public static void MapAlly(this RouteGroupBuilder api)
    {
        api.MapPost("/ally/report-check", (HttpRequest request, IAllyCheckService s, PublicDemoOptions demo, Actor u, CancellationToken ct) =>
                demo.Enabled ? Task.FromResult(PublicDemoOptions.Disabled("Ally report uploads are")) : Check(request, s, u, ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Reviewer)).AddEndpointFilter(Actor.Require).RequireRateLimiting("canvas");
    }

    private static async Task<IResult> Check(HttpRequest request, IAllyCheckService service, Actor actor, CancellationToken ct)
    {
        static IResult Invalid(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
        static IResult TooLarge() => Results.Problem(statusCode: 413, title: $"The Ally report is larger than the {MaxUploadBytes / (1024 * 1024)} MB upload limit.");

        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = MaxUploadBytes + FormOverhead;
        if (request.ContentLength > MaxUploadBytes + FormOverhead) return TooLarge();
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type) || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 and <= 70 } boundary)
            return Invalid("Upload the Ally report as a form file named \"file\".");

        var path = Path.Combine(Path.GetTempPath(), $"teachwell-ally-{Guid.NewGuid():N}.tmp");
        await using var temp = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        var fileName = "";
        var received = false;
        try
        {
            var reader = new MultipartReader(boundary, request.Body);
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) continue;
                if (!disposition.IsFileDisposition() || HeaderUtilities.RemoveQuotes(disposition.Name).Value != "file") continue;
                if (received) return Invalid("Upload one Ally report at a time.");
                fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "";
                if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    return Invalid("Upload one of the CSV files from Ally's institution report export.");
                received = true;
                var buffer = new byte[81920];
                int read;
                while ((read = await section.Body.ReadAsync(buffer, ct)) > 0)
                {
                    if (temp.Length + read > MaxUploadBytes) return TooLarge();
                    await temp.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
        }
        // A malformed or empty multipart body is the client's mistake, not a server fault, so it reads as one.
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return Invalid("The upload could not be read as a form file. Choose the CSV again and retry.");
        }
        if (!received) return Invalid("Upload the Ally report as a form file named \"file\".");

        temp.Position = 0;
        return await ReviewEndpoints.Run(() => service.CheckAsync(actor.User, temp, Path.GetFileName(fileName), ct));
    }
}
