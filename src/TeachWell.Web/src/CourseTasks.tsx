import { useEffect, useState } from "react";
import { ClipboardList } from "lucide-react";
import { ApiError, request, type TaskSummary } from "./api";
import "./Tasks.css";
import { Collapsible } from "./Collapsible";
import { taskStatusLabel } from "./labels";

interface Props {
  courseId: string;
  onOpen: (taskId: string) => void;
  onSessionExpired: (error: unknown) => void;
}

export function CourseTasks({ courseId, onOpen, onSessionExpired }: Props) {
  const [tasks, setTasks] = useState<TaskSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    request<TaskSummary[]>(`/courses/${courseId}/revision-tasks`)
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
  }, [courseId, retry]);

  if (loading) return <p role="status">Loading revision tasks…</p>;
  return (
    <Collapsible
      id="tasks"
      title="Revision tasks"
      icon={<ClipboardList size={22} />}
      className="sharing-panel course-tasks"
      meta={`${tasks.length} ${tasks.length === 1 ? "task" : "tasks"} · ${tasks.filter((t) => !["Resolved", "Withdrawn", "NotApplicable"].includes(t.status)).length} open`}
      description="Instructor work items created from confirmed review findings."
    >
      {error && (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Refresh tasks</button>
        </div>
      )}
      {!error && tasks.length === 0 && <p className="sharing-empty">No revision tasks for this course yet.</p>}
      {tasks.length > 0 && (
        <ul className="task-list">
          {tasks.map((t) => (
            <li key={t.id}>
              <div>
                <span className={"tag status-" + t.status.toLowerCase()}>{taskStatusLabel(t.status).toUpperCase()}</span>{" "}
                <span className={"tag severity-" + t.priority.toLowerCase()}>{t.priority.toUpperCase()}</span>
                <strong>{t.title}</strong>
                <small>
                  {t.elementType} · {t.elementTitle} · Assigned to {t.assigneeName} · {t.findingCount} finding{t.findingCount === 1 ? "" : "s"}
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
    </Collapsible>
  );
}
