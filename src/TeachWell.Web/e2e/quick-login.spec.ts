import { expect, test } from "@playwright/test";

// The runner enables Features:DemoQuickLogin and connects over loopback, so the sign-in screen offers one-click demo accounts.
test("demo accounts sign in with one click and no password", async ({ page }) => {
  await page.goto("/");
  const accounts = page.getByRole("region", { name: "Choose a demo account" });
  await expect(accounts.getByRole("button")).toHaveCount(4);
  await expect(accounts.getByRole("button", { name: /Dana Brooks\s*Academic administrator/ })).toBeVisible();
  await expect(accounts.getByRole("button", { name: /Jordan Lee\s*CTL reviewer \+ Instructor|Jordan Lee\s*Instructor \+ CTL reviewer/ })).toBeVisible();
  // The password form remains available as a fallback, empty.
  await expect(page.getByLabel("Password")).toHaveValue("");

  await accounts.getByRole("button", { name: /Alex Morgan/ }).click();
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
  await expect(page.getByText("Alex Morgan").first()).toBeVisible();
  // One role: the sidebar names it, with nothing to switch.
  await expect(page.getByRole("combobox", { name: "Workspace role" })).toHaveCount(0);
  await expect(page.getByRole("complementary").getByText("Instructor", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("region", { name: "Choose a demo account" })).toBeVisible();
});
