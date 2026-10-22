import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

// Uses DES 120 so the WEB 101 guide journey (guide.spec) is unaffected; does not register for the seeded workshop, which support.spec uses.
test("CTL links a job aid to a check and the instructor reads it inside the guide step", async ({ browser, page, context }) => {
  await authenticate(context, "reviewer");
  await page.goto("/");
  await page.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
  await page.getByRole("button", { name: "Standards", exact: true }).click();
  const panel = page.getByRole("region", { name: "Resources in the improvement guide" });
  await panel.getByLabel("Check").selectOption({ label: "No course competencies are defined" });
  await panel.getByLabel("Resource").selectOption({ label: "Writing useful image alt text" });
  await panel.getByRole("button", { name: "Link resource" }).click();
  await expect(panel.getByText("Resource linked.", { exact: false })).toBeVisible();
  await expect(panel.getByText("Shown to instructors")).toBeVisible();

  const ownerContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  try {
    await authenticate(ownerContext, "instructor");
    const owner = await ownerContext.newPage();
    await owner.goto("/");
    await owner
      .getByRole("article")
      .filter({ has: owner.getByRole("heading", { name: "Designing for Everyone" }) })
      .getByRole("button", { name: "Explore course" })
      .click();
    await owner.getByRole("button", { name: /No course competencies are defined/ }).click();
    await expect(owner.getByRole("heading", { name: "Learn more from the CTL" })).toBeVisible();
    await owner.getByRole("button", { name: "Writing useful image alt text" }).click();
    await expect(owner.getByText("Alt text should describe the purpose of an image", { exact: false })).toBeVisible();
  } finally {
    await ownerContext.close();
  }

  await panel.getByRole("button", { name: /^Unlink Writing useful image alt text/ }).click();
  await expect(panel.getByText("No resources are linked yet.")).toBeVisible();
});
