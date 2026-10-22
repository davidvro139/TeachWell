# ADR 017: Hosted public demo

Accepted October 22, 2026.

## Context

TeachWell needs a public link that anyone can open without installing .NET, Node.js and PostgreSQL. The obvious host is Render: it builds the existing `Dockerfile` and provides managed PostgreSQL. Its free plan has three constraints that shape the design:
- the web service sleeps after 15 idle minutes
- an instance gets a fraction of a CPU
- the free database expires after 30 days

Until now, demo sign-in ran only in Development, and one-click sign-in ([ADR 012](ADR-012-DEMO-QUICK-SIGN-IN.md)) only for a loopback client. A public demo must also stay usable when many anonymous visitors share the same fictional personas.

## Decision

A `Features:PublicDemo` switch, off by default, configured by [`render.yaml`](../render.yaml).

### Sign-in

- One-click sign-in is offered to every visitor, from any address and in any environment. This is safe only because the database never holds anything but the fictional seed.
- Password sign-in (`POST /auth/login`) answers 404. Otherwise anyone could lock the shared personas out with five wrong passwords.
- The personas get a random password at each data refresh unless `Demo:Password` is set. Nobody needs it, and none is stored in the repository or the Blueprint.

### What visitors cannot change

- Course export uploads and role changes answer 403, with a message explaining why. The UI shows the Canvas export steps and the roles table but replaces the controls with that explanation.
- Uploads are refused because a visitor's real course would otherwise be visible to every other visitor.
- Role changes are refused because one visitor could otherwise break every persona for everyone.
- Startup fails if the public demo is combined with `Features:CanvasPAT`, so visitors' Canvas tokens can never reach the shared database.

Everything else (reviews, tasks, tickets, standards, glossary edits) stays writable. That is the point of a demo. Visitor changes disappear at the next data refresh.

### Daily refresh without making visitors wait

Seeding the full demo data takes about 40 seconds on a desktop, and much longer on a free instance. A free instance also restarts on almost every visit after being idle, so rebuilding at startup would make nearly every portfolio visitor wait. Instead, `PublicDemoResetService`:
1. Reads when the live data was seeded. This is recorded as a comment on the `public` schema, so it survives restarts without a table of its own.
2. If there is no data at all (the first deploy), builds it. Only in this case does the API answer 503 ("loading fresh sample data"); `/healthz` stays 200, so the deploy succeeds.
3. Otherwise brings the live data up to the current migrations and serves it immediately.
4. Once the live data is `PublicDemo:ResetHours` old (default 24), builds a fresh copy beside it. The copy goes into the `teachwell_demo_next` schema, chosen through an `AsyncLocal` search path that only that background flow's database contexts see. One short transaction then renames the schemas and drops the old one.
5. Checks again every hour.

All of this runs on the thread pool and waits until the web server reports it has started. Hosted services start before the server binds its port, and run inline up to their first real await. On Render's first deploy, the initial database work kept the port closed until Render's deploy timed out.

The swap works because no migration or query qualifies a schema name. A restart against fresh data does no seeding at all.

Verified locally against PostgreSQL 17 in Production mode, using a `postgresql://` URL and plain HTTP behind a forwarded-headers proxy:
- A first start answered 503 for 38 seconds, then served.
- A restart against data marked three weeks old served every request with 200 from the first second, while it rebuilt and swapped in the background (69 seconds).
- Afterwards the database held exactly one `public` schema, with a fresh marker.

### Hosting details

- **Database URL:** `DATABASE_URL`, a `postgres://` URL as Render provides it, is accepted when `ConnectionStrings:Database` is absent. It is converted to an Npgsql connection string with SSL preferred.
- **Allowed hosts:** the public demo accepts any host, set in code and also in the Blueprint. The default `localhost;127.0.0.1` in `appsettings.json` answered 400 to Render's health checks and to visitors, so the first deploys timed out. Render's router only forwards this service's own domains, and TeachWell never builds links from the Host header.
- **Proxy headers:** `ForwardedHeaders:Enabled` trusts the nearest proxy's `X-Forwarded-For` and `X-Forwarded-Proto`. This lets the HTTPS-only session and CSRF cookies work behind Render's TLS termination, and applies the login rate limit per visitor rather than to the proxy.

## Consequences

- The demo is public, so anyone can write text into it that other visitors see until the next refresh. React escapes all user text, and the refresh bounds the impact.
- Data Protection keys live in the container and are lost on restart. Visitors are signed out after a restart or redeploy and sign in again with one click.
- The free database expires after 30 days. Upgrading it, or re-creating it from the Blueprint, restores the demo with no other change.
- If Render's edge adds more than one proxy hop, the rate limit may group visitors behind the same edge node. That is acceptable for a demo; a production deployment would configure the known proxy networks instead.
- The `Dockerfile` is built by Render, not by this repository's automated checks, because Docker is not installed in the development environment.
