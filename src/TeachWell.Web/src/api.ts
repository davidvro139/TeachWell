export type Role = "Instructor" | "CTLReviewer" | "SystemAdmin" | "AcademicAdmin";
export interface User {
  id: string;
  displayName: string;
  email: string;
  roles: Role[];
}
export interface Course {
  id: string;
  title: string;
  code: string;
  sourceType: string;
  elementCount: number;
  permission: string;
  /** Readiness state (ADR 013/014) for the card's badge: NotRequested, Requested, InReview, ChangesRequested, Endorsed or RecheckDue. */
  readiness: string;
  readinessLabel: string;
}
export interface Element {
  id: string;
  parentElementId: string | null;
  title: string;
  type: string;
  plainText: string;
  targetElementId?: string | null;
  sourceUrl?: string | null;
  sourceStatus?: string;
  sourceWarning?: string | null;
  currentSnapshotId?: string | null;
  position?: number;
}
export interface CourseDetail {
  id: string;
  title: string;
  code: string;
  sourceType: string;
  elements: Element[];
  permissions: { canManageSharing: boolean; canReview?: boolean; canRefreshExport?: boolean };
  sourceWebUrl?: string | null;
  sourceOrigin?: string | null;
  sourceCourseId?: string;
  lastSyncedAt?: string | null;
  warnings?: string[];
  /** D4: maintained by the CTL; selects the course's standard set. */
  deliveryMode?: DeliveryMode | null;
  department?: string | null;
}
export interface CanvasCourseOption {
  id: string;
  title: string;
  code: string;
}
export interface CanvasImportResult {
  courseId: string;
  elementCount: number;
  warnings: string[];
}
export interface ReviewerOption {
  id: string;
  displayName: string;
}
export interface CourseShare {
  userId: string;
  displayName: string;
  permission: string;
  grantedAt: string;
}
export class ApiError extends Error {
  // API errors never carry a submitted credential or raw upstream response.
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
export interface CanvasSettings {
  enabled: boolean;
  allowedOrigins: string[];
}
export interface CanvasConnection {
  id: string;
  baseUrl: string;
  status: string;
  canvasUserId: string | null;
  tokenMask: string | null;
  expiresAt: string | null;
  lastValidatedAt: string | null;
  lastCheckedAt: string | null;
  errorCode: string | null;
}
let token: string | undefined;
export async function request<T>(
  path: string,
  body?: unknown,
  method = body !== undefined ? "POST" : "GET",
): Promise<T> {
  const unsafe = method !== "GET";
  if (unsafe && !token)
    token = (await request<{ token: string }>("/auth/csrf")).token;
  const response = await fetch("/api/v1" + path, {
    credentials: "same-origin",
    method,
    headers: {
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(unsafe ? { "X-CSRF-TOKEN": token! } : {}),
    },
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new ApiError(
      response.status,
      error.errors
        ? Object.values(error.errors).flat().join(" ")
        : (error.title ?? `Request failed (${response.status}).`),
    );
  }
  return response.status === 204 ? (undefined as T) : response.json();
}
/** Multipart upload with the same CSRF and error handling as request(). */
export async function upload<T>(path: string, form: FormData): Promise<T> {
  if (!token) token = (await request<{ token: string }>("/auth/csrf")).token;
  const response = await fetch("/api/v1" + path, {
    credentials: "same-origin",
    method: "POST",
    headers: { "X-CSRF-TOKEN": token },
    body: form,
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new ApiError(
      response.status,
      error.errors ? Object.values(error.errors).flat().join(" ") : (error.title ?? `Upload failed (${response.status}).`),
    );
  }
  return response.json();
}
/**
 * Multipart upload that reports progress (fetch cannot), with the same CSRF and error handling as upload().
 * onProgress receives 0–1 while the file is sent; the promise resolves once the server has finished processing it.
 */
export function uploadWithProgress<T>(path: string, form: FormData, onProgress: (fraction: number) => void): Promise<T> {
  return (async () => {
    if (!token) token = (await request<{ token: string }>("/auth/csrf")).token;
    return new Promise<T>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open("POST", "/api/v1" + path);
      xhr.withCredentials = true;
      xhr.setRequestHeader("X-CSRF-TOKEN", token!);
      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) onProgress(e.loaded / e.total);
      };
      xhr.onload = () => {
        let body: { errors?: Record<string, string[]>; title?: string } = {};
        try {
          body = JSON.parse(xhr.responseText || "{}");
        } catch {
          body = {};
        }
        if (xhr.status >= 200 && xhr.status < 300) resolve(body as T);
        else
          reject(
            new ApiError(
              xhr.status,
              body.errors ? Object.values(body.errors).flat().join(" ") : (body.title ?? `Upload failed (${xhr.status}).`),
            ),
          );
      };
      xhr.onerror = () => reject(new ApiError(0, "The upload was interrupted. Check your connection and try again."));
      xhr.send(form);
    });
  })();
}
export interface CourseExportImport {
  courseId: string;
  title: string;
  code: string;
  elementCount: number;
  created: boolean;
  warnings: string[];
}
export function resetCsrf() {
  token = undefined;
}
export type Outcome = "Rated" | "NotApplicable" | "NotReviewed";
export type Severity = "Low" | "Medium" | "High" | "Critical";
export interface Criterion {
  id: string;
  code: string;
  title: string;
  description: string;
  category: string;
  elementTypes: string[];
  reviewMethod: string;
  evidenceGuidance: string;
  ratingAnchors: string[];
  weight: number;
  required: boolean;
  automatedChecks: string[];
}
export type CriterionInput = Omit<Criterion, "id">;
export interface RubricVersionSummary {
  id: string;
  versionNumber: number;
  status: "Draft" | "Published";
  createdAt: string;
  publishedAt: string | null;
  criterionCount: number;
  /** What this version's criteria apply to: page, assignment and/or course. */
  elementTypes: string[];
}
export interface RubricProfile {
  id: string;
  name: string;
  deliveryMethod: string;
  versions: RubricVersionSummary[];
}
export interface RubricVersion {
  id: string;
  profileId: string;
  profileName: string;
  versionNumber: number;
  status: "Draft" | "Published";
  revision: string;
  createdAt: string;
  publishedAt: string | null;
  publishedByName: string | null;
  criteria: Criterion[];
}
export interface Evaluation {
  criterionId: string;
  outcome: Outcome;
  rating: number | null;
  comment: string;
  evidence: string;
  updatedAt: string | null;
}
export interface Finding {
  id: string;
  criterionId: string | null;
  source: "Automated" | "Manual";
  checkCode: string | null;
  title: string;
  description: string;
  severity: Severity;
  recommendation: string;
  evidence: string;
  status: "Proposed" | "Confirmed" | "Rejected";
  createdAt: string;
  /** The module or item a course-level finding concerns. */
  subjectElementId?: string | null;
  subjectTitle?: string | null;
  subjectSourceUrl?: string | null;
}
export interface CategoryScore {
  category: string;
  applicable: number;
  rated: number;
  notApplicable: number;
  percent: number | null;
}
export interface ScoreSummary {
  applicable: number;
  rated: number;
  notApplicable: number;
  notReviewed: number;
  coveragePercent: number;
  percent: number | null;
  requiredBelowExpectations: number;
  requiredNotReviewed: number;
  categories: CategoryScore[];
}
export interface Review {
  id: string;
  courseId: string;
  courseTitle: string;
  elementId: string;
  elementType: string;
  elementTitle: string;
  status: "Draft" | "Published";
  revision: string;
  reviewerName: string;
  startedAt: string;
  checksRunAt: string | null;
  publishedAt: string | null;
  summary: string;
  snapshot: {
    id: string;
    fetchedAt: string;
    contentHash: string;
    title: string;
    plainText: string;
    sourceUrl: string | null;
    sourceStatus: string;
    hasStructuralEvidence: boolean;
    images: number;
    headings: number;
    links: number;
    tables: number;
    /** Server-generated allowlisted markup; contains no scripts, styles, attributes from the source, URLs or images. */
    previewHtml?: string | null;
  };
  isLatestSnapshot: boolean;
  sourceOrigin: string | null;
  rubric: RubricVersion;
  evaluations: Evaluation[];
  findings: Finding[];
  score: ScoreSummary;
  checkLimitations: string[];
  canEdit: boolean;
  outline?: CourseOutline | null;
  /** The standard-set version pinned when the review started. */
  standards?: AppliedStandards | null;
  /** "Why it matters / how to fix / example" for each automated check among the visible findings. */
  guidance?: Record<string, CheckGuidance> | null;
}
export interface OutlineItem {
  elementId: string;
  type: string;
  title: string;
  status: string;
  snapshotId: string | null;
  sourceUrl: string | null;
  hasDueDate: boolean | null;
  hasRubric: boolean | null;
  aligned: boolean;
}
export interface OutlineModule {
  elementId: string;
  title: string;
  status: string;
  sourceUrl: string | null;
  items: OutlineItem[];
}
export interface CourseOutline {
  modules: OutlineModule[];
  unplaced: OutlineItem[];
  competencies: number;
}
export interface Preview {
  snapshotId: string;
  title: string;
  previewHtml: string | null;
}
export interface ReviewSummary {
  id: string;
  elementId: string;
  elementType: string;
  elementTitle: string;
  status: "Draft" | "Published";
  reviewerName: string;
  rubricName: string;
  rubricVersion: number;
  startedAt: string;
  publishedAt: string | null;
  snapshotFetchedAt: string;
  coveragePercent: number;
  confirmedFindings: number;
  isMine: boolean;
}
export const CATEGORIES = [
  "Course structure and navigation",
  "Objectives and competencies",
  "Assessment and feedback",
  "Instructional content and scaffolding",
  "Engagement and active learning",
  "Accessibility and UDL",
  "Multimedia and copyright",
  "Student support and communication",
  "Technical quality",
  "Continuous improvement",
];
/** Page and assignment checks (server: DeterministicChecks.Rules, including ContentChecks). */
export const ELEMENT_CHECK_CODES = [
  "IMG_ALT_MISSING", "IMG_ALT_FILENAME", "IMG_ALT_EMPTY", "HEADING_EMPTY", "HEADING_SKIPPED_LEVEL", "LINK_VAGUE", "LINK_EMPTY",
  "TABLE_HEADERS_MISSING", "ASSIGNMENT_RUBRIC_MISSING", "ITEM_UNAVAILABLE", "ITEM_UNPUBLISHED",
  "PAGE_TOO_LONG", "PAGE_LONG_NO_HEADINGS", "PARAGRAPH_TOO_LONG", "READING_LEVEL_HIGH", "PAGE_NEARLY_EMPTY",
  "ASSIGNMENT_INSTRUCTIONS_SHORT", "ASSIGNMENT_POINTS_MISSING", "LIST_TYPED_AS_TEXT", "COLOR_ONLY_REFERENCE",
];
/** Course-level checks only apply to course criteria; page/assignment checks only to page and assignment criteria. */
export const COURSE_CHECK_CODES = [
  "COURSE_START_MISSING", "MODULE_EMPTY", "MODULE_NAMING_INCONSISTENT", "MODULE_UNPUBLISHED", "CONTENT_NOT_IN_MODULE",
  "ASSIGNMENT_DUE_DATE_MISSING", "ASSIGNMENT_NOT_ALIGNED", "COURSE_COMPETENCIES_MISSING",
  "MODULE_NO_OVERVIEW", "MODULE_TOO_MANY_ITEMS", "MODULE_NO_ASSESSMENT", "WORKLOAD_IMBALANCE", "ASSIGNMENTS_DUE_SAME_DAY",
  "OUTCOME_VAGUE_VERB", "OUTCOME_NOT_ASSESSED", "OUTCOMES_LOWER_ORDER_ONLY", "EARLY_LOW_STAKES_MISSING", "OUTCOME_ASSESSMENT_LEVEL_MISMATCH",
];
export const CHECK_CODES = [...ELEMENT_CHECK_CODES, ...COURSE_CHECK_CODES];
export const RATING_LABELS = [
  "Not demonstrated",
  "Partially demonstrated",
  "Meets expectations",
  "Exceeds expectations",
];

