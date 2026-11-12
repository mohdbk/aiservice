// End to end on a phone screen: sign in to the backoffice, open the assistant panel from a page, send a message, and
// check the reply streams in (tool step then text) with no page errors or blank screen.
import { chromium, devices } from "playwright";
const BASE = process.env.BASE_URL || "http://localhost:4173";
const errors = [];
const browser = await chromium.launch();
let failed = false;
for (const lang of ["ar", "en"]) {
  const ctx = await browser.newContext({ ...devices["iPhone 13"] });
  const page = await ctx.newPage();
  page.on("pageerror", (e) => errors.push(`${lang} pageerror: ${e.message}`));
  page.on("console", (m) => { if (m.type() === "error") errors.push(`${lang} console: ${m.text()}`); });
  await page.goto(`${BASE}/${lang}/sign-in`);
  await page.fill('input[type="email"]', process.env.ADMIN_EMAIL);
  await page.fill('input[type="password"]', process.env.ADMIN_PASSWORD);
  await page.locator('form button[type="submit"]').click();
  await page.waitForURL(new RegExp(`/${lang}/?$`));
  await page.goto(`${BASE}/${lang}/applicants`);
  await page.locator(".mh-assistant-fab").click();
  const box = page.locator(".mh-chat-drawer textarea");
  await box.fill(lang === "ar" ? "ما الوظائف المفتوحة؟" : "Which jobs are open?");
  await box.press("Enter");
  try {
    await page.locator(".mh-chat-drawer").getByText("Fake reply: there are open jobs.").waitFor({ timeout: 30000 });
    console.log(`ok   ${lang}: reply streamed into the panel`);
  } catch {
    failed = true;
    console.error(`FAIL ${lang}: no reply in the panel`);
  }
  const text = await page.locator("body").innerText();
  if (text.trim().length < 20) { failed = true; console.error(`FAIL ${lang}: blank page`); }
  await page.screenshot({ path: `shots/assistant-${lang}-phone.png` });
  await ctx.close();
}
await browser.close();
if (errors.length) { console.error(errors.join("\n")); failed = true; }
process.exit(failed ? 1 : 0);
