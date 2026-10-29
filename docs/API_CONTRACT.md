# Draft REST API Contract (v1)

## Implemented in Phase 1

`GET /auth/csrf`, `POST /auth/login`, `POST /auth/logout`, `GET /me`, `GET /courses?role=Instructor|CTLReviewer`, `GET /courses/{id}` (includes plain-text elements), and read-only `GET /admin/users`. Additional implemented sharing routes are listed below; remaining routes are proposed. Course lists contain the small demo fixture without pagination; add pagination before real imports. Development OpenAPI at `/openapi/v1.json` is the executable contract. All POST requests require the CSRF header, including login. Unauthorized course IDs return 404.

## Implemented Phase 2 slice: course sharing

- `GET /courses/{id}/reviewers`: eligible active CTL reviewer names/IDs in the owner's tenant.
- `GET /courses/{id}/shares`: current shares, reviewer names, permission and UTC grant time.
- `POST /courses/{id}/shares`: `{reviewerId}` grants Review access, idempotent, 204 on success, 400 for an ineligible reviewer, 409 for concurrent grant conflict.
- `DELETE /courses/{id}/shares/{reviewerId}`: revokes access idempotently, 204 on success. Missing CSRF yields 400.

These routes require Instructor role plus ownership of the course; non-owners receive 404 and role failures 403. Both course and candidate must be in the same tenant. No permission string is accepted from clients. Course detail includes `permissions.canManageSharing`; the UI flag does not replace server checks. Each actual change commits its target-aware audit entry with the permission change. All unsafe requests, including DELETE, require `X-CSRF-TOKEN`.

## Implemented Phase 2 slice: Canvas connection lifecycle

`GET /canvas/settings`, `GET /canvas/connections`, `POST /canvas/connections`, `POST /canvas/connections/{id}/validate`, and `DELETE /canvas/connections/{id}` are implemented. Connect accepts `{baseUrl,token,expiresAt?}` and returns a safe summary with a fixed saved-token mask, never token fragments/ciphertext. Routes require Instructor or CTLReviewer role and scope each connection by owner plus tenant. Disabled mode permits list/disconnect but rejects connect/validate. Remote token rejection returns 422, not a local session 401. See `CANVAS_CONNECTIONS.md` for configuration and status codes.

## Implemented Phase 2 slice: read-only import

- `GET /canvas/connections/{id}/courses`: bounded array of `{id,title,code}` for active teacher courses. Backend follows same-origin/same-collection opaque pagination links; cursors and credentials are not returned.
- `POST /courses/import`: `{connectionId,sourceCourseId}`; validates the current caller's connection and active Canvas teacher enrollment, returns `{courseId,elementCount,warnings,error,notFound}` on success, and standard ProblemDetails on failure. Existing local IDs/shares survive refresh; no partial course changes on error. Repeated unchanged import does not add duplicate snapshots.
- `GET /courses/{id}` now includes source origin/course ID, last sync time, warnings, element source URL/status, placement target IDs and current snapshot IDs. Plain text only. Existing ACLs protect imported copies as well as samples.

Import/list require Instructor or CTLReviewer, tenant+connection ownership, enabled integration and per-user rate limits; import requires CSRF. Removed/unsupported content is labeled explicitly. Sync/background-job routes, direct snapshot-history routes and additional Canvas content types below remain proposed. To refresh now, repeat `POST /courses/import`.

## Implemented Phase 3 slice: rubrics and element reviews

Rubrics (CTLReviewer role, tenant-scoped):
- `GET /rubrics`: profiles with version summaries.
- `POST /rubrics` `{name,deliveryMethod}`: creates a profile with an empty draft version 1.
- `GET /rubrics/{id}/versions/{versionId}`: version with criteria and rating anchors.
- `POST /rubrics/{id}/versions`: new draft copied from the latest version; 409 if a draft exists.
- `PUT /rubrics/{id}/versions/{versionId}` `{revision,criteria[]}`: replaces draft criteria; 400 for invalid criteria, 409 for stale revision or published version.
- `POST /rubrics/{id}/versions/{versionId}/publish` `{revision}`: freezes the version; 400 if it has no criteria.

