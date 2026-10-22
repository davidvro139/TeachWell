# ADR 011: Configurable review engine and instructor improvement guide

Accepted October 19, 2026. Docker remains deferred. This ADR is the design for Phases 8 (CTL standards library) and 9 (course improvement guide) of the project roadmap, and records how they are being built. Status per slice is tracked at the end.

## Goal

The project owner asked for "an advanced review algorithm with lots of settings on the back end" that gives instructors "an intelligent review in many ways", plus "a very user-friendly guide" explaining how to improve and why.

Two constraints shape the answer:

- **No AI until Phase 13.** The owner placed AI last on the roadmap. "Intelligent" therefore means many explainable, instructional-design-aware rules, not a language model. Every finding names the rule that fired and the evidence it saw, and the same snapshot under the same settings always gives the same result.
- **Content and design, never instructor performance.** No score is presented as compliance or certification (REVIEW_FRAMEWORK.md, ADR 008).

## Before this ADR

Automated checks were two hardcoded methods (`DeterministicChecks.Run`, `CourseChecks.Run`): 19 checks with fixed severities, built-in word lists and baked-in recommendation text. The CTL could choose which checks a rubric criterion referenced, but could not tune, disable or explain any of them.

## Design

### 1. Check engine (slice 1)

Every check is a registered rule (`ReviewCheck`, in `src/TeachWell.Domain/ReviewEngine.cs`) declaring:

| Member | Purpose |
| --- | --- |
| `Code` | Stable identifier, unchanged from before (`LINK_VAGUE`, `MODULE_EMPTY`, …) |
| `Name` | Short catalogue name |
| `Aspect` | One of the five guide aspects (below) |
| `Target` | `Element` (page/assignment) or `Course` (course structure) |
| `DefaultSeverity` | Used when no setting overrides it |
| `Parameters` | Configurable numbers (with min/max) and word lists (with size limits) |
| `Stage` | Ordering group, so findings come out in reading order |
| `Run(context, settings)` | Pure function over an immutable snapshot |

`CheckRegistry.All` lists every check. Adding a check means adding one class.

A **`CheckProfile`** maps check codes to `CheckSettings` (enabled, severity, numbers, word lists). Checks without an entry use their defaults, so `CheckProfile.Default` reproduces the pre-ADR behaviour exactly. `ReviewEngine.RunElement` / `RunCourse` run only enabled checks and order findings by stage, then document position, then registry order.

**Validation** (`CheckProfile.Validate`, `ReviewCheck.Validate`) rejects unknown checks, unknown settings, unknown severities, missing parameters, numbers outside their range, word lists over their item limit and empty or over-long entries. Word lists are matched literally (regex-escaped, with a one-second match timeout), never interpreted as patterns supplied by the CTL.

Parameters introduced in slice 1, replacing previously hardcoded lists:

| Check | Parameter | Default |
| --- | --- | --- |
| `LINK_VAGUE` | `phrases` — vague link phrases | click here, here, click, read more, more, link, this, this link, learn more, go, details, info, download |
| `COURSE_START_MISSING` | `keywords` — orientation keywords | start, welcome, orientation, getting started, overview, syllabus, introduction, read me, readme |
| `MODULE_NAMING_INCONSISTENT` | `prefixes` — numbered module prefixes | module, week, unit, topic, lesson, chapter, part, session |

The orientation keywords are also used by the naming check, which skips an opening orientation module. That shared fact lives on `CourseCheckContext.HasStart`.

### 2. Aspects

Every check belongs to exactly one aspect. The improvement guide groups steps by these, each with a short instructional-design introduction.

| Aspect | Existing checks |
| --- | --- |
| Getting started and navigation | `ITEM_UNAVAILABLE`, `ITEM_UNPUBLISHED`, `COURSE_START_MISSING`, `MODULE_EMPTY`, `MODULE_UNPUBLISHED`, `MODULE_NAMING_INCONSISTENT`, `CONTENT_NOT_IN_MODULE` |
| Outcomes and alignment | `COURSE_COMPETENCIES_MISSING`, `ASSIGNMENT_NOT_ALIGNED` |
| Assessment and feedback | `ASSIGNMENT_RUBRIC_MISSING`, `ASSIGNMENT_DUE_DATE_MISSING` |
| Content and cognitive load | (none yet; slice 3) |
| Accessibility | `IMG_ALT_*`, `HEADING_*`, `LINK_*`, `TABLE_HEADERS_MISSING` |

