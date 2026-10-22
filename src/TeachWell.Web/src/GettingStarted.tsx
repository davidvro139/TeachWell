import { useEffect, useState } from "react";
import { BadgeCheck, CircleCheck, Circle, LifeBuoy, ListChecks, MessagesSquare, Upload, Wrench } from "lucide-react";
import { ApiError, request } from "./api";
import { Term } from "./Glossary";
import "./GettingStarted.css";

interface GettingStarted {
  broughtInACourse: boolean;
  markedAStep: boolean;
  askedForReadiness: boolean;
  endorsed: boolean;
  courseId: string | null;
  courseTitle: string | null;
}

interface Props {
  userId: string;
  onImport: () => void;
  onOpenCourse: (courseId: string, section: string) => void;
  onAskForHelp: () => void;
  onSessionExpired: (error: unknown) => void;
}

const hiddenKey = (userId: string) => "teachwell.startHere.hidden." + userId;

/**
 * The instructor's "Start here" checklist (ADR 016): the path from bringing a course in to an endorsement, with the next step highlighted
 * and one button to take it. Progress comes from the server, from what the instructor has actually done, so it is the same on any device.
 * Hiding the checklist is remembered in this browser only.
 */
export function StartHere({ userId, onImport, onOpenCourse, onAskForHelp, onSessionExpired }: Props) {
  const [data, setData] = useState<GettingStarted | null>(null);
  const [failed, setFailed] = useState(false);
  const [hidden, setHidden] = useState(() => {
    try {
      return localStorage.getItem(hiddenKey(userId)) === "1";
    } catch {
      return false;
    }
  });

  useEffect(() => {
    let active = true;
    request<GettingStarted>("/getting-started")
      .then((d) => {
        if (active) setData(d);
      })
      .catch((e) => {
        if (!active) return;
        if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
        else setFailed(true);
      });
    return () => {
      active = false;
    };
  }, []);

  function setHiddenAndRemember(value: boolean) {
    setHidden(value);
    try {
      if (value) localStorage.setItem(hiddenKey(userId), "1");
      else localStorage.removeItem(hiddenKey(userId));
    } catch {
      /* storage unavailable: the choice lasts for this visit */
    }
  }

  // The checklist is a convenience: if it cannot load, the dashboard works without it.
  if (failed) return null;
  if (hidden)
    return (
      <p className="start-here-restore">
        <button type="button" className="link-button" onClick={() => setHiddenAndRemember(false)}>
          Show the Start here checklist
        </button>
      </p>
    );

  const course = data?.courseId ? { id: data.courseId, title: data.courseTitle ?? "your course" } : null;
  const steps = [
    {
      key: "import",
      done: !!data?.broughtInACourse,
      icon: <Upload size={18} aria-hidden="true" />,
      title: "Bring in your course",
      text: "Download an export of your course from Canvas and upload it here. TeachWell checks a copy; nothing in Canvas changes.",
      action: { label: "Bring in a course", run: onImport },
    },
    {
      key: "guide",
      done: !!data?.markedAStep,
      icon: <Wrench size={18} aria-hidden="true" />,
      title: "Work through the improvement guide",
      text: (
        <>
          The <Term id="improvement-guide">improvement guide</Term> lists what to fix, most helpful first, and how to fix it. Start with the{" "}
          <Term id="quick-win">quick wins</Term>: fix each one in Canvas, then mark it Done.
        </>
      ),
      action: course ? { label: `Open the guide for ${course.title}`, run: () => onOpenCourse(course.id, "guide") } : null,
    },
    {
      key: "readiness",
      done: !!data?.askedForReadiness,
      icon: <MessagesSquare size={18} aria-hidden="true" />,
      title: "Ask the CTL for a readiness review",
      text: (
        <>
          Once the required steps are done or explained, a <Term id="readiness-review">readiness review</Term> lets the CTL look at the whole
          course with you.
        </>
      ),
      action: course ? { label: "Go to the readiness review", run: () => onOpenCourse(course.id, "readiness") } : null,
    },
    {
      key: "endorsed",
      done: !!data?.endorsed,
      icon: <BadgeCheck size={18} aria-hidden="true" />,
      title: "Get your course endorsed",
      text: (
        <>
          An <Term id="endorsement">endorsement</Term> is the CTL's dated record that the course met your college's standards. It is not a grade
          of your teaching.
        </>
      ),
      action: null,
    },
  ];
  const current = steps.findIndex((s) => !s.done);
  const doneCount = steps.filter((s) => s.done).length;

  return (
    <section className="panel start-here" aria-labelledby="start-here-title">
      <div className="start-here-head">
        <div>
          <span className="eyebrow">
            <ListChecks size={14} aria-hidden="true" /> START HERE
          </span>
          <h2 id="start-here-title">{doneCount === steps.length ? "You have done every step" : "Your path to a stronger course"}</h2>
          <p className="fine">
            {data ? `${doneCount} of ${steps.length} done.` : "Checking your progress…"} You can do these at your own pace; the CTL is here to help at
            every step.
          </p>
        </div>
        <button type="button" className="link-button" onClick={() => setHiddenAndRemember(true)}>
          Hide this checklist
        </button>
      </div>
      <ol className="start-here-steps">
        {steps.map((s, i) => (
          <li key={s.key} className={s.done ? "is-done" : i === current ? "is-current" : ""} aria-current={i === current ? "step" : undefined}>
            <span className="start-here-mark" aria-hidden="true">
              {s.done ? <CircleCheck size={22} /> : <Circle size={22} />}
            </span>
            <div>
              <h3>
                {s.icon} {s.title}
                <span className="visually-hidden">{s.done ? " (done)" : i === current ? " (your next step)" : ""}</span>
              </h3>
              <p>{s.text}</p>
              {i === current && s.action && (
                <button type="button" onClick={s.action.run}>
                  {s.action.label}
                </button>
              )}
            </div>
          </li>
        ))}
      </ol>
      <p className="start-here-foot">
        <a href="#how-it-works" className="link-button">
          How TeachWell fits together
        </a>
        <button type="button" className="link-button" onClick={onAskForHelp}>
          <LifeBuoy size={14} aria-hidden="true" /> Ask the CTL a question
        </button>
      </p>
    </section>
  );
}

