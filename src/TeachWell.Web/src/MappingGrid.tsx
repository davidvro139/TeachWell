import { useEffect, useMemo, useRef, useState } from "react";
import { Grid3x3 } from "lucide-react";
import { ApiError, request, typeLabel, type Competency, type CourseDetail, type Element } from "./api";

const MAPPABLE = new Set(["assignment", "quiz", "discussion"]);
const key = (competencyId: string, elementId: string) => `${competencyId}|${elementId}`;

interface Group {
  id: string;
  title: string;
  items: Element[];
}

/** Assessments grouped by module in course order; anything not placed in a module is listed last. Each assessment appears once. */
function groupAssessments(course: CourseDetail): Group[] {
  const byId = new Map(course.elements.map((e) => [e.id, e]));
  const seen = new Set<string>();
  const groups: Group[] = [];
  for (const module of course.elements.filter((e) => e.type === "module")) {
    const items = course.elements
      .filter((e) => e.parentElementId === module.id)
      // Canvas module items point at their content; demo courses nest the content directly.
      .map((e) => (e.targetElementId ? byId.get(e.targetElementId) : e))
      .filter((e): e is Element => !!e && MAPPABLE.has(e.type) && !seen.has(e.id));
    items.forEach((e) => seen.add(e.id));
    if (items.length > 0) groups.push({ id: module.id, title: module.title, items });
  }
  const rest = course.elements.filter((e) => MAPPABLE.has(e.type) && !seen.has(e.id));
  if (rest.length > 0) groups.push({ id: "other", title: groups.length > 0 ? "Not in a module" : "Assessments", items: rest });
  return groups;
}

/**
 * Map many assessments to competencies at once: tick cells, or a module row to map every assessment in that module.
 * Changes stay pending in the browser until "Save", which applies them in one all-or-nothing request.
 */
