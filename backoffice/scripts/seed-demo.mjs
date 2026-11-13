// Fills an empty recruitment database with demo content through the public APIs (nothing is written directly).
// Usage: API=http://localhost:5080 ADMIN_EMAIL=... ADMIN_PASSWORD=... PORTAL_KEY=key:secret node backoffice/scripts/seed-demo.mjs
const API = (process.env.API || "http://localhost:5080").replace(/\/$/, "");
const { ADMIN_EMAIL, ADMIN_PASSWORD, PORTAL_KEY } = process.env;
let cookie = "";

async function call(method, path, body, { portal = false, form } = {}) {
  const headers = { Accept: "application/json" };
  if (portal) headers.Authorization = `token ${PORTAL_KEY}`;
  else {
    if (cookie) headers.Cookie = cookie;
    if (method !== "GET") headers["X-Backoffice"] = "1";
  }
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const res = await fetch(API + path, { method, headers, body: form || (body !== undefined ? JSON.stringify(body) : undefined) });
  const setCookie = res.headers.get("set-cookie");
  if (setCookie) cookie = setCookie.split(";")[0];
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${text.slice(0, 300)}`);
  return text ? JSON.parse(text) : null;
}
const bo = (m, p, b) => call(m, `/api/backoffice${p}`, b);
const portal = (m, p, b, form) => call(m, `/api/portal/${p}`, b, { portal: true, form });

const today = new Date();
const day = (n) => new Date(today.getTime() + n * 86400000).toISOString().slice(0, 10);

async function main() {
  await bo("POST", "/sign-in", { email: ADMIN_EMAIL, password: ADMIN_PASSWORD });
  const existing = await bo("GET", "/jobs?size=10");
  if (existing.total > 0) {
    console.log("Demo content already present; nothing to do.");
    return;
  }

  // Staff
  for (const s of [
    { email: "fatima.alhashmi@moet.test", full_name: "فاطمة الهاشمي", role: "RecruitmentAdmin" },
    { email: "khalid.almansoori@moet.test", full_name: "خالد المنصوري", role: "Recruiter" },
    { email: "sara.alketbi@moet.test", full_name: "سارة الكتبي", role: "Recruiter" },
  ]) await bo("POST", "/staff", { ...s, is_enabled: true, password: "Demo-Password-2026" });
  const staff = (await bo("GET", "/lookups")).staff;

  // Departments and interview rounds
  const depts = [];
  for (const [ar, en] of [["إدارة التحول الرقمي", "Digital Transformation"], ["إدارة السياسات الاقتصادية", "Economic Policy"], ["إدارة الموارد البشرية", "Human Resources"], ["إدارة السياحة", "Tourism"]])
    depts.push((await bo("POST", "/departments", { name: ar, name_en: en, is_active: true, description: null, skills: null })).id);
  const rounds = [];
  for (const [name, name_en, skills] of [
    ["مقابلة فنية", "Technical Interview", ["المعرفة التقنية", "حل المشكلات", "التواصل"]],
    ["مقابلة مع لجنة الموارد البشرية", "HR Panel Interview", ["الملاءمة الثقافية", "الدافعية", "التواصل"]],
  ]) rounds.push((await bo("POST", "/rounds", { name, name_en, description: "", is_active: true, skills })).id);

  // Screening questions
  const sqLicence = (await bo("POST", "/screening-questions", { text: "هل تحمل رخصة قيادة إماراتية سارية؟", text_en: "Do you hold a valid UAE driving licence?", english_approved: true, answer_type: "YesNo", category: "عام", is_active: true, options: [] })).id;
  const sqYears = (await bo("POST", "/screening-questions", { text: "كم سنة من الخبرة لديك في تحليل البيانات؟", text_en: "How many years of data analysis experience do you have?", english_approved: true, answer_type: "Number", category: "خبرة", is_active: true, options: [] })).id;
  const sqDegree = (await bo("POST", "/screening-questions", {
    text: "ما أعلى مؤهل علمي حصلت عليه؟", text_en: "What is your highest qualification?", english_approved: true, answer_type: "SingleChoice", category: "تعليم", is_active: true,
    options: [{ text: "دبلوم", text_en: "Diploma" }, { text: "بكالوريوس", text_en: "Bachelor's Degree" }, { text: "ماجستير", text_en: "Master's Degree" }, { text: "دكتوراه", text_en: "Doctorate" }],
  })).id;
  const sqStart = (await bo("POST", "/screening-questions", { text: "متى يمكنك المباشرة؟", text_en: "", english_approved: false, answer_type: "Date", category: "عام", is_active: true, options: [] })).id;

  // Question bank and a two-section test
  const q = async (body) => (await bo("POST", "/questions", { category: "تحليل البيانات", job_family: "البيانات", difficulty: "Medium", points: 1, tolerance: 0, rubric: "", max_words: 0, is_active: true, english_approved: true, accepted_answers: [], ...body })).id;
  const qs = [];
  qs.push(await q({ question_type: "SingleChoice", text: "<p>أي من الدوال التالية يحسب المتوسط في Excel؟</p>", text_en: "<p>Which function calculates the mean in Excel?</p>",
    options: [{ text: "AVERAGE", text_en: "AVERAGE", correct: true }, { text: "SUM", text_en: "SUM", correct: false }, { text: "COUNT", text_en: "COUNT", correct: false }] }));
  qs.push(await q({ question_type: "MultipleChoice", text: "<p>أي مما يلي يُعد من مؤشرات النزعة المركزية؟</p>", text_en: "<p>Which of these are measures of central tendency?</p>",
    options: [{ text: "الوسيط", text_en: "Median", correct: true }, { text: "المنوال", text_en: "Mode", correct: true }, { text: "المدى", text_en: "Range", correct: false }] }));
  qs.push(await q({ question_type: "TrueFalse", text: "<p>الارتباط القوي يعني دائمًا وجود علاقة سببية.</p>", text_en: "<p>A strong correlation always means causation.</p>",
    options: [{ text: "صح", text_en: "True", correct: false }, { text: "خطأ", text_en: "False", correct: true }] }));
  qs.push(await q({ question_type: "Number", text: "<p>إذا ارتفع الناتج المحلي من 400 إلى 420 مليار درهم، فما نسبة النمو المئوية؟</p>", text_en: "<p>If GDP grew from 400 to 420 billion dirhams, what is the growth rate in percent?</p>", accepted_answers: ["5"], tolerance: 0.1, difficulty: "Easy" }));
  qs.push(await q({ question_type: "ShortAnswer", text: "<p>ما اسم الوزارة الاتحادية المسؤولة عن السياحة في الدولة؟</p>", text_en: "<p>Which federal ministry is responsible for tourism in the UAE?</p>", accepted_answers: ["وزارة الاقتصاد والسياحة", "Ministry of Economy and Tourism"], category: "عام" }));
  qs.push(await q({ question_type: "Essay", text: "<p>صف كيف تقيس أثر مبادرة لدعم الشركات الصغيرة والمتوسطة.</p>", text_en: "<p>Describe how you would measure the impact of an SME support initiative.</p>", points: 5, max_words: 300, rubric: "Clear indicators (2), data sources (1), baseline and comparison (1), limitations (1).", difficulty: "Hard" }));
  const test = (await bo("POST", "/assessments", {
    title: "اختبار تحليل البيانات", title_en: "Data Analysis Test", instructions: "<p>يتكون الاختبار من قسمين مؤقتين. لا يمكن الرجوع إلى قسم بعد الانتقال منه.</p>",
    instructions_en: "<p>The test has two timed sections. You cannot go back to a section after you move on.</p>", english_approved: true,
    assessment_type: "KnowledgeTest", status: "Published", pass_mark: 60, max_attempts: 1, score_policy: "Highest", shuffle_questions: true, shuffle_options: true,
    show_score_to_candidate: true, default_deadline_days: 7,
    sections: [
      { title: "المفاهيم الأساسية", title_en: "Core Concepts", instructions: "", instructions_en: "", minutes: 15, pass_mark: 50,
        items: [{ question_id: qs[0], draw_category: "", draw_difficulty: "", draw_job_family: "", draw_count: 0, points_override: null },
                { question_id: null, draw_category: "تحليل البيانات", draw_difficulty: "", draw_job_family: "", draw_count: 3, points_override: null }] },
      { title: "سؤال مقالي", title_en: "Written Answer", instructions: "اكتب إجابة موجزة.", instructions_en: "Write a short answer.", minutes: 20, pass_mark: 0,
        items: [{ question_id: qs[5], draw_category: "", draw_difficulty: "", draw_job_family: "", draw_count: 0, points_override: null }] },
    ],
  })).id;

  // Jobs
  const job = async (body) => bo("POST", "/jobs", {
    department_id: depts[0], location: "دبي", employment_type: "FullTime", status: "Open", is_published: true, posted_on: day(-10), closes_on: day(30),
    salary_min: null, salary_max: null, salary_currency: "AED", salary_period: "Month", show_salary: false, screening: [], stages: [], row_version: null, ...body,
  });
  const analyst = await job({
    title: "محلل بيانات أول", description: "<p>نبحث عن محلل بيانات لدعم صناعة القرار الاقتصادي.</p><h3>المهام</h3><ul><li>تحليل مؤشرات الاقتصاد الوطني</li><li>إعداد لوحات المعلومات للإدارة العليا</li></ul><h3>المتطلبات</h3><ul><li>بكالوريوس في الإحصاء أو علوم البيانات (إلزامي)</li><li>خبرة لا تقل عن 3 سنوات في التحليل (إلزامي)</li><li>إتقان SQL و Power BI</li></ul>",
    translations: [{ lang: "en", title: "Senior Data Analyst", description: "<p>We are looking for a data analyst to support economic decision making.</p><h3>Duties</h3><ul><li>Analyse national economic indicators</li><li>Build dashboards for senior management</li></ul><h3>Requirements</h3><ul><li>Bachelor's in statistics or data science (required)</li><li>At least 3 years of analysis experience (required)</li><li>Strong SQL and Power BI</li></ul>", approved: true }],
    salary_min: 25000, salary_max: 35000, show_salary: true,
    screening: [{ question_id: sqYears, required: true, rule: "AtLeast", expected: "3" }, { question_id: sqDegree, required: true, rule: null, expected: null }, { question_id: sqStart, required: false, rule: null, expected: null }],
    stages: [{ assessment_id: test, send_when: "OnApply", deadline_days: 7 }],
  });
  await job({ title: "أخصائي سياسات اقتصادية", description: "<p>المساهمة في إعداد السياسات الاقتصادية وتقييم أثرها.</p>", department_id: depts[1], location: "أبوظبي",
    translations: [{ lang: "en", title: "Economic Policy Specialist", description: "<p>Help prepare economic policies and assess their impact.</p>", approved: false }],
    screening: [{ question_id: sqLicence, required: true, rule: "MustBe", expected: "Yes" }] });
  await job({ title: "منسق فعاليات سياحية", description: "<p>تنسيق الفعاليات والمعارض السياحية داخل الدولة وخارجها.</p>", department_id: depts[3], employment_type: "Contract", translations: [] });
  await job({ title: "مهندس برمجيات", description: "<p>تطوير الخدمات الرقمية للوزارة.</p>", status: "Draft", is_published: false, translations: [] });
  await job({ title: "مستشار موارد بشرية", description: "<p>تقديم المشورة في سياسات الموارد البشرية.</p>", department_id: depts[2], status: "Closed", posted_on: day(-60), closes_on: day(-5), translations: [] });

  // HR's approved screening criteria for the analyst role (CVs are scored only against approved criteria).
  await bo("PUT", `/jobs/${analyst.id}/criteria`, {
    approve: true,
    criteria: [
      { criterion: "بكالوريوس في الإحصاء أو علوم البيانات", weight: 25, must: true },
      { criterion: "3 سنوات على الأقل من الخبرة في تحليل البيانات", weight: 30, must: true },
      { criterion: "إتقان SQL و Power BI", weight: 25, must: false },
      { criterion: "خبرة في المؤشرات الاقتصادية أو العمل الحكومي", weight: 20, must: false },
    ],
  });

  // Applicants through the portal API
  const jobCode = analyst.code;
  const detail = await portal("GET", `job?lang=ar&name=${jobCode}`);
  const degreeOption = detail.message.screening.find((x) => x.type === "Single Choice").options;
  const pdf = new Blob([Buffer.from("%PDF-1.4\n% demo CV\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n")], { type: "application/pdf" });
  const people = [
    ["ليلى أحمد", "layla.ahmed@example.test", "6", 2], ["محمد علي", "mohammed.ali@example.test", "2", 1], ["Aisha Khan", "aisha.khan@example.test", "4", 2],
    ["عمر حسن", "omar.hassan@example.test", "8", 3], ["نورة سالم", "noura.salem@example.test", "1", 1], ["Daniel Smith", "daniel.smith@example.test", "5", 2],
  ];
  const epochs = {};
  for (const [name, email, years, deg] of people) {
    const s = await portal("POST", "signup", { full_name: name, email, password: "Demo-Applicant-2026", lang: /[a-z]/i.test(name) ? "en" : "ar" });
    const form = new FormData();
    for (const [k, v] of Object.entries({ email, epoch: String(s.message.epoch ?? 0), job: jobCode, phone: "+971501234567", country: "United Arab Emirates", declaration: "1", lang: "ar",
      cover_note: "أتطلع إلى الانضمام إلى فريق الوزارة.", screening: JSON.stringify({ sq1: years, sq2: degreeOption[deg].value }) })) form.append(k, v);
    form.append("cv", pdf, "cv.pdf");
    epochs[email] = String(s.message.epoch ?? 0);
    await portal("POST", "apply", undefined, form);
  }

  // Pipeline activity
  const apps = (await bo("GET", "/applicants?size=10&sort=name&dir=asc")).items;
  await bo("POST", `/applicants/${apps[0].id}/status`, { status: "Replied" });
  await bo("POST", `/applicants/${apps[1].id}/status`, { status: "Hold" });
  await bo("POST", `/applicants/${apps[0].id}/notes`, { text: "خبرة قوية في Power BI. يُقترح دعوته لمقابلة فنية." });
  await bo("POST", "/interviews", { application_id: apps[0].id, round_id: rounds[0], date: day(2), from: "10:00", to: "11:00", location: "مقر الوزارة، دبي", notes: "", panelists: staff.slice(0, 2).map((x) => x.id) });
  await bo("POST", "/interviews", { application_id: apps[2].id, round_id: rounds[1], date: day(3), from: "13:00", to: "13:45", location: "Microsoft Teams", notes: "", panelists: staff.slice(1, 3).map((x) => x.id) });
  const offer = await bo("POST", "/offers", { application_id: apps[3].id, position_title: "محلل بيانات أول", offer_date: day(0), start_date: day(30), monthly_salary: 30000, currency: "AED", terms: "عقد دائم مع فترة تجربة لمدة ستة أشهر." });
  await bo("POST", `/offers/${offer.id}/status`, { status: "Sent" });
  // Big Five personality questionnaire (public-domain IPIP items): add, publish, and have two applicants complete it.
  const big5 = await bo("POST", "/assessment-templates/ipip-big-five-50", {});
  const q5 = await bo("GET", `/assessments/${big5.id}`);
  await bo("PUT", `/assessments/${big5.id}`, { ...q5, status: "Published" });
  for (const [k, app] of [apps[0], apps[2]].entries()) {
    await bo("POST", `/applicants/${app.id}/invite`, { assessment_id: big5.id, deadline_days: 7 });
    const inv = (await bo("GET", `/applicants/${app.id}`)).invitations.find((x) => x.assessment_id === big5.id);
    const who = { email: app.email, epoch: epochs[app.email], name: inv.code };
    const state = (await portal("POST", "start_assessment", { ...who, lang: "ar" })).message;
    for (const [i, q] of state.section.questions.entries()) {
      const answer = String(((i * (k + 3)) % 5) + 1);
      await portal("POST", "save_assessment_answer", { ...who, key: q.key, section: state.section_index, answer: JSON.stringify(answer) });
    }
    await portal("POST", "next_assessment_section", { ...who, section: state.section_index, lang: "ar" });
    const done = (await bo("GET", `/applicants/${app.id}`)).invitations.find((x) => x.assessment_id === big5.id);
    if (done.result !== "Completed" || !done.attempts[0].trait_profile) throw new Error(`Big Five not scored for ${app.email}: ${JSON.stringify(done)}`);
  }

  console.log(`Demo content created: ${apps.length} applicants for ${jobCode}, two Big Five profiles.`);
}

main().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
