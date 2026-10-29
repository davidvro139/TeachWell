import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  ArrowRight,
  BadgeCheck,
  BarChart3,
  BookMarked,
  BookOpen,
  ClipboardList,
  Gauge,
  LayoutDashboard,
  Leaf,
  Library,
  LifeBuoy,
  ListChecks,
  LogOut,
  ShieldCheck,
  Upload,
  Users,
  type LucideIcon,
} from "lucide-react";
import {
  ApiError,
  request,
  resetCsrf,
  type Course,
  type CourseDetail,
  type DemoAccount,
  type DemoMode,
  type RevisionReport,
  type Role,
  type User,
} from "./api";
import "./style.css";
import { CourseContent, CourseHeader } from "./CourseContent";
import { goToSection } from "./Collapsible";
import { HowItWorks, StartHere } from "./GettingStarted";
import { CourseSharing } from "./CourseSharing";
import { CanvasConnections } from "./CanvasConnections";
import { CourseExportUpload } from "./CourseExportUpload";
import { CourseReviews } from "./CourseReviews";
import { ReviewWorkspace } from "./ReviewWorkspace";
import { Rubrics } from "./Rubrics";
import { Standards } from "./Standards";
import { CourseStandards } from "./CourseStandards";
import { CourseTasks } from "./CourseTasks";
import { TaskDashboard } from "./TaskDashboard";
import { TaskWorkspace } from "./TaskWorkspace";
import { Support } from "./Support";
import { TicketWorkspace } from "./TicketWorkspace";
import { CourseReports } from "./CourseReports";
import { CourseGuide } from "./CourseGuide";
import { SyllabusPaste } from "./SyllabusPaste";
import { CourseConformance } from "./CourseConformance";
import { AllyReportCheck } from "./AllyReportCheck";
import { CourseReadiness } from "./CourseReadiness";
import { ReadinessQueue } from "./ReadinessQueue";
import { Reports } from "./Reports";
import { Oversight } from "./Oversight";
import { AdminUsers } from "./AdminUsers";
import { AuditLog } from "./AuditLog";
import { Notifications } from "./Notifications";
import { GlossaryNavigation, GlossaryPage, GlossaryProvider } from "./Glossary";
import { taskStatusLabel } from "./labels";

const roleLabel = (r: Role) =>
  r === "CTLReviewer" ? "CTL reviewer" : r === "SystemAdmin" ? "Administrator" : r === "AcademicAdmin" ? "Academic administrator" : r;

type View = "overview" | "canvas" | "rubrics" | "standards" | "readiness" | "oversight" | "tasks" | "support" | "reports" | "glossary";

// One navigation for the whole workspace, grouped and filtered by the role being viewed as.
const navGroups: { label: string; roles: Role[]; items: { view: View; label: string; icon: LucideIcon; roles?: Role[] }[] }[] = [
  {
    label: "COURSES",
    roles: ["Instructor", "CTLReviewer"],
    items: [
      { view: "overview", label: "Courses", icon: LayoutDashboard },
      { view: "tasks", label: "Revision tasks", icon: ListChecks },
      { view: "reports", label: "Reports", icon: BarChart3 },
      { view: "canvas", label: "Import courses", icon: Upload },
    ],
  },
  {
    label: "CTL",
    roles: ["CTLReviewer"],
    items: [
      { view: "readiness", label: "Readiness", icon: BadgeCheck },
      { view: "oversight", label: "Oversight", icon: Gauge },
      { view: "rubrics", label: "Rubrics", icon: ClipboardList },
      { view: "standards", label: "Standards", icon: Library },
    ],
  },
  {
    label: "HELP",
    roles: ["Instructor", "CTLReviewer"],
    items: [
      { view: "support", label: "Support", icon: LifeBuoy },
      { view: "glossary", label: "Glossary", icon: BookMarked },
    ],
  },
  { label: "RESULTS", roles: ["AcademicAdmin"], items: [{ view: "oversight", label: "Oversight", icon: Gauge }] },
  { label: "ADMINISTRATION", roles: ["SystemAdmin"], items: [{ view: "overview", label: "People and audit", icon: Users }] },
];

