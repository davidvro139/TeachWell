# ADR 019: Importing the Canvas syllabus, and its required elements

Accepted October 28, 2026. This ADR covers items 4a and 4 of the [roadmap](ROADMAP.md).

## Goal

The syllabus is the page students read first and return to whenever something goes wrong: a missed deadline, a needed accommodation, an unclear grade. Which elements it must contain is set by institutional policy and by accreditors, and colleges check it by hand today, syllabus by syllabus, every term.

Ally treats `application/x-syllabus` as one more content type and scores its accessibility. It never asks what is *in* it. That makes this one of the clearest pieces of ground TeachWell can hold on its own.

## Before this ADR

TeachWell could not check the syllabus because it never saw it. Neither reader read it: `CanvasCourseReader` never asked Canvas for `syllabus_body`, and `CanvasExportReader` never looked for `course_settings/syllabus.html`. The word "syllabus" appeared in the codebase only as an orientation keyword and in demo page text. The roadmap first sized this item as "small"; discovering the import gap is what corrected it to two slices.

## Design

### 1. The vocabulary problem, and why it came first

The obvious move — add `"syllabus"` to `ReviewVocabulary.ElementTypes` — would have introduced a bug, because that one array was carrying two different meanings:

```csharp
public static readonly string[] ElementTypes = ["page", "assignment", "quiz", "discussion"];
public static bool IsContent(string type) => ElementTypes.Contains(type);
```

