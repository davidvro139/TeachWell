namespace TeachWell.Domain;

/// <summary>
/// A college's change to the built-in glossary (ADR 015 amendment). One row per tenant and term id: it either replaces a built-in term's
/// wording, hides a built-in term, or adds a term of the college's own. Deleting the row returns a built-in term to its shipped wording.
/// </summary>
public class GlossaryEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>A built-in term's id (e.g. "blooms-taxonomy") or a new lower-case slug.</summary>
    public string TermId { get; set; } = "";
    public string Term { get; set; } = "";
    public string Definition { get; set; } = "";
    public string PhrasesJson { get; set; } = "[]";
    public string TagsJson { get; set; } = "[]";
    public string LinksJson { get; set; } = "[]";
    public bool Hidden { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid Revision { get; set; } = Guid.NewGuid();
}
