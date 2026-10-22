import { useEffect, useState } from "react";
import { ShieldCheck, UserPlus } from "lucide-react";
import {
  ApiError,
  request,
  type CourseShare,
  type ReviewerOption,
} from "./api";
import "./CourseSharing.css";
import { Collapsible } from "./Collapsible";

interface Props {
  courseId: string;
  onSessionExpired: (error: unknown) => void;
}

export function CourseSharing({ courseId, onSessionExpired }: Props) {
  const [reviewers, setReviewers] = useState<ReviewerOption[]>([]);
  const [shares, setShares] = useState<CourseShare[]>([]);
  const [selected, setSelected] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const base = `/courses/${courseId}`;

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    setNotice("");
    setSelected("");
    Promise.all([
      request<ReviewerOption[]>(base + "/reviewers"),
      request<CourseShare[]>(base + "/shares"),
    ])
      .then(([options, existing]) => {
        if (active) {
          setReviewers(options);
          setShares(existing);
        }
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
  }, [courseId, retry]);

  async function change(reviewerId: string, revoke: boolean) {
    setSaving(true);
    setError("");
    setNotice("");
    try {
      if (revoke)
        await request(base + "/shares/" + reviewerId, undefined, "DELETE");
      else await request(base + "/shares", { reviewerId });
      setShares(await request<CourseShare[]>(base + "/shares"));
      setSelected("");
      setNotice(
        revoke
          ? "Reviewer access removed. Their next course request will be denied."
          : "Course shared for review. The reviewer can now open it from their CTL dashboard.",
      );
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }

  const available = reviewers.filter(
    (r) => !shares.some((s) => s.userId === r.id),
  );
  return (
    <Collapsible
      id="sharing"
      title="Share with a CTL reviewer"
      icon={<ShieldCheck size={22} />}
      busy={loading || saving}
      meta={loading ? "Loading…" : shares.length === 0 ? "Private to you" : `Shared with ${shares.length} ${shares.length === 1 ? "reviewer" : "reviewers"}`}
      description="You control access to this course. Reviewers can read shared content; they cannot manage sharing."
    >
      {loading && <p role="status">Loading course access…</p>}
      {error && (
        <div className="error" role="alert">
          {error}
          <button disabled={saving} onClick={() => setRetry((x) => x + 1)}>
            Refresh access
          </button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {!loading && !error && (
        <>
          {shares.length === 0 ? (
            <p className="sharing-empty">
              Private to you. No CTL reviewers have access yet.
            </p>
          ) : (
            <ul className="share-list">
              {shares.map((share) => (
                <li key={share.userId}>
                  <div>
                    <strong>{share.displayName}</strong>
                    <span>
                      Review access · shared{" "}
                      {new Date(share.grantedAt).toLocaleDateString()}
                    </span>
                  </div>
                  <button
                    className="remove-access"
                    disabled={saving}
                    onClick={() => change(share.userId, true)}
                    aria-label={`Remove ${share.displayName}'s course access`}
                  >
                    Remove access
                  </button>
                </li>
              ))}
            </ul>
          )}
          {available.length > 0 ? (
            <form
              className="sharing-form"
              onSubmit={(e) => {
                e.preventDefault();
                if (selected) void change(selected, false);
              }}
            >
              <label htmlFor="reviewer-select">
                Reviewer
                <select
                  id="reviewer-select"
                  value={selected}
                  disabled={saving}
                  required
                  onChange={(e) => setSelected(e.target.value)}
                >
                  <option value="">Choose a reviewer</option>
                  {available.map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.displayName}
                    </option>
                  ))}
                </select>
              </label>
              <button disabled={saving || !selected}>
                <UserPlus size={16} />
                {saving ? "Saving…" : "Share for review"}
              </button>
            </form>
          ) : (
            <p className="fine">
              {reviewers.length === 0
                ? "No eligible CTL reviewers are available in your tenant."
                : "All available CTL reviewers already have access."}
            </p>
          )}
        </>
      )}
      <p className="fine">
        Access changes are recorded in the audit trail. Removing access cannot
        recall information already viewed or downloaded.
      </p>
    </Collapsible>
  );
}
