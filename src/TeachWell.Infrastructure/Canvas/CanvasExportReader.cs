using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CourseExportException(string message) : Exception(message);

// Reads a Canvas course export (IMS Common Cartridge, .imscc) into the same normalized shape the Canvas API reader produces.
// The archive is read in memory and never extracted to disk. Every entry is untrusted: names, declared sizes, compression
// ratios and XML are all checked, and nothing in the archive is executed or followed.
// Built to Canvas's documented export layout (imsmanifest.xml, course_settings/, wiki_content/, one folder per assignment)
// and verified against fictional exports in that layout only; confirm against a real export before relying on it.
public static class CanvasExportReader
{
    // Exports are large mostly because of course files and media, which are never opened. The entry limit bounds the size of the
    // zip directory; the expanded-size and compression-ratio limits apply to the XML and HTML entries TeachWell actually reads.
    public const int MaxEntries = 20000;
    private const int MaxHtmlChars = 60000, MaxCompressionRatio = 200;
    private const long MaxExpandedBytes = 250L * 1024 * 1024, MaxXmlBytes = 5L * 1024 * 1024;

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = MaxXmlBytes * 2,
        IgnoreComments = true, IgnoreProcessingInstructions = true
    };

    // Item SourceUrls are returned as paths relative to the course ("pages/x"); resolve them with ResolveLinks once the course's Canvas address is known.
    public static CanvasCourseData Read(Stream export)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(export, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new CourseExportException("This file is not a valid course export. Export the course from Canvas (Settings → Export Course Content) and upload the .imscc file."); }
        using (zip)
        {
            if (zip.Entries.Count > MaxEntries) throw new CourseExportException($"This export contains more than {MaxEntries:N0} files, which is beyond TeachWell's import limit.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            long expanded = 0, mediaBytes = 0;
            var mediaFiles = 0;
            foreach (var entry in zip.Entries)
            {
                if (!SafeName(entry.FullName)) throw new CourseExportException("This export contains a file with an unsafe or invalid path, so it was rejected.");
                if (entry.FullName.EndsWith('/')) continue;
                if (!entries.TryAdd(entry.FullName, entry)) throw new CourseExportException("This export contains duplicate file names, so it was rejected.");
                if (!Readable(entry.FullName))
                {
                    // Never opened or decompressed, so neither its size nor its compression ratio can exhaust anything.
                    if (entry.FullName.StartsWith("web_resources/", StringComparison.Ordinal)) { mediaFiles++; mediaBytes += entry.Length; }
                    continue;
                }
                expanded += entry.Length;
                if (expanded > MaxExpandedBytes) throw new CourseExportException("This export's pages, assignments and settings expand to more than 250 MB, which is beyond TeachWell's import limit.");
                if (entry.Length > 1_000_000 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                    throw new CourseExportException("This export contains a file with an implausible compression ratio, so it was rejected.");
            }
            return Parse(entries, mediaFiles, mediaBytes);
        }
    }

    // The only entries TeachWell ever reads: the manifest, settings and assignment XML, and page and assignment HTML.
    private static bool Readable(string name) => !name.StartsWith("web_resources/", StringComparison.Ordinal) &&
        (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".qti", StringComparison.OrdinalIgnoreCase)
         || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase));

    private static CanvasCourseData Parse(Dictionary<string, ZipArchiveEntry> entries, int mediaFiles, long mediaBytes)
    {
        var manifest = Xml(entries, "imsmanifest.xml") ?? throw new CourseExportException("This file has no imsmanifest.xml, so it is not a Canvas course export.");
        var settings = Xml(entries, "course_settings/course_settings.xml");
        var courseNode = settings?.Root is { Name.LocalName: "course" } root ? root : null;
        var courseId = Clip(Attr(courseNode, "identifier") ?? Attr(manifest.Root, "identifier"), 80);
        if (string.IsNullOrEmpty(courseId)) throw new CourseExportException("This export does not identify its course, so it cannot be imported or matched to an earlier import.");
        var title = Clip(Value(courseNode, "title") ?? manifest.Root?.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "string")?.Value, 240);
        var option = new CanvasCourseOption(courseId, string.IsNullOrWhiteSpace(title) ? "Imported course" : title!, Clip(Value(courseNode, "course_code"), 80) ?? "");

        var resources = manifest.Descendants().Where(e => e.Name.LocalName == "resource").Select(r => new Resource(Attr(r, "identifier") ?? "", Attr(r, "type") ?? "", Attr(r, "href"),
            r.Elements().Where(f => f.Name.LocalName == "file").Select(f => Attr(f, "href")).OfType<string>().ToList(),
            r.Elements().Where(d => d.Name.LocalName == "dependency").Select(d => Attr(d, "identifierref")).OfType<string>().ToList())).Where(r => r.Id.Length is > 0 and <= 80).ToList();
        static string Link(string suffix) => suffix;

        var elements = new List<CanvasElementData>(); var warnings = new List<string>();

        // The Canvas syllabus. Canvas exports it as a standalone file rather than as a manifest resource, so it is
        // read by path. An export from a course with no syllabus simply has no such file.
        if (Html(entries, "course_settings/syllabus.html") is { } syllabusHtml && !string.IsNullOrWhiteSpace(CanvasText.FromHtml(syllabusHtml)))
        {
            var (syllabusText, syllabusEvidence) = CanvasText.Normalize(syllabusHtml, null);
            elements.Add(new(ReviewVocabulary.SyllabusSourceId, ReviewVocabulary.SyllabusType, "Syllabus", syllabusText,
                Link("assignments/syllabus"), "Published", Evidence: syllabusEvidence, PreviewHtml: CanvasText.Preview(syllabusHtml, null)));
        }

        var pages = new HashSet<string>(StringComparer.Ordinal); var assignments = new HashSet<string>(StringComparer.Ordinal);
        var newQuizzes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in resources.Where(r => r.Href is { } h && h.StartsWith("wiki_content/", StringComparison.Ordinal) && h.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
        {
            if (!pages.Add(r.Id)) continue;
            var slug = Path.GetFileNameWithoutExtension(r.Href!);
            var url = Link("pages/" + Uri.EscapeDataString(slug));
            var html = Html(entries, r.Href!);
            if (html is null)
            {
                const string warning = "A page was missing from the export or exceeded the 60,000-character limit; it was not reviewed.";
                warnings.Add(warning); elements.Add(new(r.Id, "page", Clip(slug, 240)!, "", url, "Unavailable", Warning: warning));
                continue;
            }
            var (pageTitle, state) = HeadMetadata(html);
            var (text, evidence) = CanvasText.Normalize(html, null);
            elements.Add(new(r.Id, "page", Clip(pageTitle, 240) ?? slug, text, url, state == "unpublished" ? "Unpublished" : "Published", Evidence: evidence, PreviewHtml: CanvasText.Preview(html)));
        }
        foreach (var r in resources)
        {
            var settingsFile = r.Files.FirstOrDefault(f => f.EndsWith("/assignment_settings.xml", StringComparison.Ordinal));
            if (settingsFile is null || !assignments.Add(r.Id)) continue;
            var a = Xml(entries, settingsFile)?.Root;
            if (a is null) continue;
            var descriptionFile = r.Href is { } href && href.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? href : r.Files.FirstOrDefault(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
            var html = descriptionFile is null ? "" : Html(entries, descriptionFile) ?? "";
            var metadata = new AssignmentEvidence(!string.IsNullOrWhiteSpace(Value(a, "rubric_identifierref") ?? Value(a, "rubric_external_identifier")),
                decimal.TryParse(Value(a, "points_possible"), NumberStyles.Float, CultureInfo.InvariantCulture, out var points) ? points : null,
                !string.IsNullOrWhiteSpace(Value(a, "due_at")),
                // Canvas exports write due_at in UTC.
                DateTimeOffset.TryParse(Value(a, "due_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dueAt) ? dueAt.UtcDateTime : null);
            var toolUrl = Value(a, "external_tool_url");
            var externalTool = Value(a, "submission_types")?.Contains("external_tool", StringComparison.Ordinal) == true;
            var newQuiz = externalTool && CanvasActivityContent.IsNewQuiz(toolUrl);
            if (newQuiz)
            {
                newQuizzes.Add(r.Id);
                html = CanvasActivityContent.Quiz("New Quiz", html, null, metadata.PointsPossible, null, null, CanvasActivityContent.NewQuizNote) ?? html;
            }
            else if (externalTool) html = $"<p>{System.Net.WebUtility.HtmlEncode(CanvasActivityContent.ExternalToolNote(toolUrl))}</p>{html}";
            var (text, evidence) = CanvasText.Normalize(html, null, metadata);
            elements.Add(new(r.Id, newQuiz ? ReviewVocabulary.QuizType : "assignment", Clip(Value(a, "title"), 240) ?? "Untitled assignment", text, Link(newQuiz ? "quizzes" : "assignments"),
                Value(a, "workflow_state") == "unpublished" ? "Unpublished" : "Published", Evidence: evidence, PreviewHtml: CanvasText.Preview(html)));
        }

        // Discussion topics: the prompt in the IMS discussion file, Canvas settings (type, state, grading) in its topicMeta dependency.
        // Announcements are messages rather than course content, so they are counted but not imported.
        var discussions = new HashSet<string>(StringComparer.Ordinal);
        var announcements = 0;
        foreach (var r in resources.Where(r => r.Type.Contains("imsdt", StringComparison.OrdinalIgnoreCase)))
        {
            var topicFile = r.Files.FirstOrDefault(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            var topic = topicFile is null ? null : Xml(entries, topicFile)?.Root;
            if (topic is null) continue;
            var meta = r.Dependencies.Select(d => Xml(entries, d + ".xml")?.Root).FirstOrDefault(m => m?.Name.LocalName == "topicMeta");
            if (Value(meta, "type") == "announcement") { announcements++; continue; }
            if (!discussions.Add(r.Id)) continue;
            var graded = meta?.Elements().FirstOrDefault(e => e.Name.LocalName == "assignment");
            var points = decimal.TryParse(Value(graded, "points_possible"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : (decimal?)null;
            var dueText = Value(graded, "due_at");
            var metadata = graded is null ? null : new AssignmentEvidence(!string.IsNullOrWhiteSpace(Value(graded, "rubric_identifierref")), points, !string.IsNullOrWhiteSpace(dueText),
                DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var due) ? due.UtcDateTime : null);
            var message = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "text")?.Value;
            var html = CanvasActivityContent.Discussion(message, points, graded is not null);
            var topicTitle = Clip(Value(topic, "title"), 240) ?? "Untitled discussion";
            var state = Value(meta, "workflow_state") is "unpublished" or "post_delayed" ? "Unpublished" : "Published";
            if (html is null)
            {
                const string warning = "A discussion prompt exceeded the 60,000-character limit, so its content was not imported.";
                warnings.Add(warning);
                elements.Add(new(r.Id, ReviewVocabulary.DiscussionType, topicTitle, "", Link("discussion_topics"), "Unavailable", Warning: warning));
                continue;
            }
            var (text, evidence) = CanvasText.Normalize(html, null, metadata);
            elements.Add(new(r.Id, ReviewVocabulary.DiscussionType, topicTitle, text, Link("discussion_topics"), state, Evidence: evidence, PreviewHtml: CanvasText.Preview(html)));
        }

        // Classic quizzes (including surveys): settings in {id}/assessment_meta.xml, questions in Canvas's QTI file.
        var quizzes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var metaFile in entries.Keys.Where(k => k.EndsWith("/assessment_meta.xml", StringComparison.Ordinal) && k.Count(c => c == '/') == 1).Order(StringComparer.Ordinal))
        {
            var quiz = Xml(entries, metaFile)?.Root;
            var id = Clip(Attr(quiz, "identifier") ?? metaFile[..metaFile.IndexOf('/')], 80);
            if (quiz is null || string.IsNullOrEmpty(id) || !quizzes.Add(id)) continue;
            var (html, _) = QuizHtml(entries, id, quiz);
            var assignment = quiz.Elements().FirstOrDefault(e => e.Name.LocalName == "assignment");
            var dueText = Value(quiz, "due_at") ?? Value(assignment, "due_at");
            var points = Value(quiz, "points_possible") ?? Value(assignment, "points_possible");
            var metadata = new AssignmentEvidence(null,
                decimal.TryParse(points, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null,
                !string.IsNullOrWhiteSpace(dueText),
                DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var due) ? due.UtcDateTime : null);
            var published = Value(quiz, "available") == "true" || Value(assignment, "workflow_state") == "published";
            if (html is null)
            {
                const string warning = "A quiz's questions exceeded the 60,000-character limit, so its content was not imported.";
                warnings.Add(warning);
                elements.Add(new(id, ReviewVocabulary.QuizType, Clip(Value(quiz, "title"), 240) ?? "Untitled quiz", "", Link("quizzes"), "Unavailable", Warning: warning));
                continue;
            }
            var (text, evidence) = CanvasText.Normalize(html, null, metadata);
            elements.Add(new(id, ReviewVocabulary.QuizType, Clip(Value(quiz, "title"), 240) ?? "Untitled quiz", text, Link("quizzes"),
                published ? "Published" : "Unpublished", Evidence: evidence, PreviewHtml: CanvasText.Preview(html)));
        }

        var modules = Xml(entries, "course_settings/module_meta.xml");
        if (modules is null) warnings.Add("The export has no module structure (course_settings/module_meta.xml), so pages and assignments are shown without modules.");
        foreach (var m in modules?.Root?.Elements().Where(e => e.Name.LocalName == "module") ?? [])
        {
            var moduleId = Clip(Attr(m, "identifier"), 80);
            if (string.IsNullOrEmpty(moduleId) || elements.Any(e => e.Type == "module" && e.Id == moduleId)) continue;
            elements.Add(new(moduleId, "module", Clip(Value(m, "title"), 240) ?? "Untitled module", "", Link("modules"),
                Value(m, "workflow_state") == "unpublished" ? "Unpublished" : "Published", Position(m)));
            foreach (var item in m.Elements().FirstOrDefault(e => e.Name.LocalName == "items")?.Elements().Where(e => e.Name.LocalName == "item") ?? [])
            {
                var itemId = Clip(Attr(item, "identifier"), 80);
                if (string.IsNullOrEmpty(itemId) || elements.Any(e => e.Type == "moduleItem" && e.Id == itemId)) continue;
                var type = Value(item, "content_type"); var target = Value(item, "identifierref");
                var (targetType, targetId) = type switch
                {
                    "WikiPage" when target is not null && pages.Contains(target) => ("page", target),
                    "Assignment" when target is not null && newQuizzes.Contains(target) => (ReviewVocabulary.QuizType, target),
                    "Assignment" when target is not null && assignments.Contains(target) => ("assignment", target),
                    "Quizzes::Quiz" when target is not null && quizzes.Contains(target) => (ReviewVocabulary.QuizType, target),
                    "DiscussionTopic" when target is not null && discussions.Contains(target) => (ReviewVocabulary.DiscussionType, target),
                    _ => ((string?)null, (string?)null)
                };
                var header = type == "ContextModuleSubHeader";
                var warning = targetId is not null || header ? null : type switch
                {
                    "DiscussionTopic" => "This module item is a discussion that is not in the export, so it was not imported.",
                    "Attachment" => "This module item is a course file; files and media are listed but not imported.",
                    "ExternalUrl" => "This module item is an external link; external links are not imported.",
                    "ContextExternalTool" => "This module item opens an external tool (LTI), such as New Quizzes or a publisher tool; external tools are not imported.",
                    "Quizzes::Quiz" => "This module item is a quiz whose settings are not in the export, so it was not imported.",
                    "WikiPage" or "Assignment" => "This module item's page or assignment is not in the export.",
                    _ => "This module item is a type TeachWell does not import."
                };
                if (warning is not null) warnings.Add(warning);
                var status = header ? ReviewVocabulary.TextHeaderStatus : warning is null ? (Value(item, "workflow_state") == "unpublished" ? "Unpublished" : "Published") : "Unavailable";
                elements.Add(new(itemId, "moduleItem", Clip(Value(item, "title"), 240) ?? "Untitled item", "", Link("modules"),
                    status, Position(item), moduleId, targetType, targetId, warning));
            }
        }

        // Quiz resources whose settings were not found (imported quizzes are not "skipped"); question banks are not counted.
        var missingQuizzes = resources.Count(r => r.Type.EndsWith("/assessment", StringComparison.OrdinalIgnoreCase) && !quizzes.Contains(r.Id));
        var missingDiscussions = resources.Count(r => r.Type.Contains("imsdt", StringComparison.OrdinalIgnoreCase)) - discussions.Count - announcements;
        var skipped = new[] { (missingQuizzes, "quiz", "quizzes"), (missingDiscussions, "discussion", "discussions"), (announcements, "announcement", "announcements") }
            .Where(x => x.Item1 > 0).Select(x => $"{x.Item1} {(x.Item1 == 1 ? x.Item2 : x.Item3)}").ToList();
        if (mediaFiles > 0) skipped.Add($"{mediaFiles:N0} course {(mediaFiles == 1 ? "file" : "files")} ({Size(mediaBytes)} of documents and media)");
        if (skipped.Count > 0) warnings.Add($"The export also contains {string.Join(", ", skipped)}, which are not imported or reviewed.");
        if (elements.Count > CourseDataApplier.ElementLimit) throw new CourseExportException($"This export has more than {CourseDataApplier.ElementLimit:N0} pages, assignments, modules and module items, which is beyond TeachWell's import limit.");
        return new(option, elements, warnings.Distinct().ToList());
    }

    public static CanvasCourseData ResolveLinks(CanvasCourseData data, Uri? courseUrl)
    {
        var root = courseUrl?.AbsoluteUri.TrimEnd('/');
        var elements = data.Elements.Select(e => e with { SourceUrl = root is null || e.SourceUrl is null ? null : root + "/" + e.SourceUrl }).ToList();
        var note = root is null
            ? "Imported from an uploaded course export. No Canvas course address was given, so items do not link to Canvas."
            : "Imported from an uploaded course export. Pages link to themselves in Canvas; assignments and modules link to the course's assignment and module lists, because exports do not include Canvas item numbers.";
        return data with { Elements = elements, Warnings = [.. data.Warnings, note] };
    }

    private sealed record Resource(string Id, string Type, string? Href, List<string> Files, List<string> Dependencies);

    private static bool SafeName(string name) =>
        name.Length is > 0 and <= 400 && !name.StartsWith('/') && !name.Contains('\\') && !name.Contains(':') && !name.Any(char.IsControl)
        && name.TrimEnd('/').Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");

    // Reads at most limit bytes regardless of the size the archive declares, so a forged header cannot cause unbounded reads.
    private static byte[]? Bytes(Dictionary<string, ZipArchiveEntry> entries, string name, long limit)
    {
        if (!entries.TryGetValue(name, out var entry) || entry.Length > limit) return null;
        using var stream = entry.Open();
        var buffer = new MemoryStream();
        var chunk = new byte[81920]; int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit) return null;
        }
        return buffer.ToArray();
    }

    private static XDocument? Xml(Dictionary<string, ZipArchiveEntry> entries, string name)
    {
        if (!entries.ContainsKey(name)) return null;
        var bytes = Bytes(entries, name, MaxXmlBytes) ?? throw new CourseExportException($"{name} in this export is larger than TeachWell's limit.");
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), XmlSettings);
            return XDocument.Load(reader);
        }
        catch (XmlException) { throw new CourseExportException($"{name} in this export is not valid XML, or uses a document type declaration, which TeachWell does not accept."); }
    }

    private static string? Html(Dictionary<string, ZipArchiveEntry> entries, string name)
    {
        var bytes = Bytes(entries, name, MaxHtmlChars * 4L);
        if (bytes is null) return null;
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > MaxHtmlChars ? null : text;
    }

    // A classic quiz's content: settings summary, instructions and question text (see CanvasActivityContent).
    private static (string? Html, int Questions) QuizHtml(Dictionary<string, ZipArchiveEntry> entries, string id, XElement quiz)
    {
        var qti = Xml(entries, $"non_cc_assessments/{id}.xml.qti") ?? Xml(entries, $"{id}/assessment_qti.xml");
        var items = qti?.Descendants().Where(e => e.Name.LocalName == "item").ToList() ?? [];
        string? Field(XElement item, string label) => item.Descendants().FirstOrDefault(f => f.Name.LocalName == "qtimetadatafield"
            && f.Elements().Any(l => l.Name.LocalName == "fieldlabel" && l.Value == label))?.Elements().FirstOrDefault(e => e.Name.LocalName == "fieldentry")?.Value;
        var questions = items.Select(item => new CanvasActivityContent.Question(
            item.Descendants().FirstOrDefault(e => e.Name.LocalName == "mattext")?.Value ?? "", CanvasActivityContent.QuestionType(Field(item, "question_type")))).ToList();
        var html = CanvasActivityContent.Quiz(CanvasActivityContent.QuizKind(Value(quiz, "quiz_type")), Value(quiz, "description"), questions,
            decimal.TryParse(Value(quiz, "points_possible"), NumberStyles.Float, CultureInfo.InvariantCulture, out var points) ? points : null,
            Value(quiz, "allowed_attempts"), Value(quiz, "time_limit"));
        return (html, questions.Count);
    }

    private static (string? Title, string? State) HeadMetadata(string html)
    {
        using var document = new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(html);
        var state = document.QuerySelector("meta[name='workflow_state']")?.GetAttribute("content");
        var title = document.Title;
        return (string.IsNullOrWhiteSpace(title) ? null : title.Trim(), state?.Trim().ToLowerInvariant());
    }
    private static string? Attr(XElement? e, string name) => e?.Attribute(name)?.Value.Trim() is { Length: > 0 } v ? v : null;
    private static string? Value(XElement? e, string name) => e?.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } v ? v : null;
    private static int Position(XElement e) => int.TryParse(Value(e, "position"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? Math.Clamp(p, 0, 100000) : 0;
    private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.#} MB" : $"{Math.Max(1, bytes / 1024):N0} KB";
    private static string? Clip(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
