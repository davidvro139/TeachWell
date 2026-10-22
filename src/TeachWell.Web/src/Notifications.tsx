import { useEffect, useRef, useState } from "react";
import { Bell } from "lucide-react";
import { ApiError, request } from "./api";
import "./Guide.css";

export interface Notification {
  id: string;
  kind: string;
  title: string;
  body: string;
  courseId: string | null;
  createdAt: string;
  read: boolean;
}
interface NotificationList {
  unread: number;
  items: Notification[];
}

const POLL_MS = 60_000;

/**
 * In-app notifications (readiness requests, reviews taken, decisions, withdrawals). Checked on load, every minute while the tab is
 * visible, and when opened. Opening a notification marks it read and goes to where it points.
 */
export function Notifications({ onOpen, onSessionExpired }: { onOpen: (n: Notification) => void; onSessionExpired: (error: unknown) => void }) {
  const [data, setData] = useState<NotificationList | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState("");
  const panel = useRef<HTMLDivElement>(null);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  async function load() {
    try {
      setData(await request<NotificationList>("/notifications"));
      setError("");
    } catch (e) {
      fail(e);
    }
  }
  useEffect(() => {
    load();
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") load();
    }, POLL_MS);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => {
      if (panel.current && !panel.current.contains(e.target as Node)) setOpen(false);
    };
    const escape = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("mousedown", close);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);

  async function choose(n: Notification) {
    setOpen(false);
    try {
      if (!n.read) setData(await request<NotificationList>(`/notifications/${n.id}/read`, {}));
    } catch (e) {
      fail(e);
    }
    onOpen(n);
  }
  async function readAll() {
    try {
      setData(await request<NotificationList>("/notifications/read-all", {}));
    } catch (e) {
      fail(e);
    }
  }

  const unread = data?.unread ?? 0;
  return (
    <div className="notifications" ref={panel}>
      <button
        type="button"
        className="notifications-button"
        aria-expanded={open}
        aria-label={unread ? `Notifications, ${unread} unread` : "Notifications"}
        onClick={() => {
          setOpen(!open);
          if (!open) load();
        }}
      >
        <Bell size={18} />
        {unread > 0 && <span className="notifications-count">{unread > 99 ? "99+" : unread}</span>}
      </button>
      {open && (
        <div className="notifications-panel" role="region" aria-label="Notifications">
          <div className="notifications-head">
            <strong>Notifications</strong>
            {unread > 0 && (
              <button type="button" className="link-button" onClick={readAll}>
                Mark all read
              </button>
            )}
          </div>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          {!data ? (
            <p role="status">Loading…</p>
          ) : data.items.length === 0 ? (
            <p className="fine">Nothing yet. You will hear here when a readiness review moves.</p>
          ) : (
            <ul>
              {data.items.map((n) => (
                <li key={n.id} className={n.read ? "" : "is-unread"}>
                  <button type="button" onClick={() => choose(n)}>
                    <strong>{n.title}</strong>
                    <span>{n.body}</span>
                    <small>{new Date(n.createdAt).toLocaleString()}</small>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
