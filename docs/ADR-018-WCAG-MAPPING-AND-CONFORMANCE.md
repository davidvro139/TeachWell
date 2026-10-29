# ADR 018: WCAG criterion mapping and the accessibility conformance report

Accepted October 28, 2026. This ADR covers item 1 of the [roadmap](ROADMAP.md), plus the one design check from item 5 that needed no new evidence.

## Goal

A college needs to document its accessibility remediation work, not only measure it. The ADA Title II rule requires WCAG 2.1 Level AA conformance for public institutions by April 26, 2027 (extended from April 2026 by an interim final rule published April 20, 2026), and what an institution must be able to show is a plan, measurable progress, and a record of what was fixed and when.

TeachWell already held almost all of that and could render none of it. Content snapshots, published reviews and endorsements are immutable and dated; the audit trail records every decision. What was missing was the vocabulary that makes those records legible to someone asking a conformance question: which success criterion does any of this relate to?

The comparison with Anthology Ally that produced the roadmap sharpened the point. Ally produces a score per file and per course. A score is a measurement, not evidence of a process: it does not say who fixed what, when, under which standard, or who signed it off. That gap is the one piece of this problem TeachWell is already built to fill.

## Constraints

- **Never claim conformance.** TeachWell's own framing (REVIEW_FRAMEWORK.md, ADR 008) is that no score is presented as compliance or certification. A report organized by success criterion invites exactly that misreading, so resisting it is the central design problem here, not a disclaimer to add at the end.
- **No second source of truth.** The instructor's improvement guide already decides what is open, fixed or not applicable. A report that recomputed any of that could disagree with the guide, and then neither would be trustworthy.
- **A criterion is not a setting.** The CTL tunes thresholds, severities and guidance. It must not be able to reassign which criterion a check relates to, because that would make the report's vocabulary mean something different at each institution.

## Design

### 1. The mapping

`Wcag` (in `src/TeachWell.Domain/Wcag.cs`) holds a `WcagCriterion` per referenced criterion — number, name and level — and a table from check code to criteria. `ReviewCheck.WcagCriteria` exposes it, exactly as `ReviewCheck.Guidance` exposes `DefaultGuidance`.

The reference is **WCAG 2.1 Level AA**, because that is the standard the Title II rule names. Every criterion referenced is worded identically in WCAG 2.2, so the mapping holds for either version, and the guidance text that already cited "WCAG 2.2" was left alone.

Because a criterion is a fact about a check and not a CTL setting, it lives on the check and **not** in `StandardCheckSetting`. It therefore never enters a published version's `ChecksJson`. That has three consequences worth stating: no migration was needed, published standard sets are unaffected, and a correction to the mapping applies to past reports as well as future ones. The last is the right behaviour — the mapping records what a check *is about*, which does not change retroactively — but it does mean the mapping is not versioned, unlike almost everything else in the review framework.

Five criteria are referenced, covering ten checks:

| Criterion | Level | Checks |
| --- | --- | --- |
| 1.1.1 Non-text Content | A | `IMG_ALT_MISSING`, `IMG_ALT_FILENAME`, `IMG_ALT_EMPTY` |
| 1.3.1 Info and Relationships | A | `HEADING_EMPTY`, `HEADING_SKIPPED_LEVEL`, `TABLE_HEADERS_MISSING`, `LIST_TYPED_AS_TEXT` |
| 1.4.1 Use of Color | A | `COLOR_ONLY_REFERENCE` |
| 2.4.4 Link Purpose (In Context) | A | `LINK_EMPTY`, `LINK_VAGUE` |
| 2.4.6 Headings and Labels | AA | `HEADING_EMPTY` |

**Checks that relate to no criterion carry none.** TeachWell's course-design checks — workload, pacing, module structure, alignment, reading level — are about teaching, not conformance. `PAGE_LONG_NO_HEADINGS` is a deliberate example: missing headings on a long page is a real readability problem and a best practice, but it is not a failure of any success criterion, so claiming 1.3.1 for it would inflate the report. `Wcag.For` returns an empty list rather than throwing, because having no criterion is the normal case.

A unit test enforces both directions: every check whose aspect is Accessibility must map to at least one criterion, and every check whose aspect is not must map to none. That is the guard that stops a new accessibility check silently dropping out of the report.

### 2. The conformance report

