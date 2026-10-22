using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public sealed class Factory : WebApplicationFactory<Program>
{
    private readonly bool canvasEnabled, quickLogin;
    private readonly System.Net.IPAddress clientAddress;
    // TestServer leaves the client address unset; tests choose one so loopback-only features (ADR 012) can be exercised both ways.
    private readonly IDictionary<string, string?> settings;
    public Factory(bool canvasEnabled = false, bool quickLogin = false, string clientAddress = "127.0.0.1", IDictionary<string, string?>? settings = null)
    { this.canvasEnabled = canvasEnabled; this.quickLogin = quickLogin; this.clientAddress = System.Net.IPAddress.Parse(clientAddress); this.settings = settings ?? new Dictionary<string, string?>(); }
    public CanvasStub Canvas { get; } = new();
    public string CanvasKey { get; } = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string? postgresConnection = TestPostgresConnection();
    private static string? TestPostgresConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEACHWELL_TEST_POSTGRES");
        if (string.IsNullOrEmpty(configured)) return null;
        return new Npgsql.NpgsqlConnectionStringBuilder(configured) { Database = "teachwell_test_" + Guid.NewGuid().ToString("N") }.ConnectionString;
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (postgresConnection is null) connection.Open();
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Database"] = "Host=unused", ["Features:DemoAuth"] = "true",
            ["Features:CanvasPAT"] = canvasEnabled.ToString(), ["Features:DemoQuickLogin"] = quickLogin.ToString(), ["Canvas:EncryptionKey"] = CanvasKey,
            ["Canvas:AllowedHosts:0"] = "canvas.example.test",
            // The scheduled oversight re-check is tested directly (RunOnceAsync); it must not run on its own during tests.
            ["Oversight:AutoRecheckHours"] = "0"
        }.Concat(settings)));
        builder.ConfigureServices(services => {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => {
                if (postgresConnection is null) o.UseSqlite(connection);
                else o.UseNpgsql(postgresConnection);
            });
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new ClientAddressFilter(clientAddress));
            services.RemoveAll<ICanvasClient>();
            services.AddSingleton<ICanvasClient>(Canvas);
        });
    }
    public async Task Seed()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (postgresConnection is null) await db.Database.EnsureCreatedAsync();
        else await db.Database.MigrateAsync();
        await DemoSeed.RunAsync(db, scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Password"] = Password }).Build());
    }
    // Generated per process; no fixed demo credential in the repository.
    public static string Password { get; } = Guid.NewGuid().ToString("N") + "aA1!";
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        connection.Dispose();
        if (postgresConnection is not null)
        {
            using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgresConnection).Options);
            // Only the generated, isolated test database is removed; never the configured database.
            if (!new Npgsql.NpgsqlConnectionStringBuilder(postgresConnection).Database!.StartsWith("teachwell_test_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to remove a database outside the test namespace.");
            db.Database.EnsureDeleted();
        }
    }
}
sealed class ClientAddressFilter(System.Net.IPAddress address) : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) => { context.Connection.RemoteIpAddress ??= address; return nextMiddleware(context); });
        next(app);
    };
}
public class ApiTests
{
    private const string PrivateCourse = "30000000-0000-0000-0000-000000000002";
    private static readonly Guid ReviewerId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    internal static HttpClient Client(Factory f) => f.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    internal static async Task Csrf(HttpClient c)
    {
        var token = await c.GetFromJsonAsync<Token>("/api/v1/auth/csrf");
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!.TokenValue);
    }
    private record Token([property: System.Text.Json.Serialization.JsonPropertyName("token")] string TokenValue);
    internal static async Task Login(HttpClient c, string persona)
    {
        await Csrf(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = persona + "@demo.teachwell.test", password = Factory.Password })).StatusCode);
        await Csrf(c);
    }
    [Fact] public async Task Anonymous_and_csrf_requests_are_denied()
    {
        using var f = new Factory(); using var c = Client(f); await f.Seed();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/courses?role=Instructor")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "x", password = "x" })).StatusCode);
        await Csrf(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "invalid", password = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "instructor@demo.teachwell.test", password = "wrong" })).StatusCode);
    }
    [Fact] public async Task Instructor_can_read_owned_courses_but_not_admin_users_or_ctl_dashboard()
    {
        using var f = new Factory(); using var c = Client(f); await f.Seed(); await Login(c, "instructor");
        var courses = await c.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=Instructor");
        Assert.Equal(2, courses!.Length);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/courses/" + courses[0].Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/courses?role=CTLReviewer")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/v1/courses?role=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/v1/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/me")).StatusCode);
    }
    [Fact] public async Task Reviewer_sees_only_shared_course_and_can_switch_to_empty_instructor_view()
    {
        using var f = new Factory(); using var c = Client(f); await f.Seed(); await Login(c, "reviewer");
        Assert.Single((await c.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=CTLReviewer"))!);
        Assert.Empty((await c.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=Instructor"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/courses/30000000-0000-0000-0000-000000000002")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/courses/30000000-0000-0000-0000-000000000001")).StatusCode);
    }
    [Fact] public async Task Admin_has_no_implicit_course_access_and_seed_is_idempotent()
    {
        using var f = new Factory(); using var c = Client(f); await f.Seed(); await f.Seed(); await Login(c, "admin");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/courses/30000000-0000-0000-0000-000000000001")).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Courses.CountAsync()); Assert.Equal(6, await db.Elements.CountAsync()); Assert.Equal(5, await db.AuditLogs.CountAsync());
        Assert.Equal(6, await db.ElementSnapshots.CountAsync()); Assert.Single(await db.RubricVersions.ToListAsync()); Assert.Single(await db.StandardSetVersions.ToListAsync());
        Assert.Single(await db.KnowledgeArticles.ToListAsync()); Assert.Single(await db.Workshops.ToListAsync());
    }
    [Fact] public void Tenant_boundary_overrides_ownership_and_explicit_access()
    {
        var id = Guid.NewGuid(); var user = new CurrentUser(id, Guid.NewGuid(), "Test", "test@example.test", [Roles.Admin]);
        var course = new Course { TenantId = Guid.NewGuid(), OwnerId = id, Access = [new() { UserId = id }] };
        Assert.False(CoursePolicy.CanView(course, user));
    }
    [Fact] public async Task Cross_tenant_course_is_hidden_even_with_explicit_share()
    {
        using var f = new Factory(); using var c = Client(f); await f.Seed(); await Login(c, "reviewer");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses.SingleAsync(x => x.Id == Guid.Parse("30000000-0000-0000-0000-000000000001"));
            course.TenantId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        Assert.Empty((await c.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=CTLReviewer"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/courses/30000000-0000-0000-0000-000000000001")).StatusCode);
    }
    [Fact] public async Task Repeated_bad_password_locks_account_with_a_distinct_message_and_logout_requires_csrf()
    {
        using var f = new Factory(); using var c = Client(f); await f.Seed(); await Csrf(c);
        // The very first wrong attempt reports a plain credentials failure, not a lockout.
        var first = await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "instructor@demo.teachwell.test", password = "incorrect" });
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Contains("Check your email and password", await first.Content.ReadAsStringAsync());
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "instructor@demo.teachwell.test", password = "incorrect" })).StatusCode);
        // After the 5th failure the account is locked; even the correct password now fails with a distinct, explicit message.
        var lockedOut = await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "instructor@demo.teachwell.test", password = Factory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);
        Assert.Contains("temporarily locked", await lockedOut.Content.ReadAsStringAsync());
        await Login(c, "reviewer");
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/logout", new { })).StatusCode);
    }
    [Fact] public async Task Owner_can_grant_and_revoke_review_access_with_atomic_audit_and_idempotency()
    {
        using var f = new Factory(); using var owner = Client(f); using var reviewer = Client(f);
        await f.Seed(); await Login(owner, "instructor"); await Login(reviewer, "reviewer");
        var directory = await owner.GetFromJsonAsync<ReviewerOption[]>($"/api/v1/courses/{PrivateCourse}/reviewers");
        Assert.Equal(ReviewerId, Assert.Single(directory!).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"/api/v1/courses/{PrivateCourse}")).StatusCode);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/v1/courses/{PrivateCourse}/shares", new { reviewerId = ReviewerId })).StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<CourseShare[]>($"/api/v1/courses/{PrivateCourse}/shares"))!);
        Assert.Equal(2, (await reviewer.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=CTLReviewer"))!.Length);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync($"/api/v1/courses/{PrivateCourse}")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync($"/api/v1/courses/{PrivateCourse}/shares/{ReviewerId}")).StatusCode);
        await Csrf(owner);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/courses/{PrivateCourse}/shares/{ReviewerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"/api/v1/courses/{PrivateCourse}")).StatusCode);
        Assert.Single((await reviewer.GetFromJsonAsync<CourseSummary[]>("/api/v1/courses?role=CTLReviewer"))!);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.Where(a => a.EntityId == Guid.Parse(PrivateCourse)).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => { Assert.Equal(ReviewerId, a.SubjectUserId); Assert.InRange(a.Timestamp, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1)); });
        Assert.Contains(audit, a => a.Action == "CourseReviewAccessGranted");
        Assert.Contains(audit, a => a.Action == "CourseReviewAccessRevoked");
    }
    [Fact] public async Task Reviewers_admins_and_anonymous_users_cannot_manage_sharing()
    {
        using var f = new Factory(); using var reviewer = Client(f); using var admin = Client(f); using var anonymous = Client(f);
        await f.Seed(); await Login(reviewer, "reviewer"); await Login(admin, "admin");
        const string shared = "30000000-0000-0000-0000-000000000001";
        foreach (var suffix in new[] { "shares", "reviewers" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"/api/v1/courses/{shared}/{suffix}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/courses/{shared}/{suffix}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/courses/{shared}/{suffix}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.PostAsJsonAsync($"/api/v1/courses/{shared}/shares", new { reviewerId = ReviewerId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.DeleteAsync($"/api/v1/courses/{shared}/shares/{ReviewerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/courses/{shared}/shares", new { reviewerId = ReviewerId })).StatusCode);
    }
    [Fact] public async Task Share_validation_rejects_non_reviewers_inactive_cross_tenant_and_unknown_users()
    {
        using var f = new Factory(); using var owner = Client(f); await f.Seed(); await Login(owner, "instructor");
        foreach (var id in new[] { Guid.Empty, Guid.NewGuid(), Guid.Parse("20000000-0000-0000-0000-000000000001") })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/courses/{PrivateCourse}/shares", new { reviewerId = id })).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(ReviewerId); user!.IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Empty((await owner.GetFromJsonAsync<ReviewerOption[]>($"/api/v1/courses/{PrivateCourse}/reviewers"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/courses/{PrivateCourse}/shares", new { reviewerId = ReviewerId })).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(ReviewerId); user!.IsActive = true; user.TenantId = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        Assert.Empty((await owner.GetFromJsonAsync<ReviewerOption[]>($"/api/v1/courses/{PrivateCourse}/reviewers"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/courses/{PrivateCourse}/shares", new { reviewerId = ReviewerId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/courses/{Guid.NewGuid()}/shares")).StatusCode);
    }
}
