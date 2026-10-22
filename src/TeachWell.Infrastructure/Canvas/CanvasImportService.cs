using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeachWell.Application;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CanvasImportService(AppDbContext db, ICanvasClient client, ICanvasTokenProtector protector) : ICanvasImportService
{
    private Task<CanvasConnection?> Owned(CurrentUser user, Guid id, CancellationToken ct) =>
        db.CanvasConnections.SingleOrDefaultAsync(c => c.Id == id && c.TenantId == user.TenantId && c.OwnerId == user.Id, ct);

    private (string? Token, CanvasError Error) Credential(CanvasConnection c)
    {
        if (c.Status == "Disconnected") return (null, CanvasError.Disconnected);
        if (c.ExpiresAt <= DateTime.UtcNow) return (null, CanvasError.Expired);
        if (c.EncryptedToken.Length == 0) return (null, CanvasError.InvalidToken);
        try { return (protector.Unprotect(c.EncryptedToken, c.TenantId, c.OwnerId, c.Id), CanvasError.None); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return (null, CanvasError.TokenUnreadable); }
    }

    public async Task<(CanvasCoursesResult Result, bool NotFound)> ListCoursesAsync(CurrentUser user, Guid connectionId, CancellationToken ct)
    {
        var connection = await Owned(user, connectionId, ct);
        if (connection is null) return (new([]), true);
        var credential = Credential(connection);
        if (credential.Token is null) { await RecordInvalidCredential(user, connection, credential.Error, ct); return (new([], credential.Error), false); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var result = await client.ListCoursesAsync(new Uri(connection.BaseUrl), credential.Token, deadline.Token);
            await RecordInvalidCredential(user, connection, result.Error, ct);
            return (result, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (new([], CanvasError.Timeout), false); }
    }

    public async Task<CanvasImportResult> ImportAsync(CurrentUser user, Guid connectionId, string sourceCourseId, CancellationToken ct)
    {
        var connection = await Owned(user, connectionId, ct);
        if (connection is null) return new(NotFound: true);
        var credential = Credential(connection);
        if (credential.Token is null) { await RecordInvalidCredential(user, connection, credential.Error, ct); return new(Error: credential.Error); }
        // Load the existing aggregate before network IO so concurrent refreshes use the original revision.
        var course = await db.Courses.AsSplitQuery().Include(c => c.Elements).ThenInclude(e => e.Snapshots.OrderByDescending(s => s.FetchedAt).Take(1))
            .SingleOrDefaultAsync(c => c.TenantId == user.TenantId && c.OwnerId == user.Id && c.SourceOrigin == connection.BaseUrl && c.SourceCourseId == sourceCourseId, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        CanvasReadResult read;
        try { read = await client.ReadCourseAsync(new Uri(connection.BaseUrl), credential.Token, sourceCourseId, deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(Error: CanvasError.Timeout); }
        if (read.Error != CanvasError.None || read.Data is null)
        {
            await RecordInvalidCredential(user, connection, read.Error, ct);
            return new(Error: read.Error == CanvasError.None ? CanvasError.UnexpectedResponse : read.Error);
        }
        var data = read.Data;
        if (data.Course.Id != sourceCourseId || !CourseDataApplier.IsConsistent(data)) return new(Error: CanvasError.UnexpectedResponse);
        var created = course is null;
        course ??= new() { Id = Guid.NewGuid(), TenantId = user.TenantId, OwnerId = user.Id, SourceType = "Canvas", SourceOrigin = connection.BaseUrl, SourceCourseId = sourceCourseId };
        var warnings = CourseDataApplier.Apply(course, data, DateTime.UtcNow);
        course.CanvasConnectionId = connection.Id;
        // A CAS update of the connection prevents commit if it was disconnected/replaced during the read.
        connection.Revision = Guid.NewGuid();
        if (created) db.Courses.Add(course);
        db.AuditLogs.Add(new() { ActorId = user.Id, SubjectUserId = user.Id, EntityId = course.Id, Action = created ? "CanvasCourseImported" : "CanvasCourseRefreshed" });
        await db.SaveChangesAsync(ct);
        // A course that has been reviewed at course level gets its structure re-captured, so revision tasks see the change.
        await CourseStructureCapture.RefreshIfPresentAsync(db, course.Id, ct);
        return new(course.Id, data.Elements.Count, warnings);
    }

    private async Task RecordInvalidCredential(CurrentUser user, CanvasConnection connection, CanvasError error, CancellationToken ct)
    {
        if (error is not (CanvasError.InvalidToken or CanvasError.Expired) || connection.EncryptedToken.Length == 0) return;
        connection.EncryptedToken = ""; connection.Status = error == CanvasError.Expired ? "Expired" : "Invalid";
        connection.ErrorCode = error.ToString(); connection.LastCheckedAt = DateTime.UtcNow; connection.Revision = Guid.NewGuid();
        db.AuditLogs.Add(new() { ActorId = user.Id, SubjectUserId = user.Id, EntityId = connection.Id, Action = "CanvasReadCredentialRejected:" + error });
        await db.SaveChangesAsync(ct);
    }
}