export type TaskStatus =
  | "Draft"
  | "NeedsRevision"
  | "InProgress"
  | "ReadyForReview"
  | "ChangesRequested"
  | "Blocked"
  | "Withdrawn"
  | "NotApplicable"
  | "Resolved";
export interface TaskFindingSummary {
  findingId: string;
  reviewId: string;
  criterionCode: string | null;
  title: string;
  severity: Severity;
  recommendation: string;
  evidence: string;
}
export interface TaskCommentEntry {
  id: string;
  authorName: string;
  body: string;
  createdAt: string;
}
export interface TaskEventEntry {
  id: string;
  actorName: string;
  fromStatus: string | null;
  toStatus: string;
  reason: string | null;
  createdAt: string;
}
export interface TaskAction {
  toStatus: string;
  label: string;
  reasonRequired: boolean;
}
export interface RevisionTask {
  id: string;
  courseId: string;
  courseTitle: string;
  elementId: string;
  elementType: string;
  elementTitle: string;
  elementSourceUrl: string | null;
  title: string;
  instructions: string;
  priority: Severity;
  status: TaskStatus;
  blockedFromStatus: string | null;
  revision: string;
  assigneeId: string;
  assigneeName: string;
  reviewerName: string;
  createdAt: string;
  dueAt: string | null;
  resolvedAt: string | null;
  sourceSnapshot: Review["snapshot"];
  currentSnapshotId: string;
  snapshotChanged: boolean;
  isDemoCourse: boolean;
  findings: TaskFindingSummary[];
  comments: TaskCommentEntry[];
  events: TaskEventEntry[];
  availableActions: TaskAction[];
  canComment: boolean;
}
export interface TaskSummary {
  id: string;
  elementId: string;
  elementType: string;
  elementTitle: string;
  title: string;
  priority: Severity;
  status: TaskStatus;
  assigneeName: string;
  dueAt: string | null;
  createdAt: string;
  findingCount: number;
  snapshotChanged: boolean;
  isMine: boolean;
}
export interface Comparison {
  source: Review["snapshot"];
  latest: Review["snapshot"];
  changed: boolean;
  textDiff: { kind: "Same" | "Added" | "Removed"; text: string }[];
  metadataChanges: string[];
}
export const PRIORITIES: Severity[] = ["Low", "Medium", "High", "Critical"];

