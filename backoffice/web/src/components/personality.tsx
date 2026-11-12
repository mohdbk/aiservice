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
  WorkplaceBigFive: [
    { code: "C", ar: "يقظة الضمير (التخطيط والاعتمادية)", en: "Conscientiousness (Planning & Reliability)" },
    { code: "E", ar: "الانبساط (الطاقة والتفاعل)", en: "Extraversion (Energy & Interaction)" },
    { code: "A", ar: "الوداعة (العمل الجماعي والتعاون)", en: "Agreeableness (Teamwork & Cooperation)" },
    { code: "ES", ar: "الاتزان الانفعالي (إدارة الضغوط)", en: "Emotional Stability (Stress Management)" },
    { code: "O", ar: "الانفتاح (الابتكار والتعلم)", en: "Openness (Innovation & Learning)" },
  ],
  WorkplaceType16: [
    { code: "E", ar: "الانبساط", en: "Extraversion" }, { code: "I", ar: "الانطواء", en: "Introversion" },
    { code: "N", ar: "الحدس", en: "Intuition" }, { code: "S", ar: "الحس", en: "Sensing" },
    { code: "T", ar: "التفكير", en: "Thinking" }, { code: "F", ar: "الشعور", en: "Feeling" },
    { code: "J", ar: "الحكم والتنظيم", en: "Judging" }, { code: "P", ar: "الإدراك والمرونة", en: "Perceiving" },
  ],
};

/**
 * The models HR picks from. Big Five is one model with two scorings (WorkplaceBigFive: sums 5 to 25 with bands; BigFive:
 * the 1 to 10 scale with optional norms). 16PF is scored by the backend but hidden until licensed questions exist.
 */
export const MODEL_KEYS = ["BigFive", "WorkplaceType16"];
export const BIG_FIVE = ["WorkplaceBigFive", "BigFive"];
/** Models scored with reference norms and stens (the workplace questionnaires use sums and fixed bands). */
export const NORMED = ["BigFive", "SixteenPF"];
export const SUM_BANDS = ["VeryLow", "Low", "Moderate", "High", "VeryHigh"];

export type TraitScore = {
  code: string; name_ar: string; name_en: string; items: number; answered: number; raw_mean: number; score: number; percentile: number | null; sten: number; band: string;
  sum: number | null; max_sum: number | null;
};

export type PersonalitySummary = {
  type: string | null;
  pairs: { first: string; second: string; first_mean: number; second_mean: number; letter: string }[] | null;
  report: { name_ar: string; name_en: string; strengths_ar: string[]; strengths_en: string[]; risks_ar: string[]; risks_en: string[] } | null;
  fits: { profile_id: number; name_ar: string; name_en: string; percent: number; level: string; below_target: string[] }[];
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
    sum: x.sum ?? x.Sum ?? null, max_sum: x.max_sum ?? x.MaxSum ?? null,
  }));
}

const BAND_TONE: Record<string, string> = { VeryHigh: "green", High: "green", Moderate: "gray", Average: "gray", Low: "amber", VeryLow: "amber" };

/**
 * A trait profile: one bar per trait. Workplace questionnaires show the sum (for example 22/25) with five bands; the sten
 * models show the sten (1 to 10), the band and, when norms exist, the percentile. Type questionnaires show the letters.
 */
export function TraitProfile({ profile, summary }: { profile: TraitScore[]; summary?: PersonalitySummary | null }) {
  const { t, lang } = useLang();
  if (profile.length === 0) return <p className="text-sm text-aeblack-500">{t("pers.no_profile")}</p>;
  if (summary?.pairs) return <TypeProfile profile={profile} summary={summary} />;
  const sums = profile.some((p) => p.sum != null);
  return (
    <div className="space-y-3">
      <ul className="space-y-3">
        {profile.map((p) => (
          <li key={p.code}>
            <div className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm">
              <span className="font-semibold">{lang === "en" ? p.name_en : p.name_ar} <span className="text-xs text-aeblack-400" dir="ltr">({p.code})</span></span>
              <span className="flex items-center gap-2">
                {p.percentile != null && <span className="text-xs text-aeblack-500">{t("pers.percentile", { n: p.percentile })}</span>}
                <Badge tone={BAND_TONE[p.band] || "gray"}>{t(`pers.band.${p.band}`)}</Badge>
                <span className="tabular-nums font-semibold" dir="ltr">{sums && p.sum != null ? `${p.sum}/${p.max_sum}` : `${p.sten}/10`}</span>
              </span>
            </div>
            <Bar value={sums && p.sum != null && p.max_sum ? p.sum / p.max_sum : p.sten / 10} label={lang === "en" ? p.name_en : p.name_ar} />
            {p.answered < p.items && <p className="mt-1 text-xs text-aeblack-500">{t("pers.partial", { answered: p.answered, items: p.items })}</p>}
          </li>
        ))}
      </ul>
      {summary && <RoleFits summary={summary} />}
      <p className="text-xs text-aeblack-500">{t(sums ? "pers.note_sums" : profile.some((p) => p.percentile != null) ? "pers.note_norms" : "pers.note_scale")}</p>
    </div>
  );
}

