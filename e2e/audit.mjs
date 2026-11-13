// Design audit: screenshots of every backoffice and portal page in Arabic and English, on a desktop and a phone, with a
// report of anything wider than the screen (horizontal scrolling on phones), console errors, and accessibility problems
// (axe-core, WCAG 2.1 A and AA, serious and critical only, on the desktop views).
// Usage: BASE_URL=http://127.0.0.1:4173 PORTAL_URL=http://127.0.0.1:8080 node audit.mjs  (writes shots/ and shots/report.txt)
import { chromium, devices } from "playwright";
import fs from "node:fs";
import { createRequire } from "node:module";

// axe-core is optional: without it the audit runs as before.
let AXE = "";
try { AXE = fs.readFileSync(createRequire(import.meta.url).resolve("axe-core/axe.min.js"), "utf8"); } catch { /* not installed */ }
const a11yTotals = {};

/** Serious and critical WCAG 2.1 A/AA problems on the page (evaluated outside the page's own script rules, so CSP does not block it). */
async function a11y(page, name) {
  if (!AXE) return;
  await page.evaluate(AXE);
  const found = await page.evaluate(async () => {
    // eslint-disable-next-line no-undef
    const r = await axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] }, resultTypes: ["violations"] });
    return r.violations.filter((v) => v.impact === "serious" || v.impact === "critical")
      .map((v) => ({ id: v.id, impact: v.impact, help: v.help, n: v.nodes.length, where: v.nodes.slice(0, 2).map((x) => x.target.join(" ")).join(" ; ") }));
  });
  for (const v of found) {
    a11yTotals[v.id] = (a11yTotals[v.id] || 0) + v.n;
    note(`A11Y  ${name}: ${v.id} (${v.impact}, ${v.n}) ${v.help}\n        ${v.where}`);
  }
}

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
        // The skip link waits off-screen until it is focused (on purpose).
        if (el.closest(".mc-skip, .sr-only")) continue;
        const tag = el.tagName.toLowerCase() + (el.className && typeof el.className === "string" ? "." + el.className.split(" ").slice(0, 3).join(".") : "");
        out.push(`${tag} [${Math.round(r.left)}..${Math.round(r.right)}] "${(el.textContent || "").trim().slice(0, 40)}"`);
        if (out.length >= 4) break;
      }
    }
    return { doc, w, out };
  });
}

