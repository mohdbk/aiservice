// Films the hiring journey in Arabic, step by step, as full-HD JPEG frames with captions (manifest.json): a job is
// opened, its screening requirements drafted from the description and approved, a candidate applies on the careers
// portal and takes the Big Five questionnaire, HR sees the CV result and the personality profile, offers interview
// times, the candidate books one, and the panel prepares an interview guide. A scripted model (film-model.mjs) stands in
// for the language model. Usage: BASE_URL=... PORTAL_URL=... REPO=../.. CVS=./cvs node film.mjs
import { chromium } from "playwright";
import fs from "node:fs";
import path from "node:path";

const BASE = process.env.BASE_URL || "http://127.0.0.1:4173";
const PORTAL = process.env.PORTAL_URL || "http://127.0.0.1:8080";
const REPO = process.env.REPO || "../..";
const CVS = process.env.CVS || "cvs";
const T = JSON.parse(fs.readFileSync(path.join(REPO, "backoffice/web/src/text/ar.json"), "utf8"));
const P = JSON.parse(fs.readFileSync(path.join(REPO, "portal/web/src/text/ar.json"), "utf8"));
const OUT = "film";
fs.mkdirSync(OUT, { recursive: true });
const day = (n) => new Date(Date.now() + n * 86400000).toISOString().slice(0, 10);
const stamp = Date.now().toString(36);
const VIEW = { width: 1440, height: 810 };
const SCALE = 4 / 3; // 1920 x 1080 frames

const manifest = [];
let chapter = { en: "", ar: "" };
const chapterOf = (en, ar) => { chapter = { en, ar }; };
let n = 0;
const log = (s) => console.log(s);

const browser = await chromium.launch();
const ctxOpts = { viewport: VIEW, deviceScaleFactor: SCALE, locale: "ar-AE", timezoneId: "Asia/Dubai" };
const hr = await browser.newContext(ctxOpts);
const signIn = await hr.request.post(`${BASE}/api/backoffice/sign-in`, { headers: { "X-Backoffice": "1" }, data: { email: "admin@moet.test", password: "Admin-Password-123" } });
if (!signIn.ok()) throw new Error(`sign-in ${signIn.status()}`);
const bo = async (method, p, data) => {
  const r = await hr.request.fetch(`${BASE}/api/backoffice${p}`, { method, data, headers: method === "GET" ? {} : { "X-Backoffice": "1" } });
  if (!r.ok()) throw new Error(`${method} ${p} -> ${r.status()} ${(await r.text()).slice(0, 300)}`);
  const text = await r.text();
  return text ? JSON.parse(text) : null;
};
const portalApi = async (method, data, form) => {
  const r = await hr.request.fetch(`${BASE.replace(/:\d+$/, ":5080")}/api/portal/${method}`, {
    method: "POST", headers: { Authorization: "token e2e-portal-key:e2e-secret-0123456789" }, ...(form ? { multipart: form } : { data }),
  });
  if (!r.ok()) throw new Error(`portal ${method} -> ${r.status()} ${(await r.text()).slice(0, 300)}`);
  return (await r.json()).message;
};

/** One frame: the page as it is, a caption in English and Arabic, and an optional box to move the camera towards. */
async function shot(page, caption, captionAr, focus) {
  const file = `${String(++n).padStart(2, "0")}.jpg`;
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(OUT, file), type: "jpeg", quality: 92 });
  let box = null;
  if (focus) {
    try {
      const b = await (typeof focus === "string" ? page.locator(focus).first() : focus).boundingBox();
      if (b) box = { x: Math.round(b.x * SCALE), y: Math.round(b.y * SCALE), w: Math.round(b.width * SCALE), h: Math.round(b.height * SCALE) };
    } catch { /* no camera move */ }
  }
  manifest.push({ file, caption, caption_ar: captionAr, focus: box, chapter: chapter.en, chapter_ar: chapter.ar });
  log(`shot ${file}: ${caption}`);
}
async function step(name, fn) {
  try { await fn(); } catch (e) { log(`FAIL ${name}: ${e.message.split("\n")[0]}`); }
}
const tab = (page, label) => page.getByRole("tab", { name: label }).first().click();
const toTop = (page) => page.evaluate(() => window.scrollTo({ top: 0, behavior: "instant" }));
const scrollTo = async (page, sel, offset = 120) => {
  await page.locator(sel).first().evaluate((el, o) => window.scrollTo({ top: el.getBoundingClientRect().top + window.scrollY - o, behavior: "instant" }), offset);
  await page.waitForTimeout(300);
};

