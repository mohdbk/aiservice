import { ArrowClockwise, CheckCircle, PencilSimple, WarningCircle, XCircle } from "@phosphor-icons/react";
import { useState } from "react";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, Button, Card, ErrorState, Loading, Modal, Note, PageHeader, SectionTabs, SelectInput, TextArea, TextInput, Toggle, useSaving } from "../components/ui";
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
      <Backups />
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

// ---------------- data protection (PDPL) ----------------

type RequestRow = { id: number; kind: string; status: string; requester: string; reason: string; response: string; created_at: string; closed_at: string | null; applications: number };
type ConsentRow = { id: number; kind: string; granted: boolean; detail: string; at: string; email: string };
type Retention = {
  is_enabled: boolean; unsuccessful_months: number; inactive_account_months: number; last_run_at: string | null; last_run_count: number;
  due_applications: number; due_accounts: number; open_requests: number;
};

/** Candidates' data requests, how long data is kept, and the consent records. */
export function DataProtection() {
  const { t } = useLang();
  usePageTitle(t("nav.data_protection"));
  const [tab, setTab] = useState("requests");
  return (
    <>
      <PageHeader title={t("nav.data_protection")} subtitle={t("pdp.subtitle")} />
      <SectionTabs value={tab} onChange={setTab} tabs={[
        { value: "requests", label: t("pdp.requests") }, { value: "retention", label: t("pdp.retention") }, { value: "consents", label: t("pdp.consents") },
      ]} />
      {tab === "requests" && <DataRequests />}
      {tab === "retention" && <RetentionSettings />}
      {tab === "consents" && <Consents />}
    </>
  );
}

function DataRequests() {
  const { t, date } = useLang();
  const toast = useToast();
  const list = useList<RequestRow>("/data-requests", ["status", "kind"]);
  const [open, setOpen] = useState<{ row: RequestRow; action: "approve" | "decline" } | null>(null);
  const [response, setResponse] = useState("");
  const { busy, error, run } = useSaving();
  const act = async () => {
    if (!open) return;
    if (await run(() => api(`/data-requests/${open.row.id}/${open.action}`, { body: { response } })) !== undefined) {
      toast(t("common.saved"));
      setOpen(null);
      setResponse("");
      list.reload();
    }
  };
  return (
    <>
      <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("pdp.search")}
        filters={[
          { key: "status", label: t("common.status"), options: opts(t, "pdp.status.", ["Open", "Completed", "Declined"]) },
          { key: "kind", label: t("pdp.kind"), options: opts(t, "pdp.kind.", ["Deletion", "Export"]) },
        ]}
        columns={[
          { key: "created", label: t("pdp.received"), sort: "created", className: "whitespace-nowrap", render: (r) => date(r.created_at, true) },
          { key: "requester", label: t("pdp.requester"), render: (r) => <span dir="auto">{r.requester}</span> },
          { key: "kind", label: t("pdp.kind"), sort: "kind", render: (r) => t(`pdp.kind.${r.kind}`) },
          { key: "reason", label: t("pdp.reason"), render: (r) => <span className="block max-w-xs truncate text-sm" dir="auto" title={r.reason}>{r.reason || "-"}</span> },
          { key: "status", label: t("common.status"), sort: "status", render: (r) => (
            <>
              <Badge tone={r.status === "Open" ? "amber" : r.status === "Completed" ? "green" : "gray"}>{t(`pdp.status.${r.status}`)}</Badge>
              {r.response && <p className="mt-1 max-w-xs truncate text-xs text-aeblack-500" dir="auto" title={r.response}>{r.response}</p>}
            </>
          ) },
          { key: "act", label: t("common.actions"), render: (r) => r.status === "Open" && r.kind === "Deletion" && (
            <div className="flex flex-wrap gap-2">
              <Button size="sm" onClick={() => setOpen({ row: r, action: "approve" })}>{t("pdp.approve")}</Button>
              <Button size="sm" variant="outline" onClick={() => setOpen({ row: r, action: "decline" })}>{t("pdp.decline")}</Button>
            </div>
          ) },
        ]} />
      {open && (
        <Modal open onClose={() => setOpen(null)} title={t(open.action === "approve" ? "pdp.approve_title" : "pdp.decline_title")} footer={<>
          <Button variant="outline" onClick={() => setOpen(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} onClick={act}>{t(open.action === "approve" ? "pdp.approve" : "pdp.decline")}</Button></>}>
          {error && <ErrorState error={error} />}
          <p className="text-sm" dir="auto">{open.row.requester}</p>
          {open.action === "approve" && <Note tone="warning">{t("pdp.approve_hint", { n: open.row.applications })}</Note>}
          <TextArea label={t(open.action === "approve" ? "pdp.note" : "pdp.decline_reason")} rows={3} value={response} onChange={(e) => setResponse(e.target.value)} />
        </Modal>
      )}
    </>
  );
}

