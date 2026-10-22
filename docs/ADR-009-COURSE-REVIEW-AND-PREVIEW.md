# ADR 009: Course-level review and visual page preview

Accepted October 16, 2026. Docker remains deferred. Supersedes ADR 005's "no markup is stored or rendered" in the narrow way described below.

## Why

Element reviews judged one page or assignment at a time, from plain text. Two things reviewers needed were impossible:

- **Problems that only exist across the course.** REVIEW_FRAMEWORK.md §1 asks for a clear starting point, consistent module naming and organization, and detection of unpublished or unplaced content. §2 asks that orphan objectives and unmapped assessments are identified. None of these belong to a single page.
- **Judging design.** Plain text cannot show heading hierarchy, emphasis, tables or where images sit, so visual consistency could not be assessed.

## Course-level review reuses the element-review pipeline

A course-level review targets one **synthetic course element** per course (`Type = "course"`, `SourceElementId = "course"`, title "Course structure"). Its `PlainText` is a readable outline, and its `EvidenceJson` holds a `CourseStructureEvidence` record: modules in captured order, each item's type, title, publication state, due-date and rubric metadata, competency alignment, and the **snapshot id each item had at capture**. Every change appends an immutable `ElementSnapshot`, deduplicated by content hash, exactly as for imported content.

This was chosen over a parallel `CourseReview` aggregate. It means course reviews automatically get the same snapshot pinning, rubric-version pinning, scoring, draft privacy, publish immutability (EF guard and PostgreSQL triggers), findings, revision tasks, comparisons and reports, with no second copy of any of that logic to keep consistent.

Consequences, each handled explicitly:

- **Created lazily** by the first `POST /courses/{id}/course-reviews` (CTL with course access), and audited as `CourseStructureElementCreated`. Courses never reviewed at course level have no such element.
- **Never shown as content.** It is excluded from `GET /courses/{id}` elements and from course element counts, and the element-review route rejects it.
- **Never marked Removed by a Canvas re-import**, since Canvas cannot return it. Re-import re-captures the structure when the element exists.
- **When structure is captured:** at review start, after a re-import, and when a course-structure revision task moves to ReadyForReview, so the reviewer compares against the course as it is now. The demo "simulated update" (a text edit) is refused for course-structure tasks.
- **Rubric criteria** may now target `course` as well as `page`/`assignment` (`ReviewVocabulary.CriterionTargets`). Rubric version summaries report which targets their criteria cover, so the UI offers only versions that fit.

### Course checks (`CourseChecks`, pure, like `DeterministicChecks`)

| Code | Fires when | Subject |
| --- | --- | --- |
| `COURSE_START_MISSING` | The first module neither is nor opens with an orientation/start/welcome/syllabus item | First module |
| `MODULE_EMPTY` | A module has no items | The module |
| `MODULE_NAMING_INCONSISTENT` | Modules after the opening one use more than one numbered-prefix pattern (`Week N`, `Module N`, `Unit N`, or none); the minority are flagged | The module |
| `MODULE_UNPUBLISHED` | A module was unpublished at capture | The module |
| `CONTENT_NOT_IN_MODULE` | A page or assignment is in no module | The item |
| `ASSIGNMENT_DUE_DATE_MISSING` | Assignment metadata reports no due date (unknown metadata is a stated limitation, not a finding) | The assignment |
| `ASSIGNMENT_NOT_ALIGNED` | An assignment has no competency mapping, when the course defines competencies | The assignment |
| `COURSE_COMPETENCIES_MISSING` | The course has assignments but no competencies (reported instead of one finding per assignment) | — |

Findings now carry an optional `SubjectElementId`. A course finding names the module or assignment it concerns and links to that item's Canvas location.

Visual consistency, predictable navigation and balanced workload remain **manual** criteria in the seeded "Course-level design (demo)" rubric. No automated check claims to judge them.

## Visual preview: an allowlisted re-serialization, not stored source HTML

`CanvasText.Preview` parses with the same scripting-disabled AngleSharp parser and removes the same dangerous nodes as `Normalize`. It then **writes new markup from scratch**. Nothing from the source document passes through except text:

- Only structural tags are emitted (headings, paragraphs, lists, emphasis, blockquote/pre/code, tables, definition lists). Any other element is unwrapped to its text.
- All text is HTML-encoded.
- No source attribute survives except numeric `colspan`/`rowspan` (2–50) and an enumerated `th scope`. Styles, classes, ids, event handlers and data attributes are all dropped.
- Links become labeled text whose tooltip names only the destination kind or external host. **No URL is stored**, preserving ADR 005's rule that full URLs (which can carry query-string secrets) are never kept.
- Images become placeholders showing the alt text, or flagging a missing, empty or file-name alt. **No image source is stored or fetched.** Empty headings are flagged in place.
- Node count, nesting depth and output size are capped. The 60,000-character input limit is unchanged.

The preview is stored on `CourseElement.PreviewHtml` and copied onto each `ElementSnapshot`, joining the content hash only when present (as evidence did), so existing snapshots keep their hashes. Reviews therefore show exactly the rendering of the snapshot they are pinned to.

**Rendering.** The SPA renders it with `dangerouslySetInnerHTML`. That is acceptable only because of the generation rules above. It is additionally covered by the existing Content-Security-Policy (`script-src 'self'`, `style-src 'self'`, `img-src 'self' data:`), which blocks any script, inline style or remote image even if a sanitizer bug let one through. Canvas themes, colors, embedded media and scripts are deliberately not reproduced, and the UI says so beside every preview.

