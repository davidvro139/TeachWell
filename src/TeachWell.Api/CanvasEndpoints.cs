using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;

namespace TeachWell.Api;

public static class CanvasEndpoints
{
    public static void MapCanvas(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/canvas").RequireAuthorization(p => p.RequireRole(Roles.Instructor, Roles.Reviewer));
        group.MapGet("/settings", (CanvasIntegrationOptions options) => Results.Ok(new
        {
            enabled = options.Enabled,
            allowedOrigins = options.Enabled ? options.AllowedHosts.Select(h => "https://" + h + "/").ToArray() : []
        }));
        group.MapGet("/connections", async (HttpContext c, IIdentityProvider identity, ICanvasConnectionService service, CancellationToken ct) =>
        {
            var user = await identity.GetUserAsync(c.User);
            return user is null ? Results.Unauthorized() : Results.Ok(await service.ListAsync(user, ct));
        });
        group.MapPost("/connections", async (ConnectRequest input, HttpContext c, IIdentityProvider identity, ICanvasConnectionService service,
            CanvasIntegrationOptions options, CanvasHostPolicy hosts, CancellationToken ct) =>
        {
            var user = await identity.GetUserAsync(c.User);
            if (!Allowed(user)) return Results.Forbid();
            if (!options.Enabled) return Disabled();
            if (!Validator.TryValidateObject(input, new ValidationContext(input), [], true) || input.Token.Any(ch => ch <= 32 || ch >= 127))
                return Invalid("connection", "Provide an approved HTTPS Canvas URL and a token without whitespace (maximum 4096 characters).");
            var origin = hosts.ParseOrigin(input.BaseUrl);
            if (origin is null) return Invalid("baseUrl", "Choose an approved Canvas origin with no path, query, credentials, or custom port.");
            if (input.ExpiresAt <= DateTimeOffset.UtcNow) return Invalid("expiresAt", "Token expiry must be in the future.");
            try { return Result(await service.ConnectAsync(user!, origin, input.Token, input.ExpiresAt?.UtcDateTime, ct)); }
            catch (DbUpdateException) { return Conflict(); }
        }).RequireRateLimiting("canvas");
        group.MapPost("/connections/{id:guid}/validate", async (Guid id, HttpContext c, IIdentityProvider identity, ICanvasConnectionService service, CanvasIntegrationOptions options, CancellationToken ct) =>
        {
            var user = await identity.GetUserAsync(c.User);
            if (!Allowed(user)) return Results.Forbid();
            if (!options.Enabled) return Disabled();
            try { return Result(await service.ValidateAsync(user!, id, ct)); }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
        }).RequireRateLimiting("canvas");
        group.MapDelete("/connections/{id:guid}", async (Guid id, HttpContext c, IIdentityProvider identity, ICanvasConnectionService service, CancellationToken ct) =>
        {
            var user = await identity.GetUserAsync(c.User);
            if (!Allowed(user)) return Results.Forbid();
            try { return await service.DisconnectAsync(user!, id, ct) ? Results.NoContent() : Results.NotFound(); }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
        });
    }
    private static bool Allowed(CurrentUser? user) => user is not null && (user.Roles.Contains(Roles.Instructor) || user.Roles.Contains(Roles.Reviewer));
    private static IResult Invalid(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    internal static IResult Disabled() => Results.Problem(statusCode: 503, title: "Canvas connections are not enabled for this workspace.");
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "The connection changed. Refresh its status and try again.");
    private static IResult Result(CanvasConnectionResult result)
    {
        if (result.NotFound) return Results.NotFound();
        if (result.Error == CanvasError.None) return Results.Ok(result.Connection);
        return Error(result.Error);
    }
    internal static IResult Error(CanvasError error)
    {
        var (status, title) = error switch
        {
            CanvasError.InvalidToken => (422, "Canvas rejected the token. It may have expired or been revoked."),
            CanvasError.PermissionDenied => (422, "Canvas denied access. Check the token's permissions."),
            CanvasError.ResourceNotFound => (404, "Canvas did not provide the requested resource."),
            CanvasError.CourseNotAuthorized => (422, "Canvas must report an active teacher enrollment for this course before import."),
            CanvasError.LimitExceeded => (422, "This course exceeds the current import limits. Existing imported content was not changed."),
            CanvasError.Expired => (422, "The saved token has reached its declared expiry. Reconnect with a valid token."),
            CanvasError.TokenUnreadable => (409, "The saved credential cannot be decrypted. Disconnect and reconnect."),
            CanvasError.Disconnected => (409, "This connection is disconnected. Reconnect with a valid token."),
            CanvasError.Timeout => (504, "Canvas did not respond in time. Try again later."),
            CanvasError.RateLimited => (503, "Canvas is limiting requests. Wait before validating again."),
            CanvasError.RedirectBlocked => (502, "Canvas redirected the request. Use its approved API hostname."),
            CanvasError.UnexpectedResponse => (502, "Canvas returned an unexpected response."),
            _ => (503, "Canvas could not be reached. Check the approved hostname or try again later.")
        };
        return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    }
    // A class deliberately avoids record-generated ToString containing the credential.
    public sealed class ConnectRequest
    {
        [Required, MaxLength(300)] public string BaseUrl { get; set; } = "";
        [Required, MaxLength(4096)] public string Token { get; set; } = "";
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
