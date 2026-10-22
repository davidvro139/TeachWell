import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, Layers } from "lucide-react";
import { request, typeLabel, type CourseDetail, type Element } from "./api";
import { Collapsible, goToSection } from "./Collapsible";
import "./Reviews.css";

const PREVIEW_CHARS = 280;
const OTHER = "other-content";
const countWords = (text: string) => (text.match(/[\p{L}\p{N}]+/gu) ?? []).length;
const plural = (n: number, one: string, many = one + "s") => `${n.toLocaleString()} ${n === 1 ? one : many}`;
const VIEW_KEY = "teachwell.contentView";
const BATCH = 100;
const CONTENT_TYPES = new Set(["page", "assignment", "quiz", "discussion"]);

// Server-generated, allowlisted markup (CanvasText.Preview): structural tags, encoded text and a fixed set of formatting classes; no
// scripts, inline styles, URLs or images. Long items are clipped until expanded.
function FormattedContent({ html }: { html: string }) {
  const [expanded, setExpanded] = useState(false);
  const long = html.length > 1500;
  return (
    <>
      <div className={"page-preview item-preview" + (long && !expanded ? " is-clipped" : "")} dangerouslySetInnerHTML={{ __html: html }} />
      {long && (
        <button type="button" className="link-button" aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>
          {expanded ? "Show less" : "Show more"}
        </button>
      )}
    </>
  );
}

/** The top of a course page: which course this is, where TeachWell's copy came from, and links to each section below. */
export function CourseHeader({ course, sections }: { course: CourseDetail; sections: { id: string; label: string }[] }) {
  const copiedOn = course.lastSyncedAt ? new Date(course.lastSyncedAt).toLocaleDateString(undefined, { dateStyle: "long" }) : null;
  return (
    // A plain container: the global "header" styles belong to the top bar.
    <div className="course-header">
      <span className="eyebrow">
        {course.code} ·{" "}
        {course.sourceType === "Demo" ? "FICTIONAL DEMO COURSE" : course.sourceType === "CanvasExport" ? "FROM A CANVAS EXPORT" : "FROM CANVAS"}
      </span>
      <h1>{course.title}</h1>
      <p className="intro">
        {course.sourceType === "Demo"
          ? "A made-up course for trying TeachWell. "
          : `TeachWell is looking at a copy of this course${copiedOn ? ` from ${copiedOn}` : ""}. Make changes in Canvas, then bring the course in again to see them here. `}
        Nothing you do in TeachWell changes your Canvas course.
      </p>
      {sections.length > 1 && (
        <nav className="course-sections" aria-label="On this page">
          <span>On this page:</span>
          {sections.map((s) => (
            <button key={s.id} type="button" className="link-button" onClick={() => goToSection(s.id)}>
              {s.label}
            </button>
          ))}
        </nav>
      )}
    </div>
  );
}

