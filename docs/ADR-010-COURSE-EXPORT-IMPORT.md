# ADR 010: Canvas course export import (Phase 7)

Accepted October 16, 2026. Docker remains deferred.

## Why

The owner wants real courses reviewed now, with a live Canvas connection deliberately left as the last integration feature (project roadmap, decision 4). A Canvas course export (`.imscc`, IMS Common Cartridge) is something any instructor can download from their own course without institutional API approval, so it is the near-term way in.

## One import pipeline, two sources

The logic that applies normalized course content to a local course was extracted from the Canvas API importer into `CourseDataApplier`, and both sources now use it. Refreshing a course therefore behaves identically whichever way it arrived:
- local element IDs are kept, so shares, reviews and tasks survive
- a snapshot is appended only when content changed
- items no longer present are marked Removed, never deleted
- the synthetic course-structure element is preserved and re-captured

The applier now validates the whole incoming structure before changing anything. Previously a rejected API import could leave tracked elements half-updated in memory, although it was never saved.

Export courses have `SourceType = "CanvasExport"` and `SourceCourseId` = the export's course identifier. Uploading another export with the same identifier refreshes the instructor's existing course instead of duplicating it. Refreshing a specific course (`POST /courses/{id}/course-exports`) is rejected when the export's identifier differs.

## Reading the archive (`CanvasExportReader`)

Read: `imsmanifest.xml` (resources), `course_settings/course_settings.xml` (identifier, title, code), `course_settings/module_meta.xml` (modules, positions, publish state, items and their targets), `wiki_content/*.html` (pages, with title and `workflow_state` from the page head), and each assignment's `assignment_settings.xml` plus its description HTML (title, points, due date, rubric reference, publish state). Page and assignment HTML goes through the same sanitizer, structural evidence and allowlisted preview as every other source (ADRs 005, 009).

Listed, not imported: quizzes, discussions, files, media, external links and tools. Module items pointing at them are kept and flagged, matching the API importer.

The archive is untrusted input:
- **Upload** (amended October 19, 2026, see below): configurable limit, 500 MB by default. The upload is streamed to a private temporary file that the operating system deletes when it is closed, at the end of the request. It is never written to the database or logs; only normalized output is kept.
- **Archive** (amended October 19, 2026): at most 20,000 entries. The XML and HTML entries TeachWell reads may expand to at most 250 MB in total. Among those, an entry over 1 MB with a compression ratio above 200 is rejected (zip bomb). Course files and media are never opened, so they are excluded from both limits. Every read is bounded by actual bytes, not the size the archive declares.
- **Entry names:** rejected if empty, rooted, using backslashes or drive letters, containing control characters, or containing `.`/`..` segments. Nothing is extracted, so traversal could not escape anyway; such archives are refused as malformed.
- **XML:** parsed with DTDs prohibited and no resolver, so external entities and entity expansion are impossible. Documents are capped at 5 MB.
- **Content limits:** each page or description is capped at 60,000 characters, the same limit as the API importer. An over-limit page is imported as Unavailable with a warning, instead of failing the whole course. The 3,000-element course limit is unchanged.
- **Not provided:** malware scanning, stated as a limitation.

## Links back to Canvas

An export contains neither the course's Canvas address nor Canvas's numeric item IDs. The uploader may supply the course address (`https://…/courses/{number}`: https only, no query or credentials, never fetched). It is saved on the course (`Course.SourceWebUrl`) and reused on later uploads. Pages link exactly (`/pages/{slug}`). Assignments and modules link to the course's assignment and module lists. With no address, nothing links out, and the import warning says why. Links are resolved after the upload is matched to its course, so a refresh without an address keeps the saved one.

## Authorization

- Creating a course from an export: Instructor role. The course is always owned by the uploader.
- Refreshing: the course's owner (as instructor), or a CTL reviewer it is explicitly shared with.
- Unauthorized or unknown course IDs return 404. Wrong-role requests return 403 at the route.
- CSRF and the per-user rate limit (10/minute) apply as for other Canvas operations.
- Imports and refreshes are audited (`CourseExportImported`, `CourseExportRefreshed`).

