import { Plus, X } from "@phosphor-icons/react";
import { useState } from "react";
import { Link } from "react-router-dom";
import { DataTable, useList } from "../components/DataTable";
import { Badge, Button, ErrorState, FlagLabel, Modal, Note, PageHeader, SelectInput, TextArea, TextInput, useSaving } from "../components/ui";
import { api, useLang, usePageTitle, useToast } from "../core";
import { useLookups } from "./JobEdit";

type Row = {
  id: number; code: string; title: string; title_ar: string; title_en: string; department: string | null; department_id: number | null; location_id: number | null;
  employment_type: string; grade: string; headcount: number; budget_monthly: number | null; justification: string; needed_by: string | null;
  status: string; job_id: number | null; requested_by: string; created_at: string;
};
type Draft = {
  title: string; title_en: string; department_id: string; location_id: string; employment_type: string; grade: string; headcount: string; budget_monthly: string;
  justification: string; needed_by: string;
};

const EMPTY: Draft = { title: "", title_en: "", department_id: "", location_id: "", employment_type: "FullTime", grade: "", headcount: "1", budget_monthly: "", justification: "", needed_by: "" };
const TONE: Record<string, string> = { Pending: "amber", Approved: "green", Rejected: "red", Filled: "green", Cancelled: "gray" };
const TYPES = ["FullTime", "PartTime", "Contract", "Temporary", "Internship"];

