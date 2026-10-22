import { useEffect, useState, type FormEvent } from "react";
import { Upload } from "lucide-react";
import { ApiError, request, uploadWithProgress, type CourseExportImport } from "./api";
import { Collapsible } from "./Collapsible";

interface Props {
  /** Refresh this course; omit to import a new course (or refresh the caller's earlier import of the same export). */
  courseId?: string;
  onImported: (courseId: string) => void;
  onSessionExpired: (error: unknown) => void;
  /** When set (the public demo), the steps stay visible but the upload form is replaced by this explanation. */
  disabledReason?: string;
}

export function CourseExportUpload({ courseId, onImported, onSessionExpired, disabledReason }: Props) {
  const [file, setFile] = useState<File | null>(null);
  const [canvasUrl, setCanvasUrl] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<CourseExportImport | null>(null);
  // Null while sending nothing; 0–1 while uploading; 1 once the server is reading the export.
  const [progress, setProgress] = useState<number | null>(null);
  // The server's configured limit; until it loads, the server still enforces it.
  const [maxBytes, setMaxBytes] = useState<number | null>(null);
  useEffect(() => {
    request<{ maxUploadBytes: number }>("/course-exports/limits")
      .then((l) => setMaxBytes(l.maxUploadBytes))
      .catch(() => setMaxBytes(null));
  }, []);
  const limitMb = maxBytes === null ? null : Math.floor(maxBytes / (1024 * 1024));
  const tooLarge = file !== null && maxBytes !== null && file.size > maxBytes;
  const sizeMb = (bytes: number) => (bytes / (1024 * 1024)).toLocaleString(undefined, { maximumFractionDigits: 1 });

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!file) return;
    setBusy(true);
    setError("");
    setResult(null);
    const form = new FormData();
    form.append("file", file);
    if (canvasUrl.trim()) form.append("canvasCourseUrl", canvasUrl.trim());
    setProgress(0);
    try {
      setResult(await uploadWithProgress<CourseExportImport>(courseId ? `/courses/${courseId}/course-exports` : "/course-exports", form, setProgress));
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) onSessionExpired(err);
      else setError((err as Error).message);
    } finally {
      setBusy(false);
      setProgress(null);
    }
  }

  return (
    <Collapsible
      id={courseId ? "refresh-export" : "import-export"}
      title={courseId ? "Bring in a newer copy from Canvas" : "Import a course export"}
      icon={<Upload size={22} />}
      className="sharing-panel export-upload"
      busy={busy}
      defaultOpen={!courseId}
      description={
        courseId
          ? "Changed the course in Canvas? Bring in a newer export of the same course to see the changes here. Earlier reviews keep the version they looked at."
          : "Bring a copy of your Canvas course into TeachWell. It takes a few minutes and changes nothing in Canvas."
      }
    >
      {/* Step by step, for instructors who have never exported a course (ADR 016). */}
      <ol className="export-steps">
        <li>In Canvas, open your course.</li>
        <li>
          In the course menu on the left, select <strong>Settings</strong>.
        </li>
        <li>
          On the right side of the page, select <strong>Export Course Content</strong>.
        </li>
        <li>
          Choose <strong>Course</strong>, then select <strong>Create Export</strong>. A large course can take a few minutes.
        </li>
        <li>
          When it finishes, select the new export link. Your browser downloads a file whose name ends in <strong>.imscc</strong>.
        </li>
        <li>
          Back here, choose that file below and select <strong>{courseId ? "Upload and refresh" : "Upload and import"}</strong>.
        </li>
      </ol>
      <p className="fine export-help">Stuck at any step? Ask the CTL from Support; they can walk you through it.</p>
      {disabledReason ? (
        <p role="note">
          {disabledReason}
        </p>
      ) : (
      <form className="export-form" onSubmit={submit}>
        <label>
          Course export (.imscc{limitMb !== null ? `, up to ${limitMb.toLocaleString()} MB` : ""})
          <input
            type="file"
            accept=".imscc,.zip"
            required
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setResult(null);
            }}
          />
        </label>
        <label>
          Canvas course address (optional, for links back to Canvas)
          <input
            type="url"
            inputMode="url"
            placeholder="https://canvas.example.edu/courses/12345"
            maxLength={500}
            value={canvasUrl}
            onChange={(e) => setCanvasUrl(e.target.value)}
          />
        </label>
        {file && !tooLarge && <p className="fine">Selected: {sizeMb(file.size)} MB</p>}
        {tooLarge && (
          <p className="connection-warning">
            This file is {sizeMb(file!.size)} MB, larger than the {limitMb!.toLocaleString()} MB limit.
          </p>
        )}
        {progress !== null && (
          <div role="status" className="upload-progress">
            <progress max={1} value={progress < 1 ? progress : undefined} aria-label="Upload progress" />
            <span>{progress < 1 ? `Uploading… ${Math.round(progress * 100)}%` : "Uploaded. Reading the export and importing pages and assignments…"}</span>
          </div>
        )}
        <button disabled={busy || !file || tooLarge}>{busy ? "Importing…" : courseId ? "Upload and refresh" : "Upload and import"}</button>
      </form>
      )}
      <p className="fine">
        Your file is deleted as soon as the import finishes; TeachWell keeps only the course's pages, assignments and structure. Quizzes,
        discussions, files and videos are listed but not copied, so a course with lots of video is fine. This works with Canvas's standard export
        and has not yet been tried with every Canvas version.
      </p>
      {error && (
        <div className="error" role="alert">
          {error}
        </div>
      )}
      {result && (
        <div className="success" role="status">
          <p>
            {result.created ? "Imported" : "Refreshed"} <strong>{result.title}</strong>
            {result.code && ` (${result.code})`} · {result.elementCount} items.
          </p>
          {result.warnings.length > 0 && (
            <ul className="import-warnings-list">
              {result.warnings.map((w) => (
                <li key={w}>{w}</li>
              ))}
            </ul>
          )}
          <button onClick={() => onImported(result.courseId)}>{courseId ? "Reload course" : "Open course"}</button>
        </div>
      )}
    </Collapsible>
  );
}