export function CourseContent({ course }: { course: CourseDetail }) {
  const byId = useMemo(() => new Map(course.elements.map((e) => [e.id, e])), [course.elements]);
  const modules = course.elements.filter((e) => e.type === "module");
  const targets = new Set(course.elements.map((e) => e.targetElementId).filter(Boolean));
  const other = course.elements.filter((e) => e.type !== "module" && !e.parentElementId && !targets.has(e.id));
  const childrenOf = (module: Element) => course.elements.filter((e) => e.parentElementId === module.id);
  const contentOf = (reference: Element) => (reference.targetElementId ? (byId.get(reference.targetElementId) ?? reference) : reference);

  // Small courses open fully, as before; large ones start folded so the page stays scannable.
  const [open, setOpen] = useState<Set<string>>(() => new Set(modules.length <= 3 ? modules.map((m) => m.id) : []));
  const [filter, setFilter] = useState("");
  const query = filter.trim().toLowerCase();
  // Formatted (Canvas-like) or plain text; remembered in this browser only.
  const [view, setView] = useState<"formatted" | "text">(() => {
    try {
      return localStorage.getItem(VIEW_KEY) === "text" ? "text" : "formatted";
    } catch {
      return "formatted";
    }
  });
  const chooseView = (next: "formatted" | "text") => {
    setView(next);
    try {
      localStorage.setItem(VIEW_KEY, next);
    } catch {
      /* storage unavailable: the choice applies to this visit */
    }
  };
  // Previews by content element id; null means none was captured. Loaded in batches as modules open.
  const [previews, setPreviews] = useState<Record<string, string | null>>({});
  const requested = useRef(new Set<string>());
  const matches = (reference: Element) => !query || reference.title.toLowerCase().includes(query) || contentOf(reference).title.toLowerCase().includes(query);

  const counts = useMemo(() => {
    const pages = course.elements.filter((e) => e.type === "page").length;
    const assignments = course.elements.filter((e) => e.type === "assignment").length;
    const quizzes = course.elements.filter((e) => e.type === "quiz").length;
    const discussions = course.elements.filter((e) => e.type === "discussion").length;
    return [
      plural(modules.length, "module"),
      plural(pages, "page"),
      plural(assignments, "assignment"),
      ...(quizzes ? [plural(quizzes, "quiz", "quizzes")] : []),
      ...(discussions ? [plural(discussions, "discussion")] : []),
    ].join(" · ");
  }, [course.elements, modules.length]);

  function sourceLink(element: Element) {
    if (!element.sourceUrl || !course.sourceOrigin) return null;
    try {
      const url = new URL(element.sourceUrl),
        origin = new URL(course.sourceOrigin);
      return url.protocol === "https:" && url.origin === origin.origin ? (
        <a className="source-link" href={url.href} target="_blank" rel="noopener noreferrer">
          Open source in Canvas ↗
        </a>
      ) : null;
    } catch {
      return null;
    }
  }

  // Canvas text headers label sections inside a module. Imports before this change stored them as plain module items with no target
  // and no warning, so those are recognised too.
  const isHeader = (e: Element) =>
    e.type === "moduleItem" && (e.sourceStatus === "TextHeader" || (!e.targetElementId && !e.sourceWarning && e.sourceStatus !== "Unavailable"));

  // Content ids of items currently on screen (open modules, open "other content", or search results).
  const visibleContentIds = (() => {
    const ids: string[] = [];
    const add = (items: Element[]) =>
      items.filter(matches).forEach((r) => {
        const c = contentOf(r);
        if (CONTENT_TYPES.has(c.type)) ids.push(c.id);
      });
    for (const m of modules) if (query || open.has(m.id)) add(childrenOf(m));
    if (query || open.has(OTHER)) add(other);
    return [...new Set(ids)];
  })();
  const wanted = visibleContentIds.filter((id) => !requested.current.has(id)).join(",");
  useEffect(() => {
    if (view !== "formatted" || !wanted) return;
    const ids = wanted.split(",");
    ids.forEach((id) => requested.current.add(id));
    for (let i = 0; i < ids.length; i += BATCH)
      request<Record<string, string | null>>(`/courses/${course.id}/previews?ids=${ids.slice(i, i + BATCH).join(",")}`)
        .then((batch) => setPreviews((current) => ({ ...current, ...batch })))
        // Formatting is an enhancement: on failure the plain text stays, and the next visit retries.
        .catch(() => ids.slice(i, i + BATCH).forEach((id) => requested.current.delete(id)));
  }, [view, wanted, course.id]);

  function item(reference: Element) {
    if (isHeader(reference))
      return (
        <h4 className="module-text-header" key={reference.id}>
          {reference.title}
        </h4>
      );
    const content = contentOf(reference);
    const status = reference.sourceStatus && reference.sourceStatus !== "Published" ? reference.sourceStatus : content.sourceStatus;
    const text = content.plainText ?? "";
    const long = text.length > PREVIEW_CHARS;
    return (
      <div className="element item-row" key={reference.id}>
        <span className="eyebrow">{typeLabel(content.type).toLowerCase()}</span>
        {status && status !== "Published" && <span className="tag item-status">{status}</span>}
        <h4>{reference.title}</h4>
        {(reference.sourceWarning || content.sourceWarning) && <p className="connection-warning">{reference.sourceWarning ?? content.sourceWarning}</p>}
        {view === "formatted" && previews[content.id] ? (
          <FormattedContent html={previews[content.id]!} />
        ) : !text ? (
          <p className="fine">{status === "Unavailable" ? "Content was not available for import." : "No text content."}</p>
        ) : long ? (
          <details className="item-text">
            <summary>
              {text.slice(0, PREVIEW_CHARS).trimEnd()}… <strong>Show full text ({plural(countWords(text), "word")})</strong>
            </summary>
            <p>{text}</p>
          </details>
        ) : (
          <p>{text}</p>
        )}
        {view === "formatted" && previews[content.id] === null && text && (
          <p className="fine">No formatted version was captured for this item; re-import the course to capture one.</p>
        )}
        {sourceLink(content)}
      </div>
    );
  }

  function moduleSummary(children: Element[]) {
    const kinds = new Map<string, number>();
    let notImported = 0;
    for (const child of children) {
      if (isHeader(child)) continue;
      const content = contentOf(child);
      if (content.type === "moduleItem") notImported++;
      else kinds.set(content.type, (kinds.get(content.type) ?? 0) + 1);
    }
    const parts = [...kinds].map(([type, n]) => plural(n, type, type === "quiz" ? "quizzes" : undefined));
    if (notImported) parts.push(`${notImported} not imported`);
    return parts.length ? parts.join(" · ") : "Empty";
  }

  const toggle = (id: string) =>
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <>
      {course.warnings && course.warnings.length > 0 && (
        <Collapsible id="import-limitations" title="Import limitations" className="panel import-warnings" defaultOpen={false} meta={plural(course.warnings.length, "note")}>
          <ul>
            {course.warnings.map((warning) => (
              <li key={warning}>{warning}</li>
            ))}
          </ul>
        </Collapsible>
      )}
      {course.elements.length === 0 ? (
        <div className="panel">
          <h2>Nothing to show yet</h2>
          <p>TeachWell did not find any modules, pages or assignments in this course. If that seems wrong, bring the course in again or ask the CTL.</p>
        </div>
      ) : (
        <Collapsible id="course-content" title="Course content" icon={<Layers size={22} />} meta={counts} className="panel course-content">
          <div className="content-toolbar">
            <input type="search" aria-label="Find a page or assignment" placeholder="Find a page or assignment…" value={filter} onChange={(e) => setFilter(e.target.value)} />
            <button type="button" className="secondary" onClick={() => setOpen(new Set([...modules.map((m) => m.id), OTHER]))}>
              Expand all modules
            </button>
            <button type="button" className="secondary" onClick={() => setOpen(new Set())}>
              Collapse all
            </button>
            <div className="view-toggle" role="group" aria-label="Content view">
              <button type="button" aria-pressed={view === "formatted"} className={view === "formatted" ? "" : "secondary"} onClick={() => chooseView("formatted")}>
                Formatted
              </button>
              <button type="button" aria-pressed={view === "text"} className={view === "text" ? "" : "secondary"} onClick={() => chooseView("text")}>
                Plain text
              </button>
            </div>
          </div>
          <section className="elements">
            {modules.map((module) => {
              const children = childrenOf(module);
              const visible = children.filter(matches);
              if (query && visible.length === 0 && !module.title.toLowerCase().includes(query)) return null;
              // While searching, modules with matches open so the results are visible.
              const expanded = query ? true : open.has(module.id);
              return (
                <article key={module.id} className="module-block">
                  <div className="module-head" onClick={(e) => (e.target as HTMLElement).closest("button, a") === null && !query && toggle(module.id)}>
                    <div>
                      <span className="tag">MODULE · {module.sourceStatus ?? "Published"}</span>
                      <h3 className="module-title">{module.title}</h3>
                      <span className="fine">{moduleSummary(children)}</span>
                    </div>
                    <button
                      type="button"
                      className="collapsible-toggle"
                      aria-expanded={expanded}
                      aria-controls={`module-${module.id}`}
                      aria-label={`${expanded ? "Collapse" : "Expand"} module ${module.title}`}
                      disabled={!!query}
                      onClick={() => toggle(module.id)}
                    >
                      <ChevronDown size={18} aria-hidden="true" style={{ transform: expanded ? undefined : "rotate(-90deg)" }} />
                    </button>
                  </div>
                  <div id={`module-${module.id}`} className="module-items" hidden={!expanded}>
                    {module.plainText && <p>{module.plainText}</p>}
                    {module.sourceWarning && <p>{module.sourceWarning}</p>}
                    {sourceLink(module)}
                    {visible.map(item)}
                  </div>
                </article>
              );
            })}
          </section>
          {other.filter(matches).length > 0 &&
            (() => {
              // Content outside any module starts folded (it can be long); a search that matches inside it opens it.
              const expanded = query ? true : open.has(OTHER);
              return (
                <article className="module-block ungrouped-content">
                  <div className="module-head" onClick={(e) => (e.target as HTMLElement).closest("button, a") === null && !query && toggle(OTHER)}>
                    <div>
                      <h3 className="module-title">Other course content</h3>
                      <span className="fine">
                        {moduleSummary(other)} · not placed in any module
                      </span>
                    </div>
                    <button
                      type="button"
                      className="collapsible-toggle"
                      aria-expanded={expanded}
                      aria-controls="module-other-content"
                      aria-label={`${expanded ? "Collapse" : "Expand"} other course content`}
                      disabled={!!query}
                      onClick={() => toggle(OTHER)}
                    >
                      <ChevronDown size={18} aria-hidden="true" style={{ transform: expanded ? undefined : "rotate(-90deg)" }} />
                    </button>
                  </div>
                  <div id="module-other-content" className="module-items" hidden={!expanded}>
                    {other.filter(matches).map(item)}
                  </div>
                </article>
              );
            })()}
        </Collapsible>
      )}
    </>
  );
}