### 3. Standard sets (slice 2)

A **standard set** is a versioned draft → publish bundle, immutable once published (EF guard plus PostgreSQL trigger, the rubric pattern from ADR 005). Each version stores:

- the `CheckProfile`: per-check enabled, severity, thresholds and word lists
- guidance per check: *why it matters*, *how to fix it* (Canvas steps) and a *before/after example*, all CTL-editable, with links to knowledge-base articles and workshops
- which checks are **required** for readiness (Phase 10)
- the rubric versions to use for element and course reviews

Selection follows owner decision D1. Courses carry a delivery mode and a department (D4). The most specific published set applies: department + mode, then department, then mode, then college-wide. The seed publishes a college-wide set equal to today's defaults, so nothing changes until the CTL publishes something else.

Every automated result records the standard-set version it ran under. Earlier results keep showing the version that produced them.

Only CTL reviewers draft and publish standard sets. Instructors can read the active set's guidance but not edit it. Every change is audited. The CTL's settings screen groups checks by aspect, shows each parameter with its range, and offers a **preview** that dry-runs a draft set against an accessible course before publishing, showing how many findings would be added or removed.

#### As built (slice 2, October 19, 2026)

- **Model.** `StandardSet` (name, optional `DeliveryMode` of Online/Hybrid/FaceToFace, optional `Department`, a unique per-tenant `ScopeKey`) and `StandardSetVersion` (draft → published, `Notes`, `Revision` concurrency token, and `ChecksJson`). `ChecksJson` stores every check's enabled flag, severity, required flag, numbers, word lists and guidance in full, so a published version never depends on later code defaults.
- **Scope is fixed at creation.** Only one set may claim each scope; department matching ignores case and surrounding spaces. Changing which courses a set governs therefore means creating a set for the new scope, which keeps "which standards applied when" answerable from history.
- **A new set starts from the standards that currently apply to its scope.** For example, a department set's first draft copies the college-wide set's latest published version. A new draft of an existing set copies that set's latest version. Either way, checks added to TeachWell since then are appended with their defaults.
- **A check a published version does not mention does not run under it.** New analyzers (slice 3) therefore reach instructors only when the CTL publishes a version that includes them.
- **Course classification (D4).** `Course.DeliveryMode` and `Course.Department` are set by a CTL reviewer with course access (`PUT /courses/{id}/classification`). Owners see them but cannot change them. Each actual change is audited as `CourseClassificationChanged`.
- **Reviews pin a version at start.** `ElementReview.StandardSetVersionId` is set when a review starts, from the course's classification at that moment, exactly like the rubric version. Re-running checks always uses the pinned version. Reviews started before this slice, or when no set is published, have no pinned version and use the built-in defaults, and say so.
- **Guidance reaches instructors now, not only in slice 5.** Each review returns the pinned version's guidance for the automated checks among the findings the caller can see. The review screen shows it under each finding as "Why this matters and how to fix it". Each finding keeps its own specific recommendation as well; the guidance adds context rather than replacing it.
- **Preview** runs the version's saved settings and the course's current standards over the latest snapshot of every live page and assignment, plus a freshly built course structure (not saved). It returns per-check counts. It requires course access, is POST/CSRF-protected, and changes nothing.
- **Immutability.** Enforced by the EF guard and a PostgreSQL trigger (`immutable_published_standard_set_versions`, reusing `teachwell_guard_published_row()`).
- **Audit actions.** `StandardSetCreated`, `StandardSetVersionDrafted`, `StandardSetDraftUpdated`, `StandardSetVersionPublished`, `CourseClassificationChanged`.
- **Seed.** The baseline seed publishes "College-wide course standards (demo)" v1, equal to the built-in defaults, so behaviour is unchanged until the CTL publishes something else. The extended demo seed adds "Online course standards (demo)", which makes four checks required, raises the unpublished-module severity, adds "watch this", "video" and "slides" to the vague link phrases, and rewrites the start-here guidance for online students. It also classifies WEB 101 and EDU 210 as Online, DES 120 as face-to-face and DATA 150 as Hybrid. ASMT 210 is left unclassified to show the college-wide fallback.
- **Deferred to later slices:**
  - choosing rubric versions inside a standard set
  - linking guidance to knowledge-base articles and workshops (slice 5, where the guide shows them)
  - requiring checks for readiness, which is stored now but has no effect until Phase 10