function Bar({ value, label }: { value: number; label: string }) {
  const pct = Math.round(Math.max(0, Math.min(1, value)) * 100);
  return (
    <div className="h-2.5 overflow-hidden rounded-full bg-aeblack-100" dir="ltr" role="meter" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct} aria-label={label}>
      <div className="h-full rounded-full bg-primary-500" style={{ width: `${pct}%` }} />
    </div>
  );
}

/** The 16-type result: the letters, each pair as two opposing scores, the workplace notes and the role fits. */
function TypeProfile({ profile, summary }: { profile: TraitScore[]; summary: PersonalitySummary }) {
  const { t, lang } = useLang();
  const en = lang === "en";
  const pole = (code: string) => profile.find((p) => p.code === code);
  const name = (code: string) => { const p = pole(code); return p ? (en ? p.name_en : p.name_ar) : code; };
  const r = summary.report;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <span className="rounded-xl bg-primary-50 px-4 py-2 text-2xl font-bold tracking-widest text-primary-800" dir="ltr">{summary.type}</span>
        {r && <span className="font-semibold">{en ? r.name_en : r.name_ar}</span>}
      </div>
      <ul className="space-y-3">
        {(summary.pairs || []).map((pr) => {
          const a = pole(pr.first), b = pole(pr.second);
          return (
            <li key={pr.first} className="text-sm">
              <div className="mb-1 grid grid-cols-[1fr_auto_1fr] items-center gap-2">
                <span className={pr.letter === pr.first ? "font-bold" : "text-aeblack-500"}>{name(pr.first)} <span dir="ltr">({pr.first})</span></span>
                <span className="tabular-nums text-xs text-aeblack-500" dir="ltr">{a?.sum ?? "-"}/{a?.max_sum ?? "-"} · {b?.sum ?? "-"}/{b?.max_sum ?? "-"}</span>
                <span className={`text-end ${pr.letter === pr.second ? "font-bold" : "text-aeblack-500"}`}>{name(pr.second)} <span dir="ltr">({pr.second})</span></span>
              </div>
              <div className="flex h-2.5 overflow-hidden rounded-full bg-aeblack-100" role="img" aria-label={`${name(pr.first)} ${pr.first_mean} · ${name(pr.second)} ${pr.second_mean}`}>
                <div className={pr.letter === pr.first ? "bg-primary-500" : "bg-primary-200"} style={{ width: `${(pr.first_mean / (pr.first_mean + pr.second_mean || 1)) * 100}%` }} />
                <div className={pr.letter === pr.second ? "bg-primary-500" : "bg-primary-200"} style={{ flex: 1 }} />
              </div>
            </li>
          );
        })}
      </ul>
      {r && (
        <div className="grid gap-4 sm:grid-cols-2">
          <div><p className="mb-1 text-sm font-semibold">{t("pers.strengths")}</p>
            <ul className="list-disc space-y-1 ps-5 text-sm">{(en ? r.strengths_en : r.strengths_ar).map((x) => <li key={x}>{x}</li>)}</ul></div>
          <div><p className="mb-1 text-sm font-semibold">{t("pers.risks")}</p>
            <ul className="list-disc space-y-1 ps-5 text-sm">{(en ? r.risks_en : r.risks_ar).map((x) => <li key={x}>{x}</li>)}</ul></div>
        </div>
      )}
      <RoleFits summary={summary} />
      <p className="text-xs text-aeblack-500">{t("pers.note_type")}</p>
    </div>
  );
}

/** Fit with each of HR's role profiles for this questionnaire. */
export function RoleFits({ summary }: { summary: PersonalitySummary }) {
  const { t, lang } = useLang();
  if (!summary.fits?.length) return null;
  const sorted = [...summary.fits].sort((a, b) => b.percent - a.percent);
  return (
    <div>
      <p className="mb-2 text-sm font-semibold">{t("pers.role_fit")}</p>
      <ul className="divide-y divide-aeblack-100 rounded-xl border border-aeblack-100">
        {sorted.map((f) => (
          <li key={f.profile_id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
            <span>
              <span className="font-semibold">{lang === "en" ? f.name_en : f.name_ar}</span>
              {f.below_target.length > 0 && <span className="ms-2 text-xs text-aeblack-500">{t("pers.below_target", { traits: f.below_target.join(", ") })}</span>}
            </span>
            <span className="flex items-center gap-2">
              {!summary.pairs && <span className="tabular-nums font-semibold" dir="ltr">{f.percent}%</span>}
              <Badge tone={f.level === "Strong" ? "green" : f.level === "Moderate" ? "gold" : "gray"}>{t(`pers.fit.${f.level}`)}</Badge>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
