import { useState } from "react";
import { Link } from "react-router-dom";
import { DataTable, useList } from "../components/DataTable";
import { DocumentDialog } from "../components/DocumentDialog";
import { ActionMenu, Badge, ErrorState, PageHeader, SectionTabs, useSaving } from "../components/ui";
import { api, useLang, usePageTitle, useToast } from "../core";
import { OfferDialog } from "./Applicant";

type Row = {
  id: number; code: string; position_title: string; status: string; offer_date: string; start_date: string | null; monthly_salary: number | null;
  currency: string; applicant_id: number; applicant: string; job_title: string;
};

const tone = (s: string) => (s === "Accepted" ? "green" : s === "Sent" ? "blue" : s === "Declined" || s === "Withdrawn" ? "gray" : "amber");
const NEXT: Record<string, string[]> = { Draft: ["Sent", "Withdrawn"], Sent: ["Accepted", "Declined", "Withdrawn"], Accepted: ["Withdrawn"], Declined: [], Withdrawn: [] };
const LABEL: Record<string, string> = { Sent: "offer.mark_sent", Accepted: "offer.mark_accepted", Declined: "offer.mark_declined", Withdrawn: "offer.withdraw" };

export default function Offers() {
  const { t, to, date } = useLang();
  const toast = useToast();
  usePageTitle(t("offer.title"));
  const list = useList<Row>("/offers", ["status"]);
  const status = list.get("status");
  const reload = list.reload;
  const [editing, setEditing] = useState<any>(null);
  const [letter, setLetter] = useState<number | null>(null);
  const act = useSaving();
  const change = async (id: number, s: string) => {
    const r = await act.run(() => api<{ emailed?: boolean }>(`/offers/${id}/status`, { body: { status: s } }));
    if (r !== undefined) {
      toast(r?.emailed ? t("offer.emailed") : (r as { hand_off_queued?: boolean })?.hand_off_queued ? t("handoff.queued") : t("common.saved"));
      reload();
    }
  };
  const sendAgain = async (id: number) => {
    const r = await act.run(() => api<{ ok: boolean; error: string }>(`/offers/${id}/handoff`, { body: {} }));
    if (r) toast(r.ok ? t("handoff.sent") : t("handoff.failed", { error: r.error }), r.ok ? "success" : "error");
  };
  const open = async (id: number) => {
    const o = await act.run(() => api<any>(`/offers/${id}`));
    if (o) setEditing(o);
  };
  return (
    <>
      <PageHeader title={t("offer.title")} />
      <SectionTabs value={status} onChange={(v) => list.set({ status: v || null })}
        tabs={[{ value: "", label: t("common.all") }, ...["Draft", "Sent", "Accepted", "Declined", "Withdrawn"].map((s) => ({ value: s, label: t(`offer.status.${s}`) }))]} />
      {act.error && <div className="mb-4"><ErrorState error={act.error} /></div>}
      <DataTable list={list} rowKey={(o) => o.id} columns={[
        { key: "code", label: t("common.code"), sort: "code", render: (o) => <span dir="ltr">{o.status === "Draft" ? <button type="button" className="font-semibold text-primary-700" onClick={() => open(o.id)}>{o.code}</button> : o.code}</span> },
        { key: "applicant", label: t("int.applicant"), sort: "applicant", render: (o) => <><Link to={to(`/applicants/${o.applicant_id}`)}>{o.applicant}</Link><p className="text-xs text-aeblack-500">{o.job_title}</p></> },
        { key: "position", label: t("offer.position"), render: (o) => o.position_title },
        { key: "date", label: t("offer.date"), sort: "offer_date", className: "whitespace-nowrap", render: (o) => date(o.offer_date) },
        { key: "salary", label: t("offer.salary"), sort: "salary", render: (o) => <span className="tabular-nums" dir="ltr">{o.monthly_salary != null ? `${o.monthly_salary.toLocaleString()} ${o.currency}` : "-"}</span> },
        { key: "status", label: t("common.status"), sort: "status", render: (o) => <Badge tone={tone(o.status)}>{t(`offer.status.${o.status}`)}</Badge> },
        { key: "act", label: t("common.actions"), render: (o) => <ActionMenu label={t("common.actions")} items={[
          { label: t("offer.view_letter"), onClick: () => setLetter(o.id) },
          ...NEXT[o.status].map((s) => ({ label: t(LABEL[s]), onClick: () => change(o.id, s) })),
          ...(o.status === "Accepted" ? [
            { label: t("handoff.download"), onClick: () => { window.location.href = `/api/backoffice/offers/${o.id}/handoff`; } },
            { label: t("handoff.send_again"), onClick: () => sendAgain(o.id) },
          ] : []),
        ]} /> },
      ]} />
      {editing && (
        <OfferDialog open onClose={() => setEditing(null)} busy={act.busy} defaultTitle={editing.position_title}
          initial={{ position_title: editing.position_title, offer_date: editing.offer_date, start_date: editing.start_date || "", monthly_salary: editing.monthly_salary, currency: editing.currency, terms: editing.terms, offer_template_id: editing.offer_template_id }}
          onSave={(b) => act.run(() => api(`/offers/${editing.id}`, { method: "PUT", body: { ...b, application_id: editing.applicant.id } })).then((r) => { if (r !== undefined) { setEditing(null); reload(); toast(t("common.saved")); } })} />
      )}
      {letter != null && <DocumentDialog kind="OfferLetter" recordId={letter} title={t("offer.letter")} chooseTemplate={false} onClose={() => setLetter(null)} />}
    </>
  );
}
