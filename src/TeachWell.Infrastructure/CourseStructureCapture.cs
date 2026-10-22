using Microsoft.EntityFrameworkCore;
using TeachWell.Domain;

namespace TeachWell.Infrastructure;

// A course-level review targets one synthetic element per course (type "course"). Its current PlainText/EvidenceJson describe
// the course's structure, and every change appends an immutable snapshot exactly like page and assignment content, so course
// reviews, findings and revision tasks reuse the same pinning, scoring and history guarantees.
public static class CourseStructureCapture
{
    public const string SourceElementId = "course";
    private const int ModuleLimit = 200, ItemLimit = 200, UnplacedLimit = 500;

    public static async Task<CourseElement> EnsureAsync(AppDbContext db, Course course, CancellationToken ct)
    {
        var element = await db.Elements.Include(e => e.Snapshots)
            .SingleOrDefaultAsync(e => e.CourseId == course.Id && e.Type == ReviewVocabulary.CourseType && e.SourceElementId == SourceElementId, ct);
        if (element is not null) return element;
        element = new CourseElement
        {
            Id = Guid.NewGuid(), CourseId = course.Id, Type = ReviewVocabulary.CourseType, SourceElementId = SourceElementId, Title = "Course structure",
            SourceUrl = course.SourceWebUrl ?? (course.SourceType == "Canvas" && Uri.TryCreate(course.SourceOrigin, UriKind.Absolute, out var origin)
                ? new Uri(origin, $"courses/{Uri.EscapeDataString(course.SourceCourseId)}").AbsoluteUri : null)
        };
        db.Elements.Add(element);
        return element;
    }

    // Rebuilds the structure and appends a snapshot only when it differs from the latest one. The caller saves.
    public static async Task CaptureAsync(AppDbContext db, CourseElement courseElement, CancellationToken ct)
    {
        var structure = await BuildAsync(db, courseElement.CourseId, ct);
        courseElement.PlainText = structure.Outline();
        courseElement.EvidenceJson = structure.Serialize();
        courseElement.PreviewHtml = null;
        var snapshot = ElementSnapshot.Capture(courseElement, DateTime.UtcNow);
        var latest = courseElement.Snapshots.OrderByDescending(s => s.FetchedAt).FirstOrDefault();
        if (latest is null || latest.ContentHash != snapshot.ContentHash) courseElement.Snapshots.Add(snapshot);
    }

    public static async Task RefreshIfPresentAsync(AppDbContext db, Guid courseId, CancellationToken ct)
    {
        var element = await db.Elements.Include(e => e.Snapshots)
            .SingleOrDefaultAsync(e => e.CourseId == courseId && e.Type == ReviewVocabulary.CourseType && e.SourceElementId == SourceElementId, ct);
        if (element is null) return;
        await CaptureAsync(db, element, ct);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<CourseStructureEvidence> BuildAsync(AppDbContext db, Guid courseId, CancellationToken ct)
    {
        var elements = await db.Elements.AsNoTracking().Where(e => e.CourseId == courseId && e.Type != ReviewVocabulary.CourseType && e.SourceStatus != "Removed").ToListAsync(ct);
        var ids = elements.Select(e => e.Id).ToList();
        var latest = await db.ElementSnapshots.AsNoTracking().Where(s => ids.Contains(s.ElementId)).GroupBy(s => s.ElementId)
            .Select(g => new { ElementId = g.Key, Id = g.OrderByDescending(s => s.FetchedAt).Select(s => s.Id).First() }).ToDictionaryAsync(g => g.ElementId, g => g.Id, ct);
        var aligned = (await db.CompetencyMappings.AsNoTracking().Where(m => db.CourseCompetencies.Any(c => c.Id == m.CompetencyId && c.CourseId == courseId))
            .Select(m => m.ElementId).ToListAsync(ct)).ToHashSet();
        var outcomes = await db.CourseCompetencies.AsNoTracking().Where(c => c.CourseId == courseId).OrderBy(c => c.Code)
            .Select(c => new OutcomeEvidence(c.Id, c.Code, c.Title, c.Description, c.Mappings.Count, c.Mappings.Select(m => m.ElementId).ToList())).ToListAsync(ct);
        var competencies = outcomes.Count;
        var byId = elements.ToDictionary(e => e.Id);
        var truncated = false;

        CourseItemEvidence Item(CourseElement placement)
        {
            var content = placement.TargetElementId is { } t && byId.TryGetValue(t, out var target) ? target : placement;
            var status = placement.SourceStatus != "Published" ? placement.SourceStatus : content.SourceStatus;
            // Graded discussions carry assignment metadata too; ungraded ones have none, so their points stay null.
            var assignment = ReviewVocabulary.IsAssessment(content.Type, graded: true) ? ContentEvidence.Parse(content.EvidenceJson)?.Assignment : null;
            return new(content.Id, content.Type, placement.Title, status, latest.TryGetValue(content.Id, out var s) ? s : null,
                assignment?.HasDueDate, assignment?.HasRubric, aligned.Contains(content.Id),
                ReviewVocabulary.IsContent(content.Type) ? TextStats.CountWords(content.PlainText) : null, assignment?.PointsPossible, assignment?.DueAt,
                ReviewVocabulary.IsAssessment(content.Type, graded: true) ? Bloom.HighestLevelIn(content.Title + " " + content.PlainText) : null);
        }
        List<T> Cap<T>(IEnumerable<T> source, int limit)
        {
            var list = source.ToList();
            if (list.Count > limit) truncated = true;
            return list.Take(limit).ToList();
        }
        static IEnumerable<CourseElement> Ordered(IEnumerable<CourseElement> items) => items.OrderBy(e => e.Position).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase);

        var modules = Cap(Ordered(elements.Where(e => e.Type == "module")), ModuleLimit).Select(m =>
            new ModuleEvidence(m.Id, m.Title, m.SourceStatus, m.Position, Cap(Ordered(elements.Where(e => e.ParentElementId == m.Id)), ItemLimit).Select(Item).ToList())).ToList();
        var placed = modules.SelectMany(m => m.Items.Select(i => i.ElementId)).ToHashSet();
        var targeted = elements.Where(e => e.TargetElementId is not null).Select(e => e.TargetElementId!.Value).ToHashSet();
        var unplaced = Cap(Ordered(elements.Where(e => ReviewVocabulary.IsContent(e.Type) && e.ParentElementId is null && !targeted.Contains(e.Id) && !placed.Contains(e.Id))), UnplacedLimit)
            .Select(Item).ToList();
        return new(CourseStructureEvidence.CurrentVersion, CourseStructureEvidence.KindName, modules, unplaced, competencies, truncated, outcomes);
    }
}
