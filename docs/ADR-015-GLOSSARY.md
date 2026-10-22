# ADR 015: Built-in glossary and term explanations

Accepted October 20, 2026.

## Context

TeachWell's guidance uses terms that many instructors have never been taught, for example:
- "Bloom's revised taxonomy"
- "WCAG 2.2, 1.3.1"
- "UDL: Engagement"
- "constructive alignment"
- "cognitive load"
- "orphan competencies"

The owner asked for three things:
- an explanation wherever such a word appears
- links to online resources for learning more
- a way into what the CTL offers

These should work without a reviewer, like the improvement guide (ADR 011).

## Decisions

### 1. A built-in glossary, shipped with the app

There are 31 terms in `src/TeachWell.Web/src/glossaryTerms.ts`, covering learning design, assessment, accessibility and TeachWell's own concepts. TeachWell's own concepts include the improvement guide, quick wins, standard sets, readiness reviews and endorsements.

Each term has:
- a plain-language definition
- the phrases that identify it in text
- knowledge-base tags
- up to three outside links

The glossary is code, not data:
- **Why:** it is the same for every college, it is reviewed like any other text, and it works offline.
- **What the CTL controls:** the college-specific part, through the knowledge base (decision 4).
- **What the CTL cannot do:** edit the definitions themselves; see "Not built".

### 2. Outside links are a short, checked list

The links point to free, non-commercial sources:
- W3C WAI and WCAG Understanding documents
- WebAIM
- CAST's UDL Guidelines
- the teaching centres of Carnegie Mellon (Eberly), Iowa State (CELT), University of Illinois Chicago (CATE) and Rice (Course Workload Estimator)
- Wikipedia, for general background

Every URL returned HTTP 200 with the expected page title on October 20, 2026. Two candidates had moved or closed and were left out: Vanderbilt CFT's Bloom page and retrievalpractice.org.

How the links behave:
- They open in a new tab with `rel="noopener noreferrer"` and are announced as such.
- TeachWell never fetches them, so there is no server-side request and no SSRF surface.
- Offline, the links simply do not load, and every definition still works.

**Upkeep:** links rot. Re-check them when the glossary is edited.

### 3. Explanations appear where the words are

There are two ways a term gets an explanation.

**Automatically (`Glossed`).** These texts are plain strings, often edited by the CTL in standard sets, so the app scans them:
- the improvement guide's "Why it matters", "How to fix it" and "Example"
- review and revision-task recommendations
- the guide, report and readiness panel descriptions

The first mention of each term in a block becomes an explanation. Matching is case-insensitive, whole-word, accepts typographic apostrophes, and prefers the longest phrase, so "Bloom's revised taxonomy" wins over "Bloom". The text is still rendered as text, never as HTML.

**Explicitly (`Term`).** Fixed labels are wrapped by hand:
- "Competency coverage", "Orphan competencies", "Unmapped assessments" and "rubric score" on the report
- "Bloom's revised taxonomy" in outcome import
- "quick wins" in the guide

### 4. How an explanation works

**The term:**
- It is a real button styled as text with a dotted underline.
- It reports `aria-expanded` and opens a non-modal `role="dialog"` popover.

**The popover:**
- It shows the definition, up to two "Learn more" links and **From your CTL: articles, workshops and help**.
- It opens on mouse hover after 300 ms and stays open while the pointer is over the term or the popover, so its links can be reached (WCAG 1.4.13).
- A click or tap pins it open. Escape closes it and returns focus, as do clicking elsewhere and tabbing away.
- It is positioned against the viewport, so scrolling containers do not clip it, and it flips above the word near the bottom of the screen.

Touch input never triggers hover, so a tap opens the popover, rather than opening and immediately closing it.

### 5. The Glossary page and the CTL connection

**Glossary** is under Help in the sidebar for instructors and CTL reviewers. It lists every term A–Z and has a search box. "From your CTL" in a popover opens it at that term, highlighted.

Each entry shows:
- the definition and all outside links
- **From your CTL:**
  - published knowledge articles tagged with the term's id, its name or one of its tags, or that use the term in their title
  - upcoming workshops that use the term in their title or description

Opening an article goes to Support → Knowledge base with that article open; a workshop opens Support → Workshops.

**Ask the CTL about this** (instructors) opens a new support ticket with the title "Question about {term}" and a fitting category, ready to complete. Nothing is sent until the instructor submits it.

CTL reviewers see which tags will list an article under each term. No API, schema or permission changes were needed: the page reads the existing `/knowledge` and `/workshops` endpoints with the viewer's own permissions, so instructors never see drafts.

