import { useEffect, useState } from "react";
import { Download, Gauge } from "lucide-react";
import { ApiError, request, type CtlOverviewReport, type RevisionReport } from "./api";
import "./Reports.css";
import { supportCategoryLabel, taskStatusLabel } from "./labels";

export function Reports({ isCtl, onSessionExpired }: { isCtl: boolean; onSessionExpired: (error: unknown) => void }) {
  const [overview, setOverview] = useState<CtlOverviewReport | null>(null);
  const [revisions, setRevisions] = useState<RevisionReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    Promise.all([isCtl ? request<CtlOverviewReport>("/reports/ctl/overview") : Promise.resolve(null), request<RevisionReport>("/reports/revisions")])
      .then(([o, r]) => {
        if (active) {
          setOverview(o);
          setRevisions(r);
        }
      })
      .catch((e) => {
        if (active) fail(e);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [isCtl, retry]);

  return (
    <>
      <span className="eyebrow">ALIGNMENT AND REPORTING</span>
      <h1>Reports</h1>
      <p className="intro">Figures describe what TeachWell has recorded. They are not a compliance, accreditation or accessibility certification.</p>
      {error && (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Refresh</button>
        </div>
      )}
      {loading && <p role="status">Loading reports…</p>}

      {overview && (
        <section className="panel" aria-labelledby="overview-title">
          <div className="review-heading">
            <Gauge size={22} />
            <div>
              <h2 id="overview-title">CTL overview</h2>
              <p>{overview.scope.description}</p>
            </div>
          </div>
          <div className="report-grid">
            <div>
              <dt>Accessible courses</dt>
              <dd>{overview.accessibleCourses}</dd>
            </div>
            <div>
              <dt>Published reviews</dt>
              <dd>{overview.publishedReviews}</dd>
            </div>
            <div>
              <dt>Outstanding findings</dt>
              <dd>{overview.outstandingFindings}</dd>
            </div>
          </div>
          {overview.tasksByStatus.length > 0 && (
            <>
              <h3 className="criteria-title">Revision tasks by status</h3>
              <ul className="category-scores">
                {overview.tasksByStatus.map((s) => (
                  <li key={s.status}>
                    <span>{taskStatusLabel(s.status)}</span>
                    <span>
                      {s.count} task{s.count === 1 ? "" : "s"} · average {s.averageAgeDays ?? "—"} days old
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}
          {overview.supportThemes.length > 0 && (
            <>
              <h3 className="criteria-title">Common support topics across the college</h3>
              <ul className="category-scores">
                {overview.supportThemes.map((t) => (
                  <li key={t.category}>
                    <span>{supportCategoryLabel(t.category)}</span>
                    <span>
                      {t.count} {t.count === 1 ? "ticket" : "tickets"}
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}
          {overview.scope.caveats.map((c) => (
            <p className="fine" key={c}>
              {c}
            </p>
          ))}
        </section>
      )}

      {revisions && (
        <section className="panel" aria-labelledby="revisions-title">
          <div className="review-heading">
            <Gauge size={22} />
            <div>
              <h2 id="revisions-title">Your revision tasks</h2>
              <p>{revisions.scope.description}</p>
            </div>
          </div>
          {revisions.openTasks.length === 0 ? (
            <p className="sharing-empty">No open revision tasks.</p>
          ) : (
            <ul className="task-list">
              {revisions.openTasks.map((t) => (
                <li key={t.id}>
                  <div>
                    <span className={"tag status-" + t.status.toLowerCase()}>{taskStatusLabel(t.status).toUpperCase()}</span>{" "}
                    <span className={"tag severity-" + t.priority.toLowerCase()}>{t.priority.toUpperCase()}</span>
                    <strong>{t.title}</strong>
                    <small>
                      {t.courseTitle} · open {t.daysOpen} day{t.daysOpen === 1 ? "" : "s"}
                    </small>
                  </div>
                </li>
              ))}
            </ul>
          )}
          <a className="link-button" href="/api/v1/reports/revisions?format=csv" download="revision-tasks.csv">
            <Download size={14} /> Download as CSV
          </a>
          {revisions.scope.caveats.map((c) => (
            <p className="fine" key={c}>
              {c}
            </p>
          ))}
        </section>
      )}
    </>
  );
}