### 4. New analyzers (slice 3)

Each is a new `ReviewCheck` with configurable parameters and default guidance written in TeachWell's own words. They cite WCAG 2.2, UDL and Bloom's as references; licensed rubric text (for example Quality Matters) is not copied.

| Aspect | Planned checks |
| --- | --- |
| Content and cognitive load | Reading grade level (Flesch–Kincaid) above a threshold; estimated reading time; long pages without headings; overly long pages; long paragraphs; published pages with almost no content |
| Outcomes and alignment | Outcome statements using vague verbs ("understand", "know", "appreciate"); each outcome classified by Bloom level from its verb; outcomes not assessed by any assignment; level mismatch (a "create" outcome assessed only by recall tasks) |
| Assessment and feedback | Instructions below a minimum length; points missing; several items due on one day; no low-stakes assessment early in the course |
| Workload and pacing | Estimated hours per module (reading time + assignment estimates); modules heavier or lighter than the course median by a configurable ratio |
| Getting started and navigation | Modules with no overview; modules with too many items; modules with no assessment |
| Accessibility | Lists typed as plain lines; instructions that rely on colour ("the items in red") |

Some of these need evidence not captured yet: word and paragraph counts, sentence statistics and outcome text. Capturing them adds fields to `ContentEvidence`/`CourseStructureEvidence` under a new evidence version. Snapshots with older evidence report a limitation ("re-import to run this check") instead of passing silently, as ADR 005 requires.

#### As built (slice 3, October 19, 2026)

Seventeen checks were added, bringing the total to 36. Every one has parameters the CTL can change (where it has a setting) and default guidance in `DefaultGuidance.cs`.

| Check | Aspect | Default | Fires when (defaults) |
| --- | --- | --- | --- |
| `PAGE_TOO_LONG` | Content | Low | A page has more than 1,500 words |
| `PAGE_LONG_NO_HEADINGS` | Content | Medium | A page or assignment has 400+ words and no headings |
| `PARAGRAPH_TOO_LONG` | Content | Low | A paragraph has more than 150 words |
| `READING_LEVEL_HIGH` | Content | Low | 150+ words and an estimated Flesch–Kincaid grade above 14 |
| `PAGE_NEARLY_EMPTY` | Content | Medium | A published page has fewer than 30 words |
| `ASSIGNMENT_INSTRUCTIONS_SHORT` | Assessment | Medium | Assignment instructions have fewer than 50 words |
| `ASSIGNMENT_POINTS_MISSING` | Assessment | Low | An assignment reports no or zero points |
| `LIST_TYPED_AS_TEXT` | Accessibility | Low | 2+ consecutive lines start with typed dashes, bullets or numbers |
| `COLOR_ONLY_REFERENCE` | Accessibility | Medium | A paragraph contains a configurable colour phrase ("in red", "colour-coded", …) |
| `MODULE_NO_OVERVIEW` | Navigation | Low | A module's first item does not match the overview keywords (orientation module excluded) |
| `MODULE_TOO_MANY_ITEMS` | Navigation | Low | A module has more than 15 items |
| `MODULE_NO_ASSESSMENT` | Assessment | Low | A module (after orientation) has items but no assignment |
| `WORKLOAD_IMBALANCE` | Content | Medium | A module's estimated time is more than 2× or less than ½ the median (3+ modules; reading at 200 wpm plus 60 min per assignment) |
| `ASSIGNMENTS_DUE_SAME_DAY` | Assessment | Medium | More than 2 assignments are due on one UTC calendar day |
| `OUTCOME_VAGUE_VERB` | Outcomes | Medium | A competency title opens (after "Students will be able to" and similar) with a configurable vague phrase |
| `OUTCOME_NOT_ASSESSED` | Outcomes | Medium | No assignment is mapped to a competency |
| `OUTCOMES_LOWER_ORDER_ONLY` | Outcomes | Low | 2+ outcomes have a recognised Bloom verb, and all of them are Remember or Understand |

