// A scripted OpenAI-compatible model for the walkthrough film: it answers each JSON task (requirements from the job
// description, CV reading and scoring, interview guide) with fixed, realistic Arabic content, so the film shows the real
// screens end to end without calling a live model. CVs are told apart by words only they contain.
import http from "node:http";

const REQS = [
  { category: "Qualification", requirement: "بكالوريوس في الإحصاء أو علوم البيانات أو تخصص ذي صلة", min_years: 0, level: "", essential: true, high: false,
    guide: "0: لا توجد شهادة جامعية\n4: بكالوريوس في تخصص بعيد\n7: بكالوريوس في تخصص قريب مثل الرياضيات أو نظم المعلومات\n10: بكالوريوس أو أعلى في الإحصاء أو علوم البيانات" },
  { category: "Experience", requirement: "تحليل البيانات", min_years: 3, level: "", essential: true, high: true,
    guide: "0: لا خبرة في التحليل\n4: أقل من سنتين أو مهام تحليل جانبية\n7: 3 سنوات في تحليل البيانات\n10: 5 سنوات أو أكثر مع مؤشرات ولوحات معلومات" },
  { category: "Skill", requirement: "SQL", min_years: 0, level: "", essential: true, high: false,
    guide: "0: لا ذكر لـ SQL\n4: استخدام بسيط ضمن تقارير\n7: استعلامات منتظمة في العمل\n10: استعلامات متقدمة على قواعد بيانات كبيرة" },
  { category: "Skill", requirement: "Power BI أو Tableau", min_years: 0, level: "", essential: false, high: true,
    guide: "0: لا خبرة\n4: تقارير بسيطة\n7: لوحات معلومات منتظمة\n10: لوحات معلومات للإدارة العليا" },
  { category: "Experience", requirement: "المؤشرات الاقتصادية أو العمل الحكومي", min_years: 0, level: "", essential: false, high: false,
    guide: "0: لا خبرة\n4: تعامل محدود مع بيانات اقتصادية\n7: عمل في جهة حكومية أو مع مؤشرات\n10: إعداد مؤشرات اقتصادية في جهة حكومية" },
  { category: "Language", requirement: "الإنجليزية", min_years: 0, level: "Fluent", essential: false, high: false,
    guide: "0: لا ذكر\n4: مستوى أساسي\n7: مستوى جيد\n10: بطلاقة" },
];

