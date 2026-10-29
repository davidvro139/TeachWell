# ADR 020: Ingesting Anthology Ally's report

Accepted October 28, 2026. This ADR covers item 2 of the [roadmap](ROADMAP.md). Slice 1 — reading the reports and joining them to TeachWell — is built. Storing and surfacing the findings is slice 2 and is not.

## Goal

Most colleges running Canvas already license [Anthology Ally](https://help.anthology.com/ally-lms/en/administrators.html). Ally scores every file and WYSIWYG item in every course, continuously, and its unit of work is a file. TeachWell's unit of work is a course under review by a person, and it does not read files at all: `CanvasExportReader` counts them and reports them as "not imported or reviewed".

That is the largest gap in TeachWell's coverage, and reimplementing it would take months to arrive somewhere worse than a tool the college already pays for. Ally exposes its results as CSV exports and a reporting API, and the keys to join them to TeachWell already exist in both systems. So the decision this ADR records is: **consume Ally rather than reimplement it, and spend the saved effort on the course-design work Ally has no concept of.**

What that buys, concretely: the whole file layer (scanned PDFs, untagged PDFs, unreadable and password-protected files, document language and title), plus contrast, broken links, form labels and flashing images. What it does not touch: workload, pacing, module structure, alignment to competencies, reading level, or anything else in `CourseChecks` and `CourseDesignChecks`.

## Why CSV upload and not the API

Ally offers both, and the API is the more convenient:

- **Institutional report CSV exports.** Eight files: a *Courses* report (one row per course), *Months*, *Terms* and *Years* rollups, three `department_*` equivalents, and an *Individual Course, Section, or Domain* report (one row per content item). Slice 1 reads the Courses and Individual reports; the rollups share the same issue columns and are the natural source for an Ally score series on oversight's trend chart.
- **A reporting API.** Two GET endpoints added in Ally 2.7.7, credentials generated from the Institution Report, intended for Power BI and Tableau, refreshable on a 15-minute interval.

**CSV upload is the supported path.** Anthology documents those endpoints as private, with no deprecation policy. Building TeachWell's file coverage on them would make it silently breakable by a vendor release, and the failure would look like "the college's accessibility figures stopped updating" rather than "an integration broke". CSV upload also reuses the hardened upload-and-parse path already built for `.imscc` files.

The API belongs behind a feature flag, if it is built at all, and only once the CSV path has proved the mapping against real exports.

## This catalogue is built from documentation, not from a real export

The project owner does not have Ally exports to hand, and chose to build against Anthology's documented Canvas export columns. That single fact shapes every design decision below, because a reader built from documentation will meet files that differ from it.

So the reader is **tolerant and loud**, never strict or silent:

- Columns are addressed **by header name**, case-insensitively and ignoring surrounding spaces, so a column-order change in a future Ally release does not break it.
- An **absent expected column** becomes a null field and a note naming the column. A caller can tell "Ally reported zero" from "TeachWell could not find that column".
- An **unknown column** is ignored and reported: *"N column(s) in this file are not in TeachWell's Ally catalogue and were ignored … If Ally has added a rule, TeachWell needs updating to report it."* A new Ally rule arriving as an unrecognised column is exactly what a reviewer needs to be told.
- A **malformed file** is refused with a message an instructional designer can act on, never a parse exception.
- Uploading the wrong one of the two reports names the right file to upload instead, because that is the commonest mistake.

## The issue catalogue

`AllyIssues` (in `src/TeachWell.Domain/AllyIssues.cs`) holds all 37 documented issue columns with Ally's own severity (1 severe, 2 major, 3 minor), a plain-language description, and — the point of the exercise — whether TeachWell already finds the same thing.

**Where the two overlap, TeachWell's own finding wins** and Ally's is not reported again, so one issue never becomes two guide steps. The catalogue records which way each overlap goes:

| | |
| --- | --- |
| **Pure gain** (TeachWell has no check) | `Parsable`, `Scanned`, `Security`, `Ocred`, `Tagged`, `Title`, `LanguagePresence`, `LanguageCorrect`, `HeadingsPresence`, `HeadingsSequential`, `HeadingsHigherLevel`, `HeadingsStartAtOne`, `TableHeaders`, `AlternativeText`, `ImageDescription`, `ImageDecorative`, `ImageContrast`, `Contrast`, `ImageSeizure`, `HtmlColorContrast`, `HtmlHasLang`, `HtmlTitle`, `HtmlLabel`, `HtmlObjectAlt`, `HtmlBrokenLink`, `HtmlDefinitionList`, `HtmlHeadingsPresence`, `HtmlHeadingsStart` |
| **Overlap** (mapped, TeachWell wins) | `HtmlImageAlt`→`IMG_ALT_MISSING`, `HtmlImageRedundantAlt`→`IMG_ALT_FILENAME`, `HtmlEmptyHeading`→`HEADING_EMPTY`, `HtmlHeadingOrder`→`HEADING_SKIPPED_LEVEL`, `HtmlTdHasHeader`→`TABLE_HEADERS_MISSING`, `HtmlList`→`LIST_TYPED_AS_TEXT`, `HtmlLinkName`→`LINK_EMPTY`, `HtmlCaption`→`MEDIA_CAPTIONS_MISSING` |

Two overlaps carry a note because they are not clean equivalences: TeachWell's `LINK_VAGUE` also catches "click here", which Ally's `HtmlLinkName` does not, and `IMG_ALT_FILENAME` is specifically alt text that is a file name, a subset of Ally's redundant-alt rule. `HtmlHeadingsPresence` is deliberately **not** treated as an overlap, because `PAGE_LONG_NO_HEADINGS` is conditional on length and the two would disagree on short pages.

`ImageOcr` is in Anthology's documented column list but noted there as no longer populated. It is kept so the reader recognises the column rather than reporting it as unknown, and marked so nothing acts on it.

**Severity mapping is deliberately not generous.** Only Ally's "severe" — the file is unusable by anyone — becomes `Critical`. Major becomes `High`, minor becomes `Medium`.

## The join, and why it is reported rather than assumed

`AllyReconciliation` is pure functions over keys, so the join rules are testable without a database and without Ally. Every row either matches or is counted as unmatched **with a reason**, because a silent 60% match rate would be worse than a loud one: the figures would look complete.

**Courses join on the Canvas course id**, which both sides hold exactly — Ally's `Course id` is the Canvas id, and TeachWell stores it as `Course.SourceCourseId` at import. This join is reliable. Courses in the Ally report that TeachWell has not imported are reported, not dropped: Ally covers every course in the LMS, TeachWell covers the ones someone imported, and that difference matters when reading college-wide figures.

**Content items join on the URL, and this is the weak point.** It is treated as such:

- Comparison is on **host and path only**. Query strings carry each tool's own parameters and sometimes a Canvas verifier token, so comparing them would produce false misses *and* mean storing a secret. A test asserts a verifier token never survives normalization.
- Case, trailing slashes, fragments and protocol-relative addresses are normalized away.
- **Most Ally rows are files**, which are not TeachWell elements at all. Those are expected not to match and are reported separately from items that should have matched and did not — only the second is a problem to fix.
- **A course imported from an export file has no Canvas addresses** at all unless the uploader supplied the course's address (ADR 010), so nothing in it can be matched by URL. The reconciliation says so in those words rather than reporting a 0% match rate with no explanation.

The match rate is **floored**, so a report never rounds 99.6% up to "100% matched".

## Amendment: checked against the documentation — October 28, 2026

The project owner supplied [Anthology's Canvas export reference](https://help.anthology.com/ally-lms/en/administrators/ally-institution-report/institution-report-export/ally-institutional-report-exports-for-instructure-canvas.html), and the catalogue was read back against it rather than against the earlier summary it had been built from.

**One real defect, found and fixed.** Anthology documents each issue column with its severity **in the header**: the column is `AlternativeText:2`, not `AlternativeText`. The reader matched exact header names, so against a real export it would have found **none** of the 37 issue columns. A probe against the documented header row confirmed it: zero issues read, and every issue column reported as unknown.

The tolerance designed into the reader is what made this survivable rather than silent. It did not report a clean course; it said *"This file has none of Ally's issue columns, so it records scores but no issues"* and listed all 37 as unrecognised. That is the behaviour this ADR argued for, doing its job on the first real contact with the format.

The fix accepts **both** forms, because the documentation does not settle whether that colon is in the file or is the page's way of annotating severity. A header is indexed under its full name and, when it ends in `:<digits>`, under its base name as well. The suffix is stripped only for a numeric suffix, so a column such as `application/x-page` or `Time: start` is untouched.

**A second, smaller gain from the same reading.** Where a file states a severity in its header, the reader now compares it with the catalogue and reports any disagreement: *"Scanned (3 in this file, 1 in TeachWell) … The file's severity is the authority; TeachWell's catalogue needs updating."* The issue is still read — a severity disagreement is a note, not a reason to drop a finding. This turns the catalogue's documentation-derived severities into something a real export can correct.

**What the documentation confirmed.** All 37 issue codes and all 37 severities match what was already built, now pinned by a test that lists them independently. Every Courses and Individual report column name matches verbatim, including `Observed deleted on`, `Total WYSIWYG` and `WYSIWYG score`. The Courses report's six `application/x-*` per-content-type columns are `announcement`, `assignment`, `discussion-topic`, `quiz`, `syllabus` and `page`.

**One subtlety worth recording.** `LibraryReference` (no space) appears in the shared issue block of the Courses, Months, Terms and Years reports, while the Individual report has `Library reference` (with a space) as a column of its own and omits it from the issue block. Both spellings are handled, and neither is treated as an issue.

## As built (slice 1, October 28, 2026)

- **`AllyIssues`** (Domain): the 38 documented issue columns, their severities, descriptions, overlaps and notes; `Gains`, `Overlaps`, and the severity mapping.
- **`AllyTable`** (`Infrastructure/Ally`): an RFC 4180 reader, with `BaseName` and `SeverityIn` for the severity-suffixed issue headers — quoted fields, embedded commas, newlines and doubled quotes — with everything bounded (40 MB, 100,000 rows, 400 columns, 4,000-character fields) because an upload is untrusted. Columns by name; `Missing` reports expected-but-absent ones.
- **`AllyReportReader`**: the two report shapes, told apart by their columns, each returning its rows plus missing columns, unknown columns and notes.
- **`AllyReconciliation`** (Domain): the course and item joins, with summaries and notes.
- **`AllyFixtures`** (tests): fictional exports built to the documented column list, including the byte-order mark Ally writes. No real institutional data.
- **No migration and no persistence.** Deliberate — see below. An endpoint and a screen were added on October 29, 2026 (amendment below); they still store nothing.

### Why slice 1 stops before storing anything

Storing imported findings needs new tables, and a migration cannot be verified on this machine: PostgreSQL is not installed, and the SQLite suite uses `EnsureCreated` and so never exercises migrations. There is already one unverified migration outstanding (`SuppliedContent`, ADR 019); adding a second and larger one — new tables with indexes and constraints, far more likely to have problems than two nullable columns — would compound a risk rather than take one.

It is also the better sequence on its own merits. The format understanding is the uncertain part, because it came from documentation. Validating it against a real export before designing a schema around it is cheaper than discovering the column names differ after the tables exist.

### Slice 2, when there is a real export and a database

- Persist imported course scores and item findings, with provenance, so a finding TeachWell did not derive from its own snapshot says so and is never presented as reproducible from that snapshot.
- Surface them as `Proposed` findings with `Method = "Automated"`, flowing into the guide, revision tasks and readiness unchanged. `ReviewVocabulary.Methods` already has the vocabulary.
- Extend `Wcag.Criteria` with the criteria only Ally can reach — contrast (1.4.3), document language (3.1.1), page titled (2.4.2), flashing content (2.3.1). Held back deliberately: ADR 018's completeness test requires every catalogued criterion to have a check behind it, and listing a criterion nothing can report on would weaken the conformance report's honesty before there is data behind it.
- Use `Number of students` for impact weighting, which needs no student records and so keeps ADR 008's FERPA position intact.
- Roadmap item 3, the "fix once, fixes forty courses" worklist, built on `LibraryReference`.

### Verification

- SQLite suite: 290 passed, 0 failed (273 before this ADR; 17 new). No migration.
- `AllyReportUnitTests` (13 tests):
  - the catalogue has unique codes and descriptions, every overlap names a check that **exists in the registry** (so a renamed check cannot leave a dangling mapping), the whole file layer is a pure gain, and most of what Ally reports is new to TeachWell
  - a Courses report is read by column name with its scores, term, department, student count, date and issue counts; a zero cell is absence, not a finding
  - an item report is read with URLs and library references
  - the two shapes are told apart, and uploading each as the other names the right file
  - **a file whose columns differ from the documentation is still read**, reports the expected fields it could not find, and reports an invented future rule as an unknown column with "TeachWell needs updating"
  - empty, unclosed-quote, headerless and wrong-shape files are refused with messages rather than exceptions
  - quoted values with commas, newlines and doubled quotes survive; rows with no identifier are counted, not guessed at
  - courses join on the Canvas id, with unimported courses reported and a floored match rate
  - items join on host and path, ignoring query strings, case and trailing slashes; files, genuine misses and address-less rows are reported separately
  - a course imported from an export file matches nothing and the join says why
  - a verifier token never survives URL normalization
- Four tests were added for the amendment:
  - issue columns are found **whether or not the header carries its severity**, read identically either way, with the suffix never mistaken for an unknown column — the regression test for the defect above, using the documented header row verbatim
  - a severity differing from the catalogue is reported, one that agrees is not, and the finding is still read
  - base names are taken only from a numeric suffix, so `Time: start` and `application/x-page` survive
  - the catalogue's 37 codes and 37 severities are asserted against an independently written list of what Anthology documents
- **Still not verified against a real Ally export.** The column names, severities and report shapes now match Anthology's reference exactly, and the reader accepts both plausible readings of the issue headers. What remains unknown is whether a generated file differs from its own documentation — which is precisely what the reader's notes are for. The first real export should be read with those notes in hand before slice 2 is designed.


## Amendment: a screen for checking a real export — October 29, 2026

Slice 1 could answer every question except the one that matters most: does a real Ally export match the documentation this reader was built from? That question cannot be answered by anyone without an Ally licence, so it needed to be answerable by the person who has one.

`POST /ally/report-check` (CTL only) streams an upload to a temporary file the OS deletes on close, parses it, and returns what the reader understood. `AllyReportCheck.tsx` presents it under Standards, where the college's own checks are configured.

**It stores nothing** — no scores, no findings, no copy of the file — and says so above the upload control, because a screen that accepts an institutional report must not leave any doubt about what happens to it. An integration test asserts it: element and snapshot counts are unchanged across an upload.

What it reports: which of the two reports it is, rows read, **how many of the 37 documented issue columns it recognised**, columns it does not know, expected columns the file lacks, per-issue counts split into what Ally adds versus what TeachWell already checks, any severity that disagrees with the catalogue, and — for a Courses file — how many Canvas course ids match imported courses.

The recognised-versus-known count is the headline, because it is the direct answer to "does this reader understand your file".

### Found while building it

- **A 500 that should have been a 400.** An empty or malformed multipart body made `MultipartReader` throw with nothing catching it. Now reported as the client error it is.
- **The recognised count was meaningless.** It was derived from `MissingColumns`, which only ever held *expected field* columns and never issue codes. `AllyReport` now returns `MissingIssueColumns` separately, because "a field the reader wanted" and "a whole Ally rule this file cannot report on" are different facts and were being conflated in prose.

### The public demo

The demo takes no uploads (`PublicDemoOptions.Disabled`), so the panel is hidden when `Features__PublicDemo` is on rather than rendering a control that refuses every file.

### Verification

`AllyCheckTests`, 4 integration tests: a Courses report read end to end with its scores, issue provenance and course matching, asserting **nothing was written**; a Courses file told apart from one course's own report; a file with deliberately wrong columns still read and reporting what it could not find; and CTL-only access with instructors and the academic administrator refused, plus wrong extension, non-Ally, empty and malformed uploads.
