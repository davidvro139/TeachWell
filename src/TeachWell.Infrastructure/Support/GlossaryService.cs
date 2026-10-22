using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Reviews;

namespace TeachWell.Infrastructure.Support;

/// <summary>
/// The college's changes to the built-in glossary (ADR 015 amendment). The built-in terms ship with the web app; this stores only what the
/// CTL changed, and the app merges the two. Everyone who can use the glossary reads the changes; only CTL reviewers make them.
/// </summary>
public sealed partial class GlossaryService(AppDbContext db) : IGlossaryService
{
    private const string Stale = "This term changed since you opened it. Reload the glossary and try again.";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);

    public async Task<IReadOnlyList<GlossaryEntryDto>> ListAsync(CurrentUser user, CancellationToken ct)
    {
        var rows = await db.GlossaryEntries.AsNoTracking().Where(e => e.TenantId == user.TenantId).OrderBy(e => e.TermId).ToListAsync(ct);
        var names = await Names(rows.Select(r => r.UpdatedBy), ct);
        return rows.Select(r => Dto(r, names)).ToList();
    }

    public async Task<ServiceResult<GlossaryEntryDto>> SaveAsync(CurrentUser user, string termId, GlossaryEntryInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<GlossaryEntryDto>.NotFound();
        var errors = Validate(termId, input);
        if (errors.Count > 0) return ServiceResult<GlossaryEntryDto>.Invalid(errors);

        var entry = await db.GlossaryEntries.SingleOrDefaultAsync(e => e.TenantId == user.TenantId && e.TermId == termId, ct);
        if (entry is null)
        {
            entry = new GlossaryEntry { Id = Guid.NewGuid(), TenantId = user.TenantId, TermId = termId };
            db.GlossaryEntries.Add(entry);
        }
        else
        {
            if (input.Revision != entry.Revision) return ServiceResult<GlossaryEntryDto>.Conflict(Stale);
            db.Entry(entry).Property(e => e.Revision).OriginalValue = entry.Revision;
            entry.Revision = Guid.NewGuid();
        }
        // Hiding keeps whatever wording was there, so showing the term again later restores it.
        if (!input.Hidden)
        {
            entry.Term = input.Term!.Trim();
            entry.Definition = input.Definition!.Trim();
            entry.PhrasesJson = ReviewMapping.Json(Clean(input.Phrases));
            entry.TagsJson = ReviewMapping.Json(Clean(input.Tags));
            entry.LinksJson = JsonSerializer.Serialize(
                (input.Links ?? []).Select(l => new GlossaryLinkDto(l.Label!.Trim(), (l.Source ?? "").Trim(), l.Url!.Trim())).ToArray(), Web);
        }
        else if (entry.Term.Length == 0) entry.Term = (input.Term ?? termId).Trim();
        entry.Hidden = input.Hidden;
        entry.UpdatedBy = user.Id;
        entry.UpdatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = entry.Id, Action = input.Hidden ? "GlossaryTermHidden" : "GlossaryTermSaved" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<GlossaryEntryDto>.Ok(Dto(entry, await Names([entry.UpdatedBy], ct)));
    }

    public async Task<ServiceResult<IReadOnlyList<GlossaryEntryDto>>> ResetAsync(CurrentUser user, string termId, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<IReadOnlyList<GlossaryEntryDto>>.NotFound();
        var entry = await db.GlossaryEntries.SingleOrDefaultAsync(e => e.TenantId == user.TenantId && e.TermId == termId, ct);
        if (entry is null) return ServiceResult<IReadOnlyList<GlossaryEntryDto>>.NotFound();
        db.GlossaryEntries.Remove(entry);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = entry.Id, Action = "GlossaryTermReset" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<IReadOnlyList<GlossaryEntryDto>>.Ok(await ListAsync(user, ct));
    }

    internal static Dictionary<string, string[]> Validate(string termId, GlossaryEntryInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (termId.Length > 60 || !TermIdPattern().IsMatch(termId))
            errors["termId"] = ["Use lower-case letters, numbers and single hyphens, up to 60 characters (for example \"course-shell\")."];
        if (input.Hidden) return errors;

        if (string.IsNullOrWhiteSpace(input.Term) || input.Term.Trim().Length > 80) errors["term"] = ["Give the term a name of up to 80 characters."];
        if (string.IsNullOrWhiteSpace(input.Definition) || input.Definition.Trim().Length > 1200)
            errors["definition"] = ["Write a definition of up to 1,200 characters."];
        var phrases = Clean(input.Phrases);
        // Very short phrases would match inside ordinary sentences all over the app.
        if (phrases.Length > 12 || phrases.Any(p => p.Length is < 3 or > 60))
            errors["phrases"] = ["List up to 12 phrases, each 3 to 60 characters."];
        var tags = Clean(input.Tags);
        if (tags.Length > 10 || tags.Any(t => t.Length > 40)) errors["tags"] = ["List up to 10 tags, each up to 40 characters."];
        var links = input.Links ?? [];
        if (links.Length > 5) errors["links"] = ["Add up to 5 links."];
        for (var i = 0; i < links.Length && i < 5; i++)
        {
            var l = links[i];
            if (string.IsNullOrWhiteSpace(l.Label) || l.Label.Trim().Length > 120) errors[$"links[{i}].label"] = ["Give the link a title of up to 120 characters."];
            if ((l.Source ?? "").Trim().Length > 120) errors[$"links[{i}].source"] = ["Keep the source to 120 characters."];
            if (!IsSafeLink(l.Url)) errors[$"links[{i}].url"] = ["Use a full web address starting with https:// or http://, with no user name or password."];
        }
        return errors;
    }

    /// <summary>
    /// Only plain web pages: no javascript:, data: or file: links, and no credentials in the address. TeachWell never fetches these;
    /// the browser opens them in a new tab, so there is no server-side request to guard.
    /// </summary>
    internal static bool IsSafeLink(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.Trim().Length <= 500 &&
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.UserInfo.Length == 0 && uri.Host.Length > 0;

    private static string[] Clean(string[]? values) =>
        (values ?? []).Select(v => (v ?? "").Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex TermIdPattern();

    private async Task<Dictionary<Guid, string>> Names(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private static GlossaryEntryDto Dto(GlossaryEntry e, Dictionary<Guid, string> names) =>
        new(e.TermId, e.Term, e.Definition, ReviewMapping.Strings(e.PhrasesJson), ReviewMapping.Strings(e.TagsJson),
            JsonSerializer.Deserialize<GlossaryLinkDto[]>(e.LinksJson, Web) ?? [], e.Hidden, e.UpdatedAt,
            names.GetValueOrDefault(e.UpdatedBy, "Unknown"), e.Revision);
}
