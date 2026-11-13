import { CheckCircle, EnvelopeSimple, Key, Prohibit, ShieldCheck, Trash } from "@phosphor-icons/react";
import { useState } from "react";
import { Link } from "react-router-dom";
import { DataTable, useList } from "../components/DataTable";
import { Badge, Button, ErrorState, Loading, Modal, Note, PageHeader, TextArea, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Row = {
  id: number; full_name: string; email: string; verified: boolean; enabled: boolean; talent_pool: boolean; joined_at: string;
  last_sign_in_at: string | null; language: string | null; locked: boolean; applications: number;
};
type Detail = Omit<Row, "locked" | "applications"> & {
  anonymised: boolean; locked_until: string | null; email_configured: boolean;
  applications: { id: number; code: string; status: string; created_at: string; job_title: string }[];
  data_requests: { id: number; kind: string; status: string; created_at: string }[];
  emails: { subject: string; status: string; created_at: string; sent_at: string | null; error: string }[];
};

const yesNo = [{ value: "true", label: "yes" }, { value: "false", label: "no" }];

/** Candidate accounts on the careers portal: find one, see its applications, and help with access. */
export default function Candidates() {
  const { t, date } = useLang();
  usePageTitle(t("nav.candidates"));
  const list = useList<Row>("/candidates", ["verified", "enabled", "pool"]);
  const [open, setOpen] = useState<number | null>(null);
  const opts = yesNo.map((o) => ({ value: o.value, label: t(`common.${o.label}`) }));
  return (
    <>
      <PageHeader title={t("nav.candidates")} subtitle={list.data ? t("cand.subtitle", { n: list.data.total }) : undefined} />
      <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("cand.search")}
        filters={[
          { key: "verified", label: t("cand.verified"), options: opts },
          { key: "enabled", label: t("cand.enabled"), options: opts },
          { key: "pool", label: t("nav.talent_pool"), options: opts },
        ]}
        columns={[
          { key: "name", label: t("common.name"), sort: "name", render: (r) => (
            <button type="button" className="flex flex-col items-start text-start" onClick={() => setOpen(r.id)}>
              <span className="block font-semibold text-primary-700">{r.full_name}</span>
              <span className="block text-xs text-aeblack-500" dir="ltr">{r.email}</span>
            </button>
          ) },
          { key: "status", label: t("common.status"), render: (r) => (
            <span className="flex flex-wrap gap-1">
              {!r.enabled ? <Badge tone="red">{t("cand.disabled")}</Badge> : r.locked ? <Badge tone="amber">{t("cand.locked")}</Badge> : null}
              <Badge tone={r.verified ? "green" : "amber"}>{r.verified ? t("cand.verified") : t("cand.not_verified")}</Badge>
              {r.talent_pool && <Badge tone="gold">{t("nav.talent_pool")}</Badge>}
            </span>
          ) },
          { key: "apps", label: t("nav.applicants"), render: (r) => <span className="tabular-nums">{r.applications}</span> },
          { key: "joined", label: t("cand.joined"), sort: "joined", className: "whitespace-nowrap", render: (r) => date(r.joined_at) },
          { key: "last", label: t("setup.last_sign_in"), sort: "last_sign_in", className: "whitespace-nowrap", render: (r) => r.last_sign_in_at ? date(r.last_sign_in_at, true) : "-" },
        ]} />
      {open && <CandidateDialog id={open} onClose={() => setOpen(null)} onChanged={list.reload} />}
    </>
  );
}