Rendered review links get the same defense in depth: only https URLs on the course's own Canvas origin are returned to the client.

**Preview access.** `GET /reviews/{id}/previews/{snapshotId}` returns a preview only when the review is visible to the caller (its existing ACL, including draft privacy) **and** the snapshot is the review's own or one captured in its course structure. A review id cannot be used to read arbitrary snapshots.

## Demo data

The extended demo seed adds:

- the "Course-level design (demo)" rubric (8 criteria)
- two DATA 150 modules: "Week 2: Working with real datasets", and an empty, inconsistently named "Data storytelling"
- published course reviews of WEB 101 and DATA 150
- a revision task linked to the empty module

Existing demo pages and assignments gain previews through an appended snapshot. Reviews already pinned to an earlier snapshot keep it, and show the plain-text view with a note that no preview was captured.

Seeding no longer reverts content changed by a simulated instructor edit. Previously, re-running the seed restored the original demo text over such an edit.

## Not done

- Color contrast, template/theme fidelity, media and caption checks still require a rendered page with styles. The preview deliberately drops styles.
- Quizzes, discussions, files and external tools are not inspected at course level, as the check limitations state.
- Comparing two pages side by side in one view, and course-level checks over workload size, are not implemented.

## Amendment: Canvas formatting in previews, and formatted course content (October 19, 2026)

The project owner asked for course content to keep "the original formatting" from Canvas instead of plain text.

- **More formatting is preserved,** still without passing any source markup or styles through. `CanvasText.Preview` maps recognised formatting onto a **closed set of classes**:
  - `pv-align-center|right|justify`, from `text-align`, `align` or `<center>`
  - `pv-bold`, `pv-italic`, `pv-underline`, `pv-strike`
  - `pv-fg-*` and `pv-bg-*` for text colour and highlight, from `color`, `background(-color)` or `<font color>`

  Styled `<span>`/`<font>` elements become `<span class=...>`; unstyled ones are still unwrapped. `<mark>` is kept (highlighted yellow by default).
- **Colours snap to a palette:** red, orange, yellow, green, teal, blue, purple, pink and gray, each in a darkened text shade and a light highlight shade that stay readable. Near-black text and near-white backgrounds count as defaults and get no class. Hex, `rgb()`/`rgba()` and common named colours are understood; anything else is ignored.
- **Inline styles are still never emitted.** The Content-Security-Policy (`style-src 'self'`) forbids them, and the closed class vocabulary means nothing from the source can inject CSS (for example `url(...)` or `behavior`). A test asserts every emitted class starts with `pv-`.
- **Still not reproduced:** fonts and sizes, Canvas themes, images (placeholders that show and flag their alt text), clickable links (labelled text naming the destination host, because full URLs are never stored), embedded media and iframes.
- **Course content view.** The course page shows each page, assignment, quiz and discussion in this formatted form by default, with a **Formatted / Plain text** switch that is remembered per browser. Long items are clipped, with "Show more". Accessibility flags (missing alt text, empty headings, vague links) are visible in place.
  - Formatted copies load in batches as modules open, from `GET /courses/{id}/previews?ids=…` (1–100 ids). That route uses the same tenant, owner and share check as the course and returns nothing for ids from other courses. Rendering uses the same `dangerouslySetInnerHTML` path and CSP defence as review previews.
- **Existing imports.** Formatted copies are made at import, so courses imported earlier show structure (headings, lists, tables) but not colours or alignment until they are re-imported. Items with no formatted copy fall back to plain text with a note.

### Code examples, callout boxes and headings (same day)

Requested so that code examples in the college's courses stand out as code. A survey of a real programming-course export found:
- 741 `<pre>` blocks and 1,751 `<code>` elements
- code blocks styled as grey bordered panels
- tinted callout boxes with coloured left bars
- headings with coloured bottom rules

The vocabulary was extended, still as classes only:

- **Code:** `<pre>` always renders as a monospace panel that keeps whitespace and scrolls sideways. `<code>`, `<kbd>`, `<samp>` and `<tt>` render as inline code. Any element whose `font-family` names a monospace font gets `pv-mono`.
- **Callout boxes:** on block elements (`div`, `p`, `section`, `article`, `aside`, `blockquote`, `center`):
  - `pv-box` (padding and spacing) whenever the element has a background, a border or a left bar
  - `pv-boxed` for a full border
  - `pv-accent-{colour}` for a `border-left` of 3px or more
- **Rules:** `pv-rule` and `pv-rule-{colour}` for a `border-bottom` on headings, paragraphs and divs.
- **Size:** `pv-large` (≥1.2× body text) and `pv-small` (≤0.875×) from `font-size` in px, pt, em, rem, % or keywords.
- **Faint tints are kept.** Backgrounds are treated as "none" only when essentially white (lightness above 0.985; it was 0.93), so `#eef6ff` and `#f7f7f7` callouts keep a light blue or grey. Fully transparent colours (`rgba(…, 0)`, `transparent`) are ignored.
- **Still not reproduced:** exact colours (snapped to the palette), font families other than monospace (for example decorative heading fonts), widths and margins, and syntax highlighting inside code.
- **Verified on real pages:** two real course pages were rendered locally with the app's stylesheet and checked by eye, then the files were deleted. Code panels, inline code chips, red heading rules and tinted callouts matched the Canvas originals' structure.