`Conformance.Evaluate` (in `src/TeachWell.Domain/Conformance.cs`) is a pure projection of per-check outcomes onto criteria. `ConformanceService` supplies those outcomes from `IGuideService.GetAsync` and adds nothing of its own — the guide applies the course's standard set, enforces course access and records the oversight figures, and the report reads what it produced. Any disagreement between the two is a bug in the projection, not a second opinion about the course.

**The three states are deliberately not "pass" and "fail":**

| State | Meaning |
| --- | --- |
| `Findings` | At least one place related to this criterion is open. |
| `NoFindings` | Every related check that ran found nothing, or its places are fixed or marked not applicable. |
| `NotChecked` | No related check ran: the standards do not enable one, or the course has no content it applies to, or the snapshot lacked the evidence it needs. |

`NoFindings` avoids the word "met" because an automated check can only show that an issue was found, never that a criterion is satisfied. `NotChecked` is reported as its own state rather than folded into either, because the three reasons it arises are all cases where the report has nothing to say — and a tool that reported silence as success would be worse than no tool.

Mapping from the guide to these states uses a distinction the guide already draws carefully. A check appears as a **step** when it found something, and as a **strength** when it ran against content it applies to and found nothing — `ImprovementGuide.Strengths` already requires `Applies(coverage)`, so "this course has no images" never counts as evidence about alt text. A check in neither did not run, and becomes `NotChecked`.

**Partial coverage is stated, not hidden.** `CriterionState.ChecksRan` against `Checks.Count` gives `FullyCovered`, so a `NoFindings` row backed by one check out of four reads differently from one backed by all four. The report also states how many of the reference standard's 50 Level A and AA criteria it does not cover at all, so the five listed cannot be mistaken for the whole standard.

Every rendering carries `Conformance.Caveat`, and both dates: when the report was produced, and when the content it describes was captured. The latest endorsement's existing `Wording` is included when there is one, which is what ties the figures to a dated CTL decision.

**Endpoints.** `GET /courses/{id}/conformance` and `/conformance/export`, in the same authorization group as the guide and requiring the same course access, because the report is a projection of it. The CSV puts the per-criterion rows first and the scope, both dates, the applied standards, the endorsement and every limitation underneath, so a downloaded file cannot be circulated as a bare compliance claim.

### 3. Module deadlines out of order

One check from roadmap item 5 needed no new evidence and was built here: `MODULE_DUE_DATES_OUT_OF_ORDER` (Assessment, Medium). Students work through a course in module order, so its deadlines should advance in module order too. When a later module's work falls due before an earlier module's, the first thing students meet on the calendar is work the course has not prepared them for.

Only consecutive **dated** modules are compared, so a module with no dated assessment drops out of the chain instead of breaking it. A `toleranceDays` parameter (default 0, range 0–30) allows deliberate overlap. The guidance names the common cause — dates carried over from a previous term — and tells instructors that a long project assigned early and due late is legitimate and can be marked not applicable.

This brings the catalogue to 39 checks.

## As built (October 28, 2026)

- **Domain.** `Wcag.cs` (criteria, the check map, reverse lookup) and `Conformance.cs` (the projection, the three states, the caveat, the out-of-scope count). Both are pure and have no dependency on EF or the Application layer.
- **`ReviewCheck.WcagCriteria`** added as a virtual property delegating to `Wcag.For(Code)`.
- **Exposed** on `CheckCatalogEntryDto` for the CTL's standards screen, and on `GuideStepDto`, so an instructor sees which criterion a fix is for on the step itself.
- **`ConformanceService`** in `Infrastructure/Reports`, registered in `Program.cs`, reading `IGuideService` and the latest `Endorsement`.
- **No migration.** The report stores nothing and the mapping is code, not data.
- **Deferred:**
  - **The report screen.** The endpoints and the CSV export work; the React view is not built, so the report is currently reachable only through the API.
  - ~~**College-wide conformance.**~~ **Built October 28, 2026 — see the amendment below.**
  - **Criteria reachable only through imported findings** — contrast, captions, document language. Added when roadmap item 2 is built, not before, so the catalogue never lists a criterion nothing can report on.

### Verification

