# ADR 014: CTL and administrator oversight (Phase 11)

Accepted October 19, 2026. This implements Phase 11 of the project roadmap under owner decision 3 ("results are visible to the CTL and to an administrator") and D3 ("a new read-only Academic administrator role").

## Context

Phases 9 and 10 produce per-course results: improvement-guide progress, readiness requests and endorsements. Phase 11 makes them visible across the college to two audiences:
- CTL reviewers, who need to see where to spend coaching time
- academic leaders, who need readiness and trends without reading course content

Both views must describe courses, not people.

## Decisions

### 1. A new Academic administrator role (D3)

`AcademicAdmin` is a fourth role:
- It can read the oversight view, and nothing else.
- It gets no course list, no course page and no guide.
- It has no readiness, review, task or support actions, and no editing of any kind.
- Course content still requires ownership or an explicit share. If someone holding this role is also shared on a course, the shared course opens, but without the guide, readiness, review and report sections, which belong to people who teach or review it.

SystemAdmin remains an IT-configuration role with no course access (ADR 001). The demo adds Dana Brooks (`academic@demo.teachwell.test`) to the baseline seed and to the one-click sign-in list.

### 2. Role grants are audited and take effect promptly

A SystemAdmin can grant or revoke Instructor, CTLReviewer and AcademicAdmin on the "People and roles" panel (`PUT /admin/users/{id}/roles`). SystemAdmin itself is never grantable in the app, so the app cannot escalate anyone to IT administration.

Each change:
- is audited as `RoleGranted:{role}` or `RoleRevoked:{role}`, with the person as the subject
- renews their security stamp

Sessions are revalidated every minute (`SecurityStampValidatorOptions.ValidationInterval`), so a revoked role stops working within a minute. Until then, the oversight service re-reads roles from the database on every request and refuses a revoked viewer immediately, even while the session cookie still carries the old role claim.

### 3. Figures come from a daily per-course guide record

Every time a course's improvement guide is built (opened, marked, or re-checked), TeachWell writes or updates one figures-only row for that course and UTC day (`CourseGuideRecords`). The row holds:
- the standards in force
- items checked
- open and required steps
- open, done and not-applicable places
- open places by area and by check

There is no content and no names. Oversight aggregates each course's latest row, and the rows over time give progress.

- **Why not compute live?** Running every course's guide on each oversight load would be slow for a college and would recompute the same thing many times.
- **Why a write on read?** The guide is already computed when it is opened, and writing one small row is cheap. Concurrent first writes for the same course and day are tolerated: the second is dropped.
- **Re-check all courses.** CTL reviewers can run every course's guide now (`POST /oversight/refresh`, audited as `OversightRefreshed`), so courses nobody has opened are included. Academic administrators cannot trigger work. The full demo seed runs it once.

### 4. What oversight shows

