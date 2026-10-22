# ADR 016: Plain language and a start path for instructors

Accepted October 20, 2026.

## Context

TeachWell is meant for instructors who have not studied course design or educational technology. A review of the interface found three kinds of problem.

**Internal codes on screen:**
- task statuses such as INPROGRESS and READYFORREVIEW
- the support category CourseDesign
- audit actions such as "Role revoked: AcademicAdmin"

**Technical wording:**
- "18 course elements" and "Owner access"
- "snapshot"
- "No student endpoints"
- "Support themes (tenant-wide)"

**No obvious way in:**
- A new instructor landed on a course list with no "what do I do first".
- The course page had no title at the top; the course's name sat below the guide and readiness panels.
- Sections appeared in the order they were built, not the order an instructor needs them.
- Bringing a course in depended on a one-sentence Canvas export instruction.

## Decisions

### 1. Codes become plain labels in one place

`src/TeachWell.Web/src/labels.ts` maps every code the API returns to the words people read:

| Kind | Examples |
|---|---|
| Task status | "To do" (NeedsRevision), "Waiting for a CTL check" (ReadyForReview), "Not applicable" |
| Ticket status | "Received by the CTL" (Triaged), "Waiting for the instructor" (WaitingOnInstructor) |
| Support category | "Course design", "Video and media", "Something else" |
| Role | "Academic administrator" |

Unknown codes fall back to spaced, sentence-case words, so a new code never appears as one run-together word.

- **Covered:** task and ticket tags, status history ("In progress → Waiting for a CTL check"), the support category list and tags, oversight and report counts, Canvas connection status, and the audit trail's role changes.
- **Unchanged:** the API, stored values and CSS hooks (for example `status-readyforreview`).

### 2. Plainer wording where instructors read it

Changes include:
- **Course cards:** "3 items (modules, pages, assignments) · Your course" and "Shared with you for review".
- **Dashboard:** the third figure is now "Safe to explore: nothing here changes your Canvas course or stores student work".
- **Picture messages:** "No picture of this page was saved with the version that was reviewed".
- **Report heading:** "Common support topics across the college".
- **Export upload:** plain fine print.

### 3. Standards references sit under the reason, not inside it

Guidance often ends with a citation such as "(WCAG 2.2, 1.1.1 Non-text Content.)". The guide now shows the reason as a plain sentence and the citation on its own line: "Based on: WCAG 2.2, 1.1.1 Non-text Content". The citation keeps its glossary explanation.

- **Matching:** the split happens in the web app (`splitReference`), only for a final parenthesis that names WCAG, UDL, Bloom's, a taxonomy, a theory, alignment or Section 508. Other text is left alone.
- **Scope:** it applies equally to TeachWell's guidance and to a CTL's own.

### 4. A "Start here" checklist on the instructor dashboard

The checklist has four steps:
1. **Bring in your course.**
2. **Work through the improvement guide.**
3. **Ask the CTL for a readiness review.**
4. **Get your course endorsed.**

The next step is highlighted, with one button that takes it:
- **Bring in your course:** goes to Import courses.
- **Work through the guide:** opens the course at the guide.
- **Ask for a readiness review:** opens the course at the readiness review.

Progress comes from what the instructor has actually done, through `GET /getting-started` (instructors and CTL reviewers; Academic administrators get 403):

| Step | Done when |
|---|---|
| Bring in your course | They own a course imported from Canvas or an export. Fictional demo courses don't count. |
| Work through the guide | They have marked a guide step on one of their own courses. |
| Ask for a readiness review | They have requested a readiness review for one. |
| Get your course endorsed | One of their courses has an endorsement. |

The suggested course is their most recently imported course, or a demo course when they have none.

These are outcomes, not page views. The daily guide record is also written by automatic re-checks, so it cannot show that a person opened the guide. The figures cover only the caller's own courses and actions.

"Hide this checklist" is remembered in that browser (a convenience). "Show the Start here checklist" brings it back.

### 5. One explanation of how the tools fit together

The instructor dashboard ends with **Four tools, one goal**. It explains, in one card each, when and why to use:
- the improvement guide
- help from the CTL
- the readiness review
- reviews and revision tasks

The checklist links to it. The glossary gains "CTL (Center for Teaching and Learning)", "Course review" and "Revision task", and the cards use glossary explanations.

### 6. A calmer course page

**A header at the top:**
- the course code and source ("FROM A CANVAS EXPORT")
- its title
- one sentence on what TeachWell is looking at: "a copy of this course from October 20, 2026 … Nothing you do in TeachWell changes your Canvas course"
- **On this page** links that open a folded section and scroll to it (`goToSection`)

**Sections in working order:**
1. Improvement guide
2. Revision tasks
3. Readiness review
4. Reviews
5. Alignment and quality
6. Course content
7. Bring in a newer copy
8. Sharing
9. Course standards, which starts folded because it is a CTL setting and read-only for instructors

**Scroll position:** every new page starts at its top. Previously, opening a course from the bottom of the dashboard landed halfway down the course.

### 7. Step-by-step help for bringing a course in

The import panel numbers the Canvas steps (Settings → Export Course Content → Course → Create Export → download the .imscc file → upload). It says that a large course can take a few minutes, and that the CTL can help. There are no screenshots: TeachWell does not invent Canvas screenshots, and a college's CTL can link its own job aid from the glossary or knowledge base.

### 8. "How to fix it" names the Canvas clicks

Six default guidance texts now say where things are in the Canvas editor:
- **Heading level is skipped** and **Long page has no headings:** the block menu that usually says "Paragraph".
- **Link has no text** and **Link text is not descriptive:** Link Options → Text.
- **List typed as plain text:** the list buttons in the toolbar.
- **Deadlines on the same day:** Edit → Assign → Due date.

Published standard-set versions keep their own copy of the guidance (ADR 011), so these reach new sets, new drafts and fresh installs. A CTL can bring them into an existing set by drafting a new version.

## Not built

- In-app screenshots or videos of Canvas.
- Tailoring the checklist per course; it follows the instructor's most recent course.
- A full plain-language rewrite of CTL-facing screens (rubrics, standards, oversight). Those users are expected to know the vocabulary.

## Verification

- SQLite and native PostgreSQL 17 suites: 217 passed, 0 failed each, including the new `GettingStartedTests`:
  - progress follows the instructor's own actions
  - demo courses do not count
  - an imported course becomes the one to continue with
  - another person's progress never shows
  - Academic administrators get 403, and people who are not signed in get 401
- Headless Edge: 21 of 21 pass. `start-here.spec.ts`:
  - follows the checklist into the export steps, then into the guide of the suggested course
  - uses **On this page** to open the folded Course standards
  - hides the checklist and brings it back after a reload
  - opens a glossary explanation from **Four tools, one goal**

  `glossary.spec.ts` checks the separate "Based on" line. Other journeys were updated for the new wording and the level-2 guide heading.
