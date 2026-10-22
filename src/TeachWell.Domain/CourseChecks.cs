using System.Text.RegularExpressions;

namespace TeachWell.Domain;

// Course-structure checks: pure functions over a course-structure snapshot, run by ReviewEngine under a CheckProfile.
public static class CourseChecks
{
    public const string StartMissing = "COURSE_START_MISSING", ModuleEmpty = "MODULE_EMPTY", ModuleNaming = "MODULE_NAMING_INCONSISTENT",
        ModuleUnpublished = "MODULE_UNPUBLISHED", ContentUnplaced = "CONTENT_NOT_IN_MODULE", DueDateMissing = "ASSIGNMENT_DUE_DATE_MISSING",
        Unaligned = "ASSIGNMENT_NOT_ALIGNED", CompetenciesMissing = "COURSE_COMPETENCIES_MISSING";
    public const string StartKeywords = "keywords", NamingPrefixes = "prefixes";

    internal static readonly CourseCheck Start = new MissingStart();
    public static readonly IReadOnlyList<CourseCheck> Rules =
        [Start, new EmptyModule(), new UnpublishedModule(), new InconsistentNaming(), new UnplacedContent(), new MissingDueDate(), new MissingCompetencies(), new UnalignedAssignment(),
         .. CourseDesignChecks.Rules];
    public static readonly string[] All = [.. Rules.Select(r => r.Code)];

    public static CheckReport Run(CourseStructureEvidence? structure, CheckProfile? profile = null, TimeZoneInfo? timeZone = null) =>
        ReviewEngine.RunCourse(structure, profile, timeZone);

    private static readonly string[] DefaultPrefixes = ["module", "week", "unit", "topic", "lesson", "chapter", "part", "session"];
    // The numbered prefix a module title uses ("week", "unit", ...), or "none".
    public static string NamingPattern(string title) => NamingPattern(title, NumberedPattern(DefaultPrefixes));
    private static string NamingPattern(string title, Regex numbered) => numbered.Match(title.Trim()) is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : "none";
    private static Regex NumberedPattern(IEnumerable<string> prefixes)
    {
        var words = prefixes.Select(p => p.Trim()).Where(p => p.Length > 0).Select(Regex.Escape).ToList();
        return words.Count == 0 ? new Regex("(?!)") : new Regex($@"^({string.Join('|', words)})\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    private static string Describe(string pattern) => pattern == "none" ? "without a numbered prefix" : $"{char.ToUpperInvariant(pattern[0])}{pattern[1..]} N";

    private const int Opening = 0, Modules = 1, Naming = 2, Placement = 3, Pacing = 4, Alignment = 5;

    private sealed class MissingStart : CourseCheck
    {
        public override string Code => StartMissing; public override string Name => "No clear starting point"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Medium"; public override int Stage => Opening;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Words(StartKeywords, "Orientation keywords", "start", "welcome", "orientation", "getting started", "overview", "syllabus", "introduction", "read me", "readme")];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            if (c.HasStart) yield break;
            var first = c.FirstModule;
            yield return Hit(s, 0, null, "No clear starting point", "The first module does not identify itself, or open with, an orientation or \"start here\" item.",
                "Open the course with a short \"Start here\" module or page that explains how the course is organized and what to do first.",
                first is null ? "The course has no modules." : $"First module: \"{Clip(first.Title)}\"; first item: \"{Clip(first.Items.FirstOrDefault()?.Title ?? "(none)")}\".", first?.ElementId);
        }
    }

    private sealed class EmptyModule : CourseCheck
    {
        public override string Code => ModuleEmpty; public override string Name => "Module is empty"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Low"; public override int Stage => Modules;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) =>
            c.Structure.Modules.Select((m, i) => (m, i)).Where(x => x.m.Items.Count == 0).Select(x => Hit(s, x.i, $"{x.m.ElementId}", "Module is empty",
                $"Module \"{Clip(x.m.Title)}\" contains no items.", "Add the module's content or remove the module so students do not open an empty section.",
                $"Module \"{Clip(x.m.Title)}\": 0 items.", x.m.ElementId));
    }
    private sealed class UnpublishedModule : CourseCheck
    {
        public override string Code => ModuleUnpublished; public override string Name => "Module is unpublished"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Low"; public override int Stage => Modules;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) =>
            c.Structure.Modules.Select((m, i) => (m, i)).Where(x => x.m.Status == "Unpublished").Select(x => Hit(s, x.i, $"{x.m.ElementId}", "Module is unpublished",
                $"Module \"{Clip(x.m.Title)}\" was unpublished at capture time.", "Confirm the module is intentionally hidden; publish it when students should see it.",
                $"Module \"{Clip(x.m.Title)}\": status Unpublished.", x.m.ElementId));
    }

    private sealed class InconsistentNaming : CourseCheck
    {
        public override string Code => ModuleNaming; public override string Name => "Module name breaks the course's naming pattern"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Low"; public override int Stage => Naming;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Words(NamingPrefixes, "Numbered module prefixes", DefaultPrefixes)];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var numbered = NumberedPattern(s.Words(NamingPrefixes));
            // Naming is compared across modules after the opening one, which is often a differently named orientation module.
            var named = c.Structure.Modules.Select((m, i) => (Module: m, Index: i, Pattern: NamingPattern(m.Title, numbered))).Skip(c.HasStart ? 1 : 0).ToList();
            var patterns = named.GroupBy(n => n.Pattern).ToList();
            if (patterns.Count <= 1) yield break;
            var dominant = patterns.OrderByDescending(g => g.Count()).ThenBy(g => named.FindIndex(n => n.Pattern == g.Key)).First().Key;
            foreach (var (module, index, pattern) in named.Where(n => n.Pattern != dominant))
                yield return Hit(s, index, $"{module.ElementId}", "Module name breaks the course's naming pattern",
                    $"Most modules are named \"{Describe(dominant)}\", but \"{Clip(module.Title)}\" is named \"{Describe(pattern)}\".",
                    "Use one naming convention for modules (for example \"Week 3: Topic\") so students can predict where things are.",
                    $"Module \"{Clip(module.Title)}\"; prevailing pattern: {Describe(dominant)}.", module.ElementId);
        }
    }

