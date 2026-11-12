// In-memory stand-in for Frappe, for local development, demos and the design preview workflow.
// Never enable in production (MOCK_FRAPPE=1).
import { readFileSync, existsSync } from "node:fs";
import { randomBytes } from "node:crypto";
import { FrappeError } from "./frappe.js";

const TEXT_DIR = process.env.MOCK_TEXT_DIR || new URL("../../../frappe/apps/moet_careers/moet_careers/portal_text/", import.meta.url).pathname;
const readText = (lang) => {
  const p = `${TEXT_DIR}${lang}.json`;
  return existsSync(p) ? JSON.parse(readFileSync(p, "utf8")) : {};
};

const JOBS = [
  {
    id: "HR-OPN-2026-0001", department: "Digital Solutions", location: "Dubai", employment_type: "Full-time",
    posted_on: "2026-11-02", closes_on: "2026-11-30", salary: { currency: "AED", min: 30000, max: 45000, per: "Month" },
    en: { title: "AI Engineer: Agentic Systems", html: "<p>Design, build and operate agentic services for digital government.</p><h3>What you will do</h3><ul><li>Build LLM workflows and tools</li><li>Run models on premises</li><li>Work with product teams across the ministry</li></ul><h3>What you bring</h3><ul><li>5+ years in software engineering</li><li>Python and cloud native experience</li></ul>" },
    ar: { title: "مهندس ذكاء اصطناعي: الأنظمة الوكيلة", html: "<p>تصميم وبناء وتشغيل الخدمات الوكيلة للحكومة الرقمية.</p><h3>المهام</h3><ul><li>بناء سير عمل النماذج اللغوية والأدوات</li><li>تشغيل النماذج داخل الوزارة</li><li>العمل مع فرق المنتجات في الوزارة</li></ul><h3>المتطلبات</h3><ul><li>خبرة 5 سنوات أو أكثر في هندسة البرمجيات</li><li>خبرة في Python والحوسبة السحابية</li></ul>" },
  },
  {
    id: "HR-OPN-2026-0002", department: "Economic Analysis", location: "Abu Dhabi", employment_type: "Full-time",
    posted_on: "2026-10-28", closes_on: "2026-12-15", salary: null,
    en: { title: "Data Analyst: Economic Indicators", html: "<p>Analyse economic and licensing data and build dashboards that inform policy.</p><ul><li>SQL and Python</li><li>Power BI or similar</li></ul>" },
    ar: { title: "محلل بيانات: المؤشرات الاقتصادية", html: "<p>تحليل البيانات الاقتصادية وبيانات التراخيص وبناء لوحات متابعة تدعم السياسات.</p><ul><li>SQL و Python</li><li>Power BI أو ما يماثله</li></ul>" },
  },
  {
    id: "HR-OPN-2026-0003", department: "Tourism Development", location: "Dubai", employment_type: "Contract",
    screening: [
      { key: "sq1", type: "Yes or No", required: 1, text: { en: "Do you hold a valid UAE driving licence?", ar: "هل لديك رخصة قيادة إماراتية سارية؟" } },
      { key: "sq2", type: "Number", required: 1, text: { en: "How many years of experience do you have in tourism programmes?", ar: "كم سنة من الخبرة لديك في البرامج السياحية؟" } },
      { key: "sq3", type: "Multiple Choice", required: 0, text: { en: "Which languages do you speak fluently?", ar: "ما اللغات التي تتحدثها بطلاقة؟" },
        options: ["Arabic", "English", "French", "Chinese"], labels: { ar: ["العربية", "الإنجليزية", "الفرنسية", "الصينية"] } },
      { key: "sq4", type: "Date", required: 0, text: { en: "When could you start?", ar: "متى يمكنك البدء؟" } },
    ],
    posted_on: "2026-10-20", closes_on: "2026-11-25", salary: null,
    en: { title: "Tourism Programmes Specialist", html: "<p>Plan and follow up national tourism programmes with partners across the emirates.</p>" },
    ar: null,
  },
];

