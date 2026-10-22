import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("instructor reads a term's explanation in the guide, follows it to the glossary and the CTL's article, and asks the CTL about a term", async ({
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: "Foundations of Web Development" }) })
    .getByRole("button", { name: "Explore course" })
    .click();
  const step = page.getByRole("button", { name: /Image has no text alternative/ }).first();
  await step.click();
  const body = page.locator(".guide-step-body").first();

  // The standards reference sits under the reason, not inside it (ADR 016).
  await expect(body.locator(".guide-reference")).toHaveText("Based on: WCAG 2.2, 1.1.1 Non-text Content");
  await expect(body.getByText("(WCAG", { exact: false })).toHaveCount(0);

  // Terms in the guidance are underlined buttons; activating one shows the explanation and outside reading.
  const wcag = body.getByRole("button", { name: "WCAG 2.2" });
  await expect(wcag).toHaveAttribute("aria-expanded", "false");
  await wcag.click();
  const popover = page.getByRole("dialog", { name: "WCAG" });
  await expect(popover).toContainText("Web Content Accessibility Guidelines");
  const outside = popover.getByRole("link", { name: /WCAG overview/ });
  await expect(outside).toHaveAttribute("href", "https://www.w3.org/WAI/standards-guidelines/wcag/");
  await expect(outside).toHaveAttribute("target", "_blank");
  await expect(outside).toHaveAttribute("rel", "noopener noreferrer");
  // Escape closes it and returns focus to the term.
  await page.keyboard.press("Escape");
  await expect(popover).toHaveCount(0);
  await expect(wcag).toBeFocused();

  // From a term to the CTL's own material: the glossary lists the seeded alt-text article (tagged "images").
  await body.getByRole("button", { name: "alt text", exact: true }).click();
  await page.getByRole("dialog", { name: "Alt text" }).getByRole("button", { name: /From your CTL/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Glossary" })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Workspace sections" }).getByRole("button", { name: "Glossary" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  const entry = page.getByRole("region", { name: "Alt text" });
  await expect(entry).toHaveClass(/is-target/);
  await expect(entry.getByRole("link", { name: /Images tutorial/ })).toBeVisible();
  await entry.getByRole("button", { name: "Writing useful image alt text" }).click();
  await expect(page.getByRole("heading", { name: "Writing useful image alt text" })).toBeVisible();

  // Search the glossary and ask the CTL: the support form opens with the question filled in, ready to send.
  await page.getByRole("navigation", { name: "Workspace sections" }).getByRole("button", { name: "Glossary" }).click();
  await page.getByRole("searchbox", { name: "Search the glossary" }).fill("bloom");
  const blooms = page.getByRole("region", { name: "Bloom's taxonomy" });
  await expect(blooms).toBeVisible();
  await expect(page.getByRole("region", { name: "Alt text" })).toHaveCount(0);
  await expect(blooms).toContainText("Your CTL has not published an article or workshop on this yet.");
  await blooms.getByRole("button", { name: "Ask the CTL about this" }).click();
  await expect(page.getByRole("heading", { name: "New support ticket" })).toBeVisible();
  await expect(page.getByLabel("Title")).toHaveValue("Question about Bloom's taxonomy");
  await expect(page.getByLabel("Category")).toHaveValue("CourseDesign");
  await page.getByRole("button", { name: "Cancel" }).click();
});

