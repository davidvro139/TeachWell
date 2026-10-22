import { useEffect, useState } from "react";
import { Award } from "lucide-react";
import { ApiError, request } from "./api";
import { Collapsible } from "./Collapsible";
import { Glossed } from "./Glossary";
import "./Guide.css";

export interface ReadinessGuideSummary {
  openSteps: number;
  requiredOpenSteps: number;
  openPlaces: number;
  donePlaces: number;
  notApplicablePlaces: number;
  reasons: { check: string; where: string; reason: string }[];
}
export interface ReadinessRequest {
  id: string;
  courseId: string;
  courseCode: string;
  courseTitle: string;
  requestedByName: string;
  requestedAt: string;
  note: string;
  status: "Requested" | "InReview" | "Endorsed" | "ChangesRequested" | "Withdrawn";
  standardsName: string;
  standardsVersionNumber: number | null;
  contentAsOf: string | null;
  reviewerName: string | null;
  takenByMe: boolean;
  takenAt: string | null;
  decidedAt: string | null;
  decisionSummary: string;
  revision: string;
  guide: ReadinessGuideSummary | null;
}
interface Endorsement {
  id: string;
  wording: string;
  reviewerName: string;
  endorsedAt: string;
  validUntil: string;
  summary: string;
  recheckDue: boolean;
  recheckReasons: string[];
}
interface CourseReadiness {
  isOwner: boolean;
  canRequest: boolean;
  blockers: string[];
  requiredOpenSteps: number;
  active: ReadinessRequest | null;
  lastDecision: ReadinessRequest | null;
  endorsement: Endorsement | null;
  history: ReadinessRequest[];
  canDecide: boolean;
  hasQualifyingReview: boolean;
  requirements: { label: string; met: boolean; detail: string }[];
}

const date = (value: string) => new Date(value).toLocaleDateString(undefined, { year: "numeric", month: "long", day: "numeric" });
export const standardsLabel = (r: { standardsName: string; standardsVersionNumber: number | null }) =>
  r.standardsName + (r.standardsVersionNumber ? ` v${r.standardsVersionNumber}` : "");
export const statusLabel: Record<ReadinessRequest["status"], string> = {
  Requested: "Waiting for a CTL reviewer",
  InReview: "In review",
  Endorsed: "Endorsed",
  ChangesRequested: "Changes requested",
  Withdrawn: "Withdrawn",
};

/**
 * Phase 10 (ADR 013): the owner asks the CTL for a readiness review once the guide's required steps are resolved or justified; the
 * reviewer who took the request decides here. Endorsements are dated, name their standard-set version, and show "re-check due".
 */