/** Buttons, links and fields still under a floating button with the page scrolled to the end (nothing should stay hidden). */
async function underFab(page) {
  return page.evaluate(async () => {
    // Any floating button fixed to the bottom of the screen (the assistant used to have one).
    const fab = [...document.querySelectorAll("body *")].find((e) => getComputedStyle(e).position === "fixed" && e.matches("button, a")
      && e.getBoundingClientRect().bottom > window.innerHeight - 120 && e.getBoundingClientRect().height > 0);
    if (!fab) return [];
    const hits = new Set();
    for (const y of [document.documentElement.scrollHeight]) {
      window.scrollTo({ top: y, behavior: "instant" });
      await new Promise((r) => setTimeout(r, 250));
      const f = fab.getBoundingClientRect();
      for (const el of document.querySelectorAll("button, a[href], input, select, textarea, [role=button], [role=tab]")) {
        if (fab.contains(el) || el.closest(".mh-chat-drawer, .mh-dialog, .sr-only, .mc-skip")) continue;
        const r = el.getBoundingClientRect();
        if (r.width === 0 || r.height === 0) continue;
        if (r.left < f.right && r.right > f.left && r.top < f.bottom && r.bottom > f.top)
          hits.add(`${el.tagName.toLowerCase()} "${(el.textContent || el.getAttribute("aria-label") || "").trim().slice(0, 30)}"`);
      }
    }
    window.scrollTo(0, 0);
    return [...hits].slice(0, 4);
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
    if (!after) { const u = await underFab(page); if (u.length) note(`UNDER ${name}: under a floating button: ${u.join(", ")}`); }
    if (o.doc > o.w + 1 || o.out.length) note(`WIDE  ${name}: page ${o.doc}px on a ${o.w}px screen\n        ${o.out.join("\n        ")}`);
    if (name.endsWith("_en_dashboard") || name.endsWith("_en_jobs") && name.startsWith("po_")) {
      const fonts = await page.evaluate(async () => {
        await document.fonts.ready;
        const all = [...document.fonts].map((f) => `${f.family.replace(/"/g, "")}:${f.weight}:${f.status}`).filter((v, i, a) => a.indexOf(v) === i).join(", ");
        const h1 = document.querySelector("h1");
        return `${all} | body: ${getComputedStyle(document.body).fontFamily} | h1: ${h1 ? getComputedStyle(h1).fontFamily : "-"}`;
      });
      note(`FONTS ${name}: ${fonts}`);
    }
    if (name.includes("_desktop_") && !after) await a11y(page, name);
    if (errors.length) note(`ERROR ${name}: ${errors.slice(0, 3).join(" | ")}`);
  } catch (e) {
    note(`FAIL  ${name}: ${e.message.split("\n")[0]}`);
    try { await page.screenshot({ path: `shots/${name}-FAILED.png`, fullPage: true }); } catch { /* page gone */ }
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
    ["dashboard", "/"], ["reports", "/reports"], ["jobs", "/jobs"], ["job", `/jobs/${job.id}`], ["job_new", "/jobs/new"], ["applicants", "/applicants"],
    ["applicant", `/applicants/${applicant.id}`], ["applicant_tests", `/applicants/${applicant.id}?tab=tests`], ["interviews", "/interviews"],
    interview && ["interview", `/interviews/${interview.id}`], ["offers", "/offers"], ["talent_pool", "/talent-pool"], ["screening", "/screening"], ["tests", "/tests"],
    test && ["test", `/tests/${test.id}`], ["questions", "/questions"], question && ["question", `/questions/${question.id}`], ["invitations", "/invitations"],
    attempt && ["marking", `/marking/${attempt.id}`], ["role_profiles", "/role-profiles"], ["personality_settings", "/personality-settings"], ["assistant", "/assistant"],
    ["departments", "/setup/departments"], ["locations", "/setup/locations"], ["rounds", "/setup/rounds"], ["settings", "/setup/settings"], ["portal_text", "/setup/portal-text"],
    ["email_templates", "/setup/email-templates"], ["document_templates", "/setup/document-templates"], ["components", "/setup/components"], ["component_new", "/setup/components/new"],
    ["staff", "/setup/staff"], ["email_server", "/setup/email-server"], ["texts", "/setup/texts"], ["handoff", "/setup/handoff"], ["agent_settings", "/setup/assistant"], ["email_log", "/setup/email-log"], ["assistant_usage", "/setup/assistant-usage"], ["audit", "/setup/audit"], ["health", "/setup/health"], ["data_protection", "/setup/data-protection"],
    ["password", "/password"], ["two_step", "/two-step"], ["approvals", "/approvals"], ["requisitions", "/requisitions"],
    ["candidates", "/candidates"], ["countries", "/setup/countries"], ["offer_templates", "/setup/offer-templates"], ["test_new", "/tests/new"],
    ["question_new", "/questions/new"],
  ].filter(Boolean);
  for (const lang of ["ar", "en"]) {
    for (const [name, path] of pages) await shoot(ctx, `bo_${view}_${lang}_${name}`, `${BASE}/${lang}${path}`);
    // The assistant conversation with every kind of card (results, comparison, form, approval), from the scripted model.
    await shoot(ctx, `bo_${view}_${lang}_zz_chat`, `${BASE}/${lang}/assistant`, async (p) => {
      // A fresh conversation (the chat is kept per staff member in the browser).
      await p.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith("mh-agent-")).forEach((k) => localStorage.removeItem(k)));
      await p.reload({ waitUntil: "networkidle" });
      const ask = async (text, marker) => {
        const box = p.locator("#asst-q");
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
    // A fresh conversation in full screen: the welcome, suggestions and examples.
    await shoot(ctx, `bo_${view}_${lang}_zz_chat_full`, `${BASE}/${lang}/assistant`, async (p) => {
      await p.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith("mh-agent-")).forEach((k) => localStorage.removeItem(k)));
      await p.reload({ waitUntil: "networkidle" });
      await p.locator(`button[aria-label="${lang === "ar" ? "ملء الشاشة" : "Full Screen"}"]`).click();
    });
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

// ---------------- showcase: the latest design changes, at twice the resolution ----------------
{
  const hd = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });
  await hd.request.post(`${BASE}/api/backoffice/sign-in`, { headers: { "X-Backoffice": "1" }, data: { email: "admin@moet.test", password: "Admin-Password-123" } });
  const get = async (p) => (await hd.request.get(`${BASE}/api/backoffice${p}`)).json();
  let attemptId = null;
  for (const a of (await get("/applicants?size=10")).items) {
    const d = await get(`/applicants/${a.id}`);
    const at = d.invitations?.find((i) => i.attempts?.length)?.attempts[0];
    if (at) { attemptId = at.id; break; }
  }
  const snap = async (name, url, prepare) => {
    const page = await hd.newPage();
    try {
      await page.goto(url, { waitUntil: "networkidle", timeout: 45000 });
      await page.waitForTimeout(400);
      if (prepare) { await prepare(page); await page.waitForTimeout(700); }
      await page.screenshot({ path: `shots/showcase_${name}.png` });
    } catch (e) { note(`FAIL  showcase ${name}: ${e.message.split("\n")[0]}`); }
    await page.close();
  };
  const fresh = async (p) => {
    await p.evaluate(() => { Object.keys(localStorage).filter((k) => k.startsWith("mh-agent-")).forEach((k) => localStorage.removeItem(k)); localStorage.removeItem("mh-nav-collapsed"); });
    await p.reload({ waitUntil: "networkidle" });
  };
  for (const lang of ["en", "ar"]) {
    const L = (en, ar) => (lang === "ar" ? ar : en);
    await snap(`${lang}_1_assistant_full_screen`, `${BASE}/${lang}/assistant`, async (p) => { await fresh(p); await p.locator(`button[aria-label="${L("Full Screen", "ملء الشاشة")}"]`).click(); });
    await snap(`${lang}_2_assistant_conversation`, `${BASE}/${lang}/assistant`, async (p) => {
      await fresh(p);
      const ask = async (text, marker) => {
        await p.locator("#asst-q").fill(text);
        await p.locator("#asst-q").press("Enter");
        await p.getByText(marker).last().waitFor({ timeout: 40000 });
        await p.waitForTimeout(600);
      };
      await ask(L("Show applicant cards", "اعرض بطاقات المتقدمين"), "Cards ready.");
      await ask(L("Add a note", "أضف ملاحظة"), "Proposal ready.");
    });
    await snap(`${lang}_3_assistant_side_panel`, `${BASE}/${lang}/applicants`, async (p) => { await fresh(p); await p.locator(".mh-assistant-fab").click(); });
    await snap(`${lang}_4_menu_and_account`, `${BASE}/${lang}/jobs`, async (p) => { await fresh(p); await p.locator(".mh-user").click(); });
    await snap(`${lang}_5_menu_collapsed`, `${BASE}/${lang}/interviews`, async (p) => {
      await p.evaluate(() => localStorage.setItem("mh-nav-collapsed", "1"));
      await p.reload({ waitUntil: "networkidle" });
    });
    if (attemptId) await snap(`${lang}_6_personality_radar`, `${BASE}/${lang}/marking/${attemptId}`, async (p) => {
      await p.evaluate(() => localStorage.removeItem("mh-nav-collapsed"));
      await p.reload({ waitUntil: "networkidle" });
      await p.locator("figure svg").first().scrollIntoViewIfNeeded();
      await p.evaluate(() => window.scrollBy(0, -120));
    });
  }
  // Phone, at the device's own resolution (three times).
  const phone = await browser.newContext({ ...devices["iPhone 13"], deviceScaleFactor: 3 });
  await phone.request.post(`${BASE}/api/backoffice/sign-in`, { headers: { "X-Backoffice": "1" }, data: { email: "admin@moet.test", password: "Admin-Password-123" } });
  for (const lang of ["ar", "en"]) {
    const page = await phone.newPage();
    try {
      await page.goto(`${BASE}/${lang}/applicants`, { waitUntil: "networkidle" });
      await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith("mh-agent-")).forEach((k) => localStorage.removeItem(k)));
      await page.reload({ waitUntil: "networkidle" });
      await page.locator(".mh-assistant-fab").click();
      await page.waitForTimeout(700);
      await page.screenshot({ path: `shots/showcase_${lang}_7_phone_assistant.png` });
      await page.goto(`${BASE}/${lang}/setup/data-protection`, { waitUntil: "networkidle" });
      await page.waitForTimeout(400);
      await page.screenshot({ path: `shots/showcase_${lang}_8_phone_tabs.png` });
    } catch (e) { note(`FAIL  showcase phone ${lang}: ${e.message.split("\n")[0]}`); }
    await page.close();
  }
  await phone.close();
  await hd.close();
}

// ---------------- portal ----------------
if (PORTAL) {
  for (const [view, opts] of Object.entries(VIEWS)) {
    const guest = await browser.newContext({ ...opts });
    const jobs = await (await guest.request.get(`${PORTAL}/api/jobs?lang=ar`)).json();
    const list = jobs.jobs || jobs.items || jobs.message?.jobs || jobs;
    const code = list[0]?.id || list[0]?.name || list[0]?.code;
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
        for (const [name, path] of [["applications", "/applications"], app && ["application", `/applications/${app.id || app.name || app.code}`],
          ["assessments", "/assessments"], ["account", "/account"], ["apply", `/jobs/${code}/apply`]].filter(Boolean)) await shoot(user, `po_${view}_${lang}_${name}`, `${PORTAL}/${lang}${path}`);
      }
    }
    await user.close();
  }
}

await browser.close();
if (AXE) note(`\nAccessibility (serious and critical, all desktop pages): ${Object.keys(a11yTotals).length === 0 ? "none" : Object.entries(a11yTotals).sort((a, b) => b[1] - a[1]).map(([k, n]) => `${k} ${n}`).join(", ")}`);
fs.writeFileSync("shots/report.txt", report.join("\n") + "\n");
console.log(`\n${report.length} findings`);
