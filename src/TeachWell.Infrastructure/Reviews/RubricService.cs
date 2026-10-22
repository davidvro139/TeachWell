using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

internal static class ReviewMapping
{
    public static string[] Strings(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    public static string Json(IEnumerable<string>? values) => JsonSerializer.Serialize(values?.ToArray() ?? []);
    public static CriterionDto ToDto(RubricCriterion c) => new(c.Id, c.Code, c.Title, c.Description, c.Category, Strings(c.ElementTypesJson), c.ReviewMethod,
        c.EvidenceGuidance, Strings(c.RatingAnchorsJson), c.Weight, c.Required, Strings(c.AutomatedChecksJson));
    public static RubricVersionDto ToDto(RubricVersion v, string profileName, string? publishedBy, IEnumerable<RubricCriterion> criteria) =>
        new(v.Id, v.ProfileId, profileName, v.VersionNumber, v.Status, v.Revision, v.CreatedAt, v.PublishedAt, publishedBy,
            criteria.OrderBy(c => c.Position).Select(ToDto).ToList());
    // Rubrics target page, assignment or course. Quizzes use assignment criteria; discussions (a prompt students respond to) use both
    // page and assignment criteria.
    public static bool AppliesTo(RubricCriterion c, string elementType)
    {
        var targets = Strings(c.ElementTypesJson);
        return elementType switch
        {
            ReviewVocabulary.QuizType => targets.Contains("assignment"),
            ReviewVocabulary.DiscussionType => targets.Contains("assignment") || targets.Contains("page"),
            _ => targets.Contains(elementType)
        };
    }
}

public sealed class RubricService(AppDbContext db) : IRubricService
{
    public static readonly string[] DeliveryMethods = ["Any", "Traditional", "Hybrid", "Online", "Competency-based"];
    private const string Immutable = "Published rubric versions cannot be changed. Create a new draft version instead.";
    private const string Stale = "This rubric draft changed since you opened it. Reload it and try again.";
    private static bool Authorized(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);

    public async Task<IReadOnlyList<RubricProfileDto>> ListAsync(CurrentUser user, CancellationToken ct)
    {
        if (!Authorized(user)) return [];
        // Criteria element types are stored as JSON, which cannot be aggregated in SQL; materialize, then summarize in memory.
        var profiles = await db.RubricProfiles.AsNoTracking().AsSplitQuery().Include(p => p.Versions).ThenInclude(v => v.Criteria)
            .Where(p => p.TenantId == user.TenantId).OrderBy(p => p.Name).ToListAsync(ct);
        return profiles.Select(p => new RubricProfileDto(p.Id, p.Name, p.DeliveryMethod, p.Versions.OrderByDescending(v => v.VersionNumber)
            .Select(v => new RubricVersionSummaryDto(v.Id, v.VersionNumber, v.Status, v.CreatedAt, v.PublishedAt, v.Criteria.Count,
                v.Criteria.SelectMany(c => ReviewMapping.Strings(c.ElementTypesJson)).Distinct().OrderBy(t => Array.IndexOf(ReviewVocabulary.CriterionTargets, t)).ToArray())).ToList())).ToList();
    }

    private async Task<(RubricProfile Profile, RubricVersion Version)?> Load(CurrentUser user, Guid profileId, Guid versionId, bool tracking, CancellationToken ct)
    {
        if (!Authorized(user)) return null;
        var profile = await db.RubricProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId && p.TenantId == user.TenantId, ct);
        if (profile is null) return null;
        var versions = tracking ? db.RubricVersions : db.RubricVersions.AsNoTracking();
        var version = await versions.Include(v => v.Criteria).SingleOrDefaultAsync(v => v.Id == versionId && v.ProfileId == profileId, ct);
        return version is null ? null : (profile, version);
    }

    private async Task<RubricVersionDto> Dto(RubricProfile profile, RubricVersion version, CancellationToken ct)
    {
        var publisher = version.PublishedBy is null ? null : await db.Users.Where(u => u.Id == version.PublishedBy).Select(u => u.DisplayName).SingleOrDefaultAsync(ct);
        return ReviewMapping.ToDto(version, profile.Name, publisher, version.Criteria);
    }

    public async Task<ServiceResult<RubricVersionDto>> GetVersionAsync(CurrentUser user, Guid profileId, Guid versionId, CancellationToken ct) =>
        await Load(user, profileId, versionId, false, ct) is { } found ? ServiceResult<RubricVersionDto>.Ok(await Dto(found.Profile, found.Version, ct)) : ServiceResult<RubricVersionDto>.NotFound();

