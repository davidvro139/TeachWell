import { useEffect, useState } from "react";
import { ScrollText } from "lucide-react";
import { ApiError, request } from "./api";
import { roleName } from "./labels";

interface AuditEntry {
  id: string;
  timestamp: string;
  actorId: string;
  actorName: string;
  action: string;
  entityId: string;
  entityLabel: string | null;
  subjectName: string | null;
}
interface AuditPage {
  items: AuditEntry[];
  nextBefore: string | null;
  actions: string[];
}
interface Person {
  id: string;
  displayName: string;
}

// "ReadinessChangesRequested" → "Readiness changes requested"; "RoleGranted:AcademicAdmin" → "Role granted: Academic administrator".
const actionLabel = (action: string) => {
  const [name, detail] = action.split(":");
  const words = name.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1) + (detail ? `: ${roleName(detail)}` : "");
};

/**
 * SystemAdmin audit viewer (ADR 014 amendment): who did what and when, newest first, filtered by action, person and date. Read-only.
 */
export function AuditLog({ onSessionExpired }: { onSessionExpired: (error: unknown) => void }) {
  const [page, setPage] = useState<AuditPage | null>(null);
  const [items, setItems] = useState<AuditEntry[]>([]);
  const [people, setPeople] = useState<Person[]>([]);
  const [action, setAction] = useState("");
  const [actor, setActor] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  const query = (before?: string) => {
    const q = new URLSearchParams();
    if (action) q.set("action", action);
    if (actor) q.set("actorId", actor);
    if (from) q.set("from", from);
    if (to) q.set("to", to);
    if (before) q.set("before", before);
    const text = q.toString();
    return "/admin/audit" + (text ? "?" + text : "");
  };
  async function load(before?: string) {
    setBusy(true);
    setError("");
    try {
      const next = await request<AuditPage>(query(before));
      setPage(next);
      setItems((current) => (before ? [...current, ...next.items] : next.items));
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  useEffect(() => {
    request<Person[]>("/admin/users").then(setPeople).catch(fail);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [action, actor, from, to]);

  return (
    <section className="panel audit-log" aria-labelledby="audit-log-title">
      <h2 id="audit-log-title">
        <ScrollText size={18} /> Audit trail
      </h2>
      <p className="fine">
        Every permission change, review decision, readiness step, sharing change and integration change is recorded here, newest first. The
        trail is read-only.
      </p>
      <div className="form-row audit-filters">
        <label>
          Action
          <select value={action} onChange={(e) => setAction(e.target.value)}>
            <option value="">All actions</option>
            {page?.actions.map((a) => (
              <option key={a} value={a}>
                {actionLabel(a)}
              </option>
            ))}
          </select>
        </label>
        <label>
          Person
          <select value={actor} onChange={(e) => setActor(e.target.value)}>
            <option value="">Everyone</option>
            <option value="00000000-0000-0000-0000-000000000000">TeachWell (automatic)</option>
            {people.map((p) => (
              <option key={p.id} value={p.id}>
                {p.displayName}
              </option>
            ))}
          </select>
        </label>
        <label>
          From
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          To
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
      </div>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {!page && !error ? (
        <p role="status">Loading the audit trail…</p>
      ) : items.length === 0 ? (
        <p className="sharing-empty">No entries match these filters.</p>
      ) : (
        <div className="table-scroll">
          <table className="feedback-table audit-table">
            <thead>
              <tr>
                <th scope="col">When (your time)</th>
                <th scope="col">Who</th>
                <th scope="col">What</th>
                <th scope="col">About</th>
              </tr>
            </thead>
            <tbody>
              {items.map((i) => (
                <tr key={i.id}>
                  <td>{new Date(i.timestamp).toLocaleString()}</td>
                  <td>{i.actorName}</td>
                  <th scope="row">{actionLabel(i.action)}</th>
                  <td>
                    {i.entityLabel ?? <code title={i.entityId}>{i.entityId.slice(0, 8)}…</code>}
                    {i.subjectName && <small> · for {i.subjectName}</small>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {page?.nextBefore && (
        <button type="button" className="secondary" disabled={busy} onClick={() => load(page.nextBefore!)}>
          {busy ? "Loading…" : "Load older entries"}
        </button>
      )}
    </section>
  );
}