## Verification status

Built to Canvas's documented export layout and verified against fictional exports in that layout: unit, integration and browser tests, plus `samples/fictional-course-export.imscc`. It is **not yet verified against a real export** from the college's Canvas. Canvas export details can vary by version and by which content types a course uses, so the first real export should be imported and compared with the course in Canvas before relying on it.

## Amendment: large exports (October 19, 2026)

A real course export of about 130 MB could not be imported. It was refused by the 50 MB limit, and it would also have hit ASP.NET Core's 128 MB form limit. Canvas exports are large mostly because of course files and media under `web_resources/`, which TeachWell lists but never imports.

- **Streaming upload.** The endpoint no longer uses ASP.NET's form reader or copies the upload into memory. It reads the multipart body with `MultipartReader` and streams the file section into a temporary file opened with `FileOptions.DeleteOnClose`, counting bytes against the limit as it goes. It then hands that file to the reader. Memory use no longer grows with the size of the upload.
  - **Disk is used.** This replaces the earlier "held in memory, never written to disk". The temporary file sits in the server's temp folder, readable only by the server account, and is deleted when the request ends.
  - **Malware scanning.** There is still none, as before. A deployment that requires it must scan this temporary file.
- **Limit.** `CourseExports:MaxUploadMegabytes` (1–2048, default 500), validated at startup. The endpoint raises Kestrel's per-request body limit to match, and `GET /course-exports/limits` publishes it for the UI. Uploads over the limit get 413. More than one file in a request gets 400.
- **Media is excluded from the archive limits.** The expanded-size and compression-ratio limits now apply only to the XML and HTML entries TeachWell reads. Every entry still has its path checked for safety and duplicates. The entry limit rose from 5,000 to 20,000, because it only bounds the size of the zip directory. The import warning now reports the number and total size of course files skipped.
- **UI.** The import form shows the server's limit and the selected file's size. Uploads go through `XMLHttpRequest` so a progress bar can show while they send, followed by "Reading the export…" while the server works.
- **Verification.**
  - Reader tests: a 260 MB media entry that compresses into a few hundred kilobytes is listed and skipped, while the same bytes as a page are still refused.
  - An HTML compression bomb is still refused, and the 20,000-entry limit is enforced.
  - End to end, a 140 MB export streamed from disk through the API imports successfully. That is above both the old 50 MB limit and the 128 MB form limit.
  - A 1 MB configured limit returns 413 and is published by the limits endpoint.
  - A new Edge spec uploads the sample export through the real form.

## Amendment: quizzes and module text headers (October 19, 2026)

A real course showed 16 module items as "unavailable". All were Canvas Classic Quizzes: 8 graded quizzes and 8 ungraded "reflection" surveys. A further 40 items were module text headers that the course page presented as if they were content.

- **Quizzes are imported** as a new element type, `quiz`. Each one's settings come from `{id}/assessment_meta.xml`: title, quiz type, points, attempts, time limit, published state, and the due date (direct or from the linked assignment). Its questions come from `non_cc_assessments/{id}.xml.qti`, or `{id}/assessment_qti.xml` if that is absent.
  - The content is one document: a summary line ("Ungraded survey · 10 questions · 10 essay · 0 points · 1 attempt"), then the instructions, then each question's text. It runs through the same sanitizer, evidence capture, previews and checks as pages, so missing alt text in a question is found.
  - If the questions would exceed 60,000 characters, the later ones are left out and the content says so.
  - Answer choices and correct answers are not imported.
  - QTI files are parsed with the same XML settings (no DTDs) and per-file size limits, and count toward the readable-content limits.