- SQLite suite: 244 passed, 0 failed (231 before this change; 13 new). No migration, so the PostgreSQL run is unaffected.
- `ConformanceUnitTests` (8 tests, no database):
  - every Accessibility check maps to a criterion, and every non-Accessibility check maps to none
  - mapped criteria are all in the catalogue, every catalogued criterion has at least one check, all are Level A or AA, and they are listed in numeric order
  - an unmapped check and an unknown code return empty rather than throwing
  - nothing having run yields `NotChecked` for every criterion and `NoFindings` for none — the case a careless implementation would report as clean
  - a check present but not run counts the same as one absent, and contributes no places
  - open places give `Findings`; marking them all Done gives `NoFindings`
  - not-applicable places are reported separately and do not count as findings
  - a criterion covered by one of its four checks reports `NoFindings` with `FullyCovered` false
  - the caveat says "not a conformance claim" and never says "compliant", and the out-of-scope count is above zero
- `ConformanceTests` (4 integration tests):
  - the report lists every criterion in order; the state counts sum to the criterion count; the scope and date are present
  - **its figures are asserted equal to the guide's**, criterion by criterion, which is what stops the two drifting apart
  - guide steps in the Accessibility aspect all carry criteria
  - marking every open place for a criterion Done moves it from `Findings` to `NoFindings`, with the places arriving in `DonePlaces`
  - a shared CTL reviewer reads it; an unshared course is 404; an Academic administrator gets 403, since that role never opens course content
  - the export is CSV carrying every criterion number, both dates, the manual-review count and the caveat; the export honours the same course access
- **Not verified: the SPA.** Node is not installed on the machine this slice was built on and `src/TeachWell.Web/node_modules` is absent, so neither `tsc --noEmit` nor the Playwright specs were run. The one SPA change — the `wcagCriteria` field on the guide step's `Step` interface and the "Relates to:" line that renders it — is additive and compiles against the API shape the integration tests assert, but it has not been type-checked or seen in a browser. It needs both before this slice is considered finished.
  - What the integration tests do establish about that shape: `wcagCriteria` round-trips through the real serializer on every guide step, Accessibility steps always have at least one criterion and other steps always have none, and `reference` — a computed get-only property, the field most likely to be dropped in serialization and the one the screen actually renders — arrives correctly formed.
- `ContentCheckUnitTests` gained one test for the new check: a backwards module is flagged against the previous dated module with the day count and both dates in the evidence; deadlines that advance are not flagged; an undated module drops out of the chain without breaking it; fewer than two dated modules yield nothing; and the tolerance suppresses a configured overlap.

### Known limitations

- **The mapping is not versioned.** Correcting it changes how past reports read. This is intended, but it is the one place in the review framework where a later edit affects an earlier assessment, and it is called out here so that is a decision rather than an oversight.
- **Five criteria out of fifty.** The report covers what TeachWell's checks relate to. It is a record of remediation work, not a conformance audit, and the caveat says so wherever it appears.
- ~~**No report screen yet**~~ **Built October 29, 2026 — see the amendment below.**
- ~~**The SPA changes are untested**~~ Node is now installed under `.local`; `tsc`, the production build and the Playwright suite all run.

## Amendment: media capture and captions — October 28, 2026

Roadmap item 5a. Captions were the largest accessibility gap TeachWell could close without the Ally integration, and the reason it could not was that media was invisible to the checks: `ContentEvidence` recorded images, headings, links and tables, and `CanvasText.Normalize` stripped `iframe`, `object` and `embed` **before** building evidence, so an embedded video left no trace at all.

### Capturing media without weakening the strip

Media facts are now read from the document **before** the strip runs. Nothing about the security posture changes: iframes, objects and embeds are still removed, still never rendered, and the preview still refuses to reproduce them. Only facts survive, and only the facts the existing evidence design already allows — no markup, no `src`, and at most an external host name, exactly as `LinkEvidence` keeps a host and discards the URL because "full URLs can carry query-string secrets".

`MediaEvidence(Index, Kind, Provider, Title, HasCaptionTrack)` is captured for `video`, `audio`, `iframe`, `object`, `embed` and the legacy `a.instructure_inline_media_comment` that Canvas still emits for older recordings. `ContentEvidence.Media` is **nullable with a null default**, following `CourseStructureEvidence.Outcomes`: null means media was not captured, which is deliberately distinct from an empty list meaning the page has none. Evidence version is now 2; the version field is informational, and `Media is null` is what the checks actually test.

### Two checks, because the page proves two different things

The project's rule is that a finding states only what the evidence supports (ADR 005), and media splits cleanly in two:

