import { useEffect, useState, type FormEvent } from "react";
import { CalendarDays } from "lucide-react";
import { ApiError, MODALITIES, request, type Modality, type Workshop } from "./api";
import "./Support.css";

export function Workshops({ isCtl, onSessionExpired }: { isCtl: boolean; onSessionExpired: (error: unknown) => void }) {
  const [workshops, setWorkshops] = useState<Workshop[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [creating, setCreating] = useState(false);
  const [title, setTitle] = useState(""), [description, setDescription] = useState("");
  const [startsAt, setStartsAt] = useState(""), [endsAt, setEndsAt] = useState("");
  const [modality, setModality] = useState<Modality>("Online"), [capacity, setCapacity] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  function load() {
    return request<Workshop[]>("/workshops").then(setWorkshops);
  }
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    load()
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

  async function create(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      // datetime-local inputs are timezone-naive local wall-clock; convert to UTC instants before sending (timestamps are stored in UTC).
      await request<Workshop>(
        "/workshops",
        { title, description, startsAt: new Date(startsAt).toISOString(), endsAt: new Date(endsAt).toISOString(), modality, capacity: capacity ? Number(capacity) : null },
        "POST",
      );
      setTitle("");
      setDescription("");
      setStartsAt("");
      setEndsAt("");
      setCapacity("");
      setCreating(false);
      await load();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  async function toggleRegistration(w: Workshop) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      if (w.myStatus) {
        await request(`/workshops/${w.id}/register`, undefined, "DELETE");
        setNotice("Registration cancelled.");
      } else {
        const updated = await request<Workshop>(`/workshops/${w.id}/register`, {}, "POST");
        setNotice(updated.myStatus === "Waitlisted" ? "This workshop is full; you are on the waitlist." : "You are registered.");
      }
      await load();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <span className="eyebrow">TRAINING</span>
      <h1>Workshops</h1>
      <p className="intro">{isCtl ? "Schedule workshops and see who has registered." : "Register for upcoming training."}</p>
      {error && (
        <div className="error" role="alert">
          {error}
          <button disabled={busy} onClick={() => setRetry((x) => x + 1)}>
            Refresh
          </button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {loading && <p role="status">Loading workshops…</p>}
      {isCtl &&
        (creating ? (
          <form className="panel create-task-panel" onSubmit={create}>
            <h3>New workshop</h3>
            <label>
              Title
              <input required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
            </label>
            <label>
              Description
              <textarea maxLength={4000} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
            </label>
            <div className="form-row">
              <label>
                Starts
                <input required type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
              </label>
              <label>
                Ends
                <input required type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
              </label>
              <label>
                Modality
                <select value={modality} onChange={(e) => setModality(e.target.value as Modality)}>
                  {MODALITIES.map((m) => (
                    <option key={m}>{m}</option>
                  ))}
                </select>
              </label>
              <label>
                Capacity (optional)
                <input type="number" min={1} value={capacity} onChange={(e) => setCapacity(e.target.value)} />
              </label>
            </div>
            <div className="finding-actions">
              <button disabled={busy}>Create workshop</button>
              <button type="button" className="secondary" onClick={() => setCreating(false)}>
                Cancel
              </button>
            </div>
          </form>
        ) : (
          <button className="secondary" onClick={() => setCreating(true)}>
            New workshop
          </button>
        ))}
      {!loading && workshops.length === 0 && !error && (
        <section className="panel">
          <CalendarDays />
          <h2>No workshops scheduled</h2>
        </section>
      )}
      {workshops.length > 0 && (
        <ul className="task-list workshop-list">
          {workshops.map((w) => (
            <li key={w.id}>
              <div>
                <span className="tag">{w.modality.toUpperCase()}</span>
                <strong>{w.title}</strong>
                <small>
                  {new Date(w.startsAt).toLocaleString()} · {w.registeredCount}
                  {w.capacity ? `/${w.capacity}` : ""} registered{w.waitlistedCount > 0 && ` · ${w.waitlistedCount} waitlisted`}
                  {w.myStatus && ` · You are ${w.myStatus.toLowerCase()}`}
                </small>
                {w.canManage && w.registrations.length > 0 && (
                  <ul className="registrant-list">
                    {w.registrations.map((r) => (
                      <li key={r.userId}>
                        {r.displayName} <span className="fine">({r.status.toLowerCase()})</span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <button disabled={busy} className={w.myStatus ? "secondary" : ""} onClick={() => toggleRegistration(w)}>
                {w.myStatus ? "Cancel registration" : "Register"}
              </button>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
