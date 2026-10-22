using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

// Phase 11 (ADR 014): college-wide results for CTL reviewers and Academic administrators. Course metadata and figures only; content needs
// ownership or a share. Nothing names or ranks instructors or reviewers.
public sealed class OversightService(AppDbContext db, IGuideService guide, ReadinessOptions? options = null, OversightOptions? oversight = null) : IOversightService
{
    private readonly ReadinessOptions options = options ?? new ReadinessOptions();
    private readonly OversightOptions oversight = oversight ?? new OversightOptions();
    private const int Weeks = 12, TopChecks = 10, EndorsementLimit = 50;
    private static bool CanView(CurrentUser user) => user.Roles.Contains(Roles.Reviewer) || user.Roles.Contains(Roles.AcademicAdmin);

    private static IReadOnlyDictionary<string, string> Labels => ReadinessStates.Labels;

    public async Task<ServiceResult<int>> RefreshAsync(CurrentUser user, CancellationToken ct)
    {
        if (!user.Roles.Contains(Roles.Reviewer)) return ServiceResult<int>.NotFound();
        var count = await guide.RecordAllAsync(user.TenantId, ct);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = user.TenantId, Action = "OversightRefreshed" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<int>.Ok(count);
    }

    public async Task<ServiceResult<OversightDto>> GetAsync(CurrentUser user, string? department, string? program, CancellationToken ct)
    {
        // Defense in depth: route claims can lag a role revocation until the session is revalidated.
        if (!CanView(user)) return ServiceResult<OversightDto>.NotFound();
        var now = DateTime.UtcNow;
        var all = await db.Courses.AsNoTracking().Where(c => c.TenantId == user.TenantId)
            .Select(c => new { c.Id, c.Code, c.Title, c.Department, c.Program, c.DeliveryMode }).ToListAsync(ct);
        static List<string> Values(IEnumerable<string?> source) => source.Select(StandardSetVocabulary.NormalizeDepartment).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        // Shown as recorded on courses, whatever casing the filter used.
        static string? Canonical(string? typed, List<string> known) => StandardSetVocabulary.NormalizeDepartment(typed) is { } t
            ? known.FirstOrDefault(d => string.Equals(d, t, StringComparison.OrdinalIgnoreCase)) ?? t : null;
        static bool Matches(string? value, string? wanted) => wanted is null || string.Equals(StandardSetVocabulary.NormalizeDepartment(value), wanted, StringComparison.OrdinalIgnoreCase);
        var departments = Values(all.Select(c => c.Department));
        var programs = Values(all.Select(c => c.Program));
        var wanted = Canonical(department, departments);
        var wantedProgram = Canonical(program, programs);
        var courses = all.Where(c => Matches(c.Department, wanted) && Matches(c.Program, wantedProgram)).OrderBy(c => c.Code).ToList();
        var ids = courses.Select(c => c.Id).ToList();
        var visible = (await db.Courses.Where(c => ids.Contains(c.Id) && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id))).Select(c => c.Id).ToListAsync(ct)).ToHashSet();

        var records = await db.CourseGuideRecords.AsNoTracking().Where(r => ids.Contains(r.CourseId)).ToListAsync(ct);
        var latest = records.GroupBy(r => r.CourseId).ToDictionary(g => g.Key, g => g.MaxBy(r => r.Day)!);

        var readiness = await ReadinessStates.ForCoursesAsync(db, ids, options.ChangeThresholdPercent, ct);
        var states = readiness.States;
        var endorsements = readiness.Endorsements;
        var Due = readiness.RecheckDue;

        var aspectCounts = latest.Values.Select(r => CourseGuideRecord.Counts(r.OpenByAspectJson)).ToList();
        var issuesByAspect = ReviewAspects.All.Select(a => new AspectIssuesDto(a, GuideCatalog.Aspects[a].Label,
            aspectCounts.Sum(c => c.GetValueOrDefault(a)), aspectCounts.Count(c => c.GetValueOrDefault(a) > 0))).ToList();
        var checkCounts = latest.Values.Select(r => CourseGuideRecord.Counts(r.OpenByCheckJson)).ToList();
        var topChecks = checkCounts.SelectMany(c => c).GroupBy(kv => kv.Key)
            .Select(g => (Check: CheckRegistry.Find(g.Key), Courses: g.Count(kv => kv.Value > 0), Places: g.Sum(kv => kv.Value)))
            .Where(x => x.Check is not null && x.Courses > 0).OrderByDescending(x => x.Courses).ThenByDescending(x => x.Places).ThenBy(x => x.Check!.Code, StringComparer.Ordinal)
            .Take(TopChecks).Select(x => new CheckIssuesDto(x.Check!.Code, x.Check.Name, x.Check.Aspect, x.Courses, x.Places)).ToList();

        var tickets = await db.SupportTickets.AsNoTracking()
            .Where(t => t.TenantId == user.TenantId && t.Origin == SupportVocabulary.GuideOrigin && t.CourseId != null && ids.Contains(t.CourseId.Value))
            .Select(t => new { t.Category, t.Status }).ToListAsync(ct);
        var help = new GuideHelpDto(tickets.Count, tickets.Count(t => t.Status is not (SupportVocabulary.Resolved or SupportVocabulary.Closed)),
            tickets.GroupBy(t => t.Category).Select(g => new CategoryCountDto(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Category).ToList());

        var progress = new List<ProgressPointDto>();
        for (var w = Weeks - 1; w >= 0; w--)
        {
            var end = now.Date.AddDays(-7 * w);
            var asOf = records.Where(r => r.Day <= end).GroupBy(r => r.CourseId).Select(g => g.MaxBy(r => r.Day)!).ToList();
            if (asOf.Count > 0) progress.Add(new(end, asOf.Count, asOf.Sum(r => r.OpenPlaces), asOf.Sum(r => r.DonePlaces), asOf.Sum(r => r.NotApplicablePlaces)));
        }

        var latestEndorsement = endorsements.GroupBy(e => e.CourseId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.EndorsedAt)!);
        var rows = courses.Select(c =>
        {
            var r = latest.GetValueOrDefault(c.Id);
            var e = latestEndorsement.GetValueOrDefault(c.Id);
            return new OversightCourseDto(c.Id, c.Code, c.Title, StandardSetVocabulary.NormalizeDepartment(c.Department), c.DeliveryMode, states[c.Id], Labels[states[c.Id]],
                r?.RecordedAt, r?.OpenSteps, r?.RequiredOpenSteps, visible.Contains(c.Id), StandardSetVocabulary.NormalizeDepartment(c.Program), e?.EndorsedAt, e?.ValidUntil);
        }).ToList();
        var byId = courses.ToDictionary(c => c.Id);
        var endorsementRows = endorsements.OrderByDescending(e => e.EndorsedAt).Take(EndorsementLimit)
            .Select(e => new OversightEndorsementDto(e.CourseId, byId[e.CourseId].Code, byId[e.CourseId].Title, StandardSetVocabulary.NormalizeDepartment(byId[e.CourseId].Department),
                e.Wording, e.EndorsedAt, e.ValidUntil, Due(e))).ToList();

        var lastAuto = await db.AuditLogs.AsNoTracking().Where(a => a.Action == OversightOptions.AutoRecheckAction && a.EntityId == user.TenantId)
            .OrderByDescending(a => a.Timestamp).Select(a => (DateTime?)a.Timestamp).FirstOrDefaultAsync(ct);
        var label = string.Join(" · ", new[] { wanted, wantedProgram }.OfType<string>());
        var scope = new ReportScope($"Oversight · {(label.Length == 0 ? "whole college" : label)}", now,
        [
            "Figures describe courses, not instructors or reviewers. No one is named or ranked.",
            "Issue figures come from each course's most recent improvement-guide check (\"last checked\"). Courses never checked count toward readiness but not toward issues; CTL reviewers can re-check every course.",
            "Progress over time sums each course's latest check on or before each week's end, so it moves when courses are re-checked, not only when they change.",
            "Help requests count only those sent from an improvement-guide step.",
            "Readiness states and endorsements are CTL decisions recorded in TeachWell. An endorsement is not accreditation, compliance or certification.",
            "Course titles and figures are shown here; course content still requires ownership or an explicit share.",
        ]);
        return ServiceResult<OversightDto>.Ok(new(scope, wanted, departments, courses.Count, latest.Count,
            OversightVocabulary.States.Select(s => new ReadinessStateCountDto(s, Labels[s], states.Values.Count(v => v == s))).ToList(),
            issuesByAspect, topChecks, help, progress, rows, endorsementRows, user.Roles.Contains(Roles.Reviewer), wantedProgram, programs,
            oversight.AutoRecheckHours, lastAuto));
    }
}
