import { ArrowClockwise, EnvelopeSimple, FloppyDisk, PaperPlaneTilt, Plus, Trash } from "@phosphor-icons/react";
import { useEffect, useRef, useState } from "react";
import { DataTable, useList } from "../components/DataTable";
import { LangFlag } from "../components/flags";
import { ActionMenu, Badge, Button, Card, Empty, ErrorState, Loading, Modal, Note, PageHeader, SectionTabs, TextArea, TextInput, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useAuth, useLang, usePageTitle, useToast } from "../core";

type TestResult = { ok: boolean; server: string; error?: string };

/** Asks for an address and sends a test email; shows the server's answer. */
function TestEmail({ path, open, onClose }: { path: string; open: boolean; onClose: () => void }) {
  const { t, lang } = useLang();
  const { me } = useAuth();
  const [to, setTo] = useState(me?.email || "");
  const [l, setL] = useState(lang);
  const [result, setResult] = useState<TestResult | null>(null);
  const { busy, error, run } = useSaving();
  const send = async () => {
    setResult(null);
    const r = await run(() => api<TestResult>(path, { body: { to, lang: l } }));
    if (r) setResult(r);
  };
  return (
    <Modal open={open} onClose={onClose} title={t("mail.test")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.close")}</Button>
      <Button busy={busy} disabled={!to} onClick={send} icon={<PaperPlaneTilt size={24} aria-hidden="true" />}>{t("mail.send")}</Button>
    </>}>
      {error && <ErrorState error={error} />}
      <TextInput label={t("mail.test_to")} type="email" dir="ltr" value={to} onChange={(e) => setTo(e.target.value)} />
      <div className="flex gap-2">
        {["ar", "en"].map((x) => (
          <button key={x} type="button" aria-pressed={l === x} onClick={() => setL(x as typeof lang)}
            className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-semibold ${l === x ? "border-primary-600 bg-primary-50 text-primary-800" : "border-aeblack-200"}`}>
            <LangFlag lang={x} />{x === "ar" ? "العربية" : "English"}</button>
        ))}
      </div>
      {result && (result.ok ? <Note>{t("mail.test_ok", { server: result.server })}</Note>
        : <Note tone="warning">{t("mail.test_failed", { server: result.server })} <span dir="ltr" className="block break-all font-mono text-xs">{result.error}</span></Note>)}
    </Modal>
  );
}

// ---------------- email server (administrators) ----------------

type Server = {
  host: string; port: number; use_tls: boolean; user_name: string; has_password: boolean; can_store_password: boolean; from_address: string; from_name: string;
  signature_ar: string; signature_en: string; in_use: "Settings" | "Environment" | "None"; environment_host: string | null;
};

export function EmailServer() {
  const { t } = useLang();
  usePageTitle(t("nav.email_server"));
  const toast = useToast();
  const loaded = useAsync(() => api<Server>("/email-settings"), []);
  const [s, setS] = useState<Server | null>(null);
  const [password, setPassword] = useState("");
  const [clear, setClear] = useState(false);
  const [testing, setTesting] = useState(false);
  const { busy, error, run } = useSaving();
  useEffect(() => { if (loaded.data) setS(loaded.data); }, [loaded.data]);
  if (loaded.loading || !s) return loaded.error ? <ErrorState error={loaded.error} retry={loaded.reload} /> : <Loading />;
  const set = <K extends keyof Server>(k: K, v: Server[K]) => setS({ ...s, [k]: v });
  const save = async () => {
    const body = { host: s.host, port: s.port, use_tls: s.use_tls, user_name: s.user_name, password: password || null, clear_password: clear,
      from_address: s.from_address, from_name: s.from_name, signature_ar: s.signature_ar, signature_en: s.signature_en };
    if (await run(() => api("/email-settings", { method: "PUT", body })) !== undefined) {
      toast(t("common.saved"));
      setPassword("");
      setClear(false);
      loaded.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("nav.email_server")} subtitle={t("mail.server_intro")} actions={<>
        <Button variant="outline" onClick={() => setTesting(true)} disabled={s.in_use === "None"} icon={<PaperPlaneTilt size={24} aria-hidden="true" />}>{t("mail.test")}</Button>
        <Button busy={busy} onClick={save} icon={<FloppyDisk size={24} aria-hidden="true" />}>{t("common.save")}</Button>
      </>} />
      {error && <div className="mb-4"><ErrorState error={error} /></div>}
      <div className="mb-6">
        {s.in_use === "Settings" && <Note>{t("mail.in_use_settings")}</Note>}
        {s.in_use === "Environment" && <Note>{t("mail.in_use_env", { host: s.environment_host || "" })}</Note>}
        {s.in_use === "None" && <Note tone="warning">{t("mail.in_use_none")}</Note>}
      </div>
      <div className="grid gap-6 lg:grid-cols-2">
        <Card title={t("mail.server")}>
          <div className="space-y-4">
            <TextInput label={t("mail.host")} hint={t("mail.host_hint")} dir="ltr" value={s.host} onChange={(e) => set("host", e.target.value)} />
            <div className="grid grid-cols-2 gap-3">
              <TextInput label={t("mail.port")} type="number" min={1} max={65535} dir="ltr" value={s.port} onChange={(e) => set("port", Number(e.target.value) || 0)} />
              <div className="pt-8"><Toggle label={t("mail.tls")} checked={s.use_tls} onChange={(v) => set("use_tls", v)} /></div>
            </div>
            <TextInput label={t("mail.user")} dir="ltr" autoComplete="off" value={s.user_name} onChange={(e) => set("user_name", e.target.value)} />
            <TextInput label={t("mail.password")} type="password" dir="ltr" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)}
              hint={!s.can_store_password ? t("mail.cannot_store") : s.has_password ? t("mail.password_kept") : undefined} disabled={!s.can_store_password} />
            {s.has_password && <Toggle label={t("mail.clear_password")} checked={clear} onChange={setClear} />}
          </div>
        </Card>
        <Card title={t("mail.sender")}>
          <div className="space-y-4">
            <TextInput label={t("mail.from_address")} type="email" dir="ltr" value={s.from_address} onChange={(e) => set("from_address", e.target.value)} />
            <TextInput label={t("mail.from_name")} value={s.from_name} onChange={(e) => set("from_name", e.target.value)} />
            <TextArea label={<span className="inline-flex items-center gap-2"><LangFlag lang="ar" />{t("mail.signature_ar")}</span>} dir="rtl" rows={3} value={s.signature_ar} onChange={(e) => set("signature_ar", e.target.value)} />
            <TextArea label={<span className="inline-flex items-center gap-2"><LangFlag lang="en" />{t("mail.signature_en")}</span>} dir="ltr" rows={3} value={s.signature_en} onChange={(e) => set("signature_en", e.target.value)} />
          </div>
        </Card>
      </div>
      <TestEmail path="/email-settings/test" open={testing} onClose={() => setTesting(false)} />
    </>
  );
}

// ---------------- email templates (recruitment admins) ----------------

type TemplateRow = { id: number; key: string; event: string | null; name_ar: string; name_en: string; subject_ar: string; subject_en: string; is_enabled: boolean; required: boolean };
type Template = {
  id?: number; key: string; event: string | null; name_ar: string; name_en: string; subject_ar: string; body_ar: string; subject_en: string; body_en: string;
  is_enabled: boolean; required: boolean; placeholders: string[]; row_version?: string;
};

const SAMPLE: Record<string, [string, string]> = {
  name: ["سارة أحمد", "Sara Ahmed"], job_title: ["محلل بيانات", "Data Analyst"], reference: ["APP-2026-0042", "APP-2026-0042"], test: ["اختبار التحليل", "Analysis Test"],
  deadline: ["20/11/2026 17:00", "20/11/2026 17:00"], round: ["المقابلة الفنية", "Technical Interview"], date: ["2026-11-25", "2026-11-25"], time: ["10:00 - 11:00", "10:00 - 11:00"],
  location: ["مبنى الوزارة، دبي", "Ministry building, Dubai"], position_title: ["محلل بيانات أول", "Senior Data Analyst"], start_date: ["01/01/2027", "01/01/2027"],
  offer_letter: ["(خطاب العرض)", "(offer letter)"],
};

function fill(text: string, ar: boolean) {
  return text.replace(/\{([a-z_]+)\}/g, (m, k) => (k === "link" ? "[ ... ]" : SAMPLE[k] ? SAMPLE[k][ar ? 0 : 1] : m));
}

/** One language's subject and body, with placeholder chips that insert at the cursor and a live preview. */
function TemplateText({ lang, subject, body, placeholders, onSubject, onBody }: {
  lang: "ar" | "en"; subject: string; body: string; placeholders: string[]; onSubject: (v: string) => void; onBody: (v: string) => void;
}) {
  const { t } = useLang();
  const ref = useRef<HTMLTextAreaElement>(null);
  const ar = lang === "ar";
  const insert = (p: string) => {
    const el = ref.current;
    const token = `{${p}}`;
    if (!el) return onBody(body + token);
    const start = el.selectionStart ?? body.length, end = el.selectionEnd ?? body.length;
    onBody(body.slice(0, start) + token + body.slice(end));
    requestAnimationFrame(() => { el.focus(); el.setSelectionRange(start + token.length, start + token.length); });
  };
  return (
    <div className="space-y-3">
      <p className="inline-flex items-center gap-2 font-semibold"><LangFlag lang={lang} />{ar ? "العربية" : "English"}</p>
      <TextInput label={t("mail.subject")} dir={ar ? "rtl" : "ltr"} value={subject} onChange={(e) => onSubject(e.target.value)} />
      <TextArea ref={ref} label={t("mail.body")} hint={t("mail.body_hint")} dir={ar ? "rtl" : "ltr"} rows={10} value={body} onChange={(e) => onBody(e.target.value)} />
      <div className="flex flex-wrap gap-1.5" aria-label={t("mail.placeholders")}>
        {placeholders.map((p) => (
          <button key={p} type="button" onClick={() => insert(p)} dir="ltr" className="rounded-md bg-aeblack-50 px-2 py-1 font-mono text-xs text-aeblack-700 hover:bg-primary-50">{`{${p}}`}</button>
        ))}
      </div>
      <div className="rounded-xl border border-aeblack-100 bg-whitely-100 p-4 text-sm" dir={ar ? "rtl" : "ltr"} lang={lang}>
        <p className="mb-2 text-xs font-semibold text-aeblack-500">{t("mail.preview")}</p>
        <p className="mb-3 font-semibold">{fill(subject, ar)}</p>
        {fill(body, ar).split(/\n\s*\n/).map((para, i) => <p key={i} className="mb-3 whitespace-pre-line">{para}</p>)}
      </div>
    </div>
  );
}

export function EmailTemplates() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("nav.email_templates"));
  const toast = useToast();
  const list = useAsync(() => api<{ items: TemplateRow[]; email_ready: boolean }>("/email-templates"), []);
  const [f, setF] = useState<Template | null>(null);
  const [testing, setTesting] = useState(false);
  const { busy, error, run } = useSaving();
  const open = async (id: number) => { const x = await run(() => api<Template>(`/email-templates/${id}`)); if (x) setF(x); };
  const save = async () => {
    if (!f) return;
    if (await run(() => api(f.id ? `/email-templates/${f.id}` : "/email-templates", { method: f.id ? "PUT" : "POST", body: f })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  const remove = async () => {
    if (!f?.id || !window.confirm(t("mail.delete_confirm"))) return;
    if (await run(() => api(`/email-templates/${f.id}`, { method: "DELETE" })) !== undefined) { setF(null); list.reload(); }
  };
  if (list.loading && !list.data) return <Loading />;
  if (list.error) return <ErrorState error={list.error} retry={list.reload} />;
  const rows = list.data?.items || [];
  const section = (title: string, items: TemplateRow[], hint: string) => (
    <Card title={title}>
      <p className="mb-3 text-sm text-aeblack-500">{hint}</p>
      {items.length === 0 ? <Empty /> : (
        <ul className="divide-y divide-aeblack-100">
          {items.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
              <div className="min-w-0">
                <button type="button" className="font-semibold text-primary-700" onClick={() => open(r.id)}>{en ? r.name_en : r.name_ar}</button>
                <p className="truncate text-sm text-aeblack-600">{en ? r.subject_en : r.subject_ar}</p>
              </div>
              <Badge tone={r.is_enabled ? "green" : "gray"}>{r.is_enabled ? t("mail.on") : t("mail.off")}</Badge>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
  return (
    <>
      <PageHeader title={t("nav.email_templates")} subtitle={t("mail.templates_intro")} actions={
        <Button onClick={() => setF({ key: "", event: null, name_ar: "", name_en: "", subject_ar: "", body_ar: "", subject_en: "", body_en: "", is_enabled: true, required: false,
          placeholders: ["name", "job_title", "reference"] })} icon={<Plus size={24} aria-hidden="true" />}>{t("common.add")}</Button>} />
      {!list.data?.email_ready && <div className="mb-4"><Note tone="warning">{t("mail.not_ready")}</Note></div>}
      {error && !f && <div className="mb-4"><ErrorState error={error} /></div>}
      <div className="space-y-6">
        {section(t("mail.events"), rows.filter((r) => r.event), t("mail.events_hint"))}
        {section(t("mail.custom"), rows.filter((r) => !r.event), t("mail.custom_hint"))}
      </div>
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? (en ? f.name_en : f.name_ar) : t("common.add")} footer={<>
          {f.id && !f.event && <Button variant="outline" onClick={remove} icon={<Trash size={24} aria-hidden="true" />}>{t("common.delete")}</Button>}
          {f.id && <Button variant="outline" onClick={() => setTesting(true)} disabled={!list.data?.email_ready} icon={<PaperPlaneTilt size={24} aria-hidden="true" />}>{t("mail.test")}</Button>}
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} onClick={save}>{t("common.save")}</Button>
        </>}>
          {error && <ErrorState error={error} />}
          {f.event && <Note>{t(`mail.event_hint.${f.event}`)}</Note>}
          <div className="grid gap-3 sm:grid-cols-2">
            <TextInput label={<span className="inline-flex items-center gap-2"><LangFlag lang="ar" />{t("setup.name_ar")}</span>} dir="rtl" value={f.name_ar} onChange={(e) => setF({ ...f, name_ar: e.target.value })} />
            <TextInput label={<span className="inline-flex items-center gap-2"><LangFlag lang="en" />{t("setup.name_en")}</span>} dir="ltr" value={f.name_en} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          </div>
          {!f.event && <TextInput label={t("mail.key")} hint={t("mail.key_hint")} dir="ltr" value={f.key} onChange={(e) => setF({ ...f, key: e.target.value })} />}
          <Toggle label={t("mail.enabled")} hint={f.required ? t("mail.required_hint") : undefined} checked={f.is_enabled} onChange={(v) => !f.required && setF({ ...f, is_enabled: v })} />
          <div className="grid gap-6 xl:grid-cols-2">
            <TemplateText lang="ar" subject={f.subject_ar} body={f.body_ar} placeholders={f.placeholders}
              onSubject={(v) => setF({ ...f, subject_ar: v })} onBody={(v) => setF({ ...f, body_ar: v })} />
            <TemplateText lang="en" subject={f.subject_en} body={f.body_en} placeholders={f.placeholders}
              onSubject={(v) => setF({ ...f, subject_en: v })} onBody={(v) => setF({ ...f, body_en: v })} />
          </div>
        </Modal>
      )}
      {f?.id && <TestEmail path={`/email-templates/${f.id}/test`} open={testing} onClose={() => setTesting(false)} />}
    </>
  );
}

// ---------------- email log (administrators) ----------------

type LogRow = { id: number; to: string; subject: string; status: string; attempts: number; last_error: string; created_at: string; sent_at: string | null };

export function EmailLog() {
  const { t, date } = useLang();
  usePageTitle(t("nav.email_log"));
  const toast = useToast();
  const list = useList<LogRow>("/email-log", ["status"]);
  const status = list.get("status");
  const [shown, setShown] = useState<{ subject: string; to: string; html: string } | null>(null);
  const act = useSaving();
  const retry = async (id: number) => {
    if (await act.run(() => api(`/email-log/${id}/retry`, { body: {} })) !== undefined) { toast(t("mail.requeued")); list.reload(); }
  };
  const view = async (id: number) => { const m = await act.run(() => api<{ subject: string; to: string; html: string }>(`/email-log/${id}`)); if (m) setShown(m); };
  const tone = (s: string) => (s === "Sent" ? "green" : s === "Failed" ? "red" : "amber");
  return (
    <>
      <PageHeader title={t("nav.email_log")} subtitle={t("mail.log_intro")} />
      <SectionTabs value={status} onChange={(v) => list.set({ status: v || null })}
        tabs={[{ value: "", label: t("common.all") }, ...["Queued", "Sent", "Failed"].map((s) => ({ value: s, label: t(`mail.status.${s}`) }))]} />
      {act.error && <div className="mb-4"><ErrorState error={act.error} /></div>}
      <DataTable list={list} rowKey={(m) => m.id} columns={[
        { key: "subject", label: t("mail.subject"), render: (m) => <button type="button" className="text-start font-semibold text-primary-700" onClick={() => view(m.id)}>{m.subject}</button> },
        { key: "to", label: t("mail.to"), sort: "to", render: (m) => <span dir="ltr">{m.to}</span> },
        { key: "created", label: t("mail.queued_at"), sort: "created", className: "whitespace-nowrap", render: (m) => date(m.created_at, true) },
        { key: "status", label: t("common.status"), sort: "status", render: (m) => <>
          <Badge tone={tone(m.status)}>{t(`mail.status.${m.status}`)}</Badge>
          {m.last_error && m.status !== "Sent" && <p className="mt-1 max-w-xs truncate text-xs text-aeblack-500" dir="ltr" title={m.last_error}>{m.last_error}</p>}
        </> },
        { key: "attempts", label: t("mail.attempts"), sort: "attempts", render: (m) => <span className="tabular-nums" dir="ltr">{m.attempts}</span> },
        { key: "act", label: t("common.actions"), render: (m) => m.status === "Failed" &&
          <ActionMenu label={t("common.actions")} icon={<ArrowClockwise size={20} aria-hidden="true" />} items={[{ label: t("mail.retry"), onClick: () => retry(m.id) }]} /> },
      ]} />
      {shown && (
        <Modal open onClose={() => setShown(null)} title={shown.subject} footer={<Button variant="outline" onClick={() => setShown(null)}>{t("common.close")}</Button>}>
          <p className="inline-flex items-center gap-2 text-sm text-aeblack-600"><EnvelopeSimple size={24} aria-hidden="true" /><span dir="ltr">{shown.to}</span></p>
          <div className="max-h-[60vh] overflow-auto rounded-xl border border-aeblack-100 bg-white p-4" dangerouslySetInnerHTML={{ __html: shown.html }} />
        </Modal>
      )}
    </>
  );
}