const PEOPLE = {
  strong: {
    marker: "مركز دبي للإحصاء",
    profile: {
      basics: { label: "محللة بيانات أولى", summary: "محللة بيانات بخبرة في المؤشرات الاقتصادية ولوحات المعلومات." },
      work: [
        { name: "مركز دبي للإحصاء", description: "", position: "محللة بيانات أولى", startDate: "2021-01", endDate: "", summary: "لوحات Power BI للمؤشرات الاقتصادية واستعلامات SQL متقدمة.", highlights: [] },
        { name: "دائرة الاقتصاد والسياحة", description: "", position: "محللة بيانات", startDate: "2018-03", endDate: "2020-12", summary: "تحليل بيانات الرخص التجارية باستخدام SQL و Python.", highlights: [] },
      ],
      education: [{ institution: "جامعة الإمارات العربية المتحدة", area: "الإحصاء", studyType: "بكالوريوس", startDate: "", endDate: "2017" }],
      skills: [{ name: "تحليل البيانات", keywords: ["SQL", "Power BI", "Python"] }],
      languages: [{ language: "العربية", fluency: "لغة أم" }, { language: "الإنجليزية", fluency: "بطلاقة" }],
      certificates: [], projects: [], stated_years_of_experience: 0, cv_language: "ar",
    },
    score: [
      [10, "بكالوريوس الإحصاء، جامعة الإمارات العربية المتحدة، 2017", "بكالوريوس في الإحصاء، وهو التخصص المطلوب.", "met", [], ""],
      [9, "محللة بيانات أولى، مركز دبي للإحصاء، 2021-01 حتى الآن", "قرابة 9 سنوات في تحليل البيانات في جهتين حكوميتين.", "met", [1, 2], "خبرة في قيادة فريق تحليل."],
      [9, "كتابة استعلامات SQL متقدمة على قواعد بيانات SQL Server", "تكتب استعلامات SQL متقدمة بشكل يومي.", "met", [], "أمثلة على تحسين أداء الاستعلامات."],
      [9, "بناء لوحات معلومات Power BI للمؤشرات الاقتصادية الشهرية", "تبني لوحات Power BI شهرية للمؤشرات.", "n/a", [], "لوحات موجهة للإدارة العليا."],
      [9, "بناء لوحات معلومات Power BI للمؤشرات الاقتصادية الشهرية", "تعمل على المؤشرات الاقتصادية في جهة حكومية.", "n/a", [], "إعداد منهجية مؤشر جديد."],
      [10, "العربية (لغة أم)، الإنجليزية (بطلاقة)", "تتحدث الإنجليزية بطلاقة.", "n/a", [], ""],
    ],
    summary: "Strong match: a statistics degree, about nine years of data analysis in government, advanced SQL and Power BI dashboards for economic indicators.",
    summary_ar: "تطابق قوي: بكالوريوس في الإحصاء، وقرابة تسع سنوات في تحليل البيانات في جهات حكومية، وخبرة متقدمة في SQL ولوحات Power BI للمؤشرات الاقتصادية.",
  },
  mid: {
    marker: "شركة اتصالات",
    profile: {
      basics: { label: "محلل تقارير", summary: "" },
      work: [
        { name: "شركة اتصالات", description: "", position: "محلل تقارير", startDate: "2024-06", endDate: "", summary: "تقارير مبيعات شهرية بـ Excel و SQL.", highlights: [] },
        { name: "شركة اتصالات", description: "", position: "منسق خدمة عملاء", startDate: "2023-07", endDate: "2024-05", summary: "خدمة العملاء.", highlights: [] },
      ],
      education: [{ institution: "جامعة زايد", area: "نظم المعلومات", studyType: "بكالوريوس", startDate: "", endDate: "2023" }],
      skills: [{ name: "تقارير", keywords: ["Excel", "SQL"] }], languages: [{ language: "الإنجليزية", fluency: "جيد" }],
      certificates: [{ name: "مقدمة في تحليل البيانات", issuer: "", date: "2024" }], projects: [], stated_years_of_experience: 0, cv_language: "ar",
    },
    score: [
      [7, "بكالوريوس نظم المعلومات، جامعة زايد، 2023", "بكالوريوس نظم المعلومات، تخصص قريب.", "met", [], "شهادة في الإحصاء أو علوم البيانات."],
      [6, "محلل تقارير، شركة اتصالات، 2024-06 حتى الآن", "قرابة سنتين ونصف في التقارير.", "not_met", [1], "3 سنوات على الأقل في تحليل البيانات."],
      [5, "إعداد تقارير المبيعات الشهرية باستخدام Excel و SQL", "يستخدم SQL ضمن التقارير الشهرية.", "unclear", [], "أمثلة على استعلامات SQL متقدمة."],
      [1, "", "No evidence in CV", "n/a", [], "خبرة في Power BI أو Tableau."],
      [2, "", "No evidence in CV", "n/a", [], "عمل مع مؤشرات اقتصادية أو في جهة حكومية."],
      [7, "العربية، الإنجليزية (جيد)", "مستوى جيد في الإنجليزية.", "n/a", [], "إتقان الإنجليزية بطلاقة."],
    ],
    summary: "Partial match: an information systems degree and about two and a half years of reporting with Excel and SQL; less than the three years asked for and no dashboard tools shown.",
    summary_ar: "تطابق جزئي: بكالوريوس نظم المعلومات وقرابة سنتين ونصف في إعداد التقارير بـ Excel و SQL، أقل من السنوات الثلاث المطلوبة ودون أدوات لوحات المعلومات.",
  },
  weak: {
    marker: "Restaurant Supervisor",
    profile: {
      basics: { label: "Restaurant Supervisor", summary: "" },
      work: [
        { name: "Marina Restaurant", description: "", position: "Restaurant Supervisor", startDate: "2016", endDate: "", summary: "Team of 12, stock counts in Excel.", highlights: [] },
        { name: "Beach Hotel", description: "", position: "Waiter", startDate: "2014", endDate: "2016", summary: "", highlights: [] },
      ],
      education: [{ institution: "", area: "", studyType: "High school diploma", startDate: "", endDate: "2014" }],
      skills: [{ name: "Customer service", keywords: ["Excel"] }], languages: [], certificates: [], projects: [], stated_years_of_experience: 0, cv_language: "en",
    },
    score: [
      [0, "High school diploma, 2014", "Only a high school diploma.", "not_met", [], "A bachelor's degree in statistics or data science."],
      [0, "", "No evidence in CV", "not_met", [], "Data analysis experience."],
      [0, "", "No evidence in CV", "not_met", [], "SQL used at work."],
      [0, "", "No evidence in CV", "n/a", [], "Dashboards in Power BI or Tableau."],
      [0, "", "No evidence in CV", "n/a", [], "Work with economic indicators or in government."],
      [2, "", "Writes the CV in English; level not stated.", "n/a", [], "The English level."],
    ],
    summary: "Not a match: hospitality experience without data analysis, SQL or a relevant degree.",
    summary_ar: "غير مطابق: خبرة في الضيافة دون تحليل بيانات أو SQL أو شهادة ذات صلة.",
  },
};

