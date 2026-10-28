namespace TeachWell.Domain;

// The syllabus's required elements (ROADMAP item 4).
//
// The syllabus is the most-read page in most courses and the one students return to when something goes wrong. Which
// elements it must contain is set by institutional policy and by accreditors, not by TeachWell, and colleges check it
// by hand today. Ally scores the syllabus for accessibility like any other content and never asks what is in it.
//
// Every element is a CTL-editable keyword list, because the policy differs by college and the wording differs by
// department. A college that does not require an element empties its list, and the element is then not looked for.
//
// This is a keyword search over the syllabus text, so it finds whether a topic is *mentioned*, never whether what is
// written is correct or current. The guidance says so, and the finding asks the instructor to confirm rather than
// asserting the policy is absent: a syllabus may well cover late work in words no keyword list anticipates.
public static class SyllabusChecks
{
    public const string RequiredElementsMissing = "SYLLABUS_REQUIRED_ELEMENT_MISSING";
    public const string Accessibility = "accessibilityKeywords", Contact = "contactKeywords",
        Grading = "gradingKeywords", LateWork = "lateWorkKeywords", AcademicIntegrity = "integrityKeywords";

    public static readonly IReadOnlyList<ElementCheck> Rules = [new RequiredElements()];

    // Each required element: the setting that holds its keywords, what to call it, and what to do about it.
    private sealed record Element(string Key, string Label, string Why, string HowToFix, string[] Keywords);

    private static readonly Element[] Elements =
    [
        new(Accessibility, "an accessibility statement",
            "Students who need an accommodation often look for this first, and a course that does not say how to ask makes them ask a stranger instead.",
            "Add your college's accessibility or disability-services statement, with how to contact that office.",
            ["accessibility", "accessible", "disability", "disabilities", "accommodation", "accommodations", "disability services", "access services"]),
        new(Contact, "how to reach the instructor",
            "When something goes wrong, a student who cannot find how to reach you either guesses or gives up.",
            "Add how students should contact you, how quickly you normally reply, and your office or student hours.",
            ["office hours", "student hours", "contact me", "contact information", "reach me", "email me", "my email", "response time", "respond within"]),
        new(Grading, "how grades are worked out",
            "Students cannot judge where to put their effort, or check their own standing, without the breakdown.",
            "Add the grading breakdown: what each category is worth, and the scale that turns points into a final grade.",
            ["grading", "grade breakdown", "grading scale", "grade scale", "weighting", "weighted", "percentage of your grade", "final grade", "points possible"]),
        new(LateWork, "the late work policy",
            "Without a stated policy, every late submission becomes a private negotiation, which is both more work for you and less fair to students who do not ask.",
            "State what happens to late work, and how students should tell you when something has gone wrong.",
            ["late work", "late submission", "late assignments", "late policy", "extension", "extensions", "deadline policy", "turned in late", "submitted late"]),
        new(AcademicIntegrity, "the academic integrity policy",
            "Students need to know what counts as their own work in this subject, including where tools and collaboration are and are not allowed.",
            "State your expectations for academic integrity, including collaboration and any use of AI tools, and link the college's policy.",
            ["academic integrity", "academic honesty", "plagiarism", "cheating", "misconduct", "own work", "citation", "cite your sources"]),
    ];

    private sealed class RequiredElements : ElementCheck
    {
        public override string Code => RequiredElementsMissing;
        public override string Name => "Syllabus does not mention a required element";
        public override string Aspect => ReviewAspects.Navigation;
        public override string DefaultSeverity => "Medium";
        public override int Stage => 10;
        public override bool RequiresEvidence => false;
        public override bool RequiresText => true;
        public override IReadOnlyList<CheckParameter> Parameters =>
            [.. Elements.Select(e => CheckParameter.Words(e.Key, $"Keywords for {e.Label}", e.Keywords))];

        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            if (c.ElementType != ReviewVocabulary.SyllabusType) yield break;
            foreach (var element in Elements)
            {
                var keywords = s.Words(element.Key);
                // An empty list means the college does not require this element, so it is not looked for.
                if (keywords.Count == 0) continue;
                if (CourseCheckContext.KeywordPattern(keywords).IsMatch(c.Text)) continue;
                yield return Hit(s, Array.IndexOf(Elements, element), element.Key,
                    $"Syllabus does not mention {element.Label}",
                    $"Nothing in the syllabus matches the words this standard looks for to find {element.Label}. {element.Why}",
                    $"{element.HowToFix} If it is already there in different words, add one of the words the CTL looks for, or mark this step not applicable and say where it is covered.",
                    $"Searched the syllabus text ({c.Stats.Words:N0} words) for: {string.Join(", ", keywords.Take(8))}{(keywords.Count > 8 ? ", …" : "")}. No match.");
            }
        }
    }
}
