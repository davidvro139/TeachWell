import { useEffect, useState, type FormEvent } from "react";
import { ClipboardCheck, ClipboardList, ScanSearch } from "lucide-react";
import {
  ApiError,
  PRIORITIES,
  RATING_LABELS,
  request,
  type CheckGuidance,
  type CourseOutline,
  type Criterion,
  type Evaluation,
  type OutlineItem,
  type Preview,
  type Finding,
  type Outcome,
  type RevisionTask,
  type Review,
  type Severity,
  isAssessment,
} from "./api";
import "./Reviews.css";
import "./Standards.css";
import { Glossed } from "./Glossary";

interface Props {
  reviewId: string;
  reviewerView: boolean;
  onBack: () => void;
  onSessionExpired: (error: unknown) => void;
  onTaskCreated: (taskId: string) => void;
}

const formatTime = (value: string) => new Date(value).toLocaleString();
const percent = (value: number | null) => (value === null ? "Not scored" : `${value}%`);

export function ReviewWorkspace({ reviewId, reviewerView, onBack, onSessionExpired, onTaskCreated }: Props) {
  const [review, setReview] = useState<Review | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [stale, setStale] = useState(false);
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [summary, setSummary] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else {
      setStale(e instanceof ApiError && e.status === 409);
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    setStale(false);
    request<Review>(`/reviews/${reviewId}`)
      .then((data) => {
        if (active) {
          setReview(data);
          setSummary(data.summary);
        }
      })
      .catch((e) => {
        if (active) {
          if (e instanceof ApiError && e.status === 404)
            setError("This review is not available. It may be a draft you do not own, or course access may have been removed.");
          else fail(e);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [reviewId, retry]);

  // Every mutation returns the full review with a new revision, so later edits never overwrite silently.
  async function mutate(path: string, body: Record<string, unknown>, method = "POST", success = "") {
    if (!review) return false;
    setSaving(true);
    setError("");
    setNotice("");
    try {
      const next = await request<Review>(path, { ...body, revision: review.revision }, method);
      setReview(next);
      setNotice(success);
      return true;
    } catch (e) {
      fail(e);
      return false;
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <p role="status">Loading review…</p>;
  if (!review)
    return (
      <section className="panel">
        <button className="back" onClick={onBack}>
          ← Back to course
        </button>
        <div className="error" role="alert">
          {error || "Review unavailable."}
          <button onClick={() => setRetry((x) => x + 1)}>Try again</button>
        </div>
      </section>
    );

  const proposed = review.findings.filter((f) => f.status === "Proposed").length;
  const decided = review.evaluations.some((e) => e.outcome !== "NotReviewed");
  const criteria = review.rubric.criteria;
  const categories = [...new Set(criteria.map((c) => c.category))];
  const code = (id: string | null) => criteria.find((c) => c.id === id)?.code;

  return (
    <div className="review" aria-busy={saving}>
      <button className="back" onClick={onBack}>
        ← Back to course
      </button>
      <span className="eyebrow">
        {review.courseTitle.toUpperCase()} · {review.elementType.toUpperCase()} REVIEW ·{" "}
        {review.status === "Draft" ? "DRAFT (VISIBLE ONLY TO YOU)" : "PUBLISHED"}
      </span>
      <h1>{review.elementTitle}</h1>
      <p className="intro">
        {review.rubric.profileName} v{review.rubric.versionNumber} · Reviewer {review.reviewerName} · Started{" "}
        {formatTime(review.startedAt)}
        {review.publishedAt && ` · Published ${formatTime(review.publishedAt)}`}
      </p>
      {review.standards && (
        <p className="fine" title={review.standards.reason}>
          Automated checks use {review.standards.name}
          {review.standards.versionNumber ? ` v${review.standards.versionNumber}` : ""} ({review.standards.scope}), pinned when this review started.
        </p>
      )}
      {!review.isLatestSnapshot && (
        <p className="connection-warning" role="note">
          Newer content has been captured since this review began. This review stays pinned to the snapshot below; start a new review to
          evaluate the latest content.
        </p>
      )}
      {error && (
        <div className="error" role="alert">
          {error}
          {stale && <button onClick={() => setRetry((x) => x + 1)}>Reload review</button>}
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}

      <section className="score-panel" aria-labelledby="score-title">
        <h2 id="score-title">Rubric summary</h2>
        <dl className="score-grid">
          <div>
            <dt>Coverage</dt>
            <dd>{review.score.coveragePercent}%</dd>
          </div>
          <div>
            <dt>Weighted score (rated criteria only)</dt>
            <dd>{percent(review.score.percent)}</dd>
          </div>
          <div>
            <dt>Rated · N/A · Not reviewed</dt>
            <dd>
              {review.score.rated} · {review.score.notApplicable} · {review.score.notReviewed}
            </dd>
          </div>
          <div>
            <dt>Required criteria below expectations</dt>
            <dd>{review.score.requiredBelowExpectations}</dd>
          </div>
        </dl>
        {review.score.requiredNotReviewed > 0 && (
          <p className="fine">{review.score.requiredNotReviewed} required criteria have not been reviewed. They are not counted as zero.</p>
        )}
        <ul className="category-scores">
          {review.score.categories.map((c) => (
            <li key={c.category}>
              <span>{c.category}</span>
              <span>
                {percent(c.percent)} · {c.rated + c.notApplicable}/{c.applicable} decided
              </span>
            </li>
          ))}
        </ul>
        <p className="fine">
          Scores describe this snapshot against this rubric version. They are not a compliance, accreditation or accessibility
          certification.
        </p>
      </section>

      <section className="panel snapshot-panel" aria-labelledby="snapshot-title">
        <h2 id="snapshot-title">{review.outline ? "Reviewed course structure" : "Reviewed snapshot"}</h2>
        <p className="fine">
          Captured {formatTime(review.snapshot.fetchedAt)} · Status {review.snapshot.sourceStatus} · Content hash{" "}
          <code>{review.snapshot.contentHash.slice(0, 12)}</code>
        </p>
        {review.snapshot.sourceUrl && (
          <a className="source-link" href={review.snapshot.sourceUrl} target="_blank" rel="noopener noreferrer">
            Open in Canvas ↗
          </a>
        )}
        {review.outline ? (
          <CourseOutlinePanel reviewId={review.id} outline={review.outline} onError={fail} />
        ) : (
          <>
            <p className="fine">
              {review.snapshot.hasStructuralEvidence
                ? `Structural evidence: ${review.snapshot.images} images, ${review.snapshot.headings} headings, ${review.snapshot.links} links, ${review.snapshot.tables} tables.`
                : "No structural evidence was captured for this snapshot."}
            </p>
            <SnapshotView plainText={review.snapshot.plainText} previewHtml={review.snapshot.previewHtml ?? null} />
          </>
        )}
      </section>

      <section className="panel" aria-labelledby="findings-title">
        <div className="review-heading">
          <ScanSearch size={22} />
          <div>
            <h2 id="findings-title">Findings</h2>
            <p>
              Automated checks only propose findings from captured evidence. A reviewer confirms or rejects each proposal; only confirmed
              findings are shared.
            </p>
          </div>
        </div>
        {review.canEdit && (
          <button
            disabled={saving}
            onClick={() =>
              mutate(`/reviews/${review.id}/audit`, {}, "POST", "Automated checks ran against the pinned snapshot.")
            }
          >
            {review.checksRunAt ? "Re-run automated checks" : "Run automated checks"}
          </button>
        )}
        {review.checksRunAt && <p className="fine">Checks last run {formatTime(review.checksRunAt)}.</p>}
        {review.checkLimitations.length > 0 && (
          <ul className="check-limitations" aria-label="Check limitations">
            {review.checkLimitations.map((l) => (
              <li key={l}>{l}</li>
            ))}
          </ul>
        )}
        {review.findings.length === 0 ? (
          <p className="sharing-empty">No findings recorded{review.canEdit ? " yet" : ""}.</p>
        ) : (
          <ul className="findings">
            {review.findings.map((f) => (
              <FindingItem
                key={f.id}
                finding={f}
                criterion={code(f.criterionId)}
                guidance={f.checkCode ? review.guidance?.[f.checkCode] : undefined}
                sourceUrl={review.snapshot.sourceUrl}
                canEdit={review.canEdit}
                saving={saving}
                onStatus={(status) =>
                  mutate(`/findings/${f.id}`, { status }, "PATCH", status === "Confirmed" ? "Finding confirmed." : "Finding rejected.")
                }
              />
            ))}
          </ul>
        )}
        {review.canEdit && (
          <ManualFinding
            criteria={criteria}
            saving={saving}
            onAdd={(body) => mutate(`/reviews/${review.id}/findings`, body, "POST", "Finding added.")}
          />
        )}
        {reviewerView && review.status === "Published" && review.findings.some((f) => f.status === "Confirmed") && (
          <CreateTaskForm review={review} onCreated={onTaskCreated} onSessionExpired={onSessionExpired} />
        )}
      </section>

      <section aria-labelledby="criteria-title">
        <h2 id="criteria-title" className="criteria-title">
          Criteria
        </h2>
        {categories.map((category) => (
          <section className="panel criteria-group" key={category}>
            <h3>{category}</h3>
            {criteria
              .filter((c) => c.category === category)
              .map((c) => (
                <CriterionEditor
                  key={c.id}
                  criterion={c}
                  evaluation={review.evaluations.find((e) => e.criterionId === c.id)!}
                  canEdit={review.canEdit}
                  saving={saving}
                  onSave={(body) => mutate(`/reviews/${review.id}/criteria/${c.id}`, body, "PUT", `${c.code} saved.`)}
                />
              ))}
          </section>
        ))}
      </section>

      <section className="panel publish-panel" aria-labelledby="publish-title">
        <div className="review-heading">
          <ClipboardCheck size={22} />
          <div>
            <h2 id="publish-title">{review.status === "Published" ? "Published summary" : "Publish review"}</h2>
            <p>
              {review.status === "Published"
                ? "This review is frozen. Later Canvas edits are evaluated in a new review."
                : "Publishing freezes ratings, comments and findings and shares confirmed findings with the course owner."}
            </p>
          </div>
        </div>
        {review.canEdit ? (
          <>
            <label>
              Summary for the instructor (optional)
              <textarea maxLength={4000} rows={3} value={summary} onChange={(e) => setSummary(e.target.value)} />
            </label>
            {proposed > 0 && <p className="fine">Resolve {proposed} proposed findings before publishing.</p>}
            {!decided && <p className="fine">Rate at least one criterion, or mark it N/A, before publishing.</p>}
            <button
              disabled={saving || proposed > 0 || !decided}
              onClick={() =>
                mutate(`/reviews/${review.id}/publish`, { summary }, "POST", "Review published. Confirmed findings are now visible to the course owner.")
              }
            >
              Publish review
            </button>
          </>
        ) : (
          <p>{review.summary || "No summary provided."}</p>
        )}
      </section>
    </div>
  );
}

// The preview is markup the server rebuilt from an allowlist (structural tags only, encoded text, no source attributes, URLs or
// images), so it can be rendered as HTML; the page's Content-Security-Policy additionally blocks any script or inline style.
function PagePreview({ html }: { html: string }) {
  return <div className="page-preview" dangerouslySetInnerHTML={{ __html: html }} />;
}

function SnapshotView({ plainText, previewHtml }: { plainText: string; previewHtml: string | null }) {
  const [mode, setMode] = useState<"preview" | "text">(previewHtml ? "preview" : "text");
  return (
    <>
      {previewHtml && (
        <div className="view-toggle" role="group" aria-label="Snapshot view">
          <button type="button" className="link-button" aria-pressed={mode === "preview"} onClick={() => setMode("preview")}>
            Visual preview
          </button>
          <button type="button" className="link-button" aria-pressed={mode === "text"} onClick={() => setMode("text")}>
            Plain text
          </button>
        </div>
      )}
      {mode === "preview" && previewHtml ? (
        <>
          <PagePreview html={previewHtml} />
          <p className="fine">
            Structure and emphasis as captured. Canvas themes, colors, embedded media and scripts are not reproduced; images appear as
            their text alternative, and links as labeled text.
          </p>
        </>
      ) : (
        <div className="snapshot-text">{plainText || "No text content."}</div>
      )}
      {!previewHtml && <p className="fine">No picture of this page was saved with the version that was reviewed. Bring the course in again to save one.</p>}
    </>
  );
}

function CourseOutlinePanel({ reviewId, outline, onError }: { reviewId: string; outline: CourseOutline; onError: (e: unknown) => void }) {
  const [open, setOpen] = useState<string | null>(null);
  const [previews, setPreviews] = useState<Record<string, Preview>>({});
  async function toggle(snapshotId: string) {
    if (open === snapshotId) return setOpen(null);
    setOpen(snapshotId);
    if (previews[snapshotId]) return;
    try {
      const preview = await request<Preview>(`/reviews/${reviewId}/previews/${snapshotId}`);
      setPreviews((p) => ({ ...p, [snapshotId]: preview }));
    } catch (e) {
      setOpen(null);
      onError(e);
    }
  }
  function item(i: OutlineItem) {
    const notes = [
      i.status !== "Published" && i.status,
      isAssessment(i.type) && i.hasDueDate === false && "No due date",
      i.type === "assignment" && i.hasRubric === false && "No rubric",
      isAssessment(i.type) && !i.aligned && "Not aligned",
    ].filter(Boolean) as string[];
    const preview = i.snapshotId ? previews[i.snapshotId] : undefined;
    return (
      <li key={i.elementId}>
        <div className="outline-row">
          <span className="tag">{i.type === "moduleItem" ? "ITEM" : i.type.toUpperCase()}</span>
          <span>{i.title}</span>
          {notes.map((n) => (
            <span key={n} className="tag outline-flag">
              {n.toUpperCase()}
            </span>
          ))}
          <span className="outline-actions">
            {i.snapshotId && (i.type === "page" || isAssessment(i.type)) && (
              <button type="button" className="link-button" aria-expanded={open === i.snapshotId} onClick={() => toggle(i.snapshotId!)}>
                {open === i.snapshotId ? "Hide preview" : "Preview"}
              </button>
            )}
            {i.sourceUrl && (
              <a className="source-link" href={i.sourceUrl} target="_blank" rel="noopener noreferrer">
                Canvas ↗
              </a>
            )}
          </span>
        </div>
        {open === i.snapshotId &&
          (preview ? (
            preview.previewHtml ? (
              <PagePreview html={preview.previewHtml} />
            ) : (
              <p className="fine">No picture of this item was saved with the version that was reviewed.</p>
            )
          ) : (
            <p role="status">Loading preview…</p>
          ))}
      </li>
    );
  }
  return (
    <div className="course-outline">
      <p className="fine">
        {outline.modules.length} modules · {outline.competencies} course competencies. Open page previews side by side with the rubric to
        judge visual and navigational consistency; each preview is the snapshot captured with this structure.
      </p>
      {outline.modules.length === 0 && <p className="sharing-empty">This course has no modules.</p>}
      {outline.modules.map((m) => (
        <div key={m.elementId} className="outline-module">
          <h3>
            {m.title} {m.status !== "Published" && <span className="tag outline-flag">{m.status.toUpperCase()}</span>}
            {m.sourceUrl && (
              <a className="source-link" href={m.sourceUrl} target="_blank" rel="noopener noreferrer">
                Canvas ↗
              </a>
            )}
          </h3>
          {m.items.length === 0 ? <p className="fine">Empty module.</p> : <ul>{m.items.map(item)}</ul>}
        </div>
      ))}
      {outline.unplaced.length > 0 && (
        <div className="outline-module">
          <h3>Not placed in any module</h3>
          <ul>{outline.unplaced.map(item)}</ul>
        </div>
      )}
    </div>
  );
}

function FindingItem({
  finding,
  criterion,
  guidance,
  sourceUrl,
  canEdit,
  saving,
  onStatus,
}: {
  finding: Finding;
  criterion?: string;
  guidance?: CheckGuidance;
  sourceUrl?: string | null;
  canEdit: boolean;
  saving: boolean;
  onStatus: (status: "Confirmed" | "Rejected") => void;
}) {
  return (
    <li className={"finding finding-" + finding.status.toLowerCase()}>
      <div className="finding-tags">
        <span className={"tag severity-" + finding.severity.toLowerCase()}>{finding.severity.toUpperCase()}</span>
        <span className="tag">{finding.status.toUpperCase()}</span>
        <span className="tag">{finding.source === "Automated" ? `CHECK ${finding.checkCode}` : "REVIEWER"}</span>
        {criterion && <span className="tag">{criterion}</span>}
      </div>
      <h4>{finding.title}</h4>
      {finding.description && <p>{finding.description}</p>}
      <p>
        <strong>Evidence:</strong> {finding.evidence}
      </p>
      {finding.recommendation && (
        <p>
          <strong>Recommendation:</strong> <Glossed text={finding.recommendation} />
        </p>
      )}
      {finding.subjectTitle && (
        <p>
          <strong>Concerns:</strong> {finding.subjectTitle}
        </p>
      )}
      {guidance && (
        <details className="finding-guidance">
          <summary>Why this matters and how to fix it</summary>
          <p>{guidance.why}</p>
          <p>
            <strong>How to fix it:</strong> {guidance.howToFix}
          </p>
          {guidance.example && (
            <p>
              <strong>Example:</strong> {guidance.example}
            </p>
          )}
        </details>
      )}
      {(finding.subjectSourceUrl ?? sourceUrl) && (
        <a className="source-link" href={(finding.subjectSourceUrl ?? sourceUrl)!} target="_blank" rel="noopener noreferrer">
          Open {finding.subjectSourceUrl ? `“${finding.subjectTitle}”` : ""} in Canvas to fix ↗
        </a>
      )}
      {canEdit && (
        <div className="finding-actions">
          {finding.status !== "Confirmed" && (
            <button disabled={saving} onClick={() => onStatus("Confirmed")} aria-label={`Confirm finding: ${finding.title}`}>
              Confirm
            </button>
          )}
          {finding.status !== "Rejected" && (
            <button className="secondary" disabled={saving} onClick={() => onStatus("Rejected")} aria-label={`Reject finding: ${finding.title}`}>
              Reject
            </button>
          )}
        </div>
      )}
    </li>
  );
}

function ManualFinding({
  criteria,
  saving,
  onAdd,
}: {
  criteria: Criterion[];
  saving: boolean;
  onAdd: (body: Record<string, unknown>) => Promise<boolean>;
}) {
  const empty = { title: "", severity: "Medium" as Severity, criterionId: "", evidence: "", recommendation: "", description: "" };
  const [form, setForm] = useState(empty);
  const set = (key: keyof typeof empty) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });
  return (
    <details className="manual-finding">
      <summary>Add a reviewer finding</summary>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          if (await onAdd({ ...form, criterionId: form.criterionId || null })) setForm(empty);
        }}
      >
        <label>
          Title
          <input required maxLength={200} value={form.title} onChange={set("title")} />
        </label>
        <div className="form-row">
          <label>
            Severity
            <select value={form.severity} onChange={set("severity")}>
              {["Low", "Medium", "High", "Critical"].map((s) => (
                <option key={s}>{s}</option>
              ))}
            </select>
          </label>
          <label>
            Criterion
            <select value={form.criterionId} onChange={set("criterionId")}>
              <option value="">Not linked</option>
              {criteria.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.code} · {c.title}
                </option>
              ))}
            </select>
          </label>
        </div>
        <label>
          Evidence (required)
          <textarea required maxLength={4000} rows={2} value={form.evidence} onChange={set("evidence")} />
        </label>
        <label>
          Recommendation
          <textarea maxLength={4000} rows={2} value={form.recommendation} onChange={set("recommendation")} />
        </label>
        <button disabled={saving}>Add finding</button>
      </form>
    </details>
  );
}

