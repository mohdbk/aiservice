// The candidate's whole journey through the careers portal, in a real browser, with HR's side done through the backoffice
// API: create an account, apply with a CV and screening answers, take the timed test (both sections), pick an
// interview time HR offered, and accept the job offer. Then checks the backoffice sees each step. Screenshots go to shots/.
// Usage: BASE_URL=http://127.0.0.1:4173 PORTAL_URL=http://127.0.0.1:8080 node journey.mjs
import { chromium, devices } from "playwright";
import fs from "node:fs";

const BASE = process.env.BASE_URL || "http://127.0.0.1:4173";
const PORTAL = process.env.PORTAL_URL || "http://127.0.0.1:8080";
const stamp = Date.now().toString(36);
const EMAIL = `journey.${stamp}@example.test`;
const PASSWORD = "Journey-Password-2026";
fs.mkdirSync("shots", { recursive: true });

let step = "start";
const ok = (s) => console.log(`ok   ${s}`);
const begin = (s) => { step = s; console.log(`...  ${s}`); };

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });
const page = await ctx.newPage();
page.on("dialog", (d) => d.accept());
const errors = [];
page.on("pageerror", (e) => errors.push(e.message));

// HR's side, through the backoffice API.
const hr = await browser.newContext();
const signIn = await hr.request.post(`${BASE}/api/backoffice/sign-in`, { headers: { "X-Backoffice": "1" }, data: { email: "admin@moet.test", password: "Admin-Password-123" } });
if (!signIn.ok()) throw new Error(`backoffice sign-in ${signIn.status()}`);
const bo = async (method, path, data) => {
  const r = await hr.request.fetch(`${BASE}/api/backoffice${path}`, { method, data, headers: method === "GET" ? {} : { "X-Backoffice": "1" } });
  if (!r.ok()) throw new Error(`${method} ${path} -> ${r.status()} ${(await r.text()).slice(0, 200)}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
};
const shot = (name) => page.screenshot({ path: `shots/journey_${name}.png` });

try {
  // 1. Create an account.
  begin("create an account");
  await page.goto(`${PORTAL}/en/create-account`, { waitUntil: "networkidle" });
  await page.fill("#full_name", "Journey Candidate");
  await page.fill("#email", EMAIL);
  await page.fill("#password", PASSWORD);
  await page.getByRole("button", { name: /Create Account/i }).click();
  await page.waitForLoadState("networkidle");
  if (/check-email|sign-in/.test(page.url())) {
    // Not signed in yet: HR marks the address verified if needed (the stack sends no email), then the candidate signs in.
    if (/check-email/.test(page.url())) {
      const cand = (await bo("GET", `/candidates?q=${encodeURIComponent(EMAIL)}`)).items[0];
      await bo("POST", `/candidates/${cand.id}/verify`, {});
    }
    await page.goto(`${PORTAL}/en/sign-in`, { waitUntil: "networkidle" });
    await page.fill("#email", EMAIL);
    await page.fill("#password", PASSWORD);
    await page.getByRole("button", { name: /^Sign In$/i }).click();
    await page.waitForLoadState("networkidle");
  }
  ok("account created and signed in");

  // 2. Apply for the Senior Data Analyst job.
  begin("apply");
  const listed = await (await page.request.get(`${PORTAL}/api/jobs?lang=en`)).json();
  const jobs = listed.jobs || listed.items || listed.message?.jobs || listed;
  const job = jobs.find((j) => /Senior Data Analyst/.test(j.title || j.job_title || ""));
  if (!job) throw new Error(`job not listed on the portal: ${JSON.stringify(jobs).slice(0, 300)}`);
  const jobId = job.id || job.name || job.code;
  await page.goto(`${PORTAL}/en/jobs/${encodeURIComponent(jobId)}`, { waitUntil: "networkidle" });
  await page.getByRole("link", { name: /^Apply|Apply Now|Apply for/i }).first().click({ timeout: 5000 }).catch(() => page.goto(`${PORTAL}/en/jobs/${encodeURIComponent(jobId)}/apply`));
  await page.waitForLoadState("networkidle");
  await page.locator("#phone").waitFor({ timeout: 15000 });
  await page.fill("#phone", "+971501112233");
  await page.selectOption("#country", { index: 1 });
  fs.writeFileSync("cv.pdf", "%PDF-1.4\n% journey CV\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");
  await page.setInputFiles("#cv", "cv.pdf");
  await page.locator('input[inputmode="decimal"]').first().fill("6");
  await page.locator('form input[type="radio"]').first().check();
  await page.check("#declaration");
  await shot("1_apply");
  await page.getByRole("button", { name: /Submit Application/i }).click();
  await page.waitForURL(/\/applications\//, { timeout: 30000 });
  await page.waitForLoadState("networkidle");
  await shot("2_application");
  const app = (await bo("GET", `/applicants?q=${encodeURIComponent(EMAIL)}`)).items[0];
  if (!app) throw new Error("the application is not in the backoffice");
  ok(`applied (${app.code})`);

  // 3. Take the test the job sends on applying: both sections, every question answered.
  begin("take the test");
  const inv = (await bo("GET", `/applicants/${app.id}`)).invitations.find((i) => i.title.includes("Data") || i.title.includes("البيانات"));
  if (!inv) throw new Error("no test invitation after applying");
  await page.goto(`${PORTAL}/en/assessments/${inv.code}`, { waitUntil: "networkidle" });
  await page.check("#ready");
  await page.getByRole("button", { name: /Start Assessment|Continue Assessment/i }).click();
  await page.locator("#question-title").waitFor();
  // The test clicks Next itself, so answers do not move on automatically.
  await page.getByLabel(/Move to the Next Question Automatically/i).uncheck();
  let shotTaken = false;
  for (let guard = 0; guard < 40; guard++) {
    await page.locator("#question-title").waitFor();
    const radio = page.locator('section input[type="radio"]');
    const box = page.locator('section input[type="checkbox"]');
    const area = page.locator("section textarea");
    const text = page.locator('section input[type="text"], section input[inputmode="decimal"]');
    if (await radio.count()) await radio.first().check();
    else if (await box.count()) await box.first().check();
    else if (await area.count()) await area.first().fill("I would agree indicators with the policy team, take a baseline, and compare with similar firms over a year.");
    else if (await text.count()) await text.first().fill("5");
    await page.waitForTimeout(900); // the answer saves
    if (!shotTaken) {
      await shot("3_test");
      await page.getByRole("button", { name: /Review Questions/i }).click();
      await shot("4_test_review");
      await page.getByRole("button", { name: /Review Questions/i }).click();
      shotTaken = true;
    }
    const next = page.getByRole("button", { name: /^Next/ });
    if (await next.count() && await next.isEnabled()) { await next.click(); continue; }
    await page.getByRole("button", { name: /Finish Section|Submit Assessment/i }).last().click();
    await page.getByRole("button", { name: /Yes, Continue/i }).click();
    await page.waitForLoadState("networkidle");
    await page.waitForTimeout(800);
    if (!/\/take|\/attempt/.test(page.url()) && !(await page.locator("#question-title").count())) break;
  }
  await shot("5_test_done");
  const after = (await bo("GET", `/applicants/${app.id}`)).invitations.find((i) => i.id === inv.id);
  if (!after.attempts.some((a) => a.status !== "InProgress")) throw new Error(`test not submitted: ${JSON.stringify(after.attempts)}`);
  ok("test taken and submitted");

  // 4. HR offers interview times; the candidate picks one.
  begin("book an interview");
  const lookups = await bo("GET", "/lookups");
  const d = new Date(Date.now() + 5 * 86400000).toISOString().slice(0, 10);
  await bo("POST", "/bookings", { application_id: app.id, round_id: lookups.rounds[0].id, location: "Microsoft Teams", panelists: [lookups.staff[0].id],
    slots: [{ date: d, from: "09:00", to: "09:45" }, { date: d, from: "11:00", to: "11:45" }] });
  await page.goto(`${PORTAL}/en/applications`, { waitUntil: "networkidle" });
  await page.locator(`a[href*="/applications/"]`).first().click();
  await page.waitForLoadState("networkidle");
  await page.locator('section[aria-labelledby="book-title"] input[type="radio"]').first().check();
  await shot("6_book");
  await page.getByRole("button", { name: /Confirm This Time/i }).click();
  // Booked: the choice closes and a confirmation with the chosen time shows.
  await page.locator('section[aria-labelledby="book-title"]').waitFor({ state: "detached", timeout: 15000 });
  await page.waitForTimeout(500);
  const booked = (await bo("GET", `/applicants/${app.id}`)).interviews;
  if (!booked?.length) throw new Error("no interview after booking");
  ok("interview time booked");

  // 5. HR sends an offer; the candidate accepts it.
  begin("accept the offer");
  const offer = await bo("POST", "/offers", { application_id: app.id, position_title: "Senior Data Analyst", offer_date: d, start_date: d, monthly_salary: 30000, currency: "AED", terms: "Permanent contract." });
  await bo("POST", `/offers/${offer.id}/status`, { status: "Sent" });
  await page.reload({ waitUntil: "networkidle" });
  await shot("7_offer");
  await page.getByRole("button", { name: /Accept the Offer/i }).click();
  // Accepting is signed by typing the full name.
  await page.fill("#signature", "Journey Candidate");
  await shot("7b_offer_sign");
  await page.getByRole("button", { name: /Sign and Accept/i }).click();
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(800);
  await shot("8_offer_accepted");
  const final = await bo("GET", `/offers/${offer.id}`);
  if (final.status !== "Accepted") throw new Error(`offer is ${final.status}`);
  const signed = (await bo("GET", "/offers?status=Accepted&size=100")).items.find((o) => o.id === offer.id);
  if (signed?.signed_name !== "Journey Candidate") throw new Error(`offer not signed: ${JSON.stringify(signed)}`);
  ok("offer accepted");

  if (errors.length) throw new Error(`page errors: ${errors.slice(0, 3).join(" | ")}`);
  console.log("\nThe whole candidate journey works.");
} catch (e) {
  console.error(`FAIL at "${step}": ${e.message.split("\n")[0]}`);
  console.error(`  page: ${page.url()}`);
  console.error(`  shows: ${(await page.locator("main").innerText().catch(() => "")).replace(/\s+/g, " ").slice(0, 600)}`);
  await shot("FAILED").catch(() => {});
  await browser.close();
  process.exit(1);
}
await browser.close();
