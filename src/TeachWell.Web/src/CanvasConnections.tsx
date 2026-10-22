import { useEffect, useState } from "react";
import {
  ApiError,
  request,
  type CanvasConnection,
  type CanvasSettings,
} from "./api";
import "./CanvasConnections.css";
import { CanvasCourseImport } from "./CanvasCourseImport";
import { statusLabel } from "./labels";

export function CanvasConnections({
  onSessionExpired,
}: {
  onSessionExpired: (error: unknown) => void;
}) {
  const [settings, setSettings] = useState<CanvasSettings | null>(null);
  const [connections, setConnections] = useState<CanvasConnection[]>([]);
  const [loading, setLoading] = useState(true),
    [saving, setSaving] = useState(false);
  const [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const [origin, setOrigin] = useState(""),
    [token, setToken] = useState(""),
    [expiry, setExpiry] = useState("");
  const [retry, setRetry] = useState(0);
  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    Promise.all([
      request<CanvasSettings>("/canvas/settings"),
      request<CanvasConnection[]>("/canvas/connections"),
    ])
      .then(([config, saved]) => {
        if (active) {
          setSettings(config);
          setOrigin(config.allowedOrigins[0] ?? "");
          setConnections(saved);
        }
      })
      .catch((e) => {
        if (active) fail(e);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [retry]);
  async function connect(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError("");
    setNotice("");
    const credential = token;
    setToken("");
    try {
      await request("/canvas/connections", {
        baseUrl: origin,
        token: credential,
        expiresAt: expiry ? new Date(expiry).toISOString() : null,
      });
      setConnections(await request<CanvasConnection[]>("/canvas/connections"));
      setExpiry("");
      setNotice(
        "Canvas connection validated and saved. You can now list and import available courses.",
      );
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  async function change(connection: CanvasConnection, disconnect: boolean) {
    setSaving(true);
    setError("");
    setNotice("");
    try {
      await request(
        `/canvas/connections/${connection.id}` +
          (disconnect ? "" : "/validate"),
        disconnect ? undefined : {},
        disconnect ? "DELETE" : "POST",
      );
      setNotice(
        disconnect
          ? "Disconnected. TeachWell removed its saved token. Revoke the token in Canvas if you also want to disable it there."
          : "Canvas connection validated.",
      );
    } catch (e) {
      fail(e);
    } finally {
      try {
        setConnections(
          await request<CanvasConnection[]>("/canvas/connections"),
        );
      } catch (e) {
        fail(e);
      }
      setSaving(false);
    }
  }
  return (
    <section aria-busy={loading || saving}>
      <span className="eyebrow">READ-ONLY INTEGRATION</span>
      <h1>Your Canvas connections</h1>
      <p className="intro">
        Connect only an account and institution you are authorized to use. Your
        demo courses remain available without a connection.
      </p>
      {loading && <p role="status">Loading Canvas connections…</p>}
      {error && (
        <div role="alert" className="error">
          {error}
          <button disabled={saving} onClick={() => setRetry((x) => x + 1)}>
            Refresh connections
          </button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {!loading && settings && (
        <>
          {!settings.enabled && (
            <div className="panel">
              <h2>Canvas is not enabled for this workspace</h2>
              <p>
                You can continue exploring the fictional course library. Saved
                connections can still be disconnected here.
              </p>
            </div>
          )}
          {settings.enabled && (
            <form className="canvas-form panel" onSubmit={connect}>
              <h2>Add or replace a connection</h2>
              <label htmlFor="canvas-origin">Approved Canvas site</label>
              <select
                id="canvas-origin"
                required
                value={origin}
                disabled={saving}
                onChange={(e) => setOrigin(e.target.value)}
              >
                {settings.allowedOrigins.map((url) => (
                  <option key={url} value={url}>
                    {url}
                  </option>
                ))}
              </select>
              <label htmlFor="canvas-token">Personal access token</label>
              <input
                id="canvas-token"
                type="password"
                value={token}
                autoComplete="off"
                spellCheck={false}
                required
                maxLength={4096}
                disabled={saving}
                onChange={(e) => setToken(e.target.value)}
                aria-describedby="token-help"
              />
              <p className="fine" id="token-help">
                Stored encrypted on the server and never shown again. Saving
                checks your own Canvas profile ID; it does not import course or
                student data.
              </p>
              <label htmlFor="canvas-expiry">
                Token expiry, if known (your local time)
              </label>
              <input
                id="canvas-expiry"
                type="datetime-local"
                value={expiry}
                disabled={saving}
                onChange={(e) => setExpiry(e.target.value)}
              />
              <button disabled={saving || !token || !origin}>
                {saving ? "Working…" : "Validate and save"}
              </button>
              <p className="fine">
                For an existing site, this replaces your saved token after
                validation. Your app role does not grant Canvas permissions.
              </p>
            </form>
          )}
          <h2 className="canvas-list-title">Saved connections</h2>
          {connections.length === 0 ? (
            <div className="panel">
              <h3>No Canvas connections yet</h3>
              <p>Your fictional demo library works offline.</p>
            </div>
          ) : (
            <ul className="canvas-list">
              {connections.map((c) => (
                <li className="panel" key={c.id}>
                  <div className="connection-heading">
                    <h3>{new URL(c.baseUrl).hostname}</h3>
                    <span className="tag">{statusLabel(c.status)}</span>
                  </div>
                  <dl>
                    <div>
                      <dt>Credential</dt>
                      <dd>
                        {c.tokenMask
                          ? "•••••••• · Saved securely"
                          : "No saved token"}
                      </dd>
                    </div>
                    <div>
                      <dt>Last successful validation</dt>
                      <dd>
                        {c.lastValidatedAt
                          ? new Date(c.lastValidatedAt).toLocaleString()
                          : "Not validated"}
                      </dd>
                    </div>
                    <div>
                      <dt>Declared expiry</dt>
                      <dd>
                        {c.expiresAt
                          ? new Date(c.expiresAt).toLocaleString()
                          : "Not provided; Canvas may still expire or revoke it"}
                      </dd>
                    </div>
                  </dl>
                  {c.errorCode && (
                    <p className="connection-warning">
                      Last check: {errorLabel(c.errorCode)}
                    </p>
                  )}
                  <div className="connection-actions">
                    <button
                      disabled={
                        saving ||
                        !settings.enabled ||
                        !c.tokenMask ||
                        c.status === "Disconnected"
                      }
                      onClick={() => change(c, false)}
                    >
                      Validate connection
                    </button>
                    <button
                      className="remove-access"
                      disabled={saving || c.status === "Disconnected"}
                      onClick={() => change(c, true)}
                    >
                      Disconnect
                    </button>
                  </div>
                  {c.tokenMask && c.status !== "Disconnected" && (
                    <CanvasCourseImport
                      key={`${c.id}:${c.status}`}
                      connectionId={c.id}
                      disabled={saving || !settings.enabled}
                      onSessionExpired={onSessionExpired}
                    />
                  )}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
function errorLabel(code: string) {
  const labels: Record<string, string> = {
    InvalidToken: "token rejected; reconnect with a valid token",
    Expired: "declared expiry reached",
    PermissionDenied: "Canvas denied access",
    TokenUnreadable: "saved credential unavailable; disconnect and reconnect",
    Timeout: "Canvas did not respond in time",
    RateLimited: "Canvas is limiting requests",
    RedirectBlocked: "redirect blocked; check the approved hostname",
    UnexpectedResponse: "unexpected Canvas response",
    Unavailable: "Canvas could not be reached",
  };
  return labels[code] ?? "validation failed; try again or reconnect";
}
