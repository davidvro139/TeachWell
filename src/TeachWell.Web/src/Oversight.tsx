import { useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { ApiError, ASPECTS, request } from "./api";
import "./Guide.css";
import { supportCategoryLabel } from "./labels";

interface Oversight {
  scope: { description: string; generatedAt: string; caveats: string[] };
  department: string | null;
  departments: string[];
  program: string | null;
  programs: string[];
  autoRecheckHours: number;
  lastAutomaticRecheck: string | null;
  courses: number;
  coursesChecked: number;
  readiness: { state: string; label: string; courses: number }[];
  issuesByAspect: { aspect: string; label: string; openPlaces: number; coursesAffected: number }[];
  topChecks: { checkCode: string; name: string; aspect: string; coursesAffected: number; openPlaces: number }[];
  helpRequests: { total: number; open: number; byCategory: { category: string; count: number }[] };
  progress: { weekEnding: string; coursesChecked: number; openPlaces: number; donePlaces: number; notApplicablePlaces: number }[];
  courseRows: {
    courseId: string;
    code: string;
    title: string;
    department: string | null;
    deliveryMode: string | null;
    readinessState: string;
    readinessLabel: string;
    lastChecked: string | null;
    program: string | null;
    openSteps: number | null;
    requiredOpenSteps: number | null;
    canOpen: boolean;
  }[];
  endorsements: { courseId: string; courseCode: string; courseTitle: string; department: string | null; wording: string; endorsedAt: string; validUntil: string; recheckDue: boolean }[];
  canRefresh: boolean;
}

const day = (value: string) => new Date(value).toLocaleDateString();
// Week ends are UTC calendar days from the server; show that day, not the local time of UTC midnight.
const utcDay = (value: string) => new Date(value).toLocaleDateString(undefined, { timeZone: "UTC" });
const aspectLabel = (value: string) => ASPECTS.find((a) => a.value === value)?.label ?? value;
const modeLabel = (value: string | null) => (value === "FaceToFace" ? "Face-to-face" : (value ?? "—"));

/**
 * Phase 11 (ADR 014): readiness, common issues, help requests and guide progress across the college, for CTL reviewers and Academic
 * administrators. Figures describe courses, never people. Course content still needs ownership or a share.
 */
export function Oversight({ onOpenCourse, onSessionExpired }: { onOpenCourse: (courseId: string) => void; onSessionExpired: (error: unknown) => void }) {
  const [data, setData] = useState<Oversight | null>(null);
  const [department, setDepartment] = useState("");
  const [program, setProgram] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [retry, setRetry] = useState(0);
  const params = new URLSearchParams();
  if (department) params.set("department", department);
  if (program) params.set("program", program);
  const filters = params.toString() ? "?" + params.toString() : "";

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  useEffect(() => {
    let active = true;
    setError("");
    request<Oversight>("/oversight" + filters)
      .then((d) => active && setData(d))
      .catch((e) => active && fail(e));
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters, retry]);

  async function refresh() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = await request<{ checkedCourses: number }>("/oversight/refresh", {});
      setNotice(`Re-checked ${result.checkedCourses} ${result.checkedCourses === 1 ? "course" : "courses"} with their current standards.`);
      setRetry((x) => x + 1);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }

  const count = (state: string) => data?.readiness.find((r) => r.state === state)?.courses ?? 0;
  return (
    <>
      <span className="eyebrow">RESULTS ACROSS THE COLLEGE</span>
      <h1>Course quality oversight</h1>
      <p className="intro">
        Readiness, common issues and improvement-guide progress across courses. Figures describe courses, never the people who teach or review
        them, and nothing here opens course content.
      </p>
      <div className="form-row oversight-controls">
        <label>
          Department
          <select value={department} onChange={(e) => setDepartment(e.target.value)} disabled={!data}>
            <option value="">All departments</option>
            {data?.departments.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
        </label>
        <label>
          Program
          <select value={program} onChange={(e) => setProgram(e.target.value)} disabled={!data}>
            <option value="">All programs</option>
            {data?.programs.map((p) => (
              <option key={p} value={p}>
                {p}
              </option>
            ))}
          </select>
        </label>
        {data && (
          // A plain link: the browser sends the session cookie and saves the file the server names.
          <a className="button secondary" href={"/api/v1/oversight/export" + filters} download>
            Download CSV
          </a>
        )}
        {data?.canRefresh && (
          <button type="button" className="secondary" disabled={busy} onClick={refresh}>
            <RefreshCw size={14} /> {busy ? "Re-checking…" : "Re-check all courses"}
          </button>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {error}
          <button onClick={() => setRetry((x) => x + 1)}>Try again</button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {!data ? (
        !error && <p role="status">Loading oversight…</p>
      ) : (
        <>
          <p className="fine">
            {data.scope.description} · generated {new Date(data.scope.generatedAt).toLocaleString()} ·{" "}
            {data.autoRecheckHours > 0
              ? `courses are re-checked automatically every ${data.autoRecheckHours} hours${
                  data.lastAutomaticRecheck ? `, last on ${new Date(data.lastAutomaticRecheck).toLocaleString()}` : ", not yet run"
                }`
              : "automatic re-checks are off"}
          </p>
          <div className="stats oversight-stats">
            <div>
              <strong>{data.courses}</strong>
              <span>Courses</span>
            </div>
            <div>
              <strong>{data.coursesChecked}</strong>
              <span>Checked by the guide</span>
            </div>
            <div>
              <strong>{count("Endorsed") + count("RecheckDue")}</strong>
              <span>Endorsed ({count("RecheckDue")} re-check due)</span>
            </div>
            <div>
              <strong>{count("Requested") + count("InReview")}</strong>
              <span>Readiness reviews open</span>
            </div>
            <div>
              <strong>{data.helpRequests.open}</strong>
              <span>Open help requests from the guide</span>
            </div>
          </div>

          <section className="panel" aria-labelledby="oversight-readiness">
            <h2 id="oversight-readiness">Readiness</h2>
            <ul className="oversight-bars">
              {data.readiness.map((r) => (
                <li key={r.state}>
                  <span>{r.label}</span>
                  <meter min={0} max={Math.max(1, data.courses)} value={r.courses} aria-label={`${r.label}: ${r.courses} of ${data.courses} courses`} />
                  <strong>{r.courses}</strong>
                </li>
              ))}
            </ul>
          </section>

          <section className="panel" aria-labelledby="oversight-issues">
            <h2 id="oversight-issues">Where courses most need work</h2>
            {data.coursesChecked === 0 ? (
              <p className="sharing-empty">
                No course has been checked by the improvement guide yet.{data.canRefresh ? " Re-check all courses to fill this in." : ""}
              </p>
            ) : (
              <>
                <ul className="oversight-bars">
                  {data.issuesByAspect.map((a) => (
                    <li key={a.aspect}>
                      <span>{a.label}</span>
                      <meter
                        min={0}
                        max={Math.max(1, data.coursesChecked)}
                        value={a.coursesAffected}
                        aria-label={`${a.label}: ${a.coursesAffected} of ${data.coursesChecked} checked courses have open steps`}
                      />
                      <strong>
                        {a.coursesAffected} of {data.coursesChecked} courses
                      </strong>
                      <small>{a.openPlaces} places</small>
                    </li>
                  ))}
                </ul>
                <h3>Most common issues</h3>
                <div className="table-scroll">
                  <table className="feedback-table">
                    <thead>
                      <tr>
                        <th scope="col">Issue</th>
                        <th scope="col">Courses</th>
                        <th scope="col">Places</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.topChecks.map((c) => (
                        <tr key={c.checkCode}>
                          <th scope="row">
                            {c.name} <small>{aspectLabel(c.aspect)}</small>
                          </th>
                          <td>{c.coursesAffected}</td>
                          <td>{c.openPlaces}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </section>

          <section className="panel" aria-labelledby="oversight-progress">
            <h2 id="oversight-progress">Guide progress over time</h2>
            {data.progress.length === 0 ? (
              <p className="sharing-empty">No guide checks recorded yet.</p>
            ) : (
              <div className="table-scroll">
                <table className="feedback-table">
                  <thead>
                    <tr>
                      <th scope="col">Week ending</th>
                      <th scope="col">Courses checked</th>
                      <th scope="col">Open places</th>
                      <th scope="col">Marked done</th>
                      <th scope="col">Not applicable</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.progress.map((p) => (
                      <tr key={p.weekEnding}>
                        <th scope="row">{utcDay(p.weekEnding)}</th>
                        <td>{p.coursesChecked}</td>
                        <td>{p.openPlaces}</td>
                        <td>{p.donePlaces}</td>
                        <td>{p.notApplicablePlaces}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <p className="fine">
              Help requests sent from guide steps: {data.helpRequests.total} in total, {data.helpRequests.open} open
              {data.helpRequests.byCategory.length > 0 ? ` (${data.helpRequests.byCategory.map((c) => `${supportCategoryLabel(c.category)} ${c.count}`).join(", ")})` : ""}.
            </p>
          </section>

          <section className="panel" aria-labelledby="oversight-courses">
            <h2 id="oversight-courses">Courses</h2>
            <div className="table-scroll">
              <table className="feedback-table oversight-courses">
                <thead>
                  <tr>
                    <th scope="col">Course</th>
                    <th scope="col">Department</th>
                    <th scope="col">Program</th>
                    <th scope="col">Mode</th>
                    <th scope="col">Readiness</th>
                    <th scope="col">Open steps (required)</th>
                    <th scope="col">Last checked</th>
                  </tr>
                </thead>
                <tbody>
                  {data.courseRows.map((c) => (
                    <tr key={c.courseId}>
                      <th scope="row">
                        {c.canOpen ? (
                          <button type="button" className="link-button" onClick={() => onOpenCourse(c.courseId)}>
                            {c.code} · {c.title}
                          </button>
                        ) : (
                          <>
                            {c.code} · {c.title}
                          </>
                        )}
                      </th>
                      <td>{c.department ?? "—"}</td>
                      <td>{c.program ?? "—"}</td>
                      <td>{modeLabel(c.deliveryMode)}</td>
                      <td>{c.readinessLabel}</td>
                      <td>{c.openSteps === null ? "—" : `${c.openSteps} (${c.requiredOpenSteps})`}</td>
                      <td>{c.lastChecked ? day(c.lastChecked) : "Never"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="panel" aria-labelledby="oversight-endorsements">
            <h2 id="oversight-endorsements">Endorsement history</h2>
            {data.endorsements.length === 0 ? (
              <p className="sharing-empty">No course has been endorsed yet.</p>
            ) : (
              <ul className="guide-places">
                {data.endorsements.map((e) => (
                  <li key={e.courseId + e.endorsedAt}>
                    <div>
                      <strong>
                        {e.courseCode} · {e.courseTitle}
                      </strong>
                      <p>{e.wording}</p>
                      <p className="fine">
                        Current until {day(e.validUntil)}
                        {e.recheckDue ? " · re-check due" : ""}
                      </p>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>

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
    </>
  );
}
