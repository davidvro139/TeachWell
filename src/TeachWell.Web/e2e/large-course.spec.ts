import { expect, test, type Page } from "@playwright/test";
import { authenticate } from "./auth";
import { largeCourse } from "./largeCourse";

// A full-size course: over 500 items in 16 weekly modules. Checks that import, the course page, the improvement guide, search and the
// mapping grid stay usable and reasonably fast, and that nothing pushes the page wider than the screen. Imported by Jordan Lee as an
// instructor so the other journeys' instructor (Alex Morgan) keeps the same courses.
const course = largeCourse();
const noSidewaysScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth);

test("a full-size course of over 500 items imports, opens and stays usable", async ({ page, context }) => {
  test.setTimeout(300_000);
  const timings: Record<string, number> = {};
  const time = async (name: string, run: () => Promise<unknown>) => {
    const start = Date.now();
    await run();
    timings[name] = Date.now() - start;
  };

  await authenticate(context, "reviewer");
  await page.goto("/");
  await page.getByRole("combobox", { name: "Workspace role" }).selectOption("Instructor");
  await page.getByRole("navigation", { name: "Workspace sections" }).getByRole("button", { name: "Import courses", exact: true }).click();
  const panel = page.getByRole("region", { name: "Import a course export" });
  await panel.getByLabel(/Course export/).setInputFiles({ name: "data-900-large-sample.imscc", mimeType: "application/zip", buffer: course.bytes });
  await time("import", async () => {
    await panel.getByRole("button", { name: "Upload and import" }).click();
    await expect(panel.getByText(/^Imported Data Literacy for Everyone/)).toBeVisible({ timeout: 120_000 });
  });

  // Opening the course: the header at once, the guide once its checks have run.
  await time("open course and run the guide", async () => {
    await panel.getByRole("button", { name: "Open course" }).click();
    await expect(page.getByRole("heading", { level: 1, name: course.title })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("Select an area to see only its steps")).toBeVisible({ timeout: 60_000 });
  });
  expect(await noSidewaysScroll(page)).toBe(true);

  // The guide finds the planted problems and counts every place: an image with no text alternative on readings 5, 10 and 15 of
  // each of the 16 weeks is exactly 48 places.
  const step = page.getByRole("button", { name: /Image has no text alternative/ }).first();
  await expect(step).toContainText(/\d+ places/);
  const places = Number((await step.textContent())!.match(/(\d+) places/)![1]);
  expect(places).toBe(48);
  await time("expand a step with many places", async () => {
    await step.click();
    await expect(page.locator(".guide-step-body").first()).toBeVisible();
  });
  // Places follow the course: week 1's readings first, in order, not storage order.
  const firstPlaces = page.locator(".guide-step-body").first().getByText(/^Page: Week \d+ reading \d+/);
  await expect(firstPlaces.nth(0)).toHaveText(/^Page: Week 1 reading 5:/);
  await expect(firstPlaces.nth(1)).toHaveText(/^Page: Week 1 reading 10:/);
  await expect(firstPlaces.nth(3)).toHaveText(/^Page: Week 2 reading 5:/);
  expect(await noSidewaysScroll(page)).toBe(true);

  // Search across the whole course, including a very long title.
  const sections = page.getByRole("navigation", { name: "On this page" });
  await sections.getByRole("button", { name: "Course content" }).click();
  await time("search the course content", async () => {
    await page.getByRole("searchbox", { name: "Find a page or assignment" }).fill("extended case study");
    await expect(page.getByText(course.longTitle).first()).toBeVisible();
  });
  expect(await noSidewaysScroll(page)).toBe(true);

  // The mapping grid with 144 assessments and a new competency.
  await sections.getByRole("button", { name: "Alignment and quality" }).click();
  await page.getByRole("button", { name: "Add competency" }).click();
  await page.getByLabel("Code").fill("COMP-DATA");
  await page.getByLabel("Title").fill("Reason with data");
  await page.getByRole("button", { name: "Add competency" }).click();
  await expect(page.getByText("Competency added.")).toBeVisible();
  await time("open the mapping grid", async () => {
    await page.getByRole("button", { name: "Map assessments" }).click();
    await expect(page.getByRole("region", { name: "Map assessments to competencies" })).toBeVisible({ timeout: 30_000 });
  });
  const grid = page.getByRole("region", { name: "Map assessments to competencies" });
  await expect(grid.getByRole("checkbox", { name: "Week 16 project: COMP-DATA" })).toBeVisible();
  expect(await grid.getByRole("checkbox", { name: /: COMP-DATA$/ }).count()).toBeGreaterThanOrEqual(144);
  expect(await noSidewaysScroll(page)).toBe(true);

  // Phone width: still no sideways scrolling.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(300);
  expect(await noSidewaysScroll(page)).toBe(true);

  // Generous limits: this guards against a slowdown that would make the course unusable, not a benchmark.
  console.log("LARGE COURSE TIMINGS (ms): " + JSON.stringify(timings));
  expect(timings["import"]).toBeLessThan(90_000);
  expect(timings["open course and run the guide"]).toBeLessThan(30_000);
  expect(timings["search the course content"]).toBeLessThan(5_000);
  expect(timings["open the mapping grid"]).toBeLessThan(10_000);
});
