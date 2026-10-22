import { useEffect, useState, type FormEvent } from "react";
import { History, LifeBuoy, MessageSquare } from "lucide-react";
import {
  ApiError,
  request,
  type KnowledgeArticleSummary,
  type SupportTicket,
  type TicketAction,
  type Workshop,
} from "./api";
import "./Support.css";
import { supportCategoryLabel, ticketStatusLabel } from "./labels";

interface Props {
  ticketId: string;
  onBack: () => void;
  onSessionExpired: (error: unknown) => void;
}

const formatTime = (value: string) => new Date(value).toLocaleString();

export function TicketWorkspace({ ticketId, onBack, onSessionExpired }: Props) {
  const [ticket, setTicket] = useState<SupportTicket | null>(null);
  const [articles, setArticles] = useState<KnowledgeArticleSummary[]>([]);
  const [workshops, setWorkshops] = useState<Workshop[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [comment, setComment] = useState("");
  const [pendingAction, setPendingAction] = useState<TicketAction | null>(null);
  const [reason, setReason] = useState("");
  const [articleId, setArticleId] = useState("");
  const [workshopId, setWorkshopId] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    request<SupportTicket>(`/support/tickets/${ticketId}`)
      .then((t) => {
        if (!active) return;
        setTicket(t);
        setArticleId(t.recommendedArticle?.id ?? "");
        setWorkshopId(t.recommendedWorkshop?.id ?? "");
        if (t.canRecommend) {
          Promise.all([request<KnowledgeArticleSummary[]>("/knowledge"), request<Workshop[]>("/workshops")]).then(([a, w]) => {
            if (active) {
              setArticles(a);
              setWorkshops(w);
            }
          });
        }
      })
      .catch((e) => {
        if (active) {
          if (e instanceof ApiError && e.status === 404)
            setError("This ticket is not available to you.");
          else fail(e);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [ticketId, retry]);

  async function act<T>(path: string, body: T, method: "POST" | "PATCH", success: string) {
    setSaving(true);
    setError("");
    setNotice("");
    try {
      setTicket(await request<SupportTicket>(path, body, method));
      setNotice(success);
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  function choose(action: TicketAction) {
    if (action.reasonRequired) {
      setPendingAction(action);
      setReason("");
    } else runTransition(action, "");
  }
  function runTransition(action: TicketAction, reasonText: string) {
    if (!ticket) return;
    act(`/support/tickets/${ticket.id}/transition`, { revision: ticket.revision, toStatus: action.toStatus, reason: reasonText || null }, "POST", `${action.label} applied.`).then(
      () => {
        setPendingAction(null);
        setReason("");
      },
    );
  }
  function submitComment(e: FormEvent) {
    e.preventDefault();
    if (!comment.trim()) return;
    act(`/support/tickets/${ticketId}/comments`, { body: comment }, "POST", "Comment posted.").then(() => setComment(""));
  }
  function submitRecommendation(e: FormEvent) {
    e.preventDefault();
    if (!ticket) return;
    act(
      `/support/tickets/${ticket.id}/recommendation`,
      { revision: ticket.revision, articleId: articleId || null, workshopId: workshopId || null },
      "PATCH",
      "Recommendation updated.",
    );
  }

  if (loading) return <p role="status">Loading ticket…</p>;
  if (!ticket)
    return (
      <section className="panel">
        <button className="back" onClick={onBack}>
          ← Back
        </button>
        <div className="error" role="alert">
          {error || "Ticket unavailable."}
          <button onClick={() => setRetry((x) => x + 1)}>Try again</button>
        </div>
      </section>
    );

  return (
    <div className="task-workspace" aria-busy={saving}>
      <button className="back" onClick={onBack}>
        ← Back
      </button>
      <span className="eyebrow">SUPPORT TICKET · {supportCategoryLabel(ticket.category).toUpperCase()}</span>
      <h1>{ticket.title}</h1>
      <div className="task-meta">
        <span className={"tag status-" + ticket.status.toLowerCase()}>{ticketStatusLabel(ticket.status).toUpperCase()}</span>
        <span className={"tag severity-" + ticket.priority.toLowerCase()}>{ticket.priority.toUpperCase()} PRIORITY</span>
      </div>
      <p className="intro">
        Requested by {ticket.requesterName} on {formatTime(ticket.createdAt)}
        {ticket.assigneeName && ` · Assigned to ${ticket.assigneeName}`}
        {ticket.closedAt && ` · Closed ${formatTime(ticket.closedAt)}`}
      </p>
      <p>{ticket.description}</p>
      {ticket.linkedCourse && (
        <p className="fine">
          Linked to <strong>{ticket.linkedCourse.courseTitle}</strong> ({ticket.linkedCourse.courseCode})
          {ticket.linkedCourse.elementTitle && <> · {ticket.linkedCourse.elementTitle}</>}
          {ticket.linkedCourse.revisionTaskTitle && <> · Revision task: {ticket.linkedCourse.revisionTaskTitle}</>}
        </p>
      )}

      {error && (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Reload ticket</button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}

      {ticket.availableActions.length > 0 && (
        <section className="panel task-actions" aria-label="Available actions">
          <h2>Actions</h2>
          <div className="finding-actions">
            {ticket.availableActions.map((a) => (
              <button key={a.toStatus} disabled={saving} className={a.toStatus === "Resolved" || a.toStatus === "Closed" ? "" : "secondary"} onClick={() => choose(a)}>
                {a.label}
              </button>
            ))}
          </div>
          {pendingAction && (
            <form
              className="reason-form"
              onSubmit={(e) => {
                e.preventDefault();
                runTransition(pendingAction, reason);
              }}
            >
              <label>
                Note for "{pendingAction.label}" (required)
                <textarea required maxLength={2000} rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
              </label>
              <div className="finding-actions">
                <button disabled={saving || !reason.trim()}>Confirm {pendingAction.label}</button>
                <button type="button" className="secondary" onClick={() => setPendingAction(null)}>
                  Cancel
                </button>
              </div>
            </form>
          )}
        </section>
      )}

      {(ticket.canRecommend || ticket.recommendedArticle || ticket.recommendedWorkshop) && (
        <section className="panel" aria-labelledby="recommend-title">
          <div className="review-heading">
            <LifeBuoy size={22} />
            <div>
              <h2 id="recommend-title">Recommended resources</h2>
              <p>A knowledge article or workshop the requester can follow up on.</p>
            </div>
          </div>
          {ticket.canRecommend ? (
            <form onSubmit={submitRecommendation}>
              <div className="form-row">
                <label>
                  Knowledge article
                  <select value={articleId} onChange={(e) => setArticleId(e.target.value)}>
                    <option value="">None</option>
                    {articles.map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.title}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Workshop
                  <select value={workshopId} onChange={(e) => setWorkshopId(e.target.value)}>
                    <option value="">None</option>
                    {workshops.map((w) => (
                      <option key={w.id} value={w.id}>
                        {w.title}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <button disabled={saving}>Save recommendation</button>
            </form>
          ) : (
            <>
              {ticket.recommendedArticle && <p>Article: {ticket.recommendedArticle.title}</p>}
              {ticket.recommendedWorkshop && <p>Workshop: {ticket.recommendedWorkshop.title}</p>}
            </>
          )}
        </section>
      )}

      <section className="panel" aria-labelledby="ticket-comments-title">
        <div className="review-heading">
          <MessageSquare size={22} />
          <div>
            <h2 id="ticket-comments-title">Comments</h2>
            <p>Visible to the requester and any CTL reviewer.</p>
          </div>
        </div>
        {ticket.comments.length === 0 ? (
          <p className="sharing-empty">No comments yet.</p>
        ) : (
          <ul className="comment-list">
            {ticket.comments.map((c) => (
              <li key={c.id}>
                <strong>{c.authorName}</strong> <small>{formatTime(c.createdAt)}</small>
                <p>{c.body}</p>
              </li>
            ))}
          </ul>
        )}
        {ticket.canComment && (
          <form onSubmit={submitComment}>
            <label>
              Add a comment
              <textarea required maxLength={4000} rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
            </label>
            <button disabled={saving || !comment.trim()}>Post comment</button>
          </form>
        )}
      </section>

      <section className="panel" aria-labelledby="ticket-history-title">
        <div className="review-heading">
          <History size={22} />
          <div>
            <h2 id="ticket-history-title">History</h2>
            <p>An append-only record of every status change.</p>
          </div>
        </div>
        <ol className="event-list">
          {ticket.events.map((e) => (
            <li key={e.id}>
              <div>
                <strong>{e.fromStatus ? `${ticketStatusLabel(e.fromStatus)} → ${ticketStatusLabel(e.toStatus)}` : `Created (${ticketStatusLabel(e.toStatus)})`}</strong>{" "}
                <small>
                  {e.actorName} · {formatTime(e.createdAt)}
                </small>
                {e.reason && <p>{e.reason}</p>}
              </div>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
