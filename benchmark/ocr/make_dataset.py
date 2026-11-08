"""Generate the OCR benchmark set: fictional CVs as HTML plus their exact ground-truth text.

5 CV contents (English x2, Arabic x2, mixed Arabic/English) x 4 layouts:
- ats:      single column, plain headings (ATS-friendly)
- designed: two columns with a coloured sidebar, a header band and a skills table
- scanned:  the ATS page degraded afterwards (tilt, noise, blur, JPEG) by degrade.py
- photo:    the designed page as a phone photo (perspective, uneven light, low resolution, JPEG)
All people, organisations and contacts are fictional.
Run: python make_dataset.py && node render.js && python degrade.py
"""
import html
import json
import pathlib

OUT = pathlib.Path(__file__).parent / "data"

CVS = {
    "en_engineer": {
        "dir": "ltr", "lang": "en",
        "name": "Daniel Brooks", "title": "Senior Software Engineer",
        "contact": ["daniel.brooks@example.com", "+971 50 555 0142", "Dubai, United Arab Emirates"],
        "summary": "Software engineer with 8 years of experience building backend services and data platforms. "
                   "Led a team of five engineers and delivered a citizen services API used by 1.2 million people.",
        "experience": [
            ("Lead Backend Engineer", "Harbor Digital Services", "2021 to Present",
             ["Designed Python and FastAPI microservices handling 3 million requests per day",
              "Moved 40 services to Kubernetes and cut hosting cost by 35 percent",
              "Introduced automated testing with 85 percent coverage"]),
            ("Software Engineer", "Blue Falcon Systems", "2017 to 2021",
             ["Built payment integrations with three national banks",
              "Maintained PostgreSQL databases with 2 TB of data"]),
        ],
        "education": [("BSc Computer Engineering", "Gulf Technical University", "2017")],
        "skills": [("Python", "Expert"), ("FastAPI", "Advanced"), ("Kubernetes", "Advanced"),
                   ("PostgreSQL", "Advanced"), ("AWS", "Intermediate")],
        "languages": ["English: Fluent", "Arabic: Intermediate"],
        "certs": ["AWS Certified Solutions Architect Associate"],
        "labels": {"summary": "Profile", "experience": "Experience", "education": "Education",
                   "skills": "Skills", "languages": "Languages", "certs": "Certifications", "contact": "Contact"},
    },
    "en_analyst": {
        "dir": "ltr", "lang": "en",
        "name": "Priya Menon", "title": "Financial Data Analyst",
        "contact": ["priya.menon@example.com", "+971 52 555 0187", "Abu Dhabi, UAE"],
        "summary": "Data analyst with 5 years of experience in banking and public finance. "
                   "Builds dashboards and forecasting models for senior management.",
        "experience": [
            ("Senior Data Analyst", "Emirates Ledger Bank", "2022 to Present",
             ["Built 25 Power BI dashboards for the executive committee",
              "Forecast monthly deposits with Prophet and ARIMA models",
              "Automated weekly reports with Python and SQL"]),
            ("Data Analyst", "Crescent Advisory Group", "2019 to 2022",
             ["Analysed budget data for 12 government clients", "Cleaned and merged datasets with pandas"]),
        ],
        "education": [("MSc Data Analytics", "Northern Coast University", "2019"),
                      ("BCom Finance", "City Commerce College", "2017")],
        "skills": [("SQL", "Expert"), ("Power BI", "Expert"), ("Python", "Advanced"),
                   ("Excel", "Expert"), ("Statistics", "Advanced")],
        "languages": ["English: Native", "Hindi: Native", "Arabic: Basic"],
        "certs": ["Microsoft Certified Power BI Data Analyst"],
        "labels": {"summary": "Summary", "experience": "Work Experience", "education": "Education",
                   "skills": "Skills", "languages": "Languages", "certs": "Certificates", "contact": "Contact"},
    },
    "ar_network": {
        "dir": "rtl", "lang": "ar",
        "name": "خالد سعيد المنصوري", "title": "مهندس شبكات أول",
        "contact": ["khalid.mansouri@example.com", "+971 55 555 0193", "الشارقة، الإمارات العربية المتحدة"],
        "summary": "مهندس شبكات بخبرة تسع سنوات في تصميم وتشغيل الشبكات الحكومية. "
                   "قاد مشروع ربط اثنين وعشرين مبنى بشبكة ألياف ضوئية موحدة.",
        "experience": [
            ("مهندس شبكات أول", "هيئة الخدمات الرقمية", "2020 حتى الآن",
             ["تصميم شبكة مركز البيانات الجديد بسعة عشرة جيجابت",
              "خفض أعطال الشبكة بنسبة أربعين بالمئة خلال عامين",
              "الإشراف على فريق من ستة فنيين"]),
            ("مهندس شبكات", "شركة الواحة للاتصالات", "2016 إلى 2020",
             ["تركيب وصيانة أجهزة سيسكو وجونيبر", "إعداد جدران الحماية والشبكات الافتراضية الخاصة"]),
        ],
        "education": [("بكالوريوس هندسة الاتصالات", "جامعة الخليج التقنية", "2016")],
        "skills": [("تصميم الشبكات", "خبير"), ("أمن المعلومات", "متقدم"), ("Cisco", "خبير"),
                   ("الحوسبة السحابية", "متوسط"), ("إدارة المشاريع", "متقدم")],
        "languages": ["العربية: اللغة الأم", "الإنجليزية: ممتاز"],
        "certs": ["شهادة سيسكو المعتمدة CCNP"],
        "labels": {"summary": "نبذة مهنية", "experience": "الخبرات العملية", "education": "المؤهلات العلمية",
                   "skills": "المهارات", "languages": "اللغات", "certs": "الشهادات", "contact": "معلومات التواصل"},
    },
    "ar_hr": {
        "dir": "rtl", "lang": "ar",
        "name": "نورة عبدالله الكعبي", "title": "أخصائية موارد بشرية",
        "contact": ["noura.kaabi@example.com", "+971 56 555 0128", "أبوظبي، الإمارات العربية المتحدة"],
        "summary": "أخصائية موارد بشرية بخبرة ست سنوات في التوظيف وتطوير الموظفين في القطاع الحكومي. "
                   "ساهمت في تقليص مدة التوظيف من ستين يوما إلى خمسة وثلاثين يوما.",
        "experience": [
            ("أخصائية موارد بشرية", "دائرة التنمية الاقتصادية المحلية", "2021 حتى الآن",
             ["إدارة دورة التوظيف الكاملة لأكثر من مئة وعشرين وظيفة سنويا",
              "تصميم برنامج تهيئة للموظفين الجدد",
              "إعداد تقارير شهرية عن مؤشرات التوطين"]),
            ("منسقة توظيف", "مجموعة النخيل القابضة", "2018 إلى 2021",
             ["تنظيم معارض التوظيف في الجامعات", "فرز السير الذاتية وجدولة المقابلات"]),
        ],
        "education": [("بكالوريوس إدارة الموارد البشرية", "كلية الإمارات للإدارة", "2018")],
        "skills": [("التوظيف والاستقطاب", "خبير"), ("علاقات الموظفين", "متقدم"), ("التدريب والتطوير", "متقدم"),
                   ("أنظمة الموارد البشرية", "متوسط"), ("التواصل الفعال", "خبير")],
        "languages": ["العربية: اللغة الأم", "الإنجليزية: جيد جدا"],
        "certs": ["شهادة أخصائي موارد بشرية معتمد"],
        "labels": {"summary": "الملخص", "experience": "الخبرة المهنية", "education": "التعليم",
                   "skills": "المهارات", "languages": "اللغات", "certs": "الشهادات المهنية", "contact": "التواصل"},
    },
    "mix_datasci": {
        "dir": "rtl", "lang": "mixed",
        "name": "سارة محمد العلي", "title": "عالمة بيانات Data Scientist",
        "contact": ["sara.alali@example.com", "+971 50 555 0176", "دبي، الإمارات"],
        "summary": "عالمة بيانات بخبرة سبع سنوات في بناء نماذج التعلم الآلي باستخدام Python و TensorFlow. "
                   "طورت نموذج تنبؤ بالطلب حقق دقة 92 بالمئة.",
        "experience": [
            ("عالمة بيانات أولى", "مركز البيانات الوطني", "2021 حتى الآن",
             ["بناء نماذج تصنيف باستخدام XGBoost و scikit-learn",
              "تطوير لوحات متابعة في Power BI للإدارة العليا",
              "نشر النماذج على Azure Machine Learning"]),
            ("محللة بيانات", "شركة رؤية للاستشارات", "2017 إلى 2021",
             ["تحليل بيانات العملاء باستخدام SQL و pandas", "إعداد تقارير ربع سنوية للعملاء"]),
        ],
        "education": [("ماجستير علوم البيانات", "جامعة الخليج التقنية", "2017")],
        "skills": [("Python", "خبير"), ("TensorFlow", "متقدم"), ("SQL", "خبير"),
                   ("Power BI", "متقدم"), ("التعلم الآلي", "خبير")],
        "languages": ["العربية: اللغة الأم", "English: Fluent"],
        "certs": ["Microsoft Certified Azure Data Scientist Associate"],
        "labels": {"summary": "نبذة", "experience": "الخبرات", "education": "التعليم",
                   "skills": "المهارات التقنية", "languages": "اللغات", "certs": "الشهادات", "contact": "التواصل"},
    },
}

