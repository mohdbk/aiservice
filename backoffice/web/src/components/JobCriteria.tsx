import { ArrowClockwise, CheckCircle, FloppyDisk, Plus, Sparkle, Trash } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { api, useAsync, useLang, useToast } from "../core";
import { Badge, Button, Card, ErrorState, Loading, Note, useSaving } from "./ui";

type Row = { criterion: string; weight: number; must: boolean };
type Data = {
  criteria: Row[]; approved: boolean; approved_at: string | null; approved_by: string | null; description_changed: boolean;
  waiting: number; scored: number; in_progress: number; ai_available: boolean;
};

/**
 * The job's CV screening criteria, owned by HR: suggested by the AI from the description, then edited (wording, weight,
 * must-have) and approved. CVs are scored only against approved criteria; approving scores the CVs that were waiting.
 */
export function JobCriteria({ jobId }: { jobId: number }) {
  const { t, date } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<Data>(`/jobs/${jobId}/criteria`), [jobId]);
  const [rows, setRows] = useState<Row[]>([]);
  const [dirty, setDirty] = useState(false);
  const saving = useSaving();
  const suggesting = useSaving();
  useEffect(() => { if (data.data) { setRows(data.data.criteria.map((c) => ({ ...c, weight: Number(c.weight) }))); setDirty(false); } }, [data.data]);
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const d = data.data!;
  const total = rows.reduce((s, r) => s + (Number(r.weight) || 0), 0);
  const musts = rows.filter((r) => r.must).length;
  const edit = (i: number, patch: Partial<Row>) => { setRows(rows.map((r, k) => (k === i ? { ...r, ...patch } : r))); setDirty(true); };

  const save = async (approve: boolean) => {
    const r = await saving.run(() => api<{ queued: number; approved: boolean }>(`/jobs/${jobId}/criteria`, { method: "PUT", body: { approve, criteria: rows } }));
    if (r !== undefined) {
      toast(approve ? (r.queued ? t("crit.approved_queued", { n: r.queued }) : t("crit.approved")) : t("crit.saved_draft"));
      data.reload();
    }
  };
  const suggest = async () => {
    if (rows.length > 0 && !window.confirm(t("crit.suggest_confirm"))) return;
    if (await suggesting.run(() => api(`/jobs/${jobId}/criteria/suggest`, { body: {} })) !== undefined) { toast(t("crit.suggested")); data.reload(); }
  };
  const again = async () => {
    if (!window.confirm(t("crit.again_confirm"))) return;
    const r = await saving.run(() => api<{ queued: number }>(`/jobs/${jobId}/score-again`, { body: {} }));
    if (r !== undefined) { toast(t("crit.again_queued", { n: r.queued })); data.reload(); }
  };

  return (
    <Card title={t("jobs.tab.criteria")} actions={d.approved && !dirty
      ? <Badge tone="green"><CheckCircle size={16} className="me-1 inline" aria-hidden="true" />{t("crit.status_approved")}</Badge>
      : <Badge tone="amber">{t("crit.status_draft")}</Badge>}>
      <div className="space-y-4">
        <Note>{t("crit.hint")}</Note>
        {d.approved && !dirty && d.approved_at && <p className="text-sm text-aeblack-600">{t("crit.approved_by", { name: d.approved_by || "-", at: date(d.approved_at, true) })}</p>}
        {!d.approved && d.waiting > 0 && <Note tone="warning">{t("crit.waiting", { n: d.waiting })}</Note>}
        {d.description_changed && <Note tone="warning">{t("crit.description_changed")}</Note>}
        {(saving.error || suggesting.error) && <ErrorState error={saving.error || suggesting.error || undefined} />}

        {rows.length === 0 ? <p className="text-sm text-aeblack-500">{t("crit.none")}</p> : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[36rem] text-sm">
              <thead><tr className="border-b border-aeblack-100 text-aeblack-500">
                <th className="py-2 text-start font-semibold">{t("crit.criterion")}</th>
                <th className="w-28 py-2 text-start font-semibold">{t("jobs.weight")}</th>
                <th className="w-28 py-2 text-start font-semibold">{t("jobs.must")}</th>
                <th className="w-12" />
              </tr></thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={i} className="border-b border-aeblack-50 align-top">
                    <td className="py-2 pe-3">
                      <input value={r.criterion} onChange={(e) => edit(i, { criterion: e.target.value })} dir="auto" maxLength={300} aria-label={t("crit.criterion")}
                        className="w-full rounded-lg border border-aeblack-200 px-3 py-2 focus:border-primary-600 focus:outline-none" />
                    </td>
                    <td className="py-2 pe-3">
                      <input type="number" min={1} max={100} value={r.weight} onChange={(e) => edit(i, { weight: Number(e.target.value) || 0 })} dir="ltr" aria-label={t("jobs.weight")}
                        className="w-24 rounded-lg border border-aeblack-200 px-3 py-2 tabular-nums focus:border-primary-600 focus:outline-none" />
                    </td>
                    <td className="py-3"><input type="checkbox" checked={r.must} onChange={(e) => edit(i, { must: e.target.checked })} aria-label={t("jobs.must")} /></td>
                    <td className="py-2">
                      <button type="button" onClick={() => { setRows(rows.filter((_, k) => k !== i)); setDirty(true); }} className="rounded p-2 text-aeblack-500 hover:bg-aered-50 hover:text-aered-700"
                        aria-label={t("crit.remove")}><Trash size={20} aria-hidden="true" /></button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div className="flex flex-wrap items-center justify-between gap-3 text-sm">
          <Button variant="outline" size="sm" disabled={rows.length >= 10} onClick={() => { setRows([...rows, { criterion: "", weight: 10, must: false }]); setDirty(true); }}
            icon={<Plus size={20} aria-hidden="true" />}>{t("crit.add")}</Button>
          <span className={total === 100 ? "text-aeblack-500" : "font-semibold text-camel-800"}>{t("crit.total", { total })}{musts > 3 && ` · ${t("crit.many_musts")}`}</span>
        </div>
        {total !== 100 && rows.length > 0 && <p className="text-xs text-aeblack-500">{t("crit.total_hint")}</p>}

        <div className="flex flex-wrap gap-2 border-t border-aeblack-100 pt-4">
          {d.ai_available && <Button variant="outline" busy={suggesting.busy} onClick={suggest} icon={<Sparkle size={20} aria-hidden="true" />}>{t("crit.suggest")}</Button>}
          <Button variant="outline" busy={saving.busy} disabled={!dirty || rows.length === 0} onClick={() => save(false)} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("crit.save_draft")}</Button>
          <Button busy={saving.busy} disabled={rows.length === 0 || rows.some((r) => !r.criterion.trim() || r.weight <= 0) || (d.approved && !dirty)} onClick={() => save(true)}
            icon={<CheckCircle size={20} aria-hidden="true" />}>{t("crit.approve")}</Button>
          {d.approved && !dirty && <Button variant="outline" busy={saving.busy} onClick={again} icon={<ArrowClockwise size={20} aria-hidden="true" />}>{t("crit.again")}</Button>}
        </div>
        <p className="text-xs text-aeblack-500">{t("crit.counts", { scored: d.scored, waiting: d.waiting, progress: d.in_progress })}</p>
      </div>
    </Card>
  );
}
