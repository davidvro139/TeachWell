using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Reviews;

namespace TeachWell.Infrastructure.Support;

public sealed partial class KnowledgeService(AppDbContext db) : IKnowledgeService
{
    private const string Stale = "This article changed since you opened it. Reload it and try again.";
    private static bool IsReviewer(CurrentUser user) => user.Roles.Contains(Roles.Reviewer);

    public async Task<IReadOnlyList<KnowledgeArticleSummaryDto>> ListAsync(CurrentUser user, CancellationToken ct)
    {
        var query = db.KnowledgeArticles.AsNoTracking().Where(a => a.TenantId == user.TenantId);
        if (!IsReviewer(user)) query = query.Where(a => a.Status == ReviewVocabulary.Published);
        // JSON tag deserialization cannot translate to SQL; materialize first, then project in memory.
        var rows = await query.OrderByDescending(a => a.PublishedAt ?? a.CreatedAt).ToListAsync(ct);
        return rows.Select(a => new KnowledgeArticleSummaryDto(a.Id, a.Title, a.Slug, ReviewMapping.Strings(a.TagsJson), a.Status, a.PublishedAt)).ToList();
    }

    public async Task<ServiceResult<KnowledgeArticleDto>> GetAsync(CurrentUser user, string slug, CancellationToken ct)
    {
        var article = await db.KnowledgeArticles.AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == user.TenantId && a.Slug == slug, ct);
        if (article is null || article.Status != ReviewVocabulary.Published && !IsReviewer(user)) return ServiceResult<KnowledgeArticleDto>.NotFound();
        return ServiceResult<KnowledgeArticleDto>.Ok(await Dto(article, user, ct));
    }

    public async Task<ServiceResult<KnowledgeArticleDto>> CreateAsync(CurrentUser user, CreateArticleInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<KnowledgeArticleDto>.NotFound();
        var errors = SupportValidation.Article(input.Title, input.Body, input.Tags);
        if (errors.Count > 0) return ServiceResult<KnowledgeArticleDto>.Invalid(errors);
        var article = new KnowledgeArticle
        {
            Id = Guid.NewGuid(), TenantId = user.TenantId, Title = input.Title!.Trim(), Slug = await UniqueSlug(user.TenantId, input.Title, null, ct),
            Body = input.Body!.Trim(), TagsJson = ReviewMapping.Json(input.Tags), AuthorId = user.Id
        };
        db.KnowledgeArticles.Add(article);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = article.Id, Action = "KnowledgeArticleCreated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<KnowledgeArticleDto>.Ok(await Dto(article, user, ct));
    }

    public async Task<ServiceResult<KnowledgeArticleDto>> UpdateAsync(CurrentUser user, Guid id, UpdateArticleInput input, CancellationToken ct)
    {
        if (!IsReviewer(user)) return ServiceResult<KnowledgeArticleDto>.NotFound();
        var article = await db.KnowledgeArticles.SingleOrDefaultAsync(a => a.Id == id && a.TenantId == user.TenantId, ct);
        if (article is null) return ServiceResult<KnowledgeArticleDto>.NotFound();
        var errors = SupportValidation.Article(input.Title, input.Body, input.Tags);
        if (input.Status is not (null or ReviewVocabulary.Draft or ReviewVocabulary.Published)) errors["status"] = ["Choose Draft or Published."];
        if (errors.Count > 0) return ServiceResult<KnowledgeArticleDto>.Invalid(errors);
        if (article.Revision != input.Revision) return ServiceResult<KnowledgeArticleDto>.Conflict(Stale);
        db.Entry(article).Property(a => a.Revision).OriginalValue = input.Revision;
        article.Revision = Guid.NewGuid();
        // The slug is permanent once created, even across title edits, so a bookmarked /knowledge/{slug} link never breaks.
        article.Title = input.Title!.Trim(); article.Body = input.Body!.Trim(); article.TagsJson = ReviewMapping.Json(input.Tags);
        article.Version++; article.UpdatedAt = DateTime.UtcNow;
        article.Status = input.Status ?? article.Status;
        article.PublishedAt = article.Status == ReviewVocabulary.Published ? DateTime.UtcNow : null;
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = article.Id, Action = "KnowledgeArticleUpdated" });
        await db.SaveChangesAsync(ct);
        return ServiceResult<KnowledgeArticleDto>.Ok(await Dto(article, user, ct));
    }

    private async Task<string> UniqueSlug(Guid tenantId, string title, Guid? excludeId, CancellationToken ct)
    {
        var baseSlug = Slugify(title);
        var slug = baseSlug;
        for (var n = 2; await db.KnowledgeArticles.AnyAsync(a => a.TenantId == tenantId && a.Slug == slug && a.Id != (excludeId ?? Guid.Empty), ct); n++)
            slug = $"{baseSlug}-{n}";
        return slug;
    }
    private static string Slugify(string title)
    {
        var slug = NonSlugCharacters().Replace(title.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "article" : slug.Length <= 160 ? slug : slug[..160].TrimEnd('-');
    }
    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonSlugCharacters();

    private async Task<KnowledgeArticleDto> Dto(KnowledgeArticle a, CurrentUser user, CancellationToken ct)
    {
        var authorName = await db.Users.AsNoTracking().Where(u => u.Id == a.AuthorId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct) ?? "Unknown";
        return new(a.Id, a.Title, a.Slug, a.Body, ReviewMapping.Strings(a.TagsJson), a.Status, a.Version, authorName, a.CreatedAt, a.UpdatedAt, a.PublishedAt, a.Revision, IsReviewer(user));
    }
}
