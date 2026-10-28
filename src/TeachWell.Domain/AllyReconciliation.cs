namespace TeachWell.Domain;

// Joining an Ally report to TeachWell's own courses and content (ADR 020).
//
// Pure functions over keys, so the join rules are testable without a database and without Ally.
//
// The join is the part most likely to be imperfect against a real export, so it is reported rather than assumed:
// every row either matches or is counted as unmatched with a reason. A silent 60% match rate would be worse than a
// loud one, because the figures would look complete.

// A TeachWell course, as the join needs it: the Canvas course id it was imported from.
public sealed record CourseKey(Guid Id, string SourceCourseId, string Code);
// A TeachWell element, as the join needs it: its Canvas address, when it has one.
public sealed record ElementKey(Guid Id, string Type, string Title, string? SourceUrl);

public sealed record CourseMatch(string AllyCourseId, Guid? CourseId, string? Code, string Reason);
public sealed record ItemMatch(string AllyItemId, Guid? ElementId, string? Where, string Reason);

// Counts first, so a reader sees the match rate before the detail.
public sealed record ReconciliationSummary(int Total, int Matched, int Unmatched, IReadOnlyList<string> Notes)
{
    // Whole percent, floored, so a report never rounds 99.6% up to "100% matched".
    public int MatchedPercent => Total == 0 ? 0 : (int)(100L * Matched / Total);
}

public static class AllyReconciliation
{
    public const string Matched = "Matched", NoSuchCourse = "NoSuchCourse", NotImported = "NotImported", NoUrl = "NoUrl", NoMatchingUrl = "NoMatchingUrl";

    // Courses join on the Canvas course id, which both sides hold exactly: Ally's "Course id" is the Canvas id, and
    // TeachWell stores it as Course.SourceCourseId at import. This join is reliable; the item join below is not.
    public static (IReadOnlyList<CourseMatch> Matches, ReconciliationSummary Summary) MatchCourses(
        IEnumerable<string> allyCourseIds, IEnumerable<CourseKey> courses)
    {
        var bySource = new Dictionary<string, CourseKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var course in courses) bySource.TryAdd(course.SourceCourseId.Trim(), course);

        var matches = allyCourseIds.Select(id =>
            bySource.TryGetValue(id.Trim(), out var course)
                ? new CourseMatch(id, course.Id, course.Code, Matched)
                : new CourseMatch(id, null, null, NoSuchCourse)).ToList();

        var notes = new List<string>();
        var unmatched = matches.Count(m => m.CourseId is null);
        if (unmatched > 0)
            notes.Add($"{unmatched} course(s) in the Ally report have not been imported into TeachWell, so their figures were not used. Ally covers every course in the LMS; TeachWell covers the ones someone imported.");
        return (matches, new ReconciliationSummary(matches.Count, matches.Count - unmatched, unmatched, notes));
    }

    // Content items join on the URL, because that is the only address both sides hold. It is the weak point of this
    // integration and is treated as such:
    //
    //  - Ally reports a file's or item's URL; TeachWell stores an element's SourceUrl only when the import knew it.
    //    An uploaded course export has no Canvas address unless the uploader supplied one (ADR 010), so every element
    //    in such a course has no SourceUrl and nothing can be matched by URL at all.
    //  - A URL is compared on host, path and nothing else. Query strings carry Ally's and Canvas's own parameters and
    //    sometimes a verifier token, so comparing them would produce false misses and store a secret.
    //  - Most Ally rows are files, which are not TeachWell elements at all. Those are expected not to match, and are
    //    reported separately from items that should have matched and did not.
    public static (IReadOnlyList<ItemMatch> Matches, ReconciliationSummary Summary) MatchItems(
        IEnumerable<(string Id, string? Url, string? MimeType)> items, IEnumerable<ElementKey> elements)
    {
        var byPath = new Dictionary<string, ElementKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in elements)
            if (Path(element.SourceUrl) is { } path) byPath.TryAdd(path, element);

        var matches = items.Select(item =>
        {
            if (Path(item.Url) is not { } path) return new ItemMatch(item.Id, null, null, NoUrl);
            return byPath.TryGetValue(path, out var element)
                ? new ItemMatch(item.Id, element.Id, $"{CourseStructureEvidence.Label(element.Type)}: {element.Title}", Matched)
                : new ItemMatch(item.Id, null, null, IsFile(item.MimeType) ? NotImported : NoMatchingUrl);
        }).ToList();

        var notes = new List<string>();
        var files = matches.Count(m => m.Reason == NotImported);
        var missed = matches.Count(m => m.Reason == NoMatchingUrl);
        var noUrl = matches.Count(m => m.Reason == NoUrl);
        if (files > 0)
            notes.Add($"{files} of Ally's rows are course files, which TeachWell does not import as content. They are the reason for reading Ally's report and are reported against their course rather than a page.");
        if (missed > 0)
            notes.Add($"{missed} of Ally's rows name a page or activity whose address does not match any imported item. Re-import the course, or check that the course's Canvas address was recorded, then upload the report again.");
        if (noUrl > 0)
            notes.Add($"{noUrl} of Ally's rows have no address, so they could not be matched to anything.");
        if (byPath.Count == 0)
            notes.Add("No imported item in this course has a Canvas address, so nothing could be matched by URL. A course brought in from an export file has addresses only if the uploader supplied the course's Canvas address.");

        var matched = matches.Count(m => m.ElementId is not null);
        return (matches, new ReconciliationSummary(matches.Count, matched, matches.Count - matched, notes));
    }

    // Ally's own content types for WYSIWYG items; anything else it reports is a file.
    private static bool IsFile(string? mimeType) =>
        mimeType is not { Length: > 0 } || !mimeType.StartsWith("application/x-", StringComparison.OrdinalIgnoreCase);

    // Host and path, lower-cased, without query, fragment, trailing slash or default port. Query strings are dropped
    // deliberately: they carry parameters that differ between the two tools, and sometimes a verifier token.
    public static string? Path(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var value = url.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal)) value = "https:" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            // A relative address still joins, on its path alone.
            return value.StartsWith('/') ? Trim(value.Split('?', '#')[0]) : null;
        return uri.Host.ToLowerInvariant() + Trim(uri.AbsolutePath);
    }

    private static string Trim(string path) =>
        (path.Length > 1 ? path.TrimEnd('/') : path).ToLowerInvariant();
}
