import { useEffect, useMemo, useState, type FormEvent } from "react";
import { SlidersHorizontal } from "lucide-react";
import {
  ApiError,
  ASPECTS,
  DELIVERY_MODES,
  PRIORITIES,
  request,
  type CheckCatalogEntry,
  type EndorsementRequirements,
  type RubricProfile,
  type Course,
  type StandardCheckSetting,
  type StandardSet,
  type StandardSetVersion,
  type StandardsPreview,
} from "./api";
import "./Reviews.css";
import "./Standards.css";
import { GuideFeedback } from "./GuideFeedback";
import { GuideResources } from "./GuideResources";

/** CTL standards library (ADR 011): versioned settings and instructor-facing guidance for every automated check. */
export function Standards({ onSessionExpired }: { onSessionExpired: (error: unknown) => void }) {
  const [sets, setSets] = useState<StandardSet[]>([]);
  const [catalog, setCatalog] = useState<CheckCatalogEntry[]>([]);
  const [courses, setCourses] = useState<Course[]>([]);
  const [selected, setSelected] = useState<StandardSetVersion | null>(null);
  const [checks, setChecks] = useState<StandardCheckSetting[]>([]);
  const [notes, setNotes] = useState("");
  const [endorsement, setEndorsement] = useState<EndorsementRequirements>({ courseReview: false, rubricProfileId: null, minimumItemReviews: 0 });
  const [rubrics, setRubrics] = useState<RubricProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [name, setName] = useState("");
  const [mode, setMode] = useState("");
  const [department, setDepartment] = useState("");
  const [previewCourse, setPreviewCourse] = useState("");
  const [preview, setPreview] = useState<StandardsPreview | null>(null);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  function show(version: StandardSetVersion) {
    setSelected(version);
    setChecks(version.checks);
    setNotes(version.notes);
    setEndorsement(version.endorsement);
    setPreview(null);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    Promise.all([
      request<StandardSet[]>("/standard-sets"),
      request<CheckCatalogEntry[]>("/review-checks"),
      request<Course[]>("/courses?role=CTLReviewer"),
      request<RubricProfile[]>("/rubrics"),
    ])
      .then(([s, c, list, r]) => {
        if (!active) return;
        setRubrics(r.filter((p) => p.versions.some((v) => v.status === "Published")));
        setSets(s);
        setCatalog(c);
        setCourses(list);
        setPreviewCourse((current) => current || list[0]?.id || "");
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
  }, [retry]);

  async function act(work: () => Promise<StandardSetVersion>, success: string) {
    setSaving(true);
    setError("");
    setNotice("");
    try {
      show(await work());
      setSets(await request<StandardSet[]>("/standard-sets"));
      setNotice(success);
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  function create(e: FormEvent) {
    e.preventDefault();
    act(
      () => request<StandardSetVersion>("/standard-sets", { name, deliveryMode: mode || null, department: department.trim() || null }),
      "Standard set created. Its first draft copies the standards that currently apply to the same scope.",
    ).then(() => {
      setName("");
      setDepartment("");
    });
  }
  async function runPreview() {
    if (!selected || !previewCourse) return;
    setSaving(true);
    setError("");
    try {
      setPreview(
        await request<StandardsPreview>(`/standard-sets/${selected.setId}/versions/${selected.id}/preview`, { courseId: previewCourse }),
      );
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }

  const base = selected ? `/standard-sets/${selected.setId}/versions/${selected.id}` : "";
  const editing = selected?.status === "Draft";
  const dirty =
    !!selected &&
    (notes !== selected.notes || JSON.stringify(checks) !== JSON.stringify(selected.checks) || JSON.stringify(endorsement) !== JSON.stringify(selected.endorsement));
  const byCode = useMemo(() => new Map(catalog.map((c) => [c.code, c])), [catalog]);
  const update = (code: string, change: Partial<StandardCheckSetting>) =>
    setChecks(checks.map((c) => (c.code === code ? { ...c, ...change } : c)));

  return (
    <>
      <span className="eyebrow">CTL REVIEW FRAMEWORK</span>
      <h1>Course standards</h1>
      <p className="intro">
        Standard sets decide which automated checks run, how serious each finding is, and the guidance instructors see. Each course uses the
        most specific published set for its department and delivery mode. Published versions never change, and every review records the
        version it ran under.
      </p>
      {error && (
        <div className="error" role="alert">
          {error}
          <button disabled={saving} onClick={() => setRetry((x) => x + 1)}>
            Refresh standards
          </button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {loading && <p role="status">Loading standards…</p>}
      {!loading && sets.length === 0 && !error && (
        <section className="panel">
          <h2>No standard sets yet</h2>
          <p>Every course uses TeachWell's built-in defaults until you publish a set. Create a college-wide set below to begin.</p>
        </section>
      )}
      <div className="rubric-layout">
        <section className="panel rubric-list" aria-label="Standard sets">
          {sets.map((s) => (
            <article key={s.id}>
              <h2>{s.name}</h2>
              <p className="fine">{s.scope}</p>
              <ul>
                {s.versions.map((v) => (
                  <li key={v.id}>
                    <button
                      className="link-button"
                      aria-current={selected?.id === v.id ? "true" : undefined}
                      disabled={saving}
                      onClick={() => act(() => request<StandardSetVersion>(`/standard-sets/${s.id}/versions/${v.id}`), "")}
                    >
                      Version {v.versionNumber} · {v.status} · {v.enabledChecks} checks on · {v.requiredChecks} required
                    </button>
                  </li>
                ))}
              </ul>
              {s.versions[0]?.status !== "Draft" && (
                <button
                  className="secondary"
                  disabled={saving}
                  onClick={() => act(() => request<StandardSetVersion>(`/standard-sets/${s.id}/versions`, {}), "New draft created from the latest version.")}
                >
                  Create new draft version
                </button>
              )}
            </article>
          ))}
          <form className="new-rubric" onSubmit={create}>
            <h3>New standard set</h3>
            <label>
              Name
              <input required maxLength={160} value={name} onChange={(e) => setName(e.target.value)} />
            </label>
            <label>
              Delivery mode
              <select value={mode} onChange={(e) => setMode(e.target.value)}>
                <option value="">Any mode</option>
                {DELIVERY_MODES.map((m) => (
                  <option key={m.value} value={m.value}>
                    {m.label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Department (optional)
              <input maxLength={120} value={department} onChange={(e) => setDepartment(e.target.value)} placeholder="Any department" />
            </label>
            <p className="fine">Leave both blank for a college-wide set. Only one set can cover each scope.</p>
            <button disabled={saving}>Create standard set</button>
          </form>
        </section>
        <section className="rubric-detail" aria-labelledby="standards-detail-title">
          {!selected ? (
            <section className="panel">
              <SlidersHorizontal />
              <h2 id="standards-detail-title">Choose a version</h2>
              <p>Select a standard-set version to view or edit its check settings and guidance.</p>
            </section>
          ) : (
            <>
              <section className="panel">
                <span className="tag">{selected.status.toUpperCase()}</span>
                <h2 id="standards-detail-title">
                  {selected.setName} · Version {selected.versionNumber}
                </h2>
                <p className="fine">
                  {selected.scope} ·{" "}
                  {selected.publishedAt
                    ? `Published ${new Date(selected.publishedAt).toLocaleString()} by ${selected.publishedByName ?? "unknown"}. This version can no longer change.`
                    : "Draft. Save your changes, preview them against a course, then publish."}
                </p>
                <fieldset className="standards-endorsement" disabled={!editing}>
                  <legend>Reviews an endorsement needs</legend>
                  <p className="fine">
                    Courses requesting a readiness review under this version can be endorsed only once these reviews are published after the
                    request. The reviewer who decides must always have published at least one.
                  </p>
                  <label className="checkbox-row">
                    <input
                      type="checkbox"
                      checked={endorsement.courseReview}
                      onChange={(e) => setEndorsement({ ...endorsement, courseReview: e.target.checked })}
                    />{" "}
                    A course-level review
                  </label>
                  <label>
                    Item reviews (pages, assignments, quizzes or discussions)
                    <input
                      type="number"
                      min={0}
                      max={20}
                      value={endorsement.minimumItemReviews}
                      onChange={(e) => setEndorsement({ ...endorsement, minimumItemReviews: Math.max(0, Math.min(20, Number(e.target.value) || 0)) })}
                    />
                  </label>
                  <label>
                    Using rubric
                    <select
                      value={endorsement.rubricProfileId ?? ""}
                      onChange={(e) => setEndorsement({ ...endorsement, rubricProfileId: e.target.value || null })}
                    >
                      <option value="">Any rubric</option>
                      {rubrics.map((r) => (
                        <option key={r.id} value={r.id}>
                          {r.name}
                        </option>
                      ))}
                    </select>
                  </label>
                </fieldset>
                <label className="standards-notes">
                  What changed in this version
                  <textarea maxLength={2000} rows={2} disabled={!editing} value={notes} onChange={(e) => setNotes(e.target.value)} />
                </label>
                {editing && (
                  <div className="finding-actions">
                    <button
                      disabled={saving || !dirty}
                      onClick={() => act(() => request<StandardSetVersion>(base, { revision: selected.revision, notes, checks, endorsement }, "PUT"), "Draft saved.")}
                    >
                      Save draft
                    </button>
                    <button
                      className="secondary"
                      disabled={saving || dirty}
                      title={dirty ? "Save your changes before publishing." : undefined}
                      onClick={() => {
                        if (window.confirm("Publishing freezes this version permanently and applies it to new reviews of matching courses. Publish now?"))
                          act(() => request<StandardSetVersion>(base + "/publish", { revision: selected.revision }), "Version published. New reviews of matching courses use it.");
                      }}
                    >
                      Publish version
                    </button>
                    {dirty && <span className="fine">Unsaved changes</span>}
                  </div>
                )}
              </section>

              <section className="panel standards-preview" aria-labelledby="preview-title">
                <h3 id="preview-title">Preview against a course</h3>
                <p className="fine">
                  Runs this version's saved settings against a course's current content and compares the findings with the standards that course uses
                  today. Nothing is saved.
                </p>
                {courses.length === 0 ? (
                  <p className="sharing-empty">No courses are shared with you yet, so there is nothing to preview against.</p>
                ) : (
                  <div className="form-row">
                    <label>
                      Course
                      <select value={previewCourse} onChange={(e) => setPreviewCourse(e.target.value)}>
                        {courses.map((c) => (
                          <option key={c.id} value={c.id}>
                            {c.code} · {c.title}
                          </option>
                        ))}
                      </select>
                    </label>
                    <button className="secondary" disabled={saving || !previewCourse} onClick={runPreview}>
                      Run preview
                    </button>
                  </div>
                )}
                {dirty && preview === null && <p className="fine">The preview uses the last saved draft, not unsaved edits.</p>}
                {preview && <PreviewTable preview={preview} />}
              </section>

              {ASPECTS.map((aspect) => {
                const inAspect = checks.filter((c) => byCode.get(c.code)?.aspect === aspect.value);
                if (inAspect.length === 0) return null;
                return (
                  <section className="panel standards-aspect" key={aspect.value} aria-label={aspect.label}>
                    <h3>{aspect.label}</h3>
                    {inAspect.map((c) => (
                      <CheckEditor key={c.code} setting={c} entry={byCode.get(c.code)!} editing={editing} onChange={(change) => update(c.code, change)} />
                    ))}
                  </section>
                );
              })}
            </>
          )}
        </section>
      </div>
      <GuideResources onSessionExpired={onSessionExpired} />
      <GuideFeedback onSessionExpired={onSessionExpired} />
    </>
  );
}

function CheckEditor({
  setting: c,
  entry,
  editing,
  onChange,
}: {
  setting: StandardCheckSetting;
  entry: CheckCatalogEntry;
  editing: boolean;
  onChange: (change: Partial<StandardCheckSetting>) => void;
}) {
  const id = "check-" + c.code;
  return (
    <details className={"check-setting" + (c.enabled ? "" : " check-off")}>
      <summary>
        <span className="check-name">{entry.name}</span>
        <span className={"tag severity-" + c.severity.toLowerCase()}>{c.enabled ? c.severity.toUpperCase() : "OFF"}</span>
        {c.required && <span className="tag">REQUIRED</span>}
        <span className="fine">
          {c.code} · {entry.target === "Course" ? "course structure" : "pages and assignments"}
        </span>
      </summary>
      <fieldset disabled={!editing}>
        <legend className="visually-hidden">Settings for {entry.name}</legend>
        <div className="checks">
          <label>
            <input type="checkbox" checked={c.enabled} onChange={(e) => onChange({ enabled: e.target.checked, required: e.target.checked && c.required })} /> Run this
            check
          </label>
          <label>
            <input type="checkbox" checked={c.required} disabled={!c.enabled} onChange={(e) => onChange({ required: e.target.checked })} /> Required for
            readiness
          </label>
          <label>
            Severity{" "}
            <select value={c.severity} onChange={(e) => onChange({ severity: e.target.value as StandardCheckSetting["severity"] })}>
              {PRIORITIES.map((p) => (
                <option key={p}>{p}</option>
              ))}
            </select>
          </label>
        </div>
        {entry.parameters.map((p) =>
          p.kind === "WordList" ? (
            <label key={p.key}>
              {p.label} <span className="fine">(one per line, up to {p.maxItems})</span>
              <textarea
                id={`${id}-${p.key}`}
                rows={4}
                value={(c.lists[p.key] ?? []).join("\n")}
                onChange={(e) => onChange({ lists: { ...c.lists, [p.key]: e.target.value.split("\n") } })}
                onBlur={(e) => onChange({ lists: { ...c.lists, [p.key]: e.target.value.split("\n").map((w) => w.trim()).filter(Boolean) } })}
              />
            </label>
          ) : (
            <label key={p.key}>
              {p.label}{" "}
              <span className="fine">
                ({p.min}–{p.max})
              </span>
              <input
                type="number"
                step="any"
                min={p.min ?? undefined}
                max={p.max ?? undefined}
                value={c.numbers[p.key] ?? ""}
                onChange={(e) => onChange({ numbers: { ...c.numbers, [p.key]: Number(e.target.value) } })}
              />
            </label>
          ),
        )}
        <label>
          Why it matters <span className="fine">(shown to instructors)</span>
          <textarea maxLength={2000} rows={3} value={c.guidance.why} onChange={(e) => onChange({ guidance: { ...c.guidance, why: e.target.value } })} />
        </label>
        <label>
          How to fix it
          <textarea maxLength={4000} rows={3} value={c.guidance.howToFix} onChange={(e) => onChange({ guidance: { ...c.guidance, howToFix: e.target.value } })} />
        </label>
        <label>
          Example
          <textarea maxLength={2000} rows={2} value={c.guidance.example} onChange={(e) => onChange({ guidance: { ...c.guidance, example: e.target.value } })} />
        </label>
        {editing && (
          <button type="button" className="link-button" onClick={() => onChange({ guidance: entry.defaultGuidance })}>
            Restore TeachWell's default guidance
          </button>
        )}
      </fieldset>
    </details>
  );
}

function PreviewTable({ preview }: { preview: StandardsPreview }) {
  const changed = preview.rows.filter((r) => r.current !== r.proposed);
  return (
    <div className="preview-result" role="status">
      <p>
        <strong>{preview.courseTitle}</strong>: {preview.elementsChecked} pages and assignments plus the course structure. Today it uses{" "}
        {preview.current.name}
        {preview.current.versionNumber ? ` v${preview.current.versionNumber}` : ""}: <strong>{preview.currentTotal}</strong> findings. This version:{" "}
        <strong>{preview.proposedTotal}</strong>.
      </p>
      {changed.length === 0 ? (
        <p className="fine">No check would report a different number of findings for this course.</p>
      ) : (
        <table className="preview-table">
          <caption className="visually-hidden">Findings per check, today and under this version</caption>
          <thead>
            <tr>
              <th scope="col">Check</th>
              <th scope="col">Today</th>
              <th scope="col">This version</th>
            </tr>
          </thead>
          <tbody>
            {changed.map((r) => (
              <tr key={r.code}>
                <th scope="row">
                  {r.name}
                  {!r.enabled && <span className="fine"> (off)</span>}
                </th>
                <td>{r.current}</td>
                <td>{r.proposed}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {preview.limitations.length > 0 && (
        <ul className="check-limitations" aria-label="Preview limitations">
          {preview.limitations.map((l) => (
            <li key={l}>{l}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
