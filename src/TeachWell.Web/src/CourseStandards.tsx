import { useEffect, useState, type FormEvent } from "react";
import { SlidersHorizontal } from "lucide-react";
import { ApiError, DELIVERY_MODES, request, type CourseStandards as Standards } from "./api";
import "./Standards.css";
import { Collapsible } from "./Collapsible";

const modeLabel = (mode: string | null) => DELIVERY_MODES.find((m) => m.value === mode)?.label ?? "Not set";

/** Which standard set this course is reviewed against, and why. CTL reviewers with access maintain the classification (D4). */
export function CourseStandards({ courseId, onSessionExpired }: { courseId: string; onSessionExpired: (error: unknown) => void }) {
  const [standards, setStandards] = useState<Standards | null>(null);
  const [mode, setMode] = useState("");
  const [department, setDepartment] = useState("");
  const [program, setProgram] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError(e instanceof ApiError && e.status === 404 ? "This course is no longer available to you." : (e as Error).message);
  }
  function show(s: Standards) {
    setStandards(s);
    setMode(s.deliveryMode ?? "");
    setDepartment(s.department ?? "");
    setProgram(s.program ?? "");
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    request<Standards>(`/courses/${courseId}/standards`)
      .then((s) => active && show(s))
      .catch((e) => active && fail(e))
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
  }, [courseId, retry]);

  async function save(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError("");
    setNotice("");
    try {
      const before = standards?.applied.name;
      const updated = await request<Standards>(`/courses/${courseId}/classification`, { deliveryMode: mode || null, department: department.trim() || null, program: program.trim() || null }, "PUT");
      show(updated);
      setNotice(
        updated.applied.name === before
          ? "Classification saved."
          : `Classification saved. New reviews of this course will use ${updated.applied.name}. Reviews already started keep their standards.`,
      );
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }

  return (
    <Collapsible
      id="course-standards"
      title="Course standards"
      defaultOpen={false}
      icon={<SlidersHorizontal size={22} />}
      className="panel course-standards"
      meta={standards ? `${standards.applied.name}${standards.applied.versionNumber ? ` v${standards.applied.versionNumber}` : ""}` : undefined}
    >
      {loading && <p role="status">Loading standards…</p>}
      {error && (
        <div className="error" role="alert">
          {error}
          <button disabled={saving} onClick={() => setRetry((x) => x + 1)}>
            Retry
          </button>
        </div>
      )}
      {standards && (
        <>
          <dl>
            <dt>Reviewed against</dt>
            <dd>
              {standards.applied.name}
              {standards.applied.versionNumber ? ` (version ${standards.applied.versionNumber})` : ""} · {standards.applied.scope}
            </dd>
            <dt>Why</dt>
            <dd>{standards.applied.reason}</dd>
            <dt>Delivery mode</dt>
            <dd>{modeLabel(standards.deliveryMode)}</dd>
            <dt>Department</dt>
            <dd>{standards.department ?? "Not set"}</dd>
            <dt>Program</dt>
            <dd>{standards.program ?? "Not set"}</dd>
          </dl>
          {standards.canClassify ? (
            <form onSubmit={save} aria-label="Course classification">
              <label>
                Delivery mode
                <select value={mode} onChange={(e) => setMode(e.target.value)}>
                  <option value="">Not set</option>
                  {DELIVERY_MODES.map((m) => (
                    <option key={m.value} value={m.value}>
                      {m.label}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Department
                <input maxLength={120} value={department} onChange={(e) => setDepartment(e.target.value)} placeholder="Not set" />
              </label>
              <label>
                Program (for oversight only)
                <input maxLength={120} value={program} onChange={(e) => setProgram(e.target.value)} placeholder="Not set" />
              </label>
              <button disabled={saving}>Save classification</button>
            </form>
          ) : (
            <p className="fine">The CTL maintains this course's delivery mode and department, which decide the standards it is reviewed against.</p>
          )}
          {notice && (
            <p className="success" role="status">
              {notice}
            </p>
          )}
        </>
      )}
    </Collapsible>
  );
}