**Where the data comes from:**

- **Page and assignment text checks need no re-import.** They read the snapshot's stored plain text, in which each paragraph, heading, list item and table row is its own line. They run on existing content as soon as a standard set enables them. If a caller supplies no text, the review reports a limitation instead of passing.
- **The course structure evidence moved to version 2.** It now carries each item's word count, points and due date, plus the course's competencies with how many assignments are mapped to each. The structure is rebuilt from the database whenever it is captured. Course reviews pinned to a version 1 structure report "predates workload estimates/outcome checks" limitations.
- **Assignment due dates.** `AssignmentEvidence.DueAt` (UTC) is now read by the Canvas API reader and the export reader. Assignments imported before this report a limitation ("re-import to check them for clustering"). The demo courses have fictional November 2026 deadlines.
- **Text measures.** `TextStats` counts words, sentences (a line without closing punctuation still ends a sentence) and syllables (an English vowel-group heuristic), and estimates the Flesch–Kincaid grade. Every reading-level finding states that it is an estimate for English text.
- **Bloom's taxonomy.** `Bloom` strips lead-ins, reads the first verb and assigns it to its most common level in TeachWell's own grouping. Unknown verbs are left unclassified rather than guessed.

**Rollout through standard sets:**

- `CheckRegistry.InitialCodes` names the 19 original checks. The seeded college-wide v1 contains exactly these, so existing reviews and the test baseline are unchanged.
- A new draft of any set adopts the new checks at their defaults.
- The extended demo seed publishes v2 of both demo sets after the seeded reviews, so those reviews stay on v1. The Online v2 also requires a module overview and allows at most 12 items per module.

**Added after slice 3 (October 19, 2026):** the three items first deferred here are now built.

| Check or setting | Aspect | Default | Behaviour |
| --- | --- | --- | --- |
| `EARLY_LOW_STAKES_MISSING` | Assessment | Medium | Fires when none of the assignments in the first 2 modules is worth at most 10% of the course's points. Ungraded practice counts as low stakes. Both numbers are settings. If any assignment's points were not captured, the check reports a limitation instead. |
| `OUTCOME_ASSESSMENT_LEVEL_MISMATCH` | Outcomes | Low | Fires when an outcome at Apply level or higher has mapped assessments that all sit at a lower level. Each assignment's level is the highest Bloom verb found in its title and instructions. That estimate is deliberately generous, so the check under-reports rather than over-reports. Outcomes whose assessments use no recognised task verbs are counted in a limitation, not judged. |
| `Institution:TimeZone` | (deployment setting) | UTC | `ASSIGNMENTS_DUE_SAME_DAY` groups deadlines by calendar day in this time zone, and the evidence names the zone. The value is validated at startup, so an unknown id stops the app. `Start-Local.ps1` passes the computer's own zone, or `-TimeZone <id>`. |

Course-structure evidence is now version 3. It adds each assignment's estimated level and each outcome's mapped element ids; older snapshots report a limitation for the mismatch check. Total: 38 checks.

### 5. Turning findings into advice (slice 4)

Raw findings are post-processed into guide steps:

- **Grouping.** The same check across many items becomes one step ("12 images across 5 pages need descriptions"), listing every location.
- **Priority** = severity weight × reach (how many places) × position (earlier modules weigh more) × ease (estimated effort). All four weights are standard-set settings. Ties are broken deterministically.
- **Quick wins.** High-priority, low-effort steps are labelled so instructors can start with them.
- **Strengths.** Enabled checks with no findings are reported as what is already working ("Every assignment has a rubric"). These are descriptive, never a score.

