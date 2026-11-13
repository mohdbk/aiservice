import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { AssessmentLinkButton } from "../components/AssessmentLink";
import { Badge, PageHeader, Score } from "../components/ui";
import { useLang, usePageTitle } from "../core";
import { invTone } from "./Applicant";
import { useLookups } from "./JobEdit";

type Row = {
  id: number; code: string; status: string; deadline_at: string; score: number | null; result: string | null; attempts_used: number; attempts_allowed: number; extra_time_percent?: number;
  assessment: string; applicant_id: number; applicant: string; job_title: string; to_mark: number | null; last_attempt: number | null;
};

export default function Invitations() {
  const { t, to, date } = useLang();
  usePageTitle(t("inv.title"));
  const lk = useLookups();
  const list = useList<Row>("/invitations", ["status", "assessment", "result"]);
  return (
    <>
      <PageHeader title={t("inv.title")} subtitle={t("inv.subtitle")} />
      <StatusTabs counts={(list.data as unknown as { counts?: Record<string, number> })?.counts} value={list.get("status")} onChange={(v) => list.set({ status: v || null, page: null })} />
      <DataTable list={list} rowKey={(x) => x.id}
        filters={[
          { key: "assessment", label: t("app.test"), options: (lk.data?.assessments || []).map((a) => ({ value: String(a.id), label: a.title })) },
          { key: "result", label: t("app.result"), options: opts(t, "res.", ["Pass", "Fail", "AwaitingMarking"]) },
        ]}
        columns={[
          { key: "applicant", label: t("int.applicant"), sort: "applicant", render: (x) => <><Link to={to(`/applicants/${x.applicant_id}`)}>{x.applicant}</Link><p className="text-xs text-aeblack-500">{x.job_title}</p></> },
          { key: "test", label: t("app.test"), sort: "test", render: (x) => x.assessment },
          { key: "status", label: t("common.status"), sort: "status", render: (x) => <Badge tone={invTone(x.status)}>{t(`inv.status.${x.status}`)}</Badge> },
          { key: "deadline", label: t("app.deadline"), sort: "deadline", className: "whitespace-nowrap", render: (x) => date(x.deadline_at, true) },
          { key: "attempts", label: t("app.attempts"), render: (x) => <><span dir="ltr">{x.attempts_used} / {x.attempts_allowed}</span>{!!x.extra_time_percent && <span className="block text-xs text-aeblack-500">{t("extra.label")} {t("extra.value", { n: x.extra_time_percent })}</span>}</> },
          { key: "score", label: t("apps.score"), sort: "score", render: (x) => <span className="inline-flex items-center gap-2"><Score value={x.score} />{x.result && <span className="text-xs">{t(`res.${x.result}`)}</span>}</span> },
          { key: "mark", label: t("common.actions"), render: (x) => (
            <div className="flex flex-wrap gap-2">
              {(x.status === "Invited" || x.status === "InProgress") && <AssessmentLinkButton id={x.id} />}
              {(x.to_mark || x.last_attempt) && x.status !== "InProgress"
                ? <Link className="aegov-btn btn-outline btn-sm" to={to(`/marking/${x.to_mark || x.last_attempt}`)}>{x.to_mark ? t("app.mark") : t("inv.view_result")}</Link> : null}
            </div>
          ) },
        ]} />
    </>
  );
}

const TABS = ["Submitted", "Invited", "InProgress", "Marked", "Expired"];

/** Tabs for what needs attention first: to mark, not started, in progress, completed, expired (with counts). */
function StatusTabs({ counts, value, onChange }: { counts?: Record<string, number>; value: string; onChange: (v: string) => void }) {
  const { t } = useLang();
  const total = Object.values(counts || {}).reduce((a, b) => a + b, 0);
  const tab = (key: string, label: string, n: number | undefined, urgent = false) => (
    <button key={key} type="button" role="tab" aria-selected={value === key} onClick={() => onChange(key)}
      className={`inline-flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-semibold transition ${value === key ? "bg-primary-600 text-white" : "bg-white text-aeblack-700 ring-1 ring-aeblack-100 hover:bg-primary-50"}`}>
      {label}
      {n !== undefined && <span className={`rounded-full px-2 text-xs tabular-nums ${value === key ? "bg-white/25" : urgent && n > 0 ? "bg-camel-100 text-camel-900" : "bg-aeblack-50 text-aeblack-600"}`}>{n}</span>}
    </button>
  );
  return (
    <div role="tablist" aria-label={t("common.status")} className="mb-4 flex flex-wrap gap-2">
      {tab("", t("inv.tab.all"), counts ? total : undefined)}
      {TABS.map((k) => tab(k, t(`inv.tab.${k}`), counts ? counts[k] ?? 0 : undefined, k === "Submitted"))}
    </div>
  );
}
