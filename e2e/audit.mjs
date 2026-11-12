// Design audit: screenshots of every backoffice and portal page in Arabic and English, on a desktop and a phone, with a
// report of anything wider than the screen (horizontal scrolling on phones) and console errors.
// Usage: BASE_URL=http://127.0.0.1:4173 PORTAL_URL=http://127.0.0.1:8080 node audit.mjs  (writes shots/ and shots/report.txt)
import { chromium, devices } from "playwright";
import fs from "node:fs";

const BASE = process.env.BASE_URL, PORTAL = process.env.PORTAL_URL;
const report = [];
const note = (s) => { report.push(s); console.log(s); };
fs.mkdirSync("shots", { recursive: true });

const VIEWS = { desktop: { viewport: { width: 1440, height: 900 } }, phone: { ...devices["iPhone 13"] } };

/** Elements sticking out past the right or left edge of the viewport (the cause of sideways scrolling). */
async function overflow(page) {
  return page.evaluate(() => {
    const w = window.innerWidth;
    const doc = document.documentElement.scrollWidth;
    const out = [];
    for (const el of document.querySelectorAll("body *")) {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) continue;
      if (r.right > w + 1 || r.left < -1) {
        // Skip anything inside a horizontally scrolling container (tables, tabs): that scrolls on purpose.
        let p = el.parentElement, scrolls = false;
        while (p && p !== document.body) {
          const s = getComputedStyle(p);
          if ((s.overflowX === "auto" || s.overflowX === "scroll" || s.overflowX === "hidden") && p.scrollWidth > p.clientWidth) { scrolls = true; break; }
          p = p.parentElement;
        }
        if (scrolls) continue;
        if (getComputedStyle(el).position === "fixed") continue;
        const tag = el.tagName.toLowerCase() + (el.className && typeof el.className === "string" ? "." + el.className.split(" ").slice(0, 3).join(".") : "");
        out.push(`${tag} [${Math.round(r.left)}..${Math.round(r.right)}] "${(el.textContent || "").trim().slice(0, 40)}"`);
        if (out.length >= 4) break;
      }
    }
    return { doc, w, out };
  });
}

async function shoot(ctx, name, url, after, full = false) {
  const page = await ctx.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  page.on("console", (m) => { if (m.type() === "error" && !/status of 40[13]/.test(m.text())) errors.push(m.text()); });
  try {
    await page.goto(url, { waitUntil: "networkidle", timeout: 45000 });
    await page.waitForTimeout(500);
    if (after) { await after(page); await page.waitForTimeout(400); }
    await page.screenshot({ path: `shots/${name}.png`, fullPage: !after || full });
    const o = await overflow(page);
    if (o.doc > o.w + 1 || o.out.length) note(`WIDE  ${name}: page ${o.doc}px on a ${o.w}px screen\n        ${o.out.join("\n        ")}`);
    if (name.endsWith("_en_dashboard") || name.endsWith("_en_jobs") && name.startsWith("po_")) {
      const fonts = await page.evaluate(async () => { await document.fonts.ready; return [...document.fonts].filter((f) => f.status === "loaded").map((f) => f.family).filter((v, i, a) => a.indexOf(v) === i).join(", ")
        + " | body: " + getComputedStyle(document.body).fontFamily; });
      note(`FONTS ${name}: ${fonts}`);
    }
    if (errors.length) note(`ERROR ${name}: ${errors.slice(0, 3).join(" | ")}`);
  } catch (e) {
    note(`FAIL  ${name}: ${e.message.split("\n")[0]}`);
  }
  await page.close();
}

const browser = await chromium.launch();

