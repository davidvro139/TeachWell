# Architecture & Integration Design

## Components
`React/Vite SPA → ASP.NET Core API → PostgreSQL` plus background audit worker and Canvas connector.

Recommended solution:
```
src/TeachWell.Api
src/TeachWell.Application
src/TeachWell.Domain
src/TeachWell.Infrastructure
src/TeachWell.Web
tests/TeachWell.UnitTests
tests/TeachWell.IntegrationTests
tests/TeachWell.E2ETests
```

## Authentication
**MVP:** ASP.NET Core Identity local/demo accounts with HttpOnly cookie sessions, CSRF protection, secure password hashing, environment-based seed credentials. Do not allow public production login using fixed demo credentials.

**Future:** Microsoft Entra ID single-tenant OpenID Connect using Authorization Code + PKCE (BFF preferred). IT approval required for tenant app registration, redirect URIs, API scopes and enterprise app policies. Link external identities by `(provider, issuer, subject/objectId)` to an internal user, not email alone. Keep application roles separate from provider authentication. Entra login never auto-grants CTL/admin privileges.

## Canvas integration
**MVP:** per-user PAT submitted over TLS to backend; validate with `GET /api/v1/users/self/profile`; list courses with `GET /api/v1/courses?enrollment_type=teacher` where appropriate. API permissions and pagination govern results. Import pages, modules/items, assignments, quizzes, discussions and accessible files as feasible. Do not assume all endpoints or content types are visible to every user. Use a configurable Canvas base URL allowlist and timeouts/retries/backoff. Encrypt tokens at rest with externally managed key; support disconnect.

**Future:** Canvas OAuth 2.0 using institution-approved developer key, per-user consent, encrypted refresh tokens and least privilege. Admin-level cross-course access requires separately granted Canvas permissions.

## Course content adapters
- `ICourseContentSource`: `ListCourses`, `GetCourse`, `ListElements`, `GetElement`, `GetSnapshot`.
- `DemoCourseContentSource` seeded, offline.
- `CanvasPatContentSource` live read-only.
- Normalize into `Course`, `CourseElement` (`page`, `assignment`, `quiz`, `discussion`, `module`, `file`, `other`), and `ElementSnapshot`.
- Preserve source ID, canonical URL, fetch timestamp, content hash, sanitized HTML/text, metadata and API availability warnings.
- Prefer metadata and sanitized text over full binary/media copies.

## Review engine
- Rule registry: `IRubricRule` with `RuleId`, version, supported element types, required inputs, evaluation result, evidence, confidence, and limitations.
- Rule types: deterministic automated, assisted suggestion, manual.
- Run rules on immutable snapshots, not live mutable objects.
- Human reviewer decides rubric score for subjective criteria; automated checks produce evidence/findings, not final compliance claims.
- Store profile version, rule version, snapshot ID, reviewer and review timestamp.

## Security
- Server-side access check: course access is scoped to owner/importer and explicit shares; CTL role alone is insufficient.
- Encrypt PATs; audit token access and admin changes; redact secrets and personal information from logs.
- Sanitize imported Canvas HTML before display; protect server against SSRF from arbitrary URLs.
- Apply rate limits, input validation, CORS restriction, secure headers and appropriate cookie settings.
- Limit data retention; allow disconnect, delete imported data, and export review records.
- FERPA: MVP excludes student data; institutional deployment requires legal/IT/privacy review.

## Background jobs
Use hosted worker first; optionally Hangfire after persistence and retry needs justify it. Jobs: import/sync, audit, link validation, scheduled recheck. Store job status and error details without credentials.