### 6. Instructor improvement guide (slice 5)

For one course, the guide runs the active standard set against the latest snapshots and merges in the CTL's outstanding confirmed findings, marked as from the reviewer.

- **Overview:** one card per aspect (required and recommended counts), a quick-wins strip with an estimated time, and the strengths list. No overall score and no pass/fail.
- **One step at a time:** where the issue is (item, highlighted preview, "Open in Canvas"), why it matters, how to fix it, an example, the CTL's linked resources, and any revision task already tracking it.
- **Actions:** **Done**; **Not applicable** with a required reason; **Ask for help**, which opens a support ticket prefilled with course, item and issue.
- **Progress** is saved per course and attributed to the person who set it. After the next import, steps no longer detected resolve automatically. A step marked Done but still detected is flagged rather than hidden.
- **Language** describes the course ("This page is long without headings"), never the instructor.
- **Access:** course owner and CTL reviewers with course access, as for every course resource. Anyone else gets 404.

#### As built (slices 4 and 5, October 19, 2026)

The project owner asked for guidance built into the tool, with no reviewer needed; the reviewer helps afterwards. Slices 4 and 5 were built together.

- **On demand, nothing stored but progress.** `GET /courses/{id}/guide` runs the course's applicable standard set (D1, as for reviews) against two things: the latest snapshot of every page, assignment, quiz and discussion, and the course structure as it is now. The checks run on every load. Only the instructor's progress is saved (`guide_progress`).
- **Steps and places.** A step is one check, covering every place it was found. The place keys are stable across re-imports while the same issue is found: `{elementId}:{fingerprint}` for item checks and `course:{fingerprint}` for course checks. Each place names its item and module, and gives the check's own detailed description and an "Open in Canvas" link when the course's Canvas address is known.
- **Priority** follows section 5: severity weight × reach (1 + log₂ of open places) × position (a bonus of up to 50% for the first module, less for later ones) × ease (1.5 for 15 minutes or less in total, 1.0 up to an hour, 0.7 beyond). Steps the standard set marks **required** come first; finished steps come last; ties break on registry order.
  - **Deviation:** the weights are TeachWell's defaults (`GuideWeights`), not yet standard-set settings.
  - Effort is TeachWell's rough estimate per check, times the number of places (`GuideCatalog`). It is shown as "about N min" and is not a promise.
- **Quick wins** are open steps of Medium severity or higher that take 15 minutes or less in total. The overview shows up to five.
- **Strengths** list enabled checks that found nothing. A strength appears only when the course has something for that check to look at: no "every image has alt text" in a course without images. "Image is marked decorative" never produces a strength.
- **Area introductions.** Each of the five areas has a short explanation of why it matters, written in TeachWell's own words. It appears when the instructor selects the area.
- **Progress.**
  - The owner marks a place, or every open place in a step, as **Done**, or as **Not applicable** with a required reason (up to 500 characters). They can **Reopen** any mark.
  - Each mark records who set it and when.
  - A place marked Done before the latest import that is still found shows as open again, flagged "still found after your latest import". "Latest import" is the course's last sync or its newest snapshot, whichever is later.
  - Done marks whose issue is no longer found are counted as fixed.
  - Every change is audited as `GuideProgressDone`, `GuideProgressNotApplicable` or `GuideProgressOpen`.
- **Ask the CTL for help** creates a support ticket directly from the step, with the course and the step's places (up to 20) included in the description. The item is linked too when only one is involved. The category is Accessibility for accessibility steps and CourseDesign otherwise, and the priority is the step's severity.
- **Access:** the owner and shared CTL reviewers read the guide, including Not-applicable reasons. Only the owner records progress; anyone else gets 404 on `PUT`, and anyone without course access gets 404 on both.
- **Reviewer findings (added the same day).** The guide includes the CTL's outstanding confirmed findings. These are confirmed findings in published reviews with no resolved revision task, the same rule the course quality report uses.
  - An automated finding the guide still detects is attached to that place: "Confirmed by {reviewer} in a published review", plus an **Open task** button when an open revision task tracks it. Its step is tagged **Confirmed by CTL**. Ranking is unchanged.
  - An automated finding the guide no longer detects is left out, because the content has changed since the review.
  - A reviewer's own (manual) finding is listed under **From your CTL reviewer**, with **Open review** and **Open task** buttons. Its progress is tracked through the review and revision task, not guide marks.
  - Draft reviews contribute nothing.