// ---------------- backoffice ----------------
for (const [view, opts] of Object.entries(VIEWS)) {
  const ctx = await browser.newContext({ ...opts, deviceScaleFactor: view === "phone" ? 2 : 1 });
  const res = await ctx.request.post(`${BASE}/api/backoffice/sign-in`, { headers: { "X-Backoffice": "1" }, data: { email: "admin@moet.test", password: "Admin-Password-123" } });
  if (!res.ok()) throw new Error(`sign-in failed ${res.status()}`);
  const get = async (p) => (await ctx.request.get(`${BASE}/api/backoffice${p}`)).json();
  const job = (await get("/jobs?size=1")).items[0];
  const applicants = (await get("/applicants?size=10")).items;
  const withTest = [];
  for (const a of applicants) { const d = await get(`/applicants/${a.id}`); if (d.invitations?.some((i) => i.attempts?.length)) withTest.push(d); }
  const applicant = withTest[0] || applicants[0];
  const attempt = withTest[0]?.invitations.find((i) => i.attempts?.length)?.attempts[0];
  const interview = (await get("/interviews?size=1")).items[0];
  const test = (await get("/assessments?size=1")).items[0];
  const question = (await get("/questions?size=1")).items[0];
  const pages = [
    ["dashboard", "/"], ["jobs", "/jobs"], ["job", `/jobs/${job.id}`], ["job_new", "/jobs/new"], ["applicants", "/applicants"],
    ["applicant", `/applicants/${applicant.id}`], ["applicant_tests", `/applicants/${applicant.id}?tab=tests`], ["interviews", "/interviews"],
    interview && ["interview", `/interviews/${interview.id}`], ["offers", "/offers"], ["screening", "/screening"], ["tests", "/tests"],
    test && ["test", `/tests/${test.id}`], ["questions", "/questions"], question && ["question", `/questions/${question.id}`], ["invitations", "/invitations"],
    attempt && ["marking", `/marking/${attempt.id}`], ["role_profiles", "/role-profiles"], ["assistant", "/assistant"],
    ["departments", "/setup/departments"], ["rounds", "/setup/rounds"], ["settings", "/setup/settings"], ["portal_text", "/setup/portal-text"],
    ["email_templates", "/setup/email-templates"], ["offer_templates", "/setup/offer-templates"], ["components", "/setup/components"], ["component_new", "/setup/components/new"],
    ["staff", "/setup/staff"], ["email_server", "/setup/email-server"], ["agent_settings", "/setup/assistant"], ["email_log", "/setup/email-log"],
    ["password", "/password"],
  ].filter(Boolean);
  for (const lang of ["ar", "en"]) {
    for (const [name, path] of pages) await shoot(ctx, `bo_${view}_${lang}_${name}`, `${BASE}/${lang}${path}`);
    // The assistant conversation with every kind of card (results, comparison, form, approval), from the scripted model.
    await shoot(ctx, `bo_${view}_${lang}_zz_chat`, `${BASE}/${lang}/assistant`, async (p) => {
      const ask = async (text, marker) => {
        const box = p.locator("main textarea").first();
        await box.fill(text);
        await box.press("Enter");
        await p.getByText(marker).last().waitFor({ timeout: 40000 });
        await p.waitForTimeout(600);
      };
      await ask(lang === "ar" ? "اعرض بطاقات المتقدمين" : "Show applicant cards", "Cards ready.");
      await ask(lang === "ar" ? "قارن أفضل المتقدمين" : "Compare the top applicants", "Comparison ready.");
      await ask(lang === "ar" ? "اعرض نموذج المقابلة" : "Show the interview form", "Form shown.");
      await ask(lang === "ar" ? "أضف ملاحظة" : "Add a note", "Proposal ready.");
    }, true);
    if (view === "phone") {
      await shoot(ctx, `bo_${view}_${lang}_zz_menu`, `${BASE}/${lang}/applicants`, (p) => p.locator(`header button[aria-label="${lang === "ar" ? "القائمة" : "Menu"}"]`).click());
      await shoot(ctx, `bo_${view}_${lang}_zz_assistant_panel`, `${BASE}/${lang}/applicants`, (p) => p.locator(".mh-assistant-fab").click());
    }
  }
  const anon = await browser.newContext({ ...opts });
  for (const lang of ["ar", "en"]) await shoot(anon, `bo_${view}_${lang}_sign_in`, `${BASE}/${lang}/sign-in`);
  await anon.close();
  await ctx.close();
}

// ---------------- portal ----------------
if (PORTAL) {
  for (const [view, opts] of Object.entries(VIEWS)) {
    const guest = await browser.newContext({ ...opts });
    const jobs = await (await guest.request.get(`${PORTAL}/api/jobs?lang=ar`)).json();
    const list = jobs.jobs || jobs.items || jobs.message?.jobs || jobs;
    const code = list[0]?.name || list[0]?.code;
    for (const lang of ["ar", "en"]) {
      for (const [name, path] of [["jobs", ""], ["job", `/jobs/${code}`], ["sign_in", "/sign-in"], ["create_account", "/create-account"],
        ["forgot_password", "/forgot-password"], ["not_found", "/nothing-here"]]) await shoot(guest, `po_${view}_${lang}_${name}`, `${PORTAL}/${lang}${path}`);
    }
    await guest.close();
    const user = await browser.newContext({ ...opts });
    const r = await user.request.post(`${PORTAL}/api/login`, { headers: { "X-Portal": "1" }, data: { email: "layla.ahmed@example.test", password: "Demo-Applicant-2026" } });
    if (!r.ok()) note(`FAIL  portal sign-in ${r.status()}`);
    else {
      const apps = await (await user.request.get(`${PORTAL}/api/applications?lang=ar`)).json();
      const app = (apps.applications || apps.items || apps.message || apps)[0];
      for (const lang of ["ar", "en"]) {
        for (const [name, path] of [["applications", "/applications"], app && ["application", `/applications/${app.name || app.id || app.code}`],
          ["assessments", "/assessments"], ["apply", `/jobs/${code}/apply`]].filter(Boolean)) await shoot(user, `po_${view}_${lang}_${name}`, `${PORTAL}/${lang}${path}`);
      }
    }
    await user.close();
  }
}

await browser.close();
fs.writeFileSync("shots/report.txt", report.join("\n") + "\n");
console.log(`\n${report.length} findings`);