// ---------------- setup through the API (not filmed) ----------------
const templates = await bo("GET", "/assessment-templates");
const b5title = templates.find((x) => x.key === "workplace-big-five-25")?.title;
let big5 = (await bo("GET", "/assessments?size=100")).items.find((x) => x.title === b5title);
if (!big5) {
  const created = await bo("POST", "/assessment-templates/workplace-big-five-25", {});
  const full = await bo("GET", `/assessments/${created.id}`);
  await bo("PUT", `/assessments/${created.id}`, { ...full, status: "Published" });
  big5 = { id: created.id };
}
const lookups = await bo("GET", "/lookups");
const created = await bo("POST", "/jobs", {
  title: "محلل بيانات اقتصادية",
  description: "<p>تبحث وزارة الاقتصاد والسياحة عن محلل بيانات اقتصادية لبناء المؤشرات الاقتصادية ولوحات المعلومات التي تدعم صناعة القرار.</p>" +
    "<h3>المهام</h3><ul><li>إعداد المؤشرات الاقتصادية الشهرية</li><li>بناء لوحات المعلومات للإدارة العليا</li><li>تحليل بيانات الرخص والتجارة</li></ul>" +
    "<h3>المتطلبات</h3><ul><li>بكالوريوس في الإحصاء أو علوم البيانات أو تخصص ذي صلة (مطلوب)</li><li>خبرة لا تقل عن 3 سنوات في تحليل البيانات (مطلوب)</li>" +
    "<li>إتقان SQL (مطلوب)</li><li>يفضل إتقان Power BI أو Tableau</li><li>تعد الخبرة في المؤشرات الاقتصادية أو العمل الحكومي ميزة</li><li>إجادة اللغة الإنجليزية بطلاقة</li></ul>",
  department_id: lookups.departments?.[0]?.id ?? null, location: "دبي", location_id: lookups.locations?.[0]?.id ?? null, employment_type: "FullTime", status: "Open",
  is_published: true, posted_on: day(0), closes_on: day(30), salary_min: 28000, salary_max: 36000, salary_currency: "AED", salary_period: "Month", show_salary: true,
  translations: [{ lang: "en", title: "Economic Data Analyst", description: "<p>Build economic indicators and dashboards for decision making.</p>", approved: true }],
  screening: [], stages: [{ assessment_id: big5.id, send_when: "OnApply", deadline_days: 7 }], row_version: null,
});
const jobId = created.id;
const job = await bo("GET", `/jobs/${jobId}`);
log(`job ${jobId} ${job.code}, Big Five ${big5.id}`);

// ---------------- 1. HR opens the job ----------------
chapterOf("The Job Opening", "الإعلان عن الوظيفة");
const boPage = await hr.newPage();
await step("dashboard", async () => {
  await boPage.goto(`${BASE}/ar`, { waitUntil: "networkidle" });
  await shot(boPage, "HR starts in the recruitment backoffice", "يبدأ فريق الموارد البشرية من نظام التوظيف");
});
await step("jobs list", async () => {
  await boPage.goto(`${BASE}/ar/jobs`, { waitUntil: "networkidle" });
  await shot(boPage, "A new job opening: Economic Data Analyst", "وظيفة جديدة: محلل بيانات اقتصادية", "table tbody tr");
});
await step("job details", async () => {
  await boPage.goto(`${BASE}/ar/jobs/${jobId}`, { waitUntil: "networkidle" });
  await shot(boPage, "The job description states what is required and what is preferred", "يحدد الوصف الوظيفي ما هو مطلوب وما هو مفضل");
});
await step("job tests", async () => {
  await tab(boPage, T["jobs.tab.tests"]);
  await shot(boPage, "The Big Five questionnaire is sent automatically when someone applies", "يرسل استبيان السمات الخمس تلقائيا عند التقديم", "main section, main .aegov-card");
});

