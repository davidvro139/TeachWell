using Microsoft.Extensions.Configuration;

namespace TeachWell.Infrastructure.Canvas;

// Upload limit for course exports (CourseExports:MaxUploadMegabytes, default 500). Validated at startup.
public sealed class CourseExportOptions
{
    public const int DefaultMegabytes = 500, MaxMegabytes = 2048;
    public int MaxUploadMegabytes { get; }
    public long MaxUploadBytes => MaxUploadMegabytes * 1024L * 1024;

    public CourseExportOptions(IConfiguration config)
    {
        var configured = config["CourseExports:MaxUploadMegabytes"];
        if (string.IsNullOrWhiteSpace(configured)) { MaxUploadMegabytes = DefaultMegabytes; return; }
        if (!int.TryParse(configured, out var mb) || mb is < 1 or > MaxMegabytes)
            throw new InvalidOperationException($"CourseExports:MaxUploadMegabytes must be a whole number from 1 to {MaxMegabytes}.");
        MaxUploadMegabytes = mb;
    }
}