test("CTL rewords a term, adds the college's own and hides one; instructors see the result at once; the CTL undoes it all", async ({
  browser,
  page,
  context,
}) => {
  await authenticate(context, "reviewer");
  await page.goto("/");
  await page.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
  await page.getByRole("navigation", { name: "Workspace sections" }).getByRole("button", { name: "Glossary" }).click();

  // Reword a built-in term.
  const wcag = page.getByRole("region", { name: "WCAG" });
  await wcag.getByRole("button", { name: "Edit WCAG" }).click();
  await wcag.getByLabel("Definition").fill("The accessibility standard our college follows: WCAG 2.1 level AA for all course content.");
  await wcag.getByRole("button", { name: "Save term" }).click();
  await expect(page.getByText("Saved “WCAG”.")).toBeVisible();
  await expect(wcag).toContainText("WORDED BY YOUR CTL");

  // Add a term of the college's own, with a link.
  await page.getByRole("button", { name: "Add a term" }).click();
  const editor = page.getByRole("form", { name: "Add a term" });
  await editor.getByLabel("Term", { exact: true }).fill("Course shell");
  await editor.getByLabel("Definition").fill("The empty Canvas course the registrar creates for each section every term.");
  await editor.getByLabel(/Phrases to explain/).fill("course shell\ncourse shells");
  await expect(editor.getByText("course-shell")).toBeVisible();
  await editor.getByRole("button", { name: "Add a link" }).click();
  await editor.getByLabel("Title").fill("Requesting a course shell");
  await editor.getByLabel("Web address").fill("javascript:alert(1)");
  await editor.getByRole("button", { name: "Save term" }).click();
  // The browser's own URL check or the API rejects it; either way nothing is saved.
  await expect(page.getByRole("region", { name: "Course shell" })).toHaveCount(0);
  await editor.getByLabel("Web address").fill("https://ctl.example.edu/course-shells");
  await editor.getByRole("button", { name: "Save term" }).click();
  const shell = page.getByRole("region", { name: "Course shell" });
  await expect(shell).toContainText("ADDED BY YOUR CTL");
  await expect(shell.getByRole("link", { name: /Requesting a course shell/ })).toHaveAttribute("href", "https://ctl.example.edu/course-shells");

  // Hide a built-in term.
  await page.getByRole("region", { name: "Scaffolding" }).getByRole("button", { name: /Hide from instructors/ }).click();
  await expect(page.getByText("“Scaffolding” is hidden from instructors.")).toBeVisible();
  const hidden = page.getByRole("region", { name: "Hidden terms" });
  await expect(hidden).toContainText("Scaffolding");

  // Instructors see the college's glossary straight away, including in the guide's explanations.
  const ownerContext = await browser.newContext({ ignoreHTTPSErrors: true });
  try {
    await authenticate(ownerContext, "instructor");
    const owner = await ownerContext.newPage();
    await owner.goto("/");
    await owner.getByRole("navigation", { name: "Workspace sections" }).getByRole("button", { name: "Glossary" }).click();
    await expect(owner.getByRole("region", { name: "Course shell" })).toContainText("registrar");
    await expect(owner.getByRole("region", { name: "Scaffolding" })).toHaveCount(0);
    await expect(owner.getByRole("button", { name: /Edit/ })).toHaveCount(0);
    await owner.getByRole("navigation", { name: "Workspace sections" }).getByRole("button", { name: "Courses", exact: true }).click();
    await owner
      .getByRole("article")
      .filter({ has: owner.getByRole("heading", { name: "Foundations of Web Development" }) })
      .getByRole("button", { name: "Explore course" })
      .click();
    await owner.getByRole("button", { name: /Image has no text alternative/ }).first().click();
    await owner.locator(".guide-step-body").first().getByRole("button", { name: "WCAG 2.2" }).click();
    const popover = owner.getByRole("dialog", { name: "WCAG" });
    await expect(popover).toContainText("WCAG 2.1 level AA for all course content");
    await expect(popover).toContainText("Worded by your CTL");
  } finally {
    await ownerContext.close();
  }

  // Undo everything, so the glossary is TeachWell's own again.
  await wcag.getByRole("button", { name: /Return to TeachWell's wording/ }).click();
  await expect(page.getByText("“WCAG” uses TeachWell's wording again.")).toBeVisible();
  await expect(wcag).toContainText("Web Content Accessibility Guidelines");
  await shell.getByRole("button", { name: "Remove Course shell" }).click();
  await expect(page.getByRole("region", { name: "Course shell" })).toHaveCount(0);
  await hidden.getByRole("button", { name: "Show again Scaffolding" }).click();
  await expect(page.getByRole("region", { name: "Scaffolding" })).toBeVisible();
  await expect(page.getByRole("region", { name: "Hidden terms" })).toHaveCount(0);
});
