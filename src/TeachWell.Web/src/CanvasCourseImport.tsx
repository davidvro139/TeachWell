import { useState } from "react";
import {
  ApiError,
  request,
  type CanvasCourseOption,
  type CanvasImportResult,
} from "./api";

export function CanvasCourseImport({
  connectionId,
  disabled,
  onSessionExpired,
}: {
  connectionId: string;
  disabled: boolean;
  onSessionExpired: (error: unknown) => void;
}) {
  const [courses, setCourses] = useState<CanvasCourseOption[] | null>(null);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [imported, setImported] = useState<Record<string, boolean>>({});
  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  async function load() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      setCourses(
        await request<CanvasCourseOption[]>(
          `/canvas/connections/${connectionId}/courses`,
        ),
      );
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  async function importCourse(course: CanvasCourseOption) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = await request<CanvasImportResult>("/courses/import", {
        connectionId,
        sourceCourseId: course.id,
      });
      setImported((previous) => ({ ...previous, [course.id]: true }));
      setNotice(
        `${course.title}: ${result.elementCount} elements imported. Open Courses to view the local copy.` +
          (result.warnings.length
            ? ` Import warnings: ${result.warnings.join(" ")}`
            : ""),
      );
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section
      className="canvas-import"
      aria-label="Import Canvas courses"
      aria-busy={busy}
    >
      <h4>Course library</h4>
      <p>
        List courses where Canvas reports your active teacher enrollment, then
        import a private, read-only copy. Modules, pages and assignments are
        supported; unsupported items are labeled. No grades, rosters or
        submissions are requested.
      </p>
      <button disabled={disabled || busy} onClick={load}>
        {busy
          ? "Working…"
          : courses
            ? "Refresh course list"
            : "List available courses"}
      </button>
      {busy && (
        <p role="status">
          Reading Canvas content. Larger imports may take up to 90 seconds…
        </p>
      )}
      {error && (
        <p role="alert" className="error">
          {error} The previous local course copy is preserved if import fails.
        </p>
      )}
      {notice && (
        <p role="status" className="success">
          {notice}
        </p>
      )}
      {courses?.length === 0 && (
        <p>
          No active teacher courses were returned. Other enrollment types are
          not supported by this importer.
        </p>
      )}
      {courses && courses.length > 0 && (
        <ul className="import-options">
          {courses.map((course) => (
            <li key={course.id}>
              <div>
                <strong>{course.title}</strong>
                <small>{course.code}</small>
              </div>
              <button
                disabled={disabled || busy}
                onClick={() => importCourse(course)}
                aria-label={`${imported[course.id] ? "Refresh" : "Import"} ${course.title}`}
              >
                {imported[course.id] ? "Refresh local copy" : "Import course"}
              </button>
            </li>
          ))}
        </ul>
      )}
      <p className="fine">
        Reimporting updates your local copy and keeps earlier snapshots and
        existing shares. Disconnecting Canvas does not remove content already
        imported.
      </p>
    </section>
  );
}
