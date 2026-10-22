# Instructional Design Review Framework

## Principles
TeachWell evaluates **content and course design**, not instructor performance. CTL maintains versioned rubric profiles. Criteria can be required, advisory, not applicable, or awaiting human review. Evidence and recommendations are first-class records.

## Rating scale
- 0 Not demonstrated
- 1 Partially demonstrated
- 2 Meets expectations
- 3 Exceeds expectations
- N/A Not applicable (excluded from denominator)
- Not reviewed / insufficient evidence (not a zero)

A criterion stores: `code`, `title`, `description`, `category`, `applicableElementTypes`, `reviewMethod`, `evidenceGuidance`, `ratingAnchors`, `weight`, `required`, `standardReferences`, `suggestedRemediation`. Compute category scores only over actually rated applicable criteria; show coverage and unresolved critical issues separately. Never equate a numerical score with compliance.

## Categories and example criteria

### 1. Course structure and navigation
- Course has a clear starting point and orientation.
- Modules and items have consistent, descriptive names and organization.
- Students can find instructions and next steps without guesswork.
- Prerequisites, requirements and availability settings are understandable.
- Unpublished or missing linked content is detected.
- **Automation:** presence/structure/link analysis. **Human:** navigability, coherence.

### 2. Objectives and competencies
- Outcomes describe observable, measurable performance (Bloom's taxonomy guidance).
- Course, module and assignment outcomes align.
- Each competency has appropriate assessment evidence.
- Orphan objectives and unmapped assessments are identified.
- Cognitive complexity matches expected skill.
- **Automation:** explicit mappings, missing fields. **Assisted/manual:** verb quality and alignment.

### 3. Assessment and feedback
- Assignments have clear instructions, deliverables, evaluation criteria and rubrics.
- Assessments measure the intended skill, including authentic practical performance.
- Formative practice and feedback opportunities exist.
- Mastery/remediation opportunities are appropriate to program requirements.
- **Automation:** rubric presence, assignment metadata. **Manual:** assessment validity and sufficiency.

### 4. Instructional content and scaffolding
- Concepts progress logically from prerequisites to application.
- Worked examples and guided practice precede independent performance where appropriate.
- Cognitive load and chunking are considered.
- Examples are relevant to industry and current tools.
- **Manual/assisted:** pedagogical judgment required.

### 5. Engagement and active learning
- Students practice, apply, explain, construct and/or collaborate where appropriate.
- Activities provide meaningful feedback and opportunities to self-check.
- ICAP is a reference lens, not a mandatory quota of activity types.
- **Manual/assisted:** no automatic pass/fail based on discussion count.

### 6. Accessibility and UDL
- Image alt text, heading hierarchy, meaningful link text, table headers and detectable contrast issues.
- Captions/transcripts and accessible equivalents for instructional media.
- Keyboard access and screen-reader experience require manual validation.
- UDL: flexible engagement, representation and demonstration of learning where appropriate.
- **Automation:** axe-core-compatible checks on safely rendered HTML plus custom checks. **Manual:** complete accessibility and UDL review.
- WCAG 2.2 AA may be used as a technical reference; automated scan is not a conformance certification.

### 7. Multimedia and copyright
- Media supports objectives, uses clear labeling, accessible formats and suitable duration.
- Embedded resources load and are accessible to authorized students.
- Copyright attribution/licensing status is documented; flag uncertain cases for manual review.
- **Automation:** detectable metadata/link issues. **Manual:** media effectiveness, legal permissions.

### 8. Student support and communication
- Course expectations, instructor contact/support channels and technical help are discoverable.
- Directions are student-centered, unambiguous and actionable.
- Feedback and response expectations are clear.
- Self-paced learners can identify the next task and help path.

### 9. Technical quality
- Broken internal/external links, orphaned module items, invalid embeds and unavailable resources.
- Canvas content status and visibility inconsistencies.
- Mobile/responsive usability and cross-platform concerns (manual validation as needed).

### 10. Continuous improvement
- Prior findings are tracked to disposition.
- Evidence of revision and re-review is retained.
- Review frequency, rubric version and ownership are clear.
- Trends are aggregated without reducing instructors to rankings.

## Framework references
Use recognized concepts from constructive alignment, Bloom's revised taxonomy, UDL, WCAG, ICAP, ADDIE, OSCQR and Quality Matters where legally appropriate. Do not copy proprietary rubric text or assert accreditation/certification. CTL can add local/accreditation-aligned criteria with explicit source citations and effective dates.

## Review result schema concept
`criterionId`, `rating|null`, `evaluationMethod`, `evidence[]`, `reviewerComment`, `confidence|null`, `findingIds[]`, `reviewedAt`, `snapshotId`.

## Review scope
Course-wide, module-level, or individual Canvas element (page/assignment/quiz/discussion/file). Different criteria apply by scope and element type. Review templates can be tailored to traditional, hybrid, online and competency-based delivery.
