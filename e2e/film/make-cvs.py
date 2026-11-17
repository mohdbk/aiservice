"""Writes the CVs used in the walkthrough film as Word files (Arabic, right to left): one strong, one mid, one weak."""
import sys, zipfile, os
from xml.sax.saxutils import escape

CVS = {
    "salma.docx": [
        "سلمى الحمادي", "محللة بيانات أولى",
        "نبذة: محللة بيانات بخبرة في بناء المؤشرات الاقتصادية ولوحات المعلومات ودعم اتخاذ القرار في الجهات الحكومية.",
        "الخبرة العملية",
        "محللة بيانات أولى، مركز دبي للإحصاء، 2021-01 حتى الآن",
        "بناء لوحات معلومات Power BI للمؤشرات الاقتصادية الشهرية",
        "كتابة استعلامات SQL متقدمة على قواعد بيانات SQL Server",
        "محللة بيانات، دائرة الاقتصاد والسياحة، 2018-03 إلى 2020-12",
        "تحليل بيانات الرخص التجارية باستخدام SQL و Python",
        "التعليم", "بكالوريوس الإحصاء، جامعة الإمارات العربية المتحدة، 2017",
        "اللغات", "العربية (لغة أم)، الإنجليزية (بطلاقة)",
    ],
    "khalid.docx": [
        "خالد المري", "محلل تقارير", "الخبرة العملية",
        "محلل تقارير، شركة اتصالات، 2024-06 حتى الآن",
        "إعداد تقارير المبيعات الشهرية باستخدام Excel و SQL",
        "متابعة مؤشرات أداء فرق المبيعات وإعداد عروض تقديمية للإدارة",
        "منسق خدمة عملاء، شركة اتصالات، 2023-07 إلى 2024-05",
        "الرد على استفسارات العملاء وتسجيل الطلبات في نظام إدارة العملاء",
        "الدورات: مقدمة في تحليل البيانات (عبر الإنترنت)، 2024",
        "التعليم", "بكالوريوس نظم المعلومات، جامعة زايد، 2023",
        "اللغات", "العربية، الإنجليزية (جيد)",
    ],
    "peter.docx": [
        "Peter Novak", "Restaurant Supervisor", "Experience",
        "Restaurant Supervisor, Marina Restaurant, 2016 - present",
        "Managed a team of 12 and daily stock counts in Excel",
        "Handled customer complaints and weekly staff rotas for the restaurant",
        "Waiter, Beach Hotel, 2014 - 2016",
        "Served guests and supported events and banquets in the hotel ballroom",
        "Skills: customer service, team leadership, Microsoft Excel",
        "Education", "High school diploma, 2014",
    ],
}

def docx(path, lines):
    paras = "".join(
        f'<w:p><w:pPr><w:bidi/></w:pPr><w:r><w:rPr><w:rtl/></w:rPr><w:t xml:space="preserve">{escape(l)}</w:t></w:r></w:p>' for l in lines)
    doc = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
           '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>' + paras + '</w:body></w:document>')
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml",
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
            '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
            '<Default Extension="xml" ContentType="application/xml"/>'
            '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
            '</Types>')
        z.writestr("_rels/.rels",
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>'
            '</Relationships>')
        z.writestr("word/document.xml", doc)

out = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(out, exist_ok=True)
for name, lines in CVS.items():
    docx(os.path.join(out, name), lines)
print("CVs written to", out)