| Check | Severity | Fires when | Why it is worded as it is |
| --- | --- | --- | --- |
| `MEDIA_CAPTIONS_MISSING` | High | An HTML `video` or `audio` element has no `track` of kind `captions` or `subtitles` | The markup settles it. This page has no captions. |
| `MEDIA_CAPTIONS_UNCONFIRMED` | Medium | An `iframe`, `object` or `embed` is present | The page cannot settle it. Whether a YouTube or Studio video is captioned is a property of the hosted media, which TeachWell does not fetch. |

The second check **asks rather than asserts**. It says plainly that TeachWell could not check, tells the instructor to play the media, and offers "not applicable" for an embed that is not video or audio at all. Inferring captions from the host would produce confident findings that are wrong, which is worse than asking — and the Proposed/Confirmed model already exists for exactly this.

Both relate to WCAG 2.1 **1.2.2 Captions (Prerecorded)**, Level A, now in the catalogue. `MEDIA_CAPTIONS_MISSING` is estimated at 30 minutes per place, far above the text checks, so captioning is never ranked as a quick win. It claims the strength "Every video and audio player carries captions" only where the course has media, which needed a `Media` count on `GuideCoverage`. `MEDIA_CAPTIONS_UNCONFIRMED` has no strength: "nothing to confirm" is not an achievement, and a confirmed step is the instructor's word rather than a check's finding.

This brings the catalogue to 41 checks.

### Demo data

WEB 101's welcome page gained an uncaptioned `video` and a titled `iframe`, so both checks have a worked example. The Online standard set is now built with `StandardSetContent.WithNewChecks`, so it includes the checks added since the college-wide v1 was published. That keeps the demo honest about ADR 011 rather than papering over it: the college-wide fallback still runs the original set and reports 1.2.2 as `NotChecked`, while the Online set runs the newer one and reports findings.

### Verification

- SQLite suite: 252 passed, 0 failed (244 before this amendment; 8 new). No migration.
- `MediaCheckUnitTests` (7 tests):
  - all five player forms are captured from one page in document order, with the right kinds, and `HasCaptionTrack` true, false and null in the right places
  - the strip still removes the iframe, and neither the `src` nor a query-string token reaches the serialized evidence; `Preview` of an iframe is still null
  - an empty page and a text-only page capture an **empty** media list, not null — the distinction the whole nullable field exists for
  - a trackless player is a High definite finding naming the player; audio is advised to add a transcript rather than a video captions track; a captioned player is flagged by neither check
  - an embed produces the Medium confirmation finding, names its host, and never claims captions are missing
  - version 1 evidence reports the "predates media capture" limitation, finds nothing, and keeps `Media` null through a JSON round-trip
  - both checks map to 1.2.2, captioning is estimated at 30+ minutes, the strength is claimed only when `Media > 0`, and the confirmation check has no strength
  - neither check runs under `StandardSetContent.ToProfile(InitialDefaults())`, and a new draft picks both up at their defaults
- `ConformanceTests` gained the end-to-end case: under the extended demo standards, WEB 101 is Online, both media checks appear on the guide against 1.2.2, and the conformance report shows that criterion with findings, fully covered, and exactly two open places — seeded HTML through capture, the checks, the guide and the report.
- The baseline-seed case now asserts 1.2.2 is `NotChecked` with zero checks run, which pins the ADR 011 gating: a check added after a version was published does not reach courses governed by it.

**A test of mine was wrong before the code was.** The first version built a `CheckProfile` directly from `InitialDefaults()` and expected the media checks not to run. They ran, because `CheckProfile.For` falls back to a check's own enabled defaults for any code the profile does not mention. The production path is `StandardSetContent.ToProfile`, which disables unmentioned checks explicitly. The behaviour was right; the test was reaching past it. Worth recording, because anyone hand-building a profile in a future test will hit the same trap.

### Correction: embedded tools are not media — October 28, 2026

The first version of this amendment asked about captions for **every** iframe, object and embed. That was wrong, and learning that the college delivers its syllabi through Simple Syllabus — an LTI tool, embedded as an iframe (ADR 008) — is what exposed it. A test reproduced the defect before it was fixed: a Simple Syllabus launch produced "Embedded media needs a captions check". The same would have happened for embedded Google Docs and Forms, H5P, Padlet, publisher tools and every other LTI launch, which in a real Canvas course is most of the iframes on the page.

