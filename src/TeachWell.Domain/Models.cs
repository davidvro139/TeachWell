namespace TeachWell.Domain;

public static class Roles
{
    // AcademicAdmin (D3, ADR 014): read-only results across the college; no course content, no editing.
    public const string Instructor = "Instructor", Reviewer = "CTLReviewer", Admin = "SystemAdmin", AcademicAdmin = "AcademicAdmin";
    public static readonly string[] All = [Instructor, Reviewer, Admin, AcademicAdmin];
}
public class Course
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";
    public string SourceType { get; set; } = "Demo";
    public string SourceCourseId { get; set; } = "";
    public string? SourceOrigin { get; set; }
    // The course's own address in Canvas, when known (an uploaded export does not contain it; the uploader may supply it).
    public string? SourceWebUrl { get; set; }
    public Guid? CanvasConnectionId { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string ImportWarningsJson { get; set; } = "[]";
    // D4: maintained by the CTL and used to select the course's standard set (StandardSetSelection). Null means not set.
    public string? DeliveryMode { get; set; }
    public string? Department { get; set; }
    // A program within (or across) departments, e.g. "Web Development AAS". CTL-maintained, for filtering and aggregation only (D4).
    public string? Program { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CourseElement> Elements { get; set; } = [];
    public List<CourseAccess> Access { get; set; } = [];
}
public class CourseElement
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public Guid? ParentElementId { get; set; }
    public string SourceElementId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string PlainText { get; set; } = "";
    public Guid? TargetElementId { get; set; }
    public string? SourceUrl { get; set; }
    public string SourceStatus { get; set; } = "Published";
    public string? SourceWarning { get; set; }
    public string? EvidenceJson { get; set; }
    // Allowlisted re-serialization of the sanitized HTML (see CanvasText.Preview); never the original markup.
    public string? PreviewHtml { get; set; }
    public int Position { get; set; }
    public List<ElementSnapshot> Snapshots { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class CourseAccess
{
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public Guid GrantedBy { get; set; }
    public string Permission { get; set; } = "Review";
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid? SubjectUserId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
