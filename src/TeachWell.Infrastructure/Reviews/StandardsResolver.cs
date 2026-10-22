using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

// Resolves which published standard-set version governs a course (D1) and turns a version into the engine's CheckProfile.
internal static class StandardsResolver
{
    public const string DefaultsName = "TeachWell built-in defaults";

    public sealed record Resolved(StandardSet Set, StandardSetVersion Version, IReadOnlyList<StandardCheckSetting> Checks);

    public static async Task<Resolved?> ResolveAsync(AppDbContext db, Guid tenantId, string? deliveryMode, string? department, CancellationToken ct)
    {
        var sets = await db.StandardSets.AsNoTracking().Where(s => s.TenantId == tenantId).ToListAsync(ct);
        var ids = sets.Select(s => s.Id).ToList();
        var published = await db.StandardSetVersions.AsNoTracking().Where(v => ids.Contains(v.SetId) && v.Status == ReviewVocabulary.Published).ToListAsync(ct);
        // Each set contributes only its latest published version.
        var latest = published.GroupBy(v => v.SetId).ToDictionary(g => g.Key, g => g.MaxBy(v => v.VersionNumber)!);
        var set = StandardSetSelection.Pick(sets.Where(s => latest.ContainsKey(s.Id)), s => (s.DeliveryMode, s.Department), deliveryMode, department);
        return set is null ? null : new(set, latest[set.Id], StandardSetContent.Parse(latest[set.Id].ChecksJson));
    }

    public static async Task<Resolved?> LoadAsync(AppDbContext db, Guid? versionId, CancellationToken ct)
    {
        if (versionId is null) return null;
        var version = await db.StandardSetVersions.AsNoTracking().SingleAsync(v => v.Id == versionId, ct);
        var set = await db.StandardSets.AsNoTracking().SingleAsync(s => s.Id == version.SetId, ct);
        return new(set, version, StandardSetContent.Parse(version.ChecksJson));
    }

    public static CheckProfile Profile(Resolved? resolved) => resolved is null ? CheckProfile.Default : StandardSetContent.ToProfile(resolved.Checks);

    public static IReadOnlyDictionary<string, CheckGuidance> Guidance(Resolved? resolved) => resolved is null
        ? CheckRegistry.All.ToDictionary(c => c.Code, c => c.Guidance)
        : resolved.Checks.ToDictionary(c => c.Code, c => c.Guidance);

    public static AppliedStandardsDto Describe(Resolved? resolved, string? courseMode, string? courseDepartment, string? reason = null)
    {
        if (resolved is null)
            return new(null, null, DefaultsName, null, "College-wide", reason ?? "No published standard set applies to this course, so TeachWell's built-in defaults are used.", null);
        var (set, version) = (resolved.Set, resolved.Version);
        var scope = StandardSetVocabulary.DescribeScope(set.DeliveryMode, set.Department);
        reason ??= (set.DeliveryMode, StandardSetVocabulary.NormalizeDepartment(set.Department)) switch
        {
            (null, null) => courseMode is null && StandardSetVocabulary.NormalizeDepartment(courseDepartment) is null
                ? "The college-wide set applies because this course has no delivery mode or department recorded."
                : "The college-wide set applies because no published set targets this course's delivery mode or department.",
            (not null, not null) => "This set targets both this course's department and delivery mode.",
            (null, not null) => "This set targets this course's department.",
            _ => "This set targets this course's delivery mode."
        };
        return new(set.Id, version.Id, set.Name, version.VersionNumber, scope, reason, version.PublishedAt);
    }
}