export function MappingGrid({
  course,
  competencies,
  onSaved,
  onCancel,
  onSessionExpired,
}: {
  course: CourseDetail;
  competencies: Competency[];
  onSaved: (changed: number) => void;
  onCancel: () => void;
  onSessionExpired: (error: unknown) => void;
}) {
  const groups = useMemo(() => groupAssessments(course), [course]);
  const saved = useMemo(() => new Set(competencies.flatMap((c) => c.mappings.map((m) => key(c.id, m.elementId)))), [competencies]);
  const [pending, setPending] = useState<Map<string, boolean>>(new Map());
  const [filter, setFilter] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const isMapped = (c: string, e: string) => pending.get(key(c, e)) ?? saved.has(key(c, e));
  function set(cells: [string, string][], mapped: boolean) {
    setPending((current) => {
      const next = new Map(current);
      for (const [c, e] of cells) {
        // A change back to the saved state is no longer a change.
        if (saved.has(key(c, e)) === mapped) next.delete(key(c, e));
        else next.set(key(c, e), mapped);
      }
      return next;
    });
  }

  async function save() {
    setBusy(true);
    setError("");
    try {
      const changes = [...pending].map(([k, mapped]) => {
        const [competencyId, elementId] = k.split("|");
        return { competencyId, elementId, mapped };
      });
      await request(`/courses/${course.id}/competencies/mappings`, { changes }, "PUT");
      onSaved(changes.length);
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
      else setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const query = filter.trim().toLowerCase();
  const shown = groups
    .map((g) => (query && !g.title.toLowerCase().includes(query) ? { ...g, items: g.items.filter((e) => e.title.toLowerCase().includes(query)) } : g))
    .filter((g) => g.items.length > 0);
  const total = groups.reduce((n, g) => n + g.items.length, 0);
  const countFor = (c: Competency) => groups.reduce((n, g) => n + g.items.filter((e) => isMapped(c.id, e.id)).length, 0);
  const unmapped = groups.reduce((n, g) => n + g.items.filter((e) => !competencies.some((c) => isMapped(c.id, e.id))).length, 0);

  return (
    <section className="panel mapping-grid" aria-labelledby="mapping-grid-title">
      <h3 id="mapping-grid-title">
        <Grid3x3 size={16} /> Map assessments to competencies
      </h3>
      {competencies.length === 0 || total === 0 ? (
        <>
          <p className="fine">
            {competencies.length === 0
              ? "Add competencies first (or paste your outcomes), then map assessments to them here."
              : "This course has no assignments, quizzes or discussions to map."}
          </p>
          <div className="finding-actions">
            <button type="button" className="secondary" onClick={onCancel}>
              Close
            </button>
          </div>
        </>
      ) : (
        <>
          <p className="fine">
            Tick each competency an assessment provides evidence for. A module's row ticks every assessment in that module. Nothing is saved until
            you choose Save. {unmapped} of {total} {total === 1 ? "assessment is" : "assessments are"} not mapped to any competency.
          </p>
          <ul className="mapping-legend">
            {competencies.map((c) => (
              <li key={c.id}>
                <span className="tag">{c.code}</span> {c.title}
              </li>
            ))}
          </ul>
          <input
            type="search"
            className="name-list-filter"
            aria-label="Filter assessments"
            placeholder="Filter by assessment or module…"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
          <div className="mapping-scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">Assessment</th>
                  {competencies.map((c) => (
                    <th scope="col" key={c.id} title={c.title}>
                      {c.code}
                      <small>{countFor(c)}</small>
                    </th>
                  ))}
                </tr>
              </thead>
              {shown.map((g) => (
                <tbody key={g.id}>
                  <tr className="mapping-module">
                    <th scope="rowgroup">
                      {g.title} <small>({g.items.length})</small>
                    </th>
                    {competencies.map((c) => {
                      const ticked = g.items.filter((e) => isMapped(c.id, e.id)).length;
                      return (
                        <td key={c.id}>
                          <TriState
                            label={`Map all of ${g.title} to ${c.code}`}
                            checked={ticked === g.items.length}
                            mixed={ticked > 0 && ticked < g.items.length}
                            onChange={(mapped) => set(g.items.map((e) => [c.id, e.id]), mapped)}
                          />
                        </td>
                      );
                    })}
                  </tr>
                  {g.items.map((e) => (
                    <tr key={e.id}>
                      <th scope="row">
                        {e.title} <small>{typeLabel(e.type)}</small>
                      </th>
                      {competencies.map((c) => {
                        const k = key(c.id, e.id);
                        return (
                          <td key={c.id} className={pending.has(k) ? "is-changed" : undefined}>
                            <input
                              type="checkbox"
                              aria-label={`${e.title}: ${c.code}`}
                              checked={isMapped(c.id, e.id)}
                              onChange={(ev) => set([[c.id, e.id]], ev.target.checked)}
                            />
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              ))}
            </table>
            {shown.length === 0 && <p className="fine">No assessments match "{filter}".</p>}
          </div>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <div className="finding-actions">
            <button type="button" disabled={busy || pending.size === 0} onClick={save}>
              {busy ? "Saving…" : pending.size === 0 ? "No changes" : `Save ${pending.size} ${pending.size === 1 ? "change" : "changes"}`}
            </button>
            <button type="button" className="secondary" disabled={busy || pending.size === 0} onClick={() => setPending(new Map())}>
              Discard changes
            </button>
            <button type="button" className="secondary" disabled={busy} onClick={onCancel}>
              Close
            </button>
          </div>
        </>
      )}
    </section>
  );
}

function TriState({ label, checked, mixed, onChange }: { label: string; checked: boolean; mixed: boolean; onChange: (mapped: boolean) => void }) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (ref.current) ref.current.indeterminate = mixed;
  }, [mixed]);
  // A partly-mapped module maps the rest on click, the usual tri-state behaviour.
  return <input ref={ref} type="checkbox" aria-label={label} checked={checked} onChange={() => onChange(!checked)} />;
}
