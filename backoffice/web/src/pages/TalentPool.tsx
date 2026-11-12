import { PaperPlaneTilt } from "@phosphor-icons/react";
import { useState } from "react";
import { DataTable, useList } from "../components/DataTable";
import { Badge, Button, ErrorState, Modal, PageHeader, Score, SelectInput, TextInput, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Row = {
  id: number; full_name: string; email: string; joined_at: string | null; applications: number;
  latest: { id: number; created_at: string; status: string; job_title: string; score: number | null; years: number | null; summary: string } | null;
};

/** Candidates who agreed to be considered for other jobs, searchable by what is in their CVs, and invited to apply. */
export default function TalentPool() {
  const { t, date } = useLang();
  usePageTitle(t("nav.talent_pool"));
  const toast = useToast();
  const list = useList<Row>("/talent-pool", ["cv", "job"]);
  const jobs = useAsync(() => api<{ items: { id: number; title: string }[] }>("/jobs?size=100&status=Open&published=true"), []);
  const [invite, setInvite] = useState<Row | null>(null);
  const [jobId, setJobId] = useState(list.get("job"));
  const [cv, setCv] = useState(list.get("cv"));
  const { busy, error, run } = useSaving();
  const send = async () => {
    if (!invite || !jobId) return;
    const r = await run(() => api<{ emailed: boolean }>(`/talent-pool/${invite.id}/invite`, { body: { job_id: Number(jobId) } }));
    if (r) { toast(r.emailed ? t("pool.invited") : t("pool.not_emailed"), r.emailed ? "success" : "info"); setInvite(null); }
  };
  return (
    <>
      <PageHeader title={t("nav.talent_pool")} subtitle={list.data ? t("pool.subtitle", { n: list.data.total }) : undefined} />
      <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("pool.search")}
        filters={[{ key: "job", label: t("pool.not_applied_to"), options: (jobs.data?.items || []).map((j) => ({ value: String(j.id), label: j.title })) }]}
        toolbar={<form className="flex items-end gap-2" onSubmit={(e) => { e.preventDefault(); list.set({ cv: cv.trim() || null }); }}>
          <TextInput label={t("pool.cv_text")} value={cv} onChange={(e) => setCv(e.target.value)} wrapClass="min-w-48" />
          <Button type="submit" variant="outline" size="sm">{t("common.search")}</Button>
        </form>}
        columns={[
          { key: "name", label: t("common.name"), sort: "name", render: (r) => <><span className="font-semibold">{r.full_name}</span><span className="block text-xs text-aeblack-500" dir="ltr">{r.email}</span></> },
          { key: "latest", label: t("pool.latest"), render: (r) => r.latest ? (
            <div className="max-w-md">
              <p className="text-sm">{r.latest.job_title} <span className="text-aeblack-500">· {date(r.latest.created_at)}</span></p>
              {r.latest.summary && <p className="mt-1 line-clamp-2 text-xs text-aeblack-600" dir="auto">{r.latest.summary}</p>}
            </div>
          ) : "-" },
          { key: "score", label: t("apps.score"), render: (r) => <Score value={r.latest?.score} /> },
          { key: "years", label: t("pool.years"), render: (r) => <span className="tabular-nums">{r.latest?.years ?? "-"}</span> },
          { key: "joined", label: t("pool.joined"), sort: "joined", className: "whitespace-nowrap", render: (r) => r.joined_at ? date(r.joined_at) : <Badge>-</Badge> },
          { key: "act", label: t("common.actions"), render: (r) => (
            <Button variant="outline" size="sm" onClick={() => setInvite(r)} icon={<PaperPlaneTilt size={20} className="rtl:-scale-x-100" aria-hidden="true" />}>{t("pool.invite")}</Button>
          ) },
        ]} />
      {invite && (
        <Modal open onClose={() => setInvite(null)} title={t("pool.invite_title", { name: invite.full_name })} footer={<>
          <Button variant="outline" onClick={() => setInvite(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} disabled={!jobId} onClick={send}>{t("pool.send")}</Button></>}>
          {error && <ErrorState error={error} />}
          <p className="text-sm text-aeblack-600">{t("pool.invite_hint")}</p>
          <SelectInput label={t("apps.job")} value={jobId} onChange={(e) => setJobId(e.target.value)}
            options={[{ value: "", label: "" }, ...(jobs.data?.items || []).map((j) => ({ value: String(j.id), label: j.title }))]} />
        </Modal>
      )}
    </>
  );
}
