import { useEffect, useState, type FormEvent } from "react";
import { LifeBuoy } from "lucide-react";
import {
  ApiError,
  CATEGORIES_SUPPORT,
  PRIORITIES,
  request,
  type Severity,
  type SupportTicket,
  type SupportTicketSummary,
} from "./api";
import { KnowledgeBase } from "./KnowledgeBase";
import { Workshops } from "./Workshops";
import "./Support.css";
import { supportCategoryLabel, ticketStatusLabel } from "./labels";

interface Props {
  isCtl: boolean;
  /** Where to start when arriving from the glossary (ADR 015). */
  initialTab?: "tickets" | "knowledge" | "workshops";
  initialArticleSlug?: string | null;
  prefillRequest?: { title: string; category: string } | null;
  prefillRevisionTaskId: string | null;
  onPrefillConsumed: () => void;
  onOpenTicket: (ticketId: string) => void;
  onSessionExpired: (error: unknown) => void;
}

export function Support({
  isCtl,
  initialTab = "tickets",
  initialArticleSlug = null,
  prefillRequest = null,
  prefillRevisionTaskId,
  onPrefillConsumed,
  onOpenTicket,
  onSessionExpired,
}: Props) {
  const [tab, setTab] = useState<"tickets" | "knowledge" | "workshops">(initialTab);
  const [scope, setScope] = useState<"mine" | "queue">("mine");
  const [tickets, setTickets] = useState<SupportTicketSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const [creating, setCreating] = useState(!!prefillRevisionTaskId || !!prefillRequest);
  const [category, setCategory] = useState(prefillRequest?.category ?? "Other"),
    [title, setTitle] = useState(prefillRequest?.title ?? ""), [description, setDescription] = useState(""), [priority, setPriority] = useState<Severity>("Medium");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }

  useEffect(() => {
    if (tab !== "tickets") return;
    let active = true;
    setLoading(true);
    setError("");
    request<SupportTicketSummary[]>("/support/tickets" + (scope === "queue" ? "?scope=queue" : ""))
      .then((data) => {
        if (active) setTickets(data);
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
  }, [tab, scope, retry]);

  async function create(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const ticket = await request<SupportTicket>("/support/tickets", {
        category,
        title,
        description,
        priority,
        revisionTaskId: prefillRevisionTaskId || null,
      });
      if (prefillRevisionTaskId) onPrefillConsumed();
      onOpenTicket(ticket.id);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <span className="eyebrow">FACULTY SUPPORT CENTER</span>
      <h1>Support</h1>
      <p className="intro">
        {isCtl
          ? "Triage and respond to support requests, and maintain job aids and workshops."
          : "Request help, track your tickets, and browse job aids and workshops."}
      </p>
      <nav className="workspace-tabs support-tabs" aria-label="Support sections">
        <button aria-current={tab === "tickets" ? "page" : undefined} onClick={() => setTab("tickets")}>
          Tickets
        </button>
        <button aria-current={tab === "knowledge" ? "page" : undefined} onClick={() => setTab("knowledge")}>
          Knowledge base
        </button>
        <button aria-current={tab === "workshops" ? "page" : undefined} onClick={() => setTab("workshops")}>
          Workshops
        </button>
      </nav>

      {tab === "knowledge" && (
        <KnowledgeBase isCtl={isCtl} initialSlug={tab === initialTab ? initialArticleSlug : null} onSessionExpired={onSessionExpired} />
      )}
      {tab === "workshops" && <Workshops isCtl={isCtl} onSessionExpired={onSessionExpired} />}
      {tab === "tickets" && (
        <>
          {isCtl && (
            <div className="workspace-tabs">
              <button aria-current={scope === "mine" ? "page" : undefined} onClick={() => setScope("mine")}>
                My tickets
              </button>
              <button aria-current={scope === "queue" ? "page" : undefined} onClick={() => setScope("queue")}>
                Triage queue
              </button>
            </div>
          )}
          {error && (
            <div className="error" role="alert">
              {error}
              <button disabled={busy} onClick={() => setRetry((x) => x + 1)}>
                Refresh
              </button>
            </div>
          )}
          {creating ? (
            <form className="panel create-task-panel" onSubmit={create}>
              <h3>{prefillRevisionTaskId ? "Request help with this revision task" : "New support ticket"}</h3>
              {prefillRevisionTaskId && <p className="fine">This ticket will link back to the revision task you came from.</p>}
              <div className="form-row">
                <label>
                  Category
                  <select value={category} onChange={(e) => setCategory(e.target.value)}>
                    {CATEGORIES_SUPPORT.map((c) => (
                      <option key={c} value={c}>
                        {supportCategoryLabel(c)}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Priority
                  <select value={priority} onChange={(e) => setPriority(e.target.value as Severity)}>
                    {PRIORITIES.map((p) => (
                      <option key={p}>{p}</option>
                    ))}
                  </select>
                </label>
              </div>
              <label>
                Title
                <input required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
              </label>
              <label>
                What do you need help with?
                <textarea required maxLength={4000} rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
              </label>
              <div className="finding-actions">
                <button disabled={busy}>Submit ticket</button>
                {!prefillRevisionTaskId && (
                  <button type="button" className="secondary" onClick={() => setCreating(false)}>
                    Cancel
                  </button>
                )}
              </div>
            </form>
          ) : (
            <button onClick={() => setCreating(true)}>
              <LifeBuoy size={16} /> New support ticket
            </button>
          )}
          {loading && <p role="status">Loading tickets…</p>}
          {!loading && !error && tickets.length === 0 && (
            <section className="panel">
              <h2>No tickets here</h2>
              <p>{scope === "queue" ? "Nothing waiting in the triage queue." : "You have no open or past tickets."}</p>
            </section>
          )}
          {tickets.length > 0 && (
            <ul className="task-list">
              {tickets.map((t) => (
                <li key={t.id}>
                  <div>
                    <span className={"tag status-" + t.status.toLowerCase()}>{ticketStatusLabel(t.status).toUpperCase()}</span>{" "}
                    <span className={"tag severity-" + t.priority.toLowerCase()}>{t.priority.toUpperCase()}</span>
                    <strong>{t.title}</strong>
                    <small>
                      {supportCategoryLabel(t.category)} · Requested by {t.requesterName}
                      {t.assigneeName && ` · Assigned to ${t.assigneeName}`}
                      {t.hasCourseLink && " · Linked to a course"}
                    </small>
                  </div>
                  <button onClick={() => onOpenTicket(t.id)} aria-label={`Open ticket: ${t.title}`}>
                    Open
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </>
  );
}
