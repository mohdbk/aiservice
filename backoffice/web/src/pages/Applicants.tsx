import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, PageHeader, Score, TextInput } from "../components/ui";
import { api, useAsync, useLang, usePageTitle } from "../core";

type Row = {
  id: number; code: string; full_name: string; email: string; job_id: number; job_code: string; job_title: string; status: string;
  screening: string; country: string; score: number | null; ai_status: string | null; created_at: string; interviews: number;
};

export const appTone = (s: string) => (s === "Open" ? "blue" : s === "Accepted" ? "green" : s === "Rejected" ? "red" : s === "Hold" ? "amber" : "gray");
export const screenTone = (s: string) => (s === "Meets" ? "green" : s === "DoesNotMeet" ? "red" : "gray");

export default function Applicants() {
  const { t, to, date } = useLang();
  usePageTitle(t("apps.title"));
  const jobs = useAsync(() => api<{ items: { id: number; title: string }[] }>("/jobs?size=100&sort=title&dir=asc"), []);
  const list = useList<Row>("/applicants", ["job", "status", "screening", "ai", "minScore", "from", "to"]);
  return (
    <>
      <PageHeader title={t("apps.title")} subtitle={list.data && t("apps.total", { count: list.data.total })} />
      <DataTable list={list} rowKey={(a) => a.id} searchLabel={t("apps.search_hint")}
        filters={[
          { key: "job", label: t("apps.job"), options: (jobs.data?.items || []).map((j) => ({ value: String(j.id), label: j.title })) },
          { key: "status", label: t("common.status"), options: opts(t, "app.status.", ["Open", "Replied", "Hold", "Accepted", "Rejected"]) },
          { key: "screening", label: t("apps.screening"), options: opts(t, "screen.", ["Meets", "DoesNotMeet", "NoRequirements", "NoQuestions"]) },
          { key: "ai", label: t("apps.ai"), options: opts(t, "ai.", ["Scored", "Pending", "Processing", "Failed", "Skipped"]) },
          { key: "minScore", label: t("apps.min_score"), options: ["50", "60", "70", "80", "90"].map((v) => ({ value: v, label: `${v}+` })) },
        ]}
        toolbar={<>
          <TextInput wrapClass="w-40" label={t("apps.from")} type="date" value={list.get("from")} onChange={(e) => list.set({ from: e.target.value || null })} />
          <TextInput wrapClass="w-40" label={t("apps.to")} type="date" value={list.get("to")} onChange={(e) => list.set({ to: e.target.value || null })} />
        </>}
        columns={[
          { key: "name", label: t("common.name"), sort: "name", render: (a) => <>
            <Link to={to(`/applicants/${a.id}`)}>{a.full_name}</Link>
            <p className="text-xs text-aeblack-500" dir="ltr">{a.code}</p>
          </> },
          { key: "job", label: t("apps.job"), sort: "job", className: "max-w-64", render: (a) => <span className="line-clamp-2">{a.job_title}</span> },
          { key: "status", label: t("common.status"), sort: "status", render: (a) => <Badge tone={appTone(a.status)}>{t(`app.status.${a.status}`)}</Badge> },
          { key: "screening", label: t("apps.screening"), sort: "screening", render: (a) => <Badge tone={screenTone(a.screening)}>{t(`screen.${a.screening}`)}</Badge> },
          { key: "score", label: t("apps.score"), sort: "score", render: (a) => a.ai_status && a.ai_status !== "Scored"
            ? <span className="text-xs text-aeblack-500">{t(`ai.${a.ai_status}`)}</span> : <Score value={a.score} /> },
          { key: "created", label: t("apps.applied"), sort: "created", className: "whitespace-nowrap", render: (a) => date(a.created_at) },
        ]} />
    </>
  );
}
