import { Brain, Plus } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { ActionMenu, Badge, Button, EnglishBadge, ErrorState, PageHeader, useSaving } from "../components/ui";
import { api, useAsync, useGo, useLang, usePageTitle, useToast } from "../core";

type Row = { id: number; title: string; assessment_type: string; status: string; pass_mark: number; sections: number; minutes: number; invitations: number; english: string | null };

const TYPES = ["KnowledgeTest", "LanguageCheck", "SituationalJudgement", "WorkSample", "Aptitude", "Personality", "Other"];
type Template = { key: string; model: string; title: string; title_en: string; items: number };

export default function Tests() {
  const { t, to } = useLang();
  usePageTitle(t("test.title"));
  const go = useGo();
  const list = useList<Row>("/assessments", ["status", "type"]);
  const templates = useAsync(() => api<Template[]>("/assessment-templates"), []);
  const adding = useSaving();
  const toast = useToast();
  const { lang } = useLang();
  const addTemplate = async (tp: Template) => {
    const r = await adding.run(() => api<{ id: number; questions: number }>(`/assessment-templates/${tp.key}`, { body: {} }));
    if (r) {
      toast(t("pers.added", { count: r.questions }));
      go(`/tests/${r.id}`);
    }
  };
  return (
    <>
      <PageHeader title={t("test.title")} subtitle={t("test.subtitle")} actions={<>
        {(templates.data || []).length > 0 && (
          <ActionMenu label={t("pers.ready_made")} icon={<Brain size={20} aria-hidden="true" />}
            items={(templates.data || []).map((tp) => ({ label: lang === "en" ? tp.title_en : tp.title, onClick: () => addTemplate(tp), disabled: adding.busy }))} />
        )}
        <Button onClick={() => go("/tests/new")} icon={<Plus size={20} aria-hidden="true" />}>{t("test.new")}</Button>
      </>} />
      {adding.error && <div className="mb-4"><ErrorState error={adding.error} /></div>}
      <DataTable list={list} rowKey={(x) => x.id}
        filters={[
          { key: "status", label: t("common.status"), options: opts(t, "status.", ["Draft", "Published", "Archived"]) },
          { key: "type", label: t("test.type"), options: opts(t, "type.", TYPES) },
        ]}
        columns={[
          { key: "title", label: t("test.name"), sort: "title", render: (x) => <Link to={to(`/tests/${x.id}`)}>{x.title}</Link> },
          { key: "type", label: t("test.type"), sort: "type", render: (x) => t(`type.${x.assessment_type}`) },
          { key: "status", label: t("common.status"), sort: "status", render: (x) => <Badge tone={x.status === "Published" ? "green" : x.status === "Draft" ? "amber" : "gray"}>{t(`status.${x.status}`)}</Badge> },
          { key: "sections", label: t("test.sections"), render: (x) => <span className="text-sm">{x.sections} · {t("test.minutes_total", { minutes: x.minutes })}</span> },
          { key: "pass", label: t("test.pass_mark"), sort: "pass_mark", render: (x) => x.assessment_type === "Personality" ? <span className="text-aeblack-500">-</span> : <span className="tabular-nums" dir="ltr">{x.pass_mark}%</span> },
          { key: "inv", label: t("test.invitations"), render: (x) => <Link to={to(`/invitations?assessment=${x.id}`)} className="tabular-nums">{x.invitations}</Link> },
          { key: "en", label: t("common.english"), render: (x) => <EnglishBadge state={x.english || "Missing"} /> },
        ]} />
    </>
  );
}
