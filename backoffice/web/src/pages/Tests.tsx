import { Plus } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, Button, EnglishBadge, PageHeader } from "../components/ui";
import { useGo, useLang, usePageTitle } from "../core";

type Row = { id: number; title: string; assessment_type: string; status: string; pass_mark: number; sections: number; minutes: number; invitations: number; english: string | null };

const TYPES = ["KnowledgeTest", "LanguageCheck", "SituationalJudgement", "WorkSample", "Aptitude", "Other"];

export default function Tests() {
  const { t, to } = useLang();
  usePageTitle(t("test.title"));
  const go = useGo();
  const list = useList<Row>("/assessments", ["status", "type"]);
  return (
    <>
      <PageHeader title={t("test.title")} actions={<Button onClick={() => go("/tests/new")} icon={<Plus size={20} aria-hidden="true" />}>{t("test.new")}</Button>} />
      <DataTable list={list} rowKey={(x) => x.id}
        filters={[
          { key: "status", label: t("common.status"), options: opts(t, "status.", ["Draft", "Published", "Archived"]) },
          { key: "type", label: t("test.type"), options: opts(t, "type.", TYPES) },
        ]}
        columns={[
          { key: "title", label: t("test.name_ar"), sort: "title", render: (x) => <Link to={to(`/tests/${x.id}`)}>{x.title}</Link> },
          { key: "type", label: t("test.type"), sort: "type", render: (x) => t(`type.${x.assessment_type}`) },
          { key: "status", label: t("common.status"), sort: "status", render: (x) => <Badge tone={x.status === "Published" ? "green" : x.status === "Draft" ? "amber" : "gray"}>{t(`status.${x.status}`)}</Badge> },
          { key: "sections", label: t("test.sections"), render: (x) => <span className="text-sm">{x.sections} · {t("test.minutes_total", { minutes: x.minutes })}</span> },
          { key: "pass", label: t("test.pass_mark"), sort: "pass_mark", render: (x) => <span className="tabular-nums" dir="ltr">{x.pass_mark}%</span> },
          { key: "inv", label: t("test.invitations"), render: (x) => <Link to={to(`/invitations?assessment=${x.id}`)} className="tabular-nums">{x.invitations}</Link> },
          { key: "en", label: t("common.english"), render: (x) => <EnglishBadge state={x.english || "Missing"} /> },
        ]} />
    </>
  );
}
