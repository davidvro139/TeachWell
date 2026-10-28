namespace TeachWell.Domain;

// TeachWell's default "why it matters / how to fix it / example" for every check, written in TeachWell's own words.
// WCAG 2.2, UDL and Bloom's taxonomy are cited as references only; no licensed rubric text is reproduced.
// The CTL replaces any of this in a standard set; published standard sets keep their own copy.
public static class DefaultGuidance
{
    public static CheckGuidance For(string code) => All.TryGetValue(code, out var g) ? g
        : throw new InvalidOperationException($"No default guidance is defined for check {code}.");

    public static readonly IReadOnlyDictionary<string, CheckGuidance> All = new Dictionary<string, CheckGuidance>
    {
        [DeterministicChecks.ItemUnavailable] = new(
            "Students who follow a link or module item to missing content hit a dead end, and often email the instructor or give up on the task. It also signals that the course may be out of date.",
            "In Canvas, open the item from its module. If it was deleted or renamed, restore it or update the module item to point at the right page or assignment. If it is no longer needed, remove the module item and any links to it.",
            "A module item \"Week 2 reading\" points to a page that was deleted last term. Replace it with the current reading page, or remove the item."),
        [DeterministicChecks.ItemUnpublished] = new(
            "Unpublished items are invisible to students. That is fine for work in progress, but an item students are told to complete that stays unpublished blocks them.",
            "In Canvas, check whether the item should be visible yet. If so, select the publish icon next to it in Modules or on the item itself. If it is intentionally hidden, you can mark this step not applicable.",
            "\"Project 1 instructions\" is referenced in the Week 1 overview but still unpublished. Publish it before Week 1 begins."),
        [DeterministicChecks.ImageAltMissing] = new(
            "Screen readers announce an image with no alt text as just \"image\" or read out its file name, so blind and low-vision students miss whatever the image shows. Search and slow connections are affected too. (WCAG 2.2, 1.1.1 Non-text Content.)",
            "In the Canvas rich content editor, select the image, open Image Options, and write a short description of what the image shows and why it is there. If it is purely decorative, tick \"Decorative image\" instead.",
            "Instead of no description: \"Bar chart: enrolment rose from 120 in 2022 to 180 in 2024.\""),
        [DeterministicChecks.ImageAltFilename] = new(
            "A file name such as IMG_2041.JPG tells a screen-reader user nothing about the image, but sounds like a description is present, so the problem is easy to overlook. (WCAG 2.2, 1.1.1.)",
            "Select the image, open Image Options, and replace the file name with a description of the image's content and purpose.",
            "✗ \"IMG_2041.JPG\"  ✓ \"Network diagram: laptop connects through a router to the web server\""),
        [DeterministicChecks.ImageAltEmpty] = new(
            "Empty alt text tells assistive technology to skip the image. That is right for decoration, but if the image carries information, some students never receive it.",
            "Decide whether the image teaches anything. If it does, add a description in Image Options. If it is purely decorative (a border, a stock photo that repeats the heading), leave it marked decorative and mark this step not applicable.",
            "A diagram of the assignment workflow is marked decorative. Give it alt text that lists the steps, or add the steps as text beside it."),
        [SyllabusChecks.RequiredElementsMissing] = new(
            "The syllabus is the page students read first and return to whenever something goes wrong — a missed deadline, a needed accommodation, an unclear grade. Anything it does not answer becomes an email to you, or a student quietly guessing. Most colleges also require specific elements in every syllabus, and accreditors ask to see them, so a gap here is both a student problem and a reporting problem.",
            "In Canvas, open the course Syllabus page and select Edit. Add the missing element, using your college's standard wording where there is one; the CTL can supply it. TeachWell searches the text for the words your CTL configured, so if the element is already covered in different words, either add one of those words or mark this step not applicable and say where it is covered.",
            "A syllabus explains grading and deadlines in detail but never mentions accommodations. Adding the college's accessibility statement, with the disability-services contact, closes the gap."),
        [MediaChecks.CaptionsMissing] = new(
            "Without captions, deaf and hard-of-hearing students cannot use the video or audio at all, and no amount of effort on their part fixes it. Captions also help students in noisy places, students watching on a phone, and anyone learning the subject's vocabulary in a second language — and they make the media searchable. (WCAG 2.2, 1.2.2 Captions (Prerecorded).)",
            "If the media is in Canvas Studio or My Media, open it there, select Captions, and either request automatic captions or upload a caption file. Always read automatic captions through and correct them: technical terms and names are usually wrong, and inaccurate captions are not accessible captions. For media hosted elsewhere, add the captions at the source, then re-check the page.",
            "A 12-minute lecture recording plays with no caption option. After requesting captions in Studio and correcting the subject terms, the CC button appears and the transcript is searchable."),
        [MediaChecks.CaptionsUnconfirmed] = new(
            "An embedded player could be a captioned video, an uncaptioned one, or something that is not video at all. TeachWell reads the page, not the hosted media, so it cannot tell which. Rather than guess, it asks you to look once, because uncaptioned media is one of the barriers students most often report. (WCAG 2.2, 1.2.2 Captions (Prerecorded).)",
            "Open the page as a student would and play the media. If there is a CC button with accurate captions, mark this step done. If the embed is not video or audio — a slide deck or an interactive tool, say — mark it not applicable and give that as the reason. If captions are missing or poor, add or correct them where the media is hosted.",
            "A YouTube lecture embedded in Week 3 turns out to have only automatic captions that mis-transcribe the subject terms. Replacing them with a corrected caption file fixes it."),
        [DeterministicChecks.HeadingEmpty] = new(
            "Screen-reader users jump between headings to skim a page. An empty heading is announced as a heading with no name, which is confusing and breaks that navigation.",
            "In the editor, find the blank line styled as a heading and either type its heading text or change it back to a normal paragraph. Use spacing, not headings, to create white space.",
            "An empty \"Heading 3\" was used to add space before a table. Change it to a normal paragraph."),
        [DeterministicChecks.HeadingSkipped] = new(
            "Headings form the page's outline. Jumping from Heading 2 to Heading 4 makes the outline look like a section is missing, which disorients screen-reader users and anyone scanning the page. (WCAG 2.2, 1.3.1 Info and Relationships.)",
            "In the Canvas editor, click in the heading, open the block menu at the left of the toolbar (it usually says \"Paragraph\") and choose the right level: main sections use Heading 2, subsections Heading 3, and so on. Choose levels by structure, not by size; if you want a different look, ask the CTL about styles rather than skipping levels.",
            "Page title → \"Heading 2: Readings\" → \"Heading 3: Required\" (not Heading 4)."),
        [DeterministicChecks.LinkEmpty] = new(
            "A link with no text is announced as just \"link\", so screen-reader and keyboard users cannot tell where it goes. This often happens with icon or image links. (WCAG 2.2, 2.4.4 Link Purpose.)",
            "In the Canvas editor, click the link and select Link Options, then type text that names where it goes in the Text box. For an image link, select the image, open Image Options and give it alt text that describes where the link goes.",
            "An arrow icon linking to the syllabus becomes a link reading \"Course syllabus\"."),
        [DeterministicChecks.LinkVague] = new(
            "Many students, not only screen-reader users, scan a page for its links. \"Click here\" or a bare web address tells them nothing out of context and makes a page harder to skim. (WCAG 2.2, 2.4.4 Link Purpose.)",
            "In the Canvas editor, click the link and select Link Options, then change the Text so the linked words say where it goes or what it does, for example \"Week 3 reading (PDF)\". Keep the sentence natural; the link text does not have to be a full sentence.",
            "✗ \"To see the rubric, click here.\"  ✓ \"Review the Project 1 rubric before you start.\""),
        [DeterministicChecks.TableHeaders] = new(
            "Without header cells, a screen reader reads a table as a stream of unrelated values, and students cannot tell which column or row a number belongs to. (WCAG 2.2, 1.3.1.)",
            "In the Canvas editor, open Table Properties and set the first row (and first column, if it labels rows) as a header. If the table is only being used for layout, replace it with headings or a list.",
            "A schedule table's first row \"Week | Topic | Due\" is set as the header row."),
        [DeterministicChecks.RubricMissing] = new(
            "A rubric tells students how their work will be judged before they start, which improves the quality of submissions and makes grading more consistent and easier to explain.",
            "In Canvas, open the assignment and select \"+ Rubric\" to attach an existing rubric or build a new one. If you assess the work another way, state the criteria in the instructions instead.",
            "A three-criterion rubric for a lab report: method (clear and repeatable), results (accurate and labelled), discussion (connects results to the question)."),
        [CourseChecks.StartMissing] = new(
            "A clear starting point answers a new student's first questions — where do I begin, how is this course organized, what is due first — and reduces early confusion and drop-off. (UDL: Engagement.)",
            "Add a first module named \"Start here\" (or similar) containing a welcome page, how the course is organized, how to get help, and the first steps. Make it the first module in Canvas.",
            "Start here → Welcome and how this course works → Syllabus → Introduce yourself discussion."),
        [CourseChecks.ModuleEmpty] = new(
            "An empty module looks like missing content. Students may wonder whether they are missing work, and the course looks unfinished.",
            "Add the module's content, or remove the module if it is no longer needed. If it is a placeholder for later, unpublish it until it is ready.",
            "An empty \"Week 9\" module left over from a previous term is deleted."),
        [CourseChecks.ModuleUnpublished] = new(
            "Students cannot see unpublished modules. That is fine for future weeks, but an unpublished module students need now blocks their progress.",
            "In Canvas Modules, publish the module when students should see it. If you release modules over time, consider setting module availability dates instead.",
            "\"Week 3\" is unpublished during Week 3. Publish it, or set it to unlock automatically on the Monday of Week 3."),
        [CourseChecks.ModuleNaming] = new(
            "Consistent module names make the course predictable: students learn the pattern once and can find any week or topic quickly.",
            "Pick one naming pattern (for example \"Week 3: Topic\") and rename modules that do not follow it. The orientation module can keep its own name.",
            "\"Week 1: Data basics\", \"Week 2: Cleaning data\", \"Week 3: Charts\" — not \"Data storytelling\" in between."),
        [CourseChecks.ContentUnplaced] = new(
            "Pages and assignments outside any module are hard to find. Students who navigate by modules may never see them.",
            "Add the item to the module where students need it. If it is intentionally reachable only through a link (for example a reference page), you can mark this step not applicable.",
            "\"Late work policy\" is added to the Start here module."),
        [CourseChecks.DueDateMissing] = new(
            "Assignments without due dates do not appear in the Canvas calendar or To Do list, so students plan less well and the course's pacing is hard to see.",
            "Open the assignment in Canvas and set a due date (and availability dates if you use them). For truly self-paced work, explain the expected timing in the instructions.",
            "\"Reflection journal\" is due Sundays at 11:59 pm, so it appears in the weekly To Do list."),
        [CourseChecks.CompetenciesMissing] = new(
            "Stated competencies or outcomes are the backbone of a course: they tell students what they will be able to do and let you check that every assessment has a purpose. (Bloom's taxonomy describes measurable outcome verbs.)",
            "In TeachWell's Alignment and quality section, add the course's competencies, then map each assignment to the competencies it provides evidence for.",
            "\"C1: Build a responsive web page using semantic HTML and CSS\" mapped to the Project 1 assignment."),
        [CourseChecks.Unaligned] = new(
            "An assignment that does not map to any competency may be busy work, or its purpose may simply not be visible to students. Alignment helps you check that each assessment earns its place.",
            "Map the assignment to the competency it assesses in Alignment and quality. If it does not assess any competency, consider revising or removing it, or tell students its purpose.",
            "\"Peer review of drafts\" is mapped to \"C3: Give and use constructive feedback on technical writing\"."),

        // ADR 011 slice 3: content and cognitive load
        [ContentChecks.PageTooLong] = new(
            "Very long pages are hard to read on screen, especially on phones, and students lose their place. Working memory is limited, so splitting material into smaller pieces helps students process it. (Cognitive load theory; UDL: Representation.)",
            "Split the page at its natural sections into several shorter pages, each with one main idea, and add them to the module in order. Move reference material students only need occasionally to a separate page or file.",
            "A 3,000-word \"Week 4 notes\" page becomes \"Week 4: Key ideas\", \"Week 4: Worked example\" and \"Week 4: Further reading\"."),
        [ContentChecks.LongNoHeadings] = new(
            "Headings let every student skim, find their place again, and see how ideas fit together. Screen-reader users jump between headings to navigate, so a long page without them has to be read top to bottom. (WCAG 2.2, 2.4.6 Headings and Labels.)",
            "In the Canvas editor, put a short title before each main section, click in it, and choose Heading 2 from the block menu at the left of the toolbar (it usually says \"Paragraph\"). Use Heading 3 for subsections. Use these heading styles rather than bold text, so the structure is real, not just visual.",
            "\"Heading 2: What you will do\" · \"Heading 2: Steps\" · \"Heading 2: How it is assessed\"."),
        [ContentChecks.ParagraphTooLong] = new(
            "A very long paragraph forces students to hold many ideas at once and is hard to scan, particularly for students with dyslexia or reading on a small screen.",
            "Break the paragraph where the idea changes. If it describes steps, turn it into a numbered list; if it lists items, use bullets.",
            "A 220-word paragraph of setup instructions becomes a numbered list of six steps."),
        [ContentChecks.ReadingLevel] = new(
            "Dense sentences and uncommon words slow every reader down and are a particular barrier for multilingual students and students with reading disabilities. The estimate counts sentence and word length, so treat it as a prompt to reread, not a verdict. (UDL: Representation.)",
            "Shorten long sentences, prefer everyday words where they mean the same thing, and define technical terms the first time you use them. Keep the discipline's vocabulary students need to learn; a glossary link helps.",
            "✗ \"Utilization of the aforementioned methodology facilitates…\"  ✓ \"Using this method helps you…\""),
        [ContentChecks.PageNearlyEmpty] = new(
            "A published page with almost nothing on it looks unfinished, and students may wonder whether content is missing or failed to load.",
            "Add the content the page is meant to hold. If it is a placeholder, unpublish it until it is ready, or remove it from the module.",
            "A published \"Week 6 readings\" page that says only \"TBA\" is unpublished until the readings are chosen."),
        [ContentChecks.InstructionsShort] = new(
            "Brief instructions often leave out what to submit, how it is judged or why it matters, so students guess and email for clarification. Clear instructions are one of the strongest predictors of good submissions.",
            "Check the instructions answer: what is the task, what do students submit (format, length), how will it be assessed (or link the rubric), when is it due, and how does it connect to the module's goals?",
            "\"Submit your reflection.\" becomes: purpose, a 300–500 word length, three guiding questions, a link to the rubric, and the due date."),
        [ContentChecks.PointsMissing] = new(
            "Without points, an assignment may not count in the gradebook as intended, and students cannot tell how much it matters relative to other work.",
            "In Canvas, edit the assignment and set Points. If it is deliberately ungraded practice, say so in the instructions and mark this step not applicable.",
            "\"Lab 2\" is set to 10 points, matching the other labs."),

        // ADR 011 slice 3: accessibility (wording)
        [ContentChecks.ListAsText] = new(
            "Lines typed with dashes or numbers look like a list but are not one, so screen readers do not announce how many items there are, and the formatting breaks when text reflows on small screens. (WCAG 2.2, 1.3.1 Info and Relationships.)",
            "In the Canvas editor, select the lines and choose the bulleted or numbered list button in the toolbar, then delete the dashes or numbers you had typed.",
            "\"- Read chapter 2\" / \"- Watch the video\" become a real bulleted list."),
        [ContentChecks.ColorOnly] = new(
            "Students who are colour-blind, use a screen reader, or print in black and white cannot follow an instruction that depends on colour alone. (WCAG 2.2, 1.4.1 Use of Color.)",
            "Keep the colour if you like, but add a second cue such as a label, bold text, a symbol or the item's name, and refer to that cue in the instruction.",
            "✗ \"Complete the items in red.\"  ✓ \"Complete the items marked Required (shown in red).\""),

        // ADR 011 slice 3: course structure, pacing and outcomes
        [CourseDesignChecks.ModuleNoOverview] = new(
            "An overview at the start of each module tells students what they will learn, what to do and what is due, so they can plan their time instead of discovering the workload item by item. (UDL: Engagement.)",
            "Add a short page at the top of each module: two or three outcomes, a checklist of activities, and the due dates. Name it consistently, for example \"Week 3 overview\".",
            "Week 3 overview → In this module you will… → To do: read, practise, submit Lab 3 by Sunday."),
        [CourseDesignChecks.ModuleTooManyItems] = new(
            "A long list of items in one module is hard to scan and makes the workload look overwhelming, even when much of it is optional.",
            "Split the module into smaller ones, or group items under text headers (for example \"Learn\", \"Practise\", \"Submit\") and remove anything students do not need.",
            "A 24-item \"Unit 2\" becomes \"Unit 2a: Concepts\" and \"Unit 2b: Project\"."),
        [CourseDesignChecks.ModuleNoAssessment] = new(
            "Practice with feedback is how students find out what they have not yet understood. A module with no task leaves them to discover gaps at the next big assessment.",
            "Add a short, low-stakes practice task or self-check. If the module deliberately feeds a later assessment, say so in its overview and mark this step not applicable.",
            "A five-question self-check quiz or a one-paragraph \"explain it back\" submission at the end of the module."),
        [CourseDesignChecks.WorkloadImbalance] = new(
            "Uneven modules lead to weeks where students are overloaded and weeks where they disengage. A predictable rhythm helps students, especially those balancing work and family, plan their time.",
            "Compare the flagged module with its neighbours. Move a reading or task to a lighter module, split a heavy module, or give it more time. The estimate only counts reading and assignments, so check the result against what you know.",
            "Week 5 (about 6 hours) is split into Week 5 and Week 6, and the Week 7 reading moves to Week 4."),
        [CourseDesignChecks.DueSameDay] = new(
            "When several deadlines fall on one day, students rush or skip work, and the quality of every submission drops. Spreading deadlines also spreads your grading.",
            "In Canvas, open each assignment, select Edit and change its Due date in the Assign section, so deadlines are spread across the week. Or combine small related tasks into one submission.",
            "Reading quiz due Wednesday, discussion post Friday, lab Sunday, instead of all three on Sunday."),
        [CourseDesignChecks.ModuleDueOutOfOrder] = new(
            "Students work through a course in module order, so they reach a later module's deadline only after the earlier one. When the dates run the other way, the first thing they see on the calendar is work the course has not prepared them for, and the module they are actually in looks optional. It is also one of the most common signs that dates were carried over from a previous term.",
            "In Canvas, open the Assignments page and sort by due date, or open the course Calendar, and compare the order of deadlines with the order of your modules. Change the due dates so they advance with the modules, or move the module to where its deadlines place it. If a long project is deliberately assigned early and due late, that is fine, and you can mark this step not applicable.",
            "Week 3's quiz is due October 5 while Week 2's is due October 12. Either Week 3's quiz belongs in Week 1, or its date should move after October 12."),
        [CourseDesignChecks.OutcomeVagueVerb] = new(
            "Words like \"understand\" or \"know\" describe something you cannot see, so students cannot tell what they will be asked to do and the outcome cannot be assessed directly. Observable verbs make outcomes measurable. (Bloom's revised taxonomy.)",
            "Ask \"how would I know they understand?\" and use that action as the verb, at the level you intend: explain or summarize (understand), apply or solve (apply), compare or debug (analyze), justify or critique (evaluate), design or build (create).",
            "✗ \"Understand responsive design.\"  ✓ \"Build a page layout that adapts to phone and desktop screens.\""),
        [CourseDesignChecks.OutcomeNotAssessed] = new(
            "An outcome no assignment assesses is a promise the course does not check. Either students are not getting practice and feedback on it, or the mapping is missing.",
            "In Alignment and quality, map the assignment that assesses this outcome. If none does, add an assessment or reconsider whether the outcome belongs in this course.",
            "\"C2: Explain how a browser renders a page\" is mapped to the Week 3 reflection."),
        [CourseDesignChecks.OutcomesLowerOrder] = new(
            "If every outcome asks students only to recall or explain, the course may not prepare them to use what they learn. Most courses benefit from at least some outcomes that ask students to apply, analyze, evaluate or create. (Bloom's revised taxonomy.)",
            "Look at what students actually do in the major assessments, and write outcomes that describe it. An introductory course can reasonably stay lower; if so, mark this step not applicable with that reason.",
            "\"List the parts of a web page\" is joined by \"Build a web page from a wireframe\"."),
        [CourseDesignChecks.EarlyPracticeMissing] = new(
            "Students learn what is expected, and you learn who is struggling, from the first piece of graded or feedback-bearing work. When that first work is already high stakes, a misunderstanding costs students a large share of their grade, and those at risk are spotted late.",
            "Add a small task in the first module or two, worth only a few points or ungraded, that practises a skill the first major assessment needs, and give feedback on it before that assessment is due.",
            "A 5-point \"draft outline\" check-in in Week 1, before the 25-point Week 3 project."),
        [CourseDesignChecks.OutcomeAssessmentMismatch] = new(
            "When an outcome promises that students will design, evaluate or build, but every assessment mapped to it only asks them to describe or explain, the course never checks the higher skill it promises. (Bloom's revised taxonomy; constructive alignment.)",
            "Revise a mapped assessment so students perform the outcome's action, map an assessment that already does, or rewrite the outcome to match what the course truly assesses. The levels are estimated from task verbs, so read the instructions before deciding.",
            "\"Design an accessible page\" is assessed by \"Explain three accessibility principles\" → change the task to \"Design and submit an accessible page, with a short rationale\"."),
    };
}
