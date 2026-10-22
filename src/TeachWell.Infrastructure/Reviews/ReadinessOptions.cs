using Microsoft.Extensions.Configuration;

namespace TeachWell.Infrastructure.Reviews;

// D2 re-check rules (ADR 013): Readiness:ValidityMonths (default 36) and Readiness:ChangeThresholdPercent (default 25). Validated at startup.
public sealed class ReadinessOptions
{
    public int ValidityMonths { get; } = 36;
    public int ChangeThresholdPercent { get; } = 25;

    public ReadinessOptions() { }
    public ReadinessOptions(IConfiguration config)
    {
        ValidityMonths = Read(config, "Readiness:ValidityMonths", 36, 1, 120);
        ChangeThresholdPercent = Read(config, "Readiness:ChangeThresholdPercent", 25, 1, 100);
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
