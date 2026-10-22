import { useEffect, useState, type FormEvent } from "react";
import { ClipboardList, History, LifeBuoy, MessageSquare } from "lucide-react";
import {
  ApiError,
  request,
  type Comparison,
  type RevisionTask,
  type TaskAction,
} from "./api";
import "./Tasks.css";
import { Glossed } from "./Glossary";
import { taskStatusLabel } from "./labels";

interface Props {
  taskId: string;
  onBack: () => void;
  onSessionExpired: (error: unknown) => void;
  onRequestHelp: (taskId: string) => void;
}

const formatTime = (value: string) => new Date(value).toLocaleString();
const formatDate = (value: string) => new Date(value).toLocaleDateString();

export function TaskWorkspace({ taskId, onBack, onSessionExpired, onRequestHelp }: Props) {
  const [task, setTask] = useState<RevisionTask | null>(null);
  const [comparison, setComparison] = useState<Comparison | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [comment, setComment] = useState("");
  const [pendingAction, setPendingAction] = useState<TaskAction | null>(null);
  const [reason, setReason] = useState("");
  const [simulateText, setSimulateText] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    Promise.all([
      request<RevisionTask>(`/revision-tasks/${taskId}`),
      request<Comparison>(`/revision-tasks/${taskId}/comparison`),
    ])
      .then(([t, c]) => {
        if (active) {
          setTask(t);
          setComparison(c);
          setSimulateText(t.sourceSnapshot.plainText);
        }
      })
      .catch((e) => {
        if (active) {
          if (e instanceof ApiError && e.status === 404)
            setError("This task is not available. It may be a draft you cannot see, or course access may have changed.");
          else fail(e);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [taskId, retry]);

  async function refresh(next: RevisionTask, message: string) {
    setTask(next);
    setNotice(message);
    try {
      setComparison(await request<Comparison>(`/revision-tasks/${taskId}/comparison`));
    } catch {
      /* comparison is a convenience view; a failure here does not block the task update already shown */
    }
  }

  async function runTransition(action: TaskAction, reasonText: string) {
    if (!task) return;
    setSaving(true);
    setError("");
    setNotice("");
    try {
      const next = await request<RevisionTask>(`/revision-tasks/${task.id}/transition`, { revision: task.revision, toStatus: action.toStatus, reason: reasonText || null });
      await refresh(next, `${action.label} applied.`);
      setPendingAction(null);
      setReason("");
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  function choose(action: TaskAction) {
    if (action.reasonRequired) {
      setPendingAction(action);
      setReason("");
    } else runTransition(action, "");
  }
  async function submitComment(e: FormEvent) {
    e.preventDefault();
    if (!task || !comment.trim()) return;
    setSaving(true);
    setError("");
    try {
      const next = await request<RevisionTask>(`/revision-tasks/${task.id}/comments`, { body: comment });
      setTask(next);
      setComment("");
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  async function submitSimulated(e: FormEvent) {
    e.preventDefault();
    if (!task) return;
    setSaving(true);
    setError("");
    try {
      const next = await request<RevisionTask>(`/revision-tasks/${task.id}/simulate-update`, { revision: task.revision, plainText: simulateText });
      await refresh(next, "Demo content updated. This simulates an edit made directly in Canvas.");
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <p role="status">Loading task…</p>;
  if (!task)
    return (
      <section className="panel">
        <button className="back" onClick={onBack}>
          ← Back
        </button>
        <div className="error" role="alert">
          {error || "Task unavailable."}
          <button onClick={() => setRetry((x) => x + 1)}>Try again</button>
        </div>
      </section>
    );

  const canSimulate = task.isDemoCourse && task.canComment && task.assigneeId && !["Resolved", "Withdrawn", "NotApplicable"].includes(task.status);

  return (
    <div className="task-workspace" aria-busy={saving}>
      <button className="back" onClick={onBack}>
        ← Back
      </button>
      <span className="eyebrow">
        {task.courseTitle.toUpperCase()} · {task.elementType.toUpperCase()} · REVISION TASK
      </span>
      <h1>{task.title}</h1>
      <div className="task-meta">
        <span className={"tag status-" + task.status.toLowerCase()}>{taskStatusLabel(task.status).toUpperCase()}</span>
        <span className={"tag severity-" + task.priority.toLowerCase()}>{task.priority.toUpperCase()} PRIORITY</span>
        {task.dueAt && <span className="tag">DUE {formatDate(task.dueAt)}</span>}
        {task.blockedFromStatus && <span className="tag">BLOCKED FROM {task.blockedFromStatus.toUpperCase()}</span>}
      </div>
      <p className="intro">
        On <strong>{task.elementTitle}</strong> · Assigned to {task.assigneeName} · Opened by {task.reviewerName} on {formatDate(task.createdAt)}
        {task.resolvedAt && ` · Resolved ${formatDate(task.resolvedAt)}`}
      </p>
      {task.instructions && <p>{task.instructions}</p>}
      {task.elementSourceUrl && (
        <a className="source-link" href={task.elementSourceUrl} target="_blank" rel="noopener noreferrer">
          Open source in Canvas ↗
        </a>
      )}
      {task.canComment && (
        <button className="secondary" onClick={() => onRequestHelp(task.id)}>
          <LifeBuoy size={16} /> Request help with this task
        </button>
      )}

      {error && (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Reload task</button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}

      {task.availableActions.length > 0 && (
        <section className="panel task-actions" aria-label="Available actions">
          <h2>Actions</h2>
          <div className="finding-actions">
            {task.availableActions.map((a) => (
              <button key={a.toStatus} disabled={saving} className={a.toStatus === "Resolved" ? "" : "secondary"} onClick={() => choose(a)}>
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
                Reason for "{pendingAction.label}" (required)
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

      <section className="panel" aria-labelledby="task-findings-title">
        <h2 id="task-findings-title">Findings in this task</h2>
        <ul className="findings">
          {task.findings.map((f) => (
            <li className="finding finding-confirmed" key={f.findingId}>
              <div className="finding-tags">
                <span className={"tag severity-" + f.severity.toLowerCase()}>{f.severity.toUpperCase()}</span>
                {f.criterionCode && <span className="tag">{f.criterionCode}</span>}
              </div>
              <h4>{f.title}</h4>
              <p>
                <strong>Evidence:</strong> {f.evidence}
              </p>
              {f.recommendation && (
                <p>
                  <strong>Recommendation:</strong> <Glossed text={f.recommendation} />
                </p>
              )}
            </li>
          ))}
        </ul>
      </section>

      <section className="panel comparison-panel" aria-labelledby="comparison-title">
        <h2 id="comparison-title">Content comparison</h2>
        <p className="fine">
          Comparing the snapshot this task was opened against (captured {formatTime(task.sourceSnapshot.fetchedAt)}) with the latest captured
          content.
        </p>
        {!comparison ? (
          <p className="fine">Comparison unavailable right now.</p>
        ) : comparison.changed ? (
          <>
            {comparison.metadataChanges.length > 0 && (
              <ul className="metadata-changes">
                {comparison.metadataChanges.map((m) => (
                  <li key={m}>{m}</li>
                ))}
              </ul>
            )}
            <div className="text-diff">
              {comparison.textDiff.map((line, i) => (
                <div key={i} className={"diff-" + line.kind.toLowerCase()}>
                  {line.text || <>&nbsp;</>}
                </div>
              ))}
            </div>
          </>
        ) : (
          <p className="sharing-empty">No change captured since this task was opened.</p>
        )}
      </section>

      {canSimulate && (
        <section className="panel" aria-labelledby="simulate-title">
          <h2 id="simulate-title">Demo: record an update made outside TeachWell</h2>
          <p className="fine">
            This fictional course has no live Canvas connection. This action simulates editing the content in Canvas yourself, so the
            review-and-resolve loop can be exercised offline. It is clearly a demo action, not a real Canvas edit.
          </p>
          <form onSubmit={submitSimulated}>
            <label>
              Updated text
              <textarea required maxLength={10000} rows={4} value={simulateText} onChange={(e) => setSimulateText(e.target.value)} />
            </label>
            <button disabled={saving || simulateText === task.sourceSnapshot.plainText}>Record simulated update</button>
          </form>
        </section>
      )}

      <section className="panel" aria-labelledby="comments-title">
        <div className="review-heading">
          <MessageSquare size={22} />
          <div>
            <h2 id="comments-title">Comments</h2>
            <p>Visible to the assignee and the course's CTL reviewers.</p>
          </div>
        </div>
        {task.comments.length === 0 ? (
          <p className="sharing-empty">No comments yet.</p>
        ) : (
          <ul className="comment-list">
            {task.comments.map((c) => (
              <li key={c.id}>
                <strong>{c.authorName}</strong> <small>{formatTime(c.createdAt)}</small>
                <p>{c.body}</p>
              </li>
            ))}
          </ul>
        )}
        {task.canComment && (
          <form onSubmit={submitComment}>
            <label>
              Add a comment
              <textarea required maxLength={4000} rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
            </label>
            <button disabled={saving || !comment.trim()}>Post comment</button>
          </form>
        )}
      </section>

      <section className="panel" aria-labelledby="history-title">
        <div className="review-heading">
          <History size={22} />
          <div>
            <h2 id="history-title">History</h2>
            <p>An append-only record of every status change.</p>
          </div>
        </div>
        <ol className="event-list">
          {task.events.map((e) => (
            <li key={e.id}>
              <ClipboardList size={14} />
              <div>
                <strong>
                  {e.fromStatus ? `${taskStatusLabel(e.fromStatus)} → ${taskStatusLabel(e.toStatus)}` : `Created (${taskStatusLabel(e.toStatus)})`}
                </strong>{" "}
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
