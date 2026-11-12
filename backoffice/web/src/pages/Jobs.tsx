import { Plus } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, Button, EnglishBadge, PageHeader } from "../components/ui";
import { useGo, useLang, usePageTitle } from "../core";
import { useLookups } from "./JobEdit";

type JobRow = {
  id: number; code: string; title: string; title_en: string; department: string; status: string; is_published: boolean;
  posted_on: string | null; closes_on: string | null; applicants: number; new_applicants: number; english: string | null;
};

export const statusTone = (s: string) => (s === "Open" ? "green" : s === "Closed" ? "gray" : "amber");

export default function Jobs() {
  const { t, to, date, lang } = useLang();
  usePageTitle(t("jobs.title"));
  const go = useGo();
  const lk = useLookups();
  const list = useList<JobRow>("/jobs", ["status", "published", "department", "english"]);
  return (
    <>
      <PageHeader title={t("jobs.title")} actions={<Button onClick={() => go("/jobs/new")} icon={<Plus size={20} aria-hidden="true" />}>{t("jobs.new")}</Button>} />
      <DataTable list={list} rowKey={(j) => j.id}
        filters={[
          { key: "status", label: t("jobs.status"), options: opts(t, "status.", ["Open", "Draft", "Closed"]) },
          { key: "published", label: t("jobs.published"), options: [{ value: "true", label: t("common.yes") }, { value: "false", label: t("common.no") }] },
          { key: "department", label: t("jobs.department"), options: (lk.data?.departments || []).map((d) => ({ value: String(d.id), label: d.name })) },
          { key: "english", label: t("common.english_state"), options: opts(t, "english.", ["Missing", "Suggested", "Approved"]) },
        ]}
        columns={[
          { key: "code", label: t("common.code"), sort: "code", render: (j) => <span dir="ltr" className="whitespace-nowrap text-aeblack-500">{j.code}</span> },
          { key: "title", label: t("jobs.title"), sort: "title", render: (j) => <>
            <Link to={to(`/jobs/${j.id}`)}>{lang === "en" && j.title_en ? j.title_en : j.title}</Link>
          </> },
          { key: "dep", label: t("jobs.department"), sort: "department", render: (j) => j.department },
          { key: "status", label: t("jobs.status"), sort: "status", render: (j) => <div className="flex flex-wrap gap-1">
            <Badge tone={statusTone(j.status)}>{t(`status.${j.status}`)}</Badge>
            {j.is_published && j.status === "Open" && <Badge tone="gold">{t("dash.published_jobs")}</Badge>}
          </div> },
          { key: "apps", label: t("jobs.applicants"), sort: "applicants", render: (j) => <span className="inline-flex items-center gap-2">
            <Link to={to(`/applicants?job=${j.id}`)} className="tabular-nums">{j.applicants}</Link>
            {j.new_applicants > 0 && <Badge tone="blue">{j.new_applicants} {t("jobs.new_applicants")}</Badge>}
          </span> },
          { key: "closes", label: t("jobs.closes_on"), sort: "closes", className: "whitespace-nowrap", render: (j) => date(j.closes_on) },
          { key: "en", label: t("common.english"), render: (j) => <EnglishBadge state={j.english || "Missing"} /> },
        ]} />
    </>
  );
}
