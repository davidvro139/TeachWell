# ADR 008: Alignment and reporting

Accepted October 16, 2026. Docker remains deferred.

## A new domain concept: competencies

Nothing in DATA_MODEL.md modeled "competency/objective mapping to assessment evidence" (Phase 6 of the plan) before this phase — it is designed fresh here. `CourseCompetency` (course-scoped, instructor-authored: code, title, description) and `CompetencyMapping` (competency → assignment element) are freely editable course-design metadata, not evidence or a scored artifact, so — unlike rubric versions, reviews, and the snapshots/events those phases made immutable — a competency and its mappings can be edited and deleted at will. The course owner (Instructor role, course ownership) manages them; a CTL reviewer with course access can read them but not author them, mirroring how Canvas connections and course sharing are owner-managed.

Mapping is restricted to **assignment** elements specifically, not pages: REVIEW_FRAMEWORK.md's alignment criterion is about assessment evidence ("each competency has appropriate assessment evidence"), and assignments are this app's assessment element type.

## Reports are computed, not stored

Every report in this phase is a live read over existing review, task, ticket and competency data — there is no persisted "report" entity, consistent with a report being a query, not a record. Per the Phase 6 accept criterion, every report DTO carries a `Scope` (a one-line description, a UTC `GeneratedAt` timestamp, and a `Caveats` list) so a number is never shown without the context needed to judge it. No report presents a pass/fail badge, a compliance label, or any certification language; the UI and every caveat list say so explicitly.

- **`GET /reports/course/{id}/alignment`**: competency coverage. Orphan competencies (zero mapped assignments) and unmapped assignments (zero competencies) are both surfaced directly, matching "orphan objectives and unmapped assessments are identified" in REVIEW_FRAMEWORK.md.
- **`GET /reports/course/{id}/quality`**: reviewed-element coverage, an average rubric score across the course's published reviews (recomputed live via the same `ReviewScoring.Compute` used in a single review, not a separately maintained aggregate), per-category averages, and an outstanding-findings list.
- **`GET /reports/ctl/overview`** (CTL-only route group, 403 for other roles): courses the caller can currently access, published reviews and outstanding findings across them, revision tasks by status with average age, and tenant-wide support ticket themes — the one report that is deliberately **not** scoped to a single course.
- **`GET /reports/revisions`**: the caller's own revision tasks (assignee or reviewer — the exact same "mine" definition `TaskService.ListMineAsync` already uses, reused rather than reinvented), by status with aging, plus a `?format=csv` variant for a download. PDF export is not implemented; the product requirements describe PDF as a later addition, and nothing here claims it.

**Outstanding** is defined precisely: a `Confirmed` finding on a `Published` review with no `Resolved` revision task tracking it. `HasOpenTask` distinguishes "nobody is working on this yet" from "a task is open but not finished" — both count as outstanding, since neither is actually resolved, but the UI and API tell them apart.

## A real bug this phase found: GroupBy/Select/OrderBy translation

`CtlOverviewAsync`'s support-theme aggregation originally wrote `db.SupportTickets.Where(...).GroupBy(t => t.Category).Select(g => new SupportThemeDto(...)).OrderByDescending(t => t.Count)` as one query. EF Core cannot translate an `OrderBy` on a property of the **projected** DTO when it follows a `GroupBy`/`Select` in the same server-side expression tree, and throws `InvalidOperationException` at the point the query actually executes — which `UseExceptionHandler()` turns into an opaque generic 500 ProblemDetails response with no indication of the real cause. This was caught only by an integration test asserting the report's actual numbers (an HTTP-status-only test would have passed, since a 500 is still a well-formed response); it was diagnosed by calling the service directly inside the test, bypassing the HTTP layer, to get the real exception and stack trace. Fixed by materializing the grouped projection with `ToListAsync()` first and ordering the resulting in-memory list — the same pattern already used correctly elsewhere in this service (e.g., `CategoryAverageDto` aggregation, task-status grouping), just missed in this one spot.

## Session-reuse fix to the browser test suite (not a security change)

