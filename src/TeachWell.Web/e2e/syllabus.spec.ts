import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

// The screens added by ADR 018 and ADR 019, none of which had browser coverage: the syllabus panel, the WCAG
// criterion on a guide step, and the college-wide criterion rollup on oversight.
//
// The mutating part runs on DES 120 rather than WEB 101. Twelve specs assert against WEB 101's guide and readiness,
// and this suite shares one database sequentially, so changing WEB 101's syllabus could destabilise them. DES 120 is
// face-to-face, so it runs the college-wide standards where the syllabus check is not enabled, and its guide is
// unaffected either way. What the check itself does with pasted text is covered by SyllabusPasteTests; what this
// spec adds is that the panel renders and works.

async function openCourse(page: import("@playwright/test").Page, title: string) {
  await page.goto("/");
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: title }) })
    .getByRole("button", { name: "Explore course" })
    .click();
}

const complete = [
  "DES 120 Syllabus",
  "Contact me through Canvas; student hours are Thursdays 10am to 12pm.",
  "Accessibility: if you need an accommodation, contact Disability Services and tell me what helps.",
  "Grading: the grade breakdown is 50% studio work, 30% critique, 20% reflection, on the college grading scale.",
  "Late work: every project has a 48-hour grace period, then talk to me about an extension.",
  "Academic integrity: credit every source you build on. Submitted work must be your own work.",
].join("\n");

test("instructor pastes a syllabus, edits it and removes it, and the panel reports where it came from", async ({
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  await openCourse(page, "Designing for Everyone");

  const panel = page.getByRole("region", { name: "Syllabus" });
  await expect(panel).toBeVisible();
  // Seeded from the Canvas syllabus page, with the word count the checks actually measured.
  await expect(panel.getByText(/^From the Canvas syllabus page ·/)).toBeVisible();
  await expect(panel.getByText(/words checked/)).toBeVisible();
  await expect(panel.getByRole("button", { name: "Remove" })).toHaveCount(0);

  // Paste: nothing is saved until Save syllabus.
  await panel.getByRole("button", { name: "Paste syllabus text" }).click();
  const box = panel.getByLabel("Syllabus text");
  await expect(box).toBeVisible();
  await panel.getByRole("button", { name: "Cancel" }).click();
  await expect(panel.getByText(/^From the Canvas syllabus page ·/)).toBeVisible();

  // An empty paste is refused by the server and the panel says so rather than failing silently.
  await panel.getByRole("button", { name: "Paste syllabus text" }).click();
  await panel.getByLabel("Syllabus text").fill("   ");
  await expect(panel.getByRole("button", { name: "Save syllabus" })).toBeDisabled();

  await panel.getByLabel("Syllabus text").fill(complete);
  await panel.getByRole("button", { name: "Save syllabus" }).click();
  // Saved, attributed, and explicit that a re-import will not overwrite it.
  await expect(panel.getByText(/^Pasted by Alex Morgan/)).toBeVisible();
  await expect(panel.getByText("importing the course again will not change or remove it", { exact: false })).toBeVisible();

  // Re-opening the editor shows the stored text back to the owner.
  await panel.getByRole("button", { name: "Edit pasted syllabus" }).click();
  await expect(panel.getByLabel("Syllabus text")).toHaveValue(/Accessibility: if you need an accommodation/);
  await panel.getByRole("button", { name: "Cancel" }).click();

  // It survives leaving and coming back, so it really was stored rather than held in the page. The view is not in the
  // URL, so a bare reload would land on the course list; navigating in again also forces a fresh fetch.
  await openCourse(page, "Designing for Everyone");
  const reloaded = page.getByRole("region", { name: "Syllabus" });
  await expect(reloaded.getByText(/^Pasted by Alex Morgan/)).toBeVisible();

  // Removing it hands the course back to the importer and says the syllabus is no longer there to check.
  await reloaded.getByRole("button", { name: "Remove" }).click();
  await expect(reloaded.getByText(/^No syllabus in TeachWell,/)).toBeVisible();
  await expect(reloaded.getByRole("button", { name: "Remove" })).toHaveCount(0);

  // Leave the course with a syllabus again, so this spec does not depend on running last.
  await reloaded.getByRole("button", { name: "Paste syllabus text" }).click();
  await reloaded.getByLabel("Syllabus text").fill(complete);
  await reloaded.getByRole("button", { name: "Save syllabus" }).click();
  await expect(reloaded.getByText(/^Pasted by Alex Morgan/)).toBeVisible();
});

test("a guide step names the WCAG criterion it relates to, and a course-design step names none", async ({
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  await openCourse(page, "Foundations of Web Development");

  // An accessibility step carries its criterion, so an instructor sees what the fix is for (ADR 018).
  const alt = page.getByRole("button", { name: /Image has no text alternative/ }).first();
  await alt.click();
  const altBody = page.locator(".guide-step-body").first();
  await expect(altBody.locator(".guide-criteria")).toHaveText("Relates to: WCAG 2.1 1.1.1 Non-text Content (Level A)");
  // The standards reference from the guidance text is a separate line, and stayed separate (ADR 016).
  await expect(altBody.locator(".guide-reference")).toHaveCount(1);
  await alt.click();

  // An assessment step relates to no success criterion, and must not imply otherwise. The missing-rubric step is used
  // rather than the competencies one because reports.spec.ts sorts earlier and maps a competency on this course,
  // which removes that step before this spec runs.
  const rubric = page.getByRole("button", { name: /Assignment has no attached rubric/ }).first();
  await rubric.click();
  const rubricBody = page.locator(".guide-step-body").filter({ hasText: "Why it matters" }).first();
  await expect(rubricBody).toBeVisible();
  await expect(rubricBody.locator(".guide-criteria")).toHaveCount(0);
});

test("the academic administrator sees open accessibility work grouped by WCAG criterion", async ({ page, context }) => {
  await authenticate(context, "academic");
  await page.goto("/");

  const rollup = page.getByRole("region", { name: "Accessibility work by WCAG criterion" });
  await expect(rollup).toBeVisible();
  // It states what it cannot show before any figure is read.
  await expect(rollup.getByText("it cannot show that a criterion is met", { exact: false })).toBeVisible();

  const header = rollup.getByRole("row").first();
  await expect(header).toContainText("Success criterion");
  await expect(header).toContainText("Courses with open work");
  // One row per mapped criterion, each naming its level.
  await expect(rollup.getByRole("row").filter({ hasText: "1.1.1 Non-text Content" })).toBeVisible();
  await expect(rollup.getByRole("row").filter({ hasText: "1.2.2 Captions (Prerecorded)" })).toBeVisible();

  // The same figures reach the CSV, with the caveats alongside them.
  const csv = await page.request.get("/api/v1/oversight/export");
  expect(csv.status()).toBe(200);
  const text = await csv.text();
  expect(text).toContain("Success criterion,Name,Level,Courses with open work");
  expect(text).toContain("1.1.1");
  expect(text).toContain("Caveat");
});