const T = (en, ar) => ({ en, ar });
function sampleAssessments() {
  return [
    { id: "AINV-00001", email: "layla@example.com", job: "HR-OPN-2026-0001", type: "Knowledge Test", status: "to_do", used: 0, allowed: 1,
      deadline: "2026-11-20 17:00", title: T("Data and Excel Skills", "مهارات البيانات وإكسل"), extra: 0, showScore: false,
      instructions: T("<p>This test has two timed sections. Read each question carefully.</p>", "<p>يتكون هذا الاختبار من قسمين لكل منهما وقت محدد. اقرأ كل سؤال بعناية.</p>"),
      sections: [
        { title: T("Excel Basics", "أساسيات إكسل"), minutes: 10, instructions: T("Choose the best answer.", "اختر الإجابة الأفضل."), questions: [
          { key: "q1", type: "Single Choice", points: 1, text: T("Which function adds a range of cells?", "ما الدالة التي تجمع نطاقا من الخلايا؟"),
            options: [{ key: "a", text: T("SUM", "SUM") }, { key: "b", text: T("COUNT", "COUNT") }, { key: "c", text: T("AVERAGE", "AVERAGE") }] },
          { key: "q2", type: "Multiple Choice", points: 2, text: T("Which of these are lookup functions?", "أي مما يلي من دوال البحث؟"),
            options: [{ key: "a", text: T("XLOOKUP", "XLOOKUP") }, { key: "b", text: T("VLOOKUP", "VLOOKUP") }, { key: "c", text: T("TRIM", "TRIM") }] },
          { key: "q3", type: "Number", points: 1, text: T("A value grows from 200 to 250. What is the growth in percent?", "ارتفعت قيمة من 200 إلى 250. ما نسبة النمو المئوية؟"), options: [] },
        ] },
        { title: T("Written Task", "مهمة كتابية"), minutes: 15, instructions: T("Answer in Arabic or English.", "أجب بالعربية أو الإنجليزية."), questions: [
          { key: "q4", type: "Essay", points: 5, max_words: 200, options: [],
            text: T("In up to 200 words, explain how you would present quarterly licensing data to senior leaders.", "في حدود 200 كلمة، اشرح كيف ستعرض بيانات التراخيص الربعية على القيادات العليا.") },
          { key: "q5", type: "File Upload", points: 3, options: [], text: T("Upload a one-page chart or table that supports your answer (optional).", "ارفع رسما بيانيا أو جدولا من صفحة واحدة يدعم إجابتك (اختياري).") },
        ] },
      ] },
    { id: "AINV-00002", email: "layla@example.com", job: "HR-OPN-2026-0002", type: "Language Check", status: "completed", used: 1, allowed: 1,
      deadline: "2026-11-06 17:00", title: T("Arabic and English Writing Check", "اختبار الكتابة بالعربية والإنجليزية"), extra: 0, showScore: true, score: 85, result: "Pass",
      instructions: T("", ""), sections: [{ title: T("Writing", "الكتابة"), minutes: 20, questions: [] }] },
  ];
}

