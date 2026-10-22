import { useEffect, useState, type FormEvent } from "react";
import { Layers } from "lucide-react";
import {
  ApiError,
  CATEGORIES,
  CHECK_CODES,
  COURSE_CHECK_CODES,
  RATING_LABELS,
  request,
  type CriterionInput,
  type RubricProfile,
  type RubricVersion,
} from "./api";
import "./Reviews.css";

const DELIVERY_METHODS = ["Any", "Traditional", "Hybrid", "Online", "Competency-based"];
const blankCriterion = (): CriterionInput => ({
  code: "",
  title: "",
  description: "",
  category: CATEGORIES[0],
  elementTypes: ["page", "assignment"],
  reviewMethod: "Manual",
  evidenceGuidance: "",
  ratingAnchors: ["", "", "", ""],
  weight: 1,
  required: false,
  automatedChecks: [],
});

export function Rubrics({ onSessionExpired }: { onSessionExpired: (error: unknown) => void }) {
  const [profiles, setProfiles] = useState<RubricProfile[]>([]);
  const [selected, setSelected] = useState<RubricVersion | null>(null);
  const [draft, setDraft] = useState<CriterionInput[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [name, setName] = useState("");
  const [delivery, setDelivery] = useState("Any");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  function show(version: RubricVersion) {
    setSelected(version);
    setDraft(version.criteria.map(({ id: _, ...rest }) => rest));
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    request<RubricProfile[]>("/rubrics")
      .then((data) => {
        if (active) setProfiles(data);
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

  async function act(work: () => Promise<RubricVersion>, success: string) {
    setSaving(true);
    setError("");
    setNotice("");
    try {
      show(await work());
      setProfiles(await request<RubricProfile[]>("/rubrics"));
      setNotice(success);
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  const open = (profileId: string, versionId: string) =>
    act(() => request<RubricVersion>(`/rubrics/${profileId}/versions/${versionId}`), "");
  function create(e: FormEvent) {
    e.preventDefault();
    act(() => request<RubricVersion>("/rubrics", { name, deliveryMethod: delivery }), "Rubric created with an empty draft version.").then(() =>
      setName(""),
    );
  }
  const base = selected ? `/rubrics/${selected.profileId}/versions/${selected.id}` : "";
  const editing = selected?.status === "Draft";
  const update = (index: number, change: Partial<CriterionInput>) =>
    setDraft(draft.map((c, i) => (i === index ? { ...c, ...change } : c)));

  return (
    <>
      <span className="eyebrow">CTL REVIEW FRAMEWORK</span>
      <h1>Rubric library</h1>
      <p className="intro">
        Rubric versions are immutable once published. Reviews always cite the exact version used, so later edits never rewrite earlier
        assessments.
      </p>
      {error && (
        <div className="error" role="alert">
          {error}
          <button disabled={saving} onClick={() => setRetry((x) => x + 1)}>
            Refresh rubrics
          </button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {loading && <p role="status">Loading rubrics…</p>}
      {!loading && profiles.length === 0 && !error && (
        <section className="panel">
          <h2>No rubrics yet</h2>
          <p>Create a rubric below to begin.</p>
        </section>
      )}
      <div className="rubric-layout">
        <section className="panel rubric-list" aria-label="Rubric profiles">
          {profiles.map((p) => (
            <article key={p.id}>
              <h2>{p.name}</h2>
              <p className="fine">Delivery: {p.deliveryMethod}</p>
              <ul>
                {p.versions.map((v) => (
                  <li key={v.id}>
                    <button
                      className="link-button"
                      aria-current={selected?.id === v.id ? "true" : undefined}
                      disabled={saving}
                      onClick={() => open(p.id, v.id)}
                    >
                      Version {v.versionNumber} · {v.status} · {v.criterionCount} criteria
                    </button>
                  </li>
                ))}
              </ul>
              {p.versions[0]?.status !== "Draft" && (
                <button
                  className="secondary"
                  disabled={saving}
                  onClick={() =>
                    act(() => request<RubricVersion>(`/rubrics/${p.id}/versions`, {}), "New draft created from the latest version.")
                  }
                >
                  Create new draft version
                </button>
              )}
            </article>
          ))}
          <form className="new-rubric" onSubmit={create}>
            <h3>New rubric</h3>
            <label>
              Name
              <input required maxLength={160} value={name} onChange={(e) => setName(e.target.value)} />
            </label>
            <label>
              Delivery method
              <select value={delivery} onChange={(e) => setDelivery(e.target.value)}>
                {DELIVERY_METHODS.map((m) => (
                  <option key={m}>{m}</option>
                ))}
              </select>
            </label>
            <button disabled={saving}>Create rubric</button>
          </form>
        </section>
        <section className="rubric-detail" aria-labelledby="rubric-detail-title">
          {!selected ? (
            <section className="panel">
              <Layers />
              <h2 id="rubric-detail-title">Choose a version</h2>
              <p>Select a rubric version to view its criteria and rating anchors.</p>
            </section>
          ) : (
            <>
              <section className="panel">
                <span className="tag">{selected.status.toUpperCase()}</span>
                <h2 id="rubric-detail-title">
                  {selected.profileName} · Version {selected.versionNumber}
                </h2>
                <p className="fine">
                  {selected.publishedAt
                    ? `Published ${new Date(selected.publishedAt).toLocaleString()} by ${selected.publishedByName ?? "unknown"}. This version can no longer change.`
                    : "Draft. Save changes, then publish to make this version available for reviews."}
                </p>
                {editing && (
                  <div className="finding-actions">
                    <button
                      disabled={saving}
                      onClick={() =>
                        act(() => request<RubricVersion>(base, { revision: selected.revision, criteria: draft }, "PUT"), "Draft saved.")
                      }
                    >
                      Save draft
                    </button>
                    <button
                      className="secondary"
                      disabled={saving}
                      onClick={() => {
                        if (window.confirm("Publishing freezes this version permanently. Unsaved edits are not included. Publish now?"))
                          act(() => request<RubricVersion>(base + "/publish", { revision: selected.revision }), "Version published.");
                      }}
                    >
                      Publish version
                    </button>
                  </div>
                )}
              </section>
              {draft.map((c, i) =>
                editing ? (
                  <CriterionForm
                    key={i}
                    index={i}
                    criterion={c}
                    onChange={(change) => update(i, change)}
                    onRemove={() => setDraft(draft.filter((_, j) => j !== i))}
                  />
                ) : (
                  <article className="panel criterion" key={i}>
                    <div className="finding-tags">
                      <span className="tag">{c.code}</span>
                      {c.required && <span className="tag">REQUIRED</span>}
                      <span className="tag">WEIGHT {c.weight}</span>
                      <span className="tag">{c.reviewMethod.toUpperCase()}</span>
                      <span className="tag">{c.elementTypes.join(" + ").toUpperCase()}</span>
                    </div>
                    <h3>{c.title}</h3>
                    <p className="fine">{c.category}</p>
                    {c.evidenceGuidance && <p>{c.evidenceGuidance}</p>}
                    <ol start={0} className="anchors">
                      {c.ratingAnchors.map((a, n) => (
                        <li key={n}>
                          <strong>{RATING_LABELS[n]}:</strong> {a}
                        </li>
                      ))}
                    </ol>
                    {c.automatedChecks.length > 0 && <p className="fine">Automated checks: {c.automatedChecks.join(", ")}</p>}
                  </article>
                ),
              )}
              {editing && (
                <button className="secondary" onClick={() => setDraft([...draft, blankCriterion()])}>
                  Add criterion
                </button>
              )}
            </>
          )}
        </section>
      </div>
    </>
  );
}

function CriterionForm({
  index,
  criterion: c,
  onChange,
  onRemove,
}: {
  index: number;
  criterion: CriterionInput;
  onChange: (change: Partial<CriterionInput>) => void;
  onRemove: () => void;
}) {
  const toggle = (list: string[], value: string) => (list.includes(value) ? list.filter((x) => x !== value) : [...list, value]);
  return (
    <fieldset className="panel criterion-form">
      <legend>Criterion {index + 1}</legend>
      <div className="form-row">
        <label>
          Code
          <input required maxLength={20} pattern="[A-Z0-9\-]+" value={c.code} onChange={(e) => onChange({ code: e.target.value.toUpperCase() })} />
        </label>
        <label>
          Title
          <input required maxLength={160} value={c.title} onChange={(e) => onChange({ title: e.target.value })} />
        </label>
      </div>
      <div className="form-row">
        <label>
          Category
          <select value={c.category} onChange={(e) => onChange({ category: e.target.value })}>
            {CATEGORIES.map((x) => (
              <option key={x}>{x}</option>
            ))}
          </select>
        </label>
        <label>
          Review method
          <select value={c.reviewMethod} onChange={(e) => onChange({ reviewMethod: e.target.value })}>
            {["Manual", "Automated", "Assisted"].map((x) => (
              <option key={x}>{x}</option>
            ))}
          </select>
        </label>
        <label>
          Weight
          <input type="number" min={1} max={5} value={c.weight} onChange={(e) => onChange({ weight: Number(e.target.value) })} />
        </label>
      </div>
      <div className="checks">
        <span>Applies to</span>
        {["page", "assignment", "course"].map((t) => (
          <label key={t}>
            <input type="checkbox" checked={c.elementTypes.includes(t)} onChange={() => onChange({ elementTypes: toggle(c.elementTypes, t) })} /> {t}
          </label>
        ))}
        <label>
          <input type="checkbox" checked={c.required} onChange={(e) => onChange({ required: e.target.checked })} /> Required
        </label>
      </div>
      <label>
        Evidence guidance
        <textarea maxLength={2000} rows={2} value={c.evidenceGuidance} onChange={(e) => onChange({ evidenceGuidance: e.target.value })} />
      </label>
      {RATING_LABELS.map((label, n) => (
        <label key={n}>
          Anchor {n} · {label}
          <input
            required
            maxLength={500}
            value={c.ratingAnchors[n] ?? ""}
            onChange={(e) => onChange({ ratingAnchors: c.ratingAnchors.map((a, j) => (j === n ? e.target.value : a)) })}
          />
        </label>
      ))}
      {c.reviewMethod !== "Manual" && (
        <div className="checks">
          <span>Deterministic checks</span>
          {CHECK_CODES.filter((code) => (c.elementTypes.includes("course") ? true : !COURSE_CHECK_CODES.includes(code))).map((code) => (
            <label key={code}>
              <input
                type="checkbox"
                checked={c.automatedChecks.includes(code)}
                onChange={() => onChange({ automatedChecks: toggle(c.automatedChecks, code) })}
              />{" "}
              {code}
            </label>
          ))}
        </div>
      )}
      <button type="button" className="secondary" onClick={onRemove}>
        Remove criterion
      </button>
    </fieldset>
  );
}
