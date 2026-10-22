import { useEffect, useState } from "react";
import { MessageSquareWarning } from "lucide-react";
import { ApiError, ASPECTS, request } from "./api";
import "./Guide.css";

interface Reason {
  courseTitle: string;
  reason: string;
  setAt: string;
}
interface CheckFeedback {
  checkCode: string;
  name: string;
  aspect: string;
  notApplicable: number;
  done: number;
  notApplicablePercent: number | null;
  courses: number;
  tuningCandidate: boolean;
  reasons: Reason[];
  reasonsNotShown: number;
}
interface Feedback {
  scope: { description: string; generatedAt: string; caveats: string[] };
  courses: number;
  checks: CheckFeedback[];
}

const aspectLabel = (value: string) => ASPECTS.find((a) => a.value === value)?.label ?? value;

/**
 * ADR 011 slice 6: how instructors respond to each check in the improvement guide. A check often marked "not applicable" is a
 * candidate for a tuned threshold, an extra word-list entry or a disabled check in the next standard-set version.
 */
export function GuideFeedback({ onSessionExpired }: { onSessionExpired: (error: unknown) => void }) {
  const [data, setData] = useState<Feedback | null>(null);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    setError("");
    request<Feedback>("/standards/feedback")
      .then((d) => active && setData(d))
      .catch((e) => {
        if (!active) return;
        if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
        else setError((e as Error).message);
      });
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [retry]);

  return (
    <section className="panel guide-feedback" aria-labelledby="guide-feedback-title">
      <h2 id="guide-feedback-title">
        <MessageSquareWarning size={18} /> How instructors respond to checks
      </h2>
      <p className="fine">
        When instructors mark a step in their improvement guide as not applicable, they give a reason. A check that is often set aside may need a
        different threshold, a word-list entry or to be turned off in the next version of a standard set.
      </p>
      {error ? (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Try again</button>
        </div>
      ) : !data ? (
        <p role="status">Loading instructor responses…</p>
      ) : data.checks.length === 0 ? (
        <p className="sharing-empty">No instructor has marked a guide step yet. Responses appear here as instructors work through their guides.</p>
      ) : (
        <>
          <p className="fine">
            {data.scope.description} · {data.courses} {data.courses === 1 ? "course" : "courses"} · generated {new Date(data.scope.generatedAt).toLocaleString()}
          </p>
          <div className="table-scroll">
            <table className="feedback-table">
              <thead>
                <tr>
                  <th scope="col">Check</th>
                  <th scope="col">Not applicable</th>
                  <th scope="col">Done</th>
                  <th scope="col">Share set aside</th>
                  <th scope="col">Courses</th>
                </tr>
              </thead>
              <tbody>
                {data.checks.map((c) => (
                  <tr key={c.checkCode}>
                    <th scope="row">
                      {c.name} <small>{aspectLabel(c.aspect)}</small>
                      {c.tuningCandidate && <span className="tag severity-medium">REVIEW THIS CHECK</span>}
                      {(c.reasons.length > 0 || c.reasonsNotShown > 0) && (
                        <details>
                          <summary>Reasons ({c.notApplicable})</summary>
                          <ul>
                            {c.reasons.map((r, i) => (
                              <li key={i}>
                                "{r.reason}" <small>· {r.courseTitle}, {new Date(r.setAt).toLocaleDateString()}</small>
                              </li>
                            ))}
                          </ul>
                          {c.reasonsNotShown > 0 && (
                            <p className="fine">{c.reasonsNotShown} more from courses you cannot open, or older ones.</p>
                          )}
                        </details>
                      )}
                    </th>
                    <td>{c.notApplicable}</td>
                    <td>{c.done}</td>
                    <td>{c.notApplicablePercent === null ? "—" : `${c.notApplicablePercent}%`}</td>
                    <td>{c.courses}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <details className="report-details">
            <summary>About these figures</summary>
            <ul>
              {data.scope.caveats.map((c) => (
                <li key={c}>{c}</li>
              ))}
            </ul>
          </details>
        </>
      )}
    </section>
  );
}
