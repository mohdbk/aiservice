namespace Moet.Recruitment.Domain.Assessments;

/// <summary>Workplace notes for one of the 16 types: likely strengths and points to explore at interview.</summary>
public sealed record TypeReport(string Type, string NameAr, string NameEn, string[] StrengthsAr, string[] StrengthsEn, string[] RisksAr, string[] RisksEn);

/// <summary>
/// Short executive notes per type for the Ministry's 16-type questionnaire. They describe tendencies to explore in
/// interviews, not abilities, and are never a reason on their own to select or reject anyone.
/// </summary>
public static class TypeReports
{
    static TypeReport R(string type, string nameAr, string nameEn, string[] sAr, string[] sEn, string[] rAr, string[] rEn) => new(type, nameAr, nameEn, sAr, sEn, rAr, rEn);

    public static readonly IReadOnlyDictionary<string, TypeReport> All = new[]
    {
        R("ENTJ", "القائد الاستراتيجي", "Strategic Leader",
            ["تفكير استراتيجي", "التركيز على الأهداف", "قدرة قيادية", "حسم في اتخاذ القرار"], ["Strategic thinker", "Goal-oriented", "Leadership capability", "Strong decision maker"],
            ["قد يبدو مباشرًا أكثر من اللازم", "قد ينفد صبره مع الإجراءات البطيئة"], ["May appear overly direct", "Can become impatient with slower processes"]),
        R("ENFJ", "القائد الملهم", "Inspiring Leader",
            ["تحفيز الآخرين", "مهارات تواصل عالية", "بناء فرق متماسكة"], ["Motivates others", "Strong communicator", "Builds cohesive teams"],
            ["قد يتحمل أكثر من طاقته لإرضاء الآخرين", "قد يتجنب القرارات غير الشعبية"], ["May take on too much to please others", "May avoid unpopular decisions"]),
        R("ESTJ", "المنظم التنفيذي", "Executive Organiser",
            ["تنظيم العمل والموارد", "الالتزام بالمواعيد", "وضوح في التوقعات"], ["Organises work and resources", "Meets deadlines", "Sets clear expectations"],
            ["قد يقاوم التغيير غير المجرب", "قد يبدو صارمًا في تطبيق القواعد"], ["May resist untested change", "Can seem rigid about rules"]),
        R("INTJ", "المخطط الاستراتيجي", "Strategic Planner",
            ["رؤية بعيدة المدى", "تحليل الأنظمة المعقدة", "استقلالية في العمل"], ["Long-term vision", "Analyses complex systems", "Works independently"],
            ["قد يقلل من أهمية مشاركة الآخرين", "قد يبدو متحفظًا في التواصل"], ["May underplay involving others", "Can seem reserved in communication"]),
        R("ISTJ", "المنفذ الدقيق", "Dependable Executor",
            ["دقة واعتمادية", "اتباع الإجراءات", "إنجاز المهام في وقتها"], ["Accurate and dependable", "Follows procedures", "Delivers on time"],
            ["قد يتردد في الأساليب الجديدة", "قد يحتاج وقتًا للتكيف مع التغيير المفاجئ"], ["May hesitate with new methods", "May need time to adapt to sudden change"]),
        R("ESFJ", "المنسق المتعاون", "Supportive Coordinator",
            ["الاهتمام بالآخرين", "تنسيق العمل الجماعي", "الحفاظ على بيئة عمل إيجابية"], ["Cares for others", "Coordinates teamwork", "Keeps a positive workplace"],
            ["قد يتأثر بالنقد", "قد يصعب عليه قول لا"], ["May take criticism to heart", "May find it hard to say no"]),
        R("ENFP", "المبتكر المتحمس", "Enthusiastic Innovator",
            ["توليد الأفكار", "الحماس والتأثير", "التكيف مع المواقف الجديدة"], ["Generates ideas", "Enthusiastic and persuasive", "Adapts to new situations"],
            ["قد يبدأ أكثر مما ينهي", "قد يمل من المهام الروتينية"], ["May start more than finishes", "May tire of routine tasks"]),
        R("ESFP", "المتفاعل العملي", "Practical Energiser",
            ["التعامل مع الناس بسهولة", "الاستجابة السريعة للمواقف", "إضفاء الحيوية على الفريق"], ["At ease with people", "Responds quickly to situations", "Energises the team"],
            ["قد يقل اهتمامه بالتخطيط طويل المدى", "قد يتجنب التفاصيل النظرية"], ["May give less attention to long-term planning", "May avoid theoretical detail"]),
        R("INTP", "المحلل المفكر", "Analytical Thinker",
            ["تحليل منطقي عميق", "حل المشكلات المعقدة", "الفضول المعرفي"], ["Deep logical analysis", "Solves complex problems", "Intellectually curious"],
            ["قد يتأخر في الحسم", "قد يقل اهتمامه بالجوانب الإجرائية"], ["May delay closing decisions", "May give less attention to procedures"]),
        R("ISTP", "المحلل العملي", "Practical Analyst",
            ["حل المشكلات العملية", "الهدوء في الأزمات", "الكفاءة في استخدام الأدوات والبيانات"], ["Solves practical problems", "Calm in a crisis", "Efficient with tools and data"],
            ["قد يفضل العمل منفردًا", "قد لا يشارك خططه مبكرًا"], ["May prefer working alone", "May not share plans early"]),
        R("ENTP", "المبتكر المجادل", "Inventive Challenger",
            ["ابتكار الحلول", "مناقشة الأفكار وتطويرها", "رؤية الفرص"], ["Invents solutions", "Debates and develops ideas", "Spots opportunities"],
            ["قد يتحدى القواعد دون حاجة", "قد يحتاج دعمًا في المتابعة والتنفيذ"], ["May challenge rules unnecessarily", "May need support with follow-through"]),
        R("ISFJ", "الداعم الموثوق", "Reliable Supporter",
            ["الاعتمادية والإخلاص", "الانتباه لاحتياجات الآخرين", "الدقة في التفاصيل"], ["Reliable and loyal", "Attentive to others' needs", "Careful with details"],
            ["قد لا يبرز إنجازاته", "قد يتحفظ على التغيير السريع"], ["May not highlight own achievements", "May be wary of rapid change"]),
        R("INFJ", "المستشار صاحب الرؤية", "Insightful Adviser",
            ["فهم عميق للناس", "رؤية واضحة للقيم والأهداف", "التخطيط المدروس"], ["Deep understanding of people", "Clear sense of values and purpose", "Thoughtful planning"],
            ["قد يتحمل ضغطًا داخليًا دون أن يظهر", "قد يحتاج وقتًا قبل مشاركة آرائه"], ["May carry pressure without showing it", "May need time before sharing views"]),
        R("INFP", "المثالي المبدع", "Creative Idealist",
            ["الإبداع والأصالة", "التعاطف", "الالتزام بالقيم"], ["Creative and original", "Empathetic", "Committed to values"],
            ["قد يتأثر بالنقد الحاد", "قد يحتاج إلى أهداف واضحة ومواعيد محددة"], ["May be affected by harsh criticism", "May need clear goals and deadlines"]),
        R("ISFP", "الحرفي المرن", "Adaptable Craftsperson",
            ["المرونة والهدوء", "الاهتمام بالجودة", "التعاون دون صخب"], ["Flexible and calm", "Attentive to quality", "Collaborates quietly"],
            ["قد يتجنب المواجهة", "قد لا يحب التخطيط المسبق الطويل"], ["May avoid confrontation", "May dislike long advance planning"]),
        R("ESTP", "المبادر العملي", "Action-Oriented Doer",
            ["المبادرة والجرأة", "التفاوض", "سرعة التصرف"], ["Takes initiative", "Negotiates well", "Acts quickly"],
            ["قد يتخذ قرارات سريعة دون تحليل كافٍ", "قد يمل من الإجراءات الطويلة"], ["May decide fast without enough analysis", "May tire of lengthy procedures"]),
    }.ToDictionary(r => r.Type);
}