A captions step on every embedded tool is worse than no check, because it teaches instructors that the guide cries wolf.

So embeds are now classified at capture time, from the source, before the source is discarded:

| Kind | What it is | Captions |
| --- | --- | --- |
| `Video`, `Audio` | An HTML player; the markup says so | Asked, and settled by the `track` element |
| `Embed` | An iframe or object whose source is recognised as a media player | Asked, as a confirmation |
| `Tool` | Anything else embedded: an LTI launch, a document viewer, a form, a widget | **Never asked.** Reported as a limitation: "N embedded tools were not inspected" |

Recognition uses a list of media hosts plus the paths Canvas serves its own uploaded media from, since Studio and My Media embeds are same-origin and so have no external provider. Matching is on **domain labels**, not substrings: `college.hosted.panopto.com` is a player, `youtube.com.evil.test` is not. A test pins that distinction, because substring matching would have got it wrong.

The list is a recognition aid, not a gate. An unrecognised embed is still recorded and still reported as not inspected — it is never silently dropped, and the limitation tells a reviewer to check it if it does play media. `Tool` embeds also do not count toward the guide's media coverage, so a course whose only "media" is an LTI launch cannot earn the captions strength.

### Still deferred

- ~~**UDL multiple means.**~~ **Built October 28, 2026 — see the amendment below.**
- **Widening the media-host list.** It covers the common providers and will miss self-hosted or niche ones, which then read as `Tool`. The limitation keeps those visible rather than silent, and the list is the right thing to extend as real courses turn up hosts it does not know.
- **A transcript check.** A transcript beside a video is as good as captions for some needs, and detecting "a link near the player whose text mentions transcript" would reduce false confirmations. Not attempted: proximity in a flattened text snapshot is not reliable enough to act on yet.


## Amendment: the college-wide rollup — October 28, 2026

The deferred item above. Oversight now carries one row per WCAG success criterion: how many of the courses in scope have open work for it, and how many open places in total. It respects the existing department and program filters and travels with the CSV export.

**No new data and no migration.** `CourseGuideRecord` already stores `OpenByCheckJson`, a map of check code to open places per course per day, and ADR 018 already maps every check to its criteria. The rollup is those two facts composed: for each criterion, sum its checks' open places per course, then count the courses above zero. A course is counted once per criterion however many of its checks are open, so the figures are comparable across rows.

### What it cannot show, and why that is stated rather than papered over

Guide records store only *open* places. A check that ran and found nothing leaves no entry, and neither does a check that never ran. So at the college level, **a course with no open places for a criterion is indistinguishable from one where that criterion's checks did not run.**

That makes `CoursesAffected` a floor on the outstanding work, never a count of courses that satisfy the criterion. The scope caveat says exactly that, and points to the per-course conformance report, which does draw the distinction because it sees the course's strengths and limitations as well as its findings. The rollup also states how many of the standard's 50 Level A and AA criteria it covers at all.

Closing that gap properly would mean recording per-criterion coverage on `CourseGuideRecord` — a new field, a migration, and a change to the record writer. It is worth doing, and was not done here because a migration cannot be verified on this machine (no PostgreSQL). The honest rollup is useful meanwhile: "1.1.1 has 318 open places across 29 of 42 checked courses" is the triage and reporting figure a college actually asks for.

### Verification

- SQLite suite: 273 passed, 0 failed (269 before this amendment; 4 new).
- `OversightConformanceTests` (4 integration tests):
  - every mapped criterion appears in criterion order; each row's checks all resolve in the registry; `CoursesAffected` never exceeds the courses checked and never exceeds `OpenPlaces`
  - **the rollup agrees with the per-course report**: every criterion with open places on WEB 101 has a college total at least as large, which is what stops the two views drifting apart
  - marking every open place for a criterion done on one course lowers the college figure by exactly that many places
  - the export carries the rollup, the scope, the course counts, the generation time and every caveat
  - a course never checked contributes nothing rather than counting as clean, asserted with zero guide records in the database
- **One existing test changed.** `OversightTests.Oversight_filters_by_program_and_exports_safe_csv` asserted the filtered export had exactly two lines. That assertion's real intent was "only one course row in a filtered export", so it now asserts the course row is followed immediately by the rollup's own header, and that there is one row per criterion — intent preserved, and no longer coupled to a total the caveats can change.
- **Not verified: the SPA.** The rollup table added to `Oversight.tsx` has not been type-checked or seen in a browser, for the usual reason (no Node here).


