import { useEffect, useState } from "react";
import { ApiError, request, type TaskStatus, type TaskSummary } from "./api";
import "./Tasks.css";
import { taskStatusLabel } from "./labels";

interface Props {
  onOpen: (taskId: string) => void;
  onSessionExpired: (error: unknown) => void;
}

const STATUSES: (TaskStatus | "")[] = [
  "",
  "Draft",
  "NeedsRevision",
  "InProgress",
  "ReadyForReview",
  "ChangesRequested",
  "Blocked",
  "Resolved",
  "Withdrawn",
  "NotApplicable",
];

export function TaskDashboard({ onOpen, onSessionExpired }: Props) {
  const [tasks, setTasks] = useState<TaskSummary[]>([]);
  const [status, setStatus] = useState<TaskStatus | "">("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    request<TaskSummary[]>("/revision-tasks" + (status ? `?status=${status}` : ""))
      .then((data) => {
        if (active) setTasks(data);
      })
      .catch((e) => {
        if (!active) return;
        if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
        else setError((e as Error).message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [status, retry]);

  return (
    <>
      <span className="eyebrow">YOUR WORK QUEUE</span>
      <h1>Revision tasks</h1>
      <p className="intro">
        Instructor work items you are assigned, and tasks you have opened as a CTL reviewer. Drafts you have not assigned yet are included;
        other people's drafts are not.
      </p>
      <label className="task-filter">
        Status
        <select value={status} onChange={(e) => setStatus(e.target.value as TaskStatus | "")}>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s || "All"}
            </option>
          ))}
        </select>
      </label>
      {error && (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Refresh</button>
        </div>
      )}
      {loading && <p role="status">Loading tasks…</p>}
      {!loading && !error && tasks.length === 0 && (
        <section className="panel">
          <h2>No tasks here</h2>
          <p>Nothing matches this filter right now.</p>
        </section>
      )}
      {tasks.length > 0 && (
        <ul className="task-list">
          {tasks.map((t) => (
            <li key={t.id}>
              <div>
                <span className={"tag status-" + t.status.toLowerCase()}>{taskStatusLabel(t.status).toUpperCase()}</span>{" "}
                <span className={"tag severity-" + t.priority.toLowerCase()}>{t.priority.toUpperCase()}</span>
                <strong>{t.title}</strong>
                <small>
                  {t.elementType} · {t.elementTitle} · Assigned to {t.assigneeName}
                  {t.dueAt && ` · Due ${new Date(t.dueAt).toLocaleDateString()}`}
                  {t.snapshotChanged && " · content changed since opened"}
                </small>
              </div>
              <button onClick={() => onOpen(t.id)} aria-label={`Open task: ${t.title}`}>
                Open
              </button>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