export type TicketStatus = "New" | "Triaged" | "InProgress" | "WaitingOnInstructor" | "Resolved" | "Closed";
export const CATEGORIES_SUPPORT = ["Canvas", "Accessibility", "CourseDesign", "Multimedia", "Other"];
export interface TicketCourseLink {
  courseId: string;
  courseTitle: string;
  courseCode: string;
  elementId: string | null;
  elementTitle: string | null;
  revisionTaskId: string | null;
  revisionTaskTitle: string | null;
}
export interface ResourceRef {
  id: string;
  title: string;
  slug: string | null;
}
export interface TicketComment {
  id: string;
  authorName: string;
  body: string;
  createdAt: string;
}
export interface TicketEvent {
  id: string;
  actorName: string;
  fromStatus: string | null;
  toStatus: string;
  reason: string | null;
  createdAt: string;
}
export interface TicketAction {
  toStatus: string;
  label: string;
  reasonRequired: boolean;
}
export interface SupportTicket {
  id: string;
  requesterId: string;
  requesterName: string;
  assigneeId: string | null;
  assigneeName: string | null;
  category: string;
  priority: Severity;
  status: TicketStatus;
  title: string;
  description: string;
  createdAt: string;
  closedAt: string | null;
  revision: string;
  linkedCourse: TicketCourseLink | null;
  recommendedArticle: ResourceRef | null;
  recommendedWorkshop: ResourceRef | null;
  comments: TicketComment[];
  events: TicketEvent[];
  availableActions: TicketAction[];
  canComment: boolean;
  canAssign: boolean;
  canRecommend: boolean;
}
export interface SupportTicketSummary {
  id: string;
  title: string;
  category: string;
  priority: Severity;
  status: TicketStatus;
  requesterName: string;
  assigneeName: string | null;
  createdAt: string;
  hasCourseLink: boolean;
  isMine: boolean;
}
export interface KnowledgeArticle {
  id: string;
  title: string;
  slug: string;
  body: string;
  tags: string[];
  status: "Draft" | "Published";
  version: number;
  authorName: string;
  createdAt: string;
  updatedAt: string | null;
  publishedAt: string | null;
  revision: string;
  canEdit: boolean;
}
export interface KnowledgeArticleSummary {
  id: string;
  title: string;
  slug: string;
  tags: string[];
  status: "Draft" | "Published";
  publishedAt: string | null;
}
export type Modality = "InPerson" | "Online" | "Hybrid";
export const MODALITIES: Modality[] = ["InPerson", "Online", "Hybrid"];
export interface WorkshopRegistrationEntry {
  userId: string;
  displayName: string;
  status: "Registered" | "Waitlisted" | "Cancelled";
  registeredAt: string;
}
export interface Workshop {
  id: string;
  title: string;
  description: string;
  startsAt: string;
  endsAt: string;
  modality: Modality;
  capacity: number | null;
  registeredCount: number;
  waitlistedCount: number;
  myStatus: "Registered" | "Waitlisted" | null;
  canManage: boolean;
  registrations: WorkshopRegistrationEntry[];
}