- **Strengths** are folded into a collapsed "What is already working (N)" section grouped by area. On the full demo data the flat list ran to 29 lines and pushed the steps out of view.
- **CTL resources on steps (added the same day).** On the Standards page, under "Resources in the improvement guide", the CTL links a knowledge-base article or a workshop to a check, up to 5 per check. Every guide step for that check then shows them under "Learn more from the CTL".
  - Articles open in place. Workshops show their date and modality, with a **Register** button or the instructor's registration status.
  - Instructors see only published articles and workshops that have not started. Other links stay in place, and the CTL list says they are hidden and why.
  - **Decision:** links are a tenant setting (`check_resource_links`), not part of a standard-set version. Articles and workshops are editable and time-bound, so pinning them to an immutable version would only force new versions to fix a link.
  - Linking and unlinking are audited (`CheckResourceLinked`, `CheckResourceUnlinked`). The full demo seed links the alt-text job aid to both image checks and the rubric workshop to the missing-rubric check.
- **Deferred:**
  - highlighting the issue in a preview
  - CTL-configurable weights and effort estimates
  - caching: a course with many hundreds of items is re-checked on every load

### 7. Feedback loop (slice 6)

The CTL sees, per check, how often instructors mark steps **Not applicable** and the reasons they give. A check with a high rate is a candidate for a tuned threshold, an extra word-list entry or a disabled check in the next standard-set version. This is how the review gets more accurate over time without AI.

#### As built (slice 6, October 19, 2026)

- **Where:** CTL only. `GET /standards/feedback` is shown on the Standards page as "How instructors respond to checks".
- **What is counted:** guide marks, not everything the checks found. Running every course's guide to get that denominator would be expensive and would mix in courses nobody has opened. For each check the view shows Not-applicable and Done marks, the share set aside, and the number of courses. The check is read from the place key, whose second segment is always the check code (`GuideVocabulary.CheckCodeOf`), so no schema change was needed. Reopened marks are deleted and so not counted.
- **Review this check:** flagged at 3 or more Not-applicable marks that are at least 50% of the check's marks. Flagged checks sort first, then by Not-applicable count.
- **Privacy and access.** Counts are tenant-wide and name no one. Reasons are instructors' own words about a course, so they follow the course ACL: up to 10 recent reasons per check, only from courses the viewer can access, shown with the course title and date but no author. Others are only counted ("N more from courses you cannot open"). The view carries a scope and caveats, as ADR 008 reports do.
- **Not built:** turning a flagged check into a draft change automatically. The CTL makes the change in a new standard-set version as usual.

## Consequences

- `DeterministicChecks.Run` and `CourseChecks.Run` keep their signatures (with an optional profile), so every existing caller and test is unchanged.
- Check codes are stable identifiers stored on findings and rubric criteria. Renaming one is a migration, not a refactor.
- Each finding keeps the check's specific recommendation (for example, which link and why). The standard set's guidance is shown alongside it as general context.
- Deterministic rules will produce false positives (for example a legitimate link whose text is "Download"). The guide's Not-applicable reasons and the slice 6 feedback view are the mitigation. No check claims certainty it lacks.

## Slices and status

| Slice | Scope | Status |
| --- | --- | --- |
| 1 | Check engine: registry, aspects, parameters, profiles, validation; existing checks ported with no behaviour change | **Done** (October 19, 2026) |
| 2 | Standard sets: versioned draft/publish, D1/D4 selection, guidance, CTL settings screen, preview, audit | **Done** (October 19, 2026) |
| 3 | New analyzers and evidence capture | **Done** (October 19, 2026) |
| 4 | Grouping, priority, quick wins, strengths | **Done** (October 19, 2026); weights not yet CTL-configurable |
| 5 | Instructor improvement guide (Phase 9) | **Done** (October 19, 2026); see "As built" for what is deferred |
| 6 | False-positive feedback view for CTL | **Done** (October 19, 2026) |