function RetentionSettings() {
  const { t, date } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<Retention>("/data-protection"), []);
  const [f, setF] = useState<Retention | null>(null);
  const { busy, error, run } = useSaving();
  const r = f || data.data;
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  if (!r) return null;
  const save = async () => {
    if (await run(() => api("/data-protection", { method: "PUT", body: r })) !== undefined) { toast(t("common.saved")); setF(null); data.reload(); }
  };
  const runNow = async () => {
    if (!window.confirm(t("pdp.run_confirm", { n: r.due_applications + r.due_accounts }))) return;
    const res = await run(() => api<{ anonymised: number }>("/data-protection/run", { body: {} }));
    if (res) { toast(t("pdp.ran", { n: res.anonymised })); data.reload(); }
  };
  return (
    <Card title={t("pdp.retention")} actions={<>
      <Button variant="outline" size="sm" busy={busy} onClick={runNow}>{t("pdp.run_now")}</Button>
      <Button size="sm" busy={busy} onClick={save}>{t("common.save")}</Button>
    </>}>
      <div className="mb-4"><Note>{t("pdp.retention_hint")}</Note></div>
      {error && <div className="mb-4"><ErrorState error={error} /></div>}
      <div className="grid max-w-2xl gap-4">
        <Toggle label={t("pdp.enabled")} checked={r.is_enabled} onChange={(v) => setF({ ...r, is_enabled: v })} />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextInput required label={t("pdp.unsuccessful_months")} type="number" min={1} max={240} value={r.unsuccessful_months}
            onChange={(e) => setF({ ...r, unsuccessful_months: Number(e.target.value) || 0 })} />
          <TextInput required label={t("pdp.inactive_months")} type="number" min={1} max={240} value={r.inactive_account_months}
            onChange={(e) => setF({ ...r, inactive_account_months: Number(e.target.value) || 0 })} />
        </div>
        <p className="text-sm text-aeblack-600">{t("pdp.due", { apps: r.due_applications, accounts: r.due_accounts })}</p>
        {r.last_run_at && <p className="text-sm text-aeblack-500">{t("pdp.last_run", { at: date(r.last_run_at, true), n: r.last_run_count })}</p>}
      </div>
    </Card>
  );
}

function Consents() {
  const { t, date } = useLang();
  const list = useList<ConsentRow>("/consents", ["kind"]);
  return (
    <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("pdp.consent_search")}
      filters={[{ key: "kind", label: t("pdp.kind"), options: opts(t, "pdp.consent.", ["declaration", "talent_pool"]) }]}
      columns={[
        { key: "at", label: t("audit.when"), sort: "at", className: "whitespace-nowrap", render: (r) => date(r.at, true) },
        { key: "email", label: t("pdp.candidate"), render: (r) => <span dir="ltr">{r.email}</span> },
        { key: "kind", label: t("pdp.kind"), sort: "kind", render: (r) => t(`pdp.consent.${r.kind}`) },
        { key: "granted", label: t("pdp.given"), render: (r) => <Badge tone={r.granted ? "green" : "gray"}>{t(r.granted ? "pdp.granted" : "pdp.withdrawn")}</Badge> },
        { key: "detail", label: t("pdp.detail"), render: (r) => <span dir="ltr">{r.detail}</span> },
      ]} />
  );
}

// ---------------- text messages ----------------

type TextSettings = {
  is_enabled: boolean; channel: string; provider: string; url: string; account_id: string; has_token: boolean; sender: string; events: string[];
  all_events: { key: string; name_ar: string; name_en: string }[]; sent_day: number; failed_day: number; queued: number;
  recent: { id: number; to: string; event: string; status: string; error: string; created_at: string }[];
};

