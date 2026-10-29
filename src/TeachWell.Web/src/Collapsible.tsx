import { useEffect, useState, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import "./Collapsible.css";

// A course-page section that can be folded away. The heading stays a real heading (for screen-reader navigation); a separate
// button carries the expanded state, and clicking anywhere on the header row toggles it too. The choice is remembered per
// section in this browser only (a convenience; nothing breaks if storage is unavailable).
const OPEN_EVENT = "teachwell:open-section";

/** Opens a section if it is folded, then scrolls to it. Used by "On this page" links and the Start here checklist. */
export function goToSection(id: string) {
  window.dispatchEvent(new CustomEvent(OPEN_EVENT, { detail: id }));
  // Wait a frame so an opened section has its full height before scrolling.
  requestAnimationFrame(() => document.getElementById(`${id}-title`)?.scrollIntoView({ block: "start", behavior: "smooth" }));
}

export function Collapsible({
  id,
  title,
  icon,
  meta,
  description,
  defaultOpen = true,
  className = "sharing-panel",
  busy,
  onOpen,
  children,
}: {
  id: string;
  title: string;
  icon?: ReactNode;
  /** One-line summary shown even when collapsed, e.g. "3 reviews · 1 draft". */
  meta?: ReactNode;
  description?: ReactNode;
  defaultOpen?: boolean;
  className?: string;
  busy?: boolean;
  /**
   * Called whenever the section is open, including on mount when it was remembered open. Lets a section whose
   * contents are expensive to produce fetch them only once someone actually looks (it may fire more than once, so
   * the caller guards against repeating the work).
   */
  onOpen?: () => void;
  children: ReactNode;
}) {
  const key = "teachwell.section." + id;
  const [open, setOpen] = useState(() => {
    try {
      const saved = localStorage.getItem(key);
      return saved === null ? defaultOpen : saved === "open";
    } catch {
      return defaultOpen;
    }
  });
  useEffect(() => {
    const listener = (e: Event) => {
      if ((e as CustomEvent<string>).detail === id) setOpen(true);
    };
    window.addEventListener(OPEN_EVENT, listener);
    return () => window.removeEventListener(OPEN_EVENT, listener);
  }, [id]);
  useEffect(() => {
    if (open) onOpen?.();
    // onOpen is intentionally not a dependency: callers pass an inline closure, and this should follow the section's
    // open state rather than re-firing on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  function toggle() {
    setOpen((current) => {
      try {
        localStorage.setItem(key, current ? "closed" : "open");
      } catch {
        /* storage unavailable: the section still toggles */
      }
      return !current;
    });
  }
  return (
    <section className={`collapsible ${className}${open ? "" : " is-collapsed"}`} aria-labelledby={`${id}-title`} aria-busy={busy}>
      <div className="collapsible-head" onClick={(e) => (e.target as HTMLElement).closest("button") === null && toggle()}>
        {icon && <span className="collapsible-icon">{icon}</span>}
        <div className="collapsible-titles">
          <h2 id={`${id}-title`}>{title}</h2>
          {meta && <p className="collapsible-meta">{meta}</p>}
        </div>
        <button type="button" className="collapsible-toggle" aria-expanded={open} aria-controls={`${id}-body`} aria-label={`${open ? "Collapse" : "Expand"} ${title}`} onClick={toggle}>
          <ChevronDown size={18} aria-hidden="true" />
        </button>
      </div>
      <div id={`${id}-body`} className="collapsible-body" hidden={!open}>
        {description && <p className="collapsible-description">{description}</p>}
        {children}
      </div>
    </section>
  );
}
