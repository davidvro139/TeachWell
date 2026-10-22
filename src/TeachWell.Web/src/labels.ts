/**
 * Plain-language names for the codes the API uses (ADR 016). Screens show these, never the raw code, so an instructor reads
 * "Waiting for a CTL check", not "ReadyForReview". Unknown codes fall back to spaced, sentence-case words.
 */

/** "ReadyForReview" → "Ready for review"; "CourseDesign" → "Course design". */
export const humanize = (code: string) => {
  const words = code
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .trim()
    .toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
};

const TASK_STATUS: Record<string, string> = {
  Draft: "Draft",
  NeedsRevision: "To do",
  InProgress: "In progress",
  ReadyForReview: "Waiting for a CTL check",
  ChangesRequested: "Changes requested",
  Blocked: "Blocked",
  Withdrawn: "Withdrawn",
  NotApplicable: "Not applicable",
  Resolved: "Resolved",
};
const TICKET_STATUS: Record<string, string> = {
  New: "New",
  Triaged: "Received by the CTL",
  InProgress: "In progress",
  WaitingOnInstructor: "Waiting for the instructor",
  Resolved: "Resolved",
  Closed: "Closed",
};
const SUPPORT_CATEGORY: Record<string, string> = {
  Canvas: "Canvas",
  Accessibility: "Accessibility",
  CourseDesign: "Course design",
  Multimedia: "Video and media",
  Other: "Something else",
};
const ROLE: Record<string, string> = {
  Instructor: "Instructor",
  CTLReviewer: "CTL reviewer",
  AcademicAdmin: "Academic administrator",
  SystemAdmin: "Administrator",
};

export const taskStatusLabel = (status: string) => TASK_STATUS[status] ?? humanize(status);
export const ticketStatusLabel = (status: string) => TICKET_STATUS[status] ?? humanize(status);
export const supportCategoryLabel = (category: string) => SUPPORT_CATEGORY[category] ?? humanize(category);
export const roleName = (role: string) => ROLE[role] ?? humanize(role);
/** Any other status code shown as a tag, e.g. "Draft", "Published", "Proposed". */
export const statusLabel = (status: string) => humanize(status);
