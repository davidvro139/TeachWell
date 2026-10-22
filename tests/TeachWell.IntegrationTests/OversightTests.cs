using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using TeachWell.Infrastructure.Reviews;
using Xunit;

public class OversightTests
{
    private const string Web101 = "30000000-0000-0000-0000-000000000001", Des120 = "30000000-0000-0000-0000-000000000002";
    private static readonly Guid AlexId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact] public async Task Academic_administrator_sees_college_results_but_no_course_content_and_changes_nothing()
    {
        using var f = new Factory(); using var academic = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(academic, "academic");
        var view = (await academic.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!;
        Assert.Equal((2, false), (view.Courses, view.CanRefresh));
        Assert.All(view.CourseRows, r => Assert.False(r.CanOpen));
        Assert.Equal(2, view.Readiness.Single(s => s.State == OversightVocabulary.NotRequested).Courses);
        Assert.NotEmpty(view.Scope.Caveats);

        // No course content, no course lists, no guide, no editing.
        Assert.Equal(HttpStatusCode.NotFound, (await academic.GetAsync($"/api/v1/courses/{Web101}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.GetAsync("/api/v1/courses?role=Instructor")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.GetAsync($"/api/v1/courses/{Web101}/guide")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.GetAsync($"/api/v1/courses/{Web101}/readiness")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.PostAsJsonAsync("/api/v1/oversight/refresh", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.PutAsJsonAsync($"/api/v1/admin/users/{AlexId}/roles", new { role = "CTLReviewer", granted = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.GetAsync("/api/v1/readiness/queue")).StatusCode);
    }

    [Fact] public async Task Ctl_rechecks_every_course_and_sees_readiness_issues_help_and_progress_by_department_without_names()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/oversight")).StatusCode);
        Assert.Equal(0, (await ctl.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!.CoursesChecked);

        var refreshed = await ctl.PostAsJsonAsync("/api/v1/oversight/refresh", new { });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Contains("\"checkedCourses\":2", await refreshed.Content.ReadAsStringAsync());

        // A help request from a guide step, and a readiness request on WEB 101.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/support/tickets", new { courseId = Web101, category = "CourseDesign", title = "t", description = "d", priority = "Low", origin = "Elsewhere" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/support/tickets", new { courseId = Web101, category = "CourseDesign", title = "Help with: No course competencies are defined", description = "Where are outcomes kept?", priority = "Medium", origin = "Guide" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/support/tickets", new { courseId = Web101, category = "Canvas", title = "Not from the guide", description = "d", priority = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/courses/{Web101}/readiness", new { note = "" })).StatusCode);

        var raw = await ctl.GetStringAsync("/api/v1/oversight");
        // Figures describe courses: no instructor or reviewer names anywhere in the response.
        Assert.DoesNotContain("Alex Morgan", raw); Assert.DoesNotContain("Jordan Lee", raw);
        var view = (await ctl.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!;
        Assert.Equal((2, 2, true), (view.Courses, view.CoursesChecked, view.CanRefresh));
        Assert.Equal(1, view.Readiness.Single(s => s.State == OversightVocabulary.Requested).Courses);
        var missing = view.TopChecks.Single(c => c.CheckCode == CourseChecks.CompetenciesMissing);
        Assert.Equal((2, 2), (missing.CoursesAffected, missing.OpenPlaces));
        Assert.Equal(2, view.IssuesByAspect.Single(a => a.Aspect == ReviewAspects.Outcomes).CoursesAffected);
        Assert.Equal((1, 1, "CourseDesign"), (view.HelpRequests.Total, view.HelpRequests.Open, view.HelpRequests.ByCategory.Single().Category));
        Assert.Equal(2, Assert.Single(view.Progress).CoursesChecked);
        // WEB 101 is shared with this reviewer; DES 120 is not, so only its figures are shown.
        Assert.Equal([("DES 120", false), ("WEB 101", true)], view.CourseRows.OrderBy(r => r.Code).Select(r => (r.Code, r.CanOpen)));

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses.SingleAsync(c => c.Id == Guid.Parse(Web101))).Department = "Computer Science";
            await db.SaveChangesAsync();
        }
        var filtered = (await ctl.GetFromJsonAsync<OversightDto>("/api/v1/oversight?department=computer%20science"))!;
        Assert.Equal((1, "Computer Science", 1), (filtered.Courses, filtered.Department, filtered.HelpRequests.Total));
        Assert.Equal(["Computer Science"], filtered.Departments);
        Assert.Equal("WEB 101", Assert.Single(filtered.CourseRows).Code);
    }

    [Fact] public async Task System_admin_grants_and_revokes_the_academic_administrator_role_with_an_audit_trail()
    {
        using var f = new Factory(); using var admin = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(admin, "admin"); await ApiTests.Login(ctl, "reviewer");
        var users = (await admin.GetFromJsonAsync<AdminUserDto[]>("/api/v1/admin/users"))!;
        Assert.Equal(["Instructor"], users.Single(u => u.Id == AlexId).Roles);
        Task<HttpResponseMessage> Change(HttpClient c, Guid id, string role, bool granted) => c.PutAsJsonAsync($"/api/v1/admin/users/{id}/roles", new { role, granted });

        Assert.Equal(HttpStatusCode.BadRequest, (await Change(admin, AlexId, Roles.Admin, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Change(admin, Guid.NewGuid(), Roles.AcademicAdmin, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(ctl, AlexId, Roles.AcademicAdmin, true)).StatusCode);
        var granted = (await (await Change(admin, AlexId, Roles.AcademicAdmin, true)).Content.ReadFromJsonAsync<AdminUserDto>())!;
        Assert.Equal(["AcademicAdmin", "Instructor"], granted.Roles.Order());

        // A fresh sign-in carries the new role.
        using var alex = ApiTests.Client(f);
        await ApiTests.Login(alex, "instructor");
        Assert.Equal(HttpStatusCode.OK, (await alex.GetAsync("/api/v1/oversight")).StatusCode);
        // Revoking takes effect for the open session at once: the service re-reads roles even while the cookie still claims the role.
        Assert.Equal(HttpStatusCode.OK, (await Change(admin, AlexId, Roles.AcademicAdmin, false)).StatusCode);
        Assert.Contains((await alex.GetAsync("/api/v1/oversight")).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });

        using var scope = f.Services.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs;
        Assert.Equal(1, await log.CountAsync(a => a.Action == "RoleGranted:AcademicAdmin" && a.SubjectUserId == AlexId));
        Assert.Equal(1, await log.CountAsync(a => a.Action == "RoleRevoked:AcademicAdmin" && a.SubjectUserId == AlexId));
    }

    [Fact] public async Task Oversight_filters_by_program_and_exports_safe_csv()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f); using var academic = ApiTests.Client(f); using var owner = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer"); await ApiTests.Login(academic, "academic"); await ApiTests.Login(owner, "instructor");
        var classified = await ctl.PutAsJsonAsync($"/api/v1/courses/{Web101}/classification", new { deliveryMode = "Online", department = "Computing", program = "  Web Development AAS " });
        Assert.Equal("Web Development AAS", (await classified.Content.ReadFromJsonAsync<CourseStandardsDto>())!.Program);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // A title that a spreadsheet would run as a formula.
            (await db.Courses.SingleAsync(c => c.Id == Guid.Parse(Web101))).Title = "=HYPERLINK(\"http://x.test\",\"Click\")";
            await db.SaveChangesAsync();
        }
        var view = (await academic.GetFromJsonAsync<OversightDto>("/api/v1/oversight?program=web%20development%20aas"))!;
        Assert.Equal((1, "Web Development AAS"), (view.Courses, view.Program));
        Assert.Equal(["Web Development AAS"], view.Programs);
        Assert.Equal("Web Development AAS", Assert.Single(view.CourseRows).Program);

        var csv = await academic.GetAsync("/api/v1/oversight/export?program=Web%20Development%20AAS");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("teachwell-oversight-web-development-aas-", csv.Content.Headers.ContentDisposition!.FileNameStar ?? csv.Content.Headers.ContentDisposition.FileName);
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var lines = System.Text.Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Course code,Course title,Department,Program", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("WEB 101,\"'=HYPERLINK(", lines[1]);
        Assert.Contains("Web Development AAS", lines[1]);
        Assert.DoesNotContain("Alex Morgan", lines[1]);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/oversight/export")).StatusCode);
    }

    [Fact] public async Task Scheduled_recheck_records_every_course_and_is_audited_as_the_system()
    {
        using var f = new Factory(); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(ctl, "reviewer");
        Assert.Throws<InvalidOperationException>(() => new OversightOptions(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Oversight:AutoRecheckHours"] = "500" }).Build()));
        var before = (await ctl.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!;
        Assert.Equal((0, 0, (DateTime?)null), (before.CoursesChecked, before.AutoRecheckHours, before.LastAutomaticRecheck));

        var job = f.Services.GetServices<IHostedService>().OfType<OversightRecheckService>().Single();
        Assert.Equal(2, await job.RunOnceAsync(default));
        var after = (await ctl.GetFromJsonAsync<OversightDto>("/api/v1/oversight"))!;
        Assert.Equal(2, after.CoursesChecked);
        Assert.NotNull(after.LastAutomaticRecheck);
        using var scope = f.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.CountAsync(a => a.Action == OversightOptions.AutoRecheckAction && a.ActorId == Guid.Empty));
    }

    [Fact] public async Task System_admin_reads_the_audit_trail_filtered_and_paged_and_nobody_else_can()
    {
        using var f = new Factory(); using var admin = ApiTests.Client(f); using var owner = ApiTests.Client(f); using var academic = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(admin, "admin"); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(academic, "academic");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/courses/{Web101}/readiness", new { note = "" })).StatusCode);
        await f.Services.GetServices<IHostedService>().OfType<OversightRecheckService>().Single().RunOnceAsync(default);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // 120 older entries by Alex for paging, and one by someone outside the college, which must never appear.
            var start = DateTime.UtcNow.AddDays(-30);
            db.AuditLogs.AddRange(Enumerable.Range(0, 120).Select(i => new AuditLog { ActorId = AlexId, EntityId = Guid.Parse(Web101), Action = "PagingTest", Timestamp = start.AddMinutes(i) }));
            db.AuditLogs.Add(new AuditLog { ActorId = Guid.NewGuid(), EntityId = Guid.NewGuid(), Action = "OtherCollege" });
            await db.SaveChangesAsync();
        }
        var all = (await admin.GetFromJsonAsync<AuditPageDto>("/api/v1/admin/audit"))!;
        Assert.Equal(100, all.Items.Count);
        Assert.NotNull(all.NextBefore);
        Assert.True(all.Items.Zip(all.Items.Skip(1)).All(p => p.First.Timestamp >= p.Second.Timestamp));
        Assert.Contains("ReadinessRequested", all.Actions);
        Assert.DoesNotContain("OtherCollege", all.Actions);
        var system = all.Items.First(i => i.Action == OversightOptions.AutoRecheckAction);
        Assert.Equal((AuditService.SystemName, "This college"), (system.ActorName, system.EntityLabel));

        var requested = Assert.Single((await admin.GetFromJsonAsync<AuditPageDto>("/api/v1/admin/audit?action=ReadinessRequested"))!.Items);
        Assert.Equal("Alex Morgan", requested.ActorName);
        var paging = (await admin.GetFromJsonAsync<AuditPageDto>($"/api/v1/admin/audit?action=PagingTest&actorId={AlexId}"))!;
        Assert.Equal(100, paging.Items.Count);
        Assert.Equal("WEB 101 · Foundations of Web Development", paging.Items[0].EntityLabel);
        var rest = (await admin.GetFromJsonAsync<AuditPageDto>($"/api/v1/admin/audit?action=PagingTest&actorId={AlexId}&before={Uri.EscapeDataString(paging.NextBefore!.Value.ToString("O"))}"))!;
        Assert.Equal((20, (DateTime?)null), (rest.Items.Count, rest.NextBefore));
        Assert.Empty(paging.Items.Select(i => i.Id).Intersect(rest.Items.Select(i => i.Id)));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/audit?from=2026-10-20&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.GetAsync("/api/v1/admin/audit")).StatusCode);
    }
}
