using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Infrastructure;
using Xunit;

public class DemoSignInTests
{
    private record Account(string Email, string DisplayName, string[] Roles);

    [Fact] public async Task Local_demo_accounts_sign_in_with_one_click_and_each_sign_in_is_audited()
    {
        using var f = new Factory(quickLogin: true); using var c = ApiTests.Client(f); await f.Seed();
        var raw = await c.GetStringAsync("/api/v1/auth/demo-accounts");
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        var accounts = (await c.GetFromJsonAsync<Account[]>("/api/v1/auth/demo-accounts"))!;
        Assert.Equal(["instructor@demo.teachwell.test", "reviewer@demo.teachwell.test", "academic@demo.teachwell.test", "admin@demo.teachwell.test"], accounts.Select(a => a.Email));
        Assert.Equal(["CTLReviewer", "Instructor"], accounts[1].Roles.Order());

        await ApiTests.Csrf(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/v1/auth/demo-login", new { email = "reviewer@demo.teachwell.test" })).StatusCode);
        await ApiTests.Csrf(c);
        var me = (await c.GetFromJsonAsync<CurrentUser>("/api/v1/me"))!;
        Assert.Equal("Jordan Lee", me.DisplayName);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/courses?role=CTLReviewer")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/v1/auth/logout", new { })).StatusCode);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(me.Id, (await db.AuditLogs.SingleAsync(a => a.Action == "DemoQuickSignIn")).ActorId);
    }

    [Fact] public async Task Quick_sign_in_rejects_other_accounts_missing_csrf_and_locked_accounts()
    {
        using var f = new Factory(quickLogin: true); using var c = ApiTests.Client(f); await f.Seed();
        // The global CSRF check applies before anything else.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/demo-login", new { email = "instructor@demo.teachwell.test" })).StatusCode);
        await ApiTests.Csrf(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/demo-login", new { email = "someone@example.edu" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/demo-login", new { email = "nobody@demo.teachwell.test" })).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var admin = (await users.FindByEmailAsync("admin@demo.teachwell.test"))!;
            await users.SetLockoutEndDateAsync(admin, DateTimeOffset.UtcNow.AddMinutes(10));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/demo-login", new { email = "admin@demo.teachwell.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [InlineData(false, "127.0.0.1")] // feature off (the default outside Start-Local)
    [InlineData(true, "203.0.113.7")] // feature on, but the request is not from this machine
    public async Task Quick_sign_in_does_not_exist_unless_enabled_for_a_loopback_client(bool enabled, string client)
    {
        using var f = new Factory(quickLogin: enabled, clientAddress: client); using var c = ApiTests.Client(f); await f.Seed();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/auth/demo-accounts")).StatusCode);
        await ApiTests.Csrf(c);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/v1/auth/demo-login", new { email = "instructor@demo.teachwell.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact] public void Loopback_check_accepts_ipv4_mapped_addresses_and_rejects_remote_ones()
    {
        Assert.True(TeachWell.Api.DemoSignInEndpoints.IsLoopback(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.True(TeachWell.Api.DemoSignInEndpoints.IsLoopback(IPAddress.IPv6Loopback));
        Assert.False(TeachWell.Api.DemoSignInEndpoints.IsLoopback(IPAddress.Parse("::ffff:203.0.113.7")));
        Assert.False(TeachWell.Api.DemoSignInEndpoints.IsLoopback(null));
    }
}