function CriterionEditor({
  criterion,
  evaluation,
  canEdit,
  saving,
  onSave,
}: {
  criterion: Criterion;
  evaluation: Evaluation;
  canEdit: boolean;
  saving: boolean;
  onSave: (body: Record<string, unknown>) => Promise<boolean>;
}) {
  const choice = (e: Evaluation) => (e.outcome === "Rated" ? String(e.rating) : e.outcome);
  const [value, setValue] = useState(choice(evaluation));
  const [comment, setComment] = useState(evaluation.comment);
  const [evidence, setEvidence] = useState(evaluation.evidence);
  const dirty = value !== choice(evaluation) || comment !== evaluation.comment || evidence !== evaluation.evidence;
  const options: [string, string][] = [
    ...RATING_LABELS.map((label, i): [string, string] => [String(i), `${i} · ${label}`]),
    ["NotApplicable", "N/A · Not applicable"],
    ["NotReviewed", "Not reviewed"],
  ];
  const name = `criterion-${criterion.id}`;
  function save() {
    const rated = /^[0-3]$/.test(value);
    onSave({ outcome: (rated ? "Rated" : value) as Outcome, rating: rated ? Number(value) : null, comment, evidence });
  }
  return (
    <article className="criterion">
      <div className="finding-tags">
        <span className="tag">{criterion.code}</span>
        {criterion.required && <span className="tag">REQUIRED</span>}
        <span className="tag">WEIGHT {criterion.weight}</span>
        <span className="tag">{criterion.reviewMethod.toUpperCase()}</span>
      </div>
      <h4>{criterion.title}</h4>
      {criterion.evidenceGuidance && <p className="fine">{criterion.evidenceGuidance}</p>}
      {canEdit ? (
        <>
          <fieldset>
            <legend>Rating for {criterion.code}</legend>
            {options.map(([key, label]) => (
              <label key={key} className="rating-option">
                <input type="radio" name={name} value={key} checked={value === key} onChange={() => setValue(key)} />
                <span>
                  {label}
                  {/^[0-3]$/.test(key) && <small>{criterion.ratingAnchors[Number(key)]}</small>}
                </span>
              </label>
            ))}
          </fieldset>
          <div className="form-row">
            <label>
              Comment
              <textarea maxLength={4000} rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
            </label>
            <label>
              Evidence
              <textarea maxLength={4000} rows={2} value={evidence} onChange={(e) => setEvidence(e.target.value)} />
            </label>
          </div>
          <button disabled={saving || !dirty} onClick={save}>
            {dirty ? `Save ${criterion.code}` : "Saved"}
          </button>
        </>
      ) : (
        <>
          <p className="rating-result">
            <strong>
              {evaluation.outcome === "Rated"
                ? `${evaluation.rating} · ${RATING_LABELS[evaluation.rating!]}`
                : evaluation.outcome === "NotApplicable"
                  ? "Not applicable"
                  : "Not reviewed"}
            </strong>
            {evaluation.outcome === "Rated" && <span> — {criterion.ratingAnchors[evaluation.rating!]}</span>}
          </p>
          {evaluation.comment && <p>{evaluation.comment}</p>}
          {evaluation.evidence && (
            <p>
              <strong>Evidence:</strong> {evaluation.evidence}
            </p>
          )}
        </>
      )}
    </article>
  );
}

