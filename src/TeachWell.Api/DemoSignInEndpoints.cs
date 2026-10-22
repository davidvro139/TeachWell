using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeachWell.Domain;
using TeachWell.Infrastructure;

namespace TeachWell.Api;

// One-click sign-in for the fictional demo accounts (ADR 012). Locally, every request must pass all of
//   Development environment + Features:DemoAuth + Features:DemoQuickLogin + a loopback client + a seeded @demo.teachwell.test account,
// otherwise the routes answer 404 as if they did not exist. The hosted public demo (Features:PublicDemo, see PublicDemo.cs)
// offers it to every visitor instead, because its database only ever holds the fictional seed. No password is involved or
// sent to the browser; the global CSRF check and the login rate limit still apply; lockout is respected; every sign-in is audited.
public static class DemoSignInEndpoints
{
    public const string Domain = "@demo.teachwell.test";
    // Display order on the sign-in screen; accounts not listed sort after these.
    private static readonly string[] Order = ["instructor", "reviewer", "academic", "admin", "instructor2", "reviewer2"];

    public static bool Enabled(HttpContext context)
    {
        var services = context.RequestServices;
        var config = services.GetRequiredService<IConfiguration>();
        if (services.GetRequiredService<PublicDemoOptions>().Enabled) return true;
        return services.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
            && config.GetValue<bool>("Features:DemoAuth") && config.GetValue<bool>("Features:DemoQuickLogin")
            && IsLoopback(context.Connection.RemoteIpAddress);
    }

    // Kestrel reports IPv4 clients on a dual-stack socket as IPv4-mapped IPv6 (::ffff:127.0.0.1), which IPAddress.IsLoopback rejects.
    public static bool IsLoopback(IPAddress? ip) => ip is not null && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

    public record DemoAccount(string Email, string DisplayName, string[] Roles);
    public record DemoSignInRequest([property: Required, EmailAddress, MaxLength(254)] string Email);

    public static void MapDemoSignIn(this RouteGroupBuilder api)
    {
        // Lets the sign-in screen and workspace explain the public demo's limits; reveals nothing beyond the two settings.
        api.MapGet("/auth/demo-mode", (PublicDemoOptions options) => Results.Ok(new { publicDemo = options.Enabled, resetHours = options.Enabled ? options.ResetHours : 0 }));

        api.MapGet("/auth/demo-accounts", async (HttpContext context, UserManager<AppUser> users, CancellationToken ct) =>
        {
            if (!Enabled(context)) return Results.NotFound();
            var accounts = await users.Users.AsNoTracking().Where(u => u.IsActive && u.Email!.EndsWith(Domain)).ToListAsync(ct);
            var result = new List<DemoAccount>();
            foreach (var u in accounts.OrderBy(u => Rank(u.Email!)).ThenBy(u => u.Email))
                result.Add(new(u.Email!, u.DisplayName, [.. await users.GetRolesAsync(u)]));
            return Results.Ok(result);
        });

        api.MapPost("/auth/demo-login", async (DemoSignInRequest input, HttpContext context, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db) =>
        {
            if (!Enabled(context)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Email) || !input.Email.EndsWith(Domain, StringComparison.OrdinalIgnoreCase))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Choose one of the demo accounts."] });
            var user = await users.FindByEmailAsync(input.Email);
            if (user is null || !user.IsActive) return Results.Problem(statusCode: 401, title: "That demo account is not available.");
            if (await users.IsLockedOutAsync(user)) return Results.Problem(statusCode: 401, title: "This account is temporarily locked; wait a few minutes and try again.");
            await signIn.SignInAsync(user, isPersistent: false);
            db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = user.Id, Action = "DemoQuickSignIn" });
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireRateLimiting("login");
    }

    private static int Rank(string email)
    {
        var index = Array.IndexOf(Order, email[..email.IndexOf('@')].ToLowerInvariant());
        return index < 0 ? Order.Length : index;
    }
}