const GUIDE = {
  sections: [
    { skill: "تحليل البيانات", questions: [
      { ar: "حدثينا عن مؤشر اقتصادي بنيتِه من البداية: من أين جاءت البيانات وكيف تحققتِ من جودتها؟", en: "Tell us about an economic indicator you built from scratch: where did the data come from and how did you check its quality?",
        listen_for_ar: ["مصادر بيانات واضحة", "خطوات تحقق من الجودة", "أثر المؤشر على القرار"], listen_for_en: ["Clear data sources", "Quality checks", "Impact on decisions"] },
      { ar: "صفي موقفا اكتشفتِ فيه أن نتيجة تحليل كانت خاطئة. ماذا فعلتِ؟", en: "Describe a time you found an analysis result was wrong. What did you do?",
        listen_for_ar: ["الاعتراف بالخطأ مبكرا", "تصحيح المنهجية", "إبلاغ المعنيين"], listen_for_en: ["Owns the mistake early", "Fixes the method", "Tells stakeholders"] },
    ] },
    { skill: "SQL", questions: [
      { ar: "كيف تتعاملين مع استعلام بطيء على جدول بملايين الصفوف؟", en: "How do you handle a slow query on a table with millions of rows?",
        listen_for_ar: ["قراءة خطة التنفيذ", "الفهارس", "تقليل البيانات المقروءة"], listen_for_en: ["Reads the execution plan", "Indexes", "Reads less data"] },
    ] },
    { skill: "التواصل", questions: [
      { ar: "كيف تشرحين نتيجة تحليل معقدة لقيادي غير متخصص في دقيقتين؟", en: "How would you explain a complex result to a non-specialist leader in two minutes?",
        listen_for_ar: ["رسالة رئيسية واحدة", "رسم بياني بسيط", "توصية واضحة"], listen_for_en: ["One key message", "A simple chart", "A clear recommendation"] },
    ] },
  ],
};

const json = (res, obj, tokens = [1800, 600]) => {
  res.writeHead(200, { "Content-Type": "application/json" });
  res.end(JSON.stringify({ id: "f1", object: "chat.completion", created: 0, model: "film",
    choices: [{ index: 0, message: { role: "assistant", content: JSON.stringify(obj) }, finish_reason: "stop" }],
    usage: { prompt_tokens: tokens[0], completion_tokens: tokens[1], total_tokens: tokens[0] + tokens[1] } }));
};

http.createServer(async (req, res) => {
  let body = "";
  for await (const c of req) body += c;
  if (!req.url.endsWith("/chat/completions")) { res.writeHead(404).end(); return; }
  const input = JSON.parse(body || "{}");
  const name = input.response_format?.json_schema?.name || "";
  const text = (input.messages || []).map((m) => (typeof m.content === "string" ? m.content : "")).join("\n");
  const who = Object.values(PEOPLE).find((p) => text.includes(p.marker)) || PEOPLE.weak;
  console.log(`film model: ${name || "chat"} ${who === PEOPLE.weak && !text.includes(PEOPLE.weak.marker) ? "" : who.marker}`);
  if (name === "job_criteria") return json(res, { criteria: REQS });
  if (name === "criteria_guides") return json(res, { guides: REQS.map((r) => r.guide) });
  if (name === "cv_profile") return json(res, who.profile);
  if (name === "cv_score") return json(res, {
    criteria: who.score.map(([score, quote, evidence, status, relevant_roles, why_not_higher], i) => ({ criterion: REQS[i].requirement, score, quote, evidence, status, relevant_roles, why_not_higher })),
    summary: who.summary, summary_ar: who.summary_ar,
  }, [4200, 900]);
  if (name === "interview_guide") return json(res, GUIDE, [2600, 1100]);
  if (name === "job_translation") return json(res, { title: "Economic Data Analyst", location: "Dubai", description_html: "<p>Economic data analyst.</p>" });
  if (name) return json(res, {});
  // Anything else (the assistant): a short plain answer.
  json(res, null);
}).listen(9000, () => console.log("film model on :9000"));
