import { useEffect, useState, type FormEvent } from "react";
import { BarChart3, Target } from "lucide-react";
import {
  ApiError,
  request,
  type AlignmentReport,
  type Competency,
  type CourseDetail,
  type CourseQualityReport,
  type Element,
  isAssessment,
} from "./api";
import "./Reports.css";
import { Collapsible } from "./Collapsible";
import { Glossed, Term } from "./Glossary";
import { OutcomeImport } from "./OutcomeImport";
import { MappingGrid } from "./MappingGrid";

interface Props {
  course: CourseDetail;
  canManage: boolean;
  onSessionExpired: (error: unknown) => void;
}

const percent = (value: number | null) => (value === null ? "Not yet available" : `${value}%`);

export function CourseReports({ course, canManage, onSessionExpired }: Props) {
  const [competencies, setCompetencies] = useState<Competency[]>([]);
  const [alignment, setAlignment] = useState<AlignmentReport | null>(null);
  const [quality, setQuality] = useState<CourseQualityReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [creating, setCreating] = useState(false);
  const [pasting, setPasting] = useState(false);
  const [gridOpen, setGridOpen] = useState(false);
  const [code, setCode] = useState(""), [title, setTitle] = useState(""), [description, setDescription] = useState("");
  const [mapChoice, setMapChoice] = useState<Record<string, string>>({});
  // Evidence that can be mapped to a competency: assignments, quizzes and discussions.
  const assignments = course.elements.filter((e: Element) => isAssessment(e.type) || e.type === "discussion");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  async function reload() {
    const [c, a, q] = await Promise.all([
      request<Competency[]>(`/courses/${course.id}/competencies`),
      request<AlignmentReport>(`/reports/course/${course.id}/alignment`),
      request<CourseQualityReport>(`/reports/course/${course.id}/quality`),
    ]);
    setCompetencies(c);
    setAlignment(a);
    setQuality(q);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    reload()
      .catch((e) => {
        if (active) fail(e);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [course.id, retry]);

  async function act(work: () => Promise<unknown>, success: string) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await work();
      await reload();
      setNotice(success);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  async function create(e: FormEvent) {
    e.preventDefault();
    await act(() => request(`/courses/${course.id}/competencies`, { code, title, description }), "Competency added.");
    setCode("");
    setTitle("");
    setDescription("");
    setCreating(false);
  }

  if (loading) return <p role="status">Loading reports…</p>;
  return (
    <Collapsible
      id="reports"
      title="Alignment and quality"
      icon={<Target size={22} />}
      className="sharing-panel course-reports"
      busy={busy}
      defaultOpen={false}
      meta={
        alignment
          ? (competencies.length === 0
              ? `No competencies yet · ${alignment.totalAssessments} ${alignment.totalAssessments === 1 ? "assessment" : "assessments"} to map`
              : `Competency coverage ${percent(alignment.coveragePercent)} · ${alignment.unmappedAssessments.length} unmapped ${alignment.unmappedAssessments.length === 1 ? "assessment" : "assessments"}`) +
            (quality ? ` · ${quality.outstandingFindings.length} outstanding ${quality.outstandingFindings.length === 1 ? "finding" : "findings"}` : "")
          : undefined
      }
      description={<Glossed text="Competency coverage and review findings for this course. Figures are not a compliance or accreditation certification." />}
    >
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

      {alignment && (
        <div className="report-grid">
          <div>
            <dt>
              <Term id="competency-coverage">Competency coverage</Term>
            </dt>
            {/* With no competencies there is nothing to cover yet: say so rather than show 0%, which reads as a failing score. */}
            <dd>{competencies.length === 0 ? "Not started" : percent(alignment.coveragePercent)}</dd>
            <span className="fine">
              {competencies.length === 0
                ? "No competencies defined for this course yet"
                : `${alignment.mappedAssessmentCount} of ${alignment.totalAssessments} assessments mapped`}
            </span>
          </div>
          <div>
            <dt>
              <Term id="orphan-competency">Orphan competencies</Term>
            </dt>
            <dd>{alignment.orphanCompetencies.length}</dd>
            <span className="fine">No assessment evidence recorded</span>
          </div>
          <div>
            <dt>
              <Term id="unmapped-assessment">Unmapped assessments</Term>
            </dt>
            <dd>{alignment.unmappedAssessments.length}</dd>
            <span className="fine">Assignments, quizzes and graded discussions with no competency</span>
          </div>
        </div>
      )}
      {quality && (
        <div className="report-grid">
          <div>
            <dt>Reviewed coverage</dt>
            <dd>{percent(quality.reviewedCoveragePercent)}</dd>
            <span className="fine">
              {quality.elementsWithPublishedReview} of {quality.reviewableElements} elements have a published review
            </span>
          </div>
          <div>
            <dt>
              Average <Term id="rubric">rubric</Term> score
            </dt>
            <dd>{percent(quality.averageScorePercent)}</dd>
            <span className="fine">Across rated criteria in published reviews only</span>
          </div>
          <div>
            <dt>Outstanding findings</dt>
            <dd>{quality.outstandingFindings.length}</dd>
            <span className="fine">Confirmed, not yet resolved by a task</span>
          </div>
        </div>
      )}
      {(alignment?.scope.caveats ?? quality?.scope.caveats ?? []).map((c) => (
        <p className="fine" key={c}>
          {c}
        </p>
      ))}

      {quality && quality.outstandingFindings.length > 0 && (
        <details className="report-details">
          <summary>Outstanding findings ({quality.outstandingFindings.length})</summary>
          <ul className="finding-checklist">
            {quality.outstandingFindings.map((f) => (
              <li key={f.findingId}>
                <span className={"tag severity-" + f.severity.toLowerCase()}>{f.severity.toUpperCase()}</span>
                <span>
                  {f.title} — {f.elementTitle} {f.hasOpenTask ? "(a task is open)" : "(no task yet)"}
                </span>
              </li>
            ))}
          </ul>
        </details>
      )}

      <h3 className="criteria-title">Competencies</h3>
      {alignment && alignment.unmappedAssessments.length > 0 && <UnmappedList titles={alignment.unmappedAssessments.map((a) => a.title)} />}
      {competencies.length === 0 ? (
        <div className="sharing-empty">
          <p>No competencies recorded for this course yet, so coverage cannot be measured.</p>
          <p className="fine">
            Competencies are recorded in TeachWell; they are not imported from Canvas (including Canvas Outcomes), so imported courses start with none.{" "}
            {canManage
              ? "Add the course's competencies below, then map each assignment, quiz or graded discussion to the competency it provides evidence for."
              : "The course owner adds competencies and maps assessments to them."}
          </p>
        </div>
      ) : (
        <ul className="task-list competency-list">
          {competencies.map((c) => (
            <li key={c.id}>
              <div>
                <span className="tag">{c.code}</span>
                <strong>{c.title}</strong>
                <small>{c.mappings.length === 0 ? "No assessment evidence mapped yet" : c.mappings.map((m) => m.elementTitle).join(", ")}</small>
              </div>
              {canManage && (
                <div className="finding-actions">
                  <select
                    aria-label={`Map an assignment to ${c.title}`}
                    value={mapChoice[c.id] ?? ""}
                    onChange={(e) => setMapChoice({ ...mapChoice, [c.id]: e.target.value })}
                  >
                    <option value="">Map an assignment…</option>
                    {assignments.filter((a) => !c.mappings.some((m) => m.elementId === a.id)).map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.title}
                      </option>
                    ))}
                  </select>
                  <button
                    className="secondary"
                    disabled={busy || !mapChoice[c.id]}
                    onClick={() =>
                      act(() => request(`/courses/${course.id}/competencies/${c.id}/mappings`, { elementId: mapChoice[c.id] }), "Mapped.").then(() =>
                        setMapChoice({ ...mapChoice, [c.id]: "" }),
                      )
                    }
                  >
                    Map
                  </button>
                  {c.mappings.map((m) => (
                    <button
                      key={m.elementId}
                      className="secondary"
                      disabled={busy}
                      onClick={() => act(() => request(`/courses/${course.id}/competencies/${c.id}/mappings/${m.elementId}`, undefined, "DELETE"), "Unmapped.")}
                    >
                      Unmap {m.elementTitle}
                    </button>
                  ))}
                  <button className="secondary" disabled={busy} onClick={() => act(() => request(`/courses/${course.id}/competencies/${c.id}`, undefined, "DELETE"), "Competency removed.")}>
                    Delete
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {canManage &&
        (creating ? (
          <form className="panel create-task-panel" onSubmit={create}>
            <h3>
              <BarChart3 size={16} /> New competency
            </h3>
            <div className="form-row">
              <label>
                Code
                <input required maxLength={20} pattern="[A-Z0-9\-]+" value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} />
              </label>
              <label>
                Title
                <input required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
              </label>
            </div>
            <label>
              Description
              <textarea maxLength={2000} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
            </label>
            <div className="finding-actions">
              <button disabled={busy}>Add competency</button>
              <button type="button" className="secondary" onClick={() => setCreating(false)}>
                Cancel
              </button>
            </div>
          </form>
        ) : pasting ? (
          <OutcomeImport
            courseId={course.id}
            onCancel={() => setPasting(false)}
            onSessionExpired={onSessionExpired}
            onDone={(added) => {
              setPasting(false);
              act(() => Promise.resolve(), `Added ${added} ${added === 1 ? "competency" : "competencies"}.`);
            }}
          />
        ) : gridOpen ? (
          <MappingGrid
            course={course}
            competencies={competencies}
            onCancel={() => setGridOpen(false)}
            onSessionExpired={onSessionExpired}
            onSaved={(changed) => {
              setGridOpen(false);
              act(() => Promise.resolve(), `Saved ${changed} mapping ${changed === 1 ? "change" : "changes"}.`);
            }}
          />
        ) : (
          <div className="finding-actions">
            {competencies.length > 0 && assignments.length > 0 && <button onClick={() => setGridOpen(true)}>Map assessments</button>}
            <button className="secondary" onClick={() => setCreating(true)}>
              Add competency
            </button>
            <button className="secondary" onClick={() => setPasting(true)}>
              Paste outcomes
            </button>
          </div>
        ))}
    </Collapsible>
  );
}

// Natural sort keeps "A2-1" before "A10-1"; duplicates (for example several "MC-FTE" quizzes) are counted, not repeated.
function UnmappedList({ titles }: { titles: string[] }) {
  const [filter, setFilter] = useState("");
  const counts = new Map<string, number>();
  for (const t of titles) counts.set(t, (counts.get(t) ?? 0) + 1);
  const sorted = [...counts].sort(([a], [b]) => a.localeCompare(b, undefined, { numeric: true, sensitivity: "base" }));
  const query = filter.trim().toLowerCase();
  const shown = query ? sorted.filter(([t]) => t.toLowerCase().includes(query)) : sorted;
  return (
    <details className="report-details unmapped-list">
      <summary>
        Not yet mapped to a competency ({titles.length} {titles.length === 1 ? "assessment" : "assessments"})
      </summary>
      {sorted.length > 12 && (
        <input
          type="search"
          className="name-list-filter"
          aria-label="Filter unmapped assignments"
          placeholder="Filter assignments…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
        />
      )}
      <ul className="name-list">
        {shown.map(([t, n]) => (
          <li key={t}>
            {t}
            {n > 1 && <span className="fine"> ×{n}</span>}
          </li>
        ))}
      </ul>
      {shown.length === 0 && <p className="fine">No assignments match “{filter}”.</p>}
    </details>
  );
}
