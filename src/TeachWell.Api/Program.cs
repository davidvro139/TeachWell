using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure;
using TeachWell.Api;
using TeachWell.Infrastructure.Canvas;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>((sp, o) => {
    var config = sp.GetRequiredService<IConfiguration>();
    var connection = config.GetConnectionString("Database")
        ?? (config["DATABASE_URL"] is { Length: > 0 } databaseUrl ? DatabaseUrl.ToConnectionString(databaseUrl) : null)
        ?? throw new InvalidOperationException("ConnectionStrings__Database (or DATABASE_URL) is required.");
    // The public demo builds its next copy of the sample data in a separate schema (PublicDemo.cs).
    if (DemoSchema.Current is { } schema) connection = new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
    o.UseNpgsql(connection);
});
// Used only when ForwardedHeaders:Enabled is set (below): behind a TLS-terminating proxy such as Render's, take the client
// address and scheme from the proxy's headers so the per-address login rate limit applies per visitor rather than to the
// proxy. Only the nearest hop is trusted (the default ForwardLimit of 1).
// appsettings.json accepts only requests addressed to localhost. The public demo is served on whatever address its host
// assigns (and health-checked there), and TeachWell never builds links from the Host header, so it accepts any host.
builder.Services.AddOptions<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>().PostConfigure<IConfiguration>((o, config) => {
    if (config.GetValue<bool>("Features:PublicDemo")) o.AllowedHosts = ["*"];
});
builder.Services.Configure<ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(o => {
    o.Password.RequiredLength = 12;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => {
    o.Cookie.Name = "__Host-TeachWell";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(2);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "__Host-TeachWell-CSRF"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("canvas", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<IIdentityProvider, DemoIdentityProvider>();
builder.Services.AddScoped<ICourseContentSource, StoredCourseContentSource>();
builder.Services.AddScoped<ICanvasImportService, TeachWell.Infrastructure.Canvas.CanvasImportService>();
builder.Services.AddScoped<ICourseExportService, TeachWell.Infrastructure.Canvas.CourseExportService>();
builder.Services.AddScoped<ICourseSharingService, CourseSharingService>();
builder.Services.AddScoped<IRubricService, TeachWell.Infrastructure.Reviews.RubricService>();
builder.Services.AddScoped<IReviewService, TeachWell.Infrastructure.Reviews.ReviewService>();
builder.Services.AddScoped<IStandardSetService, TeachWell.Infrastructure.Reviews.StandardSetService>();
builder.Services.AddScoped<IGuideService, TeachWell.Infrastructure.Reviews.GuideService>();
builder.Services.AddScoped<IReadinessService, TeachWell.Infrastructure.Reviews.ReadinessService>();
builder.Services.AddSingleton<TeachWell.Infrastructure.Reviews.ReadinessOptions>();
builder.Services.AddScoped<IOversightService, TeachWell.Infrastructure.Reviews.OversightService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<TeachWell.Infrastructure.Reviews.OversightOptions>();
builder.Services.AddHostedService<TeachWell.Infrastructure.Reviews.OversightRecheckService>();
builder.Services.AddSingleton<PublicDemoOptions>();
builder.Services.AddSingleton<PublicDemoState>();
builder.Services.AddSingleton<PublicDemoResetService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PublicDemoResetService>());
// Role changes renew the user's security stamp (ADR 014); revalidate sessions every minute so a revoked role stops working promptly.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.AddScoped<ITaskService, TeachWell.Infrastructure.Tasks.TaskService>();
builder.Services.AddScoped<ISupportService, TeachWell.Infrastructure.Support.SupportService>();
builder.Services.AddScoped<IKnowledgeService, TeachWell.Infrastructure.Support.KnowledgeService>();
builder.Services.AddScoped<IGlossaryService, TeachWell.Infrastructure.Support.GlossaryService>();
builder.Services.AddScoped<IWorkshopService, TeachWell.Infrastructure.Support.WorkshopService>();
builder.Services.AddScoped<ICompetencyService, TeachWell.Infrastructure.Reports.CompetencyService>();
builder.Services.AddScoped<IReportService, TeachWell.Infrastructure.Reports.ReportService>();
builder.Services.AddScoped<IConformanceService, TeachWell.Infrastructure.Reports.ConformanceService>();
builder.Services.AddScoped<ISyllabusService, TeachWell.Infrastructure.SyllabusService>();
builder.Services.AddScoped<IAllyCheckService, TeachWell.Infrastructure.Ally.AllyCheckService>();
builder.Services.AddSingleton<CanvasIntegrationOptions>();
builder.Services.AddSingleton<InstitutionSettings>();
builder.Services.AddSingleton<CourseExportOptions>();
builder.Services.AddSingleton<CanvasHostPolicy>();
builder.Services.AddSingleton<ICanvasTokenProtector, CanvasTokenProtector>();
builder.Services.AddHttpClient<ICanvasClient, CanvasPatClient>(c => { c.Timeout = TimeSpan.FromSeconds(20); c.MaxResponseContentBufferSize = 65536; })
    .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<CanvasHostPolicy>().CreateHandler());
builder.Services.AddScoped<ICanvasConnectionService, CanvasConnectionService>();
var app = builder.Build();
_ = app.Services.GetRequiredService<CanvasIntegrationOptions>();
_ = app.Services.GetRequiredService<InstitutionSettings>();
_ = app.Services.GetRequiredService<CourseExportOptions>();
_ = app.Services.GetRequiredService<TeachWell.Infrastructure.Reviews.ReadinessOptions>();
_ = app.Services.GetRequiredService<TeachWell.Infrastructure.Reviews.OversightOptions>();
var publicDemo = app.Services.GetRequiredService<PublicDemoOptions>();
if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled")) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
if (publicDemo.Resets)
{
    var demoState = app.Services.GetRequiredService<PublicDemoState>();
    app.Use(async (context, next) => {
        if (demoState.Resetting && context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.RetryAfter = "30";
            await Results.Problem(statusCode: 503, title: "The demo is loading fresh sample data. Try again in a minute.").ExecuteAsync(context);
            return;
        }
        await next();
    });
}
app.MapGet("/healthz", () => Results.Ok());
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.Use(async (context, next) => {
    if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "Invalid CSRF token.").ExecuteAsync(context); return; }
    }
    await next();
});
var api = app.MapGroup("/api/v1");
api.MapCourseSharing();
api.MapCanvas();
api.MapCanvasImports();
api.MapCourseExports();
api.MapReviews();
api.MapStandardSets();
api.MapDemoSignIn();
api.MapCourseContent();
api.MapRevisionTasks();
api.MapSupport();
api.MapAlignment();
api.MapReadiness();
api.MapOversight();
api.MapAlly();
api.MapGet("/auth/csrf", (HttpContext c, IAntiforgery csrf) => Results.Ok(new { token = csrf.GetAndStoreTokens(c).RequestToken }));
api.MapPost("/auth/login", async (LoginRequest input, SignInManager<AppUser> signIn, UserManager<AppUser> users) => {
    // The public demo offers one-click sign-in only, so nobody can lock the shared personas out with wrong passwords.
    if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue<bool>("Features:DemoAuth") || publicDemo.Enabled) return Results.Problem(statusCode: 404);
    if (!Validator.TryValidateObject(input, new ValidationContext(input), [], true)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Enter a valid email and password (maximum 200 characters)."] });
    var user = await users.FindByEmailAsync(input.Email);
    if (user is null || !user.IsActive) return Results.Problem(statusCode: 401, title: "Sign-in failed.");
    var result = await signIn.PasswordSignInAsync(user, input.Password, false, true);
    if (result.Succeeded) return Results.NoContent();
    // Distinguishing a lockout from a wrong password here does not enumerate accounts: this endpoint exists only for
    // the three publicly documented demo logins (see README), gated to Development, and never reachable in production.
    return Results.Problem(statusCode: 401, title: result.IsLockedOut
        ? "Too many failed sign-in attempts. This account is temporarily locked; wait a few minutes and try again."
        : "Sign-in failed. Check your email and password.");
}).RequireRateLimiting("login");
api.MapPost("/auth/logout", async (SignInManager<AppUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
api.MapGet("/me", async (HttpContext c, IIdentityProvider identity) => await identity.GetUserAsync(c.User) is { } user ? Results.Ok(user) : Results.Unauthorized()).RequireAuthorization();
api.MapGet("/courses", async (string? role, HttpContext c, IIdentityProvider identity, ICourseContentSource source, CancellationToken ct) => {
    var user = await identity.GetUserAsync(c.User);
    if (user is null) return Results.Unauthorized();
    if (role is not (Roles.Instructor or Roles.Reviewer)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Choose Instructor or CTLReviewer."] });
    if (!user.Roles.Contains(role)) return Results.Forbid();
    return Results.Ok(await source.ListCoursesAsync(user, role, ct));
}).RequireAuthorization();
api.MapGet("/courses/{id:guid}", async (Guid id, HttpContext c, IIdentityProvider identity, ICourseContentSource source, CancellationToken ct) => {
    var user = await identity.GetUserAsync(c.User);
    if (user is null) return Results.Unauthorized();
    var course = await source.GetCourseAsync(user, id, ct);
    return course is null ? Results.NotFound() : Results.Ok(new { course.Id, course.Title, course.Code, course.SourceType, course.SourceOrigin, course.SourceCourseId, course.LastSyncedAt,
        warnings = System.Text.Json.JsonSerializer.Deserialize<string[]>(course.ImportWarningsJson),
        permissions = new { canManageSharing = course.OwnerId == user.Id && user.Roles.Contains(Roles.Instructor), canReview = user.Roles.Contains(Roles.Reviewer),
            canRefreshExport = course.SourceType == TeachWell.Infrastructure.Canvas.CourseExportService.SourceType && (course.OwnerId == user.Id && user.Roles.Contains(Roles.Instructor) || user.Roles.Contains(Roles.Reviewer)) },
        course.SourceWebUrl, course.DeliveryMode, course.Department,
        elements = course.Elements.Where(e => e.Type != ReviewVocabulary.CourseType).OrderBy(e => e.Position).ThenBy(e => e.Title).Select(e => new { e.Id, e.ParentElementId, e.TargetElementId, e.Type, e.Title, e.PlainText, e.SourceUrl, e.SourceStatus, e.SourceWarning, e.Position,
            currentSnapshotId = e.Snapshots.OrderByDescending(s => s.FetchedAt).Select(s => (Guid?)s.Id).FirstOrDefault() }) });
}).RequireAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
if (args.Contains("--migrate") || args.Contains("--seed"))
{
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Demo setup commands are development-only.");
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (args.Contains("--seed"))
    {
        var seedUsers = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var seedRoles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await DemoSeed.RunAsync(db, seedUsers, seedRoles, app.Configuration);
        // Extra personas, courses, and worked examples across every review/task/ticket/competency status, for exploring
        // the app locally. Kept separate from RunAsync so the integration test suite's baseline seed stays minimal and stable.
        // --baseline-only skips it for the browser suite, whose journeys assume the same minimal baseline.
        if (!args.Contains("--baseline-only")) await DemoSeed.SeedExtendedDemoDataAsync(db, seedUsers, seedRoles, app.Configuration);
    }
    return;
}
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();
public record LoginRequest([property: Required, EmailAddress, MaxLength(254)] string Email, [property: Required, MaxLength(200)] string Password);
public partial class Program { }
