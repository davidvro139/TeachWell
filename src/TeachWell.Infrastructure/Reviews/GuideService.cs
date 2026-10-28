using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reviews;

// Builds the instructor improvement guide on demand: the course's standard set runs against the latest snapshot of every page,
// assignment, quiz and discussion and against the course structure as it is now. Nothing is stored except the instructor's progress.
public sealed class GuideService(AppDbContext db, InstitutionSettings? institution = null) : IGuideService
{
    private readonly TimeZoneInfo timeZone = (institution ?? InstitutionSettings.Utc).TimeZone;

    // Course ACL, as for every course resource: tenant plus ownership or an explicit share.
    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));
    private static bool CanMark(CurrentUser user, Course course) => course.OwnerId == user.Id && user.Roles.Contains(Roles.Instructor);

    public async Task<ServiceResult<CourseGuideDto>> GetAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        return course is null ? ServiceResult<CourseGuideDto>.NotFound() : ServiceResult<CourseGuideDto>.Ok((await BuildAsync(user, course, ct)).Dto);
    }

    public async Task<ServiceResult<CourseGuideDto>> MarkAsync(CurrentUser user, Guid courseId, GuideProgressInput input, CancellationToken ct)
    {
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        // Progress belongs to the course owner; a reviewer with access reads the guide but does not mark it.
        if (course is null || !CanMark(user, course)) return ServiceResult<CourseGuideDto>.NotFound();
        var keys = (input.Keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        var status = input.Status;
        var reason = input.Reason?.Trim() ?? "";
        if (status is not (GuideVocabulary.Open or GuideVocabulary.Done or GuideVocabulary.NotApplicable))
            return ServiceResult<CourseGuideDto>.Invalid("status", "Choose Done, NotApplicable or Open.");
        if (keys.Count is 0 or > GuideVocabulary.MaxKeysPerChange || keys.Any(k => k.Length > GuideVocabulary.MaxKeyLength))
            return ServiceResult<CourseGuideDto>.Invalid("keys", $"Choose 1–{GuideVocabulary.MaxKeysPerChange:N0} places.");
        if (status == GuideVocabulary.NotApplicable && reason.Length == 0)
            return ServiceResult<CourseGuideDto>.Invalid("reason", "Say why this does not apply, so a reviewer (and you, later) can see the reasoning.");
        if (reason.Length > GuideVocabulary.MaxReasonLength)
            return ServiceResult<CourseGuideDto>.Invalid("reason", $"Keep the reason to {GuideVocabulary.MaxReasonLength} characters.");
        var current = await BuildAsync(user, course, ct);
        if (keys.Any(k => !current.Detected.Contains(k)))
            return ServiceResult<CourseGuideDto>.Invalid("keys", "Some of these places are no longer detected. Reload the guide and try again.");

        var marks = await db.GuideProgress.Where(p => p.CourseId == courseId && keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, ct);
        var now = DateTime.UtcNow;
        foreach (var key in keys)
        {
            marks.TryGetValue(key, out var mark);
            if (status == GuideVocabulary.Open) { if (mark is not null) db.GuideProgress.Remove(mark); continue; }
            if (mark is null) db.GuideProgress.Add(mark = new() { Id = Guid.NewGuid(), TenantId = course.TenantId, CourseId = courseId, Key = key });
            mark.Status = status; mark.Reason = status == GuideVocabulary.NotApplicable ? reason : ""; mark.SetBy = user.Id; mark.SetAt = now;
        }
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = courseId, Action = "GuideProgress" + status });
        await db.SaveChangesAsync(ct);
        return ServiceResult<CourseGuideDto>.Ok((await BuildAsync(user, course, ct)).Dto);
    }

    private sealed record Place(string Where, string? Module, Guid? ElementId, string? SourceUrl);

    private async Task<(CourseGuideDto Dto, HashSet<string> Detected)> BuildAsync(CurrentUser user, Course course, CancellationToken ct)
    {
        var standards = await StandardsResolver.ResolveAsync(db, course.TenantId, course.DeliveryMode, course.Department, ct);
        var profile = StandardsResolver.Profile(standards);
        var guidance = StandardsResolver.Guidance(standards);
        var required = standards?.Checks.Where(c => c.Required).Select(c => c.Code).ToHashSet() ?? [];

        var elements = await db.Elements.AsNoTracking().Where(e => e.CourseId == course.Id && e.Type != ReviewVocabulary.CourseType && e.SourceStatus != "Removed").ToListAsync(ct);
        var byId = elements.ToDictionary(e => e.Id);
        var content = elements.Where(e => ReviewVocabulary.CheckedTypes.Contains(e.Type)).ToList();
        var contentIds = content.Select(e => e.Id).ToList();
        // Only the latest snapshot of each item is loaded.
        var latestIds = await db.ElementSnapshots.AsNoTracking().Where(s => contentIds.Contains(s.ElementId)).GroupBy(s => s.ElementId)
            .Select(g => g.OrderByDescending(s => s.FetchedAt).Select(s => s.Id).First()).ToListAsync(ct);
        var snapshots = await db.ElementSnapshots.AsNoTracking().Where(s => latestIds.Contains(s.Id)).ToDictionaryAsync(s => s.ElementId, ct);
        var structure = await CourseStructureCapture.BuildAsync(db, course.Id, ct);

        // Where each module, and each item placed in a module, sits in the course; an item in several modules counts at its first.
        var moduleOf = new Dictionary<Guid, (int Index, string Title)>();
        foreach (var (module, index) in structure.Modules.Select((m, i) => (m, i)))
        {
            moduleOf.TryAdd(module.ElementId, (index, module.Title));
            foreach (var item in module.Items) moduleOf.TryAdd(item.ElementId, (index, module.Title));
        }
        // Check items in course order (module, then position in the module), so each step lists its places the way an instructor
        // works through the course, not in storage order. Items outside any module come last.
        var orderOf = new Dictionary<Guid, (int Module, int Item)>();
        foreach (var (module, index) in structure.Modules.Select((m, i) => (m, i)))
            foreach (var (item, position) in module.Items.Select((it, p) => (it, p))) orderOf.TryAdd(item.ElementId, (index, position));
        content = content
            .OrderBy(e => orderOf.TryGetValue(e.Id, out var o) ? o.Module : int.MaxValue)
            .ThenBy(e => orderOf.TryGetValue(e.Id, out var o) ? o.Item : e.Position)
            .ThenBy(e => e.Title, StringComparer.Ordinal).ToList();
        var outcomes = (structure.Outcomes ?? []).ToDictionary(o => o.Id);
        Place PlaceOf(Guid? id)
        {
            if (id is { } k && byId.TryGetValue(k, out var e))
                return new($"{(e.Type == "module" ? "Module" : CourseStructureEvidence.Label(e.Type))}: {e.Title}", e.Type == "module" ? null : moduleOf.GetValueOrDefault(k).Title,
                    e.Id, ReviewService.SafeUrl(e.SourceUrl, course.SourceOrigin));
            if (id is { } o && outcomes.TryGetValue(o, out var outcome)) return new($"Competency {outcome.Code}: {outcome.Title}", null, null, null);
            return new("Whole course", null, null, null);
        }

        var issues = new List<GuideIssue>();
        var places = new Dictionary<string, Place>();
        var limitations = new HashSet<string>();
        int pages = 0, images = 0, links = 0, headings = 0, tables = 0, media = 0;
        foreach (var e in content)
        {
            if (!snapshots.TryGetValue(e.Id, out var s)) continue;
            var evidence = ContentEvidence.Parse(s.EvidenceJson);
            if (e.Type == "page") pages++;
            images += evidence?.Images.Count ?? 0; links += evidence?.Links.Count ?? 0; headings += evidence?.Headings.Count ?? 0; tables += evidence?.Tables.Count ?? 0;
            // Only recognised players count toward media coverage: an embedded tool is not evidence about captions.
            media += evidence?.Media?.Count(m => MediaKinds.IsPlayer(m.Kind)) ?? 0;
            var report = ReviewEngine.RunElement(e.Type, s.SourceStatus, evidence, profile, s.PlainText);
            if (report.Limitations.Count > 0) limitations.Add("Some items could not be fully checked (for example, items imported before evidence capture). Re-import the course to check everything.");
            foreach (var f in report.Findings)
            {
                var key = GuideVocabulary.ElementKey(e.Id, f.Fingerprint);
                if (places.TryAdd(key, PlaceOf(e.Id))) issues.Add(new(key, f, e.Id, moduleOf.TryGetValue(e.Id, out var m) ? m.Index : null, true));
            }
        }
        var courseReport = ReviewEngine.RunCourse(structure, profile, timeZone);
        limitations.UnionWith(courseReport.Limitations);
        foreach (var f in courseReport.Findings)
        {
            var key = GuideVocabulary.CourseKey(f.Fingerprint);
            if (places.TryAdd(key, PlaceOf(f.SubjectElementId)))
                issues.Add(new(key, f, f.SubjectElementId is { } sid && byId.ContainsKey(sid) ? sid : null,
                    f.SubjectElementId is { } mid && moduleOf.TryGetValue(mid, out var m) ? m.Index : null, true));
        }

        // Done marks made before the latest import whose issue is still found are "still detected" and count as open again.
        var marks = await db.GuideProgress.AsNoTracking().Where(p => p.CourseId == course.Id).ToDictionaryAsync(p => p.Key, ct);
        var contentAsOf = new[] { course.LastSyncedAt, snapshots.Count == 0 ? null : snapshots.Values.Max(s => s.FetchedAt) }.Max();
        bool StillDetected(GuideProgress? p) => p is { Status: GuideVocabulary.Done } && contentAsOf is { } asOf && p.SetAt < asOf;
        issues = issues.Select(i => marks.TryGetValue(i.Key, out var p) && !StillDetected(p) ? i with { Open = false } : i).ToList();
        var detected = issues.Select(i => i.Key).ToHashSet();
        var setters = marks.Values.Select(p => p.SetBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => setters.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var (confirmedBy, notes) = await ReviewerFindingsAsync(course.Id, places, ct);
        var resources = await StepResourcesAsync(user, course.TenantId, issues.Select(i => i.Finding.CheckCode).Distinct().ToList(), ct);
        var steps = ImprovementGuide.Build(issues, required.Contains);
        var stepDtos = steps.Select((s, rank) =>
        {
            var check = CheckRegistry.Find(s.CheckCode)!;
            var first = s.Issues.FirstOrDefault(i => i.Open) ?? s.Issues[0];
            var locations = s.Issues.Select(i =>
            {
                var place = places[i.Key];
                var mark = marks.GetValueOrDefault(i.Key);
                var still = StillDetected(mark);
                return new GuideLocationDto(i.Key, place.ElementId, place.Where, place.Module, i.Finding.Description, i.Finding.Evidence, place.SourceUrl,
                    mark is null || still ? GuideVocabulary.Open : mark.Status, mark?.Reason is { Length: > 0 } r ? r : null,
                    mark is null ? null : names.GetValueOrDefault(mark.SetBy), mark?.SetAt, still,
                    confirmedBy.GetValueOrDefault(i.Key).Reviewer, confirmedBy.GetValueOrDefault(i.Key).TaskId);
            }).ToList();
            return new GuideStepDto(s.CheckCode, check.Name, s.Aspect, s.Severity, s.Required, s.QuickWin, rank + 1, s.MinutesEach, s.OpenMinutes, s.OpenCount, s.Issues.Count,
                s.Issues.Select(i => i.ElementId?.ToString() ?? i.Key).Distinct().Count(), first.Finding.Recommendation,
                guidance.GetValueOrDefault(s.CheckCode) ?? check.Guidance, locations, locations.Any(l => l.ConfirmedBy is not null && l.Status == GuideVocabulary.Open),
                resources.GetValueOrDefault(s.CheckCode) ?? [], check.WcagCriteria);
        }).ToList();

        var assessments = structure.AllItems().Where(i => i.IsAssessed).Select(i => i.ElementId).Distinct().Count();
        var coverage = new GuideCoverage(pages, assessments, images, links, headings, tables, structure.Modules.Count, structure.Competencies, snapshots.Count, media);
        var enabled = CheckRegistry.All.Where(c => profile.For(c).Enabled).Select(c => c.Code);
        // A course with no syllabus produces no syllabus findings, which would otherwise read as a clean syllabus.
        // This college's syllabi are in Simple Syllabus, an LTI tool outside Canvas (ADR 008), so a missing syllabus
        // is the normal case here and has to say so, with the way to fix it (ADR 019).
        if (enabled.Contains(SyllabusChecks.RequiredElementsMissing) && !content.Any(e => e.Type == ReviewVocabulary.SyllabusType))
            limitations.Add("This course has no syllabus in TeachWell, so the syllabus was not checked. If the syllabus is in Simple Syllabus or another tool outside Canvas, paste its text on the course page.");
        var strengths = ImprovementGuide.Strengths(enabled, issues.Select(i => i.Finding.CheckCode), coverage)
            .Select(x => new GuideStrengthDto(x.CheckCode, x.Aspect, x.Statement)).ToList();
        var aspects = ReviewAspects.All.Select(a =>
        {
            var open = steps.Where(s => s.Aspect == a && s.OpenCount > 0).ToList();
            var (label, why) = GuideCatalog.Aspects[a];
            return new GuideAspectDto(a, label, why, open.Count, open.Count(s => s.Required), open.Sum(s => s.OpenCount));
        }).ToList();
        var resolved = marks.Values.Count(p => p.Status == GuideVocabulary.Done && !detected.Contains(p.Key));

        var dto = new CourseGuideDto(course.Id, course.Title, StandardsResolver.Describe(standards, course.DeliveryMode, course.Department), contentAsOf, CanMark(user, course),
            snapshots.Count, aspects, stepDtos, strengths, resolved, limitations.Order().ToList(), notes);
        await RecordAsync(course, dto, ct);
        return (dto, detected);
    }

    // ADR 014: one figures-only row per course per UTC day, updated each time the guide is built.
    private async Task RecordAsync(Course course, CourseGuideDto guide, CancellationToken ct)
    {
        var day = DateTime.UtcNow.Date;
        var record = await db.CourseGuideRecords.SingleOrDefaultAsync(r => r.CourseId == course.Id && r.Day == day, ct);
        if (record is null) db.CourseGuideRecords.Add(record = new() { Id = Guid.NewGuid(), TenantId = course.TenantId, CourseId = course.Id, Day = day });
        var places = guide.Steps.SelectMany(s => s.Locations).ToList();
        var open = guide.Steps.Where(s => s.OpenPlaces > 0).ToList();
        record.RecordedAt = DateTime.UtcNow;
        record.StandardsName = guide.Standards.Name; record.StandardsVersionNumber = guide.Standards.VersionNumber; record.CheckedItems = guide.CheckedItems;
        record.OpenSteps = open.Count; record.RequiredOpenSteps = open.Count(s => s.Required); record.OpenPlaces = open.Sum(s => s.OpenPlaces);
        record.DonePlaces = places.Count(p => p.Status == GuideVocabulary.Done); record.NotApplicablePlaces = places.Count(p => p.Status == GuideVocabulary.NotApplicable);
        record.OpenByAspectJson = System.Text.Json.JsonSerializer.Serialize(open.GroupBy(s => s.Aspect).ToDictionary(g => g.Key, g => g.Sum(s => s.OpenPlaces)));
        record.OpenByCheckJson = System.Text.Json.JsonSerializer.Serialize(open.ToDictionary(s => s.CheckCode, s => s.OpenPlaces));
        try { await db.SaveChangesAsync(ct); }
        // Two first loads of the same course on the same day: the other request's row stands.
        catch (DbUpdateException) { db.Entry(record).State = EntityState.Detached; }
    }

    public async Task<int> RecordAllAsync(Guid tenantId, CancellationToken ct)
    {
        var system = new CurrentUser(Guid.Empty, tenantId, "TeachWell", "", []);
        var courses = await db.Courses.AsNoTracking().Where(c => c.TenantId == tenantId).ToListAsync(ct);
        foreach (var course in courses) await BuildAsync(system, course, ct);
        return courses.Count;
    }

    // The CTL's outstanding confirmed findings on this course (published reviews, no resolved revision task; the rule the quality report
    // uses). An automated finding the guide still detects is attached to that place; a reviewer's own (manual) finding becomes a note.
    // An automated finding the guide no longer detects is left out: the content has changed since the review.
    private async Task<(Dictionary<string, (string? Reviewer, Guid? TaskId)> ConfirmedBy, List<GuideReviewerNoteDto> Notes)> ReviewerFindingsAsync(
        Guid courseId, IReadOnlyDictionary<string, Place> places, CancellationToken ct)
    {
        var rows = await (from f in db.ReviewFindings.AsNoTracking()
                          join r in db.Reviews.AsNoTracking() on f.ReviewId equals r.Id
                          join e in db.Elements.AsNoTracking() on r.ElementId equals e.Id
                          join u in db.Users.AsNoTracking() on r.ReviewerId equals u.Id
                          where r.CourseId == courseId && r.Status == ReviewVocabulary.Published && f.Status == ReviewVocabulary.Confirmed
                          select new { f.Id, f.ReviewId, r.ElementId, ElementType = e.Type, ElementTitle = e.Title, f.Source, f.Fingerprint, f.Title, f.Description,
                              f.Recommendation, f.Severity, Reviewer = u.DisplayName, r.PublishedAt }).ToListAsync(ct);
        var confirmedBy = new Dictionary<string, (string? Reviewer, Guid? TaskId)>();
        var notes = new List<GuideReviewerNoteDto>();
        if (rows.Count == 0) return (confirmedBy, notes);
        var ids = rows.Select(r => r.Id).ToList();
        var tasks = await (from tf in db.RevisionTaskFindings.AsNoTracking()
                           join t in db.RevisionTasks.AsNoTracking() on tf.TaskId equals t.Id
                           where ids.Contains(tf.FindingId)
                           select new { tf.FindingId, t.Id, t.Status, t.CreatedAt }).ToListAsync(ct);
        foreach (var row in rows.OrderByDescending(r => r.PublishedAt))
        {
            var mine = tasks.Where(t => t.FindingId == row.Id).ToList();
            if (mine.Any(t => t.Status == TaskVocabulary.Resolved)) continue;
            var open = mine.Where(t => !TaskVocabulary.Terminal.Contains(t.Status)).MaxBy(t => t.CreatedAt);
            if (row.Source != "Manual")
            {
                if (row.Fingerprint is null) continue;
                var key = row.ElementType == ReviewVocabulary.CourseType ? GuideVocabulary.CourseKey(row.Fingerprint) : GuideVocabulary.ElementKey(row.ElementId, row.Fingerprint);
                if (places.ContainsKey(key)) confirmedBy.TryAdd(key, (row.Reviewer, open?.Id));
                continue;
            }
            var where = row.ElementType == ReviewVocabulary.CourseType ? "Whole course" : $"{CourseStructureEvidence.Label(row.ElementType)}: {row.ElementTitle}";
            notes.Add(new(row.Id, row.ReviewId, where, row.Title, row.Description, row.Recommendation, row.Severity, row.Reviewer, row.PublishedAt, open?.Id, open?.Status));
        }
        return (confirmedBy, notes);
    }

    // Instructors see only published articles and workshops that have not started yet.
    private async Task<Dictionary<string, List<GuideResourceDto>>> StepResourcesAsync(CurrentUser user, Guid tenantId, List<string> codes, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var links = await db.CheckResourceLinks.AsNoTracking().Where(l => l.TenantId == tenantId && codes.Contains(l.CheckCode)).OrderBy(l => l.CreatedAt).ToListAsync(ct);
        if (links.Count == 0) return [];
        var articleIds = links.Where(l => l.ArticleId is not null).Select(l => l.ArticleId!.Value).ToList();
        var workshopIds = links.Where(l => l.WorkshopId is not null).Select(l => l.WorkshopId!.Value).ToList();
        var articles = await db.KnowledgeArticles.AsNoTracking().Where(a => articleIds.Contains(a.Id) && a.TenantId == tenantId && a.Status == ReviewVocabulary.Published)
            .ToDictionaryAsync(a => a.Id, ct);
        var workshops = await db.Workshops.AsNoTracking().Include(w => w.Registrations).Where(w => workshopIds.Contains(w.Id) && w.TenantId == tenantId && w.StartsAt > now)
            .ToDictionaryAsync(w => w.Id, ct);
        var result = new Dictionary<string, List<GuideResourceDto>>();
        foreach (var l in links)
        {
            GuideResourceDto? dto = l.ArticleId is { } a && articles.TryGetValue(a, out var article)
                ? new("Article", article.Id, article.Title, article.Slug, null, null, null, null)
                : l.WorkshopId is { } w && workshops.TryGetValue(w, out var workshop)
                    ? new("Workshop", workshop.Id, workshop.Title, null, workshop.StartsAt, workshop.EndsAt, workshop.Modality,
                        workshop.Registrations.FirstOrDefault(r => r.UserId == user.Id && r.Status != WorkshopVocabulary.Cancelled)?.Status)
                    : null;
            if (dto is not null) (result.TryGetValue(l.CheckCode, out var list) ? list : result[l.CheckCode] = []).Add(dto);
        }
        return result;
    }

    public async Task<IReadOnlyList<CheckResourceLinkDto>> ListResourcesAsync(CurrentUser user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var links = await db.CheckResourceLinks.AsNoTracking().Where(l => l.TenantId == user.TenantId).OrderBy(l => l.CheckCode).ThenBy(l => l.CreatedAt).ToListAsync(ct);
        var articleIds = links.Where(l => l.ArticleId is not null).Select(l => l.ArticleId!.Value).ToList();
        var workshopIds = links.Where(l => l.WorkshopId is not null).Select(l => l.WorkshopId!.Value).ToList();
        var articles = await db.KnowledgeArticles.AsNoTracking().Where(a => articleIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => (a.Title, a.Status), ct);
        var workshops = await db.Workshops.AsNoTracking().Where(w => workshopIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => (w.Title, w.StartsAt), ct);
        return links.Select(l =>
        {
            var name = CheckRegistry.Find(l.CheckCode)?.Name ?? l.CheckCode;
            if (l.ArticleId is { } a)
            {
                var (title, status) = articles[a];
                return new CheckResourceLinkDto(l.Id, l.CheckCode, name, "Article", a, title, status == ReviewVocabulary.Published ? "Shown to instructors" : "Hidden until the article is published");
            }
            var (wTitle, starts) = workshops[l.WorkshopId!.Value];
            return new CheckResourceLinkDto(l.Id, l.CheckCode, name, "Workshop", l.WorkshopId!.Value, wTitle, starts > now ? "Shown to instructors until it starts" : "Hidden: the workshop has started");
        }).ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<CheckResourceLinkDto>>> LinkResourceAsync(CurrentUser user, CheckResourceInput input, CancellationToken ct)
    {
        if (input.CheckCode is null || CheckRegistry.Find(input.CheckCode) is null)
            return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.Invalid("checkCode", "Choose a check.");
        if ((input.ArticleId is null) == (input.WorkshopId is null))
            return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.Invalid("resource", "Choose one article or one workshop.");
        var exists = input.ArticleId is { } a
            ? await db.KnowledgeArticles.AnyAsync(x => x.Id == a && x.TenantId == user.TenantId, ct)
            : await db.Workshops.AnyAsync(x => x.Id == input.WorkshopId && x.TenantId == user.TenantId, ct);
        if (!exists) return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.Invalid("resource", "That article or workshop does not exist.");
        var current = await db.CheckResourceLinks.Where(l => l.TenantId == user.TenantId && l.CheckCode == input.CheckCode).ToListAsync(ct);
        if (current.Any(l => l.ArticleId == input.ArticleId && l.WorkshopId == input.WorkshopId))
            return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.Invalid("resource", "That resource is already linked to this check.");
        if (current.Count >= CheckResourceLink.MaxPerCheck)
            return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.Invalid("resource", $"A check can have at most {CheckResourceLink.MaxPerCheck} resources.");
        var link = new CheckResourceLink { Id = Guid.NewGuid(), TenantId = user.TenantId, CheckCode = input.CheckCode, ArticleId = input.ArticleId, WorkshopId = input.WorkshopId, CreatedBy = user.Id };
        db.CheckResourceLinks.Add(link);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = link.Id, Action = "CheckResourceLinked" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.Ok(await ListResourcesAsync(user, ct));
    }

    public async Task<ServiceResult<IReadOnlyList<CheckResourceLinkDto>>> UnlinkResourceAsync(CurrentUser user, Guid linkId, CancellationToken ct)
    {
        var link = await db.CheckResourceLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.TenantId == user.TenantId, ct);
        if (link is null) return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.NotFound();
        db.CheckResourceLinks.Remove(link);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = link.Id, Action = "CheckResourceUnlinked" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<IReadOnlyList<CheckResourceLinkDto>>.Ok(await ListResourcesAsync(user, ct));
    }

    public const int TuningMinimum = 3;
    public const decimal TuningPercent = 50;

    public async Task<GettingStartedDto> GettingStartedAsync(CurrentUser user, CancellationToken ct)
    {
        var owned = await db.Courses.AsNoTracking().Where(c => c.TenantId == user.TenantId && c.OwnerId == user.Id)
            .Select(c => new { c.Id, c.Title, c.SourceType, c.LastSyncedAt, c.CreatedAt }).ToListAsync(ct);
        var ids = owned.Select(c => c.Id).ToList();
        // A fictional demo course does not count as bringing in your own; it is still offered when there is nothing else.
        var imported = owned.Where(c => c.SourceType != "Demo").ToList();
        var next = (imported.Count > 0 ? imported : owned).OrderByDescending(c => c.LastSyncedAt ?? c.CreatedAt).FirstOrDefault();
        var marked = await db.GuideProgress.AsNoTracking().AnyAsync(p => p.SetBy == user.Id && ids.Contains(p.CourseId), ct);
        var asked = await db.ReadinessRequests.AsNoTracking().AnyAsync(r => r.RequestedBy == user.Id && ids.Contains(r.CourseId), ct);
        var endorsed = await db.Endorsements.AsNoTracking().AnyAsync(e => ids.Contains(e.CourseId), ct);
        return new(imported.Count > 0, marked, asked, endorsed, next?.Id, next?.Title);
    }

    public async Task<GuideFeedbackDto> FeedbackAsync(CurrentUser user, CancellationToken ct)
    {
        var rows = await db.GuideProgress.AsNoTracking().Where(p => p.TenantId == user.TenantId)
            .Select(p => new { p.CourseId, p.Key, p.Status, p.Reason, p.SetAt }).ToListAsync(ct);
        // Reasons are instructors' own words about a course, so they follow the course ACL; counts are tenant-wide and name no one.
        var visible = await Accessible(user).Select(c => new { c.Id, c.Title }).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var checks = rows.GroupBy(r => GuideVocabulary.CheckCodeOf(r.Key)).Select(g => (Check: CheckRegistry.Find(g.Key), Rows: g.ToList())).Where(x => x.Check is not null)
            .Select(x =>
            {
                var na = x.Rows.Where(r => r.Status == GuideVocabulary.NotApplicable).ToList();
                var done = x.Rows.Count(r => r.Status == GuideVocabulary.Done);
                decimal? percent = na.Count + done == 0 ? null : Math.Round(na.Count * 100m / (na.Count + done), 1);
                var reasons = na.Where(r => visible.ContainsKey(r.CourseId)).OrderByDescending(r => r.SetAt).ToList();
                return new CheckFeedbackDto(x.Check!.Code, x.Check.Name, x.Check.Aspect, na.Count, done, percent, na.Select(r => r.CourseId).Distinct().Count(),
                    na.Count >= TuningMinimum && percent >= TuningPercent,
                    reasons.Take(10).Select(r => new FeedbackReasonDto(visible[r.CourseId], r.Reason, r.SetAt)).ToList(), na.Count - Math.Min(10, reasons.Count));
            })
            .OrderByDescending(c => c.TuningCandidate).ThenByDescending(c => c.NotApplicable).ThenByDescending(c => c.NotApplicablePercent).ThenBy(c => c.CheckCode, StringComparer.Ordinal).ToList();
        var scope = new ReportScope("Instructor responses to guide steps · whole college", DateTime.UtcNow,
        [
            "Counts are marks instructors made in the improvement guide (Done or Not applicable), not everything the checks found. Reopened marks are not counted.",
            $"A check is flagged for review when at least {TuningMinimum} places were marked not applicable and they are at least {TuningPercent}% of its marks. That suggests tuning a threshold or word list, not that the check is wrong.",
            "Counts cover every course in the college; reasons are shown only from courses you can access, without naming who wrote them.",
        ]);
        return new(scope, rows.Select(r => r.CourseId).Distinct().Count(), checks);
    }
}
