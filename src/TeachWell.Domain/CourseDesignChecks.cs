namespace TeachWell.Domain;

// Course-structure checks added in ADR 011 slice 3: module design, workload and pacing, and outcome quality. They read the
// version 2 course-structure evidence (word counts, points, due dates, outcomes); version 1 snapshots report a limitation instead.
public static class CourseDesignChecks
{
    public const string ModuleNoOverview = "MODULE_NO_OVERVIEW", ModuleTooManyItems = "MODULE_TOO_MANY_ITEMS", ModuleNoAssessment = "MODULE_NO_ASSESSMENT",
        WorkloadImbalance = "WORKLOAD_IMBALANCE", DueSameDay = "ASSIGNMENTS_DUE_SAME_DAY", OutcomeVagueVerb = "OUTCOME_VAGUE_VERB",
        OutcomeNotAssessed = "OUTCOME_NOT_ASSESSED", OutcomesLowerOrder = "OUTCOMES_LOWER_ORDER_ONLY",
        EarlyPracticeMissing = "EARLY_LOW_STAKES_MISSING", OutcomeAssessmentMismatch = "OUTCOME_ASSESSMENT_LEVEL_MISMATCH",
        ModuleDueOutOfOrder = "MODULE_DUE_DATES_OUT_OF_ORDER", ModuleMediaOnly = "MODULE_MEDIA_WITHOUT_TEXT";
    public const string Keywords = "keywords", MaxItems = "maxItems", WordsPerMinute = "wordsPerMinute", MinutesPerAssignment = "minutesPerAssignment",
        Ratio = "ratio", MaxPerDay = "maxPerDay", VagueVerbs = "phrases", MinOutcomes = "minOutcomes", EarlyModules = "earlyModules", LowStakesPercent = "lowStakesPercent",
        ToleranceDays = "toleranceDays", MinWords = "minWords";

    public static readonly IReadOnlyList<CourseCheck> Rules =
        [new NoOverview(), new TooManyItems(), new NoAssessment(), new MediaOnly(), new Workload(), new SameDay(), new DueOrder(), new VagueOutcome(), new UnassessedOutcome(),
         new LowerOrderOutcomes(), new EarlyPractice(), new LevelMismatch()];

    private const int Modules = 1, Pacing = 4, Outcomes = 5;
    private const string OldStructure = "This course-structure snapshot predates {0}; start a new course review to capture the current structure.";

    // Modules after the orientation module that contain anything; orientation modules are judged by their own check.
    private static IEnumerable<(ModuleEvidence Module, int Index)> ContentModules(CourseCheckContext c) =>
        c.Structure.Modules.Select((m, i) => (m, i)).Skip(c.HasStart ? 1 : 0).Where(x => Content(x.m).Any());
    // Module items other than text headers, which label sections of a module rather than add content.
    private static IEnumerable<CourseItemEvidence> Content(ModuleEvidence m) => m.Items.Where(i => i.Status != ReviewVocabulary.TextHeaderStatus);

