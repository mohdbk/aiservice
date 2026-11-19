import { ArrowClockwise, CaretDown, CaretUp, CheckCircle, FloppyDisk, ListChecks, Plus, Sparkle, Star, Trash } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { api, useAsync, useLang, useToast } from "../core";
import { Badge, Button, Card, ErrorState, Loading, Note, useSaving } from "./ui";

export const CATEGORIES = ["Qualification", "Experience", "Skill", "Certification", "Language", "Other"] as const;
type Row = { criterion: string; must: boolean; category: string; min_years: number | null; level: string; high: boolean; guide: string; weight?: number };
type Data = {
  criteria: (Row & { weight: number })[]; approved: boolean; approved_at: string | null; approved_by: string | null; description_changed: boolean;
  waiting: number; scored: number; in_progress: number; ai_available: boolean;
};

/** Weight from Essential or Desirable and Standard or High importance (the server applies the same rule). */
export const weightOf = (must: boolean, high: boolean) => (must ? (high ? 4 : 3) : high ? 2 : 1);

/**
 * The job's CV screening requirements, owned by HR: short structured items in two lists, Essential (must have) and
 * Desirable (nice to have), each with a type, minimum years for experience, a level for languages, an importance and a
 * score guide. The AI drafts them from the job description; HR edits and approves. CVs are scored only against
 * approved requirements; approving scores the CVs that were waiting.
 */