Academic administrators and the system administrator have no Glossary item or "From your CTL" button, because they have no Support access.

## Consequences

- Explanations follow the CTL's edits: if a standard set's guidance mentions "formative assessment", it gets an explanation without further work.
- Auto-matching can occasionally explain a word used in an everyday sense. For example, "alignment" in a sentence about page layout would match. Phrases are chosen to keep this rare.
- A test that matches buttons by name prefix can also match a term. `reports.spec.ts` now looks for "Unmap " with a space, not "Unmap".

## Not built

- explanations in rich imported course content, which is sanitized HTML from Canvas and is left untouched
- translations

A CTL-editable glossary was added the same day; see the amendment below.

## Verification

- Headless Edge: 19 of 19 pass. `glossary.spec.ts`:
  1. Opens "WCAG 2.2" in a guide step and checks the definition, the W3C link, `target` and `rel`, and that Escape closes it and returns focus.
  2. Follows "alt text" → From your CTL to the highlighted Glossary entry, which lists the seeded "Writing useful image alt text" article, and opens that article in the knowledge base.
  3. Searches "bloom", sees that the CTL has nothing on it yet, and uses Ask the CTL to open a pre-filled ticket.
- No server changes; the SQLite and PostgreSQL suites are unaffected.

## Amendment: the CTL edits the glossary (October 20, 2026)

### What the CTL can do

On the Glossary page, CTL reviewers can:
- **Reword any term:** name, definition, matching phrases, knowledge-base tags and up to 5 links. "Return to TeachWell's wording" undoes it.
- **Add the college's own terms:** the id is made from the name, for example "Course shell" becomes `course-shell`, which also works as a knowledge-base tag. "Remove" deletes the term.
- **Hide a term instructors at the college should not see.** It is then neither listed nor underlined anywhere. "Show again" restores it, keeping any rewording.

Changes apply to everyone at the college on their next page load. Explanations, matching, CTL resources and the "Worded by your CTL" or "Added by your CTL" labels all use the merged result.

### Storage

- **Only changes are stored.** The built-in terms stay in the web app, and a new `GlossaryEntries` table holds one row per college and term id: a rewording, a hide, or a college term. Deleting a row returns a built-in term to TeachWell's wording. This keeps TeachWell's improvements to built-in terms flowing to colleges that have not changed them.
- **Isolation and concurrency:** rows are tenant-scoped and unique per tenant and term id. Edits carry a revision, so two CTL reviewers cannot silently overwrite each other (409).

### API

- `GET /glossary` (Instructor or CTL reviewer; Academic administrators get 403) returns the college's entries.
- `PUT /glossary/{termId}` and `DELETE /glossary/{termId}` are for CTL reviewers only.

### Validation

- **Term id:** lower-case words joined by single hyphens, up to 60 characters.
- **Name and definition:** required, with a name of up to 80 characters and a definition of up to 1,200.
- **Phrases:** up to 12, each 3–60 characters. Shorter ones would match inside everyday sentences.
- **Tags:** up to 10.
- **Links:** up to 5. Each needs a title, and its web address must be absolute `https://` or `http://` with no user name or password. `javascript:`, `data:`, `file:` and relative addresses are rejected.
- **Safety:** TeachWell never fetches these links, so a college link adds no server-side request.
- **Rendering:** definitions are rendered as text, never HTML.

### Audit

Each change is audited as `GlossaryTermSaved`, `GlossaryTermHidden` or `GlossaryTermReset`, and appears in the administrator's audit trail.

### Verification

- SQLite and native PostgreSQL 17 suites: 215 passed, 0 failed each (PostgreSQL applying the `CollegeGlossary` migration).
- New `GlossaryTests` cover:
  - rewording, adding, hiding, the stale-revision 409 and resetting
  - instructors reading the result
  - audit entries
  - 403 for instructors editing and for Academic administrators reading; 401 when signed out
  - invalid term ids, names and definitions
  - `javascript:`, credential-bearing, `file:` and relative links
  - phrases that are too short, and too many links
  - another college's entry never listed, reset or overwritten
- Headless Edge: 20 of 20 pass. In the new journey:
  1. A CTL reviewer rewords WCAG and adds "Course shell" with a link; a `javascript:` link is refused.
  2. The CTL reviewer hides Scaffolding.
  3. An instructor sees the new term, no Scaffolding, no edit controls, and the CTL's wording in the guide's WCAG explanation.
  4. The CTL reviewer undoes all three.
