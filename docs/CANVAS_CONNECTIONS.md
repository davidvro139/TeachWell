# Canvas connections

The secure connection lifecycle and bounded read-only course listing/import are implemented. The offline fictional library remains usable without Canvas. No live institutional Canvas connection has been verified in this session.

## Enable locally without Docker

Use only an approved account and Canvas site. From PowerShell, start the application with its exact hostname:

```powershell
./scripts/Start-Local.ps1 -CanvasHost 'your-approved-canvas-host.example.edu'
```

Replace the example with the real Canvas DNS name, without `https://`, a path, wildcard or port. Multiple names can be passed as a PowerShell string array when invoking the script directly. An existing PostgreSQL bin directory can still be supplied with `-PostgresBin`. The script generates an encryption key if one does not exist, stores it DPAPI-encrypted for the current Windows user under ignored `.local/canvas-key.xml`, and passes it only to the backend environment. Never commit that file or put its contents in screenshots/logs.

Start without `-CanvasHost` to keep live Canvas calls disabled. Disconnection of previously saved tokens still works in disabled mode. The server checks network destinations against both the configured names and public address rules, so private/internal-only Canvas servers are intentionally unsupported in this version.

Open **https://localhost:7143**, sign in as an instructor or CTL reviewer, then select **Canvas connections**. Choose the approved site, enter an authorized personal access token, optionally provide its expiry, then select **Validate and save**. The password-style field clears on submission. The UI shows a saved-token mask, status and last successful validation; it never displays the saved PAT.

Use **Validate connection** for a fresh profile check. A rejected/expired token requires reconnecting. Remote permission errors, throttling, timeouts and unreachable hosts have distinct messages. Canvas validation uses only the documented current-user profile endpoint; it does not list courses or prove course access.

Use **Disconnect** to remove TeachWell's saved credential. This does not revoke the PAT in Canvas. Revoke it in Canvas as well if you want to prevent its use outside TeachWell.

## Import a course

1. On a connected site's card, select **List available courses**. Only courses whose returned current-user enrollment explicitly reports active teacher status are supported.
2. Select **Import course**. TeachWell reads modules, pages, assignments and module placements, then saves a private local copy. Unsupported items and unavailable page bodies are labeled with warnings.
3. Select **Courses** to open the imported copy, source links and publication/availability labels. No rich HTML, embedded scripts, files or external URLs are fetched/rendered.
4. Import the same source course again to refresh it. The local course ID, element IDs and explicit shares are preserved. Changed content adds snapshots; missing items are marked Removed. A failed refresh leaves the prior course copy intact.

The importer has prototype limits (64 KiB per response, 40 pages/2,000 records per collection, 200 request attempts and 90 seconds of remote work, 3,000 normalized elements). Exceeding them fails explicitly. Large courses need a future background import design. Only current-user enrollment metadata is inspected; rosters, grades and submissions are not requested. Teacher access must be verified again at import time. Designer/TA/admin-only course access without active teacher enrollment is not supported yet.

Disconnect removes credentials, not imported copies or existing shares. Previously imported content remains available offline to the owner and explicitly shared reviewers. Plain text cannot reproduce media/layout or support HTML accessibility checks without additional structural evidence. See [ADR 004](ADR-004-CANVAS-COURSE-IMPORT.md).

## Manual backend configuration

Keep these values in the backend process environment or a deployment-managed secret/configuration store:

| Setting | Meaning |
| --- | --- |
| `Features__CanvasPAT` | `true` to enable connect/validate; false by default |
| `Canvas__AllowedHosts__0` (and subsequent indices) | Exact approved DNS names |
| `Canvas__EncryptionKey` | Base64-encoded cryptographically random 32-byte AES key; never use a frontend `VITE_` variable |

Enabled startup fails if a key or valid allowlist is missing. Keep the key available across restarts; losing or replacing it makes saved credentials unreadable. This version supports reconnection rather than historical-key rotation. Production key management and institutional approvals are still required before real adoption.

## API

All routes are under `/api/v1/canvas`, require an Instructor or CTLReviewer session and enforce tenant/owner isolation. CSRF is required for POST and DELETE.

- `GET /settings`: enabled flag and approved origins; no key information.
- `GET /connections`: caller's connection summaries only, without tokens or ciphertext.
- `POST /connections`: `{baseUrl, token, expiresAt?}`; validates then creates/replaces an owner-specific connection, returning a safe summary.
- `POST /connections/{id}/validate`: rechecks an owned connection and updates its status.
- `DELETE /connections/{id}`: clears the saved credential locally, idempotent for an already disconnected owned record.
- `GET /connections/{id}/courses`: bounded course list, following safe Canvas pagination links on the backend.
- `POST /api/v1/courses/import`: `{connectionId,sourceCourseId}`; returns `{courseId,elementCount,warnings,...}`. This route is under `/api/v1/courses`, not `/api/v1/canvas`.

Unauthorized connection IDs return 404. Wrong app roles receive 403; missing sessions receive 401. A remote Canvas 401 becomes **422**, so it cannot be mistaken for an expired TeachWell login. Invalid inputs return validation ProblemDetails, concurrent updates 409, remote throttling/unavailability 503, timeout 504 and malformed/redirected responses 502. No upstream body is returned.

See [ADR 003](ADR-003-CANVAS-CONNECTIONS.md) for encryption, DNS/socket safeguards, auditing, key limitations and test boundaries.
