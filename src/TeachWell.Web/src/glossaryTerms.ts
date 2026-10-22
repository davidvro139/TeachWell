/**
 * Built-in glossary (ADR 015). Plain-language definitions of teaching, course-design and accessibility terms an instructor may not know,
 * with links to free, reputable resources. Every link was checked when added; they open in a new tab and are never fetched by TeachWell.
 *
 * - `phrases` are matched case-insensitively, as whole words, in guidance text (see Glossed). Longer phrases win over shorter ones.
 * - `tags` are knowledge-base tags that list a CTL article under the term. The term's id and name always count as tags too.
 */
export interface GlossaryLink {
  label: string;
  source: string;
  url: string;
}
export interface GlossaryTerm {
  id: string;
  term: string;
  definition: string;
  phrases: string[];
  tags?: string[];
  links: GlossaryLink[];
  /** A TeachWell concept rather than a general one: no outside reading, but the CTL can still link resources. */
  teachWell?: boolean;
  /** Support ticket category for "Ask the CTL about this"; course design when absent. */
  category?: "Accessibility" | "Multimedia";
}

const cmu = "Eberly Center, Carnegie Mellon University";
const w3c = "W3C Web Accessibility Initiative";

export const GLOSSARY: GlossaryTerm[] = [
  {
    id: "blooms-taxonomy",
    term: "Bloom's taxonomy",
    definition:
      "A framework that sorts what students can do with knowledge into six levels: remember, understand, apply, analyze, evaluate and create. Instructors use it to pick verbs that make an outcome measurable, and to check that a course asks students to do more than recall. TeachWell uses the 2001 revised version.",
    phrases: ["Bloom's revised taxonomy", "revised Bloom's taxonomy", "Bloom's taxonomy", "Bloom"],
    tags: ["blooms", "bloom"],
    links: [
      { label: "Bloom's Taxonomy", source: cmu, url: "https://www.cmu.edu/teaching/designteach/design/bloomsTaxonomy.html" },
      { label: "Bloom's Taxonomy", source: "CELT, Iowa State University", url: "https://celt.iastate.edu/prepare-and-teach/design-your-course/blooms-taxonomy/" },
      {
        label: "Bloom's Taxonomy of Educational Objectives",
        source: "CATE, University of Illinois Chicago",
        url: "https://teaching.uic.edu/cate-teaching-guides/syllabus-course-design/blooms-taxonomy-of-educational-objectives/",
      },
    ],
  },
  {
    id: "learning-outcome",
    term: "Learning outcome",
    definition:
      "A statement of what students will be able to do by the end of a course or module, such as \"compare two research designs\". Good outcomes use an observable verb, so you and your students can tell whether they have been met. Also called a learning objective.",
    phrases: ["learning outcomes", "learning outcome", "learning objectives", "learning objective", "course outcomes"],
    tags: ["outcomes", "objectives"],
    links: [{ label: "Learning Objectives", source: cmu, url: "https://www.cmu.edu/teaching/designteach/design/learningobjectives.html" }],
  },
  {
    id: "observable-verb",
    term: "Observable (measurable) verb",
    definition:
      "A verb that describes something students do that you can see or assess, such as list, calculate, design or critique. Verbs like \"understand\", \"know\" or \"appreciate\" describe a state of mind, so an outcome built on them cannot be assessed directly.",
    phrases: ["observable verbs", "observable verb", "measurable verbs", "measurable verb", "measurable outcome verbs"],
    tags: ["outcomes"],
    links: [
      { label: "Learning Objectives", source: cmu, url: "https://www.cmu.edu/teaching/designteach/design/learningobjectives.html" },
      { label: "Bloom's Taxonomy (verb lists)", source: "CELT, Iowa State University", url: "https://celt.iastate.edu/prepare-and-teach/design-your-course/blooms-taxonomy/" },
    ],
  },
  {
    id: "competency",
    term: "Competency",
    definition:
      "In TeachWell, a skill or outcome the course commits to, recorded so that assessments can be mapped to it. Mapping shows which competencies have evidence and which assessments have a clear purpose. Many colleges use \"competency\" and \"learning outcome\" interchangeably.",
    phrases: ["competencies", "competency"],
    tags: ["competencies", "outcomes"],
    links: [
      { label: "Learning Objectives", source: cmu, url: "https://www.cmu.edu/teaching/designteach/design/learningobjectives.html" },
      { label: "Competency-based learning", source: "Wikipedia", url: "https://en.wikipedia.org/wiki/Competency-based_learning" },
    ],
  },
  {
    id: "constructive-alignment",
    term: "Alignment (constructive alignment)",
    definition:
      "Designing a course so that its outcomes, learning activities and assessments all point at the same skills. If an outcome asks students to evaluate, the activities should practise evaluating and an assessment should ask them to evaluate.",
    phrases: ["constructive alignment", "alignment"],
    tags: ["alignment"],
    links: [
      { label: "Align Assessments, Objectives, Instructional Strategies", source: cmu, url: "https://www.cmu.edu/teaching/assessment/basics/alignment.html" },
      { label: "Constructive alignment", source: "Wikipedia", url: "https://en.wikipedia.org/wiki/Constructive_alignment" },
    ],
  },
  {
    id: "competency-coverage",
    term: "Competency coverage",
    definition:
      "The share of the course's assessments that are mapped to at least one competency. It shows how much of the grading you can connect to what the course promises; it is not a quality score.",
    phrases: ["competency coverage"],
    tags: ["alignment"],
    teachWell: true,
    links: [],
  },
  {
    id: "orphan-competency",
    term: "Orphan competency",
    definition:
      "A competency with no assessment mapped to it, so nothing in the course yet checks whether students achieve it. Either map an assessment to it, add one, or reconsider whether it belongs in the course.",
    phrases: ["orphan competencies", "orphan competency"],
    tags: ["alignment"],
    teachWell: true,
    links: [{ label: "Align Assessments, Objectives, Instructional Strategies", source: cmu, url: "https://www.cmu.edu/teaching/assessment/basics/alignment.html" }],
  },
  {
    id: "unmapped-assessment",
    term: "Unmapped assessment",
    definition:
      "An assignment, quiz or graded discussion that is not mapped to any competency. It may still be valuable, but students and reviewers cannot see which outcome it measures.",
    phrases: ["unmapped assessments", "unmapped assessment"],
    tags: ["alignment"],
    teachWell: true,
    links: [{ label: "Align Assessments, Objectives, Instructional Strategies", source: cmu, url: "https://www.cmu.edu/teaching/assessment/basics/alignment.html" }],
  },
  {
    id: "formative-assessment",
    term: "Formative assessment",
    definition:
      "Low- or no-stakes checks during learning, such as practice quizzes, drafts or polls, that tell students and the instructor what to work on next. The point is feedback, not the grade.",
    phrases: ["formative assessments", "formative assessment", "formative"],
    tags: ["assessment"],
    links: [
      { label: "Formative vs Summative Assessment", source: cmu, url: "https://www.cmu.edu/teaching/assessment/basics/formative-summative.html" },
      { label: "Formative assessment", source: "Wikipedia", url: "https://en.wikipedia.org/wiki/Formative_assessment" },
    ],
  },
  {
    id: "summative-assessment",
    term: "Summative assessment",
    definition: "An assessment at the end of a unit or course, such as a final exam or project, that measures what students have learned against the outcomes.",
    phrases: ["summative assessments", "summative assessment", "summative"],
    tags: ["assessment"],
    links: [{ label: "Formative vs Summative Assessment", source: cmu, url: "https://www.cmu.edu/teaching/assessment/basics/formative-summative.html" }],
  },
  {
    id: "low-stakes-practice",
    term: "Low-stakes practice",
    definition:
      "Graded or ungraded activities worth little or nothing, especially early in a course, that let students practise and get feedback before a high-stakes assessment. Recalling material in practice also strengthens memory (the testing effect).",
    phrases: ["low-stakes practice", "low-stakes"],
    tags: ["assessment", "practice"],
    links: [
      { label: "Formative vs Summative Assessment", source: cmu, url: "https://www.cmu.edu/teaching/assessment/basics/formative-summative.html" },
      { label: "Testing effect", source: "Wikipedia", url: "https://en.wikipedia.org/wiki/Testing_effect" },
    ],
  },
  {
    id: "rubric",
    term: "Rubric",
    definition:
      "A scoring guide that lists the criteria for a piece of work and describes what each level of performance looks like. Shared with students in advance, it makes expectations clear and grading more consistent.",
    phrases: ["rubrics", "rubric"],
    tags: ["rubrics"],
    links: [{ label: "Rubrics", source: cmu, url: "https://www.cmu.edu/teaching/designteach/teach/rubrics.html" }],
  },
  {
    id: "udl",
    term: "Universal Design for Learning (UDL)",
    definition:
      "A framework for designing courses that work for the widest range of learners from the start, by offering multiple means of engagement (why they learn), representation (what they learn) and action and expression (how they show it), rather than adapting a course for individuals later.",
    phrases: ["Universal Design for Learning", "UDL"],
    tags: ["udl", "universal design"],
    links: [
      { label: "UDL Guidelines", source: "CAST", url: "https://udlguidelines.cast.org/" },
      { label: "About Universal Design for Learning", source: "CAST", url: "https://www.cast.org/what-we-do/universal-design-for-learning/" },
    ],
  },
  {
    id: "wcag",
    term: "WCAG",
    definition:
      "The Web Content Accessibility Guidelines, the international standard for accessible web content, published by the W3C. Numbers like 1.1.1 refer to individual success criteria. Accessibility laws and college policies often point to WCAG 2.1 or 2.2 level AA.",
    phrases: ["WCAG 2.2", "WCAG 2.1", "WCAG"],
    tags: ["accessibility", "wcag"],
    category: "Accessibility",
    links: [
      { label: "WCAG overview", source: w3c, url: "https://www.w3.org/WAI/standards-guidelines/wcag/" },
      { label: "Introduction to web accessibility", source: w3c, url: "https://www.w3.org/WAI/fundamentals/accessibility-intro/" },
    ],
  },
  {
    id: "alt-text",
    term: "Alt text",
    definition:
      "A short text alternative for an image, read aloud by screen readers and shown if the image fails to load. It should convey the image's purpose in context, not just what it looks like. Purely decorative images are marked decorative instead.",
    phrases: ["text alternatives", "text alternative", "alternative text", "alt text", "alt attribute"],
    tags: ["alt text", "images"],
    category: "Accessibility",
    links: [
      { label: "Images tutorial", source: w3c, url: "https://www.w3.org/WAI/tutorials/images/" },
      { label: "Alternative Text", source: "WebAIM", url: "https://webaim.org/techniques/alttext/" },
    ],
  },
  {
    id: "screen-reader",
    term: "Screen reader",
    definition:
      "Software that reads the screen aloud or sends it to a braille display, used by blind and low-vision people and others. Screen-reader users often move through a page by its headings, links and lists, so that structure has to be real, not just visual.",
    phrases: ["screen readers", "screen reader", "screen-reader"],
    tags: ["screen readers"],
    category: "Accessibility",
    links: [
      { label: "Designing for Screen Reader Compatibility", source: "WebAIM", url: "https://webaim.org/techniques/screenreader/" },
      { label: "Tools and techniques people use", source: w3c, url: "https://www.w3.org/WAI/people-use-web/tools-techniques/" },
    ],
  },
  {
    id: "heading-structure",
    term: "Heading structure",
    definition:
      "Using the editor's real heading styles (Heading 2, Heading 3 and so on) in order, so a page has an outline. Bold or large text only looks like a heading; screen readers and navigation tools cannot find it.",
    phrases: ["heading levels", "heading structure"],
    tags: ["headings"],
    category: "Accessibility",
    links: [
      { label: "Semantic Structure: Regions, Headings, and Lists", source: "WebAIM", url: "https://webaim.org/techniques/semanticstructure/" },
      { label: "Understanding Headings and Labels", source: w3c, url: "https://www.w3.org/WAI/WCAG22/Understanding/headings-and-labels.html" },
    ],
  },
  {
    id: "descriptive-links",
    term: "Descriptive link text",
    definition:
      "Link text that says where the link goes, such as \"Week 3 reading: climate models (PDF)\", rather than \"click here\" or a bare web address. Many people skim a page's links out of context.",
    phrases: ["descriptive link text", "link text", "Link Purpose"],
    tags: ["links"],
    category: "Accessibility",
    links: [
      { label: "Link Text and Appearance", source: "WebAIM", url: "https://webaim.org/techniques/hypertext/link_text" },
      { label: "Understanding Link Purpose (In Context)", source: w3c, url: "https://www.w3.org/WAI/WCAG22/Understanding/link-purpose-in-context.html" },
    ],
  },
  {
    id: "table-headers",
    term: "Table header cells",
    definition:
      "Cells marked as headers for a table's rows or columns, so assistive technology can say which heading each value belongs to. Tables should hold data, not be used to lay out a page.",
    phrases: ["header cells", "table headers"],
    tags: ["tables"],
    category: "Accessibility",
    links: [{ label: "Tables tutorial", source: w3c, url: "https://www.w3.org/WAI/tutorials/tables/" }],
  },
  {
    id: "use-of-color",
    term: "Colour-only information",
    definition:
      "Information conveyed only by colour, such as \"answers in red are wrong\". People who are colour-blind, use a screen reader or print in black and white miss it. Add a word, symbol or pattern as well.",
    phrases: ["colour alone", "color alone", "Use of Color", "colour-only", "color-only"],
    tags: ["colour", "color", "contrast"],
    category: "Accessibility",
    links: [{ label: "Understanding Use of Color", source: w3c, url: "https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html" }],
  },
  {
    id: "captions",
    term: "Captions and transcripts",
    definition:
      "Captions are synchronized text of a video's speech and important sounds; a transcript is the same content as a separate document. Both help deaf and hard-of-hearing students, people in noisy or quiet places, and anyone reviewing material.",
    phrases: ["captions", "captioned", "transcripts", "transcript"],
    tags: ["video", "multimedia"],
    category: "Multimedia",
    links: [{ label: "Making Audio and Video Media Accessible", source: w3c, url: "https://www.w3.org/WAI/media/av/" }],
  },
  {
    id: "cognitive-load",
    term: "Cognitive load",
    definition:
      "The amount of mental effort working memory is handling at once. Working memory is small, so long unbroken pages, clutter and unclear instructions use up effort that students need for the learning itself. Chunking material and clear structure reduce it.",
    phrases: ["cognitive load theory", "cognitive load", "working memory"],
    tags: ["cognitive load", "design"],
    links: [
      { label: "Learning Principles", source: cmu, url: "https://www.cmu.edu/teaching/principles/learning.html" },
      { label: "Cognitive load", source: "Wikipedia", url: "https://en.wikipedia.org/wiki/Cognitive_load" },
    ],
  },
  {
    id: "reading-level",
    term: "Reading level",
    definition:
      "An estimate of how hard a text is to read, based on words per sentence and syllables per word (TeachWell uses the Flesch–Kincaid grade level). It is a rough prompt to reread and simplify, not a verdict: technical terms are sometimes necessary.",
    phrases: ["reading-level estimate", "reading level"],
    tags: ["readability", "plain language"],
    links: [{ label: "Flesch–Kincaid readability tests", source: "Wikipedia", url: "https://en.wikipedia.org/wiki/Flesch%E2%80%93Kincaid_readability_tests" }],
  },
  {
    id: "workload",
    term: "Student workload",
    definition:
      "The total time students need for a course's reading, viewing, writing and studying. Uneven weeks and deadlines bunched on one day make it hard for students, especially those working or caring for others, to plan.",
    phrases: ["workload"],
    tags: ["workload"],
    links: [{ label: "Course Workload Estimator", source: "Center for Teaching Excellence, Rice University", url: "https://cte.rice.edu/resources/workload-estimator" }],
  },
  {
    id: "scaffolding",
    term: "Scaffolding",
    definition:
      "Support that helps students do something they cannot yet do alone, such as worked examples, templates or a project split into stages, removed gradually as they gain skill.",
    phrases: ["scaffolding", "scaffolded"],
    tags: ["scaffolding"],
    links: [
      { label: "Instructional Strategies", source: cmu, url: "https://www.cmu.edu/teaching/designteach/design/instructionalstrategies/index.html" },
      { label: "Instructional scaffolding", source: "Wikipedia", url: "https://en.wikipedia.org/wiki/Instructional_scaffolding" },
    ],
  },
  {
    id: "module-overview",
    term: "Module overview",
    definition:
      "A short page or introduction at the start of each module that says what students will learn, what to do and what is due. It helps students plan their week instead of discovering the work item by item.",
    phrases: ["module overviews", "module overview"],
    tags: ["course design", "navigation"],
    links: [{ label: "Content and schedule", source: cmu, url: "https://www.cmu.edu/teaching/designteach/design/contentschedule.html" }],
  },
  {
    id: "ctl",
    term: "CTL (Center for Teaching and Learning)",
    definition:
      "The college team that helps instructors with course design, teaching and educational technology. In TeachWell, CTL reviewers answer your questions, review courses with you and endorse them. Asking them for help is always welcome.",
    phrases: ["Center for Teaching and Learning", "CTL"],
    teachWell: true,
    links: [],
  },
  {
    id: "course-review",
    term: "Course review",
    definition:
      "A CTL reviewer's look at a page, an assignment or the whole course against a rubric. Once it is published you can read what they found and what they recommend.",
    phrases: ["course reviews", "course review"],
    teachWell: true,
    links: [],
  },
  {
    id: "revision-task",
    term: "Revision task",
    definition:
      "One specific change a CTL reviewer asks for after a review, such as adding alt text to an image. You work on it in Canvas, then mark it ready, and the reviewer checks it.",
    phrases: ["revision tasks", "revision task"],
    teachWell: true,
    links: [],
  },
  {
    id: "improvement-guide",
    term: "Improvement guide",
    definition:
      "TeachWell's own checks of your latest course import, ranked by how much each fix helps students, with why it matters and how to fix it. You can use it without a reviewer, and it is not a grade.",
    phrases: ["improvement guide"],
    teachWell: true,
    links: [],
  },
  {
    id: "quick-win",
    term: "Quick win",
    definition: "A guide step that should take about 15 minutes or less and still makes a real difference (medium importance or higher). A good place to start.",
    phrases: ["quick wins", "quick win"],
    teachWell: true,
    links: [],
  },
  {
    id: "standard-set",
    term: "Standard set",
    definition:
      "The CTL's version of TeachWell's checks for a group of courses: which checks run, how important each one is, and the guidance you see. A course uses the most specific published set for its department and delivery mode.",
    phrases: ["standard sets", "standard set"],
    teachWell: true,
    links: [],
  },
  {
    id: "readiness-review",
    term: "Readiness review",
    definition:
      "A review you ask the CTL for once the improvement guide's required steps are fixed or explained. The CTL either endorses the course or asks for specific changes.",
    phrases: ["readiness reviews", "readiness review"],
    teachWell: true,
    links: [],
  },
  {
    id: "endorsement",
    term: "Endorsement",
    definition:
      "The CTL's dated record that a course met the named standards when it was reviewed. It is not accreditation or certification, and it is due for a re-check after a set time or after large changes.",
    phrases: ["endorsements", "endorsement", "endorsed"],
    teachWell: true,
    links: [],
  },
];

