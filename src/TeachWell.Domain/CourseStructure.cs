using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeachWell.Domain;

// The structure of a whole course at capture time: module order and contents, publication state, assignment metadata,
// competency alignment, and the snapshot each item had then (so a course review can show those exact page previews).
// Version 2 (ADR 011 slice 3) adds item word counts, points and due dates, and the course's outcomes; version 3 adds each assignment's
// estimated Bloom level and which assignments each outcome maps to. Older snapshots lack them and the checks that need them report a limitation.
public sealed record CourseStructureEvidence(int Version, string Kind, IReadOnlyList<ModuleEvidence> Modules, IReadOnlyList<CourseItemEvidence> Unplaced,
    int Competencies, bool Truncated = false, IReadOnlyList<OutcomeEvidence>? Outcomes = null)
{
    public const int CurrentVersion = 4;
    public const string KindName = "CourseStructure";
    public string Serialize() => JsonSerializer.Serialize(this, ContentEvidence.Json);
    public static CourseStructureEvidence? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<CourseStructureEvidence>(json, ContentEvidence.Json) is { Kind: KindName, Modules: not null, Unplaced: not null } e ? e : null; }
        catch (JsonException) { return null; }
    }

    public IEnumerable<CourseItemEvidence> AllItems() => Modules.SelectMany(m => m.Items).Concat(Unplaced);

    public string Outline()
    {
        var text = new StringBuilder();
        static string Item(CourseItemEvidence i)
        {
            var notes = new List<string>();
            if (i.Status != "Published") notes.Add(i.Status);
            if (i.IsAssessed)
            {
                if (i.HasDueDate is false) notes.Add("no due date");
                if (i.HasRubric is false) notes.Add("no rubric");
                if (!i.Aligned) notes.Add("not aligned to a competency");
            }
            return $"  - {Label(i.Type)} · {i.Title}" + (notes.Count > 0 ? " · " + string.Join(" · ", notes) : "");
        }
        foreach (var module in Modules)
        {
            text.AppendLine($"Module · {module.Title}" + (module.Status != "Published" ? $" ({module.Status})" : ""));
            if (module.Items.Count == 0) text.AppendLine("  (empty)");
            foreach (var item in module.Items) text.AppendLine(Item(item));
        }
        if (Unplaced.Count > 0)
        {
            text.AppendLine("Not placed in any module");
            foreach (var item in Unplaced) text.AppendLine(Item(item));
        }
        text.Append($"Course competencies defined: {Competencies}");
        return text.ToString();
    }
    public static string Label(string type) => type switch { "page" => "Page", "assignment" => "Assignment", ReviewVocabulary.QuizType => "Quiz", ReviewVocabulary.DiscussionType => "Discussion", "moduleItem" => "Module item", ReviewVocabulary.SyllabusType => "Syllabus", _ => type };
}
public sealed record ModuleEvidence(Guid ElementId, string Title, string Status, int Position, IReadOnlyList<CourseItemEvidence> Items);
// ElementId is the page/assignment a module item points to, or the module item itself when its target was not imported.
// Media is the number of recognised video and audio players in the item (version 4). Null means media was not
// captured for it, which is not the same as zero: a snapshot taken before page evidence version 2 has no media at all.
public sealed record CourseItemEvidence(Guid ElementId, string Type, string Title, string Status, Guid? SnapshotId, bool? HasDueDate, bool? HasRubric, bool Aligned,
    int? Words = null, decimal? Points = null, DateTime? DueAt = null, string? Level = null, int? Media = null)
{
    // Assignments and quizzes always; discussions only when graded (graded ones carry points).
    [JsonIgnore] public bool IsAssessed => ReviewVocabulary.IsAssessment(Type, Points is not null);
}
// A course competency or outcome as stated by the instructor, and how many assignments are mapped to it.
public sealed record OutcomeEvidence(Guid Id, string Code, string Title, string Description, int MappedAssignments, IReadOnlyList<Guid>? MappedElementIds = null);
