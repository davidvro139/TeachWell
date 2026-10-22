using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TeachWell.Application;

namespace TeachWell.Infrastructure.Reviews;

// Oversight:AutoRecheckHours (0–168, default 24; 0 turns it off) and Oversight:AutoRecheckStartMinutes (0–1440, default 5): how often every
// course's improvement guide is re-run so oversight figures stay current without anyone pressing "Re-check all courses". Validated at startup.
public sealed class OversightOptions
{
    public const string AutoRecheckAction = "OversightAutoRechecked";
    public int AutoRecheckHours { get; } = 24;
    public int AutoRecheckStartMinutes { get; } = 5;

    public OversightOptions() { }
    public OversightOptions(IConfiguration config)
    {
        AutoRecheckHours = Read(config, "Oversight:AutoRecheckHours", 24, 0, 168);
        AutoRecheckStartMinutes = Read(config, "Oversight:AutoRecheckStartMinutes", 5, 0, 1440);
    }

    private static int Read(IConfiguration config, string key, int fallback, int min, int max)
    {
        var configured = config[key];
        if (string.IsNullOrWhiteSpace(configured)) return fallback;
        if (!int.TryParse(configured, out var value) || value < min || value > max)
            throw new InvalidOperationException($"{key} must be a whole number from {min} to {max}.");
        return value;
    }
}

// Runs inside the API process. Each run re-checks every tenant's courses in its own scope and is audited with the system actor
// (Guid.Empty) against the tenant. A failed run is logged and retried at the next interval; it never stops the app.
// With several API instances each would run it; the work is idempotent (one record per course per day), only repeated.
public sealed class OversightRecheckService(IServiceScopeFactory scopes, OversightOptions options, ILogger<OversightRecheckService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.AutoRecheckHours == 0) { log.LogInformation("Automatic oversight re-checks are turned off."); return; }
        try { await Task.Delay(TimeSpan.FromMinutes(options.AutoRecheckStartMinutes), stoppingToken); }
        catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.AutoRecheckHours));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Automatic oversight re-check failed; it will run again at the next interval."); }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guide = scope.ServiceProvider.GetRequiredService<IGuideService>();
        var tenants = await db.Courses.AsNoTracking().Select(c => c.TenantId).Distinct().ToListAsync(ct);
        var total = 0;
        foreach (var tenant in tenants)
        {
            var count = await guide.RecordAllAsync(tenant, ct);
            db.AuditLogs.Add(new() { ActorId = Guid.Empty, EntityId = tenant, Action = OversightOptions.AutoRecheckAction });
            await db.SaveChangesAsync(ct);
            total += count;
        }
        log.LogInformation("Automatic oversight re-check covered {Courses} courses in {Tenants} tenants.", total, tenants.Count);
        return total;
    }
}
