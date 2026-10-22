using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Support;

public sealed class WorkshopService(AppDbContext db) : IWorkshopService
{
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public async Task<IReadOnlyList<WorkshopDto>> ListAsync(CurrentUser user, CancellationToken ct)
    {
        var workshops = await db.Workshops.AsNoTracking().Include(w => w.Registrations).Where(w => w.TenantId == user.TenantId).OrderBy(w => w.StartsAt).ToListAsync(ct);
        var names = await Names(workshops.SelectMany(w => w.Registrations.Select(r => r.UserId)), ct);
        return workshops.Select(w => ToDto(w, user, names)).ToList();
    }

    public async Task<ServiceResult<WorkshopDto>> CreateAsync(CurrentUser user, CreateWorkshopInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<WorkshopDto>.NotFound();
        var errors = SupportValidation.Workshop(input.Title, input.Description, input.StartsAt, input.EndsAt, input.Modality, input.Capacity);
        if (errors.Count > 0) return ServiceResult<WorkshopDto>.Invalid(errors);
        var workshop = new Workshop
        {
            Id = Guid.NewGuid(), TenantId = user.TenantId, Title = input.Title!.Trim(), Description = input.Description?.Trim() ?? "",
            // PostgreSQL's timestamptz columns require a UTC-kind DateTime; a client that sent an unspecified-kind value is treated as already UTC rather than rejected.
            StartsAt = AsUtc(input.StartsAt!.Value), EndsAt = AsUtc(input.EndsAt!.Value), Modality = input.Modality!, Capacity = input.Capacity, CreatedBy = user.Id
        };
        db.Workshops.Add(workshop);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = workshop.Id, Action = "WorkshopCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<WorkshopDto>.Ok(ToDto(workshop, user, []));
    }

    public async Task<ServiceResult<WorkshopDto>> RegisterAsync(CurrentUser user, Guid workshopId, CancellationToken ct)
    {
        var workshop = await db.Workshops.Include(w => w.Registrations).SingleOrDefaultAsync(w => w.Id == workshopId && w.TenantId == user.TenantId, ct);
        if (workshop is null) return ServiceResult<WorkshopDto>.NotFound();
        var existing = workshop.Registrations.SingleOrDefault(r => r.UserId == user.Id);
        if (existing is not { Status: WorkshopVocabulary.Cancelled } and not null) return ServiceResult<WorkshopDto>.Ok(ToDto(workshop, user, await Names(workshop, ct)));
        var registeredCount = workshop.Registrations.Count(r => r.Status == WorkshopVocabulary.Registered);
        var status = workshop.Capacity is { } capacity && registeredCount >= capacity ? WorkshopVocabulary.Waitlisted : WorkshopVocabulary.Registered;
        if (existing is not null) { existing.Status = status; existing.RegisteredAt = DateTime.UtcNow; }
        else workshop.Registrations.Add(new() { WorkshopId = workshop.Id, UserId = user.Id, Status = status });
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = workshop.Id, Action = "Workshop" + status });
        await db.SaveChangesAsync(ct);
        return ServiceResult<WorkshopDto>.Ok(ToDto(workshop, user, await Names(workshop, ct)));
    }

    public async Task<ServiceResult<WorkshopDto>> CancelRegistrationAsync(CurrentUser user, Guid workshopId, CancellationToken ct)
    {
        var workshop = await db.Workshops.Include(w => w.Registrations).SingleOrDefaultAsync(w => w.Id == workshopId && w.TenantId == user.TenantId, ct);
        if (workshop is null) return ServiceResult<WorkshopDto>.NotFound();
        var mine = workshop.Registrations.SingleOrDefault(r => r.UserId == user.Id);
        if (mine is null or { Status: WorkshopVocabulary.Cancelled }) return ServiceResult<WorkshopDto>.Ok(ToDto(workshop, user, await Names(workshop, ct)));
        var wasRegistered = mine.Status == WorkshopVocabulary.Registered;
        mine.Status = WorkshopVocabulary.Cancelled;
        // Promote the longest-waiting registrant into the seat that just opened up.
        if (wasRegistered && workshop.Registrations.Where(r => r.Status == WorkshopVocabulary.Waitlisted).OrderBy(r => r.RegisteredAt).FirstOrDefault() is { } next)
            next.Status = WorkshopVocabulary.Registered;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = workshop.Id, Action = "WorkshopRegistrationCancelled" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<WorkshopDto>.Ok(ToDto(workshop, user, await Names(workshop, ct)));
    }

    private Task<Dictionary<Guid, string>> Names(Workshop w, CancellationToken ct) => Names(w.Registrations.Select(r => r.UserId), ct);
    private Task<Dictionary<Guid, string>> Names(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return db.Users.AsNoTracking().Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    // The full registrant list (names) is shown only to CTL reviewers managing workshops; other viewers see counts and their own status only.
    private static WorkshopDto ToDto(Workshop w, CurrentUser user, Dictionary<Guid, string> names)
    {
        var mine = w.Registrations.SingleOrDefault(r => r.UserId == user.Id && r.Status != WorkshopVocabulary.Cancelled);
        var reviewer = IsReviewer(user);
        return new(w.Id, w.Title, w.Description, w.StartsAt, w.EndsAt, w.Modality, w.Capacity,
            w.Registrations.Count(r => r.Status == WorkshopVocabulary.Registered), w.Registrations.Count(r => r.Status == WorkshopVocabulary.Waitlisted),
            mine?.Status, reviewer,
            reviewer ? w.Registrations.Where(r => r.Status != WorkshopVocabulary.Cancelled).OrderBy(r => r.RegisteredAt)
                .Select(r => new WorkshopRegistrationDto(r.UserId, names.GetValueOrDefault(r.UserId, "Unknown"), r.Status, r.RegisteredAt)).ToList() : []);
    }
}