## Amendment: modules that offer only media — October 28, 2026

The last deferred item from roadmap item 5. Course-structure evidence is now **version 4**: each content item carries the number of recognised video and audio players in it, counted from the page evidence the media amendment above already captures. `Media` is nullable, so a snapshot from before version 4 reports a limitation rather than being read as zero. No migration — the structure evidence is JSON.

`MODULE_MEDIA_WITHOUT_TEXT` (Content, Low) fires when a content module has at least one player and fewer than a configurable number of words of text (200 by default).

### The asymmetry is the design

This is the defensible half of a "multiple means of representation" check, and the half that was left out matters as much as the half that was built.

**Flagged:** a module whose content is only a recording. That excludes students who cannot stream it, who are on a phone data plan, who need to search and skim rather than scrub through forty minutes of video, or for whom the captions are imperfect. It is also the usual sign that a lecture recording was dropped in without a transcript.

**Deliberately not flagged:** a text-only module. In most subjects that is ordinary good teaching. A check that told instructors to add a video to every module would be giving bad pedagogical advice with TeachWell's authority behind it, and UDL does not ask for one of everything in every module — it asks for options where they matter.

A counting check ("a module should offer two of text, media and practice") was considered and rejected for exactly that reason, and because `MODULE_NO_ASSESSMENT` already covers the missing-practice case.

The check claims **no strength** in the guide: most modules legitimately have no media at all, so "every module with media also offers text" would be claimed overwhelmingly on courses the check never had anything to say about. It maps to no WCAG criterion — captions cover the conformance side; this is a design concern.

This brings the catalogue to 43 checks.

### Verification

- SQLite suite: 292 passed, 0 failed (290 before this amendment; 2 new). No migration.
- A module whose only content is a recording is flagged, naming its player count and word count; the same module with a summary page is not; **a text-only module is not flagged**, which is the assertion that pins the asymmetry; and the word threshold is the CTL's to change.
- A snapshot without media counts reports the "start a new course review" limitation and flags nothing, rather than treating null as zero and flagging every module with text in it.


## Amendment: the report screen, and why it loads lazily — October 29, 2026

`CourseConformance.tsx`, on the course page below the improvement guide. Criterion rows with level, state and open/fixed/set-aside counts; the scope caveat above the figures; both dates; the standards applied; the endorsement; the limitations; and the CSV download.

The states are worded as what was found, never as a verdict: **Work outstanding**, **Nothing found**, **Not checked**. A partially covered criterion says so inline ("Only 1 of 4 related checks ran, so this is partial"), so a clean-looking row cannot be mistaken for full coverage.

### The section is collapsed by default, and that is a correctness decision as much as a performance one

The report is a projection of the improvement guide, so asking for it **re-runs the whole guide server-side**: the course structure is rebuilt, all 43 checks run over every element, and a guide record is written. Mounting it alongside the guide therefore made every course page do the guide's work twice.

That was measured, not theorised: the browser suite went from 3.5 minutes to 9.2 and three unrelated specs began timing out. The property that makes the report trustworthy — that it never computes anything of its own — is exactly what makes it expensive to ask for.

So the section is folded by default and fetches the first time someone opens it. `Collapsible` gained an optional `onOpen` callback for this, documented as "lets a section whose contents are expensive to produce fetch them only once someone actually looks". It keeps its children mounted under `hidden`, so an inner component could not have fetched on mount instead.

With the fix the suite runs in 2.8 minutes, below the original baseline.

### Two defects the browser suite caught that the unit tests could not

- **An unguarded render could blank the course page.** The component rendered the API response without checking its shape, and React unmounts the whole subtree when a render throws. Any unexpected body — a proxy error page, an older API — would have taken out the entire course page, not just this panel. The response shape is now validated once on arrival and anything else is treated as nothing to show.
- **`.table-scroll` only scrolled inside `.guide-feedback`.** The conformance table uses that wrapper on the course page, outside that container, so it widened the document at phone width. `large-course.spec.ts` caught it at 390px. The rule is now global, which also fixes the oversight rollup.

### Verification

- `e2e/syllabus.spec.ts` covers the criterion line on a guide step (present on an accessibility step, absent on a design step) and the oversight rollup, including that its caveat renders before any figure and that the same rows reach the CSV.
- Browser suite: 25 passed, 0 failed.
