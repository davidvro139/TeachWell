# ADR 005: Versioned rubrics, snapshot-pinned element reviews and deterministic checks

Accepted October 16, 2026. Docker remains deferred.

## Structural evidence (snapshot evidence version 1)

ADR 004 required sanitized structural evidence before HTML-based checks. The importer now derives, from the same AngleSharp document used for plain text (no scripting, no resource loading, dangerous nodes removed first), a JSON evidence record per page/assignment:

- images: index, `alt` (null when the attribute is absent, distinct from `alt=""`), decorative flag (`role=presentation|none` or `aria-hidden`);
- headings: level and collapsed text;
- links: accessible text (aria-label, text, or nested image alt) and a destination *kind* (Internal, External, Email, Anchor, Invalid). Only an external host name is kept; full URLs, paths and query strings are discarded because they can carry secrets;
- tables: header-cell presence and row count;
- assignments: rubric presence (`rubric` array or `rubric_settings` reported by Canvas), points possible, due-date presence.

Each list is capped at 200 items and 200 characters per text value; truncation is recorded and surfaced as a check limitation. No markup is stored or rendered. (Superseded in part by ADR 009: an allowlisted, URL-free structural preview is now generated and stored per snapshot; original markup is still never stored.) Evidence is part of the snapshot hash only when present, so snapshots captured before this change keep their hashes and are not duplicated. Re-importing a course appends new evidence-bearing snapshots; earlier snapshots are never rewritten. Fictional demo content is seeded as HTML through the same sanitizer and backfilled idempotently through an appended snapshot.

## Rubrics

`RubricProfile` (tenant, name, delivery method) owns numbered `RubricVersion`s. Criteria store code, title, description, framework category (the ten categories in REVIEW_FRAMEWORK.md), applicable element types (`page`, `assignment` in this phase), review method (Manual, Automated, Assisted), evidence guidance, exactly four rating anchors (0–3), weight 1–5, required flag and the deterministic check codes they are linked to. Automated/Assisted criteria must name at least one check.

Only the CTLReviewer role may list, create, draft, edit and publish rubrics, and only within its tenant. A profile has at most one draft at a time; a new draft copies the latest version's criteria with new IDs. Publishing requires at least one criterion. Published versions and their criteria are immutable: the EF context rejects modifications, and PostgreSQL triggers reject direct UPDATE/DELETE of published versions and INSERT/UPDATE/DELETE of their criteria. Edits use optimistic concurrency (`revision`), returning 409 on stale writes. Draft creation, edits and publication are audited.

The seeded "Course design essentials (demo)" profile uses original TeachWell wording informed by public design principles; it does not copy proprietary rubric text or claim alignment to any accreditation standard. CTL can add local criteria; source citations/effective dates remain a later enhancement.

## Element reviews

A review is created by a CTLReviewer for a page or assignment in a course they can access (tenant plus ownership or explicit share). It pins the element's newest snapshot and a published rubric version at creation, and creates one evaluation per applicable criterion, initially **Not reviewed**. The snapshot and rubric version are immutable, so the review content, check output and score are reproducible. If newer content is captured later, the review is flagged as not the latest snapshot and a new review is required to evaluate it.

Evaluation outcomes are `Rated` (0–3), `NotApplicable` or `NotReviewed`; ratings are accepted only for `Rated`. Scoring is a weighted percentage over rated criteria only. N/A is excluded from the denominator. Not reviewed is excluded from the score and reported as missing coverage, never as zero. Summaries also show required criteria rated below 2 and required criteria not reviewed. The UI states that scores are not compliance, accreditation or accessibility certifications.

Deterministic checks are pure functions over the pinned snapshot's status and evidence: missing/empty/file-name image alt text, empty headings, skipped heading levels, vague or empty link text, tables without header cells, assignments without a reported rubric, and unavailable/removed or unpublished items. Snapshots without evidence produce explicit limitations, not passes. Checks create **Proposed** findings linked to the first applicable criterion naming that check; each carries generated evidence text. Re-running is idempotent by fingerprint and preserves earlier dispositions. Reviewers confirm or reject proposals, and may add manual findings, which require evidence.

Drafts are visible only to their author. Publishing requires every proposal to be confirmed or rejected and at least one criterion to be rated or marked N/A. A published review, its evaluations and findings are immutable (EF guard plus PostgreSQL triggers). Course owners and other users with course access see published reviews and only confirmed findings; rejected proposals remain visible only to the author. Instructors cannot create, edit or publish CTL reviews. Revoking a share removes access to that course's reviews, including the reviewer's own drafts, on the next request. Review start, check runs, finding additions/dispositions and publication are audited.

## Routes

See API_CONTRACT.md. Every mutation takes the review's or draft's current `revision`. All unsafe methods require CSRF. Missing or unauthorized resources return 404, role failures 403, invalid input ProblemDetails 400, and stale or immutable writes 409.

## Not included / limitations

- Revision tasks, instructor responses and re-review workflows (Phase 4). Findings carry stable IDs for later linking.
- Course-wide and module-level reviews; quizzes, discussions and files.
- axe-core or rendered-page accessibility testing, contrast, captions, media and remote link checking. No remote link is fetched (SSRF boundary unchanged).
- Rubric source citations, effective dates, profile archival and reviewer reassignment.
- Assignment rubric presence relies on Canvas reporting rubric fields to the importing account; it is not live-verified against an authorized Canvas instance.
