"""Render every portal page with sample data (Arabic and English) for a design preview.
Run from frappe/apps:  python3 moet_careers/tests/render_preview.py <out_dir>
Frappe does not autoescape, so neither does this; the templates turn autoescape on themselves."""
import jinja2, types, importlib.util, pathlib, sys
spec = importlib.util.spec_from_file_location("pu", "moet_careers/moet_careers/portal_utils.py"); pu = importlib.util.module_from_spec(spec); spec.loader.exec_module(pu)
# Frappe resolves "<app>/templates/..." from the app root (apps/<app>)
env = jinja2.Environment(loader=jinja2.FileSystemLoader(["moet_careers"]), undefined=jinja2.StrictUndefined, autoescape=False)
D = lambda **k: types.SimpleNamespace(**k)
settings = D(name_en="Ministry of Economy & Tourism", name_ar="وزارة الاقتصاد والسياحة", logo="", logo_dark="", main_site_url="https://www.moet.gov.ae",
             privacy_url="https://x/p", terms_url="https://x/t", accessibility_url="https://x/a", contact_url="https://x/c", allow_signup=1, max_cv_mb=5)
jobs = [D(name=f"HR-OPN-2026-000{i}", title=t, department_label=d, employment_type="Full-time", location="Dubai",
          summary=s, closes_label="30/11/2026", salary="AED 30,000 - 45,000", description_html="<p>Role</p><ul><li>Python</li></ul>", accepting=True)
        for i, (t, d, s) in enumerate([("AI Engineer: Agentic Systems", "Digital Solutions", "Build and operate agentic AI services for digital government, from LLM workflows to on-premises model serving."),
                                       ("Data Analyst: Economic Indicators", "Economic Analysis", "Analyse economic and licensing data and build dashboards that inform policy decisions.")], 1)]
out_dir = pathlib.Path(sys.argv[1]); count = 0
for lang in ("ar", "en"):
    if lang == "ar":
        jobs_l = [D(**{**j.__dict__, "title": a, "summary": s, "department_label": d}) for j, (a, s, d) in zip(jobs, [
            ("مهندس ذكاء اصطناعي: الأنظمة الذكية", "بناء وتشغيل خدمات الذكاء الاصطناعي للحكومة الرقمية، من سير عمل النماذج اللغوية إلى تشغيل النماذج داخليا.", "الحلول الرقمية"),
            ("محلل بيانات: المؤشرات الاقتصادية", "تحليل البيانات الاقتصادية وبيانات التراخيص وبناء لوحات متابعة تدعم القرارات.", "التحليل الاقتصادي")])]
    else:
        jobs_l = jobs
    base = dict(lang=lang, dir="rtl" if lang=="ar" else "ltr", t=lambda en, ar, l=lang: pu.text(l, en, ar), settings=settings,
                entity_name=pu.text(lang, settings.name_en, settings.name_ar), portal_title=pu.text(lang,"Careers","الوظائف"),
                page_title="X", user="u@x.com", is_guest=False, user_name="Layla Haddad", csrf_token="tok", asset_version="0.1.0",
                current_path="/careers", other_lang="en" if lang=="ar" else "ar", other_lang_url="?lang=x", year="2026")
    pages = {
      "index": dict(q="", department="", location="", jobs=jobs_l, total=2, page=1, pages=1, options={"department":["Digital Solutions - MOET"],"location":["Dubai"]},
                    clean_department=pu.clean_department, page_url=lambda p: f"/careers?page={p}"),
      "job": dict(job=jobs_l[0], applied=None),
      "apply": dict(job=jobs_l[0], email="layla@example.com", countries=["United Arab Emirates","Egypt","India"]),
      "applications": dict(applications=[D(name="a", title=jobs_l[0].title, submitted="08/11/2026", reference=pu.reference("a","2026"), status=pu.candidate_status("Open")),
                                         D(name="b", title=jobs_l[1].title, submitted="01/11/2026", reference=pu.reference("b","2026"), status=pu.candidate_status("Open", True))]),
      "application": dict(name="a", title=jobs_l[0].title, submitted="08/11/2026", reference="MOET-2026-ABC123", status=pu.candidate_status("Open"), just_applied=True),
      "login": dict(redirect_to="/careers"), "signup": dict(redirect_to="/careers"),
    }
    for name, ctx in pages.items():
        html = env.get_template(f"moet_careers/www/careers/{name}.html").render(**base, **ctx)
        assert "—" not in html, (name, "em dash")
        (out_dir / f"{lang}_{name}.html").write_text(html); count += 1
print("rendered", count, "pages OK")