export function CourseReadiness({ courseId, refreshKey, onSessionExpired }: { courseId: string; refreshKey: number; onSessionExpired: (error: unknown) => void }) {
  const [state, setState] = useState<CourseReadiness | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");
  const [decision, setDecision] = useState<"Endorsed" | "ChangesRequested">("ChangesRequested");
  const [summary, setSummary] = useState("");
  const [retry, setRetry] = useState(0);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  useEffect(() => {
    let active = true;
    request<CourseReadiness>(`/courses/${courseId}/readiness`)
      .then((s) => active && setState(s))
      .catch((e) => active && fail(e));
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [courseId, refreshKey, retry]);

  async function act(work: () => Promise<CourseReadiness>, success: string) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      setState(await work());
      setNotice(success);
      return true;
    } catch (e) {
      fail(e);
      return false;
    } finally {
      setBusy(false);
    }
  }

  const s = state;
  const meta = !s
    ? undefined
    : s.endorsement
      ? s.endorsement.recheckDue
        ? "Endorsed · re-check due"
        : "Endorsed"
      : s.active
        ? statusLabel[s.active.status]
        : s.lastDecision?.status === "ChangesRequested"
          ? "Changes requested"
          : "Not requested";

  return (
    <Collapsible
      id="readiness"
      title="CTL readiness review"
      icon={<Award size={18} />}
      meta={meta}
      busy={busy}
      description={
        <Glossed text="When the improvement guide's required steps are resolved or justified, ask the CTL to review the course. An endorsement is dated and names the standards it was given against; it is not accreditation or certification." />
      }
    >
      {error && (
        <div className="error" role="alert">
          {error}
          {!s && <button onClick={() => setRetry((x) => x + 1)}>Try again</button>}
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {!s ? (
        !error && <p role="status">Loading readiness…</p>
      ) : (
        <div className="readiness">
          {s.endorsement && (
            <div className={"readiness-endorsement" + (s.endorsement.recheckDue ? " is-due" : "")}>
              <p>
                <Award size={16} /> <strong>{s.endorsement.wording}</strong>
              </p>
              <p className="fine">
                Reviewed by {s.endorsement.reviewerName}. Considered current until {date(s.endorsement.validUntil)}.
              </p>
              <p>{s.endorsement.summary}</p>
              {s.endorsement.recheckDue && (
                <div className="readiness-due" role="status">
                  <strong>Re-check due.</strong> The endorsement still stands as a record; ask for a new review when you are ready.
                  <ul>
                    {s.endorsement.recheckReasons.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}

          {s.active ? (
            <div className="readiness-active">
              <p>
                <strong>{statusLabel[s.active.status]}</strong>
                {s.active.status === "InReview" && s.active.reviewerName ? ` with ${s.active.reviewerName}` : ""} · requested {date(s.active.requestedAt)} against{" "}
                {standardsLabel(s.active)}
              </p>
              {s.active.note && <p className="fine">Your note: {s.active.note}</p>}
              {s.isOwner && (
                <button
                  type="button"
                  className="secondary"
                  disabled={busy}
                  onClick={() =>
                    act(() => request<CourseReadiness>(`/readiness/${s.active!.id}/withdraw`, { revision: s.active!.revision }), "Request withdrawn.")
                  }
                >
                  Withdraw request
                </button>
              )}
              {s.active.guide && !s.isOwner && <GuideSummary summary={s.active.guide} />}
            </div>
          ) : (
            s.lastDecision?.status === "ChangesRequested" &&
            (!s.endorsement || new Date(s.lastDecision.decidedAt!) > new Date(s.endorsement.endorsedAt)) && (
              <div className="readiness-changes">
                <p>
                  <strong>Changes requested</strong> by {s.lastDecision.reviewerName} on {date(s.lastDecision.decidedAt!)}
                </p>
                <p>{s.lastDecision.decisionSummary}</p>
              </div>
            )
          )}

          {s.isOwner && !s.active && (
            <div className="guide-form">
              {s.blockers.length > 0 ? (
                <>
                  <p>
                    <strong>Not ready to request yet.</strong>
                  </p>
                  <ul>
                    {s.blockers.map((b) => (
                      <li key={b}>{b}</li>
                    ))}
                  </ul>
                  <p className="fine">Mark each required step Done after fixing it, or Not applicable with a reason, in the improvement guide above.</p>
                </>
              ) : (
                <>
                  <label>
                    Note for the CTL (optional)
                    <textarea rows={2} maxLength={2000} value={note} onChange={(e) => setNote(e.target.value)} placeholder="For example: teaching this online for the first time in spring." />
                  </label>
                  <p className="fine">
                    The CTL reviewer who takes your request gets review access to this course. They see your guide progress and your reasons for any
                    step marked not applicable.
                  </p>
                  <button
                    type="button"
                    disabled={busy || !s.canRequest}
                    onClick={async () => {
                      if (await act(() => request<CourseReadiness>(`/courses/${courseId}/readiness`, { note }), "Readiness review requested.")) setNote("");
                    }}
                  >
                    {s.endorsement ? "Request a new readiness review" : "Request readiness review"}
                  </button>
                </>
              )}
            </div>
          )}

          {s.canDecide && s.active && (
            <div className="guide-form readiness-decide">
              <h4>Your decision</h4>
              <div role="radiogroup" aria-label="Decision" className="readiness-choice">
                <label>
                  <input type="radio" name="decision" checked={decision === "ChangesRequested"} onChange={() => setDecision("ChangesRequested")} /> Request changes
                </label>
                <label>
                  <input type="radio" name="decision" checked={decision === "Endorsed"} onChange={() => setDecision("Endorsed")} /> Endorse
                </label>
              </div>
              {decision === "Endorsed" && (
                <div className="readiness-requirements">
                  <p className="fine">Before endorsing, the standards this request was made under need:</p>
                  <ul>
                    {s.requirements.map((r) => (
                      <li key={r.label} className={r.met ? "is-met" : "is-unmet"}>
                        {r.met ? "✓" : "○"} {r.label} <small>({r.detail})</small>
                      </li>
                    ))}
                  </ul>
                  {!s.hasQualifyingReview && <p className="fine">Publish the missing reviews from Course reviews, below.</p>}
                </div>
              )}
              <label>
                {decision === "Endorsed" ? "What the endorsement rests on" : "What needs to change"}
                <textarea rows={3} maxLength={4000} value={summary} onChange={(e) => setSummary(e.target.value)} />
              </label>
              <button
                type="button"
                disabled={busy || !summary.trim() || (decision === "Endorsed" && !s.hasQualifyingReview)}
                onClick={async () => {
                  if (
                    await act(
                      () => request<CourseReadiness>(`/readiness/${s.active!.id}/decide`, { revision: s.active!.revision, decision, summary }),
                      decision === "Endorsed" ? "Course endorsed." : "Changes requested.",
                    )
                  )
                    setSummary("");
                }}
              >
                {decision === "Endorsed" ? "Endorse course" : "Request changes"}
              </button>
            </div>
          )}

          {s.history.length > 0 && (
            <details className="report-details">
              <summary>History ({s.history.length})</summary>
              <ul>
                {s.history.map((r) => (
                  <li key={r.id}>
                    {date(r.requestedAt)}: {statusLabel[r.status]}
                    {r.reviewerName ? ` · ${r.reviewerName}` : ""} · {standardsLabel(r)}
                    {r.decisionSummary ? ` · "${r.decisionSummary}"` : ""}
                  </li>
                ))}
              </ul>
            </details>
          )}
        </div>
      )}
    </Collapsible>
  );
}

export function GuideSummary({ summary }: { summary: ReadinessGuideSummary }) {
  return (
    <div className="readiness-guide">
      <p className="fine">
        When the instructor asked: {summary.openSteps} guide steps open ({summary.openPlaces} places), {summary.donePlaces} places marked done,{" "}
        {summary.notApplicablePlaces} marked not applicable.
      </p>
      {summary.reasons.length > 0 && (
        <ul>
          {summary.reasons.map((r, i) => (
            <li key={i}>
              <strong>{r.check}</strong> · {r.where}: "{r.reason}"
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
