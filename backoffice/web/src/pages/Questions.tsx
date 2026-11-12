import { Plus } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, Button, EnglishBadge, PageHeader } from "../components/ui";
import { useGo, useLang, usePageTitle } from "../core";
import { useLookups } from "./JobEdit";

type Row = { id: number; text: string; question_type: string; category: string; job_family: string; difficulty: string; points: number; is_active: boolean; english: string | null };

export const plain = (html: string) => html.replace(/<[^>]+>/g, " ").replace(/&nbsp;/g, " ").replace(/\s+/g, " ").trim();

export default function Questions() {
  const { t, to } = useLang();
  usePageTitle(t("q.title"));
  const go = useGo();
  const lk = useLookups();
  const list = useList<Row>("/questions", ["type", "category", "difficulty", "family", "active", "english"]);
  return (
    <>
      <PageHeader title={t("q.title")} actions={<Button onClick={() => go("/questions/new")} icon={<Plus size={20} aria-hidden="true" />}>{t("q.new")}</Button>} />
      <DataTable list={list} rowKey={(x) => x.id}
        filters={[
          { key: "type", label: t("q.type"), options: opts(t, "qt.", ["SingleChoice", "MultipleChoice", "TrueFalse", "ShortAnswer", "Number", "Essay", "FileUpload"]) },
          { key: "category", label: t("q.category"), options: (lk.data?.categories || []).map((c) => ({ value: c, label: c })) },
          { key: "family", label: t("q.job_family"), options: (lk.data?.job_families || []).map((c) => ({ value: c, label: c })) },
          { key: "difficulty", label: t("q.difficulty"), options: opts(t, "diff.", ["Easy", "Medium", "Hard"]) },
          { key: "active", label: t("q.active_filter"), options: [{ value: "true", label: t("common.active") }, { value: "false", label: t("common.inactive") }] },
          { key: "english", label: t("common.english_state"), options: opts(t, "english.", ["Missing", "Suggested", "Approved"]) },
        ]}
        columns={[
          { key: "text", label: t("q.text"), sort: "text", className: "max-w-md", render: (x) => <>
            <Link to={to(`/questions/${x.id}`)} className="line-clamp-2">{plain(x.text) || `#${x.id}`}</Link>
            {!x.is_active && <Badge>{t("common.inactive")}</Badge>}
          </> },
          { key: "type", label: t("q.type"), sort: "type", className: "whitespace-nowrap", render: (x) => t(`qt.${x.question_type}`) },
          { key: "cat", label: t("q.category"), sort: "category", render: (x) => <>{x.category}{x.job_family && <p className="text-xs text-aeblack-500">{x.job_family}</p>}</> },
          { key: "diff", label: t("q.difficulty"), sort: "difficulty", render: (x) => t(`diff.${x.difficulty}`) },
          { key: "pts", label: t("q.points"), sort: "points", render: (x) => <span className="tabular-nums">{x.points}</span> },
          { key: "en", label: t("common.english"), render: (x) => <EnglishBadge state={x.english || "Missing"} /> },
        ]} />
    </>
  );
}