Reviews:
- `GET /courses/{id}/reviews` (Instructor or CTLReviewer with course access): published reviews plus the caller's own drafts.
- `POST /elements/{id}/reviews` `{rubricVersionId}` (CTLReviewer): draft pinned to the newest snapshot; 400 for non page/assignment elements or unpublished rubric versions, 409 if the caller already has a draft for the element.
- `GET /reviews/{id}`: review with snapshot, applicable criteria, evaluations, findings (non-authors see confirmed only), score summary and check limitations.
- `PUT /reviews/{id}/criteria/{criterionId}` `{revision,outcome,rating?,comment?,evidence?}`: outcome `Rated` (0–3), `NotApplicable` or `NotReviewed`.
- `POST /reviews/{id}/audit` `{revision}`: runs deterministic checks on the pinned snapshot; proposes findings idempotently.
- `POST /reviews/{id}/findings` `{revision,criterionId?,title,description?,severity,recommendation?,evidence}`: confirmed manual finding.
- `PATCH /findings/{id}` `{revision,status?,title?,...}`: confirm/reject or edit a draft finding.
- `POST /reviews/{id}/publish` `{revision,summary?}`: requires no proposed findings and at least one decided criterion; the review becomes immutable.

Review mutations require the CTLReviewer role, authorship of the draft and current course access. Each mutation returns the full review with a new `revision`. Course detail includes `permissions.canReview`. See ADR 005.

Base: `/api/v1`. JSON DTOs, standard ProblemDetails errors, pagination and server-side authorization. This is a proposed contract, refine during implementation and publish OpenAPI.

## Authentication & users
- `POST /auth/login` local demo login; session cookie, CSRF protection.
- `POST /auth/logout` terminate session.
- `GET /me` profile, roles, allowed actions.
- `GET /admin/users` (SystemAdmin): people in the tenant with their roles. `PUT /admin/users/{id}/roles {role, granted}` (SystemAdmin) grants or revokes Instructor, CTLReviewer or AcademicAdmin. It is audited and renews the person's security stamp. 400 for any other role; 404 for someone outside the tenant.
- Future `GET /auth/microsoft/start`, `GET /auth/microsoft/callback` via OIDC middleware.

## Canvas and courses
- `POST /canvas/connections` save PAT securely; body `{baseUrl, token}`; response never contains token.
- `GET /canvas/connections`, `DELETE /canvas/connections/{id}`.
- `POST /canvas/connections/{id}/validate`.
- `GET /canvas/connections/{id}/courses` available courses.
- `POST /courses/import` body `{connectionId, sourceCourseId}`.
- `GET /courses`, `GET /courses/{id}`, `GET /courses/{id}/elements`.
- `GET /elements/{id}`, `GET /elements/{id}/snapshots`.
- `POST /courses/{id}/sync` creates read-only import job.
- `POST /courses/{id}/shares` grant explicit user access, permission-checked.

## Rubrics and reviews
Implemented as described in the Phase 3 section above; `PATCH /findings/{id}` replaces the proposed route of the same name and `POST /rubrics/{id}/versions/{versionId}/publish` replaces `POST /rubrics/{id}/publish`.
- `GET /rubrics`, `GET /rubrics/{id}/versions/{versionId}`.
- `POST /rubrics`, `POST /rubrics/{id}/versions`, `POST /rubrics/{id}/publish` CTL-authorized.
- `POST /elements/{id}/reviews` create draft review with rubricVersionId and snapshotId.
- `PUT /reviews/{id}/criteria/{criterionId}` rate and comment.
- `POST /reviews/{id}/audit` run deterministic checks and return proposed findings.
- `POST /reviews/{id}/findings`, `PATCH /findings/{id}`.
- `POST /reviews/{id}/publish` freeze published review state.
- `GET /courses/{id}/reviews`, `GET /reviews/{id}`.

