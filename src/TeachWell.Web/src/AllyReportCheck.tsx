import { useState } from "react";
import { FileSearch } from "lucide-react";
import { ApiError, uploadWithProgress } from "./api";

interface IssueFound {
  code: string;
  description: string;
  severity: string;
  pureGain: boolean;
  overlap: string | null;
  note: string | null;
  rows: number;
  places: number;
}
interface Check {
  kind: "Courses" | "Items";
  fileName: string;
  rows: number;
  missingColumns: string[];
  unknownColumns: string[];
  notes: string[];
  issues: IssueFound[];
  issueColumnsRecognised: number;
  issueColumnsKnown: number;
  courses: { total: number; matched: number; unmatched: number; matchedPercent: number; notes: string[] } | null;
  unmatchedCourseIds: string[];
  caveat: string;
}

/**
 * Check an Anthology Ally report export against TeachWell's reader (ADR 020 slice 1).
 *
 * This is a format check, not an import. TeachWell's Ally catalogue was built from Anthology's documentation rather
 * than from a real export, and this screen exists to settle that one question before anything is stored: upload a
 * real file and see exactly what the reader understood, including every column it did not recognise.
 *
 * Nothing is saved — no scores, no findings, no copy of the file — and the screen says so before the upload.
 */
export function AllyReportCheck({ onSessionExpired }: { onSessionExpired: (error: unknown) => void }) {
  const [result, setResult] = useState<Check | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState("");

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    if (!(form.get("file") as File | null)?.name) return;
    setError("");
    setResult(null);
    setProgress(0);
    try {
      setResult(await uploadWithProgress<Check>("/ally/report-check", form, setProgress));
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
      else setError((e as Error).message);
    } finally {
      setProgress(null);
    }
  }

  const gains = result?.issues.filter((i) => i.pureGain) ?? [];
  const overlaps = result?.issues.filter((i) => !i.pureGain) ?? [];

  return (
    <section className="panel" aria-labelledby="ally-check-title">
      <h2 id="ally-check-title">
        <FileSearch size={16} /> Check an Ally report
      </h2>
      <p className="fine">
        TeachWell can read Anthology Ally's institution report export. Its catalogue of Ally's checks was built from
        Anthology's documentation, not from a real file, so upload one of the CSVs from your institution report to see
        exactly what TeachWell understood — and what it did not.
      </p>
      <p className="fine">
        <strong>Nothing is saved.</strong> No scores, no findings, and no copy of the file. This only reports what the
        reader made of it.
      </p>

      <form onSubmit={submit}>
        <label>
          Ally report CSV
          <input type="file" name="file" accept=".csv,text/csv" required />
        </label>
        <div className="finding-actions">
          <button type="submit" disabled={progress !== null}>
            {progress === null
              ? "Check this file"
              : progress < 1
                ? `Uploading… ${Math.round(progress * 100)}%`
                : "Reading the file…"}
          </button>
        </div>
      </form>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {result && (
        <div aria-live="polite">
          <h3>What TeachWell read</h3>
          <dl className="conformance-summary">
            <div>
              <dt>Report</dt>
              <dd>{result.kind === "Courses" ? "Courses report (one row per course)" : "One course's report (one row per item)"}</dd>
            </div>
            <div>
              <dt>Rows read</dt>
              <dd>{result.rows.toLocaleString()}</dd>
            </div>
            <div>
              <dt>Ally checks recognised</dt>
              <dd>
                {result.issueColumnsRecognised} of {result.issueColumnsKnown}
              </dd>
            </div>
            {result.courses && (
              <div>
                <dt>Courses matched</dt>
                <dd>
                  {result.courses.matched} of {result.courses.total} ({result.courses.matchedPercent}%)
                </dd>
              </div>
            )}
          </dl>

          {/* The honest headline: a recognised count below the known count means the file differs from the docs. */}
          {result.issueColumnsRecognised < result.issueColumnsKnown && (
            <p className="fine">
              This file does not carry every Ally check TeachWell knows about. That is expected if your Ally
              configuration differs, and a problem if it means the columns are named differently than documented.
            </p>
          )}

          {result.notes.length > 0 && (
            <>
              <h3>What to look at</h3>
              <ul className="fine">
                {result.notes.map((n, i) => (
                  <li key={i}>{n}</li>
                ))}
              </ul>
            </>
          )}

          {result.unknownColumns.length > 0 && (
            <>
              <h3>Columns TeachWell does not know</h3>
              <p className="fine">
                These were ignored. If Ally has added a rule, TeachWell needs updating before it can report on it.
              </p>
              <p className="fine">{result.unknownColumns.join(" · ")}</p>
            </>
          )}

          {result.missingColumns.length > 0 && (
            <>
              <h3>Expected columns this file does not have</h3>
              <p className="fine">{result.missingColumns.join(" · ")}</p>
            </>
          )}

          {gains.length > 0 && (
            <>
              <h3>Issues Ally found that TeachWell cannot check itself</h3>
              <p className="fine">This is what reading Ally's report adds: mostly the course files TeachWell does not open.</p>
              <div className="table-scroll">
                <table className="feedback-table">
                  <thead>
                    <tr>
                      <th scope="col">Ally check</th>
                      <th scope="col">What it means</th>
                      <th scope="col">Severity</th>
                      <th scope="col">Rows</th>
                      <th scope="col">Places</th>
                    </tr>
                  </thead>
                  <tbody>
                    {gains.map((i) => (
                      <tr key={i.code}>
                        <th scope="row">{i.code}</th>
                        <td>{i.description}</td>
                        <td>{i.severity}</td>
                        <td>{i.rows.toLocaleString()}</td>
                        <td>{i.places.toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}

          {overlaps.length > 0 && (
            <>
              <h3>Issues TeachWell already checks</h3>
              <p className="fine">
                Where both tools find the same thing, TeachWell's own finding would be used, so one issue never becomes
                two steps.
              </p>
              <ul className="fine">
                {overlaps.map((i) => (
                  <li key={i.code}>
                    <strong>{i.code}</strong> → {i.overlap} ({i.places.toLocaleString()} places)
                    {i.note ? ` — ${i.note}` : ""}
                  </li>
                ))}
              </ul>
            </>
          )}

          {result.courses && result.courses.notes.length > 0 && (
            <>
              <h3>Matching Ally's courses to TeachWell's</h3>
              <ul className="fine">
                {result.courses.notes.map((n, i) => (
                  <li key={i}>{n}</li>
                ))}
              </ul>
              {result.unmatchedCourseIds.length > 0 && (
                <p className="fine">Canvas course ids not imported here: {result.unmatchedCourseIds.join(", ")}</p>
              )}
            </>
          )}

          <p className="fine">{result.caveat}</p>
        </div>
      )}
    </section>
  );
}
