import { useState } from "react";
import { Link } from "react-router-dom";
import { DataTable, useList } from "../components/DataTable";
import { Printer } from "@phosphor-icons/react";
import { ActionMenu, Badge, Bilingual, Button, ErrorState, LangSwitch, Loading, Modal, PageHeader, SectionTabs, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
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
      toast(r?.emailed ? t("offer.emailed") : t("common.saved"));
      reload();
    }
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
        ]} /> },
      ]} />
      {editing && (
        <OfferDialog open onClose={() => setEditing(null)} busy={act.busy} defaultTitle={editing.position_title}
          initial={{ position_title: editing.position_title, offer_date: editing.offer_date, start_date: editing.start_date || "", monthly_salary: editing.monthly_salary, currency: editing.currency, terms: editing.terms, offer_template_id: editing.offer_template_id }}
          onSave={(b) => act.run(() => api(`/offers/${editing.id}`, { method: "PUT", body: { ...b, application_id: editing.applicant.id } })).then((r) => { if (r !== undefined) { setEditing(null); reload(); toast(t("common.saved")); } })} />
      )}
      {letter != null && <LetterDialog id={letter} onClose={() => setLetter(null)} />}
    </>
  );
}

/** The offer letter made from its template, in Arabic or English, ready to print or save as PDF. */
function LetterDialog({ id, onClose }: { id: number; onClose: () => void }) {
  const { t, lang } = useLang();
  const [l, setL] = useState<Bilingual | "">("");
  const res = useAsync(() => api<{ template_id: number | null; template: string; lang: string; html: string }>(`/offers/${id}/letter${l ? `?lang=${l}` : ""}`), [id, l]);
  const print = () => {
    const d = res.data;
    if (!d) return;
    const frame = document.createElement("iframe");
    frame.style.position = "fixed"; frame.style.width = "0"; frame.style.height = "0"; frame.style.border = "0";
    document.body.appendChild(frame);
    const doc = frame.contentDocument!;
    doc.open();
    doc.write(`<!doctype html><html lang="${d.lang}" dir="${d.lang === "ar" ? "rtl" : "ltr"}"><head><meta charset="utf-8"><title>${t("offer.letter")}</title>
      <style>@page{margin:24mm} body{margin:0} div{max-width:none!important}</style></head><body>${d.html}</body></html>`);
    doc.close();
    frame.contentWindow!.focus();
    frame.contentWindow!.print();
    setTimeout(() => frame.remove(), 1000);
  };
  const shown = res.data?.lang || l || lang;
  return (
    <Modal open onClose={onClose} title={t("offer.letter")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.close")}</Button>
      <Button onClick={print} disabled={!res.data?.html} icon={<Printer size={24} aria-hidden="true" />}>{t("offer.print")}</Button>
    </>}>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2 text-sm">
        <span className="text-aeblack-500">{res.data?.template}</span>
        <LangSwitch value={shown as Bilingual} onChange={setL} />
      </div>
      {res.loading && !res.data ? <Loading /> : res.error ? <ErrorState error={res.error} /> : !res.data?.html ? <p className="text-sm text-aeblack-500">{t("offer.no_template")}</p>
        : <div className="max-h-[60vh] overflow-auto rounded-xl border border-aeblack-100 bg-white p-5" dangerouslySetInnerHTML={{ __html: res.data.html }} />}
      <p className="mt-2 text-xs text-aeblack-500">{t("offer.print_hint")}</p>
    </Modal>
  );
}
