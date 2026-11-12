import { Check, X } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { DataTable, useList } from "../components/DataTable";
import { Badge, Button, Card, ErrorState, Modal, PageHeader, TextArea, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useAuth, useLang, usePageTitle, useToast } from "../core";

type Row = {
  id: number; kind: "JobPublish" | "OfferSend" | "Requisition"; record_id: number; title: string; note: string; status: string;
  requested_by: string; requested_at: string; decided_by: string; decided_at: string | null; comment: string;
};
type Settings = { require_job_approval: boolean; require_offer_approval: boolean };

const TONE: Record<string, string> = { Pending: "amber", Approved: "green", Rejected: "red", Cancelled: "gray" };

/** The approvals inbox: publishing jobs, sending offers and new requisitions waiting for a recruitment admin. */
export default function Approvals() {
  const { t, date, to } = useLang();
  usePageTitle(t("nav.approvals"));
  const toast = useToast();
  const { me } = useAuth();
  const admin = !!me?.can_administer;
  const list = useList<Row>("/approvals", ["status"], {});
  const canDecide = (list.data as unknown as { can_decide?: boolean })?.can_decide ?? admin;
  const [open, setOpen] = useState<{ row: Row; approve: boolean } | null>(null);
  const [comment, setComment] = useState("");
  const saving = useSaving();

  const decide = async () => {
    if (!open) return;
    const r = await saving.run(() => api<{ job_id: number | null }>(`/approvals/${open.row.id}/decide`, { body: { approve: open.approve, comment } }));
    if (r !== undefined) {
      toast(open.approve ? t("approvals.approved") : t("approvals.rejected"));
      setOpen(null);
      setComment("");
      list.reload();
    }
  };
  const link = (r: Row) => r.kind === "JobPublish" ? to(`/jobs/${r.record_id}`) : r.kind === "OfferSend" ? to("/offers") : to("/requisitions");

  return (
    <>
      <PageHeader title={t("nav.approvals")} subtitle={t("approvals.subtitle")} />
      {admin && <ApprovalSettings />}
      <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("common.search")}
        filters={[{ key: "status", label: t("common.status"), options: ["Pending", "Approved", "Rejected", "Cancelled"].map((s) => ({ value: s, label: t(`approvals.status.${s}`) })) }]}
        empty={t("approvals.empty")}
        columns={[
          { key: "kind", label: t("approvals.kind"), render: (r) => <Badge tone="gold">{t(`approvals.kind.${r.kind}`)}</Badge> },
          { key: "title", label: t("approvals.item"), sort: "title", render: (r) => (
            <div className="max-w-md">
              <Link to={link(r)} className="font-semibold text-primary-700 hover:underline">{r.title}</Link>
              {r.note && <p className="mt-1 line-clamp-2 text-xs text-aeblack-600" dir="auto">{r.note}</p>}
            </div>
          ) },
          { key: "by", label: t("approvals.requested_by"), sort: "requested", render: (r) => <><span className="block">{r.requested_by}</span><span className="text-xs text-aeblack-500">{date(r.requested_at)}</span></> },
          { key: "status", label: t("common.status"), render: (r) => (
            <div>
              <Badge tone={TONE[r.status]}>{t(`approvals.status.${r.status}`)}</Badge>
              {r.decided_by && <span className="mt-1 block text-xs text-aeblack-500">{r.decided_by}</span>}
              {r.comment && <span className="block text-xs text-aeblack-600" dir="auto">{r.comment}</span>}
            </div>
          ) },
          { key: "act", label: t("common.actions"), render: (r) => r.status === "Pending" && canDecide ? (
            <div className="flex gap-2">
              <Button size="sm" onClick={() => setOpen({ row: r, approve: true })} icon={<Check size={20} aria-hidden="true" />}>{t("approvals.approve")}</Button>
              <Button size="sm" variant="outline" onClick={() => setOpen({ row: r, approve: false })} icon={<X size={20} aria-hidden="true" />}>{t("approvals.reject")}</Button>
            </div>
          ) : null },
        ]} />
      {open && (
        <Modal open onClose={() => setOpen(null)} title={open.approve ? t("approvals.approve_title") : t("approvals.reject_title")} footer={<>
          <Button variant="outline" onClick={() => setOpen(null)}>{t("common.cancel")}</Button>
          <Button busy={saving.busy} disabled={!open.approve && !comment.trim()} onClick={decide}>{open.approve ? t("approvals.approve") : t("approvals.reject")}</Button></>}>
          {saving.error && <ErrorState error={saving.error} />}
          <p className="font-semibold">{open.row.title}</p>
          <p className="text-sm text-aeblack-600">{t(`approvals.effect.${open.row.kind}.${open.approve ? "yes" : "no"}`)}</p>
          <TextArea required={!open.approve} label={t("approvals.comment")} rows={3} dir="auto" value={comment} onChange={(e) => setComment(e.target.value)} />
        </Modal>
      )}
    </>
  );
}

function ApprovalSettings() {
  const { t } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<Settings>("/approval-settings"), []);
  const [s, setS] = useState<Settings | null>(null);
  useEffect(() => { if (data.data) setS(data.data); }, [data.data]);
  const save = async (next: Settings) => {
    setS(next);
    try {
      await api("/approval-settings", { method: "PUT", body: next });
      toast(t("common.saved"));
    } catch {
      toast(t("approvals.save_failed"), "error");
      data.reload();
    }
  };
  if (!s) return null;
  return (
    <div className="mb-6">
      <Card title={t("approvals.settings")}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Toggle label={t("approvals.require_job")} hint={t("approvals.require_job_hint")} checked={s.require_job_approval} onChange={(v) => save({ ...s, require_job_approval: v })} />
          <Toggle label={t("approvals.require_offer")} hint={t("approvals.require_offer_hint")} checked={s.require_offer_approval} onChange={(v) => save({ ...s, require_offer_approval: v })} />
        </div>
      </Card>
    </div>
  );
}
