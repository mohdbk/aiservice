import { Plus } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { DataTable, useList } from "../components/DataTable";
import { Badge, Button, PageHeader } from "../components/ui";
import { useGo, useLang, usePageTitle } from "../core";

type Row = { id: number; key: string; name: string; name_en: string; kind: string; action_name: string | null; is_active: boolean; updated_at: string };

export const ACTIONS = ["set_applicant_status", "add_note", "schedule_interview", "invite_to_test", "make_offer", "email_candidate", "create_draft_job"];

/** Setup: forms and approval cards that admins design for the recruitment agent. */
export default function Components() {
  const { t, to, date } = useLang();
  usePageTitle(t("nav.components"));
  const go = useGo();
  const list = useList<Row>("/components", ["kind", "active", "action"]);
  return (
    <>
      <PageHeader title={t("nav.components")} subtitle={t("comp.intro")}
        actions={<Button onClick={() => go("/setup/components/new")} icon={<Plus size={20} aria-hidden="true" />}>{t("comp.new")}</Button>} />
      <DataTable list={list} rowKey={(x) => x.id}
        filters={[
          { key: "kind", label: t("comp.kind"), options: [{ value: "Ask", label: t("comp.kind.Ask") }, { value: "Approval", label: t("comp.kind.Approval") }] },
          { key: "action", label: t("comp.action"), options: ACTIONS.map((a) => ({ value: a, label: t(`comp.action.${a}`) })) },
          { key: "active", label: t("common.status"), options: [{ value: "true", label: t("common.active") }, { value: "false", label: t("common.inactive") }] },
        ]}
        columns={[
          { key: "name", label: t("comp.name"), sort: "name", render: (x) => <>
            <Link to={to(`/setup/components/${x.id}`)} className="font-semibold text-primary-700">{x.name}</Link>
            {x.name_en && <p className="text-xs text-aeblack-500" lang="en" dir="ltr">{x.name_en}</p>}
          </> },
          { key: "key", label: t("comp.key"), sort: "key", render: (x) => <code dir="ltr" className="text-xs">{x.key}</code> },
          { key: "kind", label: t("comp.kind"), sort: "kind", render: (x) => <Badge tone={x.kind === "Approval" ? "gold" : "blue"}>{t(`comp.kind.${x.kind}`)}</Badge> },
          { key: "action", label: t("comp.action"), sort: "action", render: (x) => (x.action_name ? t(`comp.action.${x.action_name}`) : "") },
          { key: "active", label: t("common.status"), sort: "active", render: (x) => <Badge tone={x.is_active ? "green" : "gray"}>{x.is_active ? t("common.active") : t("common.inactive")}</Badge> },
          { key: "updated", label: t("comp.updated"), sort: "updated", render: (x) => date(x.updated_at) },
        ]} />
    </>
  );
}