## Implemented Phase 4 slice: revision tasks

- `POST /revision-tasks` body `{courseId,title,instructions?,priority,dueAt?,findingIds[]}` (CTLReviewer with course access). No `assigneeId` field: the assignee is always derived as the course owner. Every finding must be Confirmed, on a Published review, and on the same course element; a finding already tracked by another open (non-terminal) task is rejected. Starts as `Draft`, visible only to CTL reviewers with course access.
- `GET /revision-tasks?status=`: the caller's own queue — tasks assigned to them, or tasks they created as CTL (including their own drafts). `GET /courses/{id}/revision-tasks`: all tasks for a course the caller can access; the course owner does not see other reviewers' drafts.
- `GET /revision-tasks/{id}`: full detail, including findings, comments, append-only history and `availableActions` (each `{toStatus,label,reasonRequired}`) computed server-side for the caller's actual authority.
- `POST /revision-tasks/{id}/transition` body `{revision,toStatus,reason?}`; enforces the documented status machine (ADR 006) including `Blocked`/`Resume` and required reasons; 409 on a stale revision or a terminal task, 400 for a disallowed transition or wrong actor.
- `POST /revision-tasks/{id}/comments` body `{body}` (no revision — comments are additive and never conflict). Visible to the assignee and any CTL reviewer with course access; remain after the task closes.
- `GET /revision-tasks/{id}/comparison`: the snapshot the task opened against vs. the element's current newest snapshot, as a plain-text line diff plus metadata changes. Always live, never cached.
- `POST /revision-tasks/{id}/simulate-update` body `{revision,plainText}`: assignee-only, demo courses only (400 otherwise); records a new plain-text snapshot standing in for an out-of-Canvas edit, clearly labeled in the UI as a simulation. Real Canvas-origin courses pick up edits only through re-running `POST /courses/import`.

Revision tasks require the Instructor or Reviewer role plus course access; creation and `Creator`-actor transitions additionally require the Reviewer role. All unsafe methods require CSRF. See ADR 006.

## Implemented Phase 5 slice: faculty support center

- `GET /support/tickets?status=`: the caller's own tickets (requester or assignee). `GET /support/tickets?scope=queue&status=`: the tenant-wide CTL triage queue (CTLReviewer role; 403 otherwise, matching how `GET /courses?role=` Forbids a role mismatch rather than deferring to the service).
- `POST /support/tickets` body `{courseId?,elementId?,revisionTaskId?,category,title,description,priority}` (Instructor or Reviewer role): `category` is one of Canvas/Accessibility/CourseDesign/Multimedia/Other. Any course/element/task reference must be mutually consistent and currently visible to the requester.
- `GET /support/tickets/{id}`: visible to the requester, the assignee, or any CTL reviewer in the tenant. `LinkedCourse`/element/task titles are included only when the current viewer has course access; the ticket itself is not otherwise redacted.
- `POST /support/tickets/{id}/transition` body `{revision,toStatus,reason?}`: enforces `New → Triaged → InProgress → WaitingOnInstructor → Resolved → Closed`, with a reopen path back to `InProgress` from `Resolved` or `Closed`. No status is terminal. See ADR 007.
- `POST /support/tickets/{id}/comments` body `{body}` (no revision; additive, like revision task comments).
- `PATCH /support/tickets/{id}/assignment` body `{revision,assigneeId?}` and `PATCH /support/tickets/{id}/recommendation` body `{revision,articleId?,workshopId?}`: both CTLReviewer-only route groups (403 for other roles), independent of the status transition. Recommendation fully replaces both fields each call.
- `GET /knowledge`: Published articles for everyone; Draft and Published for CTL. `GET /knowledge/{slug}`. `POST /knowledge` body `{title,body,tags?}` and `PUT /knowledge/{id}` body `{revision,title,body,tags?,status}`: CTLReviewer-only. Published articles remain editable (not immutable like rubrics/reviews); the slug never changes after creation.
- `GET /workshops`: lists with registration counts and the caller's own status; the full registrant roster is included only for CTL. `POST /workshops` body `{title,description?,startsAt,endsAt,modality,capacity?}`: CTLReviewer-only. `POST /workshops/{id}/register` and `DELETE /workshops/{id}/register`: capacity-aware, waitlists past capacity, and promotes the longest-waiting registrant on a cancellation.