Oversight can be filtered by department (D4). It shows:
- **Courses and courses checked.**
- **Readiness states:**
  - Endorsed, or Endorsed with re-check due (ADR 013's D2 rules)
  - In review or Requested (an open request takes precedence)
  - Changes requested (when it is later than any endorsement)
  - Not requested
- **Issues:** each area's open places and the number of checked courses affected; the 10 most common issues by courses affected.
- **Help requests from guide steps:** total, open, and by category. Tickets now record an `Origin`, which is "Guide" for requests sent from a guide step.
- **Guide progress over time:** for each of the last 12 weeks, the sum of each course's latest record on or before that week's end.
- **A course table:** code, title, department, mode, readiness, open and required steps, and last checked. The course links only where the viewer already has access.
- **Endorsement history:** up to 50, with their ADR 013 wording.

### 5. Courses, not people

- The response contains no instructor or reviewer names. Endorsements appear without the reviewer; there are no rankings or league tables.
- Course titles and figures are shown to all CTL reviewers and Academic administrators in the tenant, because oversight is about courses. This is consistent with the readiness queue (ADR 013) and support triage (ADR 007).
- Every view carries a scope, a generation time and caveats, as ADR 008 reports do.

## Consequences

- A CTL reviewer sees figures for courses not shared with them, but cannot open them.
- Figures are as fresh as each course's last guide check. The table says when that was, and the CTL can re-check everything.
- "Progress over time" moves when courses are re-checked, not only when content changes. The caveats say so.

## Not built

- charts beyond simple bars and tables

Program filtering, CSV export, scheduled re-checks and the audit viewer were added the same day; see the amendment below.

## Amendment: programs, CSV export, scheduled re-checks and the audit viewer (October 20, 2026)

### Programs

Courses gain an optional **program** (for example "Web Development AAS"). The CTL maintains it alongside delivery mode and department in the course's classification form (D4).
- A program is for oversight filtering and aggregation only. It does not affect which standards apply, so D1 is unchanged.
- Oversight filters by department, program, or both. The response returns each filter's canonical name and the lists of departments and programs in use, and the course table shows each course's program.
- The full demo seed gives WEB 101 and DES 120 "Web Development AAS", and DATA 150 "Data Analytics Certificate".

### CSV export

`GET /oversight/export?department=&program=` (CTL reviewers and Academic administrators) downloads the course table with the same filters:
- **Columns:** code, title, department, program, mode, readiness, open and required steps, last checked, endorsed on, endorsement current until.
- **Names:** there are none, as on screen.
- **The file:** UTF-8 with a byte-order mark so spreadsheets read accents, named `teachwell-oversight-{filter or college}-{date}.csv`.
- **Formula safety:** a shared `Csv` helper prefixes any value that a spreadsheet would treat as a formula (starting with `=`, `+`, `-`, `@`, tab or carriage return) with an apostrophe. A course title can therefore never run as a formula. The revision-task CSV export now uses the same helper.
- **Where it is:** the page links to it as **Download CSV**; the browser sends the session cookie and saves the file.
- **Caveats:** they stay on the page and in ADR 014, not in the file, so the CSV remains a plain table.

### Scheduled re-checks

`OversightRecheckService` is a background service inside the API.
- **Timing:** it starts after `Oversight:AutoRecheckStartMinutes` (default 5), then repeats every `Oversight:AutoRecheckHours` (default 24; 0 turns it off). Both are validated at startup.
- **Each run:** re-builds every tenant's course guides in its own scope, as "Re-check all courses" does. Each run is audited per tenant as `OversightAutoRechecked` by the system actor (`Guid.Empty`) against the tenant.
- **On the page:** oversight shows the interval and when the last automatic run happened.
- **Failures:** a failed run is logged and retried at the next interval; it never stops the app.
- **Several API instances:** each instance would run it. The work is idempotent (one record per course per day), only repeated. A single scheduler is left to deployment.
- **Tests:** integration tests turn it off and call `RunOnceAsync` directly.

### Audit viewer

The SystemAdmin workspace gains **Audit trail** (`GET /admin/audit`):
- **Order and filters:** newest first; filter by action, person (including "TeachWell (automatic)") and date range.
- **Paging:** 100 entries per page, using a timestamp cursor ("Load older entries").
- **Labels:** entries show readable names for the people and courses they refer to, and an id otherwise.
- **Read-only:** viewing the trail is not itself audited.
- **Tenant scope:** the audit table has no tenant column, so an entry is in a tenant's trail when its actor belongs to that tenant, or when it is a system entry recorded against the tenant. A future multi-tenant deployment should add a tenant column.
- **Access:** SystemAdmin only. Academic administrators and CTL reviewers get 403, because the trail names people and Academic administrators see results, not activity.
- **Indexes:** new indexes on the timestamp, and on actor plus timestamp, keep it fast.

### Verification

- SQLite suite: 204 passed, 0 failed.
- New `OversightTests`:
  - **Programs and CSV:** classification trims and stores the program. Oversight filters by program case-insensitively. The CSV has a BOM, a scoped filename, one row per course, no names, and an apostrophe-escaped formula title. Instructors get 403.
  - **Scheduled re-check:** an out-of-range setting is rejected. `RunOnceAsync` records both courses, and oversight then shows them checked with the last automatic run. It is audited as the system.
  - **Audit viewer:**
    - newest-first ordering and the 100-entry page with its cursor, giving a second page of 20 with no overlap
    - action and person filters
    - the system actor and "This college" labels
    - another tenant's entry never shown
    - a reversed date range is 400; instructors and Academic administrators get 403
- Headless Edge: 18 of 18 pass. `oversight.spec.ts` now covers:
  - the program filter and the Download CSV link, including the file's type and content
  - the SystemAdmin finding the role change in the audit trail by action, with who did it and to whom
