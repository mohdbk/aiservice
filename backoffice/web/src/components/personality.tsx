import { Badge } from "./ui";
import { useLang } from "../core";

/** Trait codes of the two personality models (names in Arabic and English, as the backend scores them). */
export const MODELS: Record<string, { code: string; ar: string; en: string }[]> = {
  BigFive: [
    { code: "E", ar: "الانبساط", en: "Extraversion" }, { code: "A", ar: "الوداعة والتعاون", en: "Agreeableness" },
    { code: "C", ar: "يقظة الضمير", en: "Conscientiousness" }, { code: "ES", ar: "الاتزان الانفعالي", en: "Emotional Stability" },
    { code: "O", ar: "الانفتاح والخيال", en: "Openness and Intellect" },
  ],
  SixteenPF: [
    { code: "A", ar: "الدفء", en: "Warmth" }, { code: "B", ar: "الاستدلال", en: "Reasoning" }, { code: "C", ar: "الاستقرار الانفعالي", en: "Emotional Stability" },
    { code: "E", ar: "الهيمنة", en: "Dominance" }, { code: "F", ar: "الحيوية", en: "Liveliness" }, { code: "G", ar: "الالتزام بالقواعد", en: "Rule-Consciousness" },
    { code: "H", ar: "الجرأة الاجتماعية", en: "Social Boldness" }, { code: "I", ar: "الحساسية", en: "Sensitivity" }, { code: "L", ar: "اليقظة والحذر", en: "Vigilance" },
    { code: "M", ar: "التجريد", en: "Abstractedness" }, { code: "N", ar: "التحفظ", en: "Privateness" }, { code: "O", ar: "القلق والتوجس", en: "Apprehension" },
    { code: "Q1", ar: "الانفتاح على التغيير", en: "Openness to Change" }, { code: "Q2", ar: "الاعتماد على الذات", en: "Self-Reliance" },
    { code: "Q3", ar: "الدقة والكمال", en: "Perfectionism" }, { code: "Q4", ar: "التوتر", en: "Tension" },
  ],
};

export type TraitScore = {
  code: string; name_ar: string; name_en: string; items: number; answered: number; raw_mean: number; score: number; percentile: number | null; sten: number; band: string;
};

/** Accepts the profile as stored (JSON text, PascalCase) or as returned by the API (snake_case list). */
export function parseProfile(value: unknown): TraitScore[] {
  let v: any = value;
  if (typeof v === "string") {
    try {
      v = JSON.parse(v);
    } catch {
      return [];
    }
  }
  if (!Array.isArray(v)) return [];
  return v.map((x: any) => ({
    code: x.code ?? x.Code, name_ar: x.name_ar ?? x.NameAr, name_en: x.name_en ?? x.NameEn, items: x.items ?? x.Items, answered: x.answered ?? x.Answered,
    raw_mean: x.raw_mean ?? x.RawMean, score: x.score ?? x.Score, percentile: x.percentile ?? x.Percentile ?? null, sten: x.sten ?? x.Sten, band: x.band ?? x.Band,
  }));
}

/** A trait profile: one bar per trait on the sten scale (1 to 10) with the band and, when norms exist, the percentile. */
export function TraitProfile({ profile }: { profile: TraitScore[] }) {
  const { t, lang } = useLang();
  if (profile.length === 0) return <p className="text-sm text-aeblack-500">{t("pers.no_profile")}</p>;
  return (
    <div className="space-y-3">
      <ul className="space-y-3">
        {profile.map((p) => (
          <li key={p.code}>
            <div className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm">
              <span className="font-semibold">{lang === "en" ? p.name_en : p.name_ar} <span className="text-xs text-aeblack-400" dir="ltr">({p.code})</span></span>
              <span className="flex items-center gap-2">
                {p.percentile != null && <span className="text-xs text-aeblack-500">{t("pers.percentile", { n: p.percentile })}</span>}
                <Badge tone={p.band === "High" ? "green" : p.band === "Low" ? "amber" : "gray"}>{t(`pers.band.${p.band}`)}</Badge>
                <span className="tabular-nums font-semibold" dir="ltr">{p.sten}/10</span>
              </span>
            </div>
            <div className="h-2.5 overflow-hidden rounded-full bg-aeblack-100" dir="ltr" role="meter" aria-valuemin={1} aria-valuemax={10} aria-valuenow={p.sten}
              aria-label={lang === "en" ? p.name_en : p.name_ar}>
              <div className="h-full rounded-full bg-primary-500" style={{ width: `${p.sten * 10}%` }} />
            </div>
            {p.answered < p.items && <p className="mt-1 text-xs text-aeblack-500">{t("pers.partial", { answered: p.answered, items: p.items })}</p>}
          </li>
        ))}
      </ul>
      <p className="text-xs text-aeblack-500">{t(profile.some((p) => p.percentile != null) ? "pers.note_norms" : "pers.note_scale")}</p>
    </div>
  );
}