FONTS = {"en": "'DejaVu Sans', sans-serif", "ar": "'DejaVu Sans', 'FreeSerif', sans-serif",
         "mixed": "'DejaVu Sans', 'FreeSerif', sans-serif"}
# Designed pages use a different face (Naskh-style FreeSerif for Arabic) for font variety.
FONTS_DESIGNED = {"en": "'FreeSerif', serif", "ar": "'FreeSerif', 'DejaVu Sans', serif",
                  "mixed": "'FreeSerif', 'DejaVu Sans', serif"}
e = html.escape


def blocks(cv):
    """Ground truth as named blocks of lines."""
    L = cv["labels"]
    head = [cv["name"], cv["title"]]
    contact = [L["contact"], *cv["contact"]]
    summary = [L["summary"], cv["summary"]]
    exp = [L["experience"]]
    for role, org, dates, bullets in cv["experience"]:
        exp += [role, org, dates, *bullets]
    edu = [L["education"]] + [x for d, inst, yr in cv["education"] for x in (d, inst, yr)]
    skills = [L["skills"]] + [x for s, lvl in cv["skills"] for x in (s, lvl)]
    langs = [L["languages"], *cv["languages"]]
    certs = [L["certs"], *cv["certs"]]
    return {"head": head, "contact": contact, "summary": summary, "experience": exp, "education": edu,
            "skills": skills, "languages": langs, "certs": certs}