By this phase, the browser end-to-end suite's total real `/auth/login` calls reached 13 across eight spec files, consistently exceeding the login endpoint's 10-per-IP-per-minute rate limit (a legitimate brute-force control; ADR 001/002) within the suite's ~30–60 second run — a limitation flagged but left unresolved in ADR 007. Per `playwright.config.ts`'s existing `workers: 1`, every spec file already runs sequentially in one Node process, so `e2e/auth.ts` now caches each persona's session cookie in a module-level map and reuses it across contexts and spec files instead of logging in again; only the first use of each persona (instructor, reviewer) calls the real endpoint, cutting the suite's total logins from 13 to 2. No test asserts anything about login behavior through this helper — they all use it purely as setup — so this changes nothing about what is being tested, and the production rate limit itself is untouched. All eight browser specs, including the two that previously 429'd under full-suite load, now pass reliably on repeated runs.

## Not included / limitations

- PDF export of any report.
- Trend lines over time for rubric scores (the quality report averages all of a course's published reviews together; it does not plot score-over-time the way a "trend" chart would).
- Competency mapping to pages, modules, or Canvas-reported outcomes; only instructor-declared competencies against assignments.
- Any automatic judgment of whether mapped evidence is actually adequate — coverage counts a mapping exists, not that the mapping is sound.
- A dedicated "reports" history or snapshot; every figure is live as of the request.

## Amendment: pasting learning outcomes as competencies (October 19, 2026)

The college keeps course competencies as Student Learning Outcomes in Simple Syllabus. That is an external (LTI) tool whose content is not part of Canvas exports or the Canvas API; this was confirmed on two real exports. TeachWell therefore lets the course owner paste the outcomes and approve them as competencies. A direct Simple Syllabus integration would need institution-level API access and IT approval, so it belongs with the Phase 12 integrations.

- **Preview.** `POST /courses/{id}/competencies/parse {text}` (owner only, up to 20,000 characters) proposes competencies and saves nothing. `OutcomeListParser`:
  - drops headings ("Student Learning Outcomes", "Course Objectives", …) and short lead-ins ending in ":"
  - strips bullets and list numbering
  - keeps codes such as "SLO 2:" or "CLO-4 –" (normalized to `SLO-2`, `CLO-4`)
  - joins wrapped lines that continue in lower case
  - drops repeated lines, and stops at 100 outcomes

  The service then suggests codes, continuing the course's `SLO-n` numbering and avoiding codes already used. It also marks outcomes the course already has (same wording) as not selected, and annotates each with its Bloom's level, any vague opening phrase from the standard vague-verb list, and length over 200 characters.
- **Approve.** `POST /courses/{id}/competencies/bulk {items[]}` (owner only, 1–100 items) validates every item, checking codes against each other and the course, and saves all or none. Each competency is audited as `CompetencyCreated`.
- **UI.** On the course page, Alignment and quality → **Paste outcomes**: paste, **Read outcomes**, then edit each row's code, wording and optional description, untick any to skip, and **Add N competencies**.

### Amendment: mapping grid — October 19, 2026

Mapping one assessment at a time from a dropdown did not scale to imported courses with dozens of quizzes and assignments. The course owner can now map them in a grid.

- **Layout.** Rows are assessments (assignments, quizzes and discussions), grouped by module in course order; assessments outside any module are listed last, and an assessment linked from several modules appears once, under its first module. Columns are the course's competencies, with a sticky header that shows each one's mapped count.
- **Mapping a module.** Each module has its own row. Ticking a competency there maps every assessment in that module, and unticking it unmaps them all. When only some are mapped, the box shows as partly ticked. This is a shortcut that maps the module's current assessments; it is not a standing rule, so an assessment added to the module later must be mapped separately.
- **Saving.** Changes stay pending in the browser (changed cells are highlighted) until **Save N changes**; **Discard changes** drops them. A filter narrows the list by assessment or module name.
- **API.** `PUT /courses/{id}/competencies/mappings {changes: [{competencyId, elementId, mapped}]}` (owner only, 1–5,000 changes) first checks that every competency belongs to the course and every element is an assessment in it, then applies all the changes or none. Changes that match the current state do nothing. Each real change is audited as `CompetencyMapped` or `CompetencyUnmapped`, the same as single mappings.
- **UI.** Alignment and quality → **Map assessments** (shown once the course has competencies and assessments).
