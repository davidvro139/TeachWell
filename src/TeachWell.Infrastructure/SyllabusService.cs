using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;

namespace TeachWell.Infrastructure;

// Pasting the syllabus (ADR 019 amendment).
//
// There is exactly one syllabus element per course, keyed (syllabus, ReviewVocabulary.SyllabusSourceId), whether its
// text came from Canvas or from the owner. Pasting sets SuppliedAt, which makes CourseDataApplier leave the element
// alone and stop treating it as missing, so a later import cannot overwrite or remove a pasted syllabus. Clearing the
// paste hands the element back to the importer.
//
// The pasted text is treated as untrusted content, like any import: it goes through CanvasText so it is sanitized,
// measured and given evidence by the same code path, and it is snapshotted so reviews and the guide see it normally.
public sealed class SyllabusService(AppDbContext db) : ISyllabusService
{
    public const int MaxLength = 100_000;

    private IQueryable<Course> Accessible(CurrentUser user) =>
        db.Courses.Where(c => c.TenantId == user.TenantId && (c.OwnerId == user.Id || c.Access.Any(a => a.UserId == user.Id)));

    public async Task<ServiceResult<CourseSyllabusDto>> GetAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Accessible(user).AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return ServiceResult<CourseSyllabusDto>.NotFound();
        return ServiceResult<CourseSyllabusDto>.Ok(await DescribeAsync(course, user, ct));
    }

    public async Task<ServiceResult<CourseSyllabusDto>> SaveAsync(CurrentUser user, Guid courseId, SyllabusTextInput input, CancellationToken ct)
    {
        var course = await Owned(user, courseId, ct);
        if (course is null) return ServiceResult<CourseSyllabusDto>.NotFound();

        var raw = input.Text ?? "";
        if (raw.Length > MaxLength) return ServiceResult<CourseSyllabusDto>.Invalid("text", $"Paste at most {MaxLength:N0} characters.");
        // Pasted text is usually plain, but a copy from a web page or a Word document arrives as HTML. Normalize
        // handles both: plain text passes through as its own paragraphs, and markup is sanitized and measured.
        var (text, evidence) = CanvasText.Normalize(Looks(raw) ? raw : Paragraphs(raw), null);
        if (TextStats.CountWords(text) == 0) return ServiceResult<CourseSyllabusDto>.Invalid("text", "Paste the syllabus text. TeachWell found no words in what was pasted.");

        var element = await db.Elements.SingleOrDefaultAsync(e => e.CourseId == courseId && e.Type == ReviewVocabulary.SyllabusType, ct);
        if (element is null)
        {
            element = new CourseElement
            {
                Id = Guid.NewGuid(), CourseId = courseId, Type = ReviewVocabulary.SyllabusType,
                SourceElementId = ReviewVocabulary.SyllabusSourceId, Title = "Syllabus", Position = 0,
            };
            db.Elements.Add(element);
        }
        element.PlainText = text;
        element.EvidenceJson = evidence.Serialize();
        element.PreviewHtml = CanvasText.Preview(Looks(raw) ? raw : Paragraphs(raw));
        element.SourceStatus = "Published";
        // The syllabus is not in Canvas, so there is nowhere in Canvas to link to.
        element.SourceUrl = null;
        element.SourceWarning = null;
        element.SuppliedAt = DateTime.UtcNow;
        element.SuppliedBy = user.Id;
        Capture(element);

        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = element.Id, Action = "SyllabusPasted" });
        course.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ServiceResult<CourseSyllabusDto>.Ok(await DescribeAsync(course, user, ct));
    }

    public async Task<ServiceResult<CourseSyllabusDto>> ClearAsync(CurrentUser user, Guid courseId, CancellationToken ct)
    {
        var course = await Owned(user, courseId, ct);
        if (course is null) return ServiceResult<CourseSyllabusDto>.NotFound();
        var element = await db.Elements.SingleOrDefaultAsync(e => e.CourseId == courseId && e.Type == ReviewVocabulary.SyllabusType && e.SuppliedAt != null, ct);
        if (element is null) return ServiceResult<CourseSyllabusDto>.NotFound();

        // The element and its snapshots stay: earlier reviews and guide marks point at them, and TeachWell does not
        // rewrite history (ADR 005). Clearing provenance hands the element back to the importer, which refills it on
        // the next import if Canvas has a syllabus.
        //
        // The status becomes "Removed", the same as content an import stops returning, because that is what the guide
        // excludes. Anything weaker would leave the cleared text still being checked, and the course would keep
        // answering syllabus questions from a syllabus it no longer has.
        element.SuppliedAt = null;
        element.SuppliedBy = null;
        element.SourceStatus = "Removed";
        element.SourceWarning = "The pasted syllabus was removed. Paste it again, or re-import the course if its syllabus is in Canvas. Earlier snapshots are retained.";
        Capture(element);
        db.AuditLogs.Add(new() { ActorId = user.Id, EntityId = element.Id, Action = "SyllabusPasteCleared" });
        course.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ServiceResult<CourseSyllabusDto>.Ok(await DescribeAsync(course, user, ct));
    }

    private Task<Course?> Owned(CurrentUser user, Guid courseId, CancellationToken ct) =>
        db.Courses.SingleOrDefaultAsync(c => c.Id == courseId && c.TenantId == user.TenantId && c.OwnerId == user.Id, ct);

    private void Capture(CourseElement element)
    {
        var snapshot = ElementSnapshot.Capture(element, DateTime.UtcNow);
        if (db.Entry(element).Collection(e => e.Snapshots).IsLoaded
            && element.Snapshots.OrderByDescending(s => s.FetchedAt).FirstOrDefault()?.ContentHash == snapshot.ContentHash) return;
        db.ElementSnapshots.Add(snapshot);
    }

    private async Task<CourseSyllabusDto> DescribeAsync(Course course, CurrentUser user, CancellationToken ct)
    {
        var element = await db.Elements.AsNoTracking()
            .SingleOrDefaultAsync(e => e.CourseId == course.Id && e.Type == ReviewVocabulary.SyllabusType && e.SourceStatus != "Removed", ct);
        var owner = course.OwnerId == user.Id;
        // A "Removed" syllabus is already filtered out above, so either means the course has no syllabus to check.
        if (element is null)
            return new(course.Id, "None", 0, null, null, null, null, owner,
                "This course has no syllabus in TeachWell. If your syllabus is in Simple Syllabus or another tool outside Canvas, paste its text here so TeachWell can check it.");

        var supplied = element.SuppliedAt is not null;
        var name = supplied && element.SuppliedBy is { } by
            ? await db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.DisplayName).SingleOrDefaultAsync(ct) : null;
        return new(course.Id, supplied ? "Pasted" : "Canvas", TextStats.CountWords(element.PlainText),
            owner ? element.PlainText : null, element.SuppliedAt, name, supplied ? null : course.LastSyncedAt, owner,
            supplied
                ? "This syllabus was pasted in TeachWell, so importing the course again will not change or remove it."
                : "This syllabus came from the Canvas syllabus page. If your real syllabus lives in another tool, paste its text here instead.");
    }

    // Whether pasted text is already HTML, rather than plain text that happens to contain an angle bracket.
    private static bool Looks(string value) => value.Contains("</", StringComparison.Ordinal) || value.Contains("/>", StringComparison.Ordinal);
    // Plain text: each non-empty line becomes a paragraph, so the checks see the same line structure as an import.
    private static string Paragraphs(string value) =>
        string.Concat(value.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
            .Select(l => "<p>" + System.Net.WebUtility.HtmlEncode(l) + "</p>"));
}
