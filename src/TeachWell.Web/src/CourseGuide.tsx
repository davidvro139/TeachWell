import { useEffect, useState } from "react";
import { ArrowRight, BookOpen, CalendarDays, Check, ChevronRight, CircleCheck, Compass, ExternalLink, LifeBuoy, UserCheck, Zap } from "lucide-react";
import { ApiError, request, type CourseDetail, type KnowledgeArticle, type Workshop } from "./api";
import { Collapsible } from "./Collapsible";
import { Glossed, Term } from "./Glossary";
import "./Reviews.css";
import "./Guide.css";

interface Guidance {
  why: string;
  howToFix: string;
  example: string;
}
interface Place {
  key: string;
  elementId: string | null;
  where: string;
  module: string | null;
  detail: string;
  evidence: string;
  sourceUrl: string | null;
  status: "Open" | "Done" | "NotApplicable";
  reason: string | null;
  setBy: string | null;
  setAt: string | null;
  stillDetected: boolean;
  confirmedBy: string | null;
  taskId: string | null;
}
interface Step {
  checkCode: string;
  name: string;
  aspect: string;
  severity: string;
  required: boolean;
  quickWin: boolean;
  rank: number;
  minutesEach: number;
  openMinutes: number;
  openPlaces: number;
  totalPlaces: number;
  items: number;
  suggestion: string;
  guidance: Guidance;
  locations: Place[];
  confirmedByReviewer: boolean;
  resources: Resource[];
  wcagCriteria: WcagCriterion[];
}
// The WCAG success criteria this step's check relates to (ADR 018). Empty for checks that relate to none, such as
// workload or alignment, so the guide must not imply every step is an accessibility requirement.
interface WcagCriterion {
  number: string;
  name: string;
  level: string;
  reference: string;
}
interface Resource {
  kind: "Article" | "Workshop";
  id: string;
  title: string;
  slug: string | null;
  startsAt: string | null;
  endsAt: string | null;
  modality: string | null;
  myStatus: string | null;
}
interface ReviewerNote {
  findingId: string;
  reviewId: string;
  where: string;
  title: string;
  description: string;
  recommendation: string;
  severity: string;
  reviewerName: string;
  publishedAt: string | null;
  taskId: string | null;
  taskStatus: string | null;
}
interface Aspect {
  aspect: string;
  label: string;
  why: string;
  openSteps: number;
  requiredSteps: number;
  openPlaces: number;
}
interface Guide {
  courseId: string;
  standards: { name: string; versionNumber: number | null };
  contentAsOf: string | null;
  canMark: boolean;
  checkedItems: number;
  aspects: Aspect[];
  steps: Step[];
  strengths: { checkCode: string; aspect: string; statement: string }[];
  resolvedSinceMarked: number;
  limitations: string[];
  reviewerNotes: ReviewerNote[];
}

const minutes = (m: number) => (m < 60 ? `${m} min` : `${Math.floor(m / 60)} h${m % 60 ? ` ${m % 60} min` : ""}`);
const plural = (n: number, one: string, many = one + "s") => `${n} ${n === 1 ? one : many}`;
// "… is easy to overlook. (WCAG 2.2, 1.1.1.)" → the sentence, plus "WCAG 2.2, 1.1.1" shown separately as its source, so the reason reads as
// plain language and the standards reference stays available (ADR 016). Text with no recognisable reference is left as it is.
const REFERENCE = /\s*\(([^()]*\b(?:WCAG|UDL|Bloom|taxonomy|theory|alignment|Section 508)\b[^()]*)\)\s*$/i;
export function splitReference(text: string): { body: string; reference: string | null } {
  const match = REFERENCE.exec(text);
  if (!match) return { body: text, reference: null };
  return { body: text.slice(0, match.index).trim(), reference: match[1].trim().replace(/\.$/, "") };
}
const PLACE_LIMIT = 10;

/**
 * The instructor improvement guide: TeachWell's own checks of the latest import, ranked into steps with why each matters and how
 * to fix it. It needs no reviewer. The owner records progress (Done, Not applicable with a reason) and can ask the CTL for help.
 */
