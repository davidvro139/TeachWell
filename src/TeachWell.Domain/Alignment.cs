namespace TeachWell.Domain;

// Course-design metadata the owning instructor declares and maintains, not evidence or a scored artifact —
// unlike rubric versions and reviews, a competency and its mappings are freely editable, not immutable.
public class CourseCompetency
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CourseId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public List<CompetencyMapping> Mappings { get; set; } = [];
}
// Links a competency to the course element (typically an assignment) whose evidence demonstrates it.
public class CompetencyMapping
{
    public Guid CompetencyId { get; set; }
    public Guid ElementId { get; set; }
    public DateTime MappedAt { get; set; } = DateTime.UtcNow;
}
