# ADR 013: Readiness review and CTL endorsement (Phase 10)

Accepted October 19, 2026. This implements Phase 10 of the project roadmap under owner decisions 2 ("completing the guided improvement process counts toward something") and D2 ("endorsements are dated and re-checked, never silently revoked").

## Context

The improvement guide (ADR 011) lets an instructor find and fix detectable issues without a reviewer. Phase 10 gives that work an outcome: the instructor asks the CTL for a readiness review, and the CTL either endorses the course against a named standard-set version or requests changes. The plan leaves several things open; they are decided here.

## Decisions

### 1. Who can request, and when

- Only the course owner (Instructor role) can request a readiness review. There is at most one active request (Requested or In review) per course.
- A request is refused while any **required** step in the improvement guide is open. A step is required when the course's standard set marks its check as required. A step is open if any of its places are open; a place marked Done but still found after the latest import counts as open. Places marked Not applicable count as justified.
- With no required checks in the applicable standard set, a request can be made at any time. The guide still shows what is open, and the CTL sees it.
- The owner can withdraw an active request.

### 2. What a request pins

When the request is made, it records:
- the standard-set version that applies (or "TeachWell built-in defaults")
- the date of the content it was made against
- the latest snapshot id of every item, used later to detect substantial change
- a summary of the instructor's guide progress: open steps, required steps still open, Done and Not-applicable counts, and every Not-applicable reason with its check and place

The CTL therefore sees the course as the instructor saw it when asking, even if the content or standards change later.

### 3. How the CTL gets course access

CTL reviewers may only open courses explicitly shared with them. The readiness queue is tenant-wide, like the support triage queue. It shows the course code and title, the requester and the date, which are needed to triage, but no content.

**Taking a request grants the reviewer review access to that course**, audited as `CourseSharedForReadinessReview`, with the instructor as the granting user. The request form tells the instructor this. Asking the CTL for a readiness review is the instructor's explicit consent to share with the reviewer who takes it. The owner can remove that access later, as with any share. Guide progress and Not-applicable reasons are shown only to reviewers with course access.

### 4. Deciding

- Only the reviewer who took the request can decide it, and only while it is In review.
- **Changes requested** requires a summary saying what to change. The instructor sees it on the course page. Detailed findings and revision tasks come from the CTL's ordinary reviews, which the guide already surfaces (ADR 011).
- **Endorsed** also requires a summary, and it requires at least one review of this course that the deciding reviewer published after the request was made. An endorsement must rest on a human review, not only on automated checks. The standard set does not yet say which reviews it calls for (deferred in ADR 011), so any published course-level or item review counts.
- A decided request is immutable, and so is an endorsement. Both are enforced in EF Core and, on PostgreSQL, by triggers.

### 5. The endorsement record

An endorsement records:
- the course and the request it came from
- the standard-set version (name and number), the content date and the pinned snapshot ids
- the reviewer, the decision date and the summary
- a "valid until" date

It is always worded "CTL-endorsed against {standard set} v{n} on {date}". It is never called accreditation, compliance or certification. A course can hold several endorsements over time, and the latest is shown. Later standard-set versions do not change it.

### 6. Re-check due (D2)

An endorsement is never revoked. It is shown as **re-check due**, with the reason, when either of these is true:
- its validity period has ended. `Readiness:ValidityMonths`, default 36 (3 years), is validated at startup (1–120).
- the course has changed substantially since it was endorsed. This means at least `Readiness:ChangeThresholdPercent` (default 25%, validated 1–100) of the course's current items are new, removed or changed compared with the pinned snapshots.

Either way, the instructor can make a new request.

### 7. Audit

The following are audited:
- requested, withdrawn, taken
- the course share made by taking a request
- changes requested, endorsed

## Consequences

- The course page gains a "CTL readiness review" section for the owner and for reviewers with access. CTL reviewers gain a "Readiness" queue.
- The queue exposes course titles to all CTL reviewers in the tenant. This is accepted, because the instructor asked the CTL for help, and it matches the support triage queue.
- An endorsement is only as specific as the reviews behind it. Until standard sets can require particular rubric versions, the CTL decides which reviews are enough.

## Status

Built October 19, 2026.

## As built

- **Data:**
  - `ReadinessRequests`: the pinned standards, content date, snapshot ids and guide summary, plus status, reviewer and decision
  - `Endorsements`: unique per request
  - Both are guarded in `AppDbContext` and by the PostgreSQL triggers `immutable_endorsements` and `immutable_decided_readiness`. Course foreign keys are `Restrict`, so an endorsement cannot be removed by deleting its course.
- **Configuration:** `Readiness:ValidityMonths` (1–120, default 36) and `Readiness:ChangeThresholdPercent` (1–100, default 25), both validated at startup.
- **Interface:**
  - Course page: a **CTL readiness review** section after the improvement guide. It shows the endorsement (or "re-check due" with reasons), the open request, the last "changes requested" message, the request form or what blocks it, and history. For the reviewer who took the request, it also shows the instructor's guide summary and the decision form. Endorsing stays disabled until that reviewer has published a review of the course since the request.
  - The section refreshes when the instructor marks guide progress, so the request button unlocks as soon as the last required step is resolved or justified.
  - CTL: a **Readiness** view lists open requests with **Take request**, which shares the course and opens it, plus the reviewer's recent decisions.
- A reviewer cannot take a request they made themselves, which matters for accounts holding both roles.
- **Demo data:** the full seed adds an endorsement of WEB 101 by Morgan Blake against the course's applicable set, and a waiting request for DATA 150. Both pin the real guide state at seed time.
- **Not built at first:** badges on the course list, notifications, and review requirements in standard sets. All three were added the same day; see the amendment below.