// ---------------- 2. Screening requirements from the description ----------------
chapterOf("Screening Requirements", "متطلبات الفرز");
await step("requirements suggest", async () => {
  await tab(boPage, T["jobs.tab.criteria"]);
  await boPage.waitForTimeout(400);
  await shot(boPage, "Screening Requirements: empty until HR asks for a draft", "متطلبات الفرز: فارغة حتى يطلب الفريق مسودة");
  boPage.once("dialog", (d) => d.accept());
  await boPage.getByRole("button", { name: T["crit.suggest"] }).click();
  await boPage.locator("#req-e ~ * li, section[aria-labelledby='req-e'] li").first().waitFor({ timeout: 20000 });
  await boPage.waitForTimeout(600);
  await shot(boPage, "Drafted from the description: Essential and Desirable, with type, years, level and importance",
    "صيغت من الوصف: أساسية ومرغوبة، مع النوع والسنوات والمستوى والأهمية", "#req-e");
});
await step("requirements guide", async () => {
  await boPage.getByRole("button", { name: new RegExp(T["crit.guide"]) }).nth(1).click();
  await scrollTo(boPage, "#guide-1", 330);
  await shot(boPage, "Each requirement has a score guide: what 0, 4, 7 and 10 look like", "لكل متطلب دليل تقييم: ما الذي يمثله 0 و4 و7 و10", "#guide-1");
});
await step("requirements approve", async () => {
  await boPage.getByRole("button", { name: T["crit.approve"] }).click();
  await boPage.getByText(T["crit.status_approved"]).first().waitFor({ timeout: 15000 });
  await toTop(boPage);
  await shot(boPage, "HR approves. Only approved requirements are used to score CVs", "يعتمد الفريق المتطلبات. لا تقيم السير الذاتية إلا وفق المعتمدة");
});

