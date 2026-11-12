import { ArrowClockwise, CheckCircle, PencilSimple, WarningCircle, XCircle } from "@phosphor-icons/react";
import { useState } from "react";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, Button, Card, ErrorState, Loading, Modal, PageHeader, TextInput, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

// ---------------- audit log ----------------

type AuditRow = { id: number; record: string; key: string; action: string; by: string; actor_type: string; when: string; changes: string };

/** Who changed what: every change to the records people care about, with the values before and after. */
export function AuditLog() {
  const { t, date } = useLang();
  usePageTitle(t("nav.audit"));
  const list = useList<AuditRow>("/audit", ["record", "action", "from", "to"]);
  const records = (list.data as unknown as { records?: string[] } | undefined)?.records || [];
  const [shown, setShown] = useState<AuditRow | null>(null);
  const changes = (row: AuditRow) => {
    try { return Object.entries(JSON.parse(row.changes) as Record<string, { from?: unknown; to?: unknown }>); } catch { return []; }
  };
  const val = (v: unknown) => (v === null || v === undefined || v === "" ? "-" : String(v));
  return (
    <>
      <PageHeader title={t("nav.audit")} subtitle={t("audit.subtitle")} />
      <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("audit.search")}
        filters={[
          { key: "record", label: t("audit.record"), options: records.map((r) => ({ value: r, label: r })) },
          { key: "action", label: t("audit.action"), options: opts(t, "audit.act.", ["Created", "Updated", "Deleted"]) },
        ]}
        columns={[
          { key: "when", label: t("audit.when"), sort: "when", className: "whitespace-nowrap", render: (r) => date(r.when, true) },
          { key: "record", label: t("audit.record"), sort: "record", render: (r) => (
            <button type="button" className="text-start font-semibold text-primary-700" onClick={() => setShown(r)}>
              <span dir="ltr">{r.record} #{r.key}</span>
            </button>
          ) },
          { key: "action", label: t("audit.action"), sort: "action", render: (r) => (
            <Badge tone={r.action === "Created" ? "green" : r.action === "Deleted" ? "red" : "blue"}>{t(`audit.act.${r.action}`)}</Badge>
          ) },
          { key: "by", label: t("audit.by"), sort: "by", render: (r) => r.by || t(`audit.actor.${r.actor_type}`) },
          { key: "fields", label: t("audit.fields"), render: (r) => (
            <span className="block max-w-xs truncate text-xs text-aeblack-500" dir="ltr">{changes(r).map(([k]) => k).join(", ")}</span>
          ) },
        ]} />
      {shown && (
        <Modal open onClose={() => setShown(null)} title={<span dir="ltr">{shown.record} #{shown.key}</span>}
          footer={<Button variant="outline" onClick={() => setShown(null)}>{t("common.close")}</Button>}>
          <p className="text-sm text-aeblack-600">{t(`audit.act.${shown.action}`)} · {shown.by} · {date(shown.when, true)}</p>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[28rem] text-sm" dir="ltr">
              <thead><tr className="border-b border-aeblack-100 text-aeblack-500">
                <th className="py-2 text-start">{t("audit.field")}</th><th className="text-start">{t("audit.before")}</th><th className="text-start">{t("audit.after")}</th>
              </tr></thead>
              <tbody>
                {changes(shown).map(([k, v]) => (
                  <tr key={k} className="border-b border-aeblack-50 align-top">
                    <td className="py-2 pe-3 font-semibold">{k}</td>
                    <td className="max-w-[14rem] break-words pe-3 text-aeblack-600" dir="auto">{val(v.from)}</td>
                    <td className="max-w-[14rem] break-words" dir="auto">{val(v.to)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Modal>
      )}
    </>
  );
}

// ---------------- assistant usage ----------------

type Usage = {
  default_limit: number; total_month: number; days: { day: string; messages: number }[];
  people: { id: number; name: string; email: string; role: string; enabled: boolean; day: number; week: number; month: number; approved: number; limit: number | null }[];
};

/** How much each person uses the assistant, with their own daily limit. */
export function AssistantUsage() {
  const { t, date } = useLang();
  usePageTitle(t("nav.assistant_usage"));
  const toast = useToast();
  const data = useAsync(() => api<Usage>("/assistant-usage"), []);
  const [edit, setEdit] = useState<{ id: number; name: string; limit: string } | null>(null);
  const { busy, error, run } = useSaving();
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const u = data.data!;
  const max = Math.max(1, ...u.days.map((d) => d.messages));
  const save = async () => {
    if (!edit) return;
    const limit = edit.limit.trim() === "" ? null : Number(edit.limit);
    if (await run(() => api(`/assistant-usage/${edit.id}`, { method: "PUT", body: { limit } })) !== undefined) {
      toast(t("common.saved"));
      setEdit(null);
      data.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("nav.assistant_usage")} subtitle={t("usage.subtitle", { n: u.default_limit })} />
      <div className="space-y-6">
        <Card title={t("usage.daily", { n: u.total_month })}>
          <div className="flex h-32 items-end gap-0.5 border-b border-aeblack-200" dir="ltr">
            {u.days.map((d) => (
              <div key={d.day} className="group flex h-full flex-1 items-end" title={`${date(d.day)}: ${d.messages}`}>
                <div className="w-full rounded-t bg-primary-500 group-hover:bg-primary-700" style={{ height: `${(d.messages / max) * 100}%`, minHeight: d.messages ? 2 : 0 }} />
              </div>
            ))}
          </div>
          <div className="mt-1 flex justify-between text-xs text-aeblack-500" dir="ltr"><span>{date(u.days[0]?.day)}</span><span>{date(u.days[u.days.length - 1]?.day)}</span></div>
        </Card>
        <Card title={t("usage.people")}>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[40rem] text-sm">
              <thead><tr className="border-b border-aeblack-100 text-aeblack-500">
                {["common.name", "usage.day", "usage.week", "usage.month", "usage.approved", "usage.limit", ""].map((k, i) => <th key={i} className="py-2 text-start font-semibold">{k && t(k)}</th>)}
              </tr></thead>
              <tbody>
                {u.people.map((p) => (
                  <tr key={p.id} className={`border-b border-aeblack-50 ${p.enabled ? "" : "opacity-60"}`}>
                    <td className="py-2"><span className="font-semibold">{p.name}</span><span className="block text-xs text-aeblack-500" dir="ltr">{p.email}</span></td>
                    <td className="tabular-nums">{p.day}</td><td className="tabular-nums">{p.week}</td><td className="tabular-nums">{p.month}</td>
                    <td className="tabular-nums">{p.approved}</td>
                    <td>{p.limit === null ? <span className="text-aeblack-500">{t("usage.default", { n: u.default_limit })}</span>
                      : p.limit === 0 ? <Badge tone="red">{t("usage.off")}</Badge> : <span className="tabular-nums">{p.limit}</span>}</td>
                    <td><Button variant="link" size="sm" aria-label={t("common.edit")} onClick={() => setEdit({ id: p.id, name: p.name, limit: p.limit === null ? "" : String(p.limit) })}>
                      <PencilSimple size={20} aria-hidden="true" /></Button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      </div>
      {edit && (
        <Modal open onClose={() => setEdit(null)} title={edit.name} footer={<>
          <Button variant="outline" onClick={() => setEdit(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <TextInput label={t("usage.limit")} hint={t("usage.limit_hint", { n: u.default_limit })} type="number" min={0} max={5000}
            value={edit.limit} onChange={(e) => setEdit({ ...edit, limit: e.target.value })} />
        </Modal>
      )}
    </>
  );
}

// ---------------- health ----------------

type Health = {
  checked_at: string;
  database: { state: string; ms: number };
  background: { state: string; wait_minutes: number; queued: number; running: number; failed: number; recent_failures: { id: number; type: string; target: number; error: string; at: string }[] };
  email: { state: string; configured: boolean; queued: number; sent_day: number; failed_day: number };
  cv_scoring: { state: string; waiting: number; failed: number };
  assistant: { state: string; ms: number };
};

function State({ s }: { s: string }) {
  const { t } = useLang();
  const ok = s === "ok";
  const off = s === "off";
  const Icon = ok ? CheckCircle : off ? WarningCircle : XCircle;
  return (
    <span className={`inline-flex items-center gap-1.5 text-sm font-semibold ${ok ? "text-aegreen-700" : off ? "text-aeblack-500" : "text-aered-700"}`}>
      <Icon size={24} aria-hidden="true" />{t(`health.state.${s}`)}
    </span>
  );
}

/** The state of the service, checked when the page opens. */
export function HealthPage() {
  const { t, date } = useLang();
  usePageTitle(t("nav.health"));
  const data = useAsync(() => api<Health>("/health"), []);
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const h = data.data!;
  const box = (title: string, state: string, lines: [string, string | number][]) => (
    <Card title={title} actions={<State s={state} />}>
      <dl className="space-y-1 text-sm">
        {lines.map(([k, v]) => <div key={k} className="flex justify-between gap-3"><dt className="text-aeblack-600">{t(k)}</dt><dd className="tabular-nums" dir="ltr">{v}</dd></div>)}
      </dl>
    </Card>
  );
  return (
    <>
      <PageHeader title={t("nav.health")} subtitle={t("health.checked", { at: date(h.checked_at, true) })}
        actions={<Button variant="outline" busy={data.loading} onClick={data.reload} icon={<ArrowClockwise size={24} aria-hidden="true" />}>{t("health.check")}</Button>} />
      <div className="grid gap-6 md:grid-cols-2 xl:grid-cols-3">
        {box(t("health.database"), h.database.state, [["health.response", `${h.database.ms} ms`]])}
        {box(t("health.assistant"), h.assistant.state, [["health.response", h.assistant.state === "off" ? "-" : `${h.assistant.ms} ms`]])}
        {box(t("health.background"), h.background.state, [["health.queued", h.background.queued], ["health.running", h.background.running],
          ["health.failed", h.background.failed], ["health.wait", `${h.background.wait_minutes}`]])}
        {box(t("health.email"), h.email.state, [["health.queued", h.email.queued], ["health.sent_day", h.email.sent_day], ["health.failed_day", h.email.failed_day]])}
        {box(t("health.cv"), h.cv_scoring.state, [["health.waiting", h.cv_scoring.waiting], ["health.failed", h.cv_scoring.failed]])}
      </div>
      {h.background.recent_failures.length > 0 && (
        <Card title={t("health.failures")} className="mt-6">
          <ul className="space-y-2 text-sm">
            {h.background.recent_failures.map((f) => (
              <li key={f.id} className="rounded-xl border border-aeblack-100 p-3">
                <p className="font-semibold" dir="ltr">{f.type} #{f.target} · {date(f.at, true)}</p>
                <p className="mt-1 break-words text-xs text-aeblack-600" dir="ltr">{f.error}</p>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </>
  );
}
