import { useEffect, useState } from "react";
import { BookOpen } from "lucide-react";
import { ApiError, ASPECTS, request, type CheckCatalogEntry, type KnowledgeArticleSummary, type Workshop } from "./api";
import "./Guide.css";

interface Link {
  id: string;
  checkCode: string;
  checkName: string;
  kind: "Article" | "Workshop";
  resourceId: string;
  title: string;
  status: string;
}

/**
 * Lets the CTL offer a knowledge-base article or an upcoming workshop on every improvement-guide step for a check. Instructors see
 * published articles and workshops that have not started; the rest stay linked but hidden.
 */
export function GuideResources({ onSessionExpired }: { onSessionExpired: (error: unknown) => void }) {
  const [links, setLinks] = useState<Link[] | null>(null);
  const [checks, setChecks] = useState<CheckCatalogEntry[]>([]);
  const [articles, setArticles] = useState<KnowledgeArticleSummary[]>([]);
  const [workshops, setWorkshops] = useState<Workshop[]>([]);
  const [check, setCheck] = useState("");
  const [resource, setResource] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  useEffect(() => {
    let active = true;
    setError("");
    Promise.all([
      request<Link[]>("/standards/resources"),
      request<CheckCatalogEntry[]>("/review-checks"),
      request<KnowledgeArticleSummary[]>("/knowledge"),
      request<Workshop[]>("/workshops"),
    ])
      .then(([l, c, a, w]) => {
        if (!active) return;
        setLinks(l);
        setChecks(c);
        setArticles(a);
        setWorkshops(w);
      })
      .catch((e) => active && fail(e));
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [retry]);

  async function change(work: () => Promise<Link[]>, success: string) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      setLinks(await work());
      setNotice(success);
      return true;
    } catch (e) {
      fail(e);
      return false;
    } finally {
      setBusy(false);
    }
  }
  async function add() {
    const [kind, id] = resource.split(":");
    const ok = await change(
      () => request<Link[]>("/standards/resources", { checkCode: check, articleId: kind === "article" ? id : null, workshopId: kind === "workshop" ? id : null }),
      "Resource linked. Instructors see it on that check's guide steps.",
    );
    if (ok) setResource("");
  }

  const upcoming = workshops.filter((w) => new Date(w.startsAt) > new Date());
  const byCheck = new Map<string, Link[]>();
  for (const l of links ?? []) byCheck.set(l.checkCode, [...(byCheck.get(l.checkCode) ?? []), l]);

  return (
    <section className="panel guide-feedback" aria-labelledby="guide-resources-title">
      <h2 id="guide-resources-title">
        <BookOpen size={18} /> Resources in the improvement guide
      </h2>
      <p className="fine">
        Link a job aid from the knowledge base, or an upcoming workshop, to a check. Instructors then see it on every guide step for that check,
        next to the guidance. Draft articles and workshops that have started stay linked but are not shown.
      </p>
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
      {links === null ? (
        !error && <p role="status">Loading linked resources…</p>
      ) : (
        <>
          {links.length === 0 ? (
            <p className="sharing-empty">No resources are linked yet.</p>
          ) : (
            <ul className="guide-places">
              {[...byCheck].map(([code, list]) => (
                <li key={code}>
                  <div>
                    <strong>{list[0].checkName}</strong>
                    <ul className="resource-list">
                      {list.map((l) => (
                        <li key={l.id}>
                          <span className="tag">{l.kind.toUpperCase()}</span> {l.title} <small>· {l.status}</small>{" "}
                          <button
                            type="button"
                            className="link-button"
                            disabled={busy}
                            aria-label={`Unlink ${l.title} from ${l.checkName}`}
                            onClick={() => change(() => request<Link[]>(`/standards/resources/${l.id}`, undefined, "DELETE"), "Resource unlinked.")}
                          >
                            Unlink
                          </button>
                        </li>
                      ))}
                    </ul>
                  </div>
                </li>
              ))}
            </ul>
          )}
          <div className="form-row resource-form">
            <label>
              Check
              <select value={check} onChange={(e) => setCheck(e.target.value)}>
                <option value="">Choose a check…</option>
                {ASPECTS.map((a) => (
                  <optgroup key={a.value} label={a.label}>
                    {checks
                      .filter((c) => c.aspect === a.value)
                      .map((c) => (
                        <option key={c.code} value={c.code}>
                          {c.name}
                        </option>
                      ))}
                  </optgroup>
                ))}
              </select>
            </label>
            <label>
              Resource
              <select value={resource} onChange={(e) => setResource(e.target.value)}>
                <option value="">Choose an article or workshop…</option>
                <optgroup label="Knowledge base">
                  {articles.map((a) => (
                    <option key={a.id} value={`article:${a.id}`}>
                      {a.title}
                      {a.status === "Draft" ? " (draft)" : ""}
                    </option>
                  ))}
                </optgroup>
                <optgroup label="Upcoming workshops">
                  {upcoming.map((w) => (
                    <option key={w.id} value={`workshop:${w.id}`}>
                      {w.title} ({new Date(w.startsAt).toLocaleDateString()})
                    </option>
                  ))}
                </optgroup>
              </select>
            </label>
            <button type="button" disabled={busy || !check || !resource} onClick={add}>
              Link resource
            </button>
          </div>
        </>
      )}
    </section>
  );
}