    private sealed class NoOverview : CourseCheck
    {
        public override string Code => ModuleNoOverview; public override string Name => "Module has no overview"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Low"; public override int Stage => Modules;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Words(Keywords, "Overview keywords", "overview", "introduction", "intro", "this week", "objectives", "outcomes", "agenda", "roadmap", "checklist", "read first", "welcome", "getting started")];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var pattern = CourseCheckContext.KeywordPattern(s.Words(Keywords));
            // The first content item counts, and so do any text headers above it (a header named "Overview" introduces the module).
            static IEnumerable<CourseItemEvidence> Opening(ModuleEvidence m) =>
                m.Items.TakeWhile(i => i.Status == ReviewVocabulary.TextHeaderStatus).Concat(Content(m).Take(1));
            foreach (var (m, i) in ContentModules(c).Where(x => !Opening(x.Module).Any(item => pattern.IsMatch(item.Title))))
                yield return Hit(s, i, $"{m.ElementId}", "Module has no overview", $"Module \"{Clip(m.Title)}\" does not open with an overview or introduction.",
                    "Start the module with a short overview page: what students will learn, what to do, and what is due.",
                    $"First item: \"{Clip(Content(m).First().Title)}\".", m.ElementId);
        }
    }

    private sealed class TooManyItems : CourseCheck
    {
        public override string Code => ModuleTooManyItems; public override string Name => "Module has too many items"; public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Low"; public override int Stage => Modules;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MaxItems, "Maximum items in a module", 15, 5, 100)];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) =>
            c.Structure.Modules.Select((m, i) => (m, i, n: Content(m).Count())).Where(x => x.n > s.Number(MaxItems)).Select(x => Hit(s, x.i, $"{x.m.ElementId}", "Module has too many items",
                $"Module \"{Clip(x.m.Title)}\" has {x.n} items, more than the {s.Number(MaxItems):0} this standard allows.",
                "Split the module into smaller ones (for example by week or topic), or group related items with text headers.",
                $"Module \"{Clip(x.m.Title)}\": {x.n} items (text headers not counted).", x.m.ElementId));
    }

    private sealed class NoAssessment : CourseCheck
    {
        public override string Code => ModuleNoAssessment; public override string Name => "Module has no assessment"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Low"; public override int Stage => Modules;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) =>
            ContentModules(c).Where(x => !x.Module.Items.Any(i => i.IsAssessed)).Select(x => Hit(s, x.Index, $"{x.Module.ElementId}", "Module has no assessment",
                $"Module \"{Clip(x.Module.Title)}\" has no assignment, quiz or graded discussion, so students get no practice or feedback on it.",
                "Add a short practice task or check for understanding, or connect the module to a later assessment and say so in its overview.",
                $"Module \"{Clip(x.Module.Title)}\": {Content(x.Module).Count()} items, 0 assessments.", x.Module.ElementId));
    }

    // A module whose content is only video or audio, with no readable equivalent (ROADMAP item 5).
    //
    // This is the defensible half of a "multiple means of representation" check, and the asymmetry is deliberate. A
    // module that is only media excludes students who cannot use video, cannot stream it, or need to search, skim and
    // revise from text — and it is the usual sign that a recorded lecture was dropped in without a transcript.
    //
    // The opposite is NOT flagged. A text-only module is ordinary good teaching in most subjects, and a check that told
    // instructors to add a video to every module would be giving bad pedagogical advice with TeachWell's authority
    // behind it. UDL asks for options where they matter, not for one of everything in every module.
    private sealed class MediaOnly : CourseCheck
    {
        public override string Code => ModuleMediaOnly; public override string Name => "Module's content is only video or audio"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Low"; public override int Stage => Modules;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Number(MinWords, "Words of text a module with media should also offer", 200, 0, 5000)];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var content = ContentModules(c).ToList();
            // Media was added to the structure evidence in version 4; an older snapshot cannot answer this.
            if (content.SelectMany(x => Content(x.Module)).Any(i => ReviewVocabulary.IsContent(i.Type) && i.Media is null))
            {
                c.Limit(string.Format(OldStructure, "the check for modules that offer only media"));
                return [];
            }
            return content.Select(x => (x.Module, x.Index,
                    Players: Content(x.Module).Sum(i => i.Media ?? 0),
                    Words: Content(x.Module).Where(i => ReviewVocabulary.IsContent(i.Type)).Sum(i => i.Words ?? 0)))
                .Where(x => x.Players > 0 && x.Words < s.Number(MinWords))
                .Select(x => Hit(s, x.Index, $"{x.Module.ElementId}", "Module's content is only video or audio",
                    $"Module \"{Clip(x.Module.Title)}\" has {x.Players} video or audio {(x.Players == 1 ? "player" : "players")} but only {x.Words:N0} words of text, fewer than the {s.Number(MinWords):0} this standard expects alongside media.",
                    "Add a readable equivalent: a transcript, a summary of the key points, or the readings the recording is based on. Students who cannot stream the media, or who are revising, then have a way in.",
                    $"Module \"{Clip(x.Module.Title)}\": {x.Players} player(s), {x.Words:N0} words of text.", x.Module.ElementId))
                .ToList();
        }
    }

    private sealed class Workload : CourseCheck
    {
        public override string Code => WorkloadImbalance; public override string Name => "Module workload is uneven"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Medium"; public override int Stage => Pacing;
        public override IReadOnlyList<CheckParameter> Parameters =>
        [
            CheckParameter.Number(WordsPerMinute, "Reading speed (words per minute)", 200, 100, 400),
            CheckParameter.Number(MinutesPerAssignment, "Estimated minutes per assignment", 60, 0, 600),
            CheckParameter.Number(Ratio, "Flag modules this many times above or below the median", 2, 1.2m, 5),
        ];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var modules = ContentModules(c).ToList();
            if (modules.SelectMany(x => x.Module.Items).Any(i => ReviewVocabulary.IsContent(i.Type) && i.Words is null))
            {
                c.Limit(string.Format(OldStructure, "workload estimates"));
                return [];
            }
            if (modules.Count < 3) return [];
            c.Limit("Workload is estimated from reading time and a fixed time per assignment; it does not see videos, quizzes, discussions or external work.");
            var estimates = modules.Select(x => (x.Module, x.Index, Minutes: x.Module.Items.Sum(i =>
                (i.Words ?? 0) / s.Number(WordsPerMinute) + (i.IsAssessed ? s.Number(MinutesPerAssignment) : 0)))).ToList();
            var sorted = estimates.Select(e => e.Minutes).Order().ToList();
            var median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            if (median <= 0) return [];
            var ratio = s.Number(Ratio);
            return estimates.Where(e => e.Minutes > median * ratio || e.Minutes * ratio < median).Select(e =>
            {
                var heavier = e.Minutes > median;
                return Hit(s, e.Index, $"{e.Module.ElementId}", heavier ? "Module is much heavier than the others" : "Module is much lighter than the others",
                    $"Module \"{Clip(e.Module.Title)}\" is estimated at about {Math.Round(e.Minutes):0} minutes of work, against a typical module's {Math.Round(median):0}.",
                    heavier ? "Move some content or an assignment to a lighter module, or give students more time for this one."
                            : "Check whether this module is missing content, or whether it could be combined with a neighbouring module.",
                    $"Estimate {Math.Round(e.Minutes):0} min vs median {Math.Round(median):0} min (reading at {s.Number(WordsPerMinute):0} wpm, {s.Number(MinutesPerAssignment):0} min per assignment).",
                    e.Module.ElementId);
            }).ToList();
        }
    }

    private sealed class SameDay : CourseCheck
    {
        public override string Code => DueSameDay; public override string Name => "Several assignments are due on one day"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Medium"; public override int Stage => Pacing;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MaxPerDay, "Maximum assignments due on one day", 2, 1, 10)];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var unknown = c.Assignments.Count(a => a.HasDueDate == true && a.DueAt is null);
            if (unknown > 0) c.Limit($"Due dates were not captured for {unknown} assignment(s) with a due date; re-import the course to check them for clustering.");
            // Grouped by calendar day in the institution's time zone, so an 11:59 pm deadline counts on the day students see.
            DateTime LocalDay(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), c.TimeZone).Date;
            return c.Assignments.Where(a => a.DueAt is not null).GroupBy(a => LocalDay(a.DueAt!.Value)).Where(g => g.Count() > s.Number(MaxPerDay)).OrderBy(g => g.Key)
                .Select(g => Hit(s, 0, g.Key.ToString("yyyy-MM-dd"), "Several assignments are due on one day",
                    $"{g.Count()} assignments are due on {g.Key:MMMM d, yyyy}.",
                    "Spread the deadlines across the week, or combine small tasks, so students can give each one proper attention.",
                    $"Due {g.Key:yyyy-MM-dd} ({c.TimeZone.Id}): {string.Join("; ", g.Select(a => $"\"{Clip(a.Title)}\""))}.", g.First().ElementId)).ToList();
        }
    }

    // Scaffolding: a course is read in module order, so its deadlines should advance in module order too. When a later
    // module's work falls due before an earlier module's, students must work ahead of where the course has taken them.
    // Only consecutive dated modules are compared, so an undated module does not break the chain's meaning.
    private sealed class DueOrder : CourseCheck
    {
        public override string Code => ModuleDueOutOfOrder; public override string Name => "Module deadlines run out of order"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Medium"; public override int Stage => Pacing;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Number(ToleranceDays, "Days a module may fall due before the previous one", 0, 0, 30)];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            // A module's own deadline is the earliest its students are asked to submit anything.
            // Min over a nullable projection is null when the module has no dated assessment, which drops it from the chain.
            var dated = c.Structure.Modules.Select((m, i) => (Module: m, Index: i, Earliest: m.Items.Where(x => x.IsAssessed).Min(x => x.DueAt)))
                .Where(x => x.Earliest is not null).ToList();
            if (dated.Count < 2) yield break;

            var tolerance = TimeSpan.FromDays((double)s.Number(ToleranceDays));
            for (var i = 1; i < dated.Count; i++)
            {
                var (module, index, earliest) = dated[i];
                var (previous, _, previousEarliest) = dated[i - 1];
                if (earliest!.Value.Add(tolerance) >= previousEarliest!.Value) continue;
                var days = (int)Math.Ceiling((previousEarliest.Value - earliest.Value).TotalDays);
                yield return Hit(s, index, $"{module.ElementId}", "Module deadlines run out of order",
                    $"Module \"{Clip(module.Title)}\" has work due {days} day(s) before \"{Clip(previous.Title)}\", which comes before it in the course.",
                    "Check the due dates against the order students work through the modules, or move the module to where its deadlines place it.",
                    $"\"{Clip(module.Title)}\" earliest due {earliest:yyyy-MM-dd}; \"{Clip(previous.Title)}\" earliest due {previousEarliest:yyyy-MM-dd}.",
                    module.ElementId);
            }
        }
    }

    private static IReadOnlyList<OutcomeEvidence>? OutcomesOf(CourseCheckContext c)
    {
        if (c.Structure.Outcomes is null) c.Limit(string.Format(OldStructure, "outcome checks"));
        return c.Structure.Outcomes;
    }
    private static string Suggestions => string.Join("; ", Bloom.Levels.Skip(1).Select(l => $"{l.ToLowerInvariant()}: {string.Join(", ", Bloom.Examples[l])}"));

    private sealed class VagueOutcome : CourseCheck
    {
        public override string Code => OutcomeVagueVerb; public override string Name => "Outcome uses a vague verb"; public override string Aspect => ReviewAspects.Outcomes;
        public override string DefaultSeverity => "Medium"; public override int Stage => Outcomes;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Words(VagueVerbs, "Vague outcome phrases", "understand", "know", "learn", "appreciate", "be aware of", "be familiar with", "become familiar with",
                "comprehend", "grasp", "realize", "gain an understanding of", "have an understanding of", "develop an understanding of", "gain knowledge of", "be exposed to")];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) => (OutcomesOf(c) ?? []).Select((o, i) => (o, i, Phrase: Bloom.VaguePhrase(o.Title, s.Words(VagueVerbs))))
            .Where(x => x.Phrase is not null).Select(x => Hit(s, x.i, $"{x.o.Id}", "Outcome uses a vague verb",
                $"{x.o.Code} starts with \"{x.Phrase}\", which describes something students cannot be observed doing.",
                $"Rewrite it around an observable action at the level you intend, for example {Suggestions}.",
                $"{x.o.Code}: \"{Clip(x.o.Title)}\"."));
    }

    private sealed class UnassessedOutcome : CourseCheck
    {
        public override string Code => OutcomeNotAssessed; public override string Name => "Outcome is not assessed"; public override string Aspect => ReviewAspects.Outcomes;
        public override string DefaultSeverity => "Medium"; public override int Stage => Outcomes;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s) => (OutcomesOf(c) ?? []).Select((o, i) => (o, i)).Where(x => x.o.MappedAssignments == 0)
            .Select(x => Hit(s, x.i, $"{x.o.Id}", "Outcome is not assessed", $"No assignment is mapped to {x.o.Code}, so the course has no evidence that students achieve it.",
                "Map an existing assignment that assesses it, add an assessment, or reconsider whether the outcome belongs in the course.",
                $"{x.o.Code}: \"{Clip(x.o.Title)}\"; 0 mapped assignments."));
    }

    private sealed class LowerOrderOutcomes : CourseCheck
    {
        public override string Code => OutcomesLowerOrder; public override string Name => "Outcomes stay at recall and comprehension"; public override string Aspect => ReviewAspects.Outcomes;
        public override string DefaultSeverity => "Low"; public override int Stage => Outcomes;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MinOutcomes, "Outcomes needed before this is judged", 2, 1, 20)];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var classified = (OutcomesOf(c) ?? []).Select(o => (o, Level: Bloom.LevelOf(o.Title))).Where(x => x.Level is not null).ToList();
            if (classified.Count >= s.Number(MinOutcomes) && classified.All(x => Bloom.IsLowerOrder(x.Level!)))
                yield return Hit(s, 0, null, "Outcomes stay at recall and comprehension",
                    "Every outcome with a recognised verb asks students to remember or understand; none asks them to apply, analyze, evaluate or create.",
                    "If the course prepares students to use what they learn, add or revise outcomes at higher levels, for example apply, analyze or design.",
                    string.Join("; ", classified.Select(x => $"{x.o.Code}: {x.Level} (\"{Bloom.FirstVerb(x.o.Title)}\")")) + ". Levels follow Bloom's revised taxonomy and are suggestions.");
        }
    }

    // Practice before high stakes: some assignment in the opening modules should carry little weight, so students get feedback
    // before their first major grade.
    private sealed class EarlyPractice : CourseCheck
    {
        public override string Code => EarlyPracticeMissing; public override string Name => "No low-stakes assessment early in the course"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Medium"; public override int Stage => Pacing;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Number(EarlyModules, "Modules that count as early", 2, 1, 6), CheckParameter.Number(LowStakesPercent, "Low stakes: at most this % of course points", 10, 1, 50)];
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            if (c.Assignments.Count == 0 || c.Structure.Modules.Count == 0) yield break;
            var unknown = c.Assignments.Count(a => a.Points is null);
            if (unknown > 0) { c.Limit($"Points were not captured for {unknown} assignment(s), so early low-stakes practice was not checked."); yield break; }
            var total = c.Assignments.Sum(a => a.Points!.Value);
            if (total <= 0) yield break;
            var earlyModules = c.Structure.Modules.Take((int)s.Number(EarlyModules)).ToList();
            var early = earlyModules.SelectMany(m => m.Items).Where(i => i.IsAssessed).DistinctBy(i => i.ElementId).ToList();
            decimal Share(CourseItemEvidence a) => Math.Round(a.Points!.Value * 100 / total, 1);
            if (early.Any(a => Share(a) <= s.Number(LowStakesPercent))) yield break;
            yield return Hit(s, 0, null, "No low-stakes assessment early in the course",
                early.Count == 0
                    ? $"The first {earlyModules.Count} module(s) contain no assignment, so students get no feedback before later, weightier work."
                    : $"Every assignment in the first {earlyModules.Count} module(s) is worth more than {s.Number(LowStakesPercent):0}% of the course's points.",
                "Add a small, low-weight task early on, such as a practice submission or short quiz, so students get feedback before their first major grade.",
                early.Count == 0
                    ? $"Early modules: {string.Join("; ", earlyModules.Select(m => $"\"{Clip(m.Title)}\""))}; {total:0.##} course points in total."
                    : string.Join("; ", early.Select(a => $"\"{Clip(a.Title)}\" {a.Points:0.##} pts ({Share(a)}%)")) + $" of {total:0.##} course points.",
                earlyModules[0].ElementId);
        }
    }

    // An outcome that asks for more than its assessments do: compares the outcome's verb level with the highest task verb level
    // found in each mapped assignment. The assignment estimate is generous, so this under- rather than over-reports.
    private sealed class LevelMismatch : CourseCheck
    {
        public override string Code => OutcomeAssessmentMismatch; public override string Name => "Assessments sit below the outcome's level"; public override string Aspect => ReviewAspects.Outcomes;
        public override string DefaultSeverity => "Low"; public override int Stage => Outcomes;
        public override IEnumerable<CheckHit> Run(CourseCheckContext c, CheckSettings s)
        {
            var outcomes = OutcomesOf(c);
            if (outcomes is null) yield break;
            if (outcomes.Any(o => o.MappedElementIds is null)) { c.Limit(string.Format(OldStructure, "outcome and assessment level comparison")); yield break; }
            var items = c.Structure.AllItems().Where(i => i.IsAssessed).DistinctBy(i => i.ElementId).ToDictionary(i => i.ElementId);
            var uncompared = 0;
            foreach (var (o, i) in outcomes.Select((o, i) => (o, i)))
            {
                if (Bloom.LevelOf(o.Title) is not { } level || Bloom.Rank(level) < Bloom.Rank(Bloom.Apply)) continue;
                var mapped = o.MappedElementIds!.Where(items.ContainsKey).Select(id => items[id]).ToList();
                if (mapped.Count == 0) continue;
                if (mapped.Any(a => a.Level is null)) { uncompared++; continue; }
                var highest = mapped.Max(a => Bloom.Rank(a.Level!));
                if (highest >= Bloom.Rank(level)) continue;
                var verb = Bloom.FirstVerb(o.Title);
                yield return Hit(s, i, $"{o.Id}", "Assessments sit below the outcome's level",
                    $"{o.Code} asks students to {verb} ({level}), but its mapped assessments ask for no more than {Bloom.Levels[highest]}.",
                    $"Revise an assessment so students actually {verb}, or map one that does; otherwise consider whether the outcome overstates what the course asks.",
                    $"{o.Code}: \"{Clip(o.Title)}\" ({level}). Mapped: {string.Join("; ", mapped.Select(a => $"\"{Clip(a.Title)}\" ({a.Level})"))}. Levels are estimated from task verbs (Bloom's revised taxonomy).");
            }
            if (uncompared > 0) c.Limit($"{uncompared} outcome(s) could not be compared with their assessments, because some instructions use no recognised task verbs.");
        }
    }

    private static string Clip(string value) => value.Length <= 80 ? value : value[..77] + "...";
}
