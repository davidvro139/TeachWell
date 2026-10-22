using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class CanvasImportTests
{
    private static CanvasCourseData Fixture(string text = "Original text", bool extra = false) => new(new("42", "Fictional imported course", "TEST 42"), [
        new("1", "module", "Getting started", "", "https://canvas.example.test/courses/42/modules/1", "Published", 1),
        new("2", "page", "Orientation", text, "https://canvas.example.test/courses/42/pages/orientation", "Published"),
        new("2", "assignment", "Practice", "Explain your design.", "https://canvas.example.test/courses/42/assignments/2", "Unpublished"),
        new("4", "moduleItem", "Start here", "", "https://canvas.example.test/courses/42/modules/items/4", "Published", 1, "1", "page", "2"),
        .. (extra ? new CanvasElementData[] { new("5", "page", "New page", "Added on refresh", "https://canvas.example.test/courses/42/pages/new", "Published") } : [])
    ], []);
    private static async Task<Guid> Connect(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/canvas/connections", new { baseUrl = "https://canvas.example.test", token = Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CanvasConnectionDto>())!.Id;
    }
    private static Task<HttpResponseMessage> Import(HttpClient c, Guid connection, string course = "42") => c.PostAsJsonAsync("/api/v1/courses/import", new { connectionId = connection, sourceCourseId = course });
    [Fact] public async Task Import_refresh_preserves_ids_shares_and_immutable_snapshots_and_marks_removed_items()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); using var reviewer = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(c, "instructor"); await ApiTests.Login(reviewer, "reviewer"); var connection = await Connect(c);
        f.Canvas.CourseData = Fixture();
        Assert.Single((await c.GetFromJsonAsync<CanvasCourseOption[]>($"/api/v1/canvas/connections/{connection}/courses"))!);
        var first = await Import(c, connection); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var result = (await first.Content.ReadFromJsonAsync<CanvasImportResult>())!; var id = result.CourseId!.Value;
        Assert.Equal(4, result.ElementCount);
        var reviewerId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"/api/v1/courses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync($"/api/v1/courses/{id}/shares", new { reviewerId })).StatusCode);
        Dictionary<string, Guid> originalIds;
        Guid originalSnapshot;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var elements = await db.Elements.Where(e => e.CourseId == id).ToListAsync(); originalIds = elements.ToDictionary(e => e.Type + e.SourceElementId, e => e.Id);
            originalSnapshot = (await db.ElementSnapshots.SingleAsync(s => s.ElementId == originalIds["page2"])).Id;
        }
        var unchanged = await Import(c, connection); Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(id, (await unchanged.Content.ReadFromJsonAsync<CanvasImportResult>())!.CourseId);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(4, await db.ElementSnapshots.CountAsync(s => originalIds.Values.Contains(s.ElementId)));
        }
        var changed = Fixture("Revised text", extra: true);
        f.Canvas.CourseData = changed with { Elements = changed.Elements.Where(e => e.Type != "assignment").ToArray() };
        Assert.Equal(HttpStatusCode.OK, (await Import(c, connection)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync($"/api/v1/courses/{id}")).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Courses.CountAsync(x => x.SourceOrigin != null));
            foreach (var pair in originalIds) Assert.True(await db.Elements.AnyAsync(e => e.Id == pair.Value));
            Assert.Equal("Original text", (await db.ElementSnapshots.SingleAsync(s => s.Id == originalSnapshot)).PlainText);
            Assert.Equal(2, await db.ElementSnapshots.CountAsync(s => s.ElementId == originalIds["page2"]));
            Assert.Equal("Removed", (await db.Elements.FindAsync(originalIds["assignment2"]))!.SourceStatus);
            Assert.Equal(5, await db.Elements.CountAsync(e => e.CourseId == id));
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == id && a.Action == "CanvasCourseImported"));
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == id && a.Action == "CanvasCourseRefreshed"));
        }
        var detail = await c.GetStringAsync($"/api/v1/courses/{id}"); Assert.Contains("Revised text", detail); Assert.Contains("currentSnapshotId", detail);
    }
    [Fact] public async Task Failed_refresh_preserves_the_previous_course_and_snapshot()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor"); var connection = await Connect(c);
        f.Canvas.CourseData = Fixture(); var imported = (await (await Import(c, connection)).Content.ReadFromJsonAsync<CanvasImportResult>())!;
        f.Canvas.Error = CanvasError.Unavailable;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Import(c, connection)).StatusCode);
        var detail = await c.GetStringAsync($"/api/v1/courses/{imported.CourseId}"); Assert.Contains("Original text", detail);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == imported.CourseId));
    }
    [Fact] public async Task Import_and_listing_enforce_connection_ownership_tenant_csrf_and_input_rules()
    {
        using var f = new Factory(true); using var owner = ApiTests.Client(f); using var other = ApiTests.Client(f); using var anonymous = ApiTests.Client(f); using var admin = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(other, "reviewer"); await ApiTests.Login(admin, "admin"); var connection = await Connect(owner); f.Canvas.CourseData = Fixture();
        Assert.Equal(HttpStatusCode.NotFound, (await Import(other, connection)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/canvas/connections/{connection}/courses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/canvas/connections/{connection}/courses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Import(admin, connection)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Import(owner, connection, "../users")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await Import(owner, connection)).StatusCode); await ApiTests.Csrf(owner);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var user = await db.Users.FindAsync(Guid.Parse("20000000-0000-0000-0000-000000000001")); user!.TenantId = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Import(owner, connection)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/canvas/connections/{connection}/courses")).StatusCode);
    }
    [Fact] public async Task Expired_or_disconnected_connections_cannot_import_and_course_errors_are_distinct()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor"); var connection = await Connect(c);
        f.Canvas.Error = CanvasError.CourseNotAuthorized;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Import(c, connection)).StatusCode);
        f.Canvas.Error = CanvasError.None; f.Canvas.CourseData = Fixture();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.CanvasConnections.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Import(c, connection)).StatusCode);
        Assert.Null(Assert.Single((await c.GetFromJsonAsync<CanvasConnectionDto[]>("/api/v1/canvas/connections"))!).TokenMask);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/canvas/connections/{connection}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Import(c, connection)).StatusCode);
    }
    [Fact] public async Task Disconnect_during_import_prevents_any_course_commit()
    {
        using var f = new Factory(true); using var c = ApiTests.Client(f); await f.Seed(); await ApiTests.Login(c, "instructor"); var connection = await Connect(c); f.Canvas.CourseData = Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Canvas.BeforeRead = async () => { entered.TrySetResult(); await resume.Task.WaitAsync(TimeSpan.FromSeconds(15)); };
        var pending = Import(c, connection);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/canvas/connections/{connection}")).StatusCode); }
        finally { resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.False(await db.Courses.AnyAsync(x => x.SourceOrigin != null));
    }
    [Fact] public async Task Snapshots_cannot_be_modified_or_deleted_through_context()
    {
        using var f = new Factory(); using var c = ApiTests.Client(f); await f.Seed();
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshot = await db.ElementSnapshots.FirstAsync();
        db.Entry(snapshot).Property(x => x.PlainText).CurrentValue = "Attempted rewrite";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(snapshot).State = EntityState.Unchanged; db.ElementSnapshots.Remove(snapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(snapshot).State = EntityState.Unchanged;
        if (db.Database.IsNpgsql())
        {
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"ElementSnapshots\" SET \"PlainText\" = \"PlainText\""));
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"ElementSnapshots\""));
        }
    }
}
