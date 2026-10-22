import { useState } from "react";
import { ClipboardPaste } from "lucide-react";
import { ApiError, request } from "./api";
import { Term } from "./Glossary";

interface Proposed {
  code: string;
  title: string;
  include: boolean;
  bloomLevel: string | null;
  vaguePhrase: string | null;
  existingCode: string | null;
  notes: string[];
}
interface Proposal {
  outcomes: Proposed[];
  ignored: string[];
}
interface Row extends Proposed {
  description: string;
}

/**
 * Paste a list of learning outcomes (for example from Simple Syllabus), review what TeachWell read, edit, and approve them as
 * competencies in one step. Parsing and saving happen on the server; nothing is saved until "Add".
 */
export function OutcomeImport({
  courseId,
  onDone,
  onCancel,
  onSessionExpired,
}: {
  courseId: string;
  onDone: (added: number) => void;
  onCancel: () => void;
  onSessionExpired: (error: unknown) => void;
}) {
  const [text, setText] = useState("");
  const [rows, setRows] = useState<Row[] | null>(null);
  const [ignored, setIgnored] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  async function read() {
    setBusy(true);
    setError("");
    try {
      const proposal = await request<Proposal>(`/courses/${courseId}/competencies/parse`, { text });
      setRows(proposal.outcomes.map((o) => ({ ...o, description: "" })));
      setIgnored(proposal.ignored);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  async function save() {
    if (!rows) return;
    const chosen = rows.filter((r) => r.include);
    setBusy(true);
    setError("");
    try {
      await request(`/courses/${courseId}/competencies/bulk`, {
        items: chosen.map((r) => ({ code: r.code.trim(), title: r.title.trim(), description: r.description.trim() })),
      });
      onDone(chosen.length);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  const update = (index: number, change: Partial<Row>) => setRows((current) => current!.map((r, i) => (i === index ? { ...r, ...change } : r)));
  const chosen = rows?.filter((r) => r.include).length ?? 0;

  return (
    <section className="panel outcome-import" aria-labelledby="outcome-import-title">
      <h3 id="outcome-import-title">
        <ClipboardPaste size={16} /> Paste learning outcomes
      </h3>
      {rows === null ? (
        <>
          <p className="fine">
            Copy the Student Learning Outcomes from Simple Syllabus (or any document) and paste them here, one per line. Headings, bullets and
            numbering are tidied up; codes such as "SLO 2:" are kept. You can review and edit everything before it is saved.
          </p>
          <label>
            Outcomes
            <textarea rows={8} maxLength={20000} value={text} onChange={(e) => setText(e.target.value)} placeholder={"Student Learning Outcomes\nImplement common HTML tags…\nDemonstrate the ability to…"} />
          </label>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <div className="finding-actions">
            <button type="button" disabled={busy || !text.trim()} onClick={read}>
              {busy ? "Reading…" : "Read outcomes"}
            </button>
            <button type="button" className="secondary" onClick={onCancel}>
              Cancel
            </button>
          </div>
        </>
      ) : (
        <>
          <p className="fine">
            TeachWell found {rows.length} {rows.length === 1 ? "outcome" : "outcomes"}. Edit the code or wording, untick any you do not want, then add
            them. Levels follow <Term id="blooms-taxonomy">Bloom's revised taxonomy</Term> and are suggestions.
          </p>
          {ignored.length > 0 && <p className="fine">Ignored as headings: {ignored.join(" · ")}</p>}
          <ol className="outcome-rows">
            {rows.map((r, i) => (
              <li key={i} className={r.include ? "" : "is-skipped"}>
                <label className="outcome-include">
                  <input type="checkbox" checked={r.include} onChange={(e) => update(i, { include: e.target.checked })} /> Add
                </label>
                <label>
                  Code
                  <input
                    aria-label={`Code for outcome ${i + 1}`}
                    maxLength={20}
                    value={r.code}
                    onChange={(e) => update(i, { code: e.target.value.toUpperCase().replace(/[^A-Z0-9-]/g, "") })}
                  />
                </label>
                <label className="outcome-title">
                  Outcome
                  <textarea aria-label={`Outcome ${i + 1}`} rows={2} maxLength={400} value={r.title} onChange={(e) => update(i, { title: e.target.value })} />
                </label>
                <label className="outcome-description">
                  Description (optional)
                  <input aria-label={`Description for outcome ${i + 1}`} maxLength={2000} value={r.description} onChange={(e) => update(i, { description: e.target.value })} />
                </label>
                <div className="outcome-notes">
                  <span className="tag">{r.bloomLevel ? `Bloom: ${r.bloomLevel}` : "No level verb recognised"}</span>
                  {r.title.length > 200 && <span className="tag outline-flag">{r.title.length}/200 characters</span>}
                  {r.notes.map((n) => (
                    <span key={n} className="fine">
                      {n}
                    </span>
                  ))}
                </div>
              </li>
            ))}
          </ol>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <div className="finding-actions">
            <button type="button" disabled={busy || chosen === 0} onClick={save}>
              {busy ? "Adding…" : `Add ${chosen} ${chosen === 1 ? "competency" : "competencies"}`}
            </button>
            <button type="button" className="secondary" disabled={busy} onClick={() => setRows(null)}>
              Back to paste
            </button>
            <button type="button" className="secondary" disabled={busy} onClick={onCancel}>
              Cancel
            </button>
          </div>
        </>
      )}
    </section>
  );
}