/** SMS or WhatsApp messages alongside candidate emails, through the Ministry's gateway (a webhook) or Twilio. */
export function TextMessages() {
  const { t, lang, date } = useLang();
  usePageTitle(t("nav.texts"));
  const toast = useToast();
  const data = useAsync(() => api<TextSettings>("/text-settings"), []);
  const [f, setF] = useState<(TextSettings & { token: string }) | null>(null);
  const [to, setTo] = useState("");
  const { busy, error, run } = useSaving();
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const s = f || { ...data.data!, token: "" };
  const set = (k: string, v: unknown) => setF({ ...s, [k]: v });
  const save = async () => {
    if (await run(() => api("/text-settings", { method: "PUT", body: s })) !== undefined) { toast(t("common.saved")); setF(null); data.reload(); }
  };
  const test = async () => {
    const r = await run(() => api<{ ok: boolean; error: string }>("/text-settings/test", { body: { to } }));
    if (r) toast(r.ok ? t("texts.test_ok") : t("texts.test_failed", { error: r.error }), r.ok ? "success" : "error");
  };
  return (
    <>
      <PageHeader title={t("nav.texts")} subtitle={t("texts.subtitle")} actions={<Button busy={busy} onClick={save}>{t("common.save")}</Button>} />
      {error && <div className="mb-4"><ErrorState error={error} /></div>}
      <div className="grid gap-6 xl:grid-cols-3">
        <Card title={t("texts.provider")} className="xl:col-span-2">
          <div className="grid gap-4">
            <Toggle label={t("texts.enabled")} checked={s.is_enabled} onChange={(v) => set("is_enabled", v)} />
            <div className="grid gap-4 sm:grid-cols-2">
              <SelectInput required label={t("texts.channel")} value={s.channel} onChange={(e) => set("channel", e.target.value)}
                options={[{ value: "Sms", label: "SMS" }, { value: "WhatsApp", label: "WhatsApp" }]} />
              <SelectInput required label={t("texts.service")} value={s.provider} onChange={(e) => set("provider", e.target.value)}
                options={[{ value: "Webhook", label: t("texts.webhook") }, { value: "Twilio", label: "Twilio" }]} />
            </div>
            {s.provider === "Webhook" ? (
              <TextInput required label={t("texts.url")} hint={t("texts.url_hint")} type="url" value={s.url} onChange={(e) => set("url", e.target.value)} />
            ) : (
              <TextInput required label={t("texts.account")} value={s.account_id} dir="ltr" onChange={(e) => set("account_id", e.target.value)} />
            )}
            <div className="grid gap-4 sm:grid-cols-2">
              <TextInput label={t("texts.token")} type="password" autoComplete="new-password" value={s.token}
                hint={s.has_token ? t("texts.token_kept") : undefined} onChange={(e) => set("token", e.target.value)} />
              <TextInput required label={t("texts.sender")} hint={t("texts.sender_hint")} dir="ltr" value={s.sender} onChange={(e) => set("sender", e.target.value)} />
            </div>
            <fieldset>
              <legend className="mb-2 text-sm font-semibold">{t("texts.events")}</legend>
              <div className="grid gap-2 sm:grid-cols-2">
                {s.all_events.map((ev) => (
                  <Toggle key={ev.key} label={lang === "en" ? ev.name_en : ev.name_ar} checked={s.events.includes(ev.key)}
                    onChange={(v) => set("events", v ? [...s.events, ev.key] : s.events.filter((x) => x !== ev.key))} />
                ))}
              </div>
              <p className="mt-2 text-xs text-aeblack-500">{t("texts.events_hint")}</p>
            </fieldset>
          </div>
        </Card>
        <div className="space-y-6">
          <Card title={t("texts.test")}>
            <TextInput required label={t("texts.test_to")} type="tel" dir="ltr" placeholder="+9715..." value={to} onChange={(e) => setTo(e.target.value)} />
            <Button variant="outline" className="mt-3" busy={busy} disabled={!to} onClick={test}>{t("texts.send_test")}</Button>
            <p className="mt-2 text-xs text-aeblack-500">{t("texts.test_hint")}</p>
          </Card>
          <Card title={t("texts.activity")}>
            <dl className="space-y-1 text-sm">
              {([["health.queued", s.queued], ["health.sent_day", s.sent_day], ["health.failed_day", s.failed_day]] as const).map(([k, v]) => (
                <div key={k} className="flex justify-between"><dt className="text-aeblack-600">{t(k)}</dt><dd className="tabular-nums">{v}</dd></div>
              ))}
            </dl>
            {s.recent.length > 0 && (
              <ul className="mt-3 space-y-1 text-xs">
                {s.recent.map((m) => (
                  <li key={m.id} className="flex justify-between gap-2" title={m.error}>
                    <span dir="ltr">{m.to} · {m.event}</span>
                    <span className={m.status === "Failed" ? "text-aered-700" : "text-aeblack-500"}>{t(`mail.status.${m.status}`)} · {date(m.created_at, true)}</span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      </div>
    </>
  );
}

// ---------------- HR system hand-off ----------------

type HandOff = {
  is_enabled: boolean; url: string; has_secret: boolean; include_cv: boolean; waiting: number;
  recent: { id: number; code: string; name: string; handed_off_at: string | null; error: string }[];
};

/** Accepted offers are sent to the HR system (signed JSON); the same details can be downloaded per offer or as CSV. */
export function HandOffSettings() {
  const { t, date } = useLang();
  usePageTitle(t("nav.handoff"));
  const toast = useToast();
  const data = useAsync(() => api<HandOff>("/handoff-settings"), []);
  const [f, setF] = useState<(HandOff & { secret: string }) | null>(null);
  const { busy, error, run } = useSaving();
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const s = f || { ...data.data!, secret: "" };
  const save = async () => {
    if (await run(() => api("/handoff-settings", { method: "PUT", body: s })) !== undefined) { toast(t("common.saved")); setF(null); data.reload(); }
  };
  return (
    <>
      <PageHeader title={t("nav.handoff")} subtitle={t("handoff.subtitle")} actions={<Button busy={busy} onClick={save}>{t("common.save")}</Button>} />
      {error && <div className="mb-4"><ErrorState error={error} /></div>}
      <div className="grid gap-6 xl:grid-cols-3">
        <Card title={t("handoff.settings")} className="xl:col-span-2">
          <div className="grid gap-4">
            <Toggle label={t("handoff.enabled")} checked={s.is_enabled} onChange={(v) => setF({ ...s, is_enabled: v })} />
            <TextInput required label={t("handoff.url")} type="url" value={s.url} onChange={(e) => setF({ ...s, url: e.target.value })} />
            <TextInput label={t("handoff.secret")} type="password" autoComplete="new-password" value={s.secret}
              hint={s.has_secret ? t("handoff.secret_kept") : t("handoff.secret_hint")} onChange={(e) => setF({ ...s, secret: e.target.value })} />
            <Toggle label={t("handoff.include_cv")} checked={s.include_cv} onChange={(v) => setF({ ...s, include_cv: v })} />
            <Note>{t("handoff.how")}</Note>
          </div>
        </Card>
        <Card title={t("handoff.recent")}>
          <p className="mb-3 text-sm text-aeblack-600">{t("handoff.waiting", { n: s.waiting })}</p>
          <ul className="space-y-2 text-sm">
            {s.recent.map((o) => (
              <li key={o.id} className="flex justify-between gap-2" title={o.error}>
                <span><span className="font-semibold">{o.name}</span> <span className="text-xs text-aeblack-500" dir="ltr">{o.code}</span></span>
                {o.handed_off_at ? <Badge tone="green">{date(o.handed_off_at)}</Badge> : <Badge tone={o.error ? "red" : "gray"}>{t(o.error ? "handoff.failed_short" : "handoff.not_sent")}</Badge>}
              </li>
            ))}
          </ul>
        </Card>
      </div>
    </>
  );
}

/** Database backups in the bucket: the newest first, and taking one now. */
function Backups() {
  const { t, date } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<{ configured: boolean; interval_hours: number; items: { key: string; size: number; modified: string }[] }>("/backups"), []);
  const { busy, error, run } = useSaving();
  const now = async () => {
    if (await run(() => api("/backups", { body: {} })) !== undefined) { toast(t("backup.done")); data.reload(); }
  };
  if (!data.data) return null;
  const b = data.data;
  return (
    <Card title={t("backup.title")} className="mt-6" actions={b.configured && <Button variant="outline" size="sm" busy={busy} onClick={now}>{t("backup.now")}</Button>}>
      {error && <div className="mb-3"><ErrorState error={error} /></div>}
      {!b.configured ? <Note tone="warning">{t("backup.off")}</Note> : (
        <>
          <p className="mb-3 text-sm text-aeblack-600">{t("backup.every", { n: b.interval_hours })}</p>
          {b.items.length === 0 ? <p className="text-sm text-aeblack-500">{t("backup.none")}</p> : (
            <ul className="divide-y divide-aeblack-100 text-sm">
              {b.items.slice(0, 10).map((x) => (
                <li key={x.key} className="flex justify-between gap-3 py-2">
                  <span>{date(x.modified, true)}</span>
                  <span className="tabular-nums text-aeblack-500" dir="ltr">{(x.size / 1024 / 1024).toFixed(1)} MB</span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </Card>
  );
}
