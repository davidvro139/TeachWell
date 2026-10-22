# ADR 004: Bounded read-only course import and snapshots

Accepted October 15, 2026. Docker remains deferred.

## API and authorization

List the current Canvas user's courses using `GET /api/v1/courses?enrollment_type=teacher&enrollment_state=active`, following Canvas pagination links. Re-fetch a selected course directly and require the current user's enrollment metadata to explicitly report type `teacher` and state `active` before fetching content. Administrative app roles, a successful profile validation, a user-entered course ID, and earlier list results are not proof of current Canvas course access. Unsupported enrollment types and incomplete enrollment metadata fail closed.

References: Instructure's [Courses API](https://developerdocs.instructure.com/services/canvas/resources/courses), [Modules API](https://developerdocs.instructure.com/services/canvas/resources/modules), [Pages API](https://developerdocs.instructure.com/services/canvas/resources/pages), [Assignments API](https://developerdocs.instructure.com/services/canvas/resources/assignments), and [pagination contract](https://developerdocs.instructure.com/services/canvas/basics/file.pagination).

TeachWell exposes `GET /canvas/connections/{id}/courses` and `POST /courses/import` with `{connectionId,sourceCourseId}`. Both require an active local Instructor/CTLReviewer identity and that user's connection in the same tenant. Import requires CSRF. CTL users can see their own imported copies in their CTL view. Sharing remains explicitly owner-controlled and requires the Instructor app role; no role is automatically granted by Canvas. Each importer gets a separate private local copy; there is no tenant-wide deduplication or automatic sharing.

## Network and resource boundaries

Reuse encrypted server-only credentials and the existing approved-host, public-IP socket, TLS and redirect protections. Follow opaque Link URLs only when their origin and collection path match the original request (optional `.json` suffix accepted). Reject embedded credentials, fragments and token query parameters before sending any request. Links never reach the frontend. Detect cycles and duplicate remote records rather than returning an incomplete list silently.

Requests are GET-only. Cap each collection at 40 pages / 2,000 records, each operation at 200 request attempts and 90 seconds of remote work, each response at the existing 64 KiB client buffer, HTML bodies at 60,000 characters, and a complete course at 3,000 normalized elements. Large courses/content beyond these prototype limits fail explicitly. Retry 429/503 at most twice, honoring a Retry-After delta/date of up to three seconds; longer waits are returned to the user. Cancellation and other failures discard the pending course update.

Remote token rejection or reached declared expiry clears the saved credential with a revision check and audit event. A course-specific 403 does not invalidate a potentially valid credential. Existing course content stays unchanged on failure, even if connection status is updated for an invalid token.

## Normalization

Fetch page metadata plus individual page bodies, assignment descriptions, module metadata, and paginated module items. Never request rosters, submissions, grades, file binaries, discussion content or quiz content. Ignore returned profile/assignment/student-related fields that are outside the allowlist of parsed course-design fields.

Parse HTML using patched AngleSharp with no resource loader or JavaScript engine. Remove script/style/embedded/form/SVG/MathML nodes and produce inert plain text with block separators using a bounded iterative traversal. No imported HTML is rendered or persisted. Canonical source links are constructed from the approved origin and validated IDs/escaped page slugs; remote `html_url`, external URLs and embeds are not followed or trusted. The SPA additionally checks link scheme and origin and renders text via React escaping.

Preserve separate moduleItem elements pointing at canonical page/assignment elements, so repeated placements do not duplicate content or lose module context. Unplaced pages/assignments remain visible in Other course content. Unsupported module item types and missing targets are labeled Unavailable with warnings; SubHeader items are structural labels. A page-body 403/404 produces an unavailable page and warning; failures of entire collections abort the course update. Publication state is shown without asserting student visibility; missing publication metadata is Unknown rather than presumed published.

Plain text omits media, layout, rich links and HTML accessibility structure. Before Phase 3 HTML-based automated checks, add sanitized structural evidence/metadata through a new import snapshot version; do not infer accessibility conformance from current text snapshots. Canvas can change during the sequential fetch, so import timestamps describe TeachWell's capture, not a transactionally consistent Canvas revision or complete Canvas history.

## Persistence and concurrency

Identify a local imported course by `(tenant, importer, origin, external course ID)`. Identify an element within it by `(type, external element ID)`; Canvas numeric IDs can overlap between types. All local IDs are app-assigned immutable UUIDs. Reimport preserves those IDs and all explicit shares. Missing prior elements are marked Removed rather than deleted.

Fetch and normalize everything before modifying course entities. Save course metadata, elements, changed-content snapshots, connection revision and audit event in one transaction. Course and connection revision checks prevent overlapping refreshes or a disconnect/credential replacement from committing stale results. Conflicts return 409 and require refresh/retry. Imports are synchronous and bounded in this prototype; large institutional sync belongs in a persistent background-job design later.

Snapshots contain title, inert text, source link/status, typed metadata, UTC capture time and a SHA-256 content hash. Identical content does not create duplicate snapshots. Changing or removing content appends a new snapshot while retaining old ones. EF rejects snapshot edits/deletes; a PostgreSQL trigger also rejects direct UPDATE/DELETE. The current reader loads only the newest snapshot per element. A future authorized review/history endpoint can read prior immutable snapshots directly.

Backfill snapshots idempotently for the fictional seeds when seeding runs. Do not seed live credentials or real institutional content. The stored course source serves both offline samples and authorized imported copies, without network calls during ordinary dashboard browsing. Disconnecting Canvas does not remove already-imported content or revoke explicit TeachWell shares.

## Verification boundary

Use explicit Canvas HTTP doubles for adapter tests and generated native PostgreSQL databases for migration/transaction tests. Enabled integration UI tests explicitly mock HTTP; identity/sharing and disabled mode also run against the real local API. No authorized live Canvas account has been tested, so Phase 2's live acceptance criterion remains outstanding. More Canvas content types, broad administrative/designer/TA enrollment support, background import jobs and historical snapshot browsing are not claimed as complete.
