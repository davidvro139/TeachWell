# ADR 001: Phase 1 foundation

Accepted for the local demonstration.

- Use the available .NET 9 SDK, EF Core, PostgreSQL 17, React 19, TypeScript and Vite. Upgrade .NET to supported LTS before institutional deployment. Pinned versions are not a claim of currency.
- Separate domain, application, infrastructure, API and typed SPA client. Plain CSS implements the first screen; Tailwind/shadcn are deferred until component reuse warrants them.
- One fictional tenant. Course queries check tenant plus ownership or explicit access. Administrators receive no implicit course access. Future tenant provisioning must enforce identity/tenant consistency.
- Three fictional personas; reviewer also holds Instructor to exercise role switching. Instructor owns two courses; only the first is shared with the reviewer. Reviewer's Instructor view is intentionally empty.
- Environment-only passwords, explicit idempotent Development-only seed, no password resets on reseed. Demo login disabled outside Development. No registration/account editing in Phase 1.
- Always Secure, HttpOnly, SameSite Strict sessions; CSRF on every unsafe HTTP method including login/logout. Tokens held only in browser memory. Caddy provides local HTTPS. No credential/session localStorage.
- Canvas and Entra disabled. Identity/course interfaces are introduced; real Canvas PAT adapter, external identity linking and encrypted PAT storage are deferred. No live integration is represented as working.
- Render sample content as React-escaped plain text. No remote content fetches, student data, or AI transmission. Immutable snapshots, review versions, revisions and support belong to later phases.
- Seeded sharing emits an audit record. Permission/role editing endpoints must include audit trails before introduction.
- HTTP integration tests use SQLite (real Identity and relational queries). They do not prove PostgreSQL migrations or Docker startup work. Those need a separate smoke test.
- Local Data Protection keys persist in a Docker volume; production requires deployment-managed key protection. No production identity, recovery/MFA, tenant provisioning or deployment hardening is claimed.