/** One plain explanation of how TeachWell's tools relate, for anyone new to it (ADR 016). */
export function HowItWorks() {
  return (
    <section className="panel how-it-works" id="how-it-works" aria-labelledby="how-it-works-title">
      <span className="eyebrow">HOW TEACHWELL FITS TOGETHER</span>
      <h2 id="how-it-works-title">Four tools, one goal: a course that works for every student</h2>
      <ol className="how-it-works-list">
        <li>
          <h3>
            <Term id="improvement-guide">Improvement guide</Term>
          </h3>
          <p className="how-when">You, any time</p>
          <p>
            TeachWell checks a copy of your course and suggests fixes, most helpful first, each with why it matters and how to do it in Canvas. No
            reviewer is involved, and it is not a grade.
          </p>
        </li>
        <li>
          <h3>Help from the CTL</h3>
          <p className="how-when">Whenever you are stuck</p>
          <p>
            Ask a question from any guide step or from Support. Underlined words open a plain explanation, and the Glossary collects them all.
          </p>
        </li>
        <li>
          <h3>
            <Term id="readiness-review">Readiness review</Term>
          </h3>
          <p className="how-when">When you are ready</p>
          <p>
            The CTL looks at the whole course, then either <Term id="endorsement">endorses</Term> it or asks for specific changes, with reasons.
          </p>
        </li>
        <li>
          <h3>
            <Term id="course-review">Reviews</Term> and <Term id="revision-task">revision tasks</Term>
          </h3>
          <p className="how-when">If the CTL reviews a page or assignment</p>
          <p>
            A review records what the reviewer found. A revision task is one specific change they ask for, tracked until it is done. You will find
            both on the course page.
          </p>
        </li>
      </ol>
    </section>
  );
}