function CandidateDialog({ id, onClose, onChanged }: { id: number; onClose: () => void; onChanged: () => void }) {
  const { t, date, to } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<Detail>(`/candidates/${id}`), [id]);
  const act = useSaving();
  const [reason, setReason] = useState("");
  const [askDelete, setAskDelete] = useState(false);
  const run = async (path: string, body: unknown, message: string, confirm?: string) => {
    if (confirm && !window.confirm(confirm)) return;
    if (await act.run(() => api(`/candidates/${id}/${path}`, { body })) !== undefined) { toast(message); data.reload(); onChanged(); setAskDelete(false); }
  };
  const d = data.data;
  return (
    <Modal open onClose={onClose} title={d?.full_name || t("nav.candidates")} footer={<Button variant="outline" onClick={onClose}>{t("common.close")}</Button>}>
      {data.loading && !d ? <Loading /> : data.error ? <ErrorState error={data.error} /> : d && (
        <div className="space-y-5">
          {act.error && <ErrorState error={act.error} />}
          <div className="text-sm">
            <p dir="ltr" className="text-start font-semibold">{d.email}</p>
            <p className="text-aeblack-600">{t("cand.joined")} {date(d.joined_at)} · {t("setup.last_sign_in")} {d.last_sign_in_at ? date(d.last_sign_in_at, true) : "-"}</p>
            <div className="mt-2 flex flex-wrap gap-1">
              <Badge tone={d.enabled ? "green" : "red"}>{d.enabled ? t("cand.enabled") : t("cand.disabled")}</Badge>
              <Badge tone={d.verified ? "green" : "amber"}>{d.verified ? t("cand.verified") : t("cand.not_verified")}</Badge>
              {d.locked_until && <Badge tone="amber">{t("cand.locked_until", { at: date(d.locked_until, true) })}</Badge>}
              {d.talent_pool && <Badge tone="gold">{t("nav.talent_pool")}</Badge>}
            </div>
          </div>
          {!d.email_configured && <Note tone="warning">{t("cand.no_email")}</Note>}

          <div>
            <p className="mb-2 text-sm font-semibold">{t("cand.access")}</p>
            <div className="flex flex-wrap gap-2">
              {!d.verified && <Button size="sm" variant="outline" busy={act.busy} disabled={!d.email_configured} icon={<EnvelopeSimple size={20} aria-hidden="true" />}
                onClick={() => run("resend-verification", {}, t("cand.verification_sent"))}>{t("cand.resend")}</Button>}
              {!d.verified && <Button size="sm" variant="outline" busy={act.busy} icon={<CheckCircle size={20} aria-hidden="true" />}
                onClick={() => run("verify", {}, t("cand.verified_done"), t("cand.verify_confirm"))}>{t("cand.mark_verified")}</Button>}
              {d.enabled && <Button size="sm" variant="outline" busy={act.busy} disabled={!d.email_configured} icon={<Key size={20} aria-hidden="true" />}
                onClick={() => run("password-reset", {}, t("cand.reset_sent"))}>{t("cand.reset")}</Button>}
              {d.enabled
                ? <Button size="sm" variant="outline" busy={act.busy} icon={<Prohibit size={20} aria-hidden="true" />}
                    onClick={() => run("enabled", { enabled: false }, t("cand.disabled_done"), t("cand.disable_confirm"))}>{t("cand.disable")}</Button>
                : <Button size="sm" variant="outline" busy={act.busy} icon={<ShieldCheck size={20} aria-hidden="true" />}
                    onClick={() => run("enabled", { enabled: true }, t("cand.enabled_done"))}>{t("cand.enable")}</Button>}
              {d.locked_until && d.enabled && <Button size="sm" variant="outline" busy={act.busy} onClick={() => run("enabled", { enabled: true }, t("cand.unlocked"))}>{t("cand.unlock")}</Button>}
            </div>
          </div>

          <div>
            <p className="mb-2 text-sm font-semibold">{t("nav.applicants")}</p>
            {d.applications.length === 0 ? <p className="text-sm text-aeblack-500">{t("cand.no_applications")}</p> : (
              <ul className="divide-y divide-aeblack-100 text-sm">
                {d.applications.map((a) => (
                  <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                    <Link to={to(`/applicants/${a.id}`)} className="font-semibold text-primary-700 hover:underline">{a.job_title}</Link>
                    <span className="text-xs text-aeblack-500"><span dir="ltr">{a.code}</span> · {date(a.created_at)} · {t(`app.status.${a.status}`)}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {d.emails.length > 0 && (
            <div>
              <p className="mb-2 text-sm font-semibold">{t("cand.emails")}</p>
              <ul className="divide-y divide-aeblack-100 text-sm">
                {d.emails.map((m, i) => (
                  <li key={i} className="flex flex-wrap items-center justify-between gap-2 py-2">
                    <span className="min-w-0 truncate">{m.subject}</span>
                    <span className="flex items-center gap-2 text-xs text-aeblack-500">{date(m.sent_at || m.created_at, true)}
                      <Badge tone={m.status === "Sent" ? "green" : m.status === "Failed" ? "red" : "amber"}>{t(`asmlink.mail.${m.status}`)}</Badge></span>
                    {m.status === "Failed" && m.error && <span className="w-full text-xs text-aered-700" dir="ltr">{m.error}</span>}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="border-t border-aeblack-100 pt-4">
            <p className="mb-1 text-sm font-semibold">{t("cand.delete_title")}</p>
            <p className="mb-2 text-xs text-aeblack-600">{t("cand.delete_hint")}</p>
            {d.data_requests.some((r) => r.kind === "Deletion" && r.status === "Open")
              ? <Note>{t("cand.delete_open")} <Link to={to("/setup/data-protection")} className="font-semibold underline">{t("nav.data_protection")}</Link></Note>
              : !askDelete
                ? <Button size="sm" variant="outline" onClick={() => setAskDelete(true)} icon={<Trash size={20} aria-hidden="true" />}>{t("cand.delete_start")}</Button>
                : (
                  <div className="space-y-2">
                    <TextArea label={t("cand.delete_reason")} rows={2} dir="auto" value={reason} onChange={(e) => setReason(e.target.value)} />
                    <div className="flex gap-2">
                      <Button size="sm" busy={act.busy} onClick={() => run("deletion-request", { reason }, t("cand.delete_requested"))}>{t("cand.delete_confirm")}</Button>
                      <Button size="sm" variant="outline" onClick={() => setAskDelete(false)}>{t("common.cancel")}</Button>
                    </div>
                  </div>
                )}
          </div>
        </div>
      )}
    </Modal>
  );
}
