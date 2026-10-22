using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TeachWell.Api;
using Xunit;

public class PublicDemoTests
{
    private record Account(string Email, string DisplayName, string[] Roles);
    private record Mode(bool PublicDemo, int ResetHours);

    // Resets need PostgreSQL and are exercised against a real database by hand (see README); here they are switched off.
    private static Factory PublicDemo(string client = "203.0.113.7") => new(clientAddress: client, settings: new Dictionary<string, string?> {
        ["Features:PublicDemo"] = "true", ["PublicDemo:ResetHours"] = "0" });

    private static async Task SignIn(HttpClient c, string email)
    {
        await ApiTests.Csrf(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/v1/auth/demo-login", new { email })).StatusCode);
        await ApiTests.Csrf(c);
    }

    [Fact] public async Task Visitors_from_any_address_sign_in_with_one_click_but_never_with_a_password()
    {
        using var f = PublicDemo(); using var c = ApiTests.Client(f); await f.Seed();
        Assert.Equal(new Mode(true, 0), await c.GetFromJsonAsync<Mode>("/api/v1/auth/demo-mode"));
        var accounts = (await c.GetFromJsonAsync<Account[]>("/api/v1/auth/demo-accounts"))!;
        Assert.Contains(accounts, a => a.Email == "instructor@demo.teachwell.test");

        await ApiTests.Csrf(c);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "instructor@demo.teachwell.test", password = "anything-at-all" })).StatusCode);
        await SignIn(c, "instructor@demo.teachwell.test");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/courses?role=Instructor")).StatusCode);
    }

    [Fact] public async Task Uploads_and_role_changes_are_refused()
    {
        using var f = PublicDemo(); using var c = ApiTests.Client(f); await f.Seed();
        await SignIn(c, "instructor@demo.teachwell.test");
        using (var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "course.imscc" } })
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/v1/course-exports", form)).StatusCode);

        await SignIn(c, "admin@demo.teachwell.test");
        var users = await c.GetFromJsonAsync<JsonElement>("/api/v1/admin/users");
        var target = users.EnumerateArray().First(u => u.GetProperty("email").GetString() == "instructor@demo.teachwell.test").GetProperty("id").GetGuid();
        var response = await c.PutAsJsonAsync($"/api/v1/admin/users/{target}/roles", new { role = "CTLReviewer", granted = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("public demo", await response.Content.ReadAsStringAsync());
    }

    [Fact] public async Task The_api_answers_503_while_a_reset_runs_and_health_stays_up()
    {
        // Resets on, but with the hosted services removed so the test, not a background reset, decides when one is "running".
        using var f = new Factory(settings: new Dictionary<string, string?> { ["Features:PublicDemo"] = "true", ["PublicDemo:ResetHours"] = "24" });
        using var resetting = f.WithWebHostBuilder(b => b.ConfigureServices(s => s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>()));
        using var c = resetting.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var state = resetting.Services.GetRequiredService<PublicDemoState>();
        Assert.True(state.Resetting); // nothing is served until the first seed finishes
        var api = await c.GetAsync("/api/v1/auth/demo-mode");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
        Assert.Equal("30", api.Headers.RetryAfter?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/healthz")).StatusCode);
        state.Resetting = false;
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/demo-mode")).StatusCode);
    }

    [Fact] public async Task The_public_demo_answers_on_its_hosted_address_while_other_modes_stay_localhost_only()
    {
        static HttpRequestMessage Healthz() => new(HttpMethod.Get, "/healthz") { Headers = { Host = "teachwell-demo.onrender.com" } };
        using (var f = PublicDemo()) using (var c = ApiTests.Client(f))
            Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Healthz())).StatusCode);
        using (var f = new Factory()) using (var c = ApiTests.Client(f))
            Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Healthz())).StatusCode);
    }

    [Fact] public async Task Outside_the_public_demo_the_mode_reports_off_and_remote_quick_sign_in_stays_hidden()
    {
        using var f = new Factory(quickLogin: true, clientAddress: "203.0.113.7"); using var c = ApiTests.Client(f); await f.Seed();
        Assert.Equal(new Mode(false, 0), await c.GetFromJsonAsync<Mode>("/api/v1/auth/demo-mode"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/auth/demo-accounts")).StatusCode);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("169")]
    [InlineData("daily")]
    public void Reset_hours_must_be_a_sensible_whole_number(string hours) =>
        Assert.Throws<InvalidOperationException>(() => new PublicDemoOptions(Config(("Features:PublicDemo", "true"), ("PublicDemo:ResetHours", hours))));

    [Fact] public void The_public_demo_refuses_to_run_with_canvas_connections() =>
        Assert.Throws<InvalidOperationException>(() => new PublicDemoOptions(Config(("Features:PublicDemo", "true"), ("Features:CanvasPAT", "true"))));

    [Fact] public void Defaults_reset_daily_only_when_enabled()
    {
        Assert.True(new PublicDemoOptions(Config(("Features:PublicDemo", "true"))).Resets);
        Assert.Equal(24, new PublicDemoOptions(Config(("Features:PublicDemo", "true"))).ResetHours);
        Assert.False(new PublicDemoOptions(Config()).Resets);
    }

    [Theory]
    [InlineData("postgresql://teach:p%40ss%3Aw0rd@dpg-abc123-a/teachwell", "dpg-abc123-a", 5432, "teachwell", "teach", "p@ss:w0rd")]
    [InlineData("postgres://u:pw@db.example.test:6543/demo_db", "db.example.test", 6543, "demo_db", "u", "pw")]
    public void Database_urls_become_npgsql_connection_strings(string url, string host, int port, string database, string user, string password)
    {
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseUrl.ToConnectionString(url));
        Assert.Equal((host, port, database, user, password), (parsed.Host!, parsed.Port, parsed.Database!, parsed.Username!, parsed.Password!));
    }

    [Theory]
    [InlineData("mysql://u:p@host/db")]
    [InlineData("not a url")]
    public void Other_database_urls_are_rejected(string url) => Assert.Throws<InvalidOperationException>(() => DatabaseUrl.ToConnectionString(url));

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
}