All unsafe methods require CSRF. See ADR 007, including a UTC-timestamp normalization fix this phase required for `startsAt`/`endsAt`/`dueAt`.

## Implemented Phase 6 slice: alignment and reporting

Competencies (course-scoped, owner-authored metadata; CTL with course access can read but not author):
- `GET /courses/{id}/competencies`: list with their mapped assignment titles.
- `POST /courses/{id}/competencies` body `{code,title,description?}` (owner-only route group): `code` is 1–20 uppercase letters/digits/hyphens, unique per course.
- `PUT /courses/{id}/competencies/{competencyId}` body `{revision,title,description?}`, `DELETE /courses/{id}/competencies/{competencyId}` (204; cascades its mappings): owner-only.
- `POST /courses/{id}/competencies/{competencyId}/mappings` body `{elementId}` and `DELETE .../mappings/{elementId}`: owner-only, idempotent, restricted to assignment-type elements (assessment evidence, not pages).

Reports (every response includes `scope: {description, generatedAt, caveats[]}`; none present a pass/fail or compliance badge):
- `GET /reports/course/{id}/alignment`: competency coverage, orphan competencies (no mapped assignment), unmapped assignments (no competency).
- `GET /reports/course/{id}/quality`: reviewed-element coverage, average rubric score across the course's published reviews, per-category averages, and outstanding findings (`Confirmed`, no `Resolved` task; `hasOpenTask` distinguishes untracked from in-progress).
- `GET /reports/ctl/overview` (CTLReviewer-only route group, 403 otherwise): accessible courses, published reviews and outstanding findings across them, revision tasks by status with average age, and tenant-wide support ticket themes.
- `GET /reports/revisions?format=csv`: the caller's own revision tasks (assignee or reviewer, the same scope as `GET /revision-tasks`), by status with aging; `format=csv` returns a CSV download instead of JSON. PDF export is not implemented.

See ADR 008.