- **Quizzes count as assessments:**
  - module "has an assessment"
  - workload estimates
  - due dates and due-date clustering
  - early low-stakes practice (practice quizzes and surveys worth 0 points count)
  - competency alignment, mapping and the alignment report
  - the outcome and assessment level comparison

  They are reviewed against a rubric's assignment criteria. The rubric and instruction-length checks still apply only to assignments.
- **Module text headers** (`ContextModuleSubHeader`) are stored as module items with status `TextHeader`. The course page shows them as section labels. Checks do not count them as items, and a header such as "Overview" counts as the module's introduction. Imports made before this amendment show headers correctly without a re-upload, because a module item with no target and no warning is recognised as a header.
- **Unsupported module items say what they are:** discussion, course file, external link, external tool (LTI, including New Quizzes), a quiz missing from the export, or a missing page or assignment. "Skipped quizzes" now counts only quiz resources whose settings were not found. Question banks are no longer counted as quizzes.
- **Not yet supported at the time:** discussions, New Quizzes, and quizzes through the live Canvas API connection. All three were added the same day; see the next amendment.
- **Verified against a real 1.1 GB export** (local, read-only): 19 quizzes imported, and all 184 module items resolved (144 linked, 40 text headers, 0 unsupported).

## Amendment: discussions, external tools, New Quizzes, and quizzes over the Canvas API (October 19, 2026)

- **Discussions (exports):** each `imsdt` resource's prompt comes from its topic file; its settings come from the Canvas `topicMeta` dependency (type, state, and an `assignment` block when graded). Discussions become a new element type, `discussion`, with a summary line ("Graded discussion · 5 points" or "Ungraded discussion"). They run through the same sanitizer, evidence and checks as pages. Module items of type `DiscussionTopic` link to them.
- **Graded vs ungraded:** a graded discussion is an assessment (points, due date, alignment, workload, early practice, outcome-level comparison). An ungraded discussion is not, so it is never asked for a due date or alignment.
  - Discussions can be reviewed (against page and assignment criteria) and mapped to competencies.
  - The alignment report counts only graded discussions as assessments.
- **Announcements** (`topicMeta` type `announcement`) are messages, not course content. They are counted in the import notes and not imported.
- **External-tool assignments** (`submission_types` includes `external_tool`) keep importing as assignments. Their content now opens with "Submitted in an external tool (host); TeachWell reviews only this Canvas description." This applies, for example, to CodeGrade and TestOut LabSim in the college's own exports.
- **New Quizzes** are recognised by the New Quizzes tool's address (`*.quiz-lti*.instructure.com`), or over the API by `is_quiz_lti_assignment`. They import as `quiz` elements with their settings, instructions and a note that the questions are held by the New Quizzes tool and are not in exports or the course API. Neither of the college's exports contains a New Quiz, so **this detection is built to Canvas's documented behaviour and has not been checked against a real one.**
- **Live Canvas API** (`CanvasCourseReader`):
  - New requests: `/quizzes` (classic), `/quizzes/{id}/questions` and `/discussion_topics`, which excludes announcements.
  - Module item types `Quiz`, `Discussion` and `SubHeader` are mapped. Other unsupported items say what they are.
  - Question requests run last, one per quiz, and stop at 190 of the existing 200-request budget. Quizzes beyond that keep their settings and instructions, are named in a warning, and say "Questions were not fetched from Canvas for this quiz."
  - Like the rest of the API path, this is tested against simulated Canvas responses, **not a live Canvas account**.
- **Quiz and discussion content** is assembled by one shared helper (`CanvasActivityContent`), so exports and the API describe the same activity identically.
- **Verified locally against the college's two real exports** (read-only probe, nothing imported):
  - **A programming course:** 19 quizzes; 184 of 184 module items resolved; 12 CodeGrade assignments labelled.
  - **Program handbook:** 20 quizzes and 187 assignments; 163 TestOut LabSim assignments labelled; 1 announcement counted and not imported; 22 of 23 module items linked (the other is a course file).