def ats_html(cv):
    L, b = cv["labels"], blocks(cv)
    exp = "".join(
        f"<h3>{e(r)}</h3><div class='org'>{e(o)}</div><div class='dates'>{e(d)}</div>"
        f"<ul>{''.join(f'<li>{e(x)}</li>' for x in bl)}</ul>" for r, o, d, bl in cv["experience"])
    edu = "".join(f"<p><b>{e(d)}</b><br>{e(i)}<br>{e(y)}</p>" for d, i, y in cv["education"])
    skills = "".join(f"<li>{e(s)}<br>{e(l)}</li>" for s, l in cv["skills"])
    body = f"""
      <h1>{e(cv['name'])}</h1><div class='title'>{e(cv['title'])}</div>
      <h2>{e(L['contact'])}</h2>{''.join(f'<div>{e(c)}</div>' for c in cv['contact'])}
      <h2>{e(L['summary'])}</h2><p>{e(cv['summary'])}</p>
      <h2>{e(L['experience'])}</h2>{exp}
      <h2>{e(L['education'])}</h2>{edu}
      <h2>{e(L['skills'])}</h2><ul class='skills'>{skills}</ul>
      <h2>{e(L['languages'])}</h2>{''.join(f'<div>{e(x)}</div>' for x in cv['languages'])}
      <h2>{e(L['certs'])}</h2>{''.join(f'<div>{e(x)}</div>' for x in cv['certs'])}"""
    css = """body{margin:0;padding:48px 56px;font-size:15px;line-height:1.45;color:#111;background:#fff}
      h1{font-size:28px;margin:0}.title{font-size:18px;margin-bottom:6px}
      h2{font-size:17px;border-bottom:1px solid #444;margin:16px 0 6px;padding-bottom:2px}
      h3{font-size:15px;margin:8px 0 0}.org,.dates{font-size:14px}ul{margin:4px 0;padding-inline-start:22px}
      p{margin:4px 0}.skills{columns:1}"""
    order = [["head", "contact", "summary", "experience", "education", "skills", "languages", "certs"]]
    return css, body, order, b


