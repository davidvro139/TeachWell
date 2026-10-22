# ADR 003: Canvas PAT connection lifecycle

Accepted October 15, 2026. Docker remains deferred.

## Scope and source

Implement connect/replace, validate, owner-only list and disconnect. `CanvasPatClient` is a real read-only HTTP adapter; automated tests replace its network responses explicitly. No live account has been tested. Listing/importing courses remains a separate slice.

Validation uses a Bearer header and `GET /api/v1/users/self/profile`, following Instructure's [OAuth2 documentation](https://developerdocs.instructure.com/services/canvas/oauth2/file.oauth) and [Users API](https://developerdocs.instructure.com/services/canvas/resources/users). Only the positive numeric external user ID is retained; profile names, email, avatars, calendar URLs and other returned fields are not persisted or returned. No roster/student endpoints are called. Successful validation confirms profile access, not teacher role, course permissions, token scope completeness or institutional approval.

## Security boundaries

- Off by default. Enabling requires a valid deployment-managed base64 32-byte key and an exact administrator-configured DNS host allowlist. There are no wildcards, arbitrary base paths, embedded credentials, HTTP URLs, custom ports or IP literal destinations.
- Every outbound connection resolves DNS and rejects private, loopback, link-local, metadata, multicast, reserved/documentation and transition ranges. The socket connects to a checked IP rather than resolving again. TLS still verifies the original hostname. Mixed public/private DNS results fail closed. Redirects, proxies and cookie reuse are disabled. No arbitrary remote links are fetched.
- Profile requests have a 20-second total timeout, 8-second connect timeout and 64 KiB response limit. Calls are limited to 10 connect/validate attempts per authenticated user per minute. Validation is explicit; 429 and transient failures do not trigger automatic retry loops. Course imports will need their own pagination/backoff policy later.
- AES-256-GCM uses fresh random nonces and authenticates tenant, owner and connection IDs as associated data. Copying encrypted values between rows/users/tenants fails authentication. Ciphertext is versioned. API DTOs never serialize ciphertext, raw tokens, token fragments or upstream error bodies.
- Same-origin Secure HttpOnly session cookies, CSRF protection including DELETE, authenticated application role (Instructor or CTLReviewer), and tenant+owner checks apply. Administrator role alone grants no connector access.
- Concurrent changes use a revision concurrency token. An in-flight validation cannot restore credentials after disconnect. A conflicting change returns 409 and requires refresh.
- Actual connect/replace, validation outcomes and disconnect write sanitized audit events in the same transaction as state changes. Audit records contain identifiers and error enums, not credentials or raw remote errors.

## Lifecycle

Connect validates before saving. Reconnecting to the same origin replaces the token on the same record. A rejected replacement preserves the old credential. Invalid/revoked tokens (remote 401) and reached declared expiry clear the stored ciphertext. Remote 403, throttling, network failure and timeout remain distinct from token rejection. Transient errors preserve the saved credential for a later manual retry.

Expiry is optional owner-declared metadata; it is not discovered from Canvas or a guarantee of validity. Reached expiry blocks the outbound call. Unknown expiry is labeled explicitly. Unreadable ciphertext fails safely and requires disconnect/reconnect.

Disconnect clears the credential and external user ID locally and retains a status/audit record. It works even if the integration feature is disabled. It does not revoke the PAT remotely; users must revoke it in Canvas if desired. No Canvas write request is made.

## Key operations

Production keys must come from an external secret manager and be backed up independently of the database. Do not commit keys or set them in frontend build configuration. This first implementation has one active key, without a historical key ring: replacing/loss of the key makes prior tokens unreadable and requires reconnection. Planned key-ring rotation is a deployment-hardening task.

For Windows-only local development, Start-Local optionally generates a 32-byte key and protects it using the current Windows user's DPAPI in ignored `.local/canvas-key.xml`. This is a local convenience, not an institutional secret manager. The default start command keeps Canvas disabled. Tests use ephemeral generated keys and fictional tokens only.

## Seed and verification

Do not seed live connections, PATs or encryption keys. Offline seeds remain unchanged. Add a forward PostgreSQL migration for connection records. Test both SQLite and PostgreSQL, crypto binding/tampering, request/response isolation, host/IP policies, remote errors, expiry and disconnect races. Browser fixtures for enabled-state behavior are explicitly HTTP mocks; live testing still requires an approved host/account and should not generate screenshots or traces containing credentials.
