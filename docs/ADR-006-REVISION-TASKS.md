# ADR 006: Revision tasks

Accepted October 16, 2026. Docker remains deferred.

## Scope decision: single-element tasks

`DATA_MODEL.md` permitted a revision task to group findings "from the same authorized course," without restricting them to one element. This phase restricts a task to confirmed findings from Published reviews of **one** course element (page or assignment), because the task's source/current snapshot comparison, demo simulated update and Canvas source link all describe one piece of content. A CTL reviewer who wants to address issues on two elements opens two tasks. This is a deliberate narrowing, not an oversight; multi-element tasks are not implemented.

## Status machine

`Draft` (CTL, not yet visible to the assignee) → `NeedsRevision` (CTL assigns) → `InProgress` (instructor starts) → `ReadyForReview` (instructor submits) → `Resolved` (CTL). `ReadyForReview` → `ChangesRequested` (CTL) → `InProgress` or directly back to `ReadyForReview` (instructor), matching WORKFLOWS.md. `Blocked` is reachable from any non-terminal, non-Draft status by either party with a required reason, and resumes only to the status it was blocked from — a task cannot skip straight from `Blocked` to an arbitrary status. `Withdrawn` and `NotApplicable` are CTL-only exits with a required reason; `Resolved` is terminal. No terminal status can be reopened in this phase, unlike support tickets' documented reopen path.

CTL authority for `Creator`-actor transitions is course-level: any CTLReviewer with access to the task's course, not only the user who created it, matching how review and sharing authorization already work. The assignee is always the course owner (the only Instructor-role identity with access to a course in the current ACL model); `CreateTaskInput` does not accept an assignee from the client, since there is exactly one valid value.

## Draft visibility and findings eligibility

A `Draft` task is visible only to CTL reviewers with course access, mirroring how an unpublished `ElementReview` is visible only to its author. Creation requires every supplied finding to be `Confirmed` on a `Published` review of the same course and the same element, and rejects a finding already attached to another **open** (non-terminal) task — once that task reaches a terminal status, the finding can be regrouped into a new one. `AvailableActions` returned on the task DTO are computed server-side from the actor's actual authority, so the UI never has to re-derive the permission rules.

## Snapshot comparison and the demo-only simulated update

`SourceSnapshotId` is fixed at creation to the element's newest snapshot at that moment and never changes. `GET /revision-tasks/{id}/comparison` always compares it against the element's **current** newest snapshot — a live, dynamic comparison, not a cached one — using a minimal LCS line-level diff (`TextDiff` in Domain) over plain text only; no HTML is diffed or rendered. `ReadyForReviewSnapshotId` is recorded separately each time the task reaches `ReadyForReview`, so the task carries a record of what the instructor actually submitted even if content changes again afterward.

By design, TeachWell never fabricates a working Canvas integration. Real Canvas-origin courses pick up content changes only through re-running `POST /courses/import` (ADR 004); there is no live write-back. For the fictional demo courses, which have no Canvas connection to refresh from, `POST /revision-tasks/{id}/simulate-update` lets the assignee record replacement plain text for the element, producing a new immutable snapshot. This is explicitly labeled in the UI as a demo simulation of an out-of-Canvas edit, not a real one, and the endpoint rejects the call outright for any non-demo course. A text-only simulated edit carries no sanitized HTML, so it clears the element's structural evidence rather than keeping the old evidence against new text; the next automated check run reports this as a limitation, not a false pass.

## Comments and history

Comments do not carry an optimistic-concurrency revision: they are additive and should not conflict with a concurrent status change or another comment. They remain visible after a task reaches a terminal status, so closing context (e.g., a thank-you) is not lost. `TaskEvent` rows are append-only, enforced by the same EF `SaveChanges` guard and PostgreSQL trigger pattern used for `ElementSnapshot`; every transition, comment, and simulated update additionally writes a system `AuditLog` entry.

## Not included / limitations

- Multi-element tasks, module- or course-wide tasks.
- Reopening a `Resolved`, `Withdrawn` or `NotApplicable` task.
- Automatically linking a task's resolution to a specific follow-up `ElementReview`; a CTL reviewer verifies the comparison and/or starts a new review independently, then resolves the task manually.
- Notifications (in-app or email) when a task changes status; the instructor and CTL dashboards are pull-based (`GET /revision-tasks?status=`).
- A richer HTML-aware diff; the comparison is plain-text, line-level only.