/** Requests for new posts. A recruitment admin approves them in the approvals inbox, and each approved one becomes a draft job. */
export default function Requisitions() {
  const { t, lang, date, to } = useLang();
  usePageTitle(t("nav.requisitions"));
  const toast = useToast();
  const list = useList<Row>("/requisitions", ["status"]);
  const lookups = useLookups();
  const [d, setD] = useState<(Draft & { id?: number }) | null>(null);
  const saving = useSaving();
  const set = (k: keyof Draft, v: string) => d && setD({ ...d, [k]: v });

  const save = async () => {
    if (!d) return;
    const body = {
      title: d.title, title_en: d.title_en, department_id: d.department_id ? Number(d.department_id) : null, location_id: d.location_id ? Number(d.location_id) : null,
      employment_type: d.employment_type, grade: d.grade, headcount: Number(d.headcount) || 0, budget_monthly: d.budget_monthly ? Number(d.budget_monthly) : null,
      justification: d.justification, needed_by: d.needed_by || null,
    };
    const r = await saving.run(() => api(d.id ? `/requisitions/${d.id}` : "/requisitions", { method: d.id ? "PUT" : "POST", body }));
    if (r !== undefined) {
      toast(d.id ? t("common.saved") : t("approvals.sent_for_approval"));
      setD(null);
      list.reload();
    }
  };
  const cancel = async (r: Row) => {
    if (!window.confirm(t("req.cancel_confirm"))) return;
    if (await saving.run(() => api(`/requisitions/${r.id}/cancel`, { body: {} })) !== undefined) { toast(t("common.saved")); list.reload(); }
  };
  const open = (r: Row) => setD({
    id: r.id, title: r.title_ar, title_en: r.title_en, department_id: r.department_id ? String(r.department_id) : "", location_id: r.location_id ? String(r.location_id) : "",
    employment_type: r.employment_type, grade: r.grade, headcount: String(r.headcount), budget_monthly: r.budget_monthly == null ? "" : String(r.budget_monthly),
    justification: r.justification, needed_by: r.needed_by || "",
  });
  const lk = lookups.data;

  return (
    <>
      <PageHeader title={t("nav.requisitions")} subtitle={t("req.subtitle")}
        actions={<Button onClick={() => setD({ ...EMPTY })} icon={<Plus size={20} aria-hidden="true" />}>{t("req.new")}</Button>} />
      <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("common.search")} empty={t("req.empty")}
        filters={[{ key: "status", label: t("common.status"), options: ["Pending", "Approved", "Rejected", "Filled", "Cancelled"].map((s) => ({ value: s, label: t(`req.status.${s}`) })) }]}
        columns={[
          { key: "title", label: t("req.post"), sort: "title", render: (r) => (
            <div className="max-w-md">
              {r.status === "Pending"
                ? <button type="button" onClick={() => open(r)} className="text-start font-semibold text-primary-700 hover:underline">{r.title}</button>
                : <span className="font-semibold">{r.title}</span>}
              <span className="block text-xs text-aeblack-500"><span dir="ltr">{r.code}</span>{r.department ? ` · ${r.department}` : ""}{r.grade ? ` · ${r.grade}` : ""}</span>
            </div>
          ) },
          { key: "headcount", label: t("req.headcount"), render: (r) => <span className="tabular-nums">{r.headcount}</span> },
          { key: "needed", label: t("req.needed_by"), sort: "needed", className: "whitespace-nowrap", render: (r) => r.needed_by ? date(r.needed_by) : "-" },
          { key: "by", label: t("approvals.requested_by"), sort: "created", render: (r) => <><span className="block">{r.requested_by}</span><span className="text-xs text-aeblack-500">{date(r.created_at)}</span></> },
          { key: "status", label: t("common.status"), render: (r) => (
            <div>
              <Badge tone={TONE[r.status]}>{t(`req.status.${r.status}`)}</Badge>
              {r.job_id && <Link to={to(`/jobs/${r.job_id}`)} className="mt-1 block text-xs font-semibold text-primary-700 hover:underline">{t("req.open_job")}</Link>}
            </div>
          ) },
          { key: "act", label: t("common.actions"), render: (r) => r.status === "Pending" ? (
            <Button size="sm" variant="outline" onClick={() => cancel(r)} icon={<X size={20} aria-hidden="true" />}>{t("req.cancel")}</Button>
          ) : null },
        ]} />
      {d && (
        <Modal open onClose={() => setD(null)} title={d.id ? t("req.edit") : t("req.new")} footer={<>
          <Button variant="outline" onClick={() => setD(null)}>{t("common.cancel")}</Button>
          <Button busy={saving.busy} onClick={save}>{d.id ? t("common.save") : t("req.submit")}</Button></>}>
          {saving.error && <ErrorState error={saving.error} />}
          {!d.id && <Note>{t("req.hint")}</Note>}
          <TextInput required label={<FlagLabel lang="ar">{t("req.title_ar")}</FlagLabel>} dir="rtl" value={d.title} onChange={(e) => set("title", e.target.value)} />
          <TextInput label={<FlagLabel lang="en">{t("req.title_en")}</FlagLabel>} dir="ltr" value={d.title_en} onChange={(e) => set("title_en", e.target.value)} />
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectInput label={t("jobs.department")} value={d.department_id} onChange={(e) => set("department_id", e.target.value)}
              options={[{ value: "", label: "" }, ...(lk?.departments || []).map((x) => ({ value: String(x.id), label: lang === "en" && x.name_en ? x.name_en : x.name }))]} />
            <SelectInput label={t("jobs.location")} value={d.location_id} onChange={(e) => set("location_id", e.target.value)}
              options={[{ value: "", label: "" }, ...(lk?.locations || []).map((x) => ({ value: String(x.id), label: lang === "en" ? x.name_en || x.name : x.name_ar || x.name }))]} />
            <SelectInput required label={t("jobs.employment_type")} value={d.employment_type} onChange={(e) => set("employment_type", e.target.value)}
              options={TYPES.map((x) => ({ value: x, label: t(`emp.${x}`) }))} />
            <TextInput label={t("req.grade")} value={d.grade} onChange={(e) => set("grade", e.target.value)} />
            <TextInput required label={t("req.headcount")} type="number" min={1} max={500} value={d.headcount} onChange={(e) => set("headcount", e.target.value)} />
            <TextInput label={t("req.budget")} type="number" min={0} value={d.budget_monthly} onChange={(e) => set("budget_monthly", e.target.value)} />
            <TextInput label={t("req.needed_by")} type="date" value={d.needed_by} onChange={(e) => set("needed_by", e.target.value)} />
          </div>
          <TextArea required label={t("req.justification")} hint={t("req.justification_hint")} rows={4} dir="auto" value={d.justification} onChange={(e) => set("justification", e.target.value)} />
        </Modal>
      )}
    </>
  );
}