function CreateTaskForm({
  review,
  onCreated,
  onSessionExpired,
}: {
  review: Review;
  onCreated: (taskId: string) => void;
  onSessionExpired: (error: unknown) => void;
}) {
  const confirmed = review.findings.filter((f) => f.status === "Confirmed");
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [title, setTitle] = useState("");
  const [instructions, setInstructions] = useState("");
  const [priority, setPriority] = useState<Severity>("Medium");
  const [dueAt, setDueAt] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  function toggle(id: string) {
    setSelected((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));
  }
  async function submit(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError("");
    try {
      const task = await request<RevisionTask>("/revision-tasks", {
        courseId: review.courseId,
        title,
        instructions,
        priority,
        // The date input is timezone-naive local wall-clock; convert to a UTC instant before sending (timestamps are stored in UTC).
        dueAt: dueAt ? new Date(dueAt).toISOString() : null,
        findingIds: selected,
      });
      onCreated(task.id);
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
      else setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  if (!open)
    return (
      <button className="secondary" onClick={() => setOpen(true)}>
        <ClipboardList size={16} /> Create a revision task from confirmed findings
      </button>
    );
  return (
    <section className="create-task-panel" aria-labelledby="create-task-title">
      <h3 id="create-task-title">New revision task</h3>
      <p className="fine">
        The task starts as a draft visible only to you until you assign it. It is automatically assigned to this course's owner.
      </p>
      {error && (
        <div className="error" role="alert">
          {error}
        </div>
      )}
      <form onSubmit={submit}>
        <fieldset>
          <legend>Choose confirmed findings to include</legend>
          <ul className="finding-checklist">
            {confirmed.map((f) => (
              <li key={f.id}>
                <input
                  type="checkbox"
                  id={"task-finding-" + f.id}
                  checked={selected.includes(f.id)}
                  onChange={() => toggle(f.id)}
                />
                <label htmlFor={"task-finding-" + f.id}>{f.title}</label>
              </li>
            ))}
          </ul>
        </fieldset>
        <label>
          Title
          <input required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <div className="form-row">
          <label>
            Priority
            <select value={priority} onChange={(e) => setPriority(e.target.value as Severity)}>
              {PRIORITIES.map((p) => (
                <option key={p}>{p}</option>
              ))}
            </select>
          </label>
          <label>
            Due date (optional)
            <input type="date" value={dueAt} onChange={(e) => setDueAt(e.target.value)} />
          </label>
        </div>
        <label>
          Instructions for the instructor
          <textarea maxLength={4000} rows={3} value={instructions} onChange={(e) => setInstructions(e.target.value)} />
        </label>
        <div className="finding-actions">
          <button disabled={saving || selected.length === 0 || !title.trim()}>Create task</button>
          <button type="button" className="secondary" onClick={() => setOpen(false)}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  );
}