export function CourseGuide({
  course,
  onSessionExpired,
  onOpenReview,
  onOpenTask,
  onProgress,
}: {
  course: CourseDetail;
  onSessionExpired: (error: unknown) => void;
  onOpenReview: (reviewId: string) => void;
  onOpenTask: (taskId: string) => void;
  /** Called after progress is saved, so sections that depend on it (readiness) can refresh. */
  onProgress?: () => void;
}) {
  const [guide, setGuide] = useState<Guide | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const [aspect, setAspect] = useState<string | null>(null);
  const [openStep, setOpenStep] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    request<Guide>(`/courses/${course.id}/guide`)
      .then((g) => active && setGuide(g))
      .catch((e) => active && fail(e))
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [course.id, retry]);

  async function mark(keys: string[], status: Place["status"], reason: string | undefined, success: string) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      setGuide(await request<Guide>(`/courses/${course.id}/guide/progress`, { keys, status, reason: reason ?? null }, "PUT"));
      setNotice(success);
      onProgress?.();
      return true;
    } catch (e) {
      fail(e);
      return false;
    } finally {
      setBusy(false);
    }
  }

  const open = guide?.steps.filter((s) => s.openPlaces > 0) ?? [];
  const finished = guide?.steps.filter((s) => s.openPlaces === 0) ?? [];
  const shown = aspect ? open.filter((s) => s.aspect === aspect) : open;
  const quickWins = open.filter((s) => s.quickWin).slice(0, 5);
  const selected = guide?.aspects.find((a) => a.aspect === aspect);
  const meta = loading
    ? "Checking…"
    : guide
      ? open.length === 0
        ? "Nothing to fix right now"
        : `${plural(open.length, "step")} to consider${quickWins.length ? ` · ${plural(quickWins.length, "quick win")}` : ""}`
      : undefined;

  return (
    <Collapsible
      id="guide"
      title="Improvement guide"
      icon={<Compass size={18} />}
      meta={meta}
      busy={busy}
      description={
        <Glossed
          skip={["improvement-guide"]}
          text="TeachWell's own checks of your latest import, ranked, with why each one matters and how to fix it. You do not need a reviewer to use it, and nothing here is a grade."
        />
      }
    >
      {loading ? (
        <p role="status">Checking the course…</p>
      ) : !guide ? (
        <div className="sharing-empty">
          <p className="error" role="alert">
            {error || "The guide could not be loaded."}
          </p>
          <button className="secondary" onClick={() => setRetry((x) => x + 1)}>
            Try again
          </button>
        </div>
      ) : (
        <div className="guide">
          <p className="fine">
            Checked {plural(guide.checkedItems, "item")} against {guide.standards.name}
            {guide.standards.versionNumber ? ` (version ${guide.standards.versionNumber})` : ""}
            {guide.contentAsOf ? `, using content imported ${new Date(guide.contentAsOf).toLocaleString()}` : ""}. Steps describe the course, not
            you; fix them in Canvas, then re-import to confirm.
            {!guide.canMark && " Only the course owner records progress."}
          </p>
          {guide.resolvedSinceMarked > 0 && (
            <p className="guide-resolved">
              <CircleCheck size={16} /> {plural(guide.resolvedSinceMarked, "place")} you marked done {guide.resolvedSinceMarked === 1 ? "is" : "are"} no
              longer detected. Nice work.
            </p>
          )}
          {notice && (
            <p className="success" role="status">
              {notice}
            </p>
          )}
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}

          <p className="guide-aspects-hint" id="guide-aspects-hint">
            Select an area to see only its steps and why it matters.
          </p>
          <div className="guide-aspects" role="group" aria-label="Areas of the course" aria-describedby="guide-aspects-hint">
            {guide.aspects.map((a) => (
              <button
                key={a.aspect}
                type="button"
                className={"guide-aspect" + (a.openSteps === 0 ? " is-clear" : "")}
                aria-pressed={aspect === a.aspect}
                onClick={() => setAspect(aspect === a.aspect ? null : a.aspect)}
              >
                <strong>{a.label}</strong>
                <span>{a.openSteps === 0 ? "Nothing to fix" : plural(a.openSteps, "step")}</span>
                {a.requiredSteps > 0 && <span className="tag severity-high">{a.requiredSteps} REQUIRED</span>}
                {/* A visible call to action, so the card reads as something to press; aria-pressed already tells assistive technology. */}
                <span className="guide-aspect-action" aria-hidden="true">
                  {aspect === a.aspect ? (
                    <>
                      <Check size={14} /> Showing these steps
                    </>
                  ) : (
                    <>
                      {a.openSteps === 0 ? "See why it matters" : "Show these steps"} <ChevronRight size={14} />
                    </>
                  )}
                </span>
              </button>
            ))}
          </div>
          {selected && (
            <div className="guide-why">
              <h4>Why {selected.label.toLowerCase()} matters</h4>
              <p>{selected.why}</p>
              <button type="button" className="link-button" onClick={() => setAspect(null)}>
                Show all areas
              </button>
            </div>
          )}

          {!aspect && quickWins.length > 0 && (
            <div className="guide-quick">
              <p>
                <Zap size={16} /> <strong>
                  Start with <Term id="quick-win">quick wins</Term>:
                </strong> {plural(quickWins.length, "step")}, about{" "}
                {minutes(quickWins.reduce((n, s) => n + s.openMinutes, 0))} in total.
              </p>
              <div className="guide-chips">
                {quickWins.map((s) => (
                  <button key={s.checkCode} type="button" className="secondary" onClick={() => setOpenStep(s.checkCode)}>
                    {s.name}
                  </button>
                ))}
              </div>
            </div>
          )}

          {!aspect && guide.reviewerNotes.length > 0 && (
            <div className="guide-reviewer">
              <h4>
                <UserCheck size={16} /> From your CTL reviewer
              </h4>
              <p className="fine">Points a reviewer raised in a published review that the automated checks cannot see. They are tracked through the review and any revision task.</p>
              <ul className="guide-places">
                {guide.reviewerNotes.map((n) => (
                  <li key={n.findingId}>
                    <div>
                      <span className={"tag severity-" + n.severity.toLowerCase()}>{n.severity.toUpperCase()}</span> <strong>{n.title}</strong>
                      <small> · {n.where}</small>
                      {n.description && <p>{n.description}</p>}
                      {n.recommendation && <p>Suggested: {n.recommendation}</p>}
                      <p className="guide-status">
                        {n.reviewerName}
                        {n.publishedAt ? `, ${new Date(n.publishedAt).toLocaleDateString()}` : ""}
                        {n.taskStatus ? ` · revision task ${n.taskStatus}` : ""}
                      </p>
                    </div>
                    <div className="finding-actions">
                      <button type="button" className="secondary" onClick={() => onOpenReview(n.reviewId)}>
                        Open review
                      </button>
                      {n.taskId && (
                        <button type="button" className="secondary" onClick={() => onOpenTask(n.taskId!)}>
                          Open task
                        </button>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {shown.length === 0 ? (
            <p className="sharing-empty">
              {open.length === 0
                ? "TeachWell found nothing to fix with the checks your college uses. A CTL reviewer can still look at what checks cannot judge."
                : `Nothing to fix in ${selected?.label.toLowerCase()}.`}
            </p>
          ) : (
            <ol className="guide-steps">
              {shown.map((s, i) => (
                <StepView
                  key={s.checkCode}
                  step={s}
                  courseId={course.id}
                  expanded={openStep === s.checkCode}
                  onToggle={() => setOpenStep(openStep === s.checkCode ? null : s.checkCode)}
                  onNext={i + 1 < shown.length ? () => setOpenStep(shown[i + 1].checkCode) : undefined}
                  canMark={guide.canMark}
                  busy={busy}
                  mark={mark}
                  onSessionExpired={onSessionExpired}
                  onOpenTask={onOpenTask}
                />
              ))}
            </ol>
          )}

          {finished.length > 0 && (
            <details className="report-details">
              <summary>Done or set aside ({plural(finished.length, "step")})</summary>
              <ul className="guide-finished">
                {finished.map((s) => (
                  <li key={s.checkCode}>
                    <strong>{s.name}</strong>
                    <PlaceList step={s} canMark={guide.canMark} busy={busy} mark={mark} onOpenTask={onOpenTask} />
                  </li>
                ))}
              </ul>
            </details>
          )}

          {guide.strengths.length > 0 && (
            <details className="guide-strengths">
              <summary>
                <CircleCheck size={16} /> What is already working ({guide.strengths.length})
              </summary>
              {guide.aspects
                .filter((a) => guide.strengths.some((s) => s.aspect === a.aspect))
                .map((a) => (
                  <div key={a.aspect}>
                    <h5>{a.label}</h5>
                    <ul>
                      {guide.strengths
                        .filter((s) => s.aspect === a.aspect)
                        .map((s) => (
                          <li key={s.checkCode}>{s.statement}</li>
                        ))}
                    </ul>
                  </div>
                ))}
            </details>
          )}
          {guide.limitations.length > 0 && (
            <details className="report-details">
              <summary>What the checks could not see</summary>
              <ul>
                {guide.limitations.map((l) => (
                  <li key={l}>{l}</li>
                ))}
              </ul>
            </details>
          )}
        </div>
      )}
    </Collapsible>
  );
}

type Mark = (keys: string[], status: Place["status"], reason: string | undefined, success: string) => Promise<boolean>;

function StepView({
  step,
  courseId,
  expanded,
  onToggle,
  onNext,
  canMark,
  busy,
  mark,
  onSessionExpired,
  onOpenTask,
}: {
  step: Step;
  courseId: string;
  expanded: boolean;
  onToggle: () => void;
  onNext?: () => void;
  canMark: boolean;
  busy: boolean;
  mark: Mark;
  onSessionExpired: (error: unknown) => void;
  onOpenTask: (taskId: string) => void;
}) {
  const [form, setForm] = useState<"none" | "notApplicable" | "help">("none");
  const openKeys = step.locations.filter((l) => l.status === "Open").map((l) => l.key);
  const why = splitReference(step.guidance.why);
  const id = `guide-step-${step.checkCode}`;
  return (
    <li className={"guide-step" + (expanded ? " is-open" : "")}>
      <button type="button" className="guide-step-head" aria-expanded={expanded} aria-controls={id} onClick={onToggle}>
        <span className="guide-rank">{step.rank}</span>
        <span className="guide-step-title">
          <strong>{step.name}</strong>
          <small>
            {plural(step.openPlaces, "place")}
            {step.items > 1 ? ` in ${plural(step.items, "item")}` : ""} · about {minutes(step.openMinutes)}
          </small>
        </span>
        <span className="finding-tags">
          {step.required && <span className="tag severity-high">REQUIRED</span>}
          {step.confirmedByReviewer && <span className="tag">CONFIRMED BY CTL</span>}
          {step.quickWin && <span className="tag">QUICK WIN</span>}
          <span className={"tag severity-" + step.severity.toLowerCase()}>{step.severity.toUpperCase()}</span>
        </span>
      </button>
      {expanded && (
        <div className="guide-step-body" id={id}>
          <h4>Why it matters</h4>
          <p>
            <Glossed text={why.body} />
          </p>
          {why.reference && (
            <p className="guide-reference">
              Based on: <Glossed text={why.reference} />
            </p>
          )}
          {/* Only accessibility checks carry criteria (ADR 018); Glossed links WCAG to its plain-language entry.
              Its own class, not guide-reference: that one is the standards reference inside the guidance text
              (ADR 016), and sharing a class made ".guide-reference" ambiguous for anything selecting it. */}
          {step.wcagCriteria.length > 0 && (
            <p className="guide-criteria">
              Relates to: <Glossed text={step.wcagCriteria.map((c) => c.reference).join("; ")} />
            </p>
          )}
          <h4>How to fix it</h4>
          <p>
            <Glossed text={step.guidance.howToFix} />
          </p>
          {step.guidance.example && (
            <>
              <h4>Example</h4>
              <p className="guide-example">
                <Glossed text={step.guidance.example} />
              </p>
            </>
          )}
          {step.resources.length > 0 && <Resources resources={step.resources} onSessionExpired={onSessionExpired} />}
          <h4>Where TeachWell found it</h4>
          <PlaceList step={step} canMark={canMark} busy={busy} mark={mark} onOpenTask={onOpenTask} />

          {form === "notApplicable" ? (
            <ReasonForm
              label={`Why doesn't "${step.name}" apply to the ${plural(openKeys.length, "open place")}?`}
              busy={busy}
              onCancel={() => setForm("none")}
              onSave={async (reason) => {
                if (await mark(openKeys, "NotApplicable", reason, `Set aside "${step.name}".`)) setForm("none");
              }}
            />
          ) : form === "help" ? (
            <HelpForm step={step} courseId={courseId} onClose={() => setForm("none")} onSessionExpired={onSessionExpired} />
          ) : (
            <div className="finding-actions">
              {canMark && openKeys.length > 0 && (
                <>
                  <button type="button" disabled={busy} onClick={() => mark(openKeys, "Done", undefined, `Marked "${step.name}" done. Re-import the course to confirm.`)}>
                    Mark all done
                  </button>
                  <button type="button" className="secondary" disabled={busy} onClick={() => setForm("notApplicable")}>
                    Not applicable…
                  </button>
                </>
              )}
              {canMark && (
                <button type="button" className="secondary" onClick={() => setForm("help")}>
                  <LifeBuoy size={14} /> Ask the CTL for help
                </button>
              )}
              {onNext && (
                <button type="button" className="secondary" onClick={onNext}>
                  Next step <ArrowRight size={14} />
                </button>
              )}
            </div>
          )}
        </div>
      )}
    </li>
  );
}

// CTL resources linked to this check: read an article in place, or register for an upcoming workshop.
function Resources({ resources, onSessionExpired }: { resources: Resource[]; onSessionExpired: (error: unknown) => void }) {
  const [open, setOpen] = useState<string | null>(null);
  const [article, setArticle] = useState<KnowledgeArticle | null>(null);
  const [status, setStatus] = useState<Record<string, string | null>>({});
  const [error, setError] = useState("");
  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  async function read(r: Resource) {
    setError("");
    if (open === r.id) return setOpen(null);
    setOpen(r.id);
    setArticle(null);
    try {
      setArticle(await request<KnowledgeArticle>(`/knowledge/${r.slug}`));
    } catch (e) {
      fail(e);
    }
  }
  async function register(r: Resource) {
    setError("");
    try {
      const w = await request<Workshop>(`/workshops/${r.id}/register`, {});
      setStatus((s) => ({ ...s, [r.id]: w.myStatus ?? null }));
    } catch (e) {
      fail(e);
    }
  }
  return (
    <div className="guide-resources">
      <h4>Learn more from the CTL</h4>
      <ul>
        {resources.map((r) => {
          const mine = r.id in status ? status[r.id] : r.myStatus;
          return (
            <li key={r.id}>
              {r.kind === "Article" ? (
                <>
                  <button type="button" className="link-button" aria-expanded={open === r.id} onClick={() => read(r)}>
                    <BookOpen size={14} /> {r.title}
                  </button>
                  {open === r.id && (
                    <div className="guide-article">
                      {article ? <p className="article-body">{article.body}</p> : !error && <p role="status">Loading…</p>}
                    </div>
                  )}
                </>
              ) : (
                <span className="guide-workshop">
                  <CalendarDays size={14} /> <strong>{r.title}</strong>
                  <small>
                    {" "}
                    · {new Date(r.startsAt!).toLocaleString()} · {r.modality === "InPerson" ? "In person" : r.modality}
                  </small>{" "}
                  {mine ? (
                    <span className="tag">{mine.toUpperCase()}</span>
                  ) : (
                    <button type="button" className="secondary" onClick={() => register(r)}>
                      Register
                    </button>
                  )}
                </span>
              )}
            </li>
          );
        })}
      </ul>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

function PlaceList({
  step,
  canMark,
  busy,
  mark,
  onOpenTask,
}: {
  step: Step;
  canMark: boolean;
  busy: boolean;
  mark: Mark;
  onOpenTask: (taskId: string) => void;
}) {
  const [all, setAll] = useState(false);
  const [reasonFor, setReasonFor] = useState<string | null>(null);
  const places = all ? step.locations : step.locations.slice(0, PLACE_LIMIT);
  return (
    <>
      <ul className="guide-places">
        {places.map((p) => (
          <li key={p.key} className={p.status === "Open" ? "" : "is-marked"}>
            <div>
              <strong>{p.where}</strong>
              {p.module && <small> in {p.module}</small>}
              <p>{p.detail}</p>
              {p.confirmedBy && <p className="guide-status">Confirmed by {p.confirmedBy} in a published review.</p>}
              {p.stillDetected ? (
                <p className="guide-status is-warning">Marked done, but still found after your latest import.</p>
              ) : p.status === "Done" ? (
                <p className="guide-status">Marked done by {p.setBy}. TeachWell will confirm after your next import.</p>
              ) : p.status === "NotApplicable" ? (
                <p className="guide-status">
                  Not applicable ({p.setBy}): {p.reason}
                </p>
              ) : null}
            </div>
            <div className="finding-actions">
              {p.taskId && (
                <button type="button" className="secondary" onClick={() => onOpenTask(p.taskId!)}>
                  Open task
                </button>
              )}
              {p.sourceUrl && (
                <a href={p.sourceUrl} target="_blank" rel="noopener noreferrer">
                  Open in Canvas <ExternalLink size={12} />
                </a>
              )}
              {canMark &&
                (p.status === "Open" ? (
                  <>
                    <button type="button" className="secondary" disabled={busy} aria-label={`Done: ${p.where}`} onClick={() => mark([p.key], "Done", undefined, "Marked done.")}>
                      Done
                    </button>
                    <button type="button" className="secondary" disabled={busy} aria-label={`Not applicable: ${p.where}`} onClick={() => setReasonFor(p.key)}>
                      Not applicable
                    </button>
                  </>
                ) : (
                  <button type="button" className="secondary" disabled={busy} aria-label={`Reopen: ${p.where}`} onClick={() => mark([p.key], "Open", undefined, "Reopened.")}>
                    Reopen
                  </button>
                ))}
            </div>
            {reasonFor === p.key && (
              <ReasonForm
                label={`Why doesn't this apply to ${p.where}?`}
                busy={busy}
                onCancel={() => setReasonFor(null)}
                onSave={async (reason) => {
                  if (await mark([p.key], "NotApplicable", reason, "Set aside.")) setReasonFor(null);
                }}
              />
            )}
          </li>
        ))}
      </ul>
      {step.locations.length > PLACE_LIMIT && (
        <button type="button" className="link-button" onClick={() => setAll(!all)}>
          {all ? "Show fewer" : `Show all ${step.locations.length} places`}
        </button>
      )}
    </>
  );
}

function ReasonForm({ label, busy, onSave, onCancel }: { label: string; busy: boolean; onSave: (reason: string) => void; onCancel: () => void }) {
  const [reason, setReason] = useState("");
  return (
    <div className="guide-form">
      <label>
        {label}
        <textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="For example: this link text is the file's real name." />
      </label>
      <div className="finding-actions">
        <button type="button" disabled={busy || !reason.trim()} onClick={() => onSave(reason.trim())}>
          Save reason
        </button>
        <button type="button" className="secondary" disabled={busy} onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}

// Opens a support ticket with the step and its places already described; the instructor adds what they are stuck on.
function HelpForm({ step, courseId, onClose, onSessionExpired }: { step: Step; courseId: string; onClose: () => void; onSessionExpired: (error: unknown) => void }) {
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [sent, setSent] = useState(false);
  const open = step.locations.filter((l) => l.status === "Open");
  const elements = [...new Set(open.map((l) => l.elementId).filter(Boolean))];
  async function send() {
    setBusy(true);
    setError("");
    const places = open.slice(0, 20).map((l) => `- ${l.where}: ${l.detail}`).join("\n");
    const description = `${note.trim()}\n\nImprovement guide step: ${step.name} (${plural(step.openPlaces, "open place")}).\n${places}${open.length > 20 ? `\n…and ${open.length - 20} more.` : ""}`;
    try {
      await request(`/support/tickets`, {
        courseId,
        elementId: elements.length === 1 ? elements[0] : null,
        revisionTaskId: null,
        category: step.aspect === "Accessibility" ? "Accessibility" : "CourseDesign",
        title: `Help with: ${step.name}`.slice(0, 200),
        description: description.slice(0, 4000),
        priority: step.severity,
        origin: "Guide",
      });
      setSent(true);
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
      else setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (sent)
    return (
      <div className="guide-form" role="status">
        <p>Your request was sent to the CTL with this step and its places attached. Follow it under Support.</p>
        <button type="button" className="secondary" onClick={onClose}>
          Close
        </button>
      </div>
    );
  return (
    <div className="guide-form">
      <label>
        What would you like help with?
        <textarea rows={3} maxLength={2000} value={note} onChange={(e) => setNote(e.target.value)} placeholder="For example: I am not sure how to describe the diagrams in Module 2." />
      </label>
      <p className="fine">The step and the places TeachWell found are added to your request automatically.</p>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="finding-actions">
        <button type="button" disabled={busy || !note.trim()} onClick={send}>
          {busy ? "Sending…" : "Send request"}
        </button>
        <button type="button" className="secondary" disabled={busy} onClick={onClose}>
          Cancel
        </button>
      </div>
    </div>
  );
}
