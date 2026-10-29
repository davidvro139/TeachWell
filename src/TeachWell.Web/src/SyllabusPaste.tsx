import { useEffect, useState } from "react";
import { ClipboardPaste } from "lucide-react";
import { ApiError, request } from "./api";

interface Syllabus {
  courseId: string;
  source: "Canvas" | "Pasted" | "None";
  words: number;
  text: string | null;
  suppliedAt: string | null;
  suppliedByName: string | null;
  importedAt: string | null;
  canEdit: boolean;
  note: string | null;
}

/**
 * Paste the course syllabus (ADR 019 amendment).
 *
 * This college's syllabi live in Simple Syllabus, an LTI tool whose content is not in Canvas exports or the Canvas API
 * (ADR 008), so the course owner pastes the text and TeachWell checks that instead. Nothing is saved until "Save
 * syllabus"; a pasted syllabus survives re-importing the course, and "Remove" hands the syllabus back to the importer.
 */
export function SyllabusPaste({
  courseId,
  onSaved,
  onSessionExpired,
}: {
  courseId: string;
  onSaved?: () => void;
  onSessionExpired: (error: unknown) => void;
}) {
  const [syllabus, setSyllabus] = useState<Syllabus | null>(null);
  const [text, setText] = useState("");
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  // A panel must not be able to take the course page down with it. An unexpected response body — a proxy error page,
  // an older API, a route that answered with something else entirely — would otherwise reach the render and throw on
  // the first missing field, and React unmounts the whole subtree when a render throws. So the shape is checked once
  // here and anything else is treated as "no syllabus to show" rather than trusted.
  function isSyllabus(value: unknown): value is Syllabus {
    if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
    const v = value as Partial<Syllabus>;
    return (
      (v.source === "Canvas" || v.source === "Pasted" || v.source === "None") &&
      typeof v.words === "number" &&
      typeof v.canEdit === "boolean"
    );
  }
  async function load() {
    try {
      const loaded = await request<unknown>(`/courses/${courseId}/syllabus`);
      setSyllabus(isSyllabus(loaded) ? loaded : null);
    } catch (e) {
      fail(e);
    }
  }
  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [courseId]);

  async function save() {
    setBusy(true);
    setError("");
    try {
      const saved = await request<unknown>(`/courses/${courseId}/syllabus`, { text }, "PUT");
      if (isSyllabus(saved)) setSyllabus(saved);
      else await load();
      setEditing(false);
      setText("");
      onSaved?.();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  async function clear() {
    setBusy(true);
    setError("");
    try {
      const cleared = await request<unknown>(`/courses/${courseId}/syllabus`, undefined, "DELETE");
      if (isSyllabus(cleared)) setSyllabus(cleared);
      else await load();
      onSaved?.();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }

  if (!syllabus) return null;
  const day = (value: string) => new Date(value).toLocaleDateString(undefined, { year: "numeric", month: "long", day: "numeric" });

  return (
    <section className="panel" aria-labelledby="syllabus-title">
      <h3 id="syllabus-title">
        <ClipboardPaste size={16} /> Syllabus
      </h3>

      {syllabus.source === "None" ? (
        <p className="fine">No syllabus in TeachWell, so its required elements were not checked.</p>
      ) : (
        <p className="fine">
          {syllabus.source === "Pasted"
            ? `Pasted${syllabus.suppliedByName ? ` by ${syllabus.suppliedByName}` : ""}${syllabus.suppliedAt ? ` on ${day(syllabus.suppliedAt)}` : ""}`
            : "From the Canvas syllabus page"}
          {` · ${syllabus.words.toLocaleString()} ${syllabus.words === 1 ? "word" : "words"} checked`}
        </p>
      )}
      {syllabus.note && <p className="fine">{syllabus.note}</p>}

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {!syllabus.canEdit ? null : editing ? (
        <>
          <label>
            Syllabus text
            <textarea
              rows={10}
              maxLength={100000}
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder={"Paste the whole syllabus here, from Simple Syllabus or any document."}
            />
          </label>
          <p className="fine">
            Paste the whole syllabus. TeachWell stores the text only, checks it for the elements your college requires, and never changes anything in
            Canvas or Simple Syllabus.
          </p>
          <div className="finding-actions">
            <button type="button" disabled={busy || !text.trim()} onClick={save}>
              {busy ? "Saving…" : "Save syllabus"}
            </button>
            <button
              type="button"
              className="secondary"
              onClick={() => {
                setEditing(false);
                setText("");
                setError("");
              }}
            >
              Cancel
            </button>
          </div>
        </>
      ) : (
        <>
          {/* Reading what is stored must not require entering the editor. Owner-only, because the API returns the
              text only to the owner; a reviewer sees the panel's state and reads the syllabus itself under Course
              content, where it now carries the same allowlisted preview as a page. */}
          {syllabus.text && (
            <details className="syllabus-preview">
              <summary>Show syllabus</summary>
              <div className="syllabus-text">
                {syllabus.text.split("\n").map((line, i) => (
                  <p key={i}>{line}</p>
                ))}
              </div>
            </details>
          )}
          <div className="finding-actions">
            <button
              type="button"
              onClick={() => {
                setText(syllabus.text ?? "");
                setEditing(true);
              }}
            >
              {syllabus.source === "Pasted" ? "Edit pasted syllabus" : "Paste syllabus text"}
            </button>
            {syllabus.source === "Pasted" && (
              <button type="button" className="secondary" disabled={busy} onClick={clear}>
                Remove
              </button>
            )}
          </div>
        </>
      )}
    </section>
  );
}
