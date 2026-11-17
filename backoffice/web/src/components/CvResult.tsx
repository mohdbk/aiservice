import { CheckCircle, PencilSimple, Question, Quotes, WarningCircle, XCircle } from "@phosphor-icons/react";
import { useState } from "react";
import { api, ApiError, useLang, useToast } from "../core";
import { Badge, Button, ErrorState, Note, Score } from "./ui";

export type CriterionRow = {
  sort_order: number; criterion: string; weight: number; is_must_have: boolean; score: number; evidence: string; category: string; status: string;
  quote: string; quote_found: boolean | null; relevant_years: number | null; why_not_higher: string; override_score: number | null;
  override_reason: string; override_at: string | null; override_by: string | null; effective: number;
};
export type Analysis = {
  status: string; score: number | null; summary: string; summary_ar: string; gaps: string; years: number | null; cv_language: string; read_method: string;
  model: string; scored_at: string | null; error: string; needs_review?: boolean; review_reasons?: string[]; read_quality?: string;
  rank?: { position: number; of: number; top_percent: number } | null; criteria: CriterionRow[];
};

const STATUS: Record<string, { tone: string; icon: typeof CheckCircle }> = {
  Met: { tone: "green", icon: CheckCircle }, NotMet: { tone: "red", icon: XCircle }, Unclear: { tone: "amber", icon: Question },
};

/**
 * The CV result in the order people read it: whether a person should look, essential requirements (Met, Not Met or
 * Unclear with the CV's own words), desirable requirements (0 to 10 with what would score higher), then the overall
 * score and where it ranks in the job. A recruiter can change any requirement's score with a reason.
 */
export function CvResult({ appId, c, onChanged }: { appId: number; c: Analysis; onChanged: () => void }) {
  const { t, lang } = useLang();
  const essentials = c.criteria.filter((r) => r.is_must_have);
  const desirables = c.criteria.filter((r) => !r.is_must_have);
  const met = essentials.filter((r) => r.status === "Met").length;
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-6">
        <div className="text-5xl"><Score value={c.score} /></div>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm">
          {c.rank && <><dt className="text-aeblack-500">{t("cvr.rank")}</dt><dd className="font-semibold">{t("cvr.rank_value", { position: c.rank.position, of: c.rank.of, top: c.rank.top_percent })}</dd></>}
          {essentials.length > 0 && <><dt className="text-aeblack-500">{t("crit.essential")}</dt><dd className="font-semibold">{t("cvr.met_of", { met, of: essentials.length })}</dd></>}
          <dt className="text-aeblack-500">{t("app.years")}</dt><dd className="font-semibold" dir="ltr">{c.years ?? "-"}</dd>
          <dt className="text-aeblack-500">{t("app.read_as")}</dt><dd>{c.read_method}{c.read_quality === "poor" && <Badge tone="amber">{t("cvr.poor_read")}</Badge>}</dd>
        </dl>
      </div>
      {c.needs_review && (c.review_reasons?.length ?? 0) > 0 && (
        <Note tone="warning">
          <strong className="mb-1 block">{t("cvr.review")}</strong>
          <ul className="list-disc space-y-0.5 ps-5">{c.review_reasons!.map((r) => <li key={r} dir="auto">{r}</li>)}</ul>
        </Note>
      )}
      <p className="leading-7">{lang === "ar" && c.summary_ar ? c.summary_ar : c.summary}</p>

      {essentials.length > 0 && (
        <section aria-labelledby="cvr-ess">
          <h3 id="cvr-ess" className="mb-2 text-sm font-bold">{t("crit.essential")}</h3>
          <ul className="space-y-2">{essentials.map((r) => <Item key={r.sort_order} appId={appId} r={r} onChanged={onChanged} />)}</ul>
        </section>
      )}
      {desirables.length > 0 && (
        <section aria-labelledby="cvr-des">
          <h3 id="cvr-des" className="mb-2 text-sm font-bold">{t("crit.desirable")}</h3>
          <ul className="space-y-2">{desirables.map((r) => <Item key={r.sort_order} appId={appId} r={r} onChanged={onChanged} />)}</ul>
        </section>
      )}
      {c.model && <p className="text-xs text-aeblack-500">{t("cvr.model", { model: c.model })}</p>}
    </div>
  );
}

