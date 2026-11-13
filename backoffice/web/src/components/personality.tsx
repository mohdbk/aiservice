import { useState } from "react";
import { Badge, Note } from "./ui";
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
export type JobProfile = { id: number; name_ar: string; name_en: string } | null | undefined;

export function TraitProfile({ profile, summary, jobProfile }: { profile: TraitScore[]; summary?: PersonalitySummary | null; jobProfile?: JobProfile }) {
  const { t, lang } = useLang();
  if (profile.length === 0) return <p className="text-sm text-aeblack-500">{t("pers.no_profile")}</p>;
  if (summary?.pairs) return <TypeProfile profile={profile} summary={summary} jobProfile={jobProfile} />;
  const sums = profile.some((p) => p.sum != null);
  const value = (p: TraitScore) => (sums && p.sum != null && p.max_sum ? p.sum / p.max_sum : p.sten / 10);
  return (
    <div className="space-y-3">
      {profile.length >= 3 && (
        <Radar points={profile.map((p) => ({ label: (lang === "en" ? p.name_en : p.name_ar).split(" (")[0], value: value(p),
          text: sums && p.sum != null ? `${p.sum}/${p.max_sum}` : `${p.sten}/10` }))} />
      )}
      <ul className="space-y-3">
        {profile.map((p) => (
          <li key={p.code}>
            <div className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm">
              <span className="font-semibold">{lang === "en" ? p.name_en : p.name_ar} <span className="text-xs text-aeblack-500" dir="ltr">({p.code})</span></span>
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
      {summary && <RoleFits summary={summary} jobProfile={jobProfile} />}
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
function TypeProfile({ profile, summary, jobProfile }: { profile: TraitScore[]; summary: PersonalitySummary; jobProfile?: JobProfile }) {
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
      <Preferences pairs={summary.pairs || []} name={name} pole={pole} />
      {r && (
        <div className="grid gap-4 sm:grid-cols-2">
          <div><p className="mb-1 text-sm font-semibold">{t("pers.strengths")}</p>
            <ul className="list-disc space-y-1 ps-5 text-sm">{(en ? r.strengths_en : r.strengths_ar).map((x) => <li key={x}>{x}</li>)}</ul></div>
          <div><p className="mb-1 text-sm font-semibold">{t("pers.risks")}</p>
            <ul className="list-disc space-y-1 ps-5 text-sm">{(en ? r.risks_en : r.risks_ar).map((x) => <li key={x}>{x}</li>)}</ul></div>
        </div>
      )}
      <RoleFits summary={summary} jobProfile={jobProfile} />
      <p className="text-xs text-aeblack-500">{t("pers.note_type")}</p>
    </div>
  );
}

/**
 * Fit with HR's role profiles for this questionnaire. The job's own role profile (or its sibling with the same name for the
 * other questionnaire) comes first and stands out, since it is the one in the combined score; the other job families follow.
 */
export function RoleFits({ summary, jobProfile }: { summary: PersonalitySummary; jobProfile?: JobProfile }) {
  const { t, lang } = useLang();
  const [others, setOthers] = useState(false);
  if (!summary.fits?.length) return null;
  const isJob = (f: { profile_id: number; name_en: string }) => !!jobProfile && (f.profile_id === jobProfile.id || f.name_en === jobProfile.name_en);
  const sorted = [...summary.fits].sort((a, b) => b.percent - a.percent);
  const mine = sorted.find(isJob);
  const rest = sorted.filter((f) => !isJob(f));
  const shown = !jobProfile ? sorted : others ? rest : [];
  return (
    <div className="space-y-3">
      {jobProfile && (
        mine ? (
          <div className="rounded-xl border-2 border-primary-300 bg-primary-50 p-3">
            <p className="text-xs font-semibold text-primary-800">{t("pers.job_fit")}</p>
            <div className="mt-1 flex flex-wrap items-center justify-between gap-2">
              <span className="font-semibold">{lang === "en" ? mine.name_en : mine.name_ar}</span>
              <span className="flex items-center gap-2">
                {!summary.pairs && <span className="tabular-nums text-lg font-bold" dir="ltr">{mine.percent}%</span>}
                <Badge tone={mine.level === "Strong" ? "green" : mine.level === "Moderate" ? "gold" : "gray"}>{t(`pers.fit.${mine.level}`)}</Badge>
              </span>
            </div>
            {mine.below_target.length > 0 && <p className="mt-1 text-xs text-aeblack-600">{t("pers.below_target", { traits: mine.below_target.join(", ") })}</p>}
            <p className="mt-2 text-xs text-aeblack-600">{t("pers.job_fit_hint")}</p>
          </div>
        ) : <Note>{t("pers.job_fit_missing", { name: lang === "en" ? jobProfile.name_en : jobProfile.name_ar })}</Note>
      )}
      {!jobProfile && <Note>{t("pers.no_job_profile")}</Note>}
      {jobProfile && rest.length > 0 && (
        <button type="button" onClick={() => setOthers((o) => !o)} className="text-sm font-semibold text-primary-700 hover:underline" aria-expanded={others}>
          {others ? t("pers.hide_others") : t("pers.show_others", { n: rest.length })}
        </button>
      )}
      {shown.length > 0 && (
      <div>
      {!jobProfile && <p className="mb-2 text-sm font-semibold">{t("pers.role_fit")}</p>}
      <ul className="divide-y divide-aeblack-100 rounded-xl border border-aeblack-100">
        {shown.map((f) => (
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
      )}
    </div>
  );
}

const CLARITY = [[0.1, "Slight"], [0.3, "Moderate"], [0.6, "Clear"], [1.01, "VeryClear"]] as const;

/**
 * The 16-type result as preference clarity: one row per pair (E or I, S or N, T or F, J or P). A bar grows from the middle
 * towards the preferred side, as long as the preference is clear (a short bar means the person sits close to the middle and
 * may use both). The letters of the type are the sides the bars point to.
 */
function Preferences({ pairs, name, pole }: {
  pairs: NonNullable<PersonalitySummary["pairs"]>; name: (c: string) => string; pole: (c: string) => TraitScore | undefined;
}) {
  const { t } = useLang();
  return (
    <div className="rounded-xl border border-aeblack-100 p-4" dir="ltr">
      <p className="mb-3 text-sm font-semibold" dir="auto">{t("pers.clarity_title")}</p>
      <ul className="space-y-4">
        {pairs.map((pr) => {
          const total = pr.first_mean + pr.second_mean || 1;
          const share = pr.first_mean / total;                 // 0..1 towards the first pole
          const clarity = Math.min(1, Math.abs(share - 0.5) * 2); // 0 = in the middle, 1 = fully one side
          const toFirst = pr.letter === pr.first;
          const level = CLARITY.find(([max]) => clarity < max)?.[1] ?? "VeryClear";
          const a = pole(pr.first), b = pole(pr.second);
          return (
            <li key={pr.first}>
              <div className="mb-1 grid grid-cols-[1fr_auto_1fr] items-center gap-2 text-sm">
                <span className={toFirst ? "font-bold text-primary-800" : "text-aeblack-500"} dir="auto">
                  <span className="me-1 inline-flex h-6 w-6 items-center justify-center rounded bg-aeblack-50 text-xs font-bold" dir="ltr">{pr.first}</span>{name(pr.first)}
                </span>
                <span className="text-xs text-aeblack-500" dir="auto">{t(`pers.clarity.${level}`)}</span>
                <span className={`text-end ${!toFirst ? "font-bold text-primary-800" : "text-aeblack-500"}`} dir="auto">
                  {name(pr.second)}<span className="ms-1 inline-flex h-6 w-6 items-center justify-center rounded bg-aeblack-50 text-xs font-bold" dir="ltr">{pr.second}</span>
                </span>
              </div>
              <div className="relative h-3 rounded-full bg-aeblack-50" role="img"
                aria-label={`${name(pr.letter)} (${pr.letter}), ${t(`pers.clarity.${level}`)}: ${name(pr.first)} ${a?.sum ?? pr.first_mean}, ${name(pr.second)} ${b?.sum ?? pr.second_mean}`}>
                <span className="absolute inset-y-[-3px] left-1/2 w-0.5 -translate-x-1/2 bg-aeblack-300" aria-hidden="true" />
                <span className="absolute inset-y-0 rounded-full bg-primary-500"
                  style={toFirst ? { right: "50%", width: `${Math.max(2, clarity * 50)}%` } : { left: "50%", width: `${Math.max(2, clarity * 50)}%` }} />
              </div>
              <div className="mt-1 flex justify-between text-[11px] tabular-nums text-aeblack-500">
                <span>{a?.sum ?? "-"}/{a?.max_sum ?? "-"}</span><span>{b?.sum ?? "-"}/{b?.max_sum ?? "-"}</span>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/**
 * A radar (spider) chart of the Big Five: one axis per trait from the centre (lowest) to the edge (highest), rings at each
 * fifth, the person's profile as one shape. The values and bands are also listed below it, so nothing depends on the shape alone.
 */
function Radar({ points }: { points: { label: string; value: number; text: string }[] }) {
  const { t } = useLang();
  const size = 320, c = size / 2, r = 108;
  const n = points.length;
  const at = (i: number, v: number) => {
    const a = -Math.PI / 2 + (i * 2 * Math.PI) / n;
    return [c + Math.cos(a) * r * v, c + Math.sin(a) * r * v] as const;
  };
  const ring = (v: number) => points.map((_, i) => at(i, v).join(",")).join(" ");
  const shape = points.map((p, i) => at(i, Math.max(0.02, Math.min(1, p.value))).join(",")).join(" ");
  return (
    <figure className="mx-auto w-full max-w-sm">
      <svg viewBox={`0 0 ${size} ${size}`} className="h-auto w-full" role="img"
        aria-label={`${t("pers.radar_label")}: ${points.map((p) => `${p.label} ${p.text}`).join(", ")}`}>
        {[0.2, 0.4, 0.6, 0.8, 1].map((v) => (
          <polygon key={v} points={ring(v)} fill={v === 1 ? "var(--color-whitely-50, #fafaf7)" : "none"} stroke="var(--color-aeblack-100, #e5e5e5)" strokeWidth={1} />
        ))}
        {points.map((_, i) => {
          const [x, y] = at(i, 1);
          return <line key={i} x1={c} y1={c} x2={x} y2={y} stroke="var(--color-aeblack-100, #e5e5e5)" strokeWidth={1} />;
        })}
        <polygon points={shape} fill="var(--color-primary-500, #b68a35)" fillOpacity={0.22} stroke="var(--color-primary-600, #92722a)" strokeWidth={2} strokeLinejoin="round" />
        {points.map((p, i) => {
          const [x, y] = at(i, Math.max(0.02, Math.min(1, p.value)));
          return <circle key={i} cx={x} cy={y} r={4} fill="var(--color-primary-600, #92722a)" stroke="#fff" strokeWidth={2} />;
        })}
        {points.map((p, i) => {
          const [x, y] = at(i, 1.2);
          const anchor = Math.abs(x - c) < 8 ? "middle" : x > c ? "start" : "end";
          return (
            <text key={i} x={x} y={y} textAnchor={anchor} dominantBaseline="middle" className="fill-aeblack-800" style={{ fontSize: 11, fontWeight: 600 }}>
              <tspan x={x} dy="-0.4em">{p.label}</tspan>
              <tspan x={x} dy="1.2em" className="fill-aeblack-500" style={{ fontWeight: 400 }}>{p.text}</tspan>
            </text>
          );
        })}
      </svg>
      <figcaption className="text-center text-xs text-aeblack-500">{t("pers.radar_caption")}</figcaption>
    </figure>
  );
}
