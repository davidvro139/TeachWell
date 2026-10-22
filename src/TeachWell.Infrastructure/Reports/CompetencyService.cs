using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Reports;

// Competencies are course-design metadata the owning instructor declares; CTL with course access can read them
// for review/reporting purposes but cannot author them, matching how sharing and Canvas connections are owner-managed.
public sealed class CompetencyService(AppDbContext db) : ICompetencyService
{
    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));
    private Task<bool> IsOwner(CurrentUser user, Guid courseId, CancellationToken ct) =>
        db.Courses.AnyAsync(c => c.Id == courseId && c.TenantId == user.TenantId && c.OwnerId == user.Id, ct);

    public async Task<ServiceResult<IReadOnlyList<CompetencyDto>>> ListAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        if (!await Accessible(user).AnyAsync(c => c.Id == courseId, ct)) return ServiceResult<IReadOnlyList<CompetencyDto>>.NotFound();
        var competencies = await db.CourseCompetencies.AsNoTracking().AsSplitQuery().Include(c => c.Mappings)
            .Where(c => c.CourseId == courseId).OrderBy(c => c.Code).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<CompetencyDto>>.Ok(await ToDtos(competencies, ct));
    }

    private async Task<List<CompetencyDto>> ToDtos(List<CourseCompetency> competencies, CancellationToken ct)
    {
        var elementIds = competencies.SelectMany(c => c.Mappings.Select(m => m.ElementId)).Distinct().ToList();
        var elements = await db.Elements.AsNoTracking().Where(e => elementIds.Contains(e.Id)).Select(e => new { e.Id, e.Type, e.Title }).ToDictionaryAsync(e => e.Id, ct);
        return competencies.Select(c => new CompetencyDto(c.Id, c.Code, c.Title, c.Description, c.Revision,
            c.Mappings.Where(m => elements.ContainsKey(m.ElementId)).Select(m => new CompetencyMappingDto(m.ElementId, elements[m.ElementId].Type, elements[m.ElementId].Title)).ToList())).ToList();
    }

    public async Task<ServiceResult<CompetencyDto>> CreateAsync(CurrentUser user, Guid courseId, CreateCompetencyInput input, CancellationToken ct)
    {
        if (!await IsOwner(user, courseId, ct)) return ServiceResult<CompetencyDto>.NotFound();
        var errors = AlignmentValidation.Create(input.Code, input.Title, input.Description);
        if (errors.Count == 0 && await db.CourseCompetencies.AnyAsync(c => c.CourseId == courseId && c.Code == input.Code, ct))
            errors["code"] = ["A competency with this code already exists in this course."];
        if (errors.Count > 0) return ServiceResult<CompetencyDto>.Invalid(errors);
        var competency = new CourseCompetency { Id = Guid.NewGuid(), TenantId = user.TenantId, CourseId = courseId, Code = input.Code!, Title = input.Title!.Trim(), Description = input.Description?.Trim() ?? "", CreatedBy = user.Id };
        db.CourseCompetencies.Add(competency);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = competency.Id, Action = "CompetencyCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<CompetencyDto>.Ok((await ToDtos([competency], ct))[0]);
    }

    private Task<CourseCompetency?> Owned(CurrentUser user, Guid courseId, Guid competencyId, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.CourseCompetencies : db.CourseCompetencies.AsNoTracking();
        return query.Include(c => c.Mappings).SingleOrDefaultAsync(c => c.Id == competencyId && c.CourseId == courseId &&
            db.Courses.Any(course => course.Id == courseId && course.TenantId == user.TenantId && course.OwnerId == user.Id), ct);
    }

    public async Task<ServiceResult<CompetencyDto>> UpdateAsync(CurrentUser user, Guid courseId, Guid competencyId, UpdateCompetencyInput input, CancellationToken ct)
    {
        var competency = await Owned(user, courseId, competencyId, true, ct);
        if (competency is null) return ServiceResult<CompetencyDto>.NotFound();
        var errors = AlignmentValidation.Update(input.Title, input.Description);
        if (errors.Count > 0) return ServiceResult<CompetencyDto>.Invalid(errors);
        if (competency.Revision != input.Revision) return ServiceResult<CompetencyDto>.Conflict("This competency changed since you opened it. Reload it and try again.");
        db.Entry(competency).Property(c => c.Revision).OriginalValue = input.Revision;
        competency.Revision = Guid.NewGuid(); competency.Title = input.Title!.Trim(); competency.Description = input.Description?.Trim() ?? "";
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = competency.Id, Action = "CompetencyUpdated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<CompetencyDto>.Ok((await ToDtos([competency], ct))[0]);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(CurrentUser user, Guid courseId, Guid competencyId, CancellationToken ct)
    {
        var competency = await Owned(user, courseId, competencyId, true, ct);
        if (competency is null) return ServiceResult<bool>.NotFound();
        db.CourseCompetencies.Remove(competency);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = competencyId, Action = "CompetencyDeleted" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<CompetencyDto>> MapAsync(CurrentUser user, Guid courseId, Guid competencyId, Guid elementId, CancellationToken ct)
    {
        var competency = await Owned(user, courseId, competencyId, true, ct);
        if (competency is null) return ServiceResult<CompetencyDto>.NotFound();
        // Mapping evidence to a competency is restricted to assessment activities (assignments, quizzes and discussions): REVIEW_FRAMEWORK.md's alignment criterion is about evidence of learning, not general course content.
        if (!await db.Elements.AnyAsync(e => e.Id == elementId && e.CourseId == courseId && (e.Type == "assignment" || e.Type == ReviewVocabulary.QuizType || e.Type == ReviewVocabulary.DiscussionType), ct))
            return ServiceResult<CompetencyDto>.Invalid("elementId", "Choose an assignment, quiz or discussion in this course.");
        if (!competency.Mappings.Any(m => m.ElementId == elementId))
        {
            competency.Mappings.Add(new() { CompetencyId = competencyId, ElementId = elementId });
            db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = competency.Id, Action = "CompetencyMapped" });
            await db.SaveChangesAsync(ct);
        }
        return ServiceResult<CompetencyDto>.Ok((await ToDtos([competency], ct))[0]);
    }

    public async Task<ServiceResult<OutcomeProposalDto>> ProposeAsync(CurrentUser user, Guid courseId, OutcomePasteInput input, CancellationToken ct)
    {
        if (!await IsOwner(user, courseId, ct)) return ServiceResult<OutcomeProposalDto>.NotFound();
        if (string.IsNullOrWhiteSpace(input.Text)) return ServiceResult<OutcomeProposalDto>.Invalid("text", "Paste the outcomes, one per line.");
        if (input.Text.Length > OutcomeListParser.MaxChars) return ServiceResult<OutcomeProposalDto>.Invalid("text", $"Paste at most {OutcomeListParser.MaxChars:N0} characters at a time.");
        var parsed = OutcomeListParser.Parse(input.Text);
        if (parsed.Outcomes.Count == 0) return ServiceResult<OutcomeProposalDto>.Invalid("text", "No outcomes were found. Put each outcome on its own line.");
        var existing = await db.CourseCompetencies.AsNoTracking().Where(c => c.CourseId == courseId).Select(c => new { c.Code, c.Title }).ToListAsync(ct);
        var taken = existing.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
        // Codes the paste did not provide continue the course's SLO numbering.
        var next = existing.Select(e => System.Text.RegularExpressions.Regex.Match(e.Code, @"^SLO-(\d+)$")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max() + 1;
        var vague = CheckRegistry.Find(CourseDesignChecks.OutcomeVagueVerb)!.Defaults().Words(CourseDesignChecks.VagueVerbs);
        var rows = new List<ProposedCompetencyDto>();
        foreach (var o in parsed.Outcomes)
        {
            var notes = new List<string>();
            var duplicate = existing.FirstOrDefault(e => string.Equals(e.Title.Trim(), o.Title, StringComparison.OrdinalIgnoreCase));
            string code;
            if (o.Code is { Length: > 0 } given && !taken.Contains(given)) code = given;
            else
            {
                if (o.Code is { Length: > 0 }) notes.Add($"The code {o.Code} is already used in this course, so a new one was suggested.");
                while (taken.Contains($"SLO-{next}")) next++;
                code = $"SLO-{next++}";
            }
            taken.Add(code);
            if (duplicate is not null) notes.Add($"Already in this course as {duplicate.Code}; left unselected.");
            if (o.Title.Length > 200) notes.Add($"Longer than 200 characters ({o.Title.Length}); shorten it, or move detail to the description.");
            var phrase = Bloom.VaguePhrase(o.Title, vague);
            if (phrase is not null) notes.Add($"Starts with \"{phrase}\", which is hard to observe; consider a measurable verb.");
            rows.Add(new(code, o.Title, duplicate is null, Bloom.LevelOf(o.Title), phrase, duplicate?.Code, notes));
        }
        return ServiceResult<OutcomeProposalDto>.Ok(new(rows, parsed.Ignored));
    }

    public async Task<ServiceResult<IReadOnlyList<CompetencyDto>>> CreateManyAsync(CurrentUser user, Guid courseId, BulkCompetencyInput input, CancellationToken ct)
    {
        if (!await IsOwner(user, courseId, ct)) return ServiceResult<IReadOnlyList<CompetencyDto>>.NotFound();
        var items = input.Items ?? [];
        if (items.Length is 0 or > OutcomeListParser.MaxOutcomes)
            return ServiceResult<IReadOnlyList<CompetencyDto>>.Invalid("items", $"Choose 1–{OutcomeListParser.MaxOutcomes} competencies to add.");
        var existing = (await db.CourseCompetencies.AsNoTracking().Where(c => c.CourseId == courseId).Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Length; i++)
        {
            foreach (var (field, messages) in AlignmentValidation.Create(items[i]?.Code, items[i]?.Title, items[i]?.Description)) errors[$"items[{i}].{field}"] = messages;
            if (items[i]?.Code is { } code && (existing.Contains(code) || !seen.Add(code)))
                errors[$"items[{i}].code"] = [$"The code {code} is already used{(existing.Contains(code) ? " in this course" : " by another row")}."];
        }
        if (errors.Count > 0) return ServiceResult<IReadOnlyList<CompetencyDto>>.Invalid(errors);
        var created = items.Select(i => new CourseCompetency
        {
            Id = Guid.NewGuid(), TenantId = user.TenantId, CourseId = courseId, Code = i.Code!, Title = i.Title!.Trim(), Description = i.Description?.Trim() ?? "", CreatedBy = user.Id
        }).ToList();
        db.CourseCompetencies.AddRange(created);
        foreach (var c in created) db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = c.Id, Action = "CompetencyCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<IReadOnlyList<CompetencyDto>>.Ok(await ToDtos(created, ct));
    }

    public const int MaxMappingChanges = 5000;

    public async Task<ServiceResult<IReadOnlyList<CompetencyDto>>> ApplyMappingsAsync(CurrentUser user, Guid courseId, MappingChangesInput input, CancellationToken ct)
    {
        if (!await IsOwner(user, courseId, ct)) return ServiceResult<IReadOnlyList<CompetencyDto>>.NotFound();
        var changes = input.Changes ?? [];
        if (changes.Length is 0 or > MaxMappingChanges)
            return ServiceResult<IReadOnlyList<CompetencyDto>>.Invalid("changes", $"Send 1–{MaxMappingChanges:N0} mapping changes.");
        var competencies = await db.CourseCompetencies.Include(c => c.Mappings).Where(c => c.CourseId == courseId).ToListAsync(ct);
        var byId = competencies.ToDictionary(c => c.Id);
        var elementIds = changes.Select(c => c.ElementId).Distinct().ToList();
        // Same rule as single mappings: only assessment activities in this course are evidence for a competency.
        var mappable = (await db.Elements.AsNoTracking().Where(e => e.CourseId == courseId && elementIds.Contains(e.Id)
            && (e.Type == "assignment" || e.Type == ReviewVocabulary.QuizType || e.Type == ReviewVocabulary.DiscussionType)).Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        if (changes.Any(c => !byId.ContainsKey(c.CompetencyId)))
            return ServiceResult<IReadOnlyList<CompetencyDto>>.Invalid("changes", "Every change must name a competency in this course.");
        if (changes.Any(c => !mappable.Contains(c.ElementId)))
            return ServiceResult<IReadOnlyList<CompetencyDto>>.Invalid("changes", "Every change must name an assignment, quiz or discussion in this course.");
        // The last change for a cell wins; changes that match the current state do nothing and are not audited.
        foreach (var change in changes.GroupBy(c => (c.CompetencyId, c.ElementId)).Select(g => g.Last()))
        {
            var competency = byId[change.CompetencyId];
            var existing = competency.Mappings.SingleOrDefault(m => m.ElementId == change.ElementId);
            if (change.Mapped && existing is null)
            {
                competency.Mappings.Add(new() { CompetencyId = competency.Id, ElementId = change.ElementId });
                db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = competency.Id, Action = "CompetencyMapped" });
            }
            else if (!change.Mapped && existing is not null)
            {
                competency.Mappings.Remove(existing);
                db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = competency.Id, Action = "CompetencyUnmapped" });
            }
        }
        await db.SaveChangesAsync(ct);
        return ServiceResult<IReadOnlyList<CompetencyDto>>.Ok(await ToDtos(competencies.OrderBy(c => c.Code).ToList(), ct));
    }

    public async Task<ServiceResult<CompetencyDto>> UnmapAsync(CurrentUser user, Guid courseId, Guid competencyId, Guid elementId, CancellationToken ct)
    {
        var competency = await Owned(user, courseId, competencyId, true, ct);
        if (competency is null) return ServiceResult<CompetencyDto>.NotFound();
        var mapping = competency.Mappings.SingleOrDefault(m => m.ElementId == elementId);
        if (mapping is not null)
        {
            competency.Mappings.Remove(mapping);
            db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = competency.Id, Action = "CompetencyUnmapped" });
            await db.SaveChangesAsync(ct);
        }
        return ServiceResult<CompetencyDto>.Ok((await ToDtos([competency], ct))[0]);
    }
}
