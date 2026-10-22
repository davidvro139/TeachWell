import { createContext, Fragment, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { BookMarked, EyeOff, ExternalLink, LifeBuoy, Pencil, Plus, Search } from "lucide-react";
import { ApiError, request, type KnowledgeArticleSummary, type Workshop } from "./api";
import {
  mergeGlossary,
  normalizeTag,
  tagsOf,
  type CollegeGlossaryEntry,
  type GlossaryLink,
  type GlossaryTerm,
  type MergedTerm,
} from "./glossaryTerms";
import "./Glossary.css";

/** How a term's popover reaches the Glossary page; absent where the page is not available (e.g. administrator roles). */
export const GlossaryNavigation = createContext<{ openGlossary: (termId: string) => void } | null>(null);

const escape = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
/** One case-insensitive, whole-word pattern for a list of phrases; apostrophes match either style and spaces any whitespace. */
const phrasePattern = (list: string[]) =>
  new RegExp(
    "(?<![\\w'’-])(" + list.map((p) => escape(p).replace(/'/g, "['’]").replace(/ /g, "\\s+")).join("|") + ")(?![\\w’-]|'\\w)",
    "gi",
  );

interface GlossaryState {
  /** Every term including hidden ones, with the college's changes applied. */
  all: MergedTerm[];
  /** The terms people see. */
  terms: MergedTerm[];
  byId: Map<string, MergedTerm>;
  /** Finds the first mention of each visible term in plain text; null when no term has a phrase. */
  matcher: { pattern: RegExp; termOf: (match: string) => string | undefined } | null;
  reload: () => void;
}

function build(entries: CollegeGlossaryEntry[], reload: () => void): GlossaryState {
  const all = mergeGlossary(entries);
  const terms = all.filter((t) => !t.hidden);
  // Longest phrase first, so "Bloom's revised taxonomy" wins over "Bloom".
  const phrases = terms.flatMap((t) => t.phrases.map((p) => ({ phrase: p, id: t.id }))).sort((a, b) => b.phrase.length - a.phrase.length);
  const owner = new Map<string, string>();
  for (const p of phrases) if (!owner.has(normalizeTag(p.phrase))) owner.set(normalizeTag(p.phrase), p.id);
  return {
    all,
    terms,
    byId: new Map(all.map((t) => [t.id, t])),
    matcher: phrases.length ? { pattern: phrasePattern(phrases.map((p) => p.phrase)), termOf: (m) => owner.get(normalizeTag(m)) } : null,
    reload,
  };
}
const GlossaryData = createContext<GlossaryState>(build([], () => {}));
export const useGlossary = () => useContext(GlossaryData);

/**
 * Loads the college's changes once per sign-in and role, and shares the merged glossary with every term on the page. Without them (for
 * example offline, or for roles without glossary access) TeachWell's own terms still work.
 */
export function GlossaryProvider({ enabled, children }: { enabled: boolean; children: ReactNode }) {
  const [entries, setEntries] = useState<CollegeGlossaryEntry[]>([]);
  const [version, setVersion] = useState(0);
  const reload = useCallback(() => setVersion((v) => v + 1), []);
  useEffect(() => {
    if (!enabled) {
      setEntries([]);
      return;
    }
    let active = true;
    request<CollegeGlossaryEntry[]>("/glossary")
      .then((e) => {
        if (active) setEntries(e);
      })
      .catch(() => {
        // TeachWell's own wording is a safe fallback; the Glossary page reports load failures itself.
      });
    return () => {
      active = false;
    };
  }, [enabled, version]);
  const state = useMemo(() => build(entries, reload), [entries, reload]);
  return <GlossaryData.Provider value={state}>{children}</GlossaryData.Provider>;
}

function OutsideLink({ link }: { link: GlossaryLink }) {
  return (
    <span className="term-link-row">
      <a href={link.url} target="_blank" rel="noopener noreferrer" className="term-link">
        {link.label}
        <ExternalLink size={12} aria-hidden="true" />
        <span className="visually-hidden"> (opens in a new tab)</span>
      </a>
      {link.source && <small> · {link.source}</small>}
    </span>
  );
}

const POPOVER_WIDTH = 320;

/**
 * A glossary term in running text. Hovering (mouse) or activating it shows a short definition, up to two outside readings and a way to
 * the CTL's resources. It is a button with a non-modal popover, so it works with a keyboard, a screen reader and touch; Escape closes it.
 */
export function Term({ id, children }: { id: string; children?: ReactNode }) {
  const { byId } = useGlossary();
  const term = byId.get(id);
  const navigation = useContext(GlossaryNavigation);
  const [open, setOpen] = useState(false);
  const [pinned, setPinned] = useState(false);
  const [position, setPosition] = useState<{ left: number; top?: number; bottom?: number }>({ left: 0 });
  const wrapper = useRef<HTMLSpanElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const timer = useRef<number | undefined>(undefined);
  const popoverId = useId();

  function place() {
    const rect = trigger.current?.getBoundingClientRect();
    if (!rect) return;
    const width = Math.min(POPOVER_WIDTH, window.innerWidth - 32);
    const left = Math.max(16, Math.min(rect.left, window.innerWidth - width - 16));
    // Below the word when there is room, otherwise above it.
    setPosition(window.innerHeight - rect.bottom > 280 ? { left, top: rect.bottom + 6 } : { left, bottom: window.innerHeight - rect.top + 6 });
  }
  function show(pin: boolean) {
    window.clearTimeout(timer.current);
    place();
    setOpen(true);
    if (pin) setPinned(true);
  }
  function hide() {
    window.clearTimeout(timer.current);
    setOpen(false);
    setPinned(false);
  }

  useEffect(() => {
    if (!open) return;
    const reposition = () => place();
    const outside = (e: PointerEvent) => {
      if (!wrapper.current?.contains(e.target as Node)) hide();
    };
    window.addEventListener("scroll", reposition, true);
    window.addEventListener("resize", reposition);
    document.addEventListener("pointerdown", outside);
    return () => {
      window.removeEventListener("scroll", reposition, true);
      window.removeEventListener("resize", reposition);
      document.removeEventListener("pointerdown", outside);
    };
  }, [open]);
  useEffect(() => () => window.clearTimeout(timer.current), []);

  // A hidden or unknown term is just text.
  if (!term || term.hidden) return <>{children ?? term?.term}</>;
  return (
    <span
      ref={wrapper}
      className="term"
      onPointerEnter={(e) => {
        if (e.pointerType !== "mouse") return;
        window.clearTimeout(timer.current);
        if (!open) timer.current = window.setTimeout(() => show(false), 300);
      }}
      onPointerLeave={(e) => {
        if (e.pointerType !== "mouse" || pinned) return;
        window.clearTimeout(timer.current);
        timer.current = window.setTimeout(hide, 250);
      }}
      onKeyDown={(e) => {
        if (e.key === "Escape" && open) {
          e.stopPropagation();
          hide();
          trigger.current?.focus();
        }
      }}
      onBlur={(e) => {
        if (!wrapper.current?.contains(e.relatedTarget as Node)) hide();
      }}
    >
      <button
        ref={trigger}
        type="button"
        className="term-trigger"
        aria-expanded={open}
        aria-controls={open ? popoverId : undefined}
        aria-haspopup="dialog"
        onClick={() => (open && pinned ? hide() : show(true))}
      >
        {children ?? term.term}
      </button>
      {open && (
        <span
          id={popoverId}
          role="dialog"
          aria-label={term.term}
          className="term-pop"
          style={{ ...position, width: Math.min(POPOVER_WIDTH, window.innerWidth - 32) }}
        >
          <strong className="term-pop-title">{term.term}</strong>
          <span className="term-pop-def">{term.definition}</span>
          {term.origin !== "teachwell" && (
            <span className="term-pop-origin">{term.origin === "college" ? "Added by your CTL" : "Worded by your CTL"}</span>
          )}
          {term.links.length > 0 && (
            <span className="term-pop-links">
              <span className="term-pop-label">Learn more</span>
              {term.links.slice(0, 2).map((l) => (
                <OutsideLink key={l.url} link={l} />
              ))}
            </span>
          )}
          {navigation && (
            <button
              type="button"
              className="term-pop-ctl"
              onClick={() => {
                hide();
                navigation.openGlossary(term.id);
              }}
            >
              <BookMarked size={14} aria-hidden="true" /> From your CTL: articles, workshops and help
            </button>
          )}
        </span>
      )}
    </span>
  );
}

/** Plain text with the first mention of each glossary term turned into a Term. Text is rendered as text, never as HTML. */
export function Glossed({ text, skip = [] }: { text: string; skip?: string[] }) {
  const { matcher } = useGlossary();
  const parts = useMemo(() => {
    if (!matcher) return [text];
    const seen = new Set(skip);
    const out: ReactNode[] = [];
    let last = 0;
    for (const m of text.matchAll(matcher.pattern)) {
      const id = matcher.termOf(m[0]);
      if (!id || seen.has(id)) continue;
      seen.add(id);
      out.push(
        text.slice(last, m.index),
        <Term key={m.index} id={id}>
          {m[0]}
        </Term>,
      );
      last = m.index! + m[0].length;
    }
    out.push(text.slice(last));
    return out;
  }, [text, matcher, skip.join()]);
  return (
    <>
      {parts.map((p, i) => (
        <Fragment key={i}>{p}</Fragment>
      ))}
    </>
  );
}

/** Whether CTL material belongs under a term: an article tagged with it, or an article or workshop that names it in its title. */
function matchesTerm(term: GlossaryTerm, title: string, tags: string[] = [], description = "") {
  const wanted = tagsOf(term);
  if (tags.some((t) => wanted.has(normalizeTag(t)))) return true;
  if (term.phrases.length === 0) return false;
  return phrasePattern(term.phrases).test(title) || (!!description && phrasePattern(term.phrases).test(description));
}
const supportCategory = (term: GlossaryTerm) => term.category ?? "CourseDesign";

const slugify = (name: string) =>
  normalizeTag(name)
    .replace(/'/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 56)
    .replace(/-+$/, "");

/** The CTL's form for rewording a term or adding one. Links are plain web addresses, checked again by the API. */
function TermEditor({
  term,
  takenIds,
  onSaved,
  onCancel,
}: {
  term: MergedTerm | null;
  takenIds: Set<string>;
  onSaved: (termId: string) => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(term?.term ?? "");
  const [definition, setDefinition] = useState(term?.definition ?? "");
  const [phrases, setPhrases] = useState((term?.phrases ?? []).join("\n"));
  const [tags, setTags] = useState((term?.tags ?? []).join(", "));
  const [links, setLinks] = useState<GlossaryLink[]>(term?.links.map((l) => ({ ...l })) ?? []);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const headingId = useId();

  // A new term's id comes from its name and never collides with another term, built-in or the college's.
  let termId = term?.id ?? slugify(name);
  if (!term && termId) for (let n = 2; takenIds.has(termId); n++) termId = `${slugify(name)}-${n}`;

  async function save(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await request(
        `/glossary/${encodeURIComponent(termId)}`,
        {
          term: name,
          definition,
          phrases: phrases.split("\n"),
          tags: tags.split(","),
          links: links.filter((l) => l.label.trim() || l.url.trim()),
          hidden: false,
          revision: term?.entry?.revision ?? null,
        },
        "PUT",
      );
      onSaved(termId);
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }
  const setLink = (i: number, patch: Partial<GlossaryLink>) => setLinks((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  return (
    <form className="glossary-editor" onSubmit={save} aria-labelledby={headingId}>
      <h3 id={headingId}>{term ? `Edit “${term.term}”` : "Add a term"}</h3>
      {term?.origin === "teachwell" && (
        <p className="fine">Your wording replaces TeachWell's for everyone at your college. You can return to TeachWell's wording at any time.</p>
      )}
      <label>
        Term
        <input required maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label>
        Definition
        <textarea required maxLength={1200} rows={4} value={definition} onChange={(e) => setDefinition(e.target.value)} />
      </label>
      <label>
        Phrases to explain wherever they appear (one per line)
        <textarea rows={3} value={phrases} onChange={(e) => setPhrases(e.target.value)} placeholder={"course shell\ncourse shells"} />
        <small>Each 3 to 60 characters. Matching ignores capitals and only finds whole words.</small>
      </label>
      <label>
        Knowledge-base tags that list an article here (comma separated)
        <input value={tags} onChange={(e) => setTags(e.target.value)} />
      </label>
      <fieldset className="glossary-editor-links">
        <legend>Links to learn more (up to 5)</legend>
        {links.map((l, i) => (
          <div key={i} className="glossary-editor-link">
            <label>
              Title
              <input value={l.label} maxLength={120} onChange={(e) => setLink(i, { label: e.target.value })} />
            </label>
            <label>
              Source
              <input value={l.source} maxLength={120} onChange={(e) => setLink(i, { source: e.target.value })} placeholder="e.g. Our CTL" />
            </label>
            <label>
              Web address
              <input type="url" value={l.url} maxLength={500} onChange={(e) => setLink(i, { url: e.target.value })} placeholder="https://" />
            </label>
            <button type="button" className="secondary" onClick={() => setLinks((ls) => ls.filter((_, j) => j !== i))}>
              Remove link {i + 1}
            </button>
          </div>
        ))}
        {links.length < 5 && (
          <button type="button" className="secondary" onClick={() => setLinks((ls) => [...ls, { label: "", source: "", url: "" }])}>
            <Plus size={16} aria-hidden="true" /> Add a link
          </button>
        )}
      </fieldset>
      {!term && termId && (
        <p className="fine">
          Saved as <code>{termId}</code>, which also works as a knowledge-base tag.
        </p>
      )}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="finding-actions">
        <button disabled={busy || !termId}>{busy ? "Saving…" : "Save term"}</button>
        <button type="button" className="secondary" disabled={busy} onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}

interface PageProps {
  initialTermId: string | null;
  isCtl: boolean;
  onOpenArticle: (slug: string) => void;
  onOpenWorkshops: () => void;
  onAskCtl: (request: { title: string; category: string }) => void;
  onSessionExpired: (error: unknown) => void;
}

/** Every term in one place, with outside reading and whatever the CTL has published about it (ADR 015). The CTL edits terms here. */
export function GlossaryPage({ initialTermId, isCtl, onOpenArticle, onOpenWorkshops, onAskCtl, onSessionExpired }: PageProps) {
  const glossary = useGlossary();
  const [query, setQuery] = useState("");
  const [articles, setArticles] = useState<KnowledgeArticleSummary[] | null>(null);
  const [workshops, setWorkshops] = useState<Workshop[] | null>(null);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const [editing, setEditing] = useState<string | null>(null); // a term id, or "new"
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [actionError, setActionError] = useState("");
  const [target, setTarget] = useState(initialTermId);

  useEffect(() => {
    let active = true;
    setError("");
    Promise.all([request<KnowledgeArticleSummary[]>("/knowledge"), request<Workshop[]>("/workshops")])
      .then(([a, w]) => {
        if (!active) return;
        setArticles(a);
        setWorkshops(w);
      })
      .catch((e) => {
        if (!active) return;
        if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
        else setError("Your CTL's articles and workshops could not be loaded. The definitions and outside reading below still work.");
      });
    return () => {
      active = false;
    };
  }, [retry]);
  useEffect(() => {
    if (target) document.getElementById("term-" + target)?.scrollIntoView({ block: "start" });
  }, [target, glossary.terms]);

  async function act(run: () => Promise<unknown>, done: string) {
    setBusy(true);
    setActionError("");
    setNotice("");
    try {
      await run();
      glossary.reload();
      setNotice(done);
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
      else setActionError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const remove = (t: MergedTerm, done: string) => act(() => request(`/glossary/${encodeURIComponent(t.id)}`, undefined, "DELETE"), done);
  const hide = (t: MergedTerm) =>
    act(
      () => request(`/glossary/${encodeURIComponent(t.id)}`, { hidden: true, term: t.term, revision: t.entry?.revision ?? null }, "PUT"),
      `“${t.term}” is hidden from instructors.`,
    );
  // Showing a term the CTL had reworded or added keeps that wording; one that was only hidden returns to TeachWell's wording.
  const showAgain = (t: MergedTerm) =>
    t.origin === "teachwell" || !t.entry
      ? remove(t, `“${t.term}” is shown again.`)
      : act(() => request(`/glossary/${encodeURIComponent(t.id)}`, { ...t.entry, hidden: false }, "PUT"), `“${t.term}” is shown again.`);

  const terms = useMemo(() => {
    const q = normalizeTag(query);
    return [...glossary.terms]
      .sort((a, b) => a.term.localeCompare(b.term))
      .filter((t) => !q || normalizeTag(t.term + " " + t.phrases.join(" ") + " " + t.definition).includes(q));
  }, [query, glossary.terms]);
  const hiddenTerms = glossary.all.filter((t) => t.hidden);
  const takenIds = new Set(glossary.all.map((t) => t.id));
  const now = Date.now();

  return (
    <>
      <span className="eyebrow">HELP</span>
      <h1>Glossary</h1>
      <p className="intro">
        Plain-language explanations of the teaching, course-design and accessibility terms TeachWell uses, with free reading from teaching centres and
        standards bodies, and whatever your CTL has published on each. Underlined terms elsewhere in TeachWell open the same explanation.
        {isCtl && " As a CTL reviewer you can reword any term, add your college's own, or hide one; changes apply to everyone at your college at once."}
      </p>
      <div className="glossary-toolbar">
        <label className="glossary-search">
          <Search size={16} aria-hidden="true" />
          <span className="visually-hidden">Search the glossary</span>
          <input type="search" placeholder="Search terms, e.g. Bloom" value={query} onChange={(e) => setQuery(e.target.value)} />
        </label>
        {isCtl && editing !== "new" && (
          <button type="button" onClick={() => setEditing("new")}>
            <Plus size={16} aria-hidden="true" /> Add a term
          </button>
        )}
      </div>
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {actionError && (
        <p className="error" role="alert">
          {actionError}
        </p>
      )}
      {error && (
        <div className="error" role="alert">
          {error} <button onClick={() => setRetry((x) => x + 1)}>Try again</button>
        </div>
      )}
      {editing === "new" && (
        <section className="panel">
          <TermEditor
            term={null}
            takenIds={takenIds}
            onCancel={() => setEditing(null)}
            onSaved={(id) => {
              setEditing(null);
              setQuery("");
              glossary.reload();
              setNotice("Term added.");
              setTarget(id);
            }}
          />
        </section>
      )}
      {terms.length === 0 && <p className="sharing-empty">No terms match “{query}”.</p>}
      <div className="glossary-list">
        {terms.map((t) => {
          const related = (articles ?? []).filter((a) => matchesTerm(t, a.title, a.tags));
          const sessions = (workshops ?? []).filter((w) => new Date(w.endsAt).getTime() > now && matchesTerm(t, w.title, [], w.description));
          return (
            <section
              key={t.id}
              id={"term-" + t.id}
              className={"panel glossary-entry" + (t.id === target ? " is-target" : "")}
              aria-labelledby={"term-title-" + t.id}
            >
              <h2 id={"term-title-" + t.id}>
                {t.term}
                {t.teachWell && <span className="tag">IN TEACHWELL</span>}
                {t.origin === "college" && <span className="tag">ADDED BY YOUR CTL</span>}
                {t.origin === "edited" && <span className="tag">WORDED BY YOUR CTL</span>}
              </h2>
              {editing === t.id ? (
                <TermEditor
                  term={t}
                  takenIds={takenIds}
                  onCancel={() => setEditing(null)}
                  onSaved={() => {
                    setEditing(null);
                    glossary.reload();
                    setNotice(`Saved “${t.term}”.`);
                  }}
                />
              ) : (
                <>
                  <p>{t.definition}</p>
                  {t.links.length > 0 && (
                    <>
                      <h3>Learn more</h3>
                      <ul className="glossary-links">
                        {t.links.map((l) => (
                          <li key={l.url}>
                            <OutsideLink link={l} />
                          </li>
                        ))}
                      </ul>
                    </>
                  )}
                  <h3>From your CTL</h3>
                  {articles === null && !error ? (
                    <p className="fine" role="status">
                      Loading your CTL's resources…
                    </p>
                  ) : related.length + sessions.length > 0 ? (
                    <ul className="glossary-links">
                      {related.map((a) => (
                        <li key={a.id}>
                          <button type="button" className="glossary-ctl-link" onClick={() => onOpenArticle(a.slug)}>
                            {a.title}
                          </button>
                          <small> · knowledge base{a.status === "Draft" ? " · draft, only the CTL sees it" : ""}</small>
                        </li>
                      ))}
                      {sessions.map((w) => (
                        <li key={w.id}>
                          <button type="button" className="glossary-ctl-link" onClick={onOpenWorkshops}>
                            {w.title}
                          </button>
                          <small> · workshop on {new Date(w.startsAt).toLocaleDateString()}</small>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    !error && <p className="fine">Your CTL has not published an article or workshop on this yet.</p>
                  )}
                  {!isCtl && (
                    <button type="button" className="secondary" onClick={() => onAskCtl({ title: `Question about ${t.term}`, category: supportCategory(t) })}>
                      <LifeBuoy size={16} aria-hidden="true" /> Ask the CTL about this
                    </button>
                  )}
                  {isCtl && (
                    <>
                      <p className="fine">
                        To list a knowledge article here, tag it <code>{t.id}</code> or <code>{t.term.toLowerCase()}</code>
                        {t.tags?.length ? (
                          <>
                            {" "}
                            (or{" "}
                            {t.tags.map((x, i) => (
                              <Fragment key={x}>
                                {i > 0 && ", "}
                                <code>{x}</code>
                              </Fragment>
                            ))}
                            )
                          </>
                        ) : null}
                        , or use the term in its title. Workshops are listed when their title or description uses the term.
                        {t.entry && ` Last changed by ${t.entry.updatedByName} on ${new Date(t.entry.updatedAt).toLocaleDateString()}.`}
                      </p>
                      <div className="finding-actions">
                        <button type="button" className="secondary" disabled={busy} onClick={() => setEditing(t.id)}>
                          <Pencil size={16} aria-hidden="true" /> Edit<span className="visually-hidden"> {t.term}</span>
                        </button>
                        {t.origin === "edited" && (
                          <button type="button" className="secondary" disabled={busy} onClick={() => remove(t, `“${t.term}” uses TeachWell's wording again.`)}>
                            Return to TeachWell's wording<span className="visually-hidden"> for {t.term}</span>
                          </button>
                        )}
                        {t.origin === "college" ? (
                          <button type="button" className="secondary" disabled={busy} onClick={() => remove(t, `Removed “${t.term}”.`)}>
                            Remove<span className="visually-hidden"> {t.term}</span>
                          </button>
                        ) : (
                          <button type="button" className="secondary" disabled={busy} onClick={() => hide(t)}>
                            <EyeOff size={16} aria-hidden="true" /> Hide from instructors<span className="visually-hidden">: {t.term}</span>
                          </button>
                        )}
                      </div>
                    </>
                  )}
                </>
              )}
            </section>
          );
        })}
      </div>
      {isCtl && hiddenTerms.length > 0 && (
        <section className="panel glossary-hidden" aria-labelledby="glossary-hidden-title">
          <h2 id="glossary-hidden-title">Hidden terms</h2>
          <p className="fine">Instructors at your college do not see these, and they are not underlined anywhere.</p>
          <ul className="glossary-links">
            {hiddenTerms.map((t) => (
              <li key={t.id}>
                {t.term}{" "}
                <button type="button" className="glossary-ctl-link" disabled={busy} onClick={() => showAgain(t)}>
                  Show again<span className="visually-hidden"> {t.term}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}
