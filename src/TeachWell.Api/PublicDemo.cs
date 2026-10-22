using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TeachWell.Infrastructure;

namespace TeachWell.Api;

// A hosted public demo (for example on Render): one-click sign-in to the fictional personas from any address, no password
// sign-in, no uploads or role changes, and the sample data rebuilt from the demo seed every PublicDemo:ResetHours.
// Off unless Features:PublicDemo is true. Never enable it against a database holding real accounts or institutional data:
// each refresh replaces everything in the database's public schema.
public sealed class PublicDemoOptions
{
    public const int DefaultResetHours = 24, MaxResetHours = 168;
    public bool Enabled { get; }
    public int ResetHours { get; }

    public PublicDemoOptions(IConfiguration config)
    {
        Enabled = config.GetValue<bool>("Features:PublicDemo");
        if (Enabled && config.GetValue<bool>("Features:CanvasPAT"))
            throw new InvalidOperationException("Features:PublicDemo cannot be combined with Features:CanvasPAT: visitors' Canvas tokens must never reach a shared demo database.");
        var configured = config["PublicDemo:ResetHours"];
        if (string.IsNullOrWhiteSpace(configured)) { ResetHours = DefaultResetHours; return; }
        if (!int.TryParse(configured, out var hours) || hours is < 0 or > MaxResetHours)
            throw new InvalidOperationException($"PublicDemo:ResetHours must be a whole number from 0 (never reset) to {MaxResetHours}.");
        ResetHours = hours;
    }

    public bool Resets => Enabled && ResetHours > 0;

    public static IResult Disabled(string what) =>
        Results.Problem(statusCode: 403, title: $"{what} turned off in the public demo, so every visitor finds the same sample data. Run TeachWell locally to try it.");
}

// The API answers 503 only while there is no demo data at all (the very first start against an empty database). It starts true
// so nothing is served before the service has looked at the database.
public sealed class PublicDemoState(PublicDemoOptions options)
{
    private volatile bool resetting = options.Resets;
    public bool Resetting { get => resetting; set => resetting = value; }
}

// The schema that database contexts created in this async flow use. Set only while a fresh copy is built beside the live one.
public static class DemoSchema
{
    private static readonly AsyncLocal<string?> current = new();
    public static string? Current { get => current.Value; set => current.Value = value; }
}

// Seeding takes about a minute on a desktop and much longer on a small hosted instance, and a free instance sleeps when idle,
// so visitors must never wait for it. The live data keeps serving while a fresh copy is migrated and seeded in a separate
// schema; one short transaction then swaps it in and drops the old one. When the copy was made is recorded as a comment on
// the schema, so a restart (or a wake from sleep) only rebuilds once the live data is ResetHours old.
public sealed class PublicDemoResetService(IServiceScopeFactory scopes, PublicDemoOptions options, PublicDemoState state, IConfiguration config,
    IHostApplicationLifetime lifetime, ILogger<PublicDemoResetService> log) : BackgroundService
{
    public const string BuildSchema = "teachwell_demo_next", RetiredSchema = "teachwell_demo_old", Marker = "teachwell-demo-seeded:";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1), RetryAfterFailure = TimeSpan.FromMinutes(1);
    // Everyone signs in with one click, so unless a password is configured the seeded personas get a random one nobody needs.
    private readonly IConfiguration seedConfig = string.IsNullOrWhiteSpace(config["Demo:Password"])
        ? new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string, string?> {
            ["Demo:Password"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)) + "Aa1!" }).Build()
        : config;

    // Hosted services start before the web server binds its port, and everything up to a service's first real await runs
    // inline. On a small instance the first database work (model building, connecting, migrating) can take minutes, and the
    // host's health check would time out. So the work runs on the thread pool and begins only once the server is listening.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Resets ? Task.Run(() => RunAsync(stoppingToken), stoppingToken) : Task.CompletedTask;

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled()))
        {
            try { await started.Task; }
            catch (OperationCanceledException) { return; }
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = CheckEvery;
            try { await RefreshIfDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Public demo refresh failed; retrying shortly."); wait = RetryAfterFailure; }
            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task RefreshIfDueAsync(CancellationToken ct)
    {
        var seededAt = await SeededAtAsync(ct);
        if (seededAt is null)
        {
            log.LogInformation("No public demo data yet; building it now. The API answers 503 until it is ready.");
            await RebuildAsync(ct);
            state.Resetting = false;
            return;
        }
        if (state.Resetting)
        {
            // First look after a start: bring the live copy up to this version's schema, then serve it straight away.
            await using (var scope = scopes.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);
            state.Resetting = false;
        }
        if (DateTime.UtcNow - seededAt.Value >= TimeSpan.FromHours(options.ResetHours)) await RebuildAsync(ct);
    }

    private async Task<DateTime?> SeededAtAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("The public demo needs PostgreSQL.");
        var comment = await db.Database.SqlQueryRaw<string?>("SELECT obj_description(oid, 'pg_namespace') AS \"Value\" FROM pg_namespace WHERE nspname = 'public'")
            .FirstOrDefaultAsync(ct);
        return comment is not null && comment.StartsWith(Marker, StringComparison.Ordinal)
            && DateTime.TryParse(comment[Marker.Length..], null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;
    }

    private async Task RebuildAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS {BuildSchema} CASCADE; CREATE SCHEMA {BuildSchema};", ct);
        DemoSchema.Current = BuildSchema;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(ct);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            await DemoSeed.RunAsync(db, users, roles, seedConfig);
            await DemoSeed.SeedExtendedDemoDataAsync(db, users, roles, seedConfig);
        }
        finally { DemoSchema.Current = null; }
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var swap = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(
                $"COMMENT ON SCHEMA {BuildSchema} IS '{Marker}{DateTime.UtcNow:O}'; DROP SCHEMA IF EXISTS {RetiredSchema} CASCADE; " +
                $"ALTER SCHEMA public RENAME TO {RetiredSchema}; ALTER SCHEMA {BuildSchema} RENAME TO public; DROP SCHEMA {RetiredSchema} CASCADE;", ct);
            await swap.CommitAsync(ct);
        }
        log.LogInformation("Public demo data refreshed in {Seconds:N0} s; the next refresh is due in {Hours} hours.", (DateTime.UtcNow - started).TotalSeconds, options.ResetHours);
    }
}

// Hosting platforms such as Render hand the database over as a postgres:// URL; Npgsql wants a keyword connection string.
public static class DatabaseUrl
{
    public static string ToConnectionString(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql") || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("DATABASE_URL must be a postgres:// or postgresql:// URL.");
        var credentials = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            SslMode = SslMode.Prefer,
        }.ConnectionString;
    }
}