function Item({ appId, r, onChanged }: { appId: number; r: CriterionRow; onChanged: () => void }) {
  const { t, date } = useLang();
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [score, setScore] = useState(r.effective);
  const [reason, setReason] = useState(r.override_reason || "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const s = STATUS[r.status];
  const save = async (clear = false) => {
    setBusy(true); setError(null);
    try {
      await api(`/applicants/${appId}/criteria/${r.sort_order}/override`, { body: clear ? { score: null, reason: "" } : { score, reason } });
      toast(t(clear ? "cvr.override_removed" : "cvr.override_saved"));
      setEditing(false);
      onChanged();
    } catch (e) { setError(e as ApiError); }
    setBusy(false);
  };
  return (
    <li className="rounded-xl border border-aeblack-100 p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-semibold" dir="auto">{r.criterion}</p>
          {r.relevant_years != null && <p className="text-xs text-aeblack-600">{t("cvr.relevant_years", { n: r.relevant_years })}</p>}
        </div>
        <div className="flex items-center gap-2">
          {s && <Badge tone={s.tone}><s.icon size={14} className="me-1 inline" aria-hidden="true" />{t(`cvr.status.${r.status}`)}</Badge>}
          <span className="tabular-nums text-sm font-bold" dir="ltr">
            {r.override_score != null && <span className="me-1 text-xs font-normal text-aeblack-400 line-through">{r.score}</span>}{r.effective}/10
          </span>
          <button type="button" onClick={() => setEditing(!editing)} className="rounded p-1 text-aeblack-500 hover:bg-primary-50 hover:text-primary-700" aria-label={t("cvr.change")} title={t("cvr.change")}>
            <PencilSimple size={16} aria-hidden="true" />
          </button>
        </div>
      </div>
      {r.quote && (
        <blockquote className={`mt-2 flex gap-2 rounded-lg px-3 py-2 text-sm ${r.quote_found === false ? "bg-aered-50 text-aered-800" : "bg-whitely-100 text-aeblack-700"}`} dir="auto">
          <Quotes size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
          <span>{r.quote}{r.quote_found === false && <strong className="ms-2 inline-flex items-center gap-1 text-xs"><WarningCircle size={14} aria-hidden="true" />{t("cvr.quote_missing")}</strong>}</span>
        </blockquote>
      )}
      <p className="mt-2 text-sm text-aeblack-600" dir="auto">{r.evidence}</p>
      {r.why_not_higher && r.effective < 10 && <p className="mt-1 text-xs text-aeblack-500" dir="auto"><strong>{t("cvr.why_not_higher")}:</strong> {r.why_not_higher}</p>}
      {r.override_score != null && (
        <p className="mt-2 text-xs text-camel-800">{t("cvr.overridden", { name: r.override_by || "-", at: r.override_at ? date(r.override_at, true) : "" })}: <span dir="auto">{r.override_reason}</span></p>
      )}
      {editing && (
        <div className="mt-3 space-y-2 rounded-lg bg-primary-50 p-3">
          <label className="flex items-center gap-2 text-sm font-semibold">{t("cvr.your_score")}
            <select value={score} onChange={(e) => setScore(Number(e.target.value))} className="rounded-lg border border-aeblack-200 bg-white px-2 py-1" dir="ltr">
              {Array.from({ length: 11 }, (_, i) => <option key={i} value={i}>{i}</option>)}
            </select>
          </label>
          <label className="block text-sm font-semibold" htmlFor={`why-${r.sort_order}`}>{t("cvr.reason")}</label>
          <textarea id={`why-${r.sort_order}`} rows={2} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} dir="auto"
            className="w-full rounded-lg border border-aeblack-200 bg-white px-3 py-2 text-sm" placeholder={t("cvr.reason_ph")} />
          {error && <ErrorState error={error} />}
          <div className="flex flex-wrap gap-2">
            <Button size="sm" busy={busy} disabled={reason.trim().length < 3} onClick={() => save()}>{t("common.save")}</Button>
            {r.override_score != null && <Button size="sm" variant="outline" busy={busy} onClick={() => save(true)}>{t("cvr.use_ai")}</Button>}
            <Button size="sm" variant="link" onClick={() => setEditing(false)}>{t("common.cancel")}</Button>
          </div>
          <p className="text-xs text-aeblack-500">{t("cvr.override_hint")}</p>
        </div>
      )}
    </li>
  );
}