export interface CompetencyMapping {
  elementId: string;
  elementType: string;
  elementTitle: string;
}
export interface Competency {
  id: string;
  code: string;
  title: string;
  description: string;
  revision: string;
  mappings: CompetencyMapping[];
}
export interface ReportScope {
  description: string;
  generatedAt: string;
  caveats: string[];
}
export interface MappedCompetency {
  id: string;
  code: string;
  title: string;
  elementTitles: string[];
}
export interface OrphanCompetency {
  id: string;
  code: string;
  title: string;
}
export interface UnmappedAssessment {
  elementId: string;
  title: string;
}
export interface AlignmentReport {
  scope: ReportScope;
  totalCompetencies: number;
  totalAssessments: number;
  mappedAssessmentCount: number;
  coveragePercent: number | null;
  competencies: MappedCompetency[];
  orphanCompetencies: OrphanCompetency[];
  unmappedAssessments: UnmappedAssessment[];
}
export interface CategoryAverage {
  category: string;
  averagePercent: number | null;
  ratedReviews: number;
}
export interface OutstandingFinding {
  findingId: string;
  reviewId: string;
  elementId: string;
  elementTitle: string;
  title: string;
  severity: Severity;
  createdAt: string;
  hasOpenTask: boolean;
}
export interface CourseQualityReport {
  scope: ReportScope;
  reviewableElements: number;
  elementsWithPublishedReview: number;
  reviewedCoveragePercent: number | null;
  averageScorePercent: number | null;
  categoryAverages: CategoryAverage[];
  outstandingFindings: OutstandingFinding[];
}
export interface SupportTheme {
  category: string;
  count: number;
}
export interface TaskStatusCount {
  status: string;
  count: number;
  averageAgeDays: number | null;
}
export interface CtlOverviewReport {
  scope: ReportScope;
  accessibleCourses: number;
  publishedReviews: number;
  outstandingFindings: number;
  tasksByStatus: TaskStatusCount[];
  supportThemes: SupportTheme[];
}
export interface AgingTask {
  id: string;
  title: string;
  courseTitle: string;
  status: string;
  daysOpen: number;
  priority: Severity;
}
export interface RevisionReport {
  scope: ReportScope;
  tasksByStatus: TaskStatusCount[];
  openTasks: AgingTask[];
}