### Slice 1 verification

- `dotnet build`: zero warnings or errors.
- SQLite suite: 135 passed, 0 failed. The 127 existing tests are unmodified, including the exact finding-order assertions. The 8 new tests are in `ReviewEngineUnitTests`: registry integrity and valid defaults; the default profile equals no profile; disabled checks and severity overrides; word-list parameters for link, start and naming checks; literal (non-regex) keyword matching; and validation of unknown checks and settings, severities, ranges, list size and entry length.
- No migration, API or UI change in this slice, so the PostgreSQL and browser suites were not re-run.

### Slice 2 verification

- `dotnet build`: zero warnings or errors. TypeScript and Vite production build: passed.
- SQLite suite: 144 passed, 0 failed. Native PostgreSQL 17 suite (`scripts/Test-Postgres.ps1`, applying the new `StandardSets` migration and trigger): 144 passed, 0 failed.
- New tests:
  - `StandardSetTests` (integration):
    - the full draft → edit → publish lifecycle, with stale-revision and immutability conflicts
    - EF and PostgreSQL immutability
    - the audit trail
    - instructor 403, anonymous 401 and missing-CSRF 400
    - validation of names, scopes, duplicate scopes (case-insensitive), missing or unknown checks, severities, list sizes, guidance, required-but-disabled checks and notes
    - D1 selection across four scopes, with a draft-only set ignored
    - classification permissions (owner 403, CTL without access 404) and audit
    - reviews pinning v1 while v2 changes a severity, disables a check and edits guidance; the published v1 review is unchanged and the owner sees only the guidance for its confirmed finding
    - preview counts and access
  - `StandardSetUnitTests`: selection order, scope keys, checks a version does not mention, JSON round-trip.
  - Every check now has non-empty default guidance, and the defaults validate.
- Headless Edge (`scripts/Test-Browser.ps1`): 9 of 9 specs pass. The new `standards.spec.ts` covers:
  - the CTL opening the read-only seeded set
  - creating a department set, turning a check off and raising a severity
  - saving, previewing against WEB 101 and publishing
  - classifying the course into the set and back
  - the owner seeing the course's standards with no classification controls and no Standards tab

  `import.spec.ts`'s course mock gained a `/standards` case, the same fix ADR 006 recorded for `/reviews`.
- **Browser runner fix.** Running the browser suite on the commit before this slice showed that 5 of its 8 specs already failed: reports, review, sharing, support and task. `Test-Browser.ps1` seeds with `--seed`, which by then also loaded the extended demo data. Those journeys assume the minimal baseline (an unshared DES 120, an unreviewed WEB 101 assignment). The runner now passes `--seed --baseline-only`, which skips the extended data. The "8 of 8" reported for Phase 6 predates the extended seed.

### Slice 3 verification

- `dotnet build`: zero warnings or errors. TypeScript and Vite production build: passed.
- SQLite and native PostgreSQL 17 suites: 157 passed, 0 failed each.
- New tests:
  - `ContentCheckUnitTests`: text measures, Bloom parsing, and every new page and assignment check, including unpublished pages, missing point metadata and missing text
  - `CourseDesignCheckUnitTests`: overview and assessment per module, a configurable item limit, light and heavy workload, the fewer-than-three-modules case, due-date clustering and its limitation, all three outcome checks, and version 1 structure limitations
  - `ContentCheckTests` (integration): a new draft adopts all 36 checks; the preview shows the new outcome and instruction findings against v1's zero; a review under the published v2 proposes the short-instructions finding with its guidance and no text limitation
