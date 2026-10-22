using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using Xunit;

public class GettingStartedTests
{
    private const string Course = "30000000-0000-0000-0000-000000000001";
    private static async Task<GettingStartedDto> Get(HttpClient c) => (await c.GetFromJsonAsync<GettingStartedDto>("/api/v1/getting-started"))!;

    [Fact] public async Task The_checklist_follows_what_the_instructor_has_actually_done_with_their_own_courses()
    {
        using var f = new Factory(); using var owner = ApiTests.Client(f); using var ctl = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(owner, "instructor"); await ApiTests.Login(ctl, "reviewer");

        // Only fictional demo courses so far: nothing done yet, but a course to try the guide on.
        var start = await Get(owner);
        Assert.Equal((false, false, false, false), (start.BroughtInACourse, start.MarkedAStep, start.AskedForReadiness, start.Endorsed));
        Assert.NotNull(start.CourseId);

        // Marking a guide step counts.
        var guide = (await owner.GetFromJsonAsync<CourseGuideDto>($"/api/v1/courses/{Course}/guide"))!;
        var key = guide.Steps.Single(s => s.CheckCode == CourseChecks.CompetenciesMissing).Locations[0].Key;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/v1/courses/{Course}/guide/progress",
            new { keys = new[] { key }, status = "NotApplicable", reason = "Recorded in Simple Syllabus." })).StatusCode);
        Assert.True((await Get(owner)).MarkedAStep);

        // An imported course of their own counts as bringing one in, and becomes the course to continue with; so do a request and an endorsement.
        Guid imported, requester;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            requester = await db.Users.Where(u => u.Email == "instructor@demo.teachwell.test").Select(u => u.Id).SingleAsync();
            var reviewer = await db.Users.Where(u => u.Email == "reviewer@demo.teachwell.test").Select(u => u.Id).SingleAsync();
            imported = Guid.NewGuid();
            db.Courses.Add(new Course
            {
                Id = imported, TenantId = DemoSeed.Tenant, OwnerId = requester, Title = "My uploaded course", Code = "MINE 100",
                SourceType = "CanvasExport", SourceCourseId = "export-1", LastSyncedAt = DateTime.UtcNow
            });
            var request = new ReadinessRequest { Id = Guid.NewGuid(), TenantId = DemoSeed.Tenant, CourseId = imported, RequestedBy = requester };
            db.ReadinessRequests.Add(request);
            db.Endorsements.Add(new Endorsement
            {
                Id = Guid.NewGuid(), TenantId = DemoSeed.Tenant, CourseId = imported, RequestId = request.Id, ReviewerId = reviewer,
                StandardsName = "Demo standards", ValidUntil = DateTime.UtcNow.AddYears(3)
            });
            await db.SaveChangesAsync();
        }
        var done = await Get(owner);
        Assert.Equal((true, true, true, true), (done.BroughtInACourse, done.MarkedAStep, done.AskedForReadiness, done.Endorsed));
        Assert.Equal((imported, "My uploaded course"), (done.CourseId!.Value, done.CourseTitle));

        // Someone else's progress never shows: the CTL reviewer owns no courses here, so nothing is done and there is nothing to continue.
        var other = await Get(ctl);
        Assert.Equal((false, false, false, false, (Guid?)null), (other.BroughtInACourse, other.MarkedAStep, other.AskedForReadiness, other.Endorsed, other.CourseId));
    }

    [Fact] public async Task Only_people_who_teach_or_review_get_a_checklist()
    {
        using var f = new Factory(); using var academic = ApiTests.Client(f); using var anonymous = ApiTests.Client(f);
        await f.Seed(); await ApiTests.Login(academic, "academic");
        Assert.Equal(HttpStatusCode.Forbidden, (await academic.GetAsync("/api/v1/getting-started")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/getting-started")).StatusCode);
    }
}