/** A seeded fictional account offered for one-click sign-in (Development, loopback only; ADR 012). No credential is included. */
export interface DemoAccount {
  email: string;
  displayName: string;
  roles: Role[];
}
/** The hosted public demo: one-click sign-in only, no uploads or role changes, and sample data reset every `resetHours`. */
export interface DemoMode {
  publicDemo: boolean;
  resetHours: number;
}
/** Graded or practice work students submit; quizzes are reviewed with a rubric's assignment criteria. */
export const isAssessment = (type: string) => type === "assignment" || type === "quiz";
/** Course content that can be reviewed: pages, assignments, quizzes and discussions. */
export const isContent = (type: string) => type === "page" || type === "discussion" || isAssessment(type);
/** Labels for element types shown to people. */
export const typeLabel = (type: string) =>
  ({ page: "Page", assignment: "Assignment", quiz: "Quiz", discussion: "Discussion", module: "Module", moduleItem: "Module item" })[type] ?? type;
export type DeliveryMode = "Online" | "Hybrid" | "FaceToFace";
export const DELIVERY_MODES: { value: DeliveryMode; label: string }[] = [
  { value: "Online", label: "Online" },
  { value: "Hybrid", label: "Hybrid" },
  { value: "FaceToFace", label: "Face-to-face" },
];
export const ASPECTS: { value: string; label: string }[] = [
  { value: "GettingStartedAndNavigation", label: "Getting started and navigation" },
  { value: "OutcomesAndAlignment", label: "Outcomes and alignment" },
  { value: "AssessmentAndFeedback", label: "Assessment and feedback" },
  { value: "ContentAndCognitiveLoad", label: "Content and cognitive load" },
  { value: "Accessibility", label: "Accessibility" },
];
export interface CheckGuidance {
  why: string;
  howToFix: string;
  example: string;
}
export interface CheckParameter {
  key: string;
  label: string;
  kind: "Number" | "WordList";
  defaultNumber: number | null;
  defaultWords: string[] | null;
  min: number | null;
  max: number | null;
  maxItems: number;
  maxItemLength: number;
}
export interface CheckCatalogEntry {
  code: string;
  name: string;
  aspect: string;
  target: "Element" | "Course";
  defaultSeverity: Severity;
  parameters: CheckParameter[];
  defaultGuidance: CheckGuidance;
}
export interface StandardCheckSetting {
  code: string;
  enabled: boolean;
  severity: Severity;
  required: boolean;
  numbers: Record<string, number>;
  lists: Record<string, string[]>;
  guidance: CheckGuidance;
}
export interface StandardSetVersionSummary {
  id: string;
  versionNumber: number;
  status: "Draft" | "Published";
  createdAt: string;
  publishedAt: string | null;
  enabledChecks: number;
  requiredChecks: number;
}
export interface StandardSet {
  id: string;
  name: string;
  deliveryMode: DeliveryMode | null;
  department: string | null;
  scope: string;
  versions: StandardSetVersionSummary[];
}
export interface StandardSetVersion {
  id: string;
  setId: string;
  setName: string;
  scope: string;
  versionNumber: number;
  status: "Draft" | "Published";
  revision: string;
  notes: string;
  createdAt: string;
  publishedAt: string | null;
  publishedByName: string | null;
  checks: StandardCheckSetting[];
  endorsement: EndorsementRequirements;
}
/** Which reviews an endorsement under a standard-set version needs (ADR 013 amendment). */
export interface EndorsementRequirements {
  courseReview: boolean;
  rubricProfileId: string | null;
  minimumItemReviews: number;
}
export interface AppliedStandards {
  /** Null when no published set applies and TeachWell's built-in defaults are used. */
  setId: string | null;
  versionId: string | null;
  name: string;
  versionNumber: number | null;
  scope: string;
  reason: string;
  publishedAt: string | null;
}
export interface CourseStandards {
  courseId: string;
  deliveryMode: DeliveryMode | null;
  department: string | null;
  program: string | null;
  canClassify: boolean;
  applied: AppliedStandards;
}
export interface StandardsPreviewRow {
  code: string;
  name: string;
  aspect: string;
  enabled: boolean;
  current: number;
  proposed: number;
}
export interface StandardsPreview {
  courseId: string;
  courseTitle: string;
  current: AppliedStandards;
  elementsChecked: number;
  courseStructureChecked: boolean;
  currentTotal: number;
  proposedTotal: number;
  rows: StandardsPreviewRow[];
  limitations: string[];
}
