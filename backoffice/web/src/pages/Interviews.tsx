import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, PageHeader, SectionTabs } from "../components/ui";
import { useLang, usePageTitle } from "../core";
import { useLookups } from "./JobEdit";

type Row = {
  id: number; code: string; starts_at: string; ends_at: string; status: string; location: string; round: string;
  applicant_id: number; applicant: string; job_title: string; panel: string[]; feedback: number;
};

export const intTone = (s: string) => (s === "Scheduled" ? "blue" : s === "UnderReview" ? "amber" : s === "Cleared" ? "green" : s === "Rejected" ? "red" : "gray");

export default function Interviews() {
  const { t, to, date } = useLang();
  usePageTitle(t("int.title"));
  const lk = useLookups();
  const list = useList<Row>("/interviews", ["scope", "status", "round"]);
  const scope = list.get("scope") || "upcoming";
  return (
    <>
      <PageHeader title={t("int.title")} />
      <SectionTabs value={scope} onChange={(v) => list.set({ scope: v === "upcoming" ? null : v, sort: null, dir: null })}
        tabs={["upcoming", "mine", "review", "past"].map((s) => ({ value: s, label: t(`int.scope.${s}`) }))} />
      <DataTable list={list} rowKey={(i) => i.id}
        filters={[
          { key: "status", label: t("common.status"), options: opts(t, "int.status.", ["Scheduled", "UnderReview", "Cleared", "Rejected", "Cancelled"]) },
          { key: "round", label: t("int.round"), options: (lk.data?.rounds || []).map((r) => ({ value: String(r.id), label: r.name })) },
        ]}
        columns={[
          { key: "date", label: t("int.date"), sort: "starts", className: "whitespace-nowrap", render: (i) => <Link to={to(`/interviews/${i.id}`)}>{date(i.starts_at, true)}</Link> },
          { key: "applicant", label: t("int.applicant"), sort: "applicant", render: (i) => <>
            <Link to={to(`/applicants/${i.applicant_id}`)}>{i.applicant}</Link>
            <p className="text-xs text-aeblack-500">{i.job_title}</p>
          </> },
          { key: "round", label: t("int.round"), sort: "round", render: (i) => i.round },
          { key: "panel", label: t("int.panel"), render: (i) => <span className="text-sm">{i.panel.join("، ")}</span> },
          { key: "status", label: t("common.status"), sort: "status", render: (i) => <Badge tone={intTone(i.status)}>{t(`int.status.${i.status}`)}</Badge> },
          { key: "fb", label: t("int.feedback"), render: (i) => <span className="tabular-nums">{i.feedback}</span> },
        ]} />
    </>
  );
}