def designed_html(cv):
    L, b = cv["labels"], blocks(cv)
    exp = "".join(
        f"<div class='job'><div class='role'>{e(r)}</div><div class='org'>{e(o)}</div><div class='dates'>{e(d)}</div>"
        f"<ul>{''.join(f'<li>{e(x)}</li>' for x in bl)}</ul></div>" for r, o, d, bl in cv["experience"])
    edu = "".join(f"<div class='job'><div class='role'>{e(d)}</div><div class='org'>{e(i)}</div>"
                  f"<div class='dates'>{e(y)}</div></div>" for d, i, y in cv["education"])
    table = "<table>" + "".join(f"<tr><td>{e(s)}</td><td>{e(l)}</td></tr>" for s, l in cv["skills"]) + "</table>"
    side = f"""
      <h2>{e(L['contact'])}</h2>{''.join(f'<div class="c">{e(c)}</div>' for c in cv['contact'])}
      <h2>{e(L['skills'])}</h2>{table}
      <h2>{e(L['languages'])}</h2>{''.join(f'<div class="c">{e(x)}</div>' for x in cv['languages'])}
      <h2>{e(L['certs'])}</h2>{''.join(f'<div class="c">{e(x)}</div>' for x in cv['certs'])}"""
    main = f"""
      <h2>{e(L['summary'])}</h2><p>{e(cv['summary'])}</p>
      <h2>{e(L['experience'])}</h2>{exp}
      <h2>{e(L['education'])}</h2>{edu}"""
    body = f"""<header><h1>{e(cv['name'])}</h1><div class='title'>{e(cv['title'])}</div></header>
      <div class='cols'><aside>{side}</aside><main>{main}</main></div>"""
    css = """body{margin:0;font-size:14px;line-height:1.45;color:#1d2433;background:#fff}
      header{background:#1f3a5f;color:#fff;padding:34px 48px}h1{margin:0;font-size:30px}.title{font-size:18px;opacity:.9}
      .cols{display:flex;min-height:1000px}aside{width:34%;background:#e8eef6;padding:20px 22px}
      main{width:66%;padding:20px 34px}h2{font-size:16px;color:#1f3a5f;margin:14px 0 6px;text-transform:none}
      .c{margin:2px 0;word-break:break-all}table{border-collapse:collapse;width:100%;font-size:13px}
      td{border:1px solid #9fb3cc;padding:3px 5px}.job{margin-bottom:8px}.role{font-weight:bold}
      .org{color:#3a4a63}.dates{font-size:12px;color:#5a6a83}ul{margin:3px 0;padding-inline-start:18px}p{margin:4px 0}"""
    # Reading order is ambiguous for two columns: accept sidebar-first or main-first.
    order = [["head", "contact", "skills", "languages", "certs", "summary", "experience", "education"],
             ["head", "summary", "experience", "education", "contact", "skills", "languages", "certs"]]
    return css, body, order, b


def page(cv, css, body, fonts=FONTS):
    return f"""<!doctype html><html lang="{'ar' if cv['dir'] == 'rtl' else 'en'}" dir="{cv['dir']}"><head>
<meta charset="utf-8"><style>@page{{size:A4;margin:0}}html,body{{width:794px;box-sizing:border-box}}
body{{font-family:{fonts[cv['lang']]}}}{css}</style></head><body>{body}</body></html>"""


def main():
    OUT.mkdir(exist_ok=True)
    truth = {}
    for key, cv in CVS.items():
        for layout, fn in (("ats", ats_html), ("designed", designed_html)):
            css, body, orders, b = fn(cv)
            stem = f"{key}__{layout}"
            fonts = FONTS_DESIGNED if layout == "designed" else FONTS
            (OUT / f"{stem}.html").write_text(page(cv, css, body, fonts), encoding="utf-8")
            texts = ["\n".join(line for blk in order for line in b[blk]) for order in orders]
            truth[stem] = {"cv": key, "lang": cv["lang"], "layout": layout, "image": f"{stem}.png", "truth": texts}
        truth[f"{key}__scanned"] = {**truth[f"{key}__ats"], "layout": "scanned", "image": f"{key}__scanned.jpg"}
        truth[f"{key}__photo"] = {**truth[f"{key}__designed"], "layout": "photo", "image": f"{key}__photo.jpg"}
    (OUT / "truth.json").write_text(json.dumps(truth, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{len(truth)} pages")


if __name__ == "__main__":
    main()
