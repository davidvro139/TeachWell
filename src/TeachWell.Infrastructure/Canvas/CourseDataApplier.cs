using System.Text.Json;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Canvas;

// Applies normalized course content (from the Canvas API or an uploaded course export) to a local course. Refreshing keeps
// local element IDs (and so shares, reviews and tasks), appends a snapshot only where content changed, and marks items
// that are no longer returned as Removed rather than deleting them; earlier snapshots are never rewritten.
public static class CourseDataApplier
{
    public const int ElementLimit = 3000;
    private const string RemovedWarning = "Previously imported items are no longer returned and are marked Removed.";

    // Everything is validated before anything is changed, so a rejected import leaves the tracked course untouched.
    public static bool IsConsistent(CanvasCourseData data)
    {
        if (data.Elements.Count > ElementLimit || data.Elements.Select(e => (e.Type, e.Id)).Distinct().Count() != data.Elements.Count) return false;
        var keys = data.Elements.Select(e => (e.Type, e.Id)).ToHashSet();
        return data.Elements.All(e => (e.ParentModuleId is null || keys.Contains(("module", e.ParentModuleId)))
            && (e.TargetId is null || e.TargetType is not null && keys.Contains((e.TargetType, e.TargetId))));
    }

    public static List<string> Apply(Course course, CanvasCourseData data, DateTime now)
    {
        var bySource = course.Elements.ToDictionary(e => (e.Type, e.SourceElementId));
        foreach (var source in data.Elements.Where(s => !bySource.ContainsKey((s.Type, s.Id))))
        {
            var added = new CourseElement { Id = Guid.NewGuid(), CourseId = course.Id, Type = source.Type, SourceElementId = source.Id };
            bySource.Add((source.Type, source.Id), added); course.Elements.Add(added);
        }
        var seen = new HashSet<(string, string)>();
        foreach (var source in data.Elements)
        {
            var element = bySource[(source.Type, source.Id)]; seen.Add((source.Type, source.Id));
            element.Title = source.Title; element.PlainText = source.PlainText; element.SourceUrl = source.SourceUrl;
            element.SourceStatus = source.Status; element.SourceWarning = source.Warning; element.Position = source.Position;
            element.EvidenceJson = source.Evidence?.Serialize(); element.PreviewHtml = source.PreviewHtml;
            element.ParentElementId = source.ParentModuleId is null ? null : bySource[("module", source.ParentModuleId)].Id;
            element.TargetElementId = source.TargetId is null ? null : bySource[(source.TargetType!, source.TargetId)].Id;
            CaptureIfChanged(element, now);
        }
        var warnings = data.Warnings.ToList();
        // The synthetic course-structure element is TeachWell's own, never returned by a source, so it is not "missing".
        foreach (var missing in course.Elements.Where(e => e.Type != ReviewVocabulary.CourseType && !seen.Contains((e.Type, e.SourceElementId))))
        {
            missing.SourceStatus = "Removed";
            missing.SourceWarning = "This item was not returned by the latest completed import. Earlier snapshots are retained.";
            CaptureIfChanged(missing, now);
            if (!warnings.Contains(RemovedWarning)) warnings.Add(RemovedWarning);
        }
        course.Title = data.Course.Title; course.Code = data.Course.Code; course.LastSyncedAt = now;
        course.ImportWarningsJson = JsonSerializer.Serialize(warnings);
        course.Revision = Guid.NewGuid();
        return warnings;
    }

    private static void CaptureIfChanged(CourseElement element, DateTime now)
    {
        var snapshot = ElementSnapshot.Capture(element, now);
        var latest = element.Snapshots.OrderByDescending(s => s.FetchedAt).FirstOrDefault();
        if (latest is null || latest.ContentHash != snapshot.ContentHash) element.Snapshots.Add(snapshot);
    }
}
