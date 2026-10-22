import { useEffect, useState, type FormEvent } from "react";
import { ClipboardList } from "lucide-react";
import {
  ApiError,
  request,
  type CourseDetail,
  type Review,
  type ReviewSummary,
  type RubricProfile,
  isContent,
  typeLabel,
} from "./api";
import "./Reviews.css";
import { Collapsible } from "./Collapsible";

interface Props {
  course: CourseDetail;
  reviewerView: boolean;
  onOpen: (reviewId: string) => void;
  onSessionExpired: (error: unknown) => void;
}

export function CourseReviews({ course, reviewerView, onOpen, onSessionExpired }: Props) {
  const [reviews, setReviews] = useState<ReviewSummary[]>([]);
  const [rubrics, setRubrics] = useState<RubricProfile[]>([]);
  const [element, setElement] = useState("");
  const [version, setVersion] = useState("");
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const canStart = reviewerView && course.permissions.canReview;
  const reviewable = course.elements.filter((e) => isContent(e.type));
  // Canvas often reuses one internal title (e.g. eight quizzes called "Module Reflection"); the module item's title tells them apart.
  const placedAs = (e: { id: string; title: string }) =>
    course.elements.find((m) => m.type === "moduleItem" && m.targetElementId === e.id && m.title !== e.title)?.title;
  const published = rubrics.flatMap((p) =>
    p.versions
      .filter((v) => v.status === "Published")
      .map((v) => ({ id: v.id, label: `${p.name} v${v.versionNumber}`, types: v.elementTypes ?? [] })),
  );
  const elementVersions = published.filter((v) => v.types.includes("page") || v.types.includes("assignment"));
  const courseVersions = published.filter((v) => v.types.includes("course"));
  const [courseVersion, setCourseVersion] = useState("");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    Promise.all([
      request<ReviewSummary[]>(`/courses/${course.id}/reviews`),
      canStart ? request<RubricProfile[]>("/rubrics") : Promise.resolve([]),
    ])
      .then(([list, profiles]) => {
        if (!active) return;
        setReviews(list);
        setRubrics(profiles);
        const live = profiles.flatMap((p) => p.versions.filter((v) => v.status === "Published"));
        const first = live.find((v) => (v.elementTypes ?? []).some((t) => t === "page" || t === "assignment"));
        const firstCourse = live.find((v) => (v.elementTypes ?? []).includes("course"));
        setVersion((current) => current || first?.id || "");
        setCourseVersion((current) => current || firstCourse?.id || "");
      })
      .catch((e) => {
        if (active) fail(e);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [course.id, canStart, retry]);

  async function startCourse(e: FormEvent) {
    e.preventDefault();
    setStarting(true);
    setError("");
    try {
      const review = await request<Review>(`/courses/${course.id}/course-reviews`, { rubricVersionId: courseVersion });
      onOpen(review.id);
    } catch (err) {
      fail(err);
    } finally {
      setStarting(false);
    }
  }

  async function start(e: FormEvent) {
    e.preventDefault();
    setStarting(true);
    setError("");
    try {
      const review = await request<Review>(`/elements/${element}/reviews`, { rubricVersionId: version });
      onOpen(review.id);
    } catch (err) {
      fail(err);
    } finally {
      setStarting(false);
    }
  }

  const visible = reviewerView ? reviews : reviews.filter((r) => r.status === "Published");
  return (
    <Collapsible
      id="reviews"
      title={reviewerView ? "Reviews" : "Published reviews"}
      icon={<ClipboardList size={22} />}
      className="sharing-panel course-reviews"
      busy={loading || starting}
      meta={
        loading
          ? "Loading…"
          : `${visible.length} ${visible.length === 1 ? "review" : "reviews"}` +
            (reviewerView ? ` · ${visible.filter((r) => r.status === "Draft").length} draft` : "")
      }
      description={
        reviewerView
          ? "Review a page or assignment, or the whole course, against a published rubric version. Drafts stay private until you publish."
          : "Published CTL reviews of this course. Each review is pinned to the content captured when it began."
      }
    >
      {loading && <p role="status">Loading reviews…</p>}
      {error && (
        <div className="error" role="alert">
          {error}
          <button disabled={starting} onClick={() => setRetry((x) => x + 1)}>
            Refresh reviews
          </button>
        </div>
      )}
      {!loading && canStart && (
        <form className="start-review" onSubmit={start}>
          <label>
            Element
            <select required value={element} onChange={(e) => setElement(e.target.value)}>
              <option value="">Choose a page, assignment, quiz or discussion</option>
              {reviewable.map((e) => (
                <option key={e.id} value={e.id}>
                  {typeLabel(e.type)} · {placedAs(e) ? `${placedAs(e)} (${e.title})` : e.title}
                </option>
              ))}
            </select>
          </label>
          <label>
            Rubric version
            <select required value={version} onChange={(e) => setVersion(e.target.value)}>
              {elementVersions.length === 0 && <option value="">No published page or assignment rubrics</option>}
              {elementVersions.map((v) => (
                <option key={v.id} value={v.id}>
                  {v.label}
                </option>
              ))}
            </select>
          </label>
          <button disabled={starting || !element || !version}>{starting ? "Starting…" : "Start review"}</button>
        </form>
      )}
      {!loading && canStart && (
        <form className="start-review" onSubmit={startCourse}>
          <label>
            Whole course
            <span className="fine">Structure, alignment, pacing and design consistency across every module.</span>
          </label>
          <label>
            Course-level rubric
            <select required value={courseVersion} onChange={(e) => setCourseVersion(e.target.value)}>
              {courseVersions.length === 0 && <option value="">No published course-level rubrics</option>}
              {courseVersions.map((v) => (
                <option key={v.id} value={v.id}>
                  {v.label}
                </option>
              ))}
            </select>
          </label>
          <button disabled={starting || !courseVersion}>{starting ? "Starting…" : "Start course review"}</button>
        </form>
      )}
      {!loading && !error && visible.length === 0 && (
        <p className="sharing-empty">
          {reviewerView ? "No reviews of this course yet." : "No published reviews yet. CTL reviewers publish findings here when ready."}
        </p>
      )}
      {visible.length > 0 && (
        <ul className="review-list">
          {visible.map((r) => (
            <li key={r.id}>
              <div>
                <span className="tag">{r.status === "Draft" ? "YOUR DRAFT" : "PUBLISHED"}</span>{" "}
                <span className="tag">{r.elementType === "course" ? "WHOLE COURSE" : r.elementType.toUpperCase()}</span>
                <strong>{r.elementTitle}</strong>
                <small>
                  {r.rubricName} v{r.rubricVersion} · {r.reviewerName} · {r.coveragePercent}% coverage · {r.confirmedFindings} confirmed
                  findings · {new Date(r.publishedAt ?? r.startedAt).toLocaleDateString()}
                </small>
              </div>
              <button onClick={() => onOpen(r.id)} aria-label={`Open review of ${r.elementTitle}`}>
                Open
              </button>
            </li>
          ))}
        </ul>
      )}
    </Collapsible>
  );
}
