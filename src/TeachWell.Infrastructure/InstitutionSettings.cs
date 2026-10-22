using Microsoft.Extensions.Configuration;

namespace TeachWell.Infrastructure;

// Institution-wide settings that are deployment configuration rather than CTL standards. Validated at startup so a typo fails
// loudly instead of silently grouping deadlines by UTC.
public sealed class InstitutionSettings
{
    public TimeZoneInfo TimeZone { get; }

    public InstitutionSettings(IConfiguration config)
    {
        var id = config["Institution:TimeZone"];
        if (string.IsNullOrWhiteSpace(id)) { TimeZone = TimeZoneInfo.Utc; return; }
        try { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim()); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"Institution:TimeZone \"{id}\" is not a time zone this server recognizes. Use an IANA id such as America/Denver, or a Windows id.", e);
        }
    }

    public static readonly InstitutionSettings Utc = new(new ConfigurationBuilder().Build());
}
