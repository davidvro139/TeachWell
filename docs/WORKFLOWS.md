# Reviews, Revision Tasks, and Faculty Support Workflows

## Terminology
- **Element Review**: a CTL or authorized instructor evaluation of one Canvas item against a versioned rubric.
- **Review Finding**: specific issue/opportunity with evidence, severity, recommendation and source element.
- **Revision Task**: actionable instructor work item addressing one or more findings.
- **Support Ticket**: assistance request handled by CTL; can link to finding/task but closes independently.

## Element review workflow
1. CTL opens a course they are authorized to review; selects Canvas element.
2. System fetches/loads and stores immutable snapshot with source URL, timestamp, hash.
3. Reviewer chooses rubric version, rates criteria (0–3, N/A, Not reviewed), attaches comments/evidence.
4. Automated checks may propose findings, which reviewer confirms/edits/rejects.
5. Reviewer publishes findings and groups related ones into revision tasks.
6. Instructor sees task, source link, requested changes, due date, reviewer and comment thread.
7. Instructor edits **in Canvas**, not TeachWell; marks task ready for review (optional explanation).
8. TeachWell refetches if authorized, captures new snapshot and compares sanitized content. If no access, ask for manual evidence.
9. CTL verifies and resolves, or requests additional changes; maintain immutable event history.

## Task status machine
`Draft` (CTL) → `NeedsRevision` (Instructor) → `InProgress` (Instructor) → `ReadyForReview` (CTL) → `Resolved` (CTL).
`ReadyForReview` → `ChangesRequested` (Instructor) → `InProgress` or `ReadyForReview`.
Additional terminal/exception states: `Withdrawn`, `NotApplicable`, `Blocked` (with reason; blocked may resume).

## Permission rules
- CTL can draft/publish reviews and assign tasks **only for authorized courses**.
- Instructor can view/respond/update their own assigned tasks and submit for re-review; cannot approve their own CTL review.
- CTL can reassign review owner and resolve findings; record all changes.
- System admin manages application settings; Canvas data access still requires explicit grant.
- Notifications initially in-app; email optional later.

## Side-by-side comparison
Display reviewed snapshot and newest fetched snapshot, highlighting text/HTML differences and metadata changes. Provide fallback when snapshots cannot be compared reliably. Do not claim Canvas provides complete revision history; TeachWell owns its review snapshots.

## Support tickets
Ticket fields: requester, authorized course/element optional, linked finding/task optional, category (Canvas, accessibility, course design, multimedia, other), description, priority, status, assignee, comments, attachments policy, timestamps, resolution.
Status: `New` → `Triaged` → `InProgress` → `WaitingOnInstructor` → `Resolved` → `Closed` (reopen allowed).
CTL may attach knowledge-base article or workshop recommendation. Closing ticket does not resolve the revision task.

## Faculty Support Center
- Instructor: submit/track support tickets, view job aids, workshops, and training history.
- CTL: triage queue, assign tickets, consult, publish/version job aids, schedule workshops, log consultations.
- Later: resource recommendation based on review findings; feedback surveys; institutional needs analysis.

## Examples
**Accessibility:** CTL reviews an assignment; flags image lacking useful alt text; creates finding and revision task; instructor requests help; CTL provides job aid; instructor updates Canvas; CTL verifies and resolves task.

**Competency alignment:** CTL reviews programming assignment; learning objective is vague and no rubric is attached; two findings grouped into one task; instructor updates instructions and rubric; CTL re-reviews and records outcome.
