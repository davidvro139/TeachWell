using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

// CTL standards library (ADR 011 slice 2). Versions follow the rubric pattern: one draft at a time, optimistic concurrency
// on Revision, immutable once published (EF guard plus PostgreSQL trigger), every change audited.
public sealed class StandardSetService(AppDbContext db, InstitutionSettings? institution = null) : IStandardSetService
{
    private readonly TimeZoneInfo timeZone = (institution ?? InstitutionSettings.Utc).TimeZone;
    private const string Immutable = "Published standard-set versions cannot be changed. Create a new draft version instead.";
    private const string Stale = "This standard-set draft changed since you opened it. Reload it and try again.";
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);
    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));

    public IReadOnlyList<CheckCatalogEntryDto> Catalog() => CheckRegistry.All.Select(c => new CheckCatalogEntryDto(c.Code, c.Name, c.Aspect, c.Target.ToString(), c.DefaultSeverity,
        c.Parameters.Select(p => new CheckParameterDto(p.Key, p.Label, p.Kind.ToString(), p.DefaultNumber, p.DefaultWords, p.Min, p.Max, p.MaxItems, p.MaxItemLength)).ToList(),
        c.Guidance, c.WcagCriteria)).ToList();

    public async Task<IReadOnlyList<StandardSetDto>> ListAsync(CurrentUser user, CancellationToken ct)
    {
        if (!IsReviewer(user)) return [];
        var sets = await db.StandardSets.AsNoTracking().Include(s => s.Versions).Where(s => s.TenantId == user.TenantId).ToListAsync(ct);
        // Most general first, matching how selection falls back (D1).
        return sets.OrderBy(s => StandardSetSelection.Specificity(s.DeliveryMode, s.Department, s.DeliveryMode, s.Department)).ThenBy(s => s.Name).Select(s => new StandardSetDto(
            s.Id, s.Name, s.DeliveryMode, s.Department, StandardSetVocabulary.DescribeScope(s.DeliveryMode, s.Department),
            s.Versions.OrderByDescending(v => v.VersionNumber).Select(v =>
            {
                var checks = StandardSetContent.Parse(v.ChecksJson);
                return new StandardSetVersionSummaryDto(v.Id, v.VersionNumber, v.Status, v.CreatedAt, v.PublishedAt, checks.Count(c => c.Enabled), checks.Count(c => c.Required));
            }).ToList())).ToList();
    }

    private async Task<(StandardSet Set, StandardSetVersion Version)?> Load(CurrentUser user, Guid setId, Guid versionId, bool tracking, CancellationToken ct)
    {
        if (!IsReviewer(user)) return null;
        var set = await db.StandardSets.AsNoTracking().SingleOrDefaultAsync(s => s.Id == setId && s.TenantId == user.TenantId, ct);
        if (set is null) return null;
        var versions = tracking ? db.StandardSetVersions : db.StandardSetVersions.AsNoTracking();
        var version = await versions.SingleOrDefaultAsync(v => v.Id == versionId && v.SetId == setId, ct);
        return version is null ? null : (set, version);
    }

    private async Task<StandardSetVersionDto> Dto(StandardSet set, StandardSetVersion version, CancellationToken ct)
    {
        var publisher = version.PublishedBy is null ? null : await db.Users.Where(u => u.Id == version.PublishedBy).Select(u => u.DisplayName).SingleOrDefaultAsync(ct);
        var order = CheckRegistry.All.Select((c, i) => (c.Code, i)).ToDictionary(x => x.Code, x => x.i);
        return new(version.Id, set.Id, set.Name, StandardSetVocabulary.DescribeScope(set.DeliveryMode, set.Department), version.VersionNumber, version.Status, version.Revision,
            version.Notes, version.CreatedAt, version.PublishedAt, publisher,
            StandardSetContent.Parse(version.ChecksJson).OrderBy(c => order.GetValueOrDefault(c.Code, int.MaxValue)).ToList(),
            EndorsementRequirements.Parse(version.EndorsementJson));
    }

    public async Task<ServiceResult<StandardSetVersionDto>> GetVersionAsync(CurrentUser user, Guid setId, Guid versionId, CancellationToken ct) =>
        await Load(user, setId, versionId, false, ct) is { } found ? ServiceResult<StandardSetVersionDto>.Ok(await Dto(found.Set, found.Version, ct)) : ServiceResult<StandardSetVersionDto>.NotFound();

    public async Task<ServiceResult<StandardSetVersionDto>> CreateAsync(CurrentUser user, CreateStandardSetInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<StandardSetVersionDto>.NotFound();
        var errors = new Dictionary<string, string[]>();
        var name = input.Name?.Trim();
        var mode = string.IsNullOrWhiteSpace(input.DeliveryMode) ? null : input.DeliveryMode;
        var department = StandardSetVocabulary.NormalizeDepartment(input.Department);
        if (string.IsNullOrEmpty(name) || name.Length > StandardSetVocabulary.MaxNameLength) errors["name"] = [$"Enter a name of 1–{StandardSetVocabulary.MaxNameLength} characters."];
        if (mode is not null && !StandardSetVocabulary.DeliveryModes.Contains(mode)) errors["deliveryMode"] = ["Choose Online, Hybrid, FaceToFace, or leave it blank for any mode."];
        if (department?.Length > StandardSetVocabulary.MaxDepartmentLength) errors["department"] = [$"Department is limited to {StandardSetVocabulary.MaxDepartmentLength} characters."];
        if (errors.Count > 0) return ServiceResult<StandardSetVersionDto>.Invalid(errors);
        var scopeKey = StandardSetVocabulary.ScopeKey(mode, department);
        if (await db.StandardSets.AnyAsync(s => s.TenantId == user.TenantId && s.ScopeKey == scopeKey, ct))
            return ServiceResult<StandardSetVersionDto>.Conflict($"A standard set already covers this scope ({StandardSetVocabulary.DescribeScope(mode, department)}). Create a new version of it instead.");
        var set = new StandardSet { Id = Guid.NewGuid(), TenantId = user.TenantId, Name = name!, DeliveryMode = mode, Department = department, ScopeKey = scopeKey, CreatedBy = user.Id };
        // A new set starts from the most specific set that currently applies to its scope, so a department set inherits college-wide choices.
        var basis = await StandardsResolver.ResolveAsync(db, user.TenantId, mode, department, ct);
        var version = new StandardSetVersion
        {
            Id = Guid.NewGuid(), SetId = set.Id, VersionNumber = 1, CreatedBy = user.Id,
            ChecksJson = StandardSetContent.Serialize(StandardSetContent.WithNewChecks(basis?.Checks ?? StandardSetContent.Defaults())),
            Notes = basis is null ? "Started from TeachWell's built-in defaults." : $"Started from {basis.Set.Name} v{basis.Version.VersionNumber}."
        };
        set.Versions.Add(version);
        db.StandardSets.Add(set);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = set.Id, Action = "StandardSetCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<StandardSetVersionDto>.Ok(await Dto(set, version, ct));
    }

    public async Task<ServiceResult<StandardSetVersionDto>> CreateDraftAsync(CurrentUser user, Guid setId, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<StandardSetVersionDto>.NotFound();
        var set = await db.StandardSets.AsNoTracking().SingleOrDefaultAsync(s => s.Id == setId && s.TenantId == user.TenantId, ct);
        if (set is null) return ServiceResult<StandardSetVersionDto>.NotFound();
        var latest = await db.StandardSetVersions.AsNoTracking().Where(v => v.SetId == setId).OrderByDescending(v => v.VersionNumber).FirstAsync(ct);
        if (latest.Status == ReviewVocabulary.Draft) return ServiceResult<StandardSetVersionDto>.Conflict("A draft version already exists. Edit or publish it first.");
        var draft = new StandardSetVersion
        {
            Id = Guid.NewGuid(), SetId = setId, VersionNumber = latest.VersionNumber + 1, CreatedBy = user.Id,
            ChecksJson = StandardSetContent.Serialize(StandardSetContent.WithNewChecks(StandardSetContent.Parse(latest.ChecksJson))),
            EndorsementJson = latest.EndorsementJson
        };
        db.StandardSetVersions.Add(draft);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = draft.Id, Action = "StandardSetVersionDrafted" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<StandardSetVersionDto>.Ok(await Dto(set, draft, ct));
    }

    public async Task<ServiceResult<StandardSetVersionDto>> UpdateDraftAsync(CurrentUser user, Guid setId, Guid versionId, UpdateStandardSetDraftInput input, CancellationToken ct)
    {
        if (await Load(user, setId, versionId, true, ct) is not var (set, version)) return ServiceResult<StandardSetVersionDto>.NotFound();
        if (version.Status != ReviewVocabulary.Draft) return ServiceResult<StandardSetVersionDto>.Conflict(Immutable);
        var errors = StandardSetContent.Validate(input.Checks);
        if ((input.Notes?.Length ?? 0) > StandardSetVocabulary.MaxNotesLength) errors["notes"] = [$"Notes are limited to {StandardSetVocabulary.MaxNotesLength:N0} characters."];
        if (input.Endorsement is { } e)
        {
            if (e.MinimumItemReviews is < 0 or > EndorsementRequirements.MaxItemReviews)
                errors["endorsement.minimumItemReviews"] = [$"Choose 0–{EndorsementRequirements.MaxItemReviews} item reviews."];
            if (e.RubricProfileId is { } profile && !await db.RubricProfiles.AnyAsync(p => p.Id == profile && p.TenantId == user.TenantId
                && db.RubricVersions.Any(v => v.ProfileId == profile && v.Status == ReviewVocabulary.Published), ct))
                errors["endorsement.rubricProfileId"] = ["Choose a rubric with a published version."];
        }
        if (errors.Count > 0) return ServiceResult<StandardSetVersionDto>.Invalid(errors);
        if (version.Revision != input.Revision) return ServiceResult<StandardSetVersionDto>.Conflict(Stale);
        db.Entry(version).Property(v => v.Revision).OriginalValue = input.Revision;
        version.Revision = Guid.NewGuid();
        version.Notes = input.Notes?.Trim() ?? "";
        if (input.Endorsement is { } requirements) version.EndorsementJson = requirements.Serialize();
        version.ChecksJson = StandardSetContent.Serialize(input.Checks!.Select(c => c with
        {
            Numbers = c.Numbers ?? new Dictionary<string, decimal>(),
            Lists = (c.Lists ?? new Dictionary<string, IReadOnlyList<string>>()).ToDictionary(l => l.Key, l => (IReadOnlyList<string>)l.Value.Select(w => w.Trim()).ToList()),
            Guidance = new(c.Guidance.Why.Trim(), c.Guidance.HowToFix.Trim(), c.Guidance.Example?.Trim() ?? "")
        }));
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = version.Id, Action = "StandardSetDraftUpdated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<StandardSetVersionDto>.Ok(await Dto(set, version, ct));
    }

    public async Task<ServiceResult<StandardSetVersionDto>> PublishAsync(CurrentUser user, Guid setId, Guid versionId, Guid revision, CancellationToken ct)
    {
        if (await Load(user, setId, versionId, true, ct) is not var (set, version)) return ServiceResult<StandardSetVersionDto>.NotFound();
        if (version.Status != ReviewVocabulary.Draft) return ServiceResult<StandardSetVersionDto>.Conflict(Immutable);
        if (version.Revision != revision) return ServiceResult<StandardSetVersionDto>.Conflict(Stale);
        // Revalidated at publish: a draft saved before a check was added to TeachWell must still be complete.
        if (StandardSetContent.Validate(StandardSetContent.Parse(version.ChecksJson)) is { Count: > 0 } errors) return ServiceResult<StandardSetVersionDto>.Invalid(errors);
        db.Entry(version).Property(v => v.Revision).OriginalValue = revision;
        version.Revision = Guid.NewGuid(); version.Status = ReviewVocabulary.Published; version.PublishedAt = DateTime.UtcNow; version.PublishedBy = user.Id;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = version.Id, Action = "StandardSetVersionPublished" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<StandardSetVersionDto>.Ok(await Dto(set, version, ct));
    }

    public async Task<ServiceResult<StandardsPreviewDto>> PreviewAsync(CurrentUser user, Guid setId, Guid versionId, Guid courseId, CancellationToken ct)
    {
        if (await Load(user, setId, versionId, false, ct) is not var (_, version)) return ServiceResult<StandardsPreviewDto>.NotFound();
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return ServiceResult<StandardsPreviewDto>.NotFound();
        var current = await StandardsResolver.ResolveAsync(db, user.TenantId, course.DeliveryMode, course.Department, ct);
        var currentProfile = StandardsResolver.Profile(current);
        var proposedChecks = StandardSetContent.Parse(version.ChecksJson);
        var proposedProfile = StandardSetContent.ToProfile(proposedChecks);

        // Latest snapshot of every live page and assignment, plus the course structure as it is now (built, not saved).
        var elements = await db.Elements.AsNoTracking().Where(e => e.CourseId == courseId && ReviewVocabulary.CheckedTypes.Contains(e.Type) && e.SourceStatus != "Removed")
            .Select(e => new { e.Id, e.Type }).ToListAsync(ct);
        var ids = elements.Select(e => e.Id).ToList();
        var snapshots = (await db.ElementSnapshots.AsNoTracking().Where(s => ids.Contains(s.ElementId)).ToListAsync(ct))
            .GroupBy(s => s.ElementId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.FetchedAt)!);
        var structure = await CourseStructureCapture.BuildAsync(db, courseId, ct);

        var counts = CheckRegistry.All.ToDictionary(c => c.Code, _ => (Current: 0, Proposed: 0));
        var limitations = new HashSet<string>();
        void Tally(CheckReport now, CheckReport proposed)
        {
            foreach (var f in now.Findings) counts[f.CheckCode] = counts[f.CheckCode] with { Current = counts[f.CheckCode].Current + 1 };
            foreach (var f in proposed.Findings) counts[f.CheckCode] = counts[f.CheckCode] with { Proposed = counts[f.CheckCode].Proposed + 1 };
            limitations.UnionWith(proposed.Limitations);
        }
        var checkedElements = 0;
        foreach (var e in elements)
        {
            if (!snapshots.TryGetValue(e.Id, out var s)) continue;
            checkedElements++;
            var evidence = ContentEvidence.Parse(s.EvidenceJson);
            Tally(ReviewEngine.RunElement(e.Type, s.SourceStatus, evidence, currentProfile, s.PlainText), ReviewEngine.RunElement(e.Type, s.SourceStatus, evidence, proposedProfile, s.PlainText));
        }
        Tally(ReviewEngine.RunCourse(structure, currentProfile, timeZone), ReviewEngine.RunCourse(structure, proposedProfile, timeZone));

        var rows = CheckRegistry.All.Select(c => new StandardsPreviewRowDto(c.Code, c.Name, c.Aspect, proposedProfile.For(c).Enabled, counts[c.Code].Current, counts[c.Code].Proposed)).ToList();
        return ServiceResult<StandardsPreviewDto>.Ok(new(course.Id, course.Title, StandardsResolver.Describe(current, course.DeliveryMode, course.Department),
            checkedElements, true, rows.Sum(r => r.Current), rows.Sum(r => r.Proposed), rows, limitations.Order().ToList()));
    }

    public async Task<ServiceResult<CourseStandardsDto>> GetForCourseAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return ServiceResult<CourseStandardsDto>.NotFound();
        return ServiceResult<CourseStandardsDto>.Ok(await CourseDto(user, course, ct));
    }

    private async Task<CourseStandardsDto> CourseDto(CurrentUser user, Course course, CancellationToken ct)
    {
        var applied = await StandardsResolver.ResolveAsync(db, user.TenantId, course.DeliveryMode, course.Department, ct);
        return new(course.Id, course.DeliveryMode, course.Department, IsReviewer(user), StandardsResolver.Describe(applied, course.DeliveryMode, course.Department), course.Program);
    }

    // D4: delivery mode and department are maintained by the CTL; owners see them but do not set them.
    public async Task<ServiceResult<CourseStandardsDto>> ClassifyCourseAsync(CurrentUser user, Guid courseId, CourseClassificationInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<CourseStandardsDto>.NotFound();
        var course = await Accessible(user).SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return ServiceResult<CourseStandardsDto>.NotFound();
        var mode = string.IsNullOrWhiteSpace(input.DeliveryMode) ? null : input.DeliveryMode;
        var department = StandardSetVocabulary.NormalizeDepartment(input.Department);
        var program = StandardSetVocabulary.NormalizeDepartment(input.Program);
        var errors = new Dictionary<string, string[]>();
        if (mode is not null && !StandardSetVocabulary.DeliveryModes.Contains(mode)) errors["deliveryMode"] = ["Choose Online, Hybrid, FaceToFace, or leave it blank."];
        if (department?.Length > StandardSetVocabulary.MaxDepartmentLength) errors["department"] = [$"Department is limited to {StandardSetVocabulary.MaxDepartmentLength} characters."];
        if (program?.Length > StandardSetVocabulary.MaxDepartmentLength) errors["program"] = [$"Program is limited to {StandardSetVocabulary.MaxDepartmentLength} characters."];
        if (errors.Count > 0) return ServiceResult<CourseStandardsDto>.Invalid(errors);
        if (course.DeliveryMode != mode || course.Department != department || course.Program != program)
        {
            course.DeliveryMode = mode; course.Department = department; course.Program = program; course.Revision = Guid.NewGuid();
            db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = course.Id, Action = "CourseClassificationChanged" });
            await db.SaveChangesAsync(ct);
        }
        return ServiceResult<CourseStandardsDto>.Ok(await CourseDto(user, course, ct));
    }
}
