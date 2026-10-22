import { expect, type BrowserContext, type Cookie } from "@playwright/test";

// The suite's total real logins now exceeds the login endpoint's rate limit (10 per IP per minute — a legitimate
// brute-force control, see ADR 001/002/007) when every test logs in fresh. Since playwright.config.ts runs this
// suite with workers: 1, every spec file executes sequentially in one Node process, so this module-level cache
// persists for the whole run: each persona logs in via the real endpoint only once, and later contexts reuse that
// session cookie. This does not touch the rate limiter itself, and it does not change what any test asserts —
// no test here exercises authenticate() to test login behavior; they all use it purely as setup.
const sessionCookies = new Map<string, Cookie>();

// A role change renews the person's security stamp, which ends their existing sessions within a minute (ADR 014).
// Specs that change someone's roles call this so later specs sign that persona in again.
export function forgetSession(persona: string) {
  sessionCookies.delete(persona);
}

export async function authenticate(context: BrowserContext, persona: string) {
  const cached = sessionCookies.get(persona);
  if (cached) {
    await context.addCookies([cached]);
    return;
  }
  const password = process.env.TEACHWELL_E2E_PASSWORD;
  if (!password)
    throw new Error(
      "Set TEACHWELL_E2E_PASSWORD to the isolated test database's seed password.",
    );
  const token = await (await context.request.get("/api/v1/auth/csrf")).json();
  const response = await context.request.post("/api/v1/auth/login", {
    headers: { "X-CSRF-TOKEN": token.token },
    data: { email: `${persona}@demo.teachwell.test`, password },
  });
  expect(response.status()).toBe(204);
  const session = (await context.cookies()).find((c) => c.name === "__Host-TeachWell");
  if (session) sessionCookies.set(persona, session);
}