export function JobCriteria({ jobId }: { jobId: number }) {
  const { t, date } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<Data>(`/jobs/${jobId}/criteria`), [jobId]);
  const [rows, setRows] = useState<Row[]>([]);
  const [dirty, setDirty] = useState(false);
  const [open, setOpen] = useState<number | null>(null);
  const saving = useSaving();
  const suggesting = useSaving();
  const guiding = useSaving();
  useEffect(() => {
    if (!data.data) return;
    // Older criteria (a typed weight, no type): importance High when the weight is above the average of its group.
    const list = data.data.criteria;
    const avg = (must: boolean) => { const g = list.filter((c) => c.must === must); return g.reduce((s, c) => s + Number(c.weight), 0) / Math.max(1, g.length); };
    setRows(list.map((c) => ({
      criterion: c.criterion, must: c.must, category: c.category || "Other", min_years: c.min_years ?? null, level: c.level || "", guide: c.guide || "",
      high: [1, 2, 3, 4].includes(Number(c.weight)) ? !!c.high : Number(c.weight) > avg(c.must),
    })));
    setDirty(false);
  }, [data.data]);
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const d = data.data!;
  const total = rows.reduce((s, r) => s + weightOf(r.must, r.high), 0);
  const share = (r: Row) => Math.round((weightOf(r.must, r.high) / Math.max(1, total)) * 100);
  const essentials = rows.filter((r) => r.must).length;
  const edit = (i: number, patch: Partial<Row>) => { setRows(rows.map((r, k) => (k === i ? { ...r, ...patch } : r))); setDirty(true); };
  const add = (must: boolean) => { setRows([...rows, { criterion: "", must, category: "Skill", min_years: null, level: "", high: false, guide: "" }]); setDirty(true); };
  const body = () => rows.map((r) => ({ ...r, weight: weightOf(r.must, r.high), min_years: r.category === "Experience" ? r.min_years : null, level: r.category === "Language" ? r.level : "" }));

  const save = async (approve: boolean) => {
    const r = await saving.run(() => api<{ queued: number; approved: boolean }>(`/jobs/${jobId}/criteria`, { method: "PUT", body: { approve, criteria: body() } }));
    if (r !== undefined) {
      toast(approve ? (r.queued ? t("crit.approved_queued", { n: r.queued }) : t("crit.approved")) : t("crit.saved_draft"));
      data.reload();
    }
  };
  const suggest = async () => {
    if (rows.length > 0 && !window.confirm(t("crit.suggest_confirm"))) return;
    if (await suggesting.run(() => api(`/jobs/${jobId}/criteria/suggest`, { body: {} })) !== undefined) { toast(t("crit.suggested")); data.reload(); }
  };
  const guides = async () => {
    const r = await guiding.run(() => api<{ guides: string[] }>(`/jobs/${jobId}/criteria/guides`, { body: { approve: false, criteria: body() } }));
    if (r) { setRows(rows.map((x, i) => ({ ...x, guide: x.guide.trim() ? x.guide : r.guides[i] || "" }))); setDirty(true); toast(t("crit.guides_ready")); }
  };
  const again = async () => {
    if (!window.confirm(t("crit.again_confirm"))) return;
    const r = await saving.run(() => api<{ queued: number }>(`/jobs/${jobId}/score-again`, { body: {} }));
    if (r !== undefined) { toast(t("crit.again_queued", { n: r.queued })); data.reload(); }
  };
  const incomplete = rows.some((r) => !r.criterion.trim() || (r.category === "Experience" && r.min_years != null && r.min_years < 0));

  const group = (must: boolean) => {
    const items = rows.map((r, i) => ({ r, i })).filter((x) => x.r.must === must);
    return (
      <section aria-labelledby={`req-${must ? "e" : "d"}`} className="rounded-2xl border border-aeblack-100 p-4">
        <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
          <h3 id={`req-${must ? "e" : "d"}`} className="text-base font-bold">{t(must ? "crit.essential" : "crit.desirable")}</h3>
          <p className="text-xs text-aeblack-500">{t(must ? "crit.essential_hint" : "crit.desirable_hint")}</p>
        </div>
        {items.length === 0 ? <p className="mb-3 text-sm text-aeblack-500">{t(must ? "crit.none_essential" : "crit.none_desirable")}</p> : (
          <ul className="mb-3 space-y-2">
            {items.map(({ r, i }) => (
              <li key={i} className="rounded-xl bg-whitely-100 p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <select value={r.category} onChange={(e) => edit(i, { category: e.target.value })} aria-label={t("crit.type")}
                    className="w-40 rounded-lg border border-aeblack-200 bg-white px-2 py-2 text-sm">
                    {CATEGORIES.map((c) => <option key={c} value={c}>{t(`crit.cat.${c}`)}</option>)}
                  </select>
                  <input value={r.criterion} onChange={(e) => edit(i, { criterion: e.target.value })} dir="auto" maxLength={120} aria-label={t("crit.requirement")}
                    placeholder={t(`crit.ph.${r.category}`)} className="min-w-48 flex-1 rounded-lg border border-aeblack-200 bg-white px-3 py-2 text-sm focus:border-primary-600 focus:outline-none" />
                  {r.category === "Experience" && (
                    <label className="inline-flex items-center gap-1.5 text-sm">{t("crit.min_years")}
                      <input type="number" min={0} max={40} value={r.min_years ?? ""} onChange={(e) => edit(i, { min_years: e.target.value === "" ? null : Number(e.target.value) })} dir="ltr"
                        className="w-16 rounded-lg border border-aeblack-200 bg-white px-2 py-2 tabular-nums" />
                    </label>
                  )}
                  {r.category === "Language" && (
                    <select value={r.level} onChange={(e) => edit(i, { level: e.target.value })} aria-label={t("crit.level")} className="rounded-lg border border-aeblack-200 bg-white px-2 py-2 text-sm">
                      <option value="">{t("crit.level_any")}</option>
                      {["Basic", "Good", "Fluent", "Native"].map((l) => <option key={l} value={l}>{t(`crit.lvl.${l}`)}</option>)}
                    </select>
                  )}
                  <button type="button" aria-pressed={r.high} onClick={() => edit(i, { high: !r.high })} title={t("crit.importance")}
                    className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-xs font-semibold ${r.high ? "border-camel-300 bg-camel-50 text-camel-800" : "border-aeblack-200 text-aeblack-600"}`}>
                    <Star size={14} weight={r.high ? "fill" : "regular"} aria-hidden="true" />{t(r.high ? "crit.high" : "crit.standard")}
                  </button>
                  <span className="w-12 text-end text-xs tabular-nums text-aeblack-500" dir="ltr" title={t("crit.share_hint")}>{share(r)}%</span>
                  <button type="button" onClick={() => setOpen(open === i ? null : i)} aria-expanded={open === i}
                    className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-semibold text-primary-700 hover:bg-primary-50">
                    {t("crit.guide")}{r.guide.trim() ? " ✓" : ""}{open === i ? <CaretUp size={14} aria-hidden="true" /> : <CaretDown size={14} aria-hidden="true" />}
                  </button>
                  <button type="button" onClick={() => edit(i, { must: !r.must })} className="rounded-lg px-2 py-1 text-xs text-aeblack-600 hover:bg-aeblack-50">
                    {t(r.must ? "crit.move_desirable" : "crit.move_essential")}
                  </button>
                  <button type="button" onClick={() => { setRows(rows.filter((_, k) => k !== i)); setDirty(true); }} className="rounded p-2 text-aeblack-500 hover:bg-aered-50 hover:text-aered-700"
                    aria-label={t("crit.remove")}><Trash size={18} aria-hidden="true" /></button>
                </div>
                {open === i && (
                  <div className="mt-2">
                    <label className="mb-1 block text-xs font-semibold text-aeblack-600" htmlFor={`guide-${i}`}>{t("crit.guide_label")}</label>
                    <textarea id={`guide-${i}`} rows={4} value={r.guide} onChange={(e) => edit(i, { guide: e.target.value })} dir="auto" maxLength={2000}
                      placeholder={t("crit.guide_ph")} className="w-full rounded-lg border border-aeblack-200 bg-white px-3 py-2 text-sm" />
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
        <Button variant="outline" size="sm" disabled={rows.length >= 10} onClick={() => add(must)} icon={<Plus size={18} aria-hidden="true" />}>
          {t(must ? "crit.add_essential" : "crit.add_desirable")}
        </Button>
      </section>
    );
  };

  return (
    <Card title={t("crit.title")} actions={d.approved && !dirty
      ? <Badge tone="green"><CheckCircle size={16} className="me-1 inline" aria-hidden="true" />{t("crit.status_approved")}</Badge>
      : <Badge tone="amber">{t("crit.status_draft")}</Badge>}>
      <div className="space-y-4">
        <Note>{t("crit.hint")}</Note>
        {d.approved && !dirty && d.approved_at && <p className="text-sm text-aeblack-600">{t("crit.approved_by", { name: d.approved_by || "-", at: date(d.approved_at, true) })}</p>}
        {!d.approved && d.waiting > 0 && <Note tone="warning">{t("crit.waiting", { n: d.waiting })}</Note>}
        {d.description_changed && <Note tone="warning">{t("crit.description_changed")}</Note>}
        {(saving.error || suggesting.error || guiding.error) && <ErrorState error={saving.error || suggesting.error || guiding.error || undefined} />}
        {rows.length === 0 && <p className="text-sm text-aeblack-500">{t("crit.none")}</p>}
        {group(true)}
        {group(false)}
        {essentials > 4 && <p className="text-xs font-semibold text-camel-800">{t("crit.many_musts")}</p>}

        <div className="flex flex-wrap gap-2 border-t border-aeblack-100 pt-4">
          {d.ai_available && <Button variant="outline" busy={suggesting.busy} onClick={suggest} icon={<Sparkle size={20} aria-hidden="true" />}>{t("crit.suggest")}</Button>}
          {d.ai_available && rows.length > 0 && <Button variant="outline" busy={guiding.busy} disabled={incomplete} onClick={guides} icon={<ListChecks size={20} aria-hidden="true" />}>{t("crit.draft_guides")}</Button>}
          <Button variant="outline" busy={saving.busy} disabled={!dirty || rows.length === 0 || incomplete} onClick={() => save(false)} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("crit.save_draft")}</Button>
          <Button busy={saving.busy} disabled={rows.length === 0 || incomplete || (d.approved && !dirty)} onClick={() => save(true)}
            icon={<CheckCircle size={20} aria-hidden="true" />}>{t("crit.approve")}</Button>
          {d.approved && !dirty && <Button variant="outline" busy={saving.busy} onClick={again} icon={<ArrowClockwise size={20} aria-hidden="true" />}>{t("crit.again")}</Button>}
        </div>
        <p className="text-xs text-aeblack-500">{t("crit.counts", { scored: d.scored, waiting: d.waiting, progress: d.in_progress })}</p>
      </div>
    </Card>
  );
}
