# Roadmap: course accessibility beyond TeachWell's own checks

Written October 28, 2026, after reviewing TeachWell against [Anthology Ally for LMS](https://help.anthology.com/ally-lms/en/administrators.html), the accessibility reporting tool most colleges already run on Canvas.

This document records what that comparison found, what TeachWell should build as a result, and — as importantly — what it should decline to build. It is a planning document, not a decision record: each item becomes an ADR when work on it starts.

## What the comparison found

Ally and TeachWell are not the same kind of tool, and the overlap between them is smaller than it first appears.

**Ally's unit of work is a file.** It scores every uploaded PDF, Word document, presentation and image in every course, continuously and without the instructor asking, then generates alternative formats for students (tagged PDF, HTML, ePub, electronic braille, MP3 audio, translation). It knows nothing about course design, alignment or pedagogy.

**TeachWell's unit of work is a course under review by a person.** Its 38 checks are the entrance to rubric reviews, revision tasks, readiness endorsement and oversight. Ally has no equivalent to any of that: no versioned standard sets, no immutable review snapshots, no revision workflow, no competency alignment, no endorsement trail.

### Where TeachWell is genuinely behind

| Gap | Detail |
| --- | --- |
| **Course files are never checked** | `CanvasExportReader` counts them and reports them as skipped (`"files and media are listed but not imported"`). Most of a real course's accessibility debt lives in PDFs and slide decks, so a course can pass all 38 checks while its weekly reading is an unreadable scan. |
| **No contrast ratio is computed** | `COLOR_ONLY_REFERENCE` matches phrases such as "in red". That is WCAG 1.4.1, not 1.4.3. Canvas's editor emits inline `style="color:…"` constantly and nothing measures it. |
| ~~**No media captioning check**~~ | ~~Embedded video and audio are not detected at all.~~ **Closed October 28, 2026** (ADR 018 amendment): media is captured and captions are checked. TeachWell now covers this where Ally's `HtmlCaption` only covers HTML embeds. |
| **No presence inside Canvas** | Ally shows a gauge beside every item; TeachWell requires an export and an upload. This difference, more than any check, decides whether faculty use a tool. |
| **Nothing is written back** | Read-only is a deliberate choice (ADR 003, ADR 004), but it means every fix is a context switch. |
| **Coverage depends on someone importing a course** | Ally reports on the whole LMS from day one. Oversight figures should not be read as college-wide while that is true. |

### Where Ally is behind, and will stay behind

Ally produces a score. It keeps no record of who fixed what, when, under which standard, or who signed it off. It has no concept of a course's design: workload, pacing, scaffolding, module structure, alignment to competencies. These are not gaps Anthology is likely to close, because they need a human review workflow, which is the part TeachWell already has.

That asymmetry sets the strategy below.

## Strategy

**Consume Ally rather than reimplement it, and spend the saved effort on what Ally structurally cannot do.**

Rebuilding file checking would take months to reach a worse result than a tool the college already licenses. Ally exposes its findings in a form that joins to what TeachWell already stores, so the file layer can be acquired by integration instead of by reimplementation.

The one condition: this presupposes the college runs Ally. If it does not, item 2 below is replaced by building a reduced file checker directly (scanned-image detection, untagged PDF, document language, document title — roughly 80% of Ally's PDF value for a fraction of its scope).

## The work

Ordered by dependency, then by value. Effort estimates are rough and assume the existing architecture holds.

### 1. WCAG criterion mapping and conformance evidence — *foundational, built*

**Built October 28, 2026. See [ADR 018](ADR-018-WCAG-MAPPING-AND-CONFORMANCE.md).**


**Why.** DOJ's ADA Title II rule requires WCAG 2.1 AA conformance for public institutions by April 26, 2027 (extended from April 2026 by an interim final rule published April 20, 2026). Colleges need to show a plan, measurable progress and remediation records. Ally gives a score, which is not evidence of a process. TeachWell already holds immutable content snapshots, dated endorsements and an audit trail — most of an evidence package, with no way to render it.

**What.** A structured success-criterion field on every accessibility check, and a dated conformance progress report built from data already recorded.

Several checks already cite criteria in prose inside `DefaultGuidance` ("WCAG 2.2, 1.1.1 Non-text Content"). This formalizes what is already there.

**Design note.** The criterion is a fact about a check, not a CTL setting, so it belongs on `ReviewCheck` and not in `StandardCheckSetting`. That keeps it out of the published `ChecksJson` and means no migration and no immutability concern. Checks that map to no criterion (workload, alignment, pacing) carry none, and the report says so rather than implying coverage.

**Why first.** Both of the next two items need this taxonomy: item 2 maps Ally's issue codes onto it, and item 3's reports read it. It is also self-contained, additive, and has no external dependency.

**Effort.** Small — a field, a mapping table, one report endpoint and its screen.

### 2. Ingest Ally's report — *reader and join built*

**Slice 1 built October 28, 2026. See [ADR 020](ADR-020-ALLY-REPORT-INGESTION.md).** Storing and surfacing the findings is slice 2, held back until there is a real export to validate against and a database to run a migration on.


**Why.** Acquires the entire file layer, contrast, captions and broken links at a fraction of the cost of building them.

**What Ally exposes.** Two paths:

- **Institutional report CSV export.** The *Courses* report gives one row per course: `Term id/name`, `Department id/name`, `Course id`, `Course code`, `Course url`, `Number of students`, `Last checked on`, `Overall score`, `Files score`, `WYSIWYG score`, content-type counts, and a count column per issue type. The *Individual Course* report gives one row per content item: `Id`, `Name`, `Mime type`, `Score`, `Url`, `Library reference`, plus the same issue columns.
- **A reporting API.** Two GET endpoints (Ally 2.7.7), credentials generated from the Institution Report, intended for Power BI and Tableau, refreshable on a 15-minute interval.

**Recommendation: CSV upload is the supported path.** Anthology documents the API endpoints as private, with no deprecation policy. Building on them as the primary integration would make TeachWell's file coverage silently breakable by a vendor release. CSV upload also reuses the hardened upload-and-parse path already built for `.imscc` files in `CourseExportEndpoints`. The API belongs behind a feature flag, if at all.

**Join keys already exist.** `Course.SourceCourseId` ↔ Ally's `Course id`. `Course.Department` ↔ Ally's `Department name`. `CourseElement.SourceUrl` ↔ Ally's `Url`.

**Issue mapping.** Ally's codes divide three ways:

| | Ally codes |
| --- | --- |
| **Pure gain — files** | `Scanned`, `Parsable`, `Tagged`, `Ocred`, `Security`, `Title`, `LanguagePresence`, `LanguageCorrect` |
| **Pure gain — HTML** | `Contrast`, `HtmlColorContrast`, `ImageContrast`, `HtmlCaption`, `HtmlBrokenLink`, `ImageSeizure`, `HtmlHasLang`, `HtmlLabel`, `HtmlTitle`, `HtmlObjectAlt` |
| **Overlap — map, do not double-report** | `HtmlImageAlt`→`IMG_ALT_MISSING`, `ImageDecorative`→`IMG_ALT_EMPTY`, `HtmlEmptyHeading`→`HEADING_EMPTY`, `HtmlHeadingOrder`/`HeadingsSequential`→`HEADING_SKIPPED_LEVEL`, `HtmlTdHasHeader`/`TableHeaders`→`TABLE_HEADERS_MISSING`, `HtmlList`→`LIST_TYPED_AS_TEXT`, `HtmlLinkName`→`LINK_EMPTY` |

TeachWell's `LINK_VAGUE`, `COLOR_ONLY_REFERENCE`, `READING_LEVEL_HIGH` and every check in `CourseChecks` and `CourseDesignChecks` have no Ally equivalent and stay as they are. Ally's `HtmlLinkName` only catches empty link names, not "click here", so TeachWell's check is the stronger of the two and should win on overlap.

**Design fit.** `ReviewVocabulary.Methods` already contains `Automated`; findings already arrive `Proposed` and are confirmed or rejected by a person. An imported Ally finding is another proposed finding with a different provenance, and flows into revision tasks, readiness gates and oversight unchanged. Provenance must be recorded and shown: a finding TeachWell did not derive from its own snapshot should say so, and should not be presented as reproducible from that snapshot.

**Also acquired.** `Number of students` gives per-course impact weighting with no access to student records, which keeps the FERPA position in ADR 008 intact.

**Known limits to state plainly.** The `Url` join will be imperfect and needs a reconciliation report for unmatched rows. Ally covers only courses where it is enabled. Ally's scores are averages of averages and must not be rendered as a TeachWell judgment.

**Effort.** Medium — a migration, a CSV parser, the mapping table, reconciliation, and provenance through the review and guide surfaces.

### 3. Cross-course "fix once" worklists

**Why.** The real constraint on a three-person CTL is not finding issues, it is that the same publisher PDF or broken template appears in forty sections. Ally's export carries a `LibraryReference` column, so Ally already knows when content is shared across courses — it just does not surface it as work.

**What.** Aggregate findings by shared content across courses and rank by courses affected × students affected. "This reading is untagged in 40 sections; fixing it once clears 40 courses."

**Depends on** item 2 for `LibraryReference`, and on item 1 for consistent criterion labelling.

**Effort.** Small once item 2 lands. This is the highest labour saving per line of code on the list.

### 4. Syllabus and required-elements check

**Why.** Ally treats `application/x-syllabus` as one more content type and scores its accessibility. It never asks whether the syllabus contains an accessibility statement, a disability-services contact, a grading scale or a late policy. Those are institutional policy and accreditation requirements, and colleges check them by hand today.

**What.** A course-level check over the syllabus for configurable required elements, in the existing `CourseCheck` shape with a CTL-editable keyword list per element — the `COURSE_START_MISSING` pattern.

**Built October 28, 2026. See [ADR 019](ADR-019-SYLLABUS-IMPORT-AND-REQUIRED-ELEMENTS.md).**

**Corrected October 28, 2026.** This item was first estimated as small. On checking, the Canvas syllabus body was not captured at all: neither reader read it, and no element of type `syllabus` existed. So it became two slices, both now built — the import first:
- read `syllabus_body` from the Canvas API (`GET /courses/:id?include[]=syllabus_body`) and `course_settings/syllabus.html` from an export
- store it as a `CourseElement` with `Type = "syllabus"`, which needs no migration because `Type` is a string
- decide whether the syllabus is reviewable element by element, which means weighing `ReviewVocabulary.ElementTypes` and `CriterionTargets` against the effect on existing rubrics and reviews

That last decision resolved as **no**: `ElementTypes` was carrying two meanings at once, and a new `CheckedTypes` set now separates "has text the element checks run on" from "is content placed in a module". Adding the syllabus to `ElementTypes` would have made `CONTENT_NOT_IN_MODULE` fire on every course that has one. The syllabus is checked and appears in the guide; it is not a rubric element-review target, which keeps published rubrics and criterion targets untouched. ADR 019 section 1 has the reasoning.

`SYLLABUS_REQUIRED_ELEMENT_MISSING` fires once per missing element across five CTL-editable keyword lists: accessibility statement, instructor contact, grading, late work and academic integrity. Emptying a list is how a college opts out of an element.

### 5. Design checks Ally has no concept of

Deepen the side that cannot be bought. Each is one `CourseCheck` or `ElementCheck` class in the established shape.

| Check | Fires when | Status |
| --- | --- | --- |
| **Scaffolding order** | A module's work falls due before an earlier module's, so students meet later deadlines first. | **Built** (`MODULE_DUE_DATES_OUT_OF_ORDER`, ADR 018) |
| **Captions** | A video or audio player has no captions track, or an embed's captions cannot be read from the page. | **Built** (`MEDIA_CAPTIONS_MISSING`, `MEDIA_CAPTIONS_UNCONFIRMED`, ADR 018 amendment) |
| **Media without a readable equivalent** | A module's content is only video or audio, with no transcript or summary. The inverse (text-only) is deliberately never flagged. | **Built** (`MODULE_MEDIA_WITHOUT_TEXT`, ADR 018 amendment) |
| **First-week experience** | A new student cannot determine what to do first: no start-here path, no due date in week one, no contact route. | Partly covered; contact route needs the syllabus (item 4) |
| **What changed since your last review** | Not a check but a view. Snapshots are already immutable and versioned, so a diff is nearly free. | Not started |

**Corrected October 28, 2026.** "Small each" was right only for the scaffolding check, which needed no new data: module positions and `CourseItemEvidence.DueAt` were already captured, so it was built directly.

The other two needed evidence that did not exist. **Slice 5a was then built** (ADR 018 amendment): `ContentEvidence` now captures media — `<video>`, `<audio>`, `<iframe>`, `<object>`, `<embed>` and Canvas's legacy inline media comments — read before the HTML strip removes them, keeping facts only and no markup, URLs or tokens.

That unblocked the captions checks, which were the higher-impact half of this item and are now built. A module-level UDL modality check still needs media counts in `CourseStructureEvidence`, which is a further evidence version.

### 6. Oversight trend — already built

**Corrected October 28, 2026. This item was wrong and is withdrawn.** It claimed `OversightEndpoints` discards the time series. It does not: `OversightService` already aggregates `CourseGuideRecord` into twelve weekly `ProgressPointDto` points, each summing every course's latest check on or before that week's end, and `Oversight.tsx` renders them as a table under "Guide progress over time". The scope note explaining the aggregation is already shown with it.

The claim came from reading the endpoint signature without reading the service. Nothing here needs building.

What remains is genuinely small and depends on item 2: adding an ingested Ally score as a second series, so course design and file accessibility appear on one chart. Ally's Months, Terms and Years rollup reports are the direct source for that series. Rendering the existing table as a chart is cosmetic and not worth a slice of its own.

## Declined

**Alternative formats for students.** This is Ally's strongest feature and the only part students touch, and TeachWell should not chase it. It needs a student-facing surface that does not exist, a format conversion pipeline well outside the current architecture, and it cuts against the read-only, no-student-data position that ADR 008 and the FERPA stance depend on.

The honest division of labour is worth stating in the README rather than quietly leaving a gap: **Ally generates accessible formats from the source; TeachWell improves the source.**

**Reimplementing Ally's file checks while the college licenses Ally.** Months of work to arrive somewhere worse. Revisit only if the college drops Ally.

## Sequence

```
1. WCAG criterion mapping          BUILT (ADR 018) — UI still to come
   ├── 2. Ally ingestion           (needs 1 for the mapping target)
   │    └── 3. Fix-once worklists  (needs 2 for LibraryReference)
   ├── 4a. Import the syllabus body            BUILT (ADR 019)
   │    └── 4. Syllabus required elements  BUILT — 5 CTL-editable required elements
   ├── 5. Design checks
   │    ├── Scaffolding order      BUILT (ADR 018)
   │    ├── 5a. Capture media evidence          BUILT (ADR 018 amendment)
   │    │    ├── Captions checks   BUILT — closes the captions gap without Ally
   │    │    └── Media without text  BUILT — structure evidence v4
   └── 6. Oversight trend          ALREADY BUILT — item withdrawn
```

Items 4, 5 and 6 are independent of the Ally work and can proceed in parallel or if the Ally integration is deferred.

Two enabling slices came out of building item 1, both blocking work first estimated as small: **4a**, importing the syllabus body, and **5a**, capturing media evidence. 5a was the more valuable, because it was the only route to a captions check without Ally, and it has now been built.

## Progress

| Item | State |
| --- | --- |
| 1. WCAG mapping and conformance report | Backend built and tested (ADR 018); report screen not yet built |
| 5. Scaffolding order check | Built and tested (ADR 018) |
| 5a. Media evidence capture | Built and tested (ADR 018 amendment) |
| 5. Captions checks | Built and tested; closes the captions gap without Ally |
| 6. Oversight trend | Already existed; item withdrawn |
| College-wide WCAG rollup | Built and tested (ADR 018 amendment) |
| 2. Ally ingestion — reader, catalogue and join | Built and tested; catalogue verified against Anthology's reference (ADR 020 slice 1) |
| 2. Ally ingestion — storage and surfacing | Slice 2: needs a real export to validate the columns, and a database for the migration |
| 3. Fix-once worklists | Blocked on 2's slice 2 (`LibraryReference`) |
| 4a. Syllabus import (API and export) | Built and tested (ADR 019) |
| 4. Syllabus required elements | Built and tested (ADR 019) |
| 4b. Paste the syllabus (Simple Syllabus route) | Built and tested (ADR 019 amendment) |
| 5. UDL modality | Built as `MODULE_MEDIA_WITHOUT_TEXT` (ADR 018 amendment) |

Catalogue: **43 checks**, from 38 before this work.

### Open: this college's syllabi are in Simple Syllabus

Learned October 28, 2026, after item 4 was built. Simple Syllabus is an LTI tool whose content is not in Canvas exports or the Canvas API (ADR 008, confirmed on two real exports), so the required-elements check will find little or nothing here: a course with no Canvas syllabus yields silence, and one holding a Simple Syllabus launch yields findings about boilerplate.

It also exposed a defect in the media checks — an LTI launch was being asked about captions — which is fixed, classified and regression-tested (ADR 018's correction).

**Resolved, and built.** The owner chose the paste route, keeping the Canvas import for colleges that use the Canvas syllabus page. The course owner now pastes the syllabus text (`PUT /courses/{id}/syllabus`); it is sanitized and checked exactly like imported content, it outranks the importer so a re-import cannot overwrite or remove it, and a course with no syllabus at all gets a stated limitation rather than silence. A direct Simple Syllabus API integration stays with the Phase 12 campus integrations, where ADR 008 placed it, because it needs institutional approval. See the ADR 019 amendment.

### Not verified anywhere in this work

Two gaps, both environmental:

- **The SPA.** Node is not installed on the machine these slices were built on and `src/TeachWell.Web/node_modules` is absent, so `tsc --noEmit` and the Playwright specs have not run. Two changes are affected: the WCAG criterion line on a guide step (additive, and its API shape is asserted by tests), and `SyllabusPaste.tsx` with its mount on the course page. The second is a new component and is the part of this work most likely to need correction.
- **The `SuppliedContent` migration.** PostgreSQL is not installed here, and the SQLite suite uses `EnsureCreated`, so it never exercises migrations. The migration is two additive nullable columns and the model snapshot is consistent, but it needs a PostgreSQL run before deployment.
- **Ally's actual export format.** ADR 020's catalogue was checked column by column against [Anthology's Canvas export reference](https://help.anthology.com/ally-lms/en/administrators/ally-institution-report/institution-report-export/ally-institutional-report-exports-for-instructure-canvas.html) and matches it, which caught one real defect: the issue columns carry their severity in the header (`AlternativeText:2`), so the reader found none of them. Fixed, and both forms are now accepted. What is still unverified is whether a generated file differs from its own documentation, which is what the reader's notes exist to surface.

The backend is otherwise covered by 292 passing tests.