function App() {
  const [view, setView] = useState<View>("overview");
  const [readinessKey, setReadinessKey] = useState(0);
  const [reviewId, setReviewId] = useState<string | null>(null),
    [reviewsKey, setReviewsKey] = useState(0);
  const [taskId, setTaskId] = useState<string | null>(null),
    [tasksKey, setTasksKey] = useState(0);
  const [ticketId, setTicketId] = useState<string | null>(null);
  // The glossary entry to show, and where Support should start when the glossary sends someone there (ADR 015).
  const [glossaryTermId, setGlossaryTermId] = useState<string | null>(null);
  const [pendingSection, setPendingSection] = useState<string | null>(null);
  const [supportStart, setSupportStart] = useState<{
    key: number;
    tab: "tickets" | "knowledge" | "workshops";
    slug?: string;
    request?: { title: string; category: string };
  }>({ key: 0, tab: "tickets" });
  const [supportPrefillTaskId, setSupportPrefillTaskId] = useState<
    string | null
  >(null);
  const [user, setUser] = useState<User | null>(null),
    [boot, setBoot] = useState(true);
  const [role, setRole] = useState<Role>("Instructor"),
    [courses, setCourses] = useState<Course[]>([]);
  // Guide, readiness, reviews, tasks and reports on a course page are for the people who teach and review it.
  const courseTools = role === "Instructor" || role === "CTLReviewer";
  // Saving or removing a pasted syllabus changes what the improvement guide finds, so the guide is rebuilt (ADR 019).
  const [guideKey, setGuideKey] = useState(0);
  const [detail, setDetail] = useState<CourseDetail | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const [revisions, setRevisions] = useState<RevisionReport | null>(null);
  const [email, setEmail] = useState(""),
    [password, setPassword] = useState("");
  // Offered only when the server enables one-click demo sign-in for this machine; otherwise the request 404s and the list stays empty.
  const [demoAccounts, setDemoAccounts] = useState<DemoAccount[]>([]);
  // The hosted public demo: one-click sign-in only, no uploads or role changes, sample data reset on a schedule.
  const [demoMode, setDemoMode] = useState<DemoMode>({ publicDemo: false, resetHours: 0 });
  const publicDemo = demoMode.publicDemo;
  const uploadsOff =
    "Uploading is turned off in the public demo, so every visitor finds the same sample courses. Run TeachWell locally to import your own course export.";
  const [reload, setReload] = useState(0);
  function accept(next: User) {
    setView("overview");
    setUser(next);
    setRole(next.roles.includes("Instructor") ? "Instructor" : next.roles[0]);
  }
  function reportError(e: unknown) {
    if (e instanceof ApiError && e.status === 401) {
      setUser(null);
      setDetail(null);
      setCourses([]);
      resetCsrf();
      setError("Your session has ended. Please sign in again.");
    } else {
      setError((e as Error).message);
    }
  }
  useEffect(() => {
    request<User>("/me")
      .then(accept)
      .catch((e) => {
        // 503: the public demo is loading fresh sample data; its message says so.
        if (e instanceof ApiError && e.status === 503) setError(e.message);
        else if (!(e instanceof ApiError && e.status === 401))
          setError("Cannot reach TeachWell. Check that the API is running.");
      })
      .finally(() => setBoot(false));
    request<DemoAccount[]>("/auth/demo-accounts")
      .then(setDemoAccounts)
      .catch(() => setDemoAccounts([]));
    request<DemoMode>("/auth/demo-mode")
      .then(setDemoMode)
      .catch(() => undefined);
  }, []);
  // A new page starts at its top, as a reader expects, unless the Start here checklist asked for a particular section.
  useEffect(() => {
    if (!pendingSection) window.scrollTo(0, 0);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view, detail?.id, reviewId, taskId, ticketId]);
  // Opening a course from the Start here checklist goes straight to the section it named.
  useEffect(() => {
    if (!detail || !pendingSection) return;
    const section = pendingSection;
    setPendingSection(null);
    window.setTimeout(() => goToSection(section), 50);
  }, [detail, pendingSection]);
  // On phones the sections are one scrolling row; keep the current one in view without scrolling the page.
  useEffect(() => {
    const nav = document.querySelector<HTMLElement>(".side-nav");
    const active = nav?.querySelector<HTMLElement>('[aria-current="page"]');
    if (nav && active && nav.scrollWidth > nav.clientWidth)
      nav.scrollLeft += active.getBoundingClientRect().left - nav.getBoundingClientRect().left - 16;
  }, [view, taskId, ticketId, detail, role, user]);
  useEffect(() => {
    if (!user) return;
    let active = true;
    setDetail(null);
    setReviewId(null);
    setTaskId(null);
    setTicketId(null);
    setSupportPrefillTaskId(null);
    // Sections that belong to another role close when the role changes.
    const ctlOnly: View[] = ["rubrics", "standards", "readiness", "oversight"];
    if (role !== "CTLReviewer") setView((v) => (ctlOnly.includes(v) ? "overview" : v));
    setCourses([]);
    setRevisions(null);
    setError("");
    // Neither administrator role works with course content (ADR 001, ADR 014).
    if (role === "SystemAdmin" || role === "AcademicAdmin") return;
    setBusy(true);
    request<Course[]>("/courses?role=" + role)
      .then((data) => {
        if (active) setCourses(data);
      })
      .catch((e) => {
        if (active) reportError(e);
      })
      .finally(() => {
        if (active) setBusy(false);
      });
    // Scoped to the caller's own work either way: tasks assigned to an instructor, or tasks a CTL reviewer opened.
    request<RevisionReport>("/reports/revisions")
      .then((data) => {
        if (active) setRevisions(data);
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, [user, role, reload]);
  async function login(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await request("/auth/login", { email, password });
      setPassword("");
      resetCsrf();
      accept(await request<User>("/me"));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function quickLogin(account: DemoAccount) {
    setBusy(true);
    setError("");
    try {
      await request("/auth/demo-login", { email: account.email });
      resetCsrf();
      accept(await request<User>("/me"));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function logout() {
    setBusy(true);
    setError("");
    try {
      await request("/auth/logout", {});
      resetCsrf();
      setUser(null);
      setDetail(null);
      setCourses([]);
    } catch (e) {
      reportError(e);
    } finally {
      setBusy(false);
    }
  }
  async function open(course: Course) {
    setBusy(true);
    setError("");
    setReviewId(null);
    setTaskId(null);
    setTicketId(null);
    try {
      setDetail(await request<CourseDetail>("/courses/" + course.id));
    } catch (e) {
      reportError(e);
    } finally {
      setBusy(false);
    }
  }
  if (boot)
    return (
      <main className="loading" role="status">
        Opening your workspace…
      </main>
    );
  if (!user)
    return (
      <div className="login-layout">
        <section className="login-story">
          <div className="brand">
            <Leaf /> TeachWell
          </div>
          <div>
            <span className="eyebrow">A little clarity. A better course.</span>
            <h1>
              Good teaching
              <br />
              grows together.
            </h1>
            <p>
              A shared space for thoughtful course design and meaningful faculty
              support.
            </p>
          </div>
          <small>
            Independent demonstration · No institutional affiliation
          </small>
        </section>
        <main className="login-form">
          <span className="tag">{publicDemo ? "PUBLIC DEMO" : "OFFLINE DEMO"}</span>
          <h2>Welcome to your workspace</h2>
          {publicDemo && error && demoAccounts.length === 0 && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          {demoAccounts.length > 0 && (
            <section className="quick-login" aria-labelledby="quick-login-title">
              <h3 id="quick-login-title">Choose a demo account</h3>
              <p className="fine">
                {publicDemo
                  ? "One click, no password. Every person, course and college here is fictional" +
                    (demoMode.resetHours === 0
                      ? "."
                      : `, and the demo returns to fresh sample data every ${demoMode.resetHours === 24 ? "day" : `${demoMode.resetHours} hours`}.`)
                  : "One click, no password. Available only on this computer while running locally."}
              </p>
              <ul>
                {demoAccounts.map((a) => (
                  <li key={a.email}>
                    <button type="button" className="secondary" disabled={busy} onClick={() => quickLogin(a)}>
                      <strong>{a.displayName}</strong>
                      <span>{a.roles.map(roleLabel).join(" + ")}</span>
                    </button>
                  </li>
                ))}
              </ul>
              {error && (
                <p role="alert" className="error">
                  {error}
                </p>
              )}
            </section>
          )}
          {/* The public demo signs in with one click only, so nobody can lock its shared personas out with wrong passwords. */}
          {!publicDemo && (
          <>
          <p>
            {demoAccounts.length > 0
              ? "Or sign in with a demo account's email and the password you configured locally."
              : "Sign in with a fictional demo account and the password you configured locally."}
          </p>
          <form onSubmit={login}>
            <label>
              Email
              <input
                type="email"
                autoComplete="username"
                required
                maxLength={254}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </label>
            <label>
              Password
              <input
                type="password"
                autoComplete="current-password"
                required
                maxLength={200}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </label>
            {error && demoAccounts.length === 0 && (
              <p role="alert" className="error">
                {error}
              </p>
            )}
            <button disabled={busy}>
              {busy ? "Signing in…" : "Sign in"}
              <ArrowRight size={18} />
            </button>
          </form>
          <details>
            <summary>Demo account directory</summary>
            <p>
              Instructor: instructor@demo.teachwell.test
              <br />
              CTL + instructor: reviewer@demo.teachwell.test
              <br />
              Administrator: admin@demo.teachwell.test
            </p>
          </details>
          </>
          )}
          <p className="fine">
            {publicDemo
              ? "Canvas connections, course uploads and Microsoft sign-in are not available in the public demo."
              : "This workspace includes fictional samples and optional Canvas imports. Microsoft sign-in is not available."}
          </p>
        </main>
      </div>
    );
  // Leaving a section closes whatever was open inside it; going to Courses also refreshes the list.
  function go(next: View) {
    setView(next);
    setDetail(null);
    setReviewId(null);
    setTaskId(null);
    setTicketId(null);
    setSupportPrefillTaskId(null);
    setGlossaryTermId(null);
    setSupportStart((s) => ({ key: s.key + 1, tab: "tickets" }));
    if (next === "overview") setReload((x) => x + 1);
  }
  function supportFromGlossary(start: { tab: "tickets" | "knowledge" | "workshops"; slug?: string; request?: { title: string; category: string } }) {
    go("support");
    setSupportStart((s) => ({ ...start, key: s.key + 1 }));
  }
  const glossaryNavigation =
    role === "Instructor" || role === "CTLReviewer"
      ? {
          openGlossary: (termId: string) => {
            go("glossary");
            setGlossaryTermId(termId);
          },
        }
      : null;
  // An open task or ticket belongs to its section; an open course belongs to Courses.
  const current: View = ticketId
    ? "support"
    : taskId
      ? "tasks"
      : role === "AcademicAdmin"
        ? "oversight"
        : detail
          ? "overview"
          : view;
  return (
    <GlossaryProvider key={user.id} enabled={glossaryNavigation !== null}>
    <GlossaryNavigation.Provider value={glossaryNavigation}>
    <div className="workspace">
      <aside>
        <a href="/" className="brand">
          <Leaf /> TeachWell
        </a>
        <nav className="side-nav" aria-label="Workspace sections">
          {navGroups
            .filter((g) => g.roles.includes(role))
            .map((g) => (
              <div key={g.label} className="nav-group">
                <span className="nav-label">{g.label}</span>
                {g.items.map((item) => (
                  <button
                    key={item.label}
                    className="nav-item"
                    aria-current={current === item.view ? "page" : undefined}
                    onClick={() => go(item.view)}
                  >
                    <item.icon size={17} aria-hidden="true" />
                    {item.label}
                  </button>
                ))}
              </div>
            ))}
        </nav>
        <div className="sidebar-note">
          <ShieldCheck />
          <strong>A safe place to explore</strong>
          <p>
            {publicDemo
              ? "Every person, course and college here is fictional. Try anything; the demo resets to fresh sample data."
              : "Fictional samples are included. Imported courses stay private until shared."}
          </p>
          <span className="tag">{publicDemo ? "PUBLIC DEMO" : "DEVELOPMENT WORKSPACE"}</span>
        </div>
        <div className="profile">
          <span className="avatar">
            {user.displayName
              .split(" ")
              .map((n) => n[0])
              .join("")}
          </span>
          <div className="profile-text">
            <strong>{user.displayName}</strong>
            {user.roles.length > 1 ? (
              // Who you are and which role you are working in sit together, beside the sections that role offers.
              <label className="role-switch">
                <span>Viewing as</span>
                <select
                  aria-label="Workspace role"
                  value={role}
                  disabled={busy}
                  onChange={(e) => setRole(e.target.value as Role)}
                >
                  {user.roles.map((r) => (
                    <option key={r} value={r}>
                      {roleLabel(r)}
                    </option>
                  ))}
                </select>
              </label>
            ) : (
              <small>{roleLabel(role)}</small>
            )}
          </div>
          <button aria-label="Sign out" disabled={busy} onClick={logout}>
            <LogOut size={18} />
          </button>
        </div>
      </aside>
      <div className="main-wrap">
        <header>
          <span>
            Workspace{" "}
            <span className="muted">
              /{" "}
              {ticketId
                ? "Support ticket"
                : taskId
                  ? "Revision task"
                  : view === "canvas"
                    ? "Import courses"
                    : view === "rubrics"
                      ? "Rubrics"
                      : view === "standards"
                        ? "Standards"
                      : view === "readiness"
                        ? "Readiness reviews"
                      : view === "oversight" || role === "AcademicAdmin"
                        ? "Oversight"
                      : view === "tasks"
                        ? "Revision tasks"
                        : view === "support"
                          ? "Support"
                          : view === "reports"
                            ? "Reports"
                          : view === "glossary"
                            ? "Glossary"
                            : detail
                              ? detail.code + (reviewId ? " / Review" : "")
                              : role === "SystemAdmin"
                                ? "People and audit"
                                : "Courses"}
            </span>
          </span>
          <Notifications
            key={user.id}
            onSessionExpired={reportError}
            onOpen={(n) => {
              // A new request points CTL reviewers at the queue; everything else at the course.
              if (n.kind === "ReadinessRequested" && user.roles.includes("CTLReviewer")) {
                setRole("CTLReviewer");
                setDetail(null);
                setView("readiness");
              } else if (n.courseId) {
                setView("overview");
                open({ id: n.courseId } as Course);
              }
            }}
          />
        </header>
        <main className="content">
          {error && (
            <div role="alert" className="error">
              {error}{" "}
              <button onClick={() => setReload((x) => x + 1)}>
                Retry dashboard
              </button>
            </div>
          )}
          {ticketId ? (
            <TicketWorkspace
              key={ticketId}
              ticketId={ticketId}
              onBack={() => setTicketId(null)}
              onSessionExpired={reportError}
            />
          ) : taskId ? (
            <TaskWorkspace
              key={taskId}
              taskId={taskId}
              onBack={() => {
                setTaskId(null);
                setReviewsKey((x) => x + 1);
                setTasksKey((x) => x + 1);
              }}
              onSessionExpired={reportError}
              onRequestHelp={(id) => {
                setSupportPrefillTaskId(id);
                setTaskId(null);
                setReviewId(null);
                setDetail(null);
                setView("support");
              }}
            />
          ) : view === "canvas" ? (
            <>
              {role === "Instructor" && (
                <CourseExportUpload
                  onImported={(id) => {
                    setView("overview");
                    open({ id } as Course);
                    // Refresh the course list in place; reloading the dashboard would clear the course being opened.
                    request<Course[]>("/courses?role=" + role)
                      .then(setCourses)
                      .catch(() => undefined);
                  }}
                  onSessionExpired={reportError}
                  disabledReason={publicDemo ? uploadsOff : undefined}
                />
              )}
              <CanvasConnections onSessionExpired={reportError} />
            </>
          ) : view === "rubrics" && role === "CTLReviewer" ? (
            <Rubrics onSessionExpired={reportError} />
          ) : view === "standards" && role === "CTLReviewer" ? (
            <>
              <Standards onSessionExpired={reportError} />
              {!publicDemo && <AllyReportCheck onSessionExpired={reportError} />}
            </>
          ) : (view === "oversight" && role === "CTLReviewer") || (role === "AcademicAdmin" && !detail) ? (
            <Oversight
              key={role}
              onOpenCourse={(id) => {
                setView("overview");
                open({ id } as Course);
              }}
              onSessionExpired={reportError}
            />
          ) : view === "readiness" && role === "CTLReviewer" ? (
            <ReadinessQueue
              onOpenCourse={(id) => {
                setView("overview");
                open({ id } as Course);
              }}
              onSessionExpired={reportError}
            />
          ) : detail && reviewId ? (
            <ReviewWorkspace
              key={reviewId}
              reviewId={reviewId}
              reviewerView={role === "CTLReviewer"}
              onBack={() => {
                setReviewId(null);
                setReviewsKey((x) => x + 1);
              }}
              onSessionExpired={reportError}
              onTaskCreated={(id) => {
                setReviewId(null);
                setTaskId(id);
              }}
            />
          ) : detail ? (
            <>
              <button className="back" onClick={() => setDetail(null)}>
                ← Back to courses
              </button>
              <CourseHeader
                course={detail}
                sections={[
                  ...(courseTools
                    ? [
                        { id: "guide", label: "Improvement guide" },
                        { id: "tasks", label: "Revision tasks" },
                        { id: "readiness", label: "Readiness review" },
                        { id: "reviews", label: "Reviews" },
                        { id: "reports", label: "Alignment and quality" },
                      ]
                    : []),
                  ...(detail.elements.length > 0 ? [{ id: "course-content", label: "Course content" }] : []),
                  ...(detail.permissions.canManageSharing ? [{ id: "sharing", label: "Sharing" }] : []),
                  ...(courseTools ? [{ id: "course-standards", label: "Course standards" }] : []),
                ]}
              />
              {courseTools && (
                <SyllabusPaste
                  key={`${detail.id}-${role}-${guideKey}-syllabus`}
                  courseId={detail.id}
                  onSessionExpired={reportError}
                  onSaved={() => setGuideKey((x) => x + 1)}
                />
              )}
              {courseTools && <CourseGuide key={`${detail.id}-${role}-${guideKey}-guide`} course={detail} onSessionExpired={reportError} onOpenReview={setReviewId} onOpenTask={setTaskId} onProgress={() => setReadinessKey((x) => x + 1)} />}
              {courseTools && (
                <CourseConformance
                  key={`${detail.id}-${role}-${guideKey}-conformance`}
                  courseId={detail.id}
                  onSessionExpired={reportError}
                />
              )}
              {courseTools && (
                <CourseTasks
                  key={`${detail.id}-${role}-${tasksKey}`}
                  courseId={detail.id}
                  onOpen={setTaskId}
                  onSessionExpired={reportError}
                />
              )}
              {courseTools && (
                <CourseReadiness key={`${detail.id}-${role}-readiness`} courseId={detail.id} refreshKey={readinessKey} onSessionExpired={reportError} />
              )}
              {courseTools && (
                <CourseReviews
                  key={`${detail.id}-${role}-${reviewsKey}`}
                  course={detail}
                  reviewerView={role === "CTLReviewer"}
                  onOpen={setReviewId}
                  onSessionExpired={reportError}
                />
              )}
              {courseTools && (
                <CourseReports
                  key={`${detail.id}-${role}`}
                  course={detail}
                  canManage={detail.permissions.canManageSharing}
                  onSessionExpired={reportError}
                />
              )}
              <CourseContent course={detail} />
              {detail.permissions.canRefreshExport && (
                <CourseExportUpload
                  courseId={detail.id}
                  onImported={(id) => open({ id } as Course)}
                  onSessionExpired={reportError}
                  disabledReason={publicDemo ? uploadsOff : undefined}
                />
              )}
              {detail.permissions.canManageSharing && (
                <CourseSharing
                  key={detail.id}
                  courseId={detail.id}
                  onSessionExpired={reportError}
                />
              )}
              {courseTools && (
                <CourseStandards key={`${detail.id}-${role}-standards`} courseId={detail.id} onSessionExpired={reportError} />
              )}
            </>
          ) : view === "tasks" ? (
            <TaskDashboard onOpen={setTaskId} onSessionExpired={reportError} />
          ) : view === "support" ? (
            <Support
              key={supportPrefillTaskId ?? "support-" + supportStart.key}
              isCtl={role === "CTLReviewer"}
              initialTab={supportStart.tab}
              initialArticleSlug={supportStart.slug}
              prefillRequest={supportStart.request}
              prefillRevisionTaskId={supportPrefillTaskId}
              onPrefillConsumed={() => setSupportPrefillTaskId(null)}
              onOpenTicket={setTicketId}
              onSessionExpired={reportError}
            />
          ) : view === "glossary" && glossaryNavigation ? (
            <GlossaryPage
              key={glossaryTermId ?? "glossary"}
              initialTermId={glossaryTermId}
              isCtl={role === "CTLReviewer"}
              onOpenArticle={(slug) => supportFromGlossary({ tab: "knowledge", slug })}
              onOpenWorkshops={() => supportFromGlossary({ tab: "workshops" })}
              onAskCtl={(request) => supportFromGlossary({ tab: "tickets", request })}
              onSessionExpired={reportError}
            />
          ) : view === "reports" ? (
            <Reports isCtl={role === "CTLReviewer"} onSessionExpired={reportError} />
          ) : (
            <>
              <span className="eyebrow">
                YOUR TEACHING & LEARNING WORKSPACE
              </span>
              <h1>
                {role === "CTLReviewer"
                  ? "Small steps. Shared progress."
                  : role === "SystemAdmin"
                    ? "A foundation built on trust."
                    : `Welcome back, ${user.displayName.split(" ")[0]}.`}
              </h1>
              <p className="intro">
                {role === "CTLReviewer"
                  ? "A focused view of courses explicitly shared with you for review."
                  : role === "SystemAdmin"
                    ? "Your application role does not grant access to course content."
                    : "TeachWell helps you find and fix what gets in students' way in your course, with the CTL there when you want help."}
              </p>
              {role === "Instructor" && (
                <StartHere
                  key={user.id}
                  userId={user.id}
                  onImport={() => go("canvas")}
                  onOpenCourse={(id, section) => {
                    setView("overview");
                    setPendingSection(section);
                    open({ id } as Course);
                  }}
                  onAskForHelp={() => go("support")}
                  onSessionExpired={reportError}
                />
              )}
              {role === "SystemAdmin" ? (
                <section className="panel">
                  <ShieldCheck />
                  <h2>Administrator workspace</h2>
                  <p>
                    Course access always requires ownership or an explicit
                    share; no role here grants it.
                  </p>
                  <p>
                    {publicDemo
                      ? "Enabled: public demo sign-in. Disabled: password sign-in, uploads, role changes, Entra, Canvas PAT, Canvas OAuth, and Canvas write-back."
                      : "Enabled: local demo authentication. Disabled: Entra, Canvas PAT, Canvas OAuth, and Canvas write-back."}
                  </p>
                  <AdminUsers onSessionExpired={reportError} readOnly={publicDemo} />
                  <AuditLog onSessionExpired={reportError} />
                </section>
              ) : (
                <>
                  <div className="stats">
                    <div>
                      <BookOpen />
                      <strong>{busy ? "…" : courses.length}</strong>
                      <span>
                        {role === "CTLReviewer"
                          ? "Shared courses"
                          : "Your courses"}
                      </span>
                    </div>
                    <div>
                      <Users />
                      <strong>
                        {revisions ? revisions.openTasks.length : "…"}
                      </strong>
                      <span>
                        {role === "CTLReviewer"
                          ? "Revision tasks you opened, still open"
                          : "Open revision tasks assigned to you"}
                      </span>
                    </div>
                    <div>
                      <ShieldCheck />
                      <strong>Safe to explore</strong>
                      <span>Nothing here changes your Canvas course or stores student work</span>
                    </div>
                  </div>
                  {revisions && revisions.openTasks.length > 0 && (
                    <>
                      <div className="section-title">
                        <h2>Needs your attention</h2>
                        <span className="muted">
                          {revisions.openTasks.length} open revision task
                          {revisions.openTasks.length === 1 ? "" : "s"}
                        </span>
                      </div>
                      <ul className="task-list">
                        {[...revisions.openTasks]
                          .sort((a, b) => b.daysOpen - a.daysOpen)
                          .slice(0, 5)
                          .map((t) => (
                            <li key={t.id}>
                              <div>
                                <span
                                  className={
                                    "tag status-" + t.status.toLowerCase()
                                  }
                                >
                                  {taskStatusLabel(t.status).toUpperCase()}
                                </span>{" "}
                                <span
                                  className={
                                    "tag severity-" + t.priority.toLowerCase()
                                  }
                                >
                                  {t.priority.toUpperCase()}
                                </span>
                                <strong>{t.title}</strong>
                                <small>
                                  {t.courseTitle} · Open {t.daysOpen} day
                                  {t.daysOpen === 1 ? "" : "s"}
                                </small>
                              </div>
                              <button
                                onClick={() => setTaskId(t.id)}
                                aria-label={`Open task: ${t.title}`}
                              >
                                Open
                              </button>
                            </li>
                          ))}
                      </ul>
                      {revisions.openTasks.length > 5 && (
                        <p className="fine">
                          <button
                            className="link-button"
                            onClick={() => setView("tasks")}
                          >
                            View all {revisions.openTasks.length} open tasks
                          </button>
                        </p>
                      )}
                    </>
                  )}
                  <div className="section-title">
                    <h2>
                      {role === "CTLReviewer"
                        ? "Shared for your review"
                        : "Your courses"}
                    </h2>
                    <span className="muted">
                      {courses.length} courses · Course library
                    </span>
                  </div>
                  {busy && <p role="status">Loading courses…</p>}
                  {!busy && !error && courses.length === 0 && (
                    <section className="panel">
                      <BookOpen />
                      <h3>No courses here yet</h3>
                      <p>
                        {role === "CTLReviewer"
                          ? "Courses will appear when explicitly shared with you."
                          : "You do not own any sample courses in this role. You can switch roles above if available."}
                      </p>
                    </section>
                  )}
                  <div className="course-grid">
                    {courses.map((c, i) => (
                      <article className="course-card" key={c.id}>
                        <div className={"course-cover cover-" + i}>
                          <span>{c.code}</span>
                          <BookOpen size={58} strokeWidth={1} />
                          <span className="cover-word">
                            {i === 0 ? "Create." : "Include."}
                          </span>
                        </div>
                        <div className="card-body">
                          <span className="tag">
                            {c.sourceType === "Demo"
                              ? "FICTIONAL DEMO"
                              : c.sourceType === "CanvasExport"
                                ? "CANVAS EXPORT"
                                : "CANVAS IMPORT"}
                          </span>
                          {c.readiness && c.readiness !== "NotRequested" && (
                            <span className={"tag readiness-badge readiness-" + c.readiness.toLowerCase()}>{c.readinessLabel.toUpperCase()}</span>
                          )}
                          <h3>{c.title}</h3>
                          <p>
                            {c.elementCount} {c.elementCount === 1 ? "item" : "items"} (modules, pages, assignments){" "}
                            <span>
                              ·{" "}
                              {c.permission === "Review"
                                ? "Shared with you for review"
                                : "Your course"}
                            </span>
                          </p>
                          <button
                            className="course-link"
                            disabled={busy}
                            onClick={() => open(c)}
                          >
                            Explore course <ArrowRight size={17} />
                          </button>
                        </div>
                      </article>
                    ))}
                  </div>
                  {role === "Instructor" ? (
                    <HowItWorks />
                  ) : (
                  <section className="next-step">
                    <div>
                      <span className="eyebrow">
                        BUILT FOR THOUGHTFUL IMPROVEMENT
                      </span>
                      <h2>Start with curiosity.</h2>
                      <p>
                        {role === "CTLReviewer"
                          ? "Open a shared course to review a page or assignment against a versioned rubric, then turn confirmed findings into a revision task."
                          : "Explore the sample modules, pages, and assignments. Published CTL reviews and revision tasks appear on each course."}{" "}
                        Support, in the sidebar, has job aids, workshops, and a way to request help.
                      </p>
                    </div>
                    <Leaf size={62} strokeWidth={1} />
                  </section>
                  )}
                </>
              )}
            </>
          )}
          <footer>
            TeachWell · A space to improve, together.
            <span>Development demonstration</span>
          </footer>
        </main>
      </div>
    </div>
    </GlossaryNavigation.Provider>
    </GlossaryProvider>
  );
}
createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
