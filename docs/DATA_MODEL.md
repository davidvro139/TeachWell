# Proposed PostgreSQL Data Model

Use UUID PKs, UTC `created_at`/`updated_at`, FK constraints, appropriate indexes, optimistic concurrency and soft-delete only where justified. EF Core Identity schema may manage local auth tables.

## Identity
- `users`: id, display_name, email, is_active, created_at.
- `roles`, `user_roles`: Instructor, CTLReviewer, SystemAdmin (many-to-many).
- `external_identities`: id, user_id, provider, issuer, subject, linked_at; unique(provider,issuer,subject).
- `audit_logs`: id, actor_user_id, action, entity_type, entity_id, metadata_json, timestamp.

## Canvas/content
- `canvas_connections`: id, owner_user_id, base_url, canvas_user_id, encrypted_token, token_metadata, status, last_validated_at. Never expose encrypted token to clients.
- `courses`: id, source_type, source_course_id, title, code, owner_user_id, last_synced_at.
- `course_access`: course_id, user_id, permission (`View`, `Review`, `Manage`), granted_by, granted_at.
- `course_elements`: id, course_id, source_element_id, type, parent_element_id, title, source_url, current_snapshot_id, status.
- `element_snapshots`: id, element_id, fetched_at, content_hash, sanitized_html, plain_text, metadata_json, source_warning; immutable.
- `sync_jobs`: id, course_id, status, started_at, finished_at, error_code.

## Rubrics/reviews
- `rubric_profiles`: id, name, delivery_method, is_active.
- `rubric_versions`: id, profile_id, version_number, effective_at, published_at, immutable_definition_json.
- `rubric_criteria`: id, rubric_version_id, code, category, element_types_json, method, rating_anchors_json, weight, required, evidence_guidance, standard_references_json.
- `element_reviews`: id, course_id, element_id, snapshot_id, rubric_version_id, reviewer_user_id, status, started_at, published_at.
- `criterion_evaluations`: id, element_review_id, criterion_id, rating_nullable, is_not_applicable, is_not_reviewed, comment, evidence_json, method, reviewed_at.
- `review_findings`: id, element_review_id, criterion_evaluation_id_nullable, title, description, severity, recommendation, evidence_json, status, created_at.

## Revision work
- `revision_tasks`: id, course_id, title, instructions, assignee_user_id, reviewer_user_id, priority, due_at_nullable, status, source_snapshot_id, latest_snapshot_id, created_at, resolved_at.
- `revision_task_findings`: task_id, finding_id (many-to-many).
- `task_comments`: id, task_id, author_user_id, body, created_at.
- `task_events`: id, task_id, actor_user_id, from_status, to_status, reason, created_at; append-only.

## Faculty support
- `support_tickets`: id, requester_user_id, assignee_user_id_nullable, course_id_nullable, element_id_nullable, revision_task_id_nullable, category, priority, status, title, description, created_at, closed_at.
- `support_comments`: id, ticket_id, author_user_id, body, created_at.
- `support_events`: id, ticket_id, actor_user_id, action, metadata_json, created_at.
- `knowledge_articles`: id, title, slug, body, tags_json, status, version, author_user_id, published_at.
- `workshops`: id, title, description, starts_at, ends_at, modality, capacity_nullable.
- `workshop_registrations`: workshop_id, user_id, status, completed_at_nullable.

