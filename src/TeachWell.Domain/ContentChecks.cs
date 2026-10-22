using System.Text.RegularExpressions;

namespace TeachWell.Domain;

// Page and assignment checks added in ADR 011 slice 3: length, structure, reading level and wording. They read the snapshot's
// stored plain text, so they also work on content imported before they existed.
public static partial class ContentChecks
{
    public const string PageTooLong = "PAGE_TOO_LONG", LongNoHeadings = "PAGE_LONG_NO_HEADINGS", ParagraphTooLong = "PARAGRAPH_TOO_LONG",
        ReadingLevel = "READING_LEVEL_HIGH", PageNearlyEmpty = "PAGE_NEARLY_EMPTY", InstructionsShort = "ASSIGNMENT_INSTRUCTIONS_SHORT",
        PointsMissing = "ASSIGNMENT_POINTS_MISSING", ListAsText = "LIST_TYPED_AS_TEXT", ColorOnly = "COLOR_ONLY_REFERENCE";
    public const string MaxWords = "maxWords", MinWords = "minWords", MaxGrade = "maxGrade", MinLines = "minLines", Phrases = "phrases";

    public static readonly IReadOnlyList<ElementCheck> Rules =
        [new TooLong(), new NoHeadings(), new LongParagraph(), new HighReadingLevel(), new NearlyEmpty(), new ShortInstructions(), new NoPoints(), new TypedList(), new ColorReference()];

    private const int Length = 6, Paragraphs = 7, Wording = 8, Assessment = 9;
    private static string Words(int n) => n == 1 ? "1 word" : $"{n:N0} words";

    private abstract class TextCheck : ElementCheck
    {
        public override bool RequiresEvidence => false;
        public override bool RequiresText => true;
    }

