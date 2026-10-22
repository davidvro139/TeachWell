using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

// Explicit test double: no Canvas institution or real credential is contacted by this suite.
public sealed class CanvasStub : ICanvasClient
{
    public CanvasError Error { get; set; }
    public int Calls { get; private set; }
    public Func<Task>? BeforeReturn { get; set; }
    public CanvasCourseData? CourseData { get; set; }
    public Func<Task>? BeforeRead { get; set; }
    public Task<CanvasCoursesResult> ListCoursesAsync(Uri origin, string token, CancellationToken ct) => Task.FromResult(new CanvasCoursesResult(CourseData is null ? [] : [CourseData.Course], Error));
    public async Task<CanvasReadResult> ReadCourseAsync(Uri origin, string token, string courseId, CancellationToken ct)
    {
        if (BeforeRead is not null) await BeforeRead();
        return new(CourseData, Error);
    }
    public async Task<CanvasValidation> ValidateConnectionAsync(Uri origin, string token, CancellationToken ct)
    {
        Calls++;
        if (BeforeReturn is not null) await BeforeReturn();
        return new(Error == CanvasError.None ? "7654321" : null, Error);
    }
}

public class CanvasTests
{
    private const string Connections = "/api/v1/canvas/connections";
    private static string Credential() => Guid.NewGuid().ToString("N");
    private static Task<HttpResponseMessage> Connect(HttpClient c, string token) => c.PostAsJsonAsync(Connections, new { baseUrl = "https://canvas.example.test", token });
    [Fact] public async Task Save_encrypts_credential_never_returns_it_replaces_idempotently_and_audits()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor");
        var token = Credential();
        var response = await Connect(c, token); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(token, json); Assert.DoesNotContain("encryptedToken", json, StringComparison.OrdinalIgnoreCase);
        var dto = (await response.Content.ReadFromJsonAsync<CanvasConnectionDto>())!;
        Assert.Equal("Connected", dto.Status); Assert.NotNull(dto.TokenMask);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = await db.CanvasConnections.SingleAsync();
            Assert.NotEqual(token, saved.EncryptedToken);
            Assert.Equal(token, scope.ServiceProvider.GetRequiredService<ICanvasTokenProtector>().Unprotect(saved.EncryptedToken, saved.TenantId, saved.OwnerId, saved.Id));
        }
        var replaced = (await (await Connect(c, Credential())).Content.ReadFromJsonAsync<CanvasConnectionDto>())!;
        Assert.Equal(dto.Id, replaced.Id);
        Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { })).StatusCode);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"{Connections}/{dto.Id}")).StatusCode);
        var disconnected = Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        Assert.Equal("Disconnected", disconnected.Status); Assert.Null(disconnected.TokenMask); Assert.Null(disconnected.CanvasUserId);
        using var finalScope = f.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("", (await finalDb.CanvasConnections.SingleAsync()).EncryptedToken);
        Assert.Equal(4, await finalDb.AuditLogs.CountAsync(a => a.EntityId == dto.Id));
    }
    [Fact] public async Task Disabled_connector_still_lists_and_disconnects_saved_tokens_without_network()
    {
        using var f = new Factory(); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Connect(c, Credential())).StatusCode);
        Assert.Contains("false", await c.GetStringAsync("/api/v1/canvas/settings"));
        var saved = new CanvasConnection { TenantId = DemoSeed.Tenant, OwnerId = Guid.Parse("20000000-0000-0000-0000-000000000001"), BaseUrl = "https://canvas.example.test/", EncryptedToken = Credential() };
        using (var scope = f.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.CanvasConnections.Add(saved); await db.SaveChangesAsync(); }
        Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"{Connections}/{saved.Id}")).StatusCode);
        Assert.Equal(0, f.Canvas.Calls);
    }
    [Fact] public async Task Owner_and_tenant_isolation_apply_to_every_connection_action()
    {
        using var f = new Factory(true); using var owner = ApiTests.Client(f); using var other = ApiTests.Client(f); using var admin = ApiTests.Client(f); using var anonymous = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(other, "reviewer"); await ApiTests.Login(admin, "admin");
        var dto = (await (await Connect(owner, Credential())).Content.ReadFromJsonAsync<CanvasConnectionDto>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Connections)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Connections)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Connections}/{dto.Id}")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync($"{Connections}/{dto.Id}")).StatusCode);
        await ApiTests.Csrf(owner);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var u = await db.Users.FindAsync(Guid.Parse("20000000-0000-0000-0000-000000000001")); u!.TenantId = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        Assert.Empty((await owner.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{Connections}/{dto.Id}")).StatusCode);
    }
    [Fact] public async Task Invalid_input_is_rejected_before_any_remote_request()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor");
        foreach (var token in new[] { "", "bad\r\nheader", new string('x', 4097) })
            Assert.Equal(HttpStatusCode.BadRequest, (await Connect(c, token)).StatusCode);
        foreach (var baseUrl in new[] { "http://canvas.example.test", "https://canvas.example.test/path", "https://other.example.test", "https://127.0.0.1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Connections, new { baseUrl, token = Credential() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Connections, new { baseUrl = "https://canvas.example.test", token = Credential(), expiresAt = DateTimeOffset.UtcNow.AddDays(-1) })).StatusCode);
        Assert.Equal(0, f.Canvas.Calls);
        Assert.Empty((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
    }
    [Fact] public async Task Remote_rejection_does_not_save_token_and_validation_revocation_clears_saved_token()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor");
        var token = Credential(); f.Canvas.Error = CanvasError.InvalidToken;
        var rejected = await Connect(c, token); Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.DoesNotContain(token, await rejected.Content.ReadAsStringAsync()); Assert.Empty((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        f.Canvas.Error = CanvasError.None;
        var dto = (await (await Connect(c, token)).Content.ReadFromJsonAsync<CanvasConnectionDto>())!;
        f.Canvas.Error = CanvasError.Unavailable;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { })).StatusCode);
        Assert.NotNull(Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!).TokenMask);
        f.Canvas.Error = CanvasError.InvalidToken;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { })).StatusCode);
        var invalid = Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        Assert.Equal("Invalid", invalid.Status); Assert.Null(invalid.TokenMask);
    }
    [Fact] public async Task Declared_expiry_and_corrupted_ciphertext_fail_safely()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor");
        var dto = (await (await Connect(c, Credential())).Content.ReadFromJsonAsync<CanvasConnectionDto>())!;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var saved = await db.CanvasConnections.SingleAsync(); saved.EncryptedToken = "not-base64"; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { })).StatusCode);
        Assert.Equal("TokenUnreadable", Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!).ErrorCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var saved = await db.CanvasConnections.SingleAsync(); saved.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { })).StatusCode);
        Assert.Equal(1, f.Canvas.Calls);
        Assert.Null(Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!).TokenMask);
    }
    [Fact] public async Task Concurrent_validation_cannot_restore_a_disconnected_token()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor");
        var dto = (await (await Connect(c, Credential())).Content.ReadFromJsonAsync<CanvasConnectionDto>())!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Canvas.BeforeReturn = async () => { entered.TrySetResult(); await resume.Task.WaitAsync(TimeSpan.FromSeconds(15)); };
        var pending = c.PostAsJsonAsync($"{Connections}/{dto.Id}/validate", new { });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"{Connections}/{dto.Id}")).StatusCode);
        }
        finally { resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        var disconnected = Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>(Connections))!);
        Assert.Equal("Disconnected", disconnected.Status); Assert.Null(disconnected.TokenMask);
    }
}
