# Local development without Docker

Docker is deferred until the final packaging stage. TeachWell continues to use PostgreSQL, not an alternative runtime database.

Optional Canvas connection configuration is documented in [CANVAS_CONNECTIONS.md](CANVAS_CONNECTIONS.md). The normal start command keeps it off; `-CanvasHost` enables only the explicitly configured sites and generates a Windows-user-protected local key. No Canvas credentials or connection seeds are needed for the offline demo.

## Windows quick start

Prerequisites: .NET 9 SDK, Node.js 22 with npm, and PostgreSQL 17 binaries. Use an existing PostgreSQL installation's `bin` directory, or the binary archive linked from [PostgreSQL's Windows downloads](https://www.postgresql.org/download/windows/) and [EDB's binary downloads](https://www.enterprisedb.com/download-postgresql-binaries).

This workspace already has portable PostgreSQL 17.11 and Node.js under ignored `.local`. For a fresh clone, extract PostgreSQL so `initdb.exe` is at `.local/pgsql/bin/initdb.exe`; you only need `pgsql/bin`, `pgsql/lib`, `pgsql/share` and the server/third-party license files. Tools are not committed to the repository.

From the repository root:

```powershell
# One-time browser trust; Windows may ask you to confirm installing the local certificate.
dotnet dev-certs https --trust

# Builds both projects, creates/starts PostgreSQL, migrates, seeds, serves the SPA.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-Local.ps1
```

On first start, enter your own demo password (12+ characters, uppercase/lowercase, a digit and a symbol). It is stored encrypted for the current Windows user. No demo password is printed. Sign in at **https://localhost:7143** with that password and `instructor@demo.teachwell.test`, `reviewer@demo.teachwell.test`, or `admin@demo.teachwell.test`.

An installed PostgreSQL location can be supplied with `-PostgresBin 'C:/Program Files/PostgreSQL/17/bin'`. The script creates its own cluster and never changes an installed cluster or installs a system service. Port defaults to 55432; use the same `-DatabasePort` on subsequent application starts if you choose another port. If you already have a configured PostgreSQL database, use the manual environment workflow in README instead of these scripts.

Ctrl+C stops the API. PostgreSQL remains available for development. Stop it explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Stop-LocalDatabase.ps1
```

Pass `-PostgresBin` to the stop script if you used an installed binary path. After editing, rerun Start-Local to rebuild. `-NoBuild` skips rebuild/copy when existing binaries and frontend assets are current. Database, encrypted local credentials, logs and tools live in `.local`, which must never be committed. Session keys use ASP.NET Core's user-profile development key storage.

## Test against PostgreSQL

The default integration suite uses in-memory SQLite for speed. Set `TEACHWELL_TEST_POSTGRES` to a PostgreSQL connection string whose user may create/drop databases to run the same tests against real PostgreSQL. Each factory creates its own `teachwell_test_<random>` database, applies migrations and deletes it on cleanup; it never deletes the configured database. Credentials should remain in environment variables, not scripts or command history.

For this workspace's native cluster, the helper loads the encrypted local credential automatically:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Postgres.ps1
```

```powershell
dotnet test TeachWell.sln
```

Browser tests use a separately seeded local test database, a temporary HTTPS API on port 7443 and the installed Edge browser:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Browser.ps1
```

The runner generates a unique seed password in memory and removes the test database/API afterward. Its database is separate from `teachwell`; it does not set or change your normal demo password. No real Canvas or Microsoft credentials are required. Browser screenshots, traces and video are disabled. It checks form labels/focus and the owner-share/reviewer-read/revoke flow, not full accessibility conformance.

## Sharing walkthrough

1. Instructor opens Designing for Everyone, initially private, and chooses Jordan Lee under Share with a CTL reviewer.
2. Select Share for review. The reviewer can now see this course in the CTL reviewer view (use **Viewing as** under the name in the sidebar if the account opens in Instructor view).
3. Instructor selects Remove access. The reviewer's next list/detail request no longer permits that course.
4. Courses already viewed may remain on screen until navigation/refresh; revocation cannot erase previously viewed information.

Only the owner sees sharing controls. Other roles cannot manage course access through direct API requests. All real permission changes create an audit event; repeat clicks on an already-completed grant/revoke do not create duplicate events.