    private sealed class TooLong : TextCheck
    {
        public override string Code => PageTooLong; public override string Name => "Page is very long"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Low"; public override int Stage => Length;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MaxWords, "Maximum words on a page", 1500, 300, 20000)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var words = c.Stats.Words;
            if (c.ElementType == "page" && words > s.Number(MaxWords))
                yield return Hit(s, 0, null, "Page is very long", $"This page has {Words(words)}, more than the {s.Number(MaxWords):N0} this standard allows.",
                    "Split the page into shorter pages, one per idea or task, and link them in order from the module.",
                    $"{Words(words)}; about {TextStats.ReadingMinutes(words, 200)} minutes of reading at 200 words per minute.");
        }
    }

    // Needs the heading list from structural evidence as well as the text.
    private sealed class NoHeadings : ElementCheck
    {
        public override bool RequiresText => true;
        public override string Code => LongNoHeadings; public override string Name => "Long content has no headings"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Medium"; public override int Stage => Length;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MinWords, "Words before headings are expected", 400, 100, 5000)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var words = c.Stats.Words;
            if (words >= s.Number(MinWords) && c.Evidence.Headings.Count == 0)
                yield return Hit(s, 0, null, "Long content has no headings", $"This {c.ElementType} has {Words(words)} and no headings, so it reads as one block.",
                    "Break the text into sections with descriptive headings (Heading 2, then Heading 3 for subsections).", $"{Words(words)}; 0 headings.");
        }
    }

    private sealed class LongParagraph : TextCheck
    {
        public override string Code => ParagraphTooLong; public override string Name => "Paragraph is very long"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Low"; public override int Stage => Paragraphs;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MaxWords, "Maximum words in a paragraph", 150, 50, 1000)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var lines = c.Stats.Lines;
            for (var i = 0; i < lines.Count; i++)
                if (TextStats.CountWords(lines[i]) is var n && n > s.Number(MaxWords))
                    yield return Hit(s, i, $"{i}", "Paragraph is very long", $"Paragraph {i + 1} has {Words(n)}.",
                        "Break it into shorter paragraphs of one idea each, or turn a sequence of steps into a numbered list.",
                        $"Paragraph {i + 1} begins \"{Clip(lines[i])}\" ({Words(n)}).");
        }
    }

    private sealed class HighReadingLevel : TextCheck
    {
        public override string Code => ReadingLevel; public override string Name => "Reading level is high"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Low"; public override int Stage => Paragraphs;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Number(MaxGrade, "Highest acceptable grade level", 14, 6, 20), CheckParameter.Number(MinWords, "Words needed for an estimate", 150, 50, 5000)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var stats = c.Stats;
            if (stats.Words >= s.Number(MinWords) && stats.Grade is { } grade && grade > s.Number(MaxGrade))
                yield return Hit(s, 0, null, "Reading level is high", $"This {c.ElementType} reads at about grade {grade:0.#}, above the {s.Number(MaxGrade):0.#} this standard sets.",
                    "Shorten long sentences, prefer common words, and define technical terms the first time they appear. Keep the technical vocabulary students need to learn.",
                    $"Estimated Flesch–Kincaid grade {grade:0.#} from {Words(stats.Words)} in {stats.Sentences:N0} sentences (an estimate for English text).");
        }
    }

    private sealed class NearlyEmpty : TextCheck
    {
        public override string Code => PageNearlyEmpty; public override string Name => "Published page has almost no content"; public override string Aspect => ReviewAspects.Content;
        public override string DefaultSeverity => "Medium"; public override int Stage => Length;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MinWords, "Minimum words on a published page", 30, 1, 500)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var words = c.Stats.Words;
            if (c.ElementType == "page" && c.SourceStatus == "Published" && words < s.Number(MinWords))
                yield return Hit(s, 0, null, "Published page has almost no content", $"This published page has only {Words(words)}.",
                    "Add the page's content, or unpublish or remove it if it is a placeholder.", $"{Words(words)} of text.");
        }
    }

    private sealed class ShortInstructions : TextCheck
    {
        public override string Code => InstructionsShort; public override string Name => "Assignment instructions are short"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Medium"; public override int Stage => Assessment;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MinWords, "Minimum words of instructions", 50, 5, 1000)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var words = c.Stats.Words;
            if (c.ElementType == "assignment" && words < s.Number(MinWords))
                yield return Hit(s, 0, null, "Assignment instructions are short", $"The instructions have {Words(words)}, fewer than the {s.Number(MinWords):N0} this standard expects.",
                    "Check that the instructions say what to submit, how it will be assessed, and how it connects to the module's goals.", $"{Words(words)} of instructions.");
        }
    }

    private sealed class NoPoints : ElementCheck
    {
        public override string Code => PointsMissing; public override string Name => "Assignment has no points"; public override string Aspect => ReviewAspects.Assessment;
        public override string DefaultSeverity => "Low"; public override int Stage => Assessment;
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            if (c.ElementType != "assignment") yield break;
            if (c.Evidence.Assignment is null) { c.Limit("Assignment points were not captured, so points were not checked."); yield break; }
            if ((c.Evidence.Assignment.PointsPossible ?? 0) == 0)
                yield return Hit(s, 0, null, "Assignment has no points", "Canvas reported no points for this assignment at capture time.",
                    "Set the points possible, or, if it is ungraded practice, say so in the instructions.",
                    c.Evidence.Assignment.PointsPossible is null ? "Assignment metadata: no points reported." : "Assignment metadata: 0 points.");
        }
    }

    private sealed class TypedList : TextCheck
    {
        public override string Code => ListAsText; public override string Name => "List is typed as plain text"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Low"; public override int Stage => Wording;
        public override IReadOnlyList<CheckParameter> Parameters => [CheckParameter.Number(MinLines, "Consecutive lines that count as a list", 2, 2, 10)];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var lines = c.Stats.Lines;
            for (var i = 0; i < lines.Count;)
            {
                var end = i;
                while (end < lines.Count && Marker().IsMatch(lines[end])) end++;
                if (end - i >= s.Number(MinLines))
                    yield return Hit(s, i, $"{i}", "List is typed as plain text", $"Lines {i + 1}–{end} look like a list typed with dashes, bullets or numbers rather than a real list.",
                        "Select the lines and apply the bulleted or numbered list button, so screen readers announce it as a list and count its items.",
                        $"{end - i} lines starting \"{Clip(lines[i])}\".");
                i = Math.Max(end, i + 1);
            }
        }
    }

    private sealed class ColorReference : TextCheck
    {
        public override string Code => ColorOnly; public override string Name => "Instructions rely on colour"; public override string Aspect => ReviewAspects.Accessibility;
        public override string DefaultSeverity => "Medium"; public override int Stage => Wording;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [CheckParameter.Words(Phrases, "Colour phrases", "in red", "in green", "in blue", "in orange", "in yellow", "in purple", "highlighted in", "color-coded", "colour-coded", "red text", "green text", "blue text")];
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var pattern = CourseCheckContext.KeywordPattern(s.Words(Phrases));
            var lines = c.Stats.Lines;
            for (var i = 0; i < lines.Count; i++)
                if (pattern.Match(lines[i]) is { Success: true } m)
                    yield return Hit(s, i, $"{i}", "Instructions rely on colour", $"Paragraph {i + 1} refers to something by its colour (\"{m.Value}\").",
                        "Add a second cue that does not depend on colour, such as a label, bold text or an icon with text, and mention that cue instead.",
                        $"Paragraph {i + 1}: \"{Clip(lines[i])}\".");
        }
    }

    [GeneratedRegex(@"^(?:[-*•▪◦·]|\d{1,2}[.)])\s+\S")] private static partial Regex Marker();
}