export function createMockFrappe() {
  const ASM = sampleAssessments();
  const pick = (t, l) => (t && (t[l] || t.en)) || "";
  const ownAsm = (u, id) => {
    current(u);
    const a = ASM.find((x) => x.id === id && x.email === u.email);
    if (!a) throw new FrappeError("assessment_not_found", 404);
    return a;
  };
  const advance = (a) => {
    a.attempt.section += 1;
    if (a.attempt.section >= a.sections.length) {
      a.attempt = null;
      a.status = "submitted";
    } else {
      a.attempt.ends = Date.now() + a.sections[a.attempt.section].minutes * 60_000;
    }
  };
  const tick = (a) => {
    while (a.attempt && a.attempt.ends && a.attempt.ends <= Date.now()) advance(a);
  };
  const asmSummary = (a, l) => {
    const job = JOBS.find((j) => j.id === a.job);
    return { id: a.id, title: pick(a.title, l), type: a.type, status: a.attempt ? "in_progress" : a.status, deadline: a.deadline,
      attempts_allowed: a.allowed, attempts_used: a.used, can_start: Boolean(a.attempt) || (a.used < a.allowed && a.status === "to_do"),
      reason: a.used >= a.allowed && !a.attempt ? "no_attempts_left" : "", minutes: a.sections.reduce((n, s) => n + s.minutes, 0),
      extra_time_percent: a.extra, sections: a.sections.map((s) => ({ title: pick(s.title, l), minutes: s.minutes })),
      instructions: pick(a.instructions, l), score: a.showScore && a.status === "completed" ? a.score : null,
      result: a.showScore && a.status === "completed" ? a.result : "", job_title: job ? (job[l] || job.en).title : "" };
  };
  const asmState = (a, l) => {
    const sec = a.sections[a.attempt.section];
    return { attempt: a.used, section_index: a.attempt.section, section_count: a.sections.length,
      sections: a.sections.map((s) => ({ title: pick(s.title, l), minutes: s.minutes, questions: s.questions.length })),
      section: { title: pick(sec.title, l), instructions: pick(sec.instructions, l),
        remaining_seconds: Math.max(0, Math.round((a.attempt.ends - Date.now()) / 1000)),
        questions: sec.questions.map((q) => ({ key: q.key, type: q.type, points: q.points, max_words: q.max_words || 0, text: pick(q.text, l),
          options: q.options.map((o) => ({ key: o.key, text: pick(o.text, l) })) })) },
      answers: Object.fromEntries(Object.entries(a.attempt.answers).filter(([k]) => sec.questions.some((q) => q.key === k))) };
  };
  const users = new Map([["layla@example.com", { email: "layla@example.com", full_name: "Layla Haddad", password: "password123", verified: true, epoch: 0 }]]);
  // Emails the mock would have sent: [{ to, purpose, token, link }]. Links are also printed to the console.
  const outbox = [];
  const tokens = new Map();
  const issue = (email, purpose, lang = "ar", next = "") => {
    for (const [k, v] of tokens) if (v.email === email && v.purpose === purpose) tokens.delete(k);
    const token = randomBytes(24).toString("base64url");
    tokens.set(token, { email, purpose, next, lang, expires: Date.now() + (purpose === "verify" ? 48 : 1) * 3600_000 });
    const link = `/${lang}/${purpose === "verify" ? "verify-email" : "reset-password"}?token=${token}`;
    outbox.push({ to: email, purpose, token, link });
    console.log(`mock email to ${email}: ${link}`);
  };
  const consume = (token, purpose) => {
    const t = tokens.get(token);
    if (!t || t.purpose !== purpose) throw new FrappeError("token_invalid", 400);
    tokens.delete(token);
    if (t.expires < Date.now()) throw new FrappeError("token_expired", 400);
    return t;
  };
  const current = (u) => {
    const x = users.get(u.email);
    if (!x || Number(u.epoch || 0) !== x.epoch) throw new FrappeError("signed_out", 401);
    return x;
  };
  const deletionRequests = new Set();
  const talentPool = new Set();
  const apps = [
    { id: "HR-APP-2026-00012", job: "HR-OPN-2026-0001", email: "layla@example.com", submitted: "2026-11-05", status: { code: "interview", tone: "success" }, ready: true },
    { id: "HR-APP-2026-00007", job: "HR-OPN-2026-0002", email: "layla@example.com", submitted: "2026-10-30", status: { code: "under_review", tone: "info" }, ready: false },
  ];
  const languages = [{ code: "ar", label: "العربية", dir: "rtl" }, { code: "en", label: "English", dir: "ltr" }];
  const lang = (l) => {
    if (!languages.some((x) => x.code === l)) throw new FrappeError("unknown_language", 404);
    return l;
  };
  const present = (j, l, full) => {
    const c = j[l] || j.en;
    const content_lang = j[l] ? l : "en";
    const o = { id: j.id, title: c.title, content_lang, content_dir: content_lang === "ar" ? "rtl" : "ltr", department: j.department,
      location: j.location, employment_type: j.employment_type, posted_on: j.posted_on, closes_on: j.closes_on, salary: j.salary,
      summary: c.html.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim().slice(0, 240), accepting: true };
    if (full) {
      o.description_html = c.html;
      o.screening = (j.screening || []).map((q) => ({ key: q.key, type: q.type, required: Boolean(q.required), text: (q.text[l] || q.text.en),
        options: (q.options || (q.type === "Yes or No" ? ["Yes", "No"] : [])).map((v, i) => ({ value: v, label: (q.labels && q.labels[l] && q.labels[l][i]) || v })) }));
    }
    return o;
  };
  const reference = (id) => `MOET-2026-${id.slice(-5)}A`;
  const titleOf = (job, l) => { const j = JOBS.find((x) => x.id === job); return j ? (j[l] || j.en).title : job; };
  const own = (u, id) => {
    current(u);
    const a = apps.find((x) => x.id === id && x.email === u.email);
    if (!a) throw new FrappeError("application_not_found", 404);
    return a;
  };
  return {
    _outbox: outbox,
    async config(l) {
      const out = { languages, default_language: "ar", version: "mock" };
      if (!l) return out;
      lang(l);
      return { ...out, lang: l, dir: l === "ar" ? "rtl" : "ltr", text: { ...readText("en"), ...readText(l) }, main_site_url: "https://www.moet.gov.ae",
        logo: "", logo_dark: "", allow_signup: true, max_cv_mb: 5, countries: ["United Arab Emirates", "Egypt", "India", "Jordan", "United Kingdom"] };
    },
    async jobs({ lang: l, q = "", department = "", location = "", page = 1 }) {
      lang(l);
      const ql = q.toLowerCase();
      const rows = JOBS.filter((j) => (!department || j.department === department) && (!location || j.location === location) &&
        (!ql || [j.en.title, j.en.html, j.ar?.title, j.ar?.html].some((s) => s && s.toLowerCase().includes(ql))));
      return { jobs: rows.map((j) => present(j, l)), total: rows.length, page: Number(page) || 1, pages: 1,
        filters: { departments: [...new Set(JOBS.map((j) => j.department))].sort(), locations: [...new Set(JOBS.map((j) => j.location))].sort() } };
    },
    async job(l, name) {
      lang(l);
      const j = JOBS.find((x) => x.id === name);
      if (!j) throw new FrappeError("job_not_found", 404);
      return present(j, l, true);
    },
    async signup({ full_name, email, password }) {
      email = String(email || "").trim().toLowerCase();
      if (!full_name || full_name.trim().length < 2) throw new FrappeError("name_required", 400);
      if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) throw new FrappeError("email_invalid", 400);
      if (!password || password.length < 8) throw new FrappeError("password_short", 400);
      if (users.has(email)) throw new FrappeError("account_exists", 409);
      users.set(email, { email, full_name: full_name.trim(), password, verified: false, epoch: 0 });
      issue(email, "verify", arguments[0].lang || "ar", arguments[0].next_path || "");
      return { email, full_name: full_name.trim(), verification_sent: true };
    },
    async authenticate(email, password) {
      const u = users.get(String(email || "").trim().toLowerCase());
      if (!u || u.password !== password) throw new FrappeError("bad_credentials", 401);
      if (!u.verified) throw new FrappeError("email_not_verified", 403);
      return { email: u.email, full_name: u.full_name, epoch: u.epoch };
    },
    async resendVerification(email, lang, next) {
      const u = users.get(email);
      if (u && !u.verified) issue(email, "verify", lang, next);
      return { sent: true };
    },
    async verifyEmail(token) {
      const t = consume(token, "verify");
      const u = users.get(t.email);
      u.verified = true;
      return { email: u.email, full_name: u.full_name, epoch: u.epoch, next: t.next, lang: t.lang };
    },
    async requestPasswordReset(email, lang) {
      if (users.has(email)) issue(email, "reset", lang);
      return { sent: true };
    },
    async resetPassword(token, password) {
      if (!password || password.length < 8) throw new FrappeError("password_short", 400);
      const t = consume(token, "reset");
      const u = users.get(t.email);
      u.password = password;
      u.verified = true;
      u.epoch += 1;
      return { ok: true };
    },
    async applications(u, l) {
      lang(l);
      current(u);
      return { applications: apps.filter((a) => a.email === u.email).map((a) => ({ id: a.id, job: a.job, title: titleOf(a.job, l),
        submitted: a.submitted, reference: reference(a.id), status: a.status })) };
    },
    async application(u, id, l) {
      lang(l);
      const a = own(u, id);
      return { id: a.id, job: a.job, title: titleOf(a.job, l), submitted: a.submitted, reference: reference(a.id), status: a.status,
        details_state: a.ready ? "ready" : "preparing",
        details: a.ready ? { years: 7, work: [{ position: "Senior AI Engineer", employer: "Emirates Digital", start: "2021-03", end: "" },
          { position: "Software Engineer", employer: "Gulf Tech", start: "2018-01", end: "2021-02" }],
          education: [{ degree: "MSc Computer Science", institution: "Khalifa University", end: "2017" }],
          skills: ["Python", "LLM agents", "Kubernetes", "PostgreSQL", "Arabic NLP"], languages: ["Arabic (Native)", "English (Fluent)"],
          certificates: ["AWS Solutions Architect"] } : null };
    },
    async addCorrection(u, id, note) {
      own(u, id);
      if (!note || note.trim().length < 3) throw new FrappeError("note_length", 400);
      return { ok: true };
    },
    async apply(fields) {
      const u = current({ email: fields.email, epoch: fields.epoch });
      if (apps.some((a) => a.email === u.email && a.job === fields.job)) throw new FrappeError("already_applied", 409);
      if (!/^\+?[0-9][0-9 ()-]{6,19}$/.test(String(fields.phone || "").trim())) throw new FrappeError("phone_invalid", 400);
      const job = JOBS.find((j) => j.id === fields.job);
      const answers = JSON.parse(fields.screening || "{}");
      const missing = (job?.screening || []).filter((q) => q.required && (answers[q.key] === undefined || answers[q.key] === "" || answers[q.key] === null));
      if (missing.length) throw new FrappeError("screening_required", 400, { questions: missing.map((q) => q.key) });
      const id = `HR-APP-2026-${String(100 + apps.length).padStart(5, "0")}`;
      apps.unshift({ id, job: fields.job, email: u.email, submitted: new Date().toISOString().slice(0, 10), status: { code: "under_review", tone: "info" }, ready: false });
      return { application: id, reference: reference(id) };
    },
    // ---- assessments (in-memory sample: a two-section timed test and a completed language check)
    async assessments(u, l) {
      lang(l);
      current(u);
      return { assessments: ASM.filter((a) => a.email === u.email).map((a) => asmSummary(a, l)) };
    },
    async assessment(u, id, l) {
      lang(l);
      return asmSummary(ownAsm(u, id), l);
    },
    async startAssessment(u, id, l) {
      const a = ownAsm(u, id);
      if (!a.attempt) {
        if (a.used >= a.allowed) throw new FrappeError("no_attempts_left", 409);
        a.used += 1;
        a.status = "in_progress";
        a.attempt = { section: 0, answers: {}, ends: Date.now() + a.sections[0].minutes * 60_000 };
      }
      return asmState(a, l);
    },
    async assessmentState(u, id, l) {
      const a = ownAsm(u, id);
      tick(a);
      return a.attempt ? asmState(a, l) : { finished: true };
    },
    async saveAnswer(u, id, key, answer, section) {
      const a = ownAsm(u, id);
      tick(a);
      if (!a.attempt || section !== a.attempt.section) throw new FrappeError("section_closed", 409);
      if (!a.sections[section].questions.some((q) => q.key === key)) throw new FrappeError("question_closed", 409);
      if (answer === null || answer === "") delete a.attempt.answers[key];
      else a.attempt.answers[key] = answer;
      return { saved: true };
    },
    async uploadAnswer(u, id, key, section, file) {
      await this.saveAnswer(u, id, key, { file_url: `/private/files/${file.name}`, name: file.name }, section);
      return { saved: true, name: file.name };
    },
    async nextSection(u, id, section, l) {
      const a = ownAsm(u, id);
      tick(a);
      if (a.attempt && section === a.attempt.section) advance(a);
      return a.attempt ? asmState(a, l) : { finished: true };
    },
    async assessmentEvent() {
      return { ok: true };
    },
    async myData(u) {
      current(u);
      return { account: { email: u.email }, applications: apps.filter((a) => a.email === u.email).map((a) => ({ reference: reference(a.id), applied_at: a.submitted })), consents: [] };
    },
    async dataRequests(u) {
      current(u);
      return { deletion_pending: deletionRequests.has(u.email), in_talent_pool: talentPool.has(u.email) };
    },
    async setTalentPool(u, join) {
      current(u);
      if (join) talentPool.add(u.email); else talentPool.delete(u.email);
      return { in_talent_pool: talentPool.has(u.email) };
    },
    async respondOffer(u) {
      current(u);
      throw new FrappeError("offer_not_open", 409);
    },
    async bookInterview(u) {
      current(u);
      throw new FrappeError("booking_closed", 409);
    },
    async requestDeletion(u) {
      current(u);
      const already = deletionRequests.has(u.email);
      deletionRequests.add(u.email);
      return { requested: true, already };
    },
    async logo() {
      throw new FrappeError("not_found", 404);
    },
  };
}
