import { useEffect, useState } from "react";
import { ShieldCheck } from "lucide-react";
import { ApiError, request } from "./api";
import { Collapsible } from "./Collapsible";
import { Glossed } from "./Glossary";

interface Criterion {
  number: string;
  name: string;
  level: string;
  reference: string;
  state: "Findings" | "NoFindings" | "NotChecked";
  checks: string[];
  checksRan: number;
  fullyCovered: boolean;
  openPlaces: number;
  donePlaces: number;
  notApplicablePlaces: number;
}
interface Report {
  courseId: string;
  courseTitle: string;
  courseCode: string;
  standard: string;
  asOf: string;
  contentAsOf: string | null;
  standards: { name: string; versionNumber: number | null };
  checkedItems: number;
  criteriaInScope: number;
  criteriaOutOfScope: number;
  criteriaWithFindings: number;
  criteriaWithNoFindings: number;
  criteriaNotChecked: number;
  openPlaces: number;
  donePlaces: number;
  notApplicablePlaces: number;
  criteria: Criterion[];
  endorsement: string | null;
  endorsedAt: string | null;
  caveat: string;
  limitations: string[];
}

// What each state means, in the report's own terms. "NoFindings" deliberately avoids the word "met": an automated
// check can show that an issue was found, never that a criterion is satisfied (ADR 018).
const STATES: Record<Criterion["state"], { label: string; tag: string; meaning: string }> = {
  Findings: { label: "Work outstanding", tag: "severity-high", meaning: "At least one place related to this criterion is still open." },
  NoFindings: { label: "Nothing found", tag: "severity-low", meaning: "The checks that ran found nothing, or their places are fixed or set aside." },
  NotChecked: { label: "Not checked", tag: "", meaning: "No related check ran here, so this report says nothing about it either way." },
};

/**
 * The accessibility conformance report for one course (ADR 018): the improvement guide projected onto the WCAG 2.1
 * success criteria TeachWell's checks relate to, for the college's own record of its remediation work.
 *
 * It is not a conformance claim, and the screen is built so it cannot be read as one: every state is worded as what
 * was found rather than as a pass, the scope statement sits above the figures, and the criteria the report does not
 * cover are counted in plain sight.
 */
export function CourseConformance({
  courseId,
  onSessionExpired,
}: {
  courseId: string;
  onSessionExpired: (error: unknown) => void;
}) {
  const [report, setReport] = useState<Report | null>(null);
  const [error, setError] = useState("");
  const [wanted, setWanted] = useState(false);

  // As with the syllabus panel: a panel must not be able to take the course page down with it. An unexpected response
  // body would otherwise reach the render and throw on the first missing field, and React unmounts the whole subtree
  // when a render throws. Anything that is not a report is treated as nothing to show.
  function isReport(value: unknown): value is Report {
    if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
    const v = value as Partial<Report>;
    return Array.isArray(v.criteria) && typeof v.standard === "string" && typeof v.caveat === "string" && typeof v.asOf === "string";
  }

  useEffect(() => {
    if (!wanted || report) return;
    let active = true;
    request<unknown>(`/courses/${courseId}/conformance`)
      .then((r) => {
        if (active) setReport(isReport(r) ? r : null);
      })
      .catch((e) => {
        if (!active) return;
        if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
        else setError((e as Error).message);
      });
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [courseId, wanted]);

  const day = (value: string) => new Date(value).toLocaleDateString(undefined, { year: "numeric", month: "long", day: "numeric" });

  return (
    <Collapsible
      id="conformance"
      title="Accessibility conformance"
      icon={<ShieldCheck size={22} />}
      className="panel"
      defaultOpen={false}
      meta={report ? `${report.criteriaWithFindings} of ${report.criteriaInScope} criteria with work outstanding` : undefined}
      description="The improvement guide, grouped by the WCAG success criteria its checks relate to. Not a conformance claim."
      onOpen={() => setWanted(true)}
    >
      {error ? (
        <p className="error" role="alert">
          {error}
        </p>
      ) : !report ? (
        <p className="fine">Reading this course…</p>
      ) : (
        <>
      {/* The scope comes before the figures, not after them. */}
      <p className="fine">
        <Glossed text={report.caveat} />
      </p>

      <dl className="conformance-summary">
        <div>
          <dt>Reference standard</dt>
          <dd>{report.standard}</dd>
        </div>
        <div>
          <dt>Criteria in this report</dt>
          <dd>
            {report.criteriaInScope} of {report.criteriaInScope + report.criteriaOutOfScope} at Levels A and AA
          </dd>
        </div>
        <div>
          <dt>Needing a manual review</dt>
          <dd>{report.criteriaOutOfScope}</dd>
        </div>
        <div>
          <dt>Standards applied</dt>
          <dd>
            {report.standards.name}
            {report.standards.versionNumber !== null ? ` v${report.standards.versionNumber}` : ""}
          </dd>
        </div>
        <div>
          <dt>Content captured</dt>
          <dd>{report.contentAsOf ? day(report.contentAsOf) : "not recorded"}</dd>
        </div>
        <div>
          <dt>Report produced</dt>
          <dd>{day(report.asOf)}</dd>
        </div>
      </dl>

      {report.endorsement && (
        <p className="fine">
          {report.endorsement}
          {report.endorsedAt ? ` (${day(report.endorsedAt)})` : ""}
        </p>
      )}

      <div className="table-scroll">
        <table className="feedback-table">
          <caption className="fine">
            {report.criteriaWithFindings} with work outstanding · {report.criteriaWithNoFindings} with nothing found ·{" "}
            {report.criteriaNotChecked} not checked
          </caption>
          <thead>
            <tr>
              <th scope="col">Success criterion</th>
              <th scope="col">Level</th>
              <th scope="col">What the checks found</th>
              <th scope="col">Open</th>
              <th scope="col">Fixed</th>
              <th scope="col">Set aside</th>
            </tr>
          </thead>
          <tbody>
            {report.criteria.map((c) => (
              <tr key={c.number}>
                <th scope="row">
                  {c.number} {c.name}
                </th>
                <td>{c.level}</td>
                <td>
                  <span className={"tag " + STATES[c.state].tag}>{STATES[c.state].label}</span>
                  <small className="conformance-meaning">
                    {STATES[c.state].meaning}
                    {c.state !== "NotChecked" && !c.fullyCovered
                      ? ` Only ${c.checksRan} of ${c.checks.length} related checks ran, so this is partial.`
                      : ""}
                  </small>
                </td>
                <td>{c.openPlaces}</td>
                <td>{c.donePlaces}</td>
                <td>{c.notApplicablePlaces}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {report.limitations.length > 0 && (
        <>
          <h3>What this report could not check</h3>
          <ul className="fine">
            {report.limitations.map((l, i) => (
              <li key={i}>
                <Glossed text={l} />
              </li>
            ))}
          </ul>
        </>
      )}

      <p className="fine">
        <a href={`/api/v1/courses/${courseId}/conformance/export`}>Download CSV</a> — the same rows, with the scope,
        both dates and every limitation below them.
      </p>
        </>
      )}
    </Collapsible>
  );
}