    private sealed class UnplacedContent : CourseCheck
    {
        public override string Code => ContentUnplaced; public override string Name => "Content is not placed in a module"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Low"; public override int Stage => Placement;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) =>
            c.Structure.Unplaced.Select((item, i) => (item, i)).Where(x => ReviewVocabulary.IsContent(x.item.Type)).Select(x => Hit(s, x.i, $"{x.item.ElementId}",
                "Content is not placed in a module", $"{CourseStructureEvidence.Label(x.item.Type)} \"{Clip(x.item.Title)}\" is not in any module.",
                "Place the item in the module where students need it, or confirm it is intentionally reachable only by link.",
                $"{CourseStructureEvidence.Label(x.item.Type)} \"{Clip(x.item.Title)}\": no module placement.", x.item.ElementId));
    }

    private sealed class MissingDueDate : CourseCheck
    {
        public override string Code => DueDateMissing; public override string Name => "Assignment has no due date"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Medium"; public override int Stage => Pacing;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var unknown = c.Assignments.Count(a => a.HasDueDate is null);
            if (unknown > 0) c.Limit($"Due-date metadata was not captured for {unknown} assignment(s), so they were not checked for due dates.");
            return c.Assignments.Select((a, i) => (a, i)).Where(x => x.a.HasDueDate is false).Select(x => Hit(s, x.i, $"{x.a.ElementId}", $"{CourseStructureEvidence.Label(x.a.Type)} has no due date",
                $"{CourseStructureEvidence.Label(x.a.Type)} \"{Clip(x.a.Title)}\" has no due date at capture time.",
                "Set a due date so it appears in students' calendars and the course's pacing is visible.", $"{CourseStructureEvidence.Label(x.a.Type)} \"{Clip(x.a.Title)}\": no due date reported.", x.a.ElementId)).ToList();
        }
    }

    private sealed class MissingCompetencies : CourseCheck
    {
        public override string Code => CompetenciesMissing; public override string Name => "No course competencies are defined"; public override string Aspect => ReviewAspects.Outcomes;
        public override string DefaultSeverity => "Medium"; public override int Stage => Alignment;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            if (c.Assignments.Count > 0 && c.Structure.Competencies == 0)
                yield return Hit(s, 0, null, "No course competencies are defined", "The course has assignments but no competencies to align them to.",
                    "Define the course's competencies, then map each assignment to the competencies it provides evidence for.", $"{c.Assignments.Count} assignment(s); 0 competencies.");
        }
    }
    // Only runs when competencies exist: without any, every assignment is unaligned and MissingCompetencies reports the gap once.
    private sealed class UnalignedAssignment : CourseCheck
    {
        public override string Code => Unaligned; public override string Name => "Assignment is not aligned to a competency"; public override string Aspect => ReviewAspects.Outcomes;
        public override string DefaultSeverity => "Medium"; public override int Stage => Alignment;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) => c.Structure.Competencies == 0 ? [] :
            c.Assignments.Select((a, i) => (a, i)).Where(x => !x.a.Aligned).Select(x => Hit(s, x.i, $"{x.a.ElementId}", $"{CourseStructureEvidence.Label(x.a.Type)} is not aligned to a competency",
                $"{CourseStructureEvidence.Label(x.a.Type)} \"{Clip(x.a.Title)}\" is not mapped to any course competency.",
                "Map it to the competency it assesses, or reconsider whether it belongs in the course.", $"{CourseStructureEvidence.Label(x.a.Type)} \"{Clip(x.a.Title)}\": 0 competency mappings.", x.a.ElementId));
    }
}