// Two other applicants (through the portal API) so the candidate has someone to be ranked against.
await step("other applicants", async () => {
  for (const [name, email, file, phone] of [["خالد المري", `khalid.${stamp}@example.test`, "khalid.docx", "+971 55 318 2207"], ["Peter Novak", `peter.${stamp}@example.test`, "peter.docx", "+971 52 640 1185"]]) {
    const s = await portalApi("signup", { full_name: name, email, password: "Film-Password-2026", lang: "ar" });
    const cand = (await bo("GET", `/candidates?q=${encodeURIComponent(email)}`)).items[0];
    if (cand && !cand.verified) await bo("POST", `/candidates/${cand.id}/verify`, {});
    await portalApi("apply", null, {
      email, epoch: String(s.epoch ?? 0), job: job.code, phone, country: "United Arab Emirates", declaration: "1", lang: "ar", screening: "{}",
      cv: { name: file, mimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document", buffer: fs.readFileSync(path.join(CVS, file)) },
    });
  }
});

// ---------------- 3. The candidate applies on the careers portal ----------------
chapterOf("Applying", "التقديم");
const cand = await browser.newContext(ctxOpts);
const pp = await cand.newPage();
pp.on("dialog", (d) => d.accept());
const EMAIL = `salma.${stamp}@example.test`;
const PASSWORD = "Film-Password-2026";
await step("portal home", async () => {
  await pp.goto(`${PORTAL}/ar`, { waitUntil: "networkidle" });
  await shot(pp, "The job appears on the careers portal", "تظهر الوظيفة على بوابة الوظائف");
});
await step("portal job", async () => {
  await pp.goto(`${PORTAL}/ar/jobs/${encodeURIComponent(job.code)}`, { waitUntil: "networkidle" });
  await shot(pp, "The job page", "صفحة الوظيفة");
  await scrollTo(pp, "#reqs", 140);
  await shot(pp, "Candidates see the same Essential and Desirable requirements", "يرى المتقدمون المتطلبات الأساسية والمرغوبة نفسها", "#reqs");
});
await step("portal sign up", async () => {
  await pp.goto(`${PORTAL}/ar/create-account`, { waitUntil: "networkidle" });
  await pp.fill("#full_name", "سلمى الحمادي");
  await pp.fill("#email", EMAIL);
  await pp.fill("#password", PASSWORD);
  await shot(pp, "Salma creates an account", "تنشئ سلمى حسابا");
  await pp.getByRole("button", { name: P["auth.signup_button"] }).last().click();
  await pp.waitForURL((u) => !/create-account/.test(u.pathname), { timeout: 20000 });
  if (/check-email|sign-in/.test(pp.url())) {
    if (/check-email/.test(pp.url())) {
      const c = (await bo("GET", `/candidates?q=${encodeURIComponent(EMAIL)}`)).items[0];
      await bo("POST", `/candidates/${c.id}/verify`, {});
    }
    await pp.goto(`${PORTAL}/ar/sign-in`, { waitUntil: "networkidle" });
    await pp.fill("#email", EMAIL);
    await pp.fill("#password", PASSWORD);
    await pp.getByRole("button", { name: P["auth.sign_in_button"] }).last().click();
    await pp.waitForURL((u) => !/sign-in/.test(u.pathname), { timeout: 20000 });
  }
});
await step("portal apply", async () => {
  await pp.goto(`${PORTAL}/ar/jobs/${encodeURIComponent(job.code)}/apply`, { waitUntil: "networkidle" });
  await pp.locator("#phone").waitFor({ timeout: 15000 });
  await pp.fill("#phone", "50 482 9136");
  await pp.selectOption("#country", { index: 1 }).catch(() => {});
  await pp.setInputFiles("#cv", path.join(CVS, "salma.docx"));
  await pp.check("#declaration");
  await scrollTo(pp, "#phone", 260);
  await shot(pp, "She applies: country code and number, her CV and the declaration", "تتقدم بطلبها: رمز الدولة والرقم وسيرتها الذاتية والإقرار", "#cv-label");
  await pp.getByRole("button", { name: P["apply.submit"] }).click();
  await pp.waitForURL(/\/applications\//, { timeout: 30000 });
  await pp.waitForLoadState("networkidle");
  await shot(pp, "Application received, with a reference and its progress", "تم استلام الطلب مع رقم مرجعي ومتابعة المراحل");
});

// ---------------- 4. The Big Five questionnaire ----------------
chapterOf("The Big Five Questionnaire", "استبيان السمات الخمس");
const salma = async () => (await bo("GET", `/applicants?q=${encodeURIComponent(EMAIL)}`)).items[0];
await step("big five", async () => {
  const app = await salma();
  const inv = (await bo("GET", `/applicants/${app.id}`)).invitations.find((i) => i.assessment_id === big5.id);
  await pp.goto(`${PORTAL}/ar/assessments/${inv.code}`, { waitUntil: "networkidle" });
  await shot(pp, "The Big Five questionnaire is waiting in her account", "استبيان السمات الخمس بانتظارها في حسابها");
  await pp.check("#ready");
  await pp.getByRole("button", { name: new RegExp(`${P["asm.start"]}|${P["asm.continue"]}`) }).click();
  await pp.locator("#question-title").waitFor();
  await pp.getByLabel(P["asm.auto_next"]).uncheck().catch(() => {});
  const pattern = [4, 5, 3, 4, 2, 5, 4, 3];
  for (let i = 0; i < 60; i++) {
    await pp.locator("#question-title").waitFor();
    const radios = pp.locator('section input[type="radio"]');
    const count = await radios.count();
    if (count) await radios.nth(Math.min(count - 1, pattern[i % pattern.length] - 1)).check();
    await pp.waitForTimeout(350);
    if (i === 1) await shot(pp, "One statement at a time, with progress and time at the top", "عبارة واحدة في كل مرة مع شريط التقدم والوقت في الأعلى", "#question-title");
    if (i === 9) {
      await pp.getByRole("button", { name: P["asm.review"] }).first().click();
      await shot(pp, "She can review and return to any question", "يمكنها مراجعة أي سؤال والعودة إليه");
      await pp.getByRole("button", { name: P["asm.review"] }).first().click();
    }
    const next = pp.getByRole("button", { name: new RegExp(`^${P["asm.next"]}`) });
    if (await next.count() && await next.isEnabled()) { await next.click(); continue; }
    await pp.getByRole("button", { name: new RegExp(`${P["asm.finish_section"]}|${P["asm.submit"]}`) }).last().click();
    await pp.waitForTimeout(500);
    await shot(pp, "A last check before submitting", "مراجعة أخيرة قبل التسليم");
    await pp.getByRole("button", { name: P["asm.confirm_yes"] }).click();
    await pp.waitForLoadState("networkidle");
    await pp.waitForTimeout(800);
    if (!(await pp.locator("#question-title").count())) break;
  }
  await scrollTo(pp, "#result-title", 260).catch(() => {});
  await shot(pp, "Submitted: a clear results screen and what happens next", "تم التسليم: شاشة نتيجة واضحة والخطوات التالية", "#result-title");
});

// ---------------- 5. HR sees the results ----------------
chapterOf("Scoring", "التقييم");
await step("wait for scoring", async () => {
  for (let i = 0; i < 60; i++) {
    const items = (await bo("GET", `/applicants?job=${jobId}&size=10`)).items;
    if (items.length >= 3 && items.every((a) => a.ai_status === "Scored")) return;
    await boPage.waitForTimeout(2000);
  }
  throw new Error("CVs not scored in time");
});
await step("applicants", async () => {
  await boPage.goto(`${BASE}/ar/applicants?job=${jobId}&sort=score&dir=desc`, { waitUntil: "networkidle" });
  await shot(boPage, "HR sees the applicants ranked by their CV score", "يرى الفريق المتقدمين مرتبين حسب تقييم السيرة الذاتية", "table");
  await boPage.goto(`${BASE}/ar/applicants?job=${jobId}&view=board&size=100`, { waitUntil: "networkidle" });
  await shot(boPage, "Or as a pipeline board", "أو كلوحة لمسار التوظيف");
});
let appId = 0;
await step("cv result", async () => {
  appId = (await salma()).id;
  await boPage.goto(`${BASE}/ar/applicants/${appId}`, { waitUntil: "networkidle" });
  await shot(boPage, "Salma's application: CV score and rank in the job", "طلب سلمى: تقييم السيرة الذاتية وترتيبها في الوظيفة");
  await scrollTo(boPage, "#cvr-ess", 150);
  await shot(boPage, "Essential requirements: Met, with her CV's own words as evidence", "المتطلبات الأساسية: مستوفاة، مع كلمات سيرتها الذاتية كدليل", "#cvr-ess");
  await scrollTo(boPage, "#cvr-des", 150);
  await shot(boPage, "Desirable requirements: a score each, and what would score higher", "المتطلبات المرغوبة: تقييم لكل منها وما الذي يرفعه", "#cvr-des");
});
await step("big five profile", async () => {
  await toTop(boPage);
  await boPage.getByRole("tab", { name: new RegExp(T["app.tests"]) }).first().click();
  await boPage.waitForTimeout(800);
  await shot(boPage, "Her Big Five profile, scored from her answers", "ملف السمات الخمس، محسوب من إجاباتها");
  await scrollTo(boPage, "figure svg", 160).catch(() => {});
  await shot(boPage, "Each trait against the role's profile", "كل سمة مقارنة بملف الدور", "figure");
});

// ---------------- 6. Interview times ----------------
chapterOf("The Interview", "المقابلة");
await step("offer times", async () => {
  await toTop(boPage);
  await boPage.getByRole("button", { name: T["app.interview"] }).first().click();
  await boPage.getByRole("menuitem", { name: T["book.offer_times"] }).click();
  const dlg = boPage.getByRole("dialog");
  await dlg.locator("select").first().selectOption({ index: 1 });
  await dlg.locator('input[type="date"]').first().fill(day(4));
  await dlg.getByRole("button", { name: T["book.add_time"] }).click();
  await dlg.locator('input[type="date"]').nth(1).fill(day(5));
  await dlg.getByLabel(T["int.location"]).fill("مقر الوزارة، دبي");
  await shot(boPage, "HR offers interview times", "يعرض الفريق مواعيد للمقابلة", '[role="dialog"]');
  await dlg.getByRole("button", { name: T["book.send"] }).click();
  await boPage.waitForTimeout(1200);
});
await step("candidate books", async () => {
  await pp.goto(`${PORTAL}/ar/applications`, { waitUntil: "networkidle" });
  await pp.locator('a[href*="/applications/"]').first().click();
  await pp.waitForLoadState("networkidle");
  await pp.locator('section[aria-labelledby="book-title"] input[type="radio"]').first().check();
  await scrollTo(pp, 'section[aria-labelledby="book-title"]', 120);
  await shot(pp, "Salma picks the time that suits her", "تختار سلمى الموعد المناسب لها", 'section[aria-labelledby="book-title"]');
  await pp.getByRole("button", { name: P["book.confirm"] }).click();
  await pp.locator('section[aria-labelledby="book-title"]').waitFor({ state: "detached", timeout: 15000 });
  await pp.waitForTimeout(600);
  await toTop(pp);
  await shot(pp, "Booked. The confirmation email carries a calendar invitation", "تم الحجز. يحمل بريد التأكيد دعوة تقويم");
});
await step("interview guide", async () => {
  const intv = (await bo("GET", `/applicants/${appId}`)).interviews[0];
  await boPage.goto(`${BASE}/ar/interviews/${intv.id}`, { waitUntil: "networkidle" });
  await shot(boPage, "The interview, with Add to Calendar for the panel", "المقابلة، مع إضافة إلى التقويم للجنة");
  await boPage.getByRole("button", { name: T["guide.prepare"] }).click();
  await boPage.getByText(T["guide.listen"]).first().waitFor({ timeout: 20000 });
  await boPage.waitForTimeout(500);
  await shot(boPage, "An interview guide: questions per skill and what a strong answer covers", "دليل مقابلة: أسئلة لكل مهارة وما تتضمنه الإجابة القوية", "main .aegov-card");
});

fs.writeFileSync(path.join(OUT, "manifest.json"), JSON.stringify(manifest, null, 1));
log(`${manifest.length} shots`);
await browser.close();
