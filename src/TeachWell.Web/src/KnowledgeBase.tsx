import { useEffect, useState, type FormEvent } from "react";
import { BookOpen } from "lucide-react";
import { ApiError, request, type KnowledgeArticle, type KnowledgeArticleSummary } from "./api";
import "./Support.css";

export function KnowledgeBase({
  isCtl,
  initialSlug = null,
  onSessionExpired,
}: {
  isCtl: boolean;
  /** Opens this article straight away, e.g. from the glossary. */
  initialSlug?: string | null;
  onSessionExpired: (error: unknown) => void;
}) {
  const [articles, setArticles] = useState<KnowledgeArticleSummary[]>([]);
  const [open, setOpen] = useState<KnowledgeArticle | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [creating, setCreating] = useState(false);
  const [newTitle, setNewTitle] = useState("");
  const [newBody, setNewBody] = useState("");
  const [title, setTitle] = useState(""), [body, setBody] = useState(""), [tags, setTags] = useState(""), [status, setStatus] = useState("Draft");

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    request<KnowledgeArticleSummary[]>("/knowledge")
      .then((data) => {
        if (active) setArticles(data);
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
  }, [retry]);

  useEffect(() => {
    if (initialSlug) openSlug(initialSlug);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialSlug]);

  function select(article: KnowledgeArticle) {
    setOpen(article);
    setTitle(article.title);
    setBody(article.body);
    setTags(article.tags.join(", "));
    setStatus(article.status);
  }
  async function openSlug(slug: string) {
    setSaving(true);
    setError("");
    try {
      select(await request<KnowledgeArticle>(`/knowledge/${slug}`));
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  function tagList(value: string) {
    return value
      .split(",")
      .map((t) => t.trim())
      .filter(Boolean);
  }
  async function create(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError("");
    try {
      const article = await request<KnowledgeArticle>("/knowledge", { title: newTitle, body: newBody, tags: [] }, "POST");
      setNewTitle("");
      setNewBody("");
      setCreating(false);
      setArticles(await request<KnowledgeArticleSummary[]>("/knowledge"));
      select(article);
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }
  async function save(e: FormEvent) {
    e.preventDefault();
    if (!open) return;
    setSaving(true);
    setError("");
    setNotice("");
    try {
      const saved = await request<KnowledgeArticle>(`/knowledge/${open.id}`, { revision: open.revision, title, body, tags: tagList(tags), status }, "PUT");
      select(saved);
      setArticles(await request<KnowledgeArticleSummary[]>("/knowledge"));
      setNotice(saved.status === "Published" ? "Article published." : "Draft saved.");
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <span className="eyebrow">JOB AIDS</span>
      <h1>Knowledge base</h1>
      <p className="intro">
        {isCtl ? "Draft, edit and publish short job aids CTL can recommend from a support ticket." : "Published job aids from the teaching and learning center."}
      </p>
      {error && (
        <div className="error" role="alert">
          {error}
          <button disabled={saving} onClick={() => setRetry((x) => x + 1)}>
            Refresh
          </button>
        </div>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {loading && <p role="status">Loading articles…</p>}
      {!loading && articles.length === 0 && !error && (
        <section className="panel">
          <h2>No articles yet</h2>
          <p>{isCtl ? "Create the first one below." : "Check back later."}</p>
        </section>
      )}
      <div className="rubric-layout">
        <section className="panel rubric-list" aria-label="Knowledge articles">
          <ul>
            {articles.map((a) => (
              <li key={a.id}>
                <button className="link-button" aria-current={open?.id === a.id ? "true" : undefined} disabled={saving} onClick={() => openSlug(a.slug)}>
                  {a.title} {a.status === "Draft" && <span className="tag">DRAFT</span>}
                </button>
              </li>
            ))}
          </ul>
          {isCtl &&
            (creating ? (
              <form className="new-rubric" onSubmit={create}>
                <h3>New article</h3>
                <label>
                  Title
                  <input required maxLength={160} value={newTitle} onChange={(e) => setNewTitle(e.target.value)} />
                </label>
                <label>
                  Body
                  <textarea required maxLength={20000} rows={4} value={newBody} onChange={(e) => setNewBody(e.target.value)} />
                </label>
                <div className="finding-actions">
                  <button disabled={saving}>Create draft</button>
                  <button type="button" className="secondary" onClick={() => setCreating(false)}>
                    Cancel
                  </button>
                </div>
              </form>
            ) : (
              <button className="secondary" onClick={() => setCreating(true)}>
                New article
              </button>
            ))}
        </section>
        <section className="rubric-detail">
          {!open ? (
            <section className="panel">
              <BookOpen />
              <h2>Choose an article</h2>
              <p>Select an article to read it{isCtl ? " or edit it" : ""}.</p>
            </section>
          ) : isCtl ? (
            <form className="panel criterion-form" onSubmit={save}>
              <span className="tag">{open.status.toUpperCase()} · V{open.version}</span>
              <label>
                Title
                <input required maxLength={160} value={title} onChange={(e) => setTitle(e.target.value)} />
              </label>
              <label>
                Body
                <textarea required maxLength={20000} rows={8} value={body} onChange={(e) => setBody(e.target.value)} />
              </label>
              <label>
                Tags (comma separated)
                <input maxLength={400} value={tags} onChange={(e) => setTags(e.target.value)} />
              </label>
              <label>
                Status
                <select value={status} onChange={(e) => setStatus(e.target.value)}>
                  <option value="Draft">Draft</option>
                  <option value="Published">Published</option>
                </select>
              </label>
              <button disabled={saving}>Save</button>
            </form>
          ) : (
            <article className="panel">
              <h2>{open.title}</h2>
              <p className="fine">By {open.authorName}{open.publishedAt && ` · Published ${new Date(open.publishedAt).toLocaleDateString()}`}</p>
              <p className="article-body">{open.body}</p>
              {open.tags.length > 0 && (
                <p className="tag-list">
                  {open.tags.map((t) => (
                    <span className="tag" key={t}>
                      {t.toUpperCase()}
                    </span>
                  ))}
                </p>
              )}
            </article>
          )}
        </section>
      </div>
    </>
  );
}
