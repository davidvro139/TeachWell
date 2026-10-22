# ADR 007: Faculty Support Center

Accepted October 16, 2026. Docker remains deferred.

## Scope

Implements the three pieces WORKFLOWS.md describes: support tickets with triage/assignment/comments/lifecycle, a knowledge base of job aids, and a workshop listing with registration. The accept criterion — an instructor requests help, a CTL reviewer responds and closes the ticket, and the revision task stays independent — is met with fictional demo content.

## Ticket visibility is tenant-wide, not course-scoped

A support request is a help-desk conversation, not course content: any active CTLReviewer in the tenant can see and triage any ticket (`GET /support/tickets?scope=queue`), whether or not they currently have access to a course the ticket happens to reference. This mirrors "CTL: triage queue" in WORKFLOWS.md as a shared queue, not a per-course one, and is distinct from revision tasks (ADR 006), whose `Creator` authority is still course-scoped.

A ticket's requester always sees their own tickets (`GET /support/tickets`, the default `scope=mine`); an assignee sees tickets assigned to them. A plain Instructor cannot see the triage queue or another requester's ticket. `Requester`/`Responder`/`Either` actor rules on the status machine mirror the `Creator`/`Assignee`/`Either` pattern from ADR 006.

Because the queue is tenant-wide but course access is not, a ticket's optional link to a course, element or revision task is redacted per viewer: `LinkedCourse` is included only when the current viewer currently has course access, computed fresh on every read (not cached from creation time), consistent with "information already viewed cannot be recalled, but access is re-checked on every request" elsewhere in this app. The ticket itself, its category/title/description/comments, remains visible regardless — only the human-readable course/element/task titles are withheld. A ticket may reference a `RevisionTaskId` only when the requester can see that task under its own visibility rule (ADR 006): a `Draft` task is referenceable only by the CTL reviewer who created it.

## Status machine

`New → Triaged → InProgress → WaitingOnInstructor → Resolved → Closed`, matching WORKFLOWS.md, with `WaitingOnInstructor → InProgress` (resume) available to either party and a `reopen` path: `Resolved` or `Closed → InProgress`, available to either party with a required reason. Unlike revision tasks, no ticket status is a dead end — every status has at least one way forward, matching "(reopen allowed)" in the source document. `WaitingOnInstructor → Resolved` and `InProgress → Resolved` both require a short resolution note; `InProgress → WaitingOnInstructor` requires a note explaining what is needed. Closing a ticket sets `ClosedAt`; reopening clears it. Closing or resolving a ticket never changes the linked revision task's own status — the two lifecycles are independent, per the accept criterion.

## Assignment and recommendation are separate actions from the transition

`PATCH /support/tickets/{id}/assignment` sets or clears the assignee independently of status, and `PATCH /support/tickets/{id}/recommendation` attaches a knowledge article and/or workshop, matching the proposed contract's separate routes. Both are CTL-only, enforced by a role-gated route group (403 for the wrong role) rather than a service-level check, matching how revision task creation and course sharing are already gated — a service-level `IsReviewer` check remains as defense-in-depth but is normally unreachable through the real API. Recommendation is full-replacement: each call sets both fields; a null clears that recommendation. Reassignment is written to the system audit log but, unlike status transitions, does not appear in the ticket's own `SupportEvent` timeline, whose shape (`fromStatus`/`toStatus`/`reason`) does not fit an assignment change.

## Knowledge articles are editable, not immutable

Published rubric versions and reviews are permanently frozen (ADR 005) because they are scored evidence. A job aid is editorial content with no such requirement: `KnowledgeArticle` can be edited after publishing, incrementing `Version` and re-stamping `PublishedAt` each time it is saved while `Published`, and can be toggled back to `Draft`. The slug is assigned once at creation and never changes, even across a title edit, so a bookmarked `/knowledge/{slug}` link never breaks; a collision appends `-2`, `-3`, etc. A `Draft` article is visible only to CTL reviewers; instructors see `Published` articles only.

## Workshops are deliberately minimal

Per the plan's "simple workshops/training listing" framing, this phase implements creation, listing, registration with capacity-aware waitlisting, and cancellation with promotion of the longest-waiting registrant — not attendance tracking, editing, or cancellation of a scheduled workshop by its creator. The full registrant roster (names) is visible only to CTL reviewers; an instructor sees counts and their own status only, which this ADR treats as the appropriate default disclosure boundary absent an explicit institutional policy on roster visibility.

## A real bug found and fixed in this phase

A browser `datetime-local`/`date` input is timezone-naive; the value it produces has no UTC offset. Deserialized server-side, this produces a `DateTimeKind.Unspecified` value, which PostgreSQL's `timestamptz` columns reject outright — surfacing as an opaque 409 "changed at the same time" conflict with no relation to the real cause. This was caught by the browser test suite, not the API integration suite, because .NET-to-.NET JSON round-trips in the backend tests already preserved `DateTimeKind.Utc`. Fixed in two layers, per the project's UTC-storage requirement: the frontend now converts a picked wall-clock time to a UTC instant (`new Date(value).toISOString()`) before sending it, for both the revision task due date (ADR 006) and workshop start/end times; the server additionally normalizes any non-UTC `DateTime` it receives (`AsUtc`) rather than crashing, as defense against any other client. Regression tests assert the normalization directly against native PostgreSQL by sending a deliberately timezone-naive ISO string.

## Known test-infrastructure limitation

The browser end-to-end suite's total real logins across all spec files (11, after this phase's two additions) now sits at or past the login endpoint's rate limit (10 per IP per minute — a legitimate brute-force control, see ADR 001/002). Running the full suite can intermittently 429 whichever spec's login lands eleventh within the one-minute window; this reproduced on `task.spec.ts`, which this phase did not modify and which passes on its own. This is a test-suite capacity issue, not a product defect, and was not fixed by weakening the rate limiter. A real fix — Playwright `storageState` session reuse across specs, cutting redundant logins — is a reasonable follow-up to the shared `e2e/auth.ts` helper, not attempted here to avoid destabilizing already-passing specs under time pressure.

## Not included / limitations

- Attachments on tickets/comments (WORKFLOWS.md lists an "attachments policy" as a field to design later).
- Editing or cancelling a scheduled workshop, or recording attendance.
- Email notifications; everything here is pull-based (`GET /support/tickets?status=`).
- Resource recommendation driven automatically by review findings (explicitly a "Later" item in WORKFLOWS.md).
- Knowledge article version history (only the current body/version number is kept, not prior revisions).
