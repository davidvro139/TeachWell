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
  async function load() {
    try {
      setSyllabus(await request<Syllabus>(`/courses/${courseId}/syllabus`));
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
      setSyllabus(await request<Syllabus>(`/courses/${courseId}/syllabus`, { text }, "PUT"));
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
      setSyllabus(await request<Syllabus>(`/courses/${courseId}/syllabus`, undefined, "DELETE"));
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
      )}
    </section>
  );
}