    public async Task<ServiceResult<RubricVersionDto>> CreateAsync(CurrentUser user, CreateRubricInput input, CancellationToken ct)
    {
        if (!Authorized(user)) return ServiceResult<RubricVersionDto>.NotFound();
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 160) return ServiceResult<RubricVersionDto>.Invalid("name", "Enter a rubric name of 1–160 characters.");
        if (input.DeliveryMethod is null || !DeliveryMethods.Contains(input.DeliveryMethod)) return ServiceResult<RubricVersionDto>.Invalid("deliveryMethod", "Choose a delivery method: " + string.Join(", ", DeliveryMethods) + ".");
        if (await db.RubricProfiles.AnyAsync(p => p.TenantId == user.TenantId && p.Name == name, ct)) return ServiceResult<RubricVersionDto>.Invalid("name", "A rubric with this name already exists.");
        var profile = new RubricProfile { Id = Guid.NewGuid(), TenantId = user.TenantId, Name = name, DeliveryMethod = input.DeliveryMethod, CreatedBy = user.Id };
        var version = new RubricVersion { Id = Guid.NewGuid(), ProfileId = profile.Id, VersionNumber = 1, CreatedBy = user.Id };
        profile.Versions.Add(version);
        db.RubricProfiles.Add(profile);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = profile.Id, Action = "RubricProfileCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<RubricVersionDto>.Ok(await Dto(profile, version, ct));
    }

    public async Task<ServiceResult<RubricVersionDto>> CreateDraftAsync(CurrentUser user, Guid profileId, CancellationToken ct)
    {
        if (!Authorized(user)) return ServiceResult<RubricVersionDto>.NotFound();
        var profile = await db.RubricProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId && p.TenantId == user.TenantId, ct);
        if (profile is null) return ServiceResult<RubricVersionDto>.NotFound();
        var latest = await db.RubricVersions.AsNoTracking().Include(v => v.Criteria).Where(v => v.ProfileId == profileId).OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync(ct);
        if (latest?.Status == ReviewVocabulary.Draft) return ServiceResult<RubricVersionDto>.Conflict("A draft version already exists. Edit or publish it first.");
        var draft = new RubricVersion { Id = Guid.NewGuid(), ProfileId = profileId, VersionNumber = (latest?.VersionNumber ?? 0) + 1, CreatedBy = user.Id };
        // A new draft starts as a copy; the published source keeps its own immutable criteria.
        draft.Criteria = latest?.Criteria.OrderBy(c => c.Position).Select(c => new RubricCriterion {
            Id = Guid.NewGuid(), VersionId = draft.Id, Position = c.Position, Code = c.Code, Title = c.Title, Description = c.Description, Category = c.Category,
            ElementTypesJson = c.ElementTypesJson, ReviewMethod = c.ReviewMethod, EvidenceGuidance = c.EvidenceGuidance, RatingAnchorsJson = c.RatingAnchorsJson,
            Weight = c.Weight, Required = c.Required, AutomatedChecksJson = c.AutomatedChecksJson }).ToList() ?? [];
        db.RubricVersions.Add(draft);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = draft.Id, Action = "RubricVersionDrafted" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<RubricVersionDto>.Ok(await Dto(profile, draft, ct));
    }

    public async Task<ServiceResult<RubricVersionDto>> UpdateDraftAsync(CurrentUser user, Guid profileId, Guid versionId, UpdateRubricDraftInput input, CancellationToken ct)
    {
        if (await Load(user, profileId, versionId, true, ct) is not var (profile, version)) return ServiceResult<RubricVersionDto>.NotFound();
        if (version.Status != ReviewVocabulary.Draft) return ServiceResult<RubricVersionDto>.Conflict(Immutable);
        var errors = ReviewValidation.Criteria(input.Criteria);
        if (errors.Count > 0) return ServiceResult<RubricVersionDto>.Invalid(errors);
        if (version.Revision != input.Revision) return ServiceResult<RubricVersionDto>.Conflict(Stale);
        db.Entry(version).Property(v => v.Revision).OriginalValue = input.Revision;
        version.Revision = Guid.NewGuid();
        // Delete then insert in one transaction so reused codes never collide with the unique index.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.RubricCriteria.RemoveRange(version.Criteria);
        await db.SaveChangesAsync(ct);
        version.Criteria = input.Criteria!.Select((c, i) => new RubricCriterion {
            Id = Guid.NewGuid(), VersionId = version.Id, Position = i + 1, Code = c.Code!, Title = c.Title!.Trim(), Description = c.Description?.Trim() ?? "",
            Category = c.Category!, ElementTypesJson = ReviewMapping.Json(c.ElementTypes), ReviewMethod = c.ReviewMethod!, EvidenceGuidance = c.EvidenceGuidance?.Trim() ?? "",
            RatingAnchorsJson = ReviewMapping.Json(c.RatingAnchors!.Select(a => a.Trim())), Weight = c.Weight, Required = c.Required,
            AutomatedChecksJson = ReviewMapping.Json(c.AutomatedChecks) }).ToList();
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = version.Id, Action = "RubricDraftUpdated" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ServiceResult<RubricVersionDto>.Ok(await Dto(profile, version, ct));
    }

    public async Task<ServiceResult<RubricVersionDto>> PublishAsync(CurrentUser user, Guid profileId, Guid versionId, Guid revision, CancellationToken ct)
    {
        if (await Load(user, profileId, versionId, true, ct) is not var (profile, version)) return ServiceResult<RubricVersionDto>.NotFound();
        if (version.Status != ReviewVocabulary.Draft) return ServiceResult<RubricVersionDto>.Conflict(Immutable);
        if (version.Criteria.Count == 0) return ServiceResult<RubricVersionDto>.Invalid("criteria", "Add at least one criterion before publishing.");
        if (version.Revision != revision) return ServiceResult<RubricVersionDto>.Conflict(Stale);
        db.Entry(version).Property(v => v.Revision).OriginalValue = revision;
        version.Revision = Guid.NewGuid(); version.Status = ReviewVocabulary.Published; version.PublishedAt = DateTime.UtcNow; version.PublishedBy = user.Id;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = version.Id, Action = "RubricVersionPublished" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<RubricVersionDto>.Ok(await Dto(profile, version, ct));
    }
}
