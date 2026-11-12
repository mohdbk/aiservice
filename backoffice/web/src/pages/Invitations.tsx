import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, PageHeader, Score } from "../components/ui";
import { useLang, usePageTitle } from "../core";
import { invTone } from "./Applicant";
import { useLookups } from "./JobEdit";

type Row = {
  id: number; code: string; status: string; deadline_at: string; score: number | null; result: string | null; attempts_used: number; attempts_allowed: number;
  assessment: string; applicant_id: number; applicant: string; job_title: string; to_mark: number | null; last_attempt: number | null;
};

export default function Invitations() {
  const { t, to, date } = useLang();
  usePageTitle(t("inv.title"));
  const lk = useLookups();
  const list = useList<Row>("/invitations", ["status", "assessment", "result"]);
  return (
    <>
      <PageHeader title={t("inv.title")} />
      <DataTable list={list} rowKey={(x) => x.id}
        filters={[
          { key: "status", label: t("common.status"), options: opts(t, "inv.status.", ["Invited", "InProgress", "Submitted", "Marked", "Expired", "Cancelled"]) },
          { key: "assessment", label: t("app.test"), options: (lk.data?.assessments || []).map((a) => ({ value: String(a.id), label: a.title })) },
          { key: "result", label: t("app.result"), options: opts(t, "res.", ["Pass", "Fail", "AwaitingMarking"]) },
        ]}
        columns={[
          { key: "applicant", label: t("int.applicant"), sort: "applicant", render: (x) => <><Link to={to(`/applicants/${x.applicant_id}`)}>{x.applicant}</Link><p className="text-xs text-aeblack-500">{x.job_title}</p></> },
          { key: "test", label: t("app.test"), sort: "test", render: (x) => x.assessment },
          { key: "status", label: t("common.status"), sort: "status", render: (x) => <Badge tone={invTone(x.status)}>{t(`inv.status.${x.status}`)}</Badge> },
          { key: "deadline", label: t("app.deadline"), sort: "deadline", className: "whitespace-nowrap", render: (x) => date(x.deadline_at, true) },
          { key: "attempts", label: t("app.attempts"), render: (x) => <span dir="ltr">{x.attempts_used} / {x.attempts_allowed}</span> },
          { key: "score", label: t("apps.score"), sort: "score", render: (x) => <span className="inline-flex items-center gap-2"><Score value={x.score} />{x.result && <span className="text-xs">{t(`res.${x.result}`)}</span>}</span> },
          { key: "mark", label: t("common.actions"), render: (x) => (x.to_mark || x.last_attempt) && x.status !== "InProgress"
            ? <Link className="aegov-btn btn-outline btn-sm" to={to(`/marking/${x.to_mark || x.last_attempt}`)}>{t("app.mark")}</Link> : null },
        ]} />
    </>
  );
}