- Existing unit tests for the original checks now pin the original 19 checks explicitly (`StandardSetContent.InitialDefaults()`); their expectations are unchanged. The standard-set preview test now also asserts that checks a version does not mention report zero under it.
- Full extended `--seed` run twice against a throwaway PostgreSQL database: both runs succeed; both demo sets have published v1 and v2; all 11 seeded reviews stay pinned to a v1.
- Headless Edge: 9 of 9 pass. `standards.spec.ts` now also edits a number threshold on a new check. The run used a scratch-built API with its own content root, because a developer instance on port 7143 held the normal build output; the steps were otherwise those of `scripts/Test-Browser.ps1`.
- **Correction (found later the same day):** in Development, ASP.NET Core serves static files through the build's static-web-assets manifest. That manifest pointed at `src/TeachWell.Api/wwwroot`, which still held the slice 2 frontend, so this run exercised the slice 3 API with the slice 2 frontend. The slice 3 frontend changes were small: the rubric editor's check-code lists and decimal steps on number inputs. The follow-up run removed that manifest from the scratch build and served the current frontend: 10 of 10 pass.

### Slices 4 and 5 verification

- `dotnet build`: zero warnings or errors. TypeScript and Vite production build: passed.
- SQLite suite: 190 passed, 0 failed. Native PostgreSQL 17 suite, applying the new `GuideProgress` migration: 190 passed, 0 failed.
- New tests:
  - `GuideUnitTests`:
    - every check has an effort estimate, and every area has an introduction
    - grouping and ranking: quick fixes early in the course outrank a long rewrite later, and required steps come first while finished ones come last
    - strengths appear only where there was something to check
  - `GuideTests` (integration):
    - the owner gets a ranked guide with guidance
    - invalid marks are rejected: no reason, an unknown status, an unknown place
    - a Not-applicable reason is visible to a shared reviewer, who cannot mark (404); an unshared course is 404
    - a Done mark from before the latest import is flagged "still found"
    - fixing the issue (adding a competency) resolves the step, counts the mark as fixed and lists the strength
    - audit entries
- Headless Edge: 15 of 15 pass. The new `guide.spec.ts` covers the area introduction, step guidance, asking the CTL for help, and setting a step aside with a reason. `import.spec.ts` gained an empty-guide fixture: its catch-all course mock otherwise answered the guide request with a course list.
- Screenshots of the full demo data at desktop and phone widths were checked by eye. That caught area cards whose text was centred by the global button style; this was fixed.

### Slice 6 and reviewer-findings verification

- SQLite suite: 192 passed, 0 failed. The guide tests also pass on native PostgreSQL 17 (7 of 7); this change has no migration.
- New tests:
  - `Reviewer_findings_from_published_reviews_appear_in_the_guide`:
    - a draft review contributes nothing
    - after publishing, the automated finding is attached to the guide's own place and the step is flagged
    - the reviewer's manual finding becomes a note linking to the review
  - `Ctl_sees_which_checks_instructors_set_aside_with_reasons_only_from_courses_it_can_open`:
    - counts across two courses
    - the reason from the course not shared with the reviewer is withheld and only counted
    - an instructor-only account gets 403
- Headless Edge: 15 of 15 pass. `guide.spec.ts` now ends with the CTL opening Standards and reading the instructor's reason. The full demo data was checked by eye: four steps on WEB 101 carry "Confirmed by CTL".

### CTL resources on guide steps verification

- SQLite suite: 193 passed, 0 failed. Native PostgreSQL 17 suite, applying the new `CheckResourceLinks` migration: 193 passed, 0 failed.
- New test `Ctl_links_articles_and_workshops_to_a_check_and_instructors_see_them_on_its_steps` covers:
  - linking an article and a workshop
  - rejecting duplicate, unknown-check, both-or-neither and nonexistent-resource links
  - instructor 403
  - the guide showing both, and the workshop registration status
  - hiding a draft article and a started workshop, with the CTL list explaining why
  - unlinking, 404 on a repeat, and audit counts
- Headless Edge: 16 of 16 pass. The new `guide-resources.spec.ts` has the CTL link the alt-text article on the Standards page, an instructor read it inside a DES 120 guide step, and the CTL unlink it.
- The full demo seed run twice against a throwaway PostgreSQL database leaves exactly 3 links. A screenshot of WEB 101's first step shows the article opened in place.