export const glossaryById = new Map(GLOSSARY.map((t) => [t.id, t]));

/** Lower-case, straight apostrophes, hyphens and underscores as spaces: "Bloom’s-Taxonomy" and "blooms taxonomy" compare loosely. */
export const normalizeTag = (value: string) =>
  value
    .toLowerCase()
    .replace(/[’‘]/g, "'")
    .replace(/[-_]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

/** Every tag that lists a knowledge article under this term. */
export const tagsOf = (t: GlossaryTerm) => new Set([t.id, t.term, ...(t.tags ?? [])].map(normalizeTag));

/** The college's change to one term, as stored by the API (ADR 015 amendment). */
export interface CollegeGlossaryEntry {
  termId: string;
  term: string;
  definition: string;
  phrases: string[];
  tags: string[];
  links: GlossaryLink[];
  hidden: boolean;
  updatedAt: string;
  updatedByName: string;
  revision: string;
}
/** A term as the college sees it: TeachWell's wording, the CTL's rewording, or a term the CTL added. */
export interface MergedTerm extends GlossaryTerm {
  origin: "teachwell" | "edited" | "college";
  hidden: boolean;
  entry?: CollegeGlossaryEntry;
}

export function mergeGlossary(entries: CollegeGlossaryEntry[]): MergedTerm[] {
  const byId = new Map(entries.map((e) => [e.termId, e]));
  const fromEntry = (e: CollegeGlossaryEntry) => ({
    term: e.term,
    definition: e.definition,
    phrases: e.phrases,
    tags: e.tags,
    links: e.links,
  });
  const merged: MergedTerm[] = GLOSSARY.map((t) => {
    const e = byId.get(t.id);
    if (!e) return { ...t, origin: "teachwell", hidden: false };
    // A built-in term hidden without rewording keeps TeachWell's wording for when it is shown again.
    const reworded = e.definition.length > 0;
    return { ...t, ...(reworded ? fromEntry(e) : {}), origin: reworded ? "edited" : "teachwell", hidden: e.hidden, entry: e };
  });
  for (const e of entries)
    if (!glossaryById.has(e.termId)) merged.push({ id: e.termId, ...fromEntry(e), origin: "college", hidden: e.hidden, entry: e });
  return merged;
}