`ElementTypes` is used both for *"has reviewable text, so run the element checks on it"* (`GuideService`, the standards preview) and, through `IsContent`, for *"is a content item placed in a module"* (`CourseStructureCapture`'s unplaced list, and `CONTENT_NOT_IN_MODULE`).

The Canvas syllabus has reviewable text but is **not** module-placeable: it belongs to the course and is reached from the course navigation. Adding it to `ElementTypes` would therefore have made `CONTENT_NOT_IN_MODULE` fire on every course that has a syllabus — telling instructors to put their syllabus in a module, which is wrong.

So the two meanings were separated:

| Set | Means | Used by |
| --- | --- | --- |
| `ElementTypes` (unchanged) | Content placed in modules | `IsContent`, structure capture, the unplaced check, rubric element reviews, criterion targets |
| `CheckedTypes` = `ElementTypes` + `syllabus` | Carries text the element checks run against | `GuideService`, the standards preview |

The syllabus is deliberately **not** reviewable element by element and is **not** a criterion target. That was the ripple the roadmap warned about: making it one would reach into published rubrics, their criteria's element types, and the review screens. Its accessibility is covered by the element checks and the improvement guide, which is where the value is; rubric-based syllabus review can be added later if the CTL asks for it, as a decision of its own.

### 2. Importing it

The syllabus is a property of the course rather than a resource with its own id, so both readers use one fixed source id, `ReviewVocabulary.SyllabusSourceId`. Elements are matched on `(Type, SourceElementId)` and nothing else has type `syllabus`, so a refresh updates the same element instead of adding a second one. No migration: `CourseElement.Type` is a string.

- **Canvas API.** `syllabus_body` is not returned by default, so the existing course request became `?include[]=syllabus_body`. Canvas omits the property for a course with no syllabus and returns null when it is empty; both are treated as "no syllabus".
- **Export.** Canvas writes the syllabus to `course_settings/syllabus.html` rather than listing it as a manifest resource, so it is read by path. A syllabus holding only markup and whitespace produces no element, so an effectively empty syllabus is not imported as content and then flagged for missing everything.

Its HTML goes through the same `CanvasText.Normalize` as any page, so it gets the same sanitization, the same evidence (including media, from ADR 018's amendment) and the same allowlisted preview.

**Not to be confused with a page called "Syllabus".** Many courses also have a wiki page named Syllabus, which is a separate element and stays one. The test fixture now distinguishes `IncludeSyllabusPage` (that wiki page) from `CourseSyllabusBody` (the course syllabus), because conflating them would make the tests meaningless.

### 3. The required-elements check

`SYLLABUS_REQUIRED_ELEMENT_MISSING` (Navigation, Medium) is an `ElementCheck` that runs only when the element type is `syllabus` — the same shape as `ASSIGNMENT_INSTRUCTIONS_SHORT`, which runs only on assignments. A course-level check was considered and rejected: `CourseCheck` reads the course structure, which does not carry the syllabus text.

It fires **once per missing element**, so each is its own place that an instructor fixes and ticks separately. Five elements ship as defaults, each a CTL-editable keyword list:

| Setting | Element |
| --- | --- |
| `accessibilityKeywords` | an accessibility statement |
| `contactKeywords` | how to reach the instructor |
| `gradingKeywords` | how grades are worked out |
| `lateWorkKeywords` | the late work policy |
| `integrityKeywords` | the academic integrity policy |

**A college that does not require an element empties its list**, and the element is then not looked for. This is why the lists are settings rather than code: the policy differs by college and the wording differs by department.

**What the check actually knows, and how the wording respects that.** This is a keyword search over the syllabus text. It can show that a topic is not *mentioned* in the words the CTL configured. It cannot show that the policy is absent, and it certainly cannot judge whether what is written is any good — a syllabus may cover late work in words no keyword list anticipates. So the finding states what it searched for and found nothing, and the recommendation offers two outs: add one of the words the CTL looks for, or mark the step not applicable and say where it is covered. The check has **no strength** in the guide, because "five keywords matched" is not evidence that a syllabus is complete, and claiming it would overstate what the check saw.

It maps to **no WCAG criterion**. It is a policy and accreditation requirement, not a success criterion, and ADR 018's guard enforces that only Accessibility-aspect checks carry criteria.

This brings the catalogue to 42 checks.

## Important limitation at this college: Simple Syllabus

**Added October 28, 2026, on learning that the college delivers its syllabi through Simple Syllabus.**

ADR 008 already recorded the relevant fact: Simple Syllabus is an external LTI tool whose content is **not part of Canvas exports or the Canvas API**, confirmed on two real exports. The same is true of the syllabus itself, not just the learning outcomes.

So at this college, what this ADR builds will largely find nothing:

- If the Canvas syllabus page is empty or unused, no `syllabus` element is imported, the check never runs, and the course gets **silence** — not a pass, but not useful either.
- If the Canvas syllabus page holds a Simple Syllabus launch, TeachWell imports an element whose text is a sentence or two of boilerplate around an embed, and would report all five required elements as missing — a page of findings about a page that is not the syllabus.

This also exposed a defect in ADR 018's media checks, now fixed there: an LTI launch was being asked about captions.

The work still stands for colleges that use the Canvas syllabus, and the check, the keyword lists and the vocabulary separation are all reusable whatever the text's source. But **this college needs a different route to the syllabus text**, and that is the next decision rather than something to assume. The options, in the order they are worth considering:

1. **Paste the syllabus**, exactly as the college already pastes its learning outcomes (ADR 008's amendment, `OutcomeListParser`, `OutcomeImport.tsx`). The instructor copies the syllabus out of Simple Syllabus, TeachWell stores it as the syllabus element's text, and the required-elements check runs on it unchanged. There is a working precedent in this codebase for precisely this problem, and it needs no institutional approval.
2. **Say so rather than stay silent.** When a course has no Canvas syllabus, or has one that is only an embedded tool, report it as a stated limitation: "this course's syllabus is not in Canvas, so its required elements were not checked". Cheap, honest, and it stops a missing syllabus looking like a clean one. Worth doing regardless of which route is chosen for the text.
3. **A Simple Syllabus API integration**, which ADR 008 already places with the Phase 12 campus integrations: it needs institution-level API access and IT approval.

Option 2 should be built either way. Option 1 is the practical answer; option 3 is the one that would eventually remove the manual step.

## Amendment: pasting the syllabus — October 28, 2026

Options 1 and 2 above are now built. The project owner chose the paste route, and kept the Canvas import for colleges that use the Canvas syllabus page.

### One syllabus per course, with provenance

There is exactly one syllabus element per course, keyed `(syllabus, SyllabusSourceId)`, whether its text came from Canvas or from the owner. The alternative — a second element for pasted text — was rejected because the check would then run twice and the course would have two syllabi.

Provenance is two new nullable columns on `CourseElement`: `SuppliedAt` and `SuppliedBy`, migration `SuppliedContent`. They are named generally rather than `PastedSyllabusAt` because "a person supplied this content rather than TeachWell importing it" is a property that could apply to other elements later.

Contorting an existing field was considered and rejected. `SourceStatus` was the tempting one, but adding a `Pasted` status would have changed how `ITEM_UNPUBLISHED` and `ITEM_UNAVAILABLE` reason about every element, to record something that is not a status at all.

**Supplied content outranks the importer**, which `CourseDataApplier` now enforces in two places:

- an element with `SuppliedAt` set is skipped when applying an import, so an all-but-empty Canvas syllabus page cannot overwrite what the owner pasted
- it is excluded from the "missing" sweep, so an import that returns no syllabus does not mark the pasted one removed

Both are tested by applying real imports over a pasted syllabus: one that carries its own syllabus, and one that carries none.

### The pasted text is treated like any import

It goes through `CanvasText.Normalize`, so it is sanitized, measured and given evidence by exactly the same code path, and it is snapshotted so reviews and the guide see it normally. That means the syllabus's own accessibility is checked too: a syllabus pasted as HTML from a web page gets its images, headings, links and media checked like any page. A test pastes markup with a script tag and an image with no alt text, and asserts the script is gone and the missing alt text is found.

Plain text is the common case, so each non-empty line becomes a paragraph, giving the checks the same line structure an import would produce. Pasted HTML is detected and passed through instead.

### Clearing a paste

`DELETE` clears provenance and hands the element back to the importer. The element and its snapshots stay, because earlier reviews and guide marks point at them and TeachWell does not rewrite history (ADR 005).

**The status becomes `Removed`.** The first implementation used `Unavailable`, and a test caught it: the guide excludes only `Removed`, so a cleared syllabus went on being checked and the course kept answering syllabus questions from a syllabus it no longer had. `Removed` is also what the applier uses for content a source stops returning, which is the same situation.

### Silence is not a pass

A course with no syllabus produces no syllabus findings, which would read as a clean syllabus. The guide now states it instead, naming the fix:

> This course has no syllabus in TeachWell, so the syllabus was not checked. If the syllabus is in Simple Syllabus or another tool outside Canvas, paste its text on the course page.

The limitation appears only when the syllabus check is enabled, so a college that does not use it is not told about a gap it does not care about.

### Verification

- SQLite suite: 269 passed, 0 failed (260 before this amendment; 9 new).
- `SyllabusPasteTests` (5 integration tests):
  - the owner pastes; the source becomes `Pasted` with who and when; one element not two; the imported text is replaced; snapshots accumulate; the action is audited
  - a CTL reviewer with course access reads the state but **not the text** and cannot paste (404); an unshared course is 404 even to read
  - a pasted syllabus survives an import that carries its own syllabus, and an import that carries none does not mark it removed
  - clearing returns the course to "no syllabus", a second clear is 404, and a later import refills it from Canvas
  - empty, whitespace-only, markup-only and over-length pastes are rejected; pasted HTML is sanitized and its own evidence captured
  - the pasted syllabus is what the check runs against: a complete one clears the step, one missing the accessibility statement reports exactly that, and removing it replaces the step with the stated limitation
- **Not verified: the migration.** PostgreSQL is not installed on this machine, so `SuppliedContent` has not been applied to a real database. The SQLite suite uses `EnsureCreated` and therefore never exercises migrations. The migration is two additive nullable columns and the model snapshot is consistent, but it needs a PostgreSQL run before deployment.
- **Not verified: the SPA.** Node is not installed here, so `tsc --noEmit` and Playwright have not run. This amendment adds `SyllabusPaste.tsx` and mounts it above the improvement guide on the course page, rebuilding the guide when the syllabus changes. It is modelled closely on `OutcomeImport.tsx`, but it has not been type-checked or seen in a browser, and it is the part of this work most likely to need correction.

## As built (October 28, 2026)

- **Domain.** `SyllabusChecks.cs`; `ReviewVocabulary.SyllabusType`, `SyllabusSourceId`, `CheckedTypes` and `IsChecked`; `CourseStructureEvidence.Label` gained "Syllabus".
- **Readers.** `CanvasPatClient.ReadCourseAsync` requests and imports `syllabus_body`; `CanvasExportReader.Parse` reads `course_settings/syllabus.html`.
- **Guide and preview** select on `CheckedTypes`. Everything else still selects on `ElementTypes`.
- **Demo.** Both baseline demo courses have a syllabus. WEB 101's omits the accessibility statement, giving the demo one open required element; DES 120's covers all five, so the step is finished there.
- **No migration.**
- **Deferred:**
  - **"This course has no syllabus at all."** Genuinely valuable and deliberately not built here: it needs the course structure evidence to record whether a syllabus element exists, which is another evidence version. Right now a course with no syllabus produces no syllabus element and therefore no findings — silence, not a pass. Worth closing.
  - **Rubric-based syllabus review**, for the reason in section 1.
  - **Judging the content** of a policy rather than its presence. Out of reach of a keyword search, and the guidance says so.

### Verification

- SQLite suite: 260 passed, 0 failed (252 before this ADR; 8 new). No migration.
- `SyllabusCheckUnitTests` (6 tests):
  - a complete syllabus produces nothing; removing one section reports exactly that element; a bare syllabus reports all five as five distinct places
  - the finding names the element, states what was searched for, and offers the "already there in different words" route
  - the check runs **only** on the syllabus — the same bare text as a page, assignment, quiz or discussion produces nothing
  - emptying a keyword list opts the college out of that element, and empty lists are valid settings rather than a validation error
  - with no text supplied, it reports a limitation instead of findings
  - the vocabulary decision is pinned directly: the syllabus is in `CheckedTypes` and `IsChecked`, but not in `ElementTypes`, not `IsContent`, and not a criterion target
  - an export carries the course syllabus separately from a wiki page called "Syllabus"; its own images are checked; and neither a missing nor a whitespace-only syllabus file produces an element
- `SyllabusTests` (2 integration tests):
  - the syllabus is imported with the fixed source id and no module, and **`CONTENT_NOT_IN_MODULE` does not fire on it** — the regression this slice turns on, asserted against the real seeded standards where that check is enabled
  - under the extended demo standards, WEB 101's missing accessibility statement becomes a guide step with one open place labelled "Syllabus: Syllabus", no module, and no WCAG criterion
- **Three existing tests changed, all element counts.** `ApiTests` (6→8 elements and snapshots), `StandardSetTests` (2→3 elements checked in the preview) and `CourseReviewTests` (3→4 `ElementCount`). Each is a correct consequence of every demo course now having a syllabus, not a regression; each now carries a comment saying why the number is what it is.
- **Not verified: the SPA.** Node is still not installed on this machine, so `tsc --noEmit` and the Playwright specs have not run. This slice changes no SPA code, but a syllabus now appears in guide steps and course element lists, so those screens should be looked at before the work is called finished.


## Amendment: reading the syllabus without editing it — October 29, 2026

The project owner pointed out that once a syllabus is pasted there was no way to *see* it: the panel offered "Edit pasted syllabus", so reading what was stored meant opening the editor. That was wrong, and it hid a second gap.

- **A collapsed "Show syllabus"** in the panel renders the stored text read-only. Owner only, because the API returns the text only to the owner.
- **`CONTENT_TYPES` in `CourseContent.tsx` excluded `syllabus`**, so under Course content the syllabus showed as plain text while pages got the formatted preview. It carries the same allowlisted preview as any page, whether imported or pasted, and the previews endpoint never filtered by type — this was a client-side omission alone.

### Verification

`e2e/syllabus.spec.ts` drives the panel end to end on DES 120: where the syllabus came from and its word count, paste, cancel leaving it untouched, a whitespace-only paste disabling Save, save with attribution, re-opening the editor showing the stored text, **surviving a return to the course** (a fresh load and fetch), removal, and pasting again.

It runs on DES 120 rather than WEB 101 because twelve specs assert against WEB 101's guide and readiness and this suite shares one database sequentially. Two bugs in the spec itself were caught doing this, both worth recording as traps:

- A locator matching both the status line and the note, since each mentions where the syllabus came from.
- An assertion on a step `reports.spec.ts` removes: it sorts earlier and maps a competency on WEB 101. The missing-rubric step is used instead.
- A `page.reload()` that landed on the course list, because **this SPA keeps its view in React state, not the URL**. Navigating in again is the stronger check anyway.
