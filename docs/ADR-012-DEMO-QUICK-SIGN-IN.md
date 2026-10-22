# ADR 012: One-click demo sign-in

Accepted October 19, 2026, at the project owner's request: "make the demo logins be buttons that auto login so I don't have to type anything or remember the password."

## Context

Demo sign-in (ADR 001/002) uses ASP.NET Core Identity with a password the developer chooses when `Start-Local.ps1` first runs. That password is stored encrypted for the current Windows user under `.local`. There are no fixed demo passwords, by design ("no production default credentials"). Switching between the five fictional personas to try a workflow meant typing an email and password each time.

## Decision

The sign-in screen offers a button per seeded demo account. One click signs that account in. No password is involved, and none is ever sent to, or stored in, the browser.

The feature exists only when **every** condition holds. Otherwise both routes answer 404, as if they did not exist:

1. The host environment is Development.
2. `Features:DemoAuth` is true (the existing demo-login switch).
3. `Features:DemoQuickLogin` is true. It is false in `appsettings.json`. `Start-Local.ps1` sets it to true unless run with `-NoQuickLogin`, and `Test-Browser.ps1` sets it for its throwaway API.
4. The client address is loopback (127.0.0.1, ::1, or IPv4-mapped loopback). The API also keeps its host filter of `localhost;127.0.0.1`.

Only active accounts whose email ends in `@demo.teachwell.test` can be listed or signed in. These are the fictional seeded personas.

| Route | Behaviour |
| --- | --- |
| `GET /api/v1/auth/demo-accounts` | Lists `{email, displayName, roles}` for the demo accounts, in persona order. No credential or secret is included. |
| `POST /api/v1/auth/demo-login {email}` | Signs the account in with a session cookie (same HttpOnly, Secure, SameSite=Strict cookie as password sign-in). The global CSRF check applies, as does the `login` rate limit (10 per IP per minute). Lockout is respected. Each sign-in is audited as `DemoQuickSignIn`. 400 for a non-demo email; 401 for an unknown, inactive or locked account. |

The password form remains below the buttons, for testing the real sign-in path.

## Consequences and risks

- **Anyone who can use this computer can sign in as any demo persona,** including the SystemAdmin persona, without a password, while the app runs locally with the flag on. That is acceptable only because:
  - the data is fictional
  - the API listens on localhost only
  - the same Windows user can already decrypt the stored demo password from `.local`

  Run `Start-Local.ps1 -NoQuickLogin` to turn it off, for example when screen-sharing a session someone else controls.
- **It never applies outside Development.** Deployments do not set the Development environment, and `Features:DemoAuth` is already refused there. The three independent switches keep an accidental configuration from enabling it.
- **Reverse proxies.** Behind a proxy on the same machine, every request would appear to come from loopback. The local setup has no proxy. The retained `Caddyfile` is for later packaging and must not be used with this flag on.
- **Kestrel's dual-stack sockets** report IPv4 clients as `::ffff:127.0.0.1`. The loopback check normalises that form; a unit test covers it.

## Verification

- `DemoSignInTests` (integration):
  - the list contains no password field and is in persona order
  - one-click sign-in reaches a role-protected route, logs out, and is audited
  - a missing CSRF token gives 400, a non-demo email 400, an unknown account 401, a locked account 401
  - with the flag off, or from a non-loopback address, both routes give 404 and nobody is signed in
  - the loopback check accepts IPv4-mapped and IPv6 loopback, and rejects a mapped remote address
- `quick-login.spec.ts` (Edge): the buttons are listed, the password field stays empty, one click opens Alex Morgan's instructor workspace, and signing out returns to the buttons.