## Standards library (ADR 011)
- `standard_sets`: id, tenant_id, name, delivery_mode_nullable, department_nullable, scope_key (unique per tenant), created_by, created_at.
- `standard_set_versions`: id, set_id, version_number, status, notes, checks_json (every check's settings and guidance), created_by, created_at, published_by, published_at, revision; immutable once published.
- `courses` gains delivery_mode_nullable and department_nullable (D4, CTL-maintained).
- `reviews` gains standard_set_version_id_nullable, pinned when the review starts.
- `guide_progress` (slices 4–5): id, tenant_id, course_id, key (a guide place: `{elementId}:{fingerprint}` or `course:{fingerprint}`), status (Done | NotApplicable), reason, set_by, set_at (UTC); unique (course_id, key). Reopening deletes the row; audit entries keep the history.
- `check_resource_links`: id, tenant_id, check_code, article_id_nullable, workshop_id_nullable (exactly one set), created_by, created_at. A CTL resource offered on guide steps for a check; deleted with its article or workshop.
- `readiness_requests` (ADR 013): id, tenant_id, course_id, requested_by, requested_at, note, standard_set_version_id_nullable, standards_name, standards_version_number_nullable, content_as_of, snapshot_ids_json, guide_summary_json, status (Requested | InReview | Endorsed | ChangesRequested | Withdrawn), reviewer_id_nullable, taken_at, decided_at, decision_summary, withdrawn_at, revision. Immutable once decided.
- `standard_set_versions` gains endorsement_json: course_review, rubric_profile_id_nullable and minimum_item_reviews (ADR 013 amendment). Immutable with the version.
- `notifications`: id, tenant_id, user_id, kind, title, body, course_id_nullable, readiness_request_id_nullable, created_at, read_at_nullable. Personal; in-app only.
- `endorsements` (ADR 013): id, tenant_id, course_id, request_id (unique), standard_set_version_id_nullable, standards_name, standards_version_number_nullable, content_as_of, snapshot_ids_json, reviewer_id, endorsed_at, valid_until, summary. Immutable.
- `course_guide_records` (ADR 014): id, tenant_id, course_id, day (UTC date; unique with course_id), recorded_at, standards_name, standards_version_number, checked_items, open_steps, required_open_steps, open_places, done_places, not_applicable_places, open_by_aspect_json, open_by_check_json. Figures only.
- `support_tickets` gains origin ("" or "Guide").
- `glossary_entries` (ADR 015): id, tenant_id, term_id (unique per tenant), term, definition, phrases_json, tags_json, links_json, hidden, updated_by, updated_at, revision (concurrency token). It stores only a college's changes; the built-in terms ship with the web app.
- `courses` gains program_nullable (CTL-maintained; oversight filtering only). `audit_logs` gains indexes on timestamp, and on actor_id plus timestamp. System entries use actor_id `00000000-…` with the tenant as entity.
- Roles gain `AcademicAdmin` (read-only oversight, D3).
- Evidence JSON (no schema change): assignment evidence gains `dueAt` (UTC); course-structure evidence version 2 adds per-item `words`, `points`, `dueAt` and an `outcomes` list (code, title, description, mapped assignment count).
- Elements (no schema change, ADR 019): a course's Canvas syllabus is imported as a `CourseElement` of type `syllabus` with the fixed source id `syllabus` and no `ParentElementId`. It is in `ReviewVocabulary.CheckedTypes` (the element checks run on it) but not in `ElementTypes`, so it is never treated as content placed in a module.
- Evidence JSON (no schema change, ADR 018 amendment): course-structure evidence version 4 adds per-item `media`, the number of recognised video and audio players in the item. Null means media was not captured for it, which is distinct from zero.
- Elements (migration `SuppliedContent`, ADR 019 amendment): `SuppliedAt` and `SuppliedBy` record content a person supplied here rather than TeachWell importing it — today, a pasted syllabus. Supplied content is skipped when an import is applied and excluded from the "missing" sweep, so an import can neither overwrite nor remove it.
- Evidence JSON (no schema change, ADR 018 amendment): page evidence version 2 adds a `media` list (index, kind, provider host, title, `hasCaptionTrack`). `media` is null on earlier snapshots, which means "not captured" and is distinct from an empty list meaning "no media on the page". Captured before the HTML strip removes iframes, and holding facts only — never a `src`, URL or markup.

## Integrity rules
- A review snapshot and element must belong to the same course.
- A revision task may group multiple findings from the same authorized course; validate server-side.
- All access checks are based on authenticated internal user plus course ACL, not role alone.
- Published rubric versions and snapshots are immutable.
- Findings and task status transitions are auditable.