## Verification

- SQLite suite: 197 passed, 0 failed. Native PostgreSQL 17 suite, applying the `Readiness` migration and triggers: 197 passed, 0 failed.
- New tests:
  - `ReadinessUnitTests`:
    - the endorsement wording
    - re-check by validity period and by substantial change at and below the threshold, counting added and removed items
  - `ReadinessTests.Instructor_requests_ctl_requests_changes_then_endorses_after_a_review`:
    - access: a reviewer gets 404 on an unshared course and cannot request; a duplicate request is 409; an instructor gets 403 on the queue
    - the queue: course metadata but no guide detail before taking; taking shares the course and is audited
    - deciding: invalid decision and empty summary are 400; endorsing without a qualifying review is 400; changes requested; a decided request cannot be withdrawn
    - second round: re-request, take, publish a review, endorse, with the wording pinned to the college-wide v1
    - immutability: EF Core refuses changes to an endorsement; on PostgreSQL, direct SQL updates and deletes are refused by the triggers
  - `ReadinessTests.Required_guide_steps_block_a_request_until_resolved_or_justified_and_requests_can_be_withdrawn`:
    - an Online set requiring competencies blocks WEB 101 with a named blocker
    - marking the step Not applicable unblocks it, and the request pins the standards and the reason
    - withdrawal
- Headless Edge: 17 of 17 pass. The new `readiness.spec.ts`:
  - the instructor requests a review with a note
  - the CTL takes it from the queue; the Endorse option is disabled with an explanation; the CTL requests changes
  - the instructor sees the reason, asks again and withdraws, leaving WEB 101 as later specs expect
  - `import.spec.ts` gained a readiness fixture
- The full demo seed run twice against a throwaway PostgreSQL database leaves 2 requests, 1 endorsement and 3 resource links. Screenshots of the WEB 101 endorsement (owner view) and the CTL queue were checked by eye. That check caught the seeded request first showing placeholder guide figures; it now pins the real guide state.

## Amendment: badges, notifications and review requirements (October 19, 2026)

### Review requirements in standard sets

Each standard-set version now records which reviews an endorsement needs (`EndorsementJson`):
- **a course-level review**
- **a minimum number of different items reviewed** (pages, assignments, quizzes or discussions; 0–20)
- **optionally, a rubric** (a rubric profile with a published version) that those reviews must use

How it works:
- **Editing:** these settings are edited in a draft on the Standards page, frozen when the version is published, and carried into the next draft. Versions without them use the baseline rule.
- **Evaluation:** requirements are checked against the version pinned on the readiness request, not whatever is current, so later standards changes do not move the goalposts.
- **What counts:** reviews published after the request count, whoever wrote them. The deciding reviewer must still have published at least one of them; this is the baseline rule, and it always applies.
- **The checklist:** the deciding reviewer sees each requirement as met or not, with counts. Endorsing is refused with the unmet items listed.
- **Decision:** this keeps the CTL deciding the endorsement, but lets the standards say what evidence it needs. The rubric choice also closes ADR 011's deferred "standard set chooses rubric versions" for endorsement purposes. Reviews themselves can still use any published rubric.

### Badges on the course list

Course cards show the course's readiness state:
- Requested and In review, in blue
- Changes requested and Endorsed · re-check due, in amber
- Endorsed, in green
- Nothing for not requested

The state comes from `ReadinessStates`, the same logic as oversight (ADR 014), now shared.

### Notifications

In-app only; there is no mail service in this project. A notification is written in the same save as the action that causes it:
- **Readiness requested:** every active CTL reviewer in the tenant, except the requester.
- **Request taken:** the instructor, with the reviewer's name.
- **Endorsed or changes requested:** the instructor, with the reviewer's summary.
- **Withdrawn:** the reviewer who had taken it.

How it appears:
- A bell in the header shows the unread count. It refreshes on load, every minute while the tab is visible, and when opened.
- Opening a notification marks it read. A new request takes CTL reviewers to the Readiness queue, switching them to the CTL reviewer role; everything else opens the course.
- Notifications are personal: anyone else's notification is 404.
- The text holds only the course code and title, names and the decision summary.

The full demo seed adds Alex Morgan's WEB 101 endorsement notice and the CTL reviewers' DATA 150 request notice.

**Not built:** email or other channels, notification preferences, retention limits, and notifications for anything other than readiness.

### Verification

- SQLite suite: 201 passed, 0 failed.
- `Instructor_requests_ctl_requests_changes_then_endorses_after_a_review` now also checks:
  - the course-list badge from Not requested to Requested to Endorsed
  - the reviewer's request notification, and the instructor's taken, changes-requested and endorsed notifications
  - that marking someone else's notification is 404
  - that reading one notification and then all of them clears the count
- New `A_standard_set_version_says_which_reviews_an_endorsement_needs`:
  - out-of-range and unknown-rubric requirements are rejected
  - saved requirements are published and carried into the next draft
  - a request pins the version
  - endorsement is refused at "1 so far" of 2 items, then succeeds after a second item review
- Headless Edge: 18 of 18 pass. `readiness.spec.ts` now covers:
  - the reviewer reaching the queue from the notification bell
  - the requirement checklist with one unmet item
  - the instructor's "Changes requested" card badge and notification, and "Mark all read"
- Screenshots of the demo dashboard (bell open, Endorsed badge) and the requirements editor were checked by eye.
