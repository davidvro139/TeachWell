import { useEffect, useState } from "react";
import { ApiError, request } from "./api";
import { GuideSummary, standardsLabel, statusLabel, type ReadinessRequest } from "./CourseReadiness";
import "./Guide.css";

/**
 * The CTL's readiness queue (ADR 013): open requests from across the college, and what this reviewer decided recently. Taking a request
 * gives the reviewer review access to the course; the decision is made on the course page, next to the course's reviews.
 */
export function ReadinessQueue({ onOpenCourse, onSessionExpired }: { onOpenCourse: (courseId: string) => void; onSessionExpired: (error: unknown) => void }) {
  const [items, setItems] = useState<ReadinessRequest[] | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [retry, setRetry] = useState(0);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  useEffect(() => {
    let active = true;
    setError("");
    request<ReadinessRequest[]>("/readiness/queue")
      .then((q) => active && setItems(q))
      .catch((e) => active && fail(e));
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [retry]);

  async function take(r: ReadinessRequest) {
    setBusy(true);
    setError("");
    try {
      await request(`/readiness/${r.id}/take`, { revision: r.revision });
      onOpenCourse(r.courseId);
    } catch (e) {
      fail(e);
      setRetry((x) => x + 1);
    } finally {
      setBusy(false);
    }
  }

  const open = items?.filter((r) => r.status === "Requested" || r.status === "InReview") ?? [];
  const decided = items?.filter((r) => r.status === "Endorsed" || r.status === "ChangesRequested") ?? [];
  return (
    <>
      <span className="eyebrow">CTL REVIEW FRAMEWORK</span>
      <h1>Readiness reviews</h1>
      <p className="intro">
        Instructors ask for a readiness review once their improvement guide's required steps are resolved or justified. Take a request to get
        review access to the course, review it, then endorse it or request changes on the course page.
      </p>
      {error && (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Refresh</button>
        </div>
      )}
      {!items ? (
        !error && <p role="status">Loading readiness requests…</p>
      ) : (
        <>
          <section className="panel" aria-labelledby="readiness-open">
            <h2 id="readiness-open">Open requests ({open.length})</h2>
            {open.length === 0 ? (
              <p className="sharing-empty">No readiness reviews are waiting.</p>
            ) : (
              <ul className="guide-places">
                {open.map((r) => (
                  <li key={r.id}>
                    <div>
                      <strong>
                        {r.courseCode} · {r.courseTitle}
                      </strong>
                      <p className="fine">
                        {r.requestedByName} asked on {new Date(r.requestedAt).toLocaleDateString()} against {standardsLabel(r)} ·{" "}
                        {statusLabel[r.status]}
                        {r.status === "InReview" ? ` with ${r.takenByMe ? "you" : r.reviewerName}` : ""}
                      </p>
                      {r.note && <p>"{r.note}"</p>}
                      {r.guide && <GuideSummary summary={r.guide} />}
                    </div>
                    <div className="finding-actions">
                      {r.status === "Requested" && (
                        <button type="button" disabled={busy} onClick={() => take(r)} aria-label={`Take request for ${r.courseCode}`}>
                          Take request
                        </button>
                      )}
                      {r.takenByMe && (
                        <button type="button" className="secondary" onClick={() => onOpenCourse(r.courseId)}>
                          Open course
                        </button>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>
          {decided.length > 0 && (
            <section className="panel" aria-labelledby="readiness-decided">
              <h2 id="readiness-decided">Recently decided by you</h2>
              <ul>
                {decided.map((r) => (
                  <li key={r.id}>
                    {r.courseCode} · {statusLabel[r.status]} on {new Date(r.decidedAt!).toLocaleDateString()} · {standardsLabel(r)}
                  </li>
                ))}
              </ul>
            </section>
          )}
        </>
      )}
    </>
  );
}