## Course-level review and visual preview
- `POST /courses/{id}/course-reviews {rubricVersionId}` (CTLReviewer route group; 404 without course access): captures the course structure as a snapshot of the course's synthetic `course` element and starts a draft review pinned to it. 400 if the rubric version has no criteria targeting `course`; 409 if the caller already has a draft course review. All other review endpoints (`/reviews/{id}`, criteria, audit, findings, publish) work unchanged.
- Course reviews return `outline: {modules[], unplaced[], competencies}`; each item carries its captured `snapshotId`, Canvas `sourceUrl` (https on the course's origin only) and due-date/rubric/alignment flags.
- Findings may carry `subjectElementId`, `subjectTitle` and `subjectSourceUrl` naming the module or item a course finding concerns.
- `snapshot.previewHtml` on every review: server-generated allowlisted markup (structural tags, encoded text, no source attributes, URLs or images), or null for snapshots captured before previews existed.
- `GET /reviews/{id}/previews/{snapshotId}`: preview of a snapshot the review is pinned to (its own, or one in its course structure); 404 otherwise, including for draft reviews the caller cannot see.
- Rubric version summaries include `elementTypes`, the union of what their criteria target (`page`, `assignment`, `course`).

See ADR 009.

## Canvas course export import
- `POST /course-exports` (Instructor; multipart form with `file` = the `.imscc` export and optional `canvasCourseUrl`): creates a course owned by the caller, or refreshes the caller's earlier import of the same export. Returns `{courseId, title, code, elementCount, created, warnings[]}`. 400 with a readable `file` error for anything that is not a valid, safe export, or for more than one file; 413 over the configured limit (`CourseExports:MaxUploadMegabytes`, default 500 MB).
- `GET /course-exports/limits` (Instructor or CTLReviewer): `{maxUploadBytes}`.
- `POST /courses/{id}/course-exports` (owner as Instructor, or a CTL reviewer the course is shared with; otherwise 404): refreshes that course. 400 if the course did not come from an export, or the export is from a different course.
- `canvasCourseUrl` must be an https Canvas course address (`…/courses/{number}`); it is stored for links and never fetched.
- Course detail adds `sourceWebUrl` and `permissions.canRefreshExport`. Export courses have `sourceType: "CanvasExport"`.

See ADR 010.

## Standard sets and course classification
- `GET /review-checks` (CTLReviewer): the check catalog: code, name, aspect, target (`Element`/`Course`), default severity, parameters (`Number` with min/max, or `WordList` with item limits) and default guidance `{why, howToFix, example}`.
- `GET /standard-sets` (CTLReviewer): sets with scope and version summaries (`enabledChecks`, `requiredChecks`).
- `POST /standard-sets {name, deliveryMode?, department?}`: creates a set and its first draft, copied from the standards currently applying to that scope. 409 if the scope is taken (department compared case-insensitively).
- `GET /standard-sets/{id}/versions/{versionId}`; `POST /standard-sets/{id}/versions` (new draft; 409 if one exists); `PUT /standard-sets/{id}/versions/{versionId} {revision, notes, checks[]}`; `POST …/publish {revision}`. Every check must be listed exactly once with `{code, enabled, severity, required, numbers, lists, guidance}`. Validation errors are keyed `checks.{CODE}` and `checks.{CODE}.guidance`. 409 on a stale revision or a published version.
- `POST /standard-sets/{id}/versions/{versionId}/preview {courseId}` (course access required, else 404): per-check finding counts for the course under its current standards and under this version. Saves nothing.
- `GET /courses/{id}/standards` (Instructor or CTLReviewer with course access): `{deliveryMode, department, canClassify, applied: {setId, versionId, name, versionNumber, scope, reason, publishedAt}}`. `setId` is null when the built-in defaults apply.
- `PUT /courses/{id}/classification {deliveryMode?, department?}` (CTLReviewer with course access; owners get 403 from the role gate): sets D4 classification; audited when it changes.
- `GET /review-checks` now lists 36 checks; number parameters (thresholds) carry `min`/`max`. Course-review outlines are unchanged; the underlying structure evidence is version 2 (see DATA_MODEL).
- Course detail adds `deliveryMode` and `department`. Reviews add `standards` (the version pinned at start) and `guidance` (keyed by check code, for automated checks among the caller's visible findings).

See ADR 011.

## One-click demo sign-in (Development, loopback only)
- `GET /auth/demo-accounts`: `[{email, displayName, roles[]}]` for the seeded `@demo.teachwell.test` accounts.
- `POST /auth/demo-login {email}`: signs that account in (CSRF and the login rate limit apply). 400 for a non-demo email; 401 for an unknown, inactive or locked account.
- Both routes return 404 unless Development, `Features:DemoAuth` and `Features:DemoQuickLogin` are on and the client is loopback. See ADR 012.

## Course content previews
- `GET /courses/{id}/previews?ids=a,b,…` (1–100 element ids; any authenticated user with access to the course, else 404): `{elementId: previewHtml | null}` for those elements that belong to the course. `previewHtml` is the allowlisted re-serialization made at import, including `pv-*` formatting classes (ADR 009 amendment). 400 for malformed or too many ids.

## Competencies from pasted outcomes
- `POST /courses/{id}/competencies/parse {text}` (course owner as Instructor; others 404): `{outcomes: [{code, title, include, bloomLevel, vaguePhrase, existingCode, notes[]}], ignored: []}`. Saves nothing. 400 for empty text, more than 20,000 characters, or no outcomes found.
- `POST /courses/{id}/competencies/bulk {items: [{code, title, description}]}` (owner): creates all or none. 400 with `items[i].field` errors for invalid fields or codes already used. Returns the created competencies.
- `PUT /courses/{id}/competencies/mappings {changes: [{competencyId, elementId, mapped}]}` (owner; others 404): the mapping grid's save. It applies all the changes or none. 400 for no changes or more than 5,000, a competency outside the course, or an element that is not an assignment, quiz or discussion in the course. Changes that match the current state do nothing; if a cell appears more than once, the last change wins. Returns the course's competencies.

## Improvement guide (ADR 011 slices 4–5)
- `GET /courses/{id}/guide` (owner or a CTL reviewer with course access; others 404). It runs the course's standard set against the latest content now and returns:
  - the standard set applied (`standards`), the content date (`contentAsOf`), `checkedItems` and `canMark` (owner only)
  - `aspects`: label, why it matters, open and required step counts
  - ranked `steps`: check, name, aspect, severity, required, quick win, minutes, places, suggestion, guidance (why, how to fix, example), and `locations`. Each location has a key, item, module, detail, evidence, an optional Canvas link, a status (Open, Done or NotApplicable), reason, set by and at, and `stillDetected`.
  - `strengths`, `resolvedSinceMarked` and `limitations`
  - CTL review findings: each location's `confirmedBy` and `taskId` (an open revision task), each step's `confirmedByReviewer`, and `reviewerNotes` (manual findings from published reviews with no resolved task: where, title, description, recommendation, severity, reviewer, published date, review id, task id and status)
- `PUT /courses/{id}/guide/progress {keys: [...], status: "Done" | "NotApplicable" | "Open", reason}` (owner only; others 404). Returns the updated guide. 400 when:
  - there are no keys or more than 1,000
  - the status is unknown
  - Not applicable has no reason, or a reason is over 500 characters
  - a key is not currently shown by the guide
- Each guide step also has `resources`: linked published articles (kind, id, title, slug) and upcoming workshops (kind, id, title, start, end, modality, `myStatus`), and `wcagCriteria`: the success criteria the step's check relates to (number, name, level, reference), empty for checks that relate to none.
## Ally report check (ADR 020)
- `POST /ally/report-check` (CTL role; others 403) takes one multipart form file named `file`, a `.csv` from Ally's institution report export. It parses the file and **stores nothing**: no scores, no findings and no copy of the file. Rate-limited, refused on the public demo, and capped at 40 MB.
- Returns the report `kind` (`Courses` or `Items`), `fileName`, `rows`, `issueColumnsRecognised` of `issueColumnsKnown`, `missingColumns`, `unknownColumns`, `notes`, per-issue `issues` (code, description, severity, whether TeachWell already checks it and which check), and for a Courses file a `courses` reconciliation summary plus `unmatchedCourseIds`.
- 400 for a non-CSV name, a file that is not an Ally report, an empty or malformed upload, or more than one file; 413 above the size limit.

## Syllabus (ADR 019)
- `GET /courses/{id}/syllabus` (course access; others 404). Returns `source` (`Canvas`, `Pasted` or `None`), the `words` the checks measured, `suppliedAt`/`suppliedByName` for a paste, `importedAt` for a Canvas one, `canEdit` (owner only), a plain-language `note`, and `text` **only for the owner**.
- `PUT /courses/{id}/syllabus {text}` (owner only; others 404). Stores the pasted syllabus, which the syllabus checks then run against. Plain text and pasted HTML are both accepted and go through the same sanitizer as an import. 400 when the text is empty, holds no words, or exceeds 100,000 characters.
- `DELETE /courses/{id}/syllabus` (owner only; others 404, and 404 when nothing is pasted). Clears the paste and hands the element back to the importer; the element and its snapshots are retained.
- A pasted syllabus outranks the importer: re-importing the course neither overwrites it nor marks it removed. The course's improvement guide states a limitation when the course has no syllabus at all, so a gap does not read as a clean syllabus.

- A course's imported elements include its Canvas **syllabus** when it has one (ADR 019): type `syllabus`, a fixed source id, and no module. It is checked like a page and appears in the improvement guide, but it is not reviewable element by element and is not a rubric criterion target.
- Page and assignment evidence carries `media` from evidence version 2 (ADR 018 amendment): one entry per video or audio player, with its `kind` (Video, Audio or Embed), external `provider` host if any, accessible `title`, and `hasCaptionTrack`. The last is true or false for an HTML player, and **null for an embed**, where the page cannot settle whether the hosted media is captioned. `media` is null, not empty, on snapshots taken before version 2, and the captions checks then report a limitation rather than passing. No `src`, URL or markup is ever stored.

## Accessibility conformance report (ADR 018)
- `GET /courses/{id}/conformance` (the same course access as the guide, which it projects; others 404, and the Academic administrator role 403). It stores nothing and recomputes nothing: every figure comes from the guide. It returns:
  - the course, the reference standard (`standard`), `asOf` (when the report was produced) and `contentAsOf` (when the content was captured) — both, because they differ
  - the standards applied, `checkedItems`, and the latest `endorsement` wording with `endorsedAt` when the course has one
  - `criteriaInScope`, `criteriaOutOfScope` (how many of the standard's 50 Level A and AA criteria TeachWell has no check for), and counts of criteria by state
  - `criteria`, in criterion order: number, name, level, reference, `state`, the related `checks`, `checksRan`, `fullyCovered`, and open, done and not-applicable place counts
  - `caveat` and `limitations`
- `state` is `Findings` (an open place), `NoFindings` (the checks that ran found nothing) or `NotChecked` (no related check ran, because the standards do not enable one, the course has no content it applies to, or the snapshot lacked the evidence). It is never "pass" or "compliant": an automated check can show an issue was found, never that a criterion is satisfied.
- `GET /courses/{id}/conformance/export` returns the same report as CSV, one row per criterion, with the scope, both dates, the applied standards, the endorsement and every limitation below the rows.
- `GET /standards/resources`, `POST /standards/resources {checkCode, articleId | workshopId}`, `DELETE /standards/resources/{linkId}` (CTL role; others 403). These list, link and unlink CTL resources on guide steps. Each returns the full list, with each link's check, kind, title and whether instructors see it. 400 when:
  - the check is unknown
  - both or neither resources are given
  - the resource does not exist
  - the resource is already linked
  - the check already has 5 resources
- `GET /standards/feedback` (CTL role; others 403). Returns `{scope, courses, checks}`:
  - `scope`: description, generation date and caveats
  - `courses`: the number of courses with any guide mark
  - `checks`, one per check: Not-applicable and Done mark counts, the Not-applicable percentage, the number of courses and a `tuningCandidate` flag. Each also has up to 10 recent `reasons` (course title, reason, date), only from courses the caller can access, and `reasonsNotShown`.

## Readiness review and endorsement (ADR 013)
- `GET /courses/{id}/readiness` (owner or a reviewer with course access; others 404). Returns:
  - `isOwner`, `canRequest`, `blockers`, `requiredOpenSteps`
  - `active` and `lastDecision` requests, and `history`
  - `endorsement`: its wording, reviewer, dates, summary, `recheckDue` and `recheckReasons`
  - `canDecide` and `hasQualifyingReview`
- `POST /courses/{id}/readiness {note}` (owner; others 404). 409 if a request is already open. 400 while required guide steps are open, or for a note over 2,000 characters.
- `POST /readiness/{requestId}/withdraw {revision}` (owner). 409 if stale or not open.
- The course readiness response also lists `requirements` (label, met, detail) for the deciding reviewer, from the standard-set version pinned on the request. `hasQualifyingReview` is true when all are met.
- Standard-set versions carry `endorsement {courseReview, rubricProfileId, minimumItemReviews}`. `PUT` on a draft accepts it (optional; null keeps it). It is 400 for more than 20 item reviews, or for a rubric without a published version.
- `GET /courses?role=…` items carry `readiness` and `readinessLabel` for the course-card badge.
- `GET /notifications` (any signed-in user): `{unread, items[50]: {id, kind, title, body, courseId, createdAt, read}}`. `POST /notifications/{id}/read` (404 for anyone else's) and `POST /notifications/read-all` return the same list.
- `GET /readiness/queue` (CTL role; others 403): open requests in the tenant, plus up to 20 recent decisions by the caller. `guide` is null unless the caller has course access.
- `POST /readiness/{requestId}/take {revision}` (CTL). Shares the course with the caller. 409 if stale, already taken, or the caller made the request.
- `POST /readiness/{requestId}/decide {revision, decision: "Endorsed" | "ChangesRequested", summary}` (only the reviewer who took it; others 404). Returns the course readiness. 400 for:
  - an unknown decision
  - an empty summary, or one over 4,000 characters
  - endorsing without a review this reviewer published since the request
  409 if stale or not in review.

## Oversight (ADR 014)
- `GET /oversight?department=` (CTLReviewer or AcademicAdmin; others 403, and 404 if a role was just revoked). Returns:
  - `scope`: description, generation time and caveats
  - `department` (canonical name) and `departments`
  - `courses`, `coursesChecked` and `readiness` state counts
  - `issuesByAspect`, `topChecks` (10) and `helpRequests` from guide steps
  - weekly `progress` (12 weeks)
  - `courseRows`, each with `canOpen` (only where the caller has access)
  - `endorsements` (50)
  - `canRefresh` (CTL)
  - No instructor or reviewer names.
- `GET /oversight` also accepts `program`. It returns `program`, `programs`, `autoRecheckHours` and `lastAutomaticRecheck`; course rows add `program`, `endorsedAt` and `endorsementValidUntil`.
- `GET /oversight/export?department=&program=` (CTLReviewer or AcademicAdmin): the course table as a UTF-8 CSV attachment, with formula-safe values.
- `PUT /courses/{id}/classification` accepts `program` (optional; blank clears it; 120 characters max). The course standards response includes it.
- `GET /admin/audit?action=&actorId=&from=&to=&before=` (SystemAdmin): `{items[100]: {id, timestamp, actorId, actorName, action, entityId, entityLabel, subjectName}, nextBefore, actions}`, newest first. 400 when `from` is after `to`.
- `GET /getting-started` (Instructor or CTLReviewer): `{broughtInACourse, markedAStep, askedForReadiness, endorsed, courseId, courseTitle}` for the caller's own courses only (ADR 016).
- `GET /glossary` (Instructor or CTLReviewer): `[{termId, term, definition, phrases, tags, links: [{label, source, url}], hidden, updatedAt, updatedByName, revision}]`, the college's changes to the built-in glossary.
- `PUT /glossary/{termId}` (CTLReviewer): `{term, definition, phrases, tags, links, hidden, revision}`. Revision is required when the entry exists (409 if stale); links must be absolute http(s) without credentials. `DELETE /glossary/{termId}` returns a built-in term to TeachWell's wording or removes a college term, and returns the remaining entries.
- `POST /oversight/refresh` (CTLReviewer): builds every course's guide in the tenant to update the figures. Returns `{checkedCourses}`. Audited.
- `POST /support/tickets` accepts an optional `origin: "Guide"` (anything else is 400); the improvement guide's "Ask the CTL for help" sends it.

## DTO conventions
- Use UUIDs for local IDs and separate strings/integers for Canvas external IDs.
- Include `permissions` flags in resource responses for UI, but always re-check on server.
- Every finding links to rubric criterion, evidence, source element and review snapshot.
- Pagination for course elements, reviews, tickets and comments.
- ETags or row-version checks for concurrent editing.
