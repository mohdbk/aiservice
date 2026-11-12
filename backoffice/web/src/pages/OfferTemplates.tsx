import { Plus, Trash } from "@phosphor-icons/react";
import { useRef, useState } from "react";
import { LangFlag } from "../components/flags";
import { Badge, Button, Card, Empty, ErrorState, Loading, Modal, PageHeader, TextArea, TextInput, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Template = { id?: number; name_ar: string; name_en: string; body_ar: string; body_en: string; is_default: boolean; is_active: boolean; row_version?: string };

/** One language of the letter: the text, placeholder chips that insert at the cursor, and a preview with headings. */
function LetterText({ lang, value, placeholders, onChange }: { lang: "ar" | "en"; value: string; placeholders: string[]; onChange: (v: string) => void }) {
  const { t } = useLang();
  const ref = useRef<HTMLTextAreaElement>(null);
  const ar = lang === "ar";
  const insert = (p: string) => {
    const el = ref.current, token = `{${p}}`;
    if (!el) return onChange(value + token);
    const start = el.selectionStart ?? value.length, end = el.selectionEnd ?? value.length;
    onChange(value.slice(0, start) + token + value.slice(end));
    requestAnimationFrame(() => { el.focus(); el.setSelectionRange(start + token.length, start + token.length); });
  };
  return (
    <div className="space-y-3">
      <TextArea ref={ref} label={<span className="inline-flex items-center gap-2"><LangFlag lang={lang} />{t(ar ? "otpl.body_ar" : "otpl.body_en")}</span>}
        hint={t("otpl.body_hint")} dir={ar ? "rtl" : "ltr"} rows={16} value={value} onChange={(e) => onChange(e.target.value)} />
      <div className="flex flex-wrap gap-1.5">
        {placeholders.map((p) => (
          <button key={p} type="button" onClick={() => insert(p)} dir="ltr" className="rounded-md bg-aeblack-50 px-2 py-1 font-mono text-xs text-aeblack-700 hover:bg-primary-50">{`{${p}}`}</button>
        ))}
      </div>
      <div className="rounded-xl border border-aeblack-100 bg-white p-5 text-sm" dir={ar ? "rtl" : "ltr"} lang={lang}>
        <p className="mb-2 text-xs font-semibold text-aeblack-500">{t("mail.preview")}</p>
        {value.split(/\n\s*\n/).map((para, i) => para.trim().startsWith("# ")
          ? <h3 key={i} className="mb-3 text-lg font-bold">{para.trim().slice(2)}</h3>
          : <p key={i} className="mb-3 whitespace-pre-line">{para}</p>)}
      </div>
    </div>
  );
}

/** Offer letter templates: picked when making an offer, printed from the offer, and emailed when the offer is sent. */
export default function OfferTemplates() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("nav.offer_templates"));
  const toast = useToast();
  const list = useAsync(() => api<{ items: Template[]; placeholders: string[] }>("/offer-templates"), []);
  const [f, setF] = useState<Template | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!f) return;
    if (await run(() => api(f.id ? `/offer-templates/${f.id}` : "/offer-templates", { method: f.id ? "PUT" : "POST", body: f })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  const remove = async () => {
    if (!f?.id || !window.confirm(t("otpl.delete_confirm"))) return;
    if (await run(() => api(`/offer-templates/${f.id}`, { method: "DELETE" })) !== undefined) { setF(null); list.reload(); }
  };
  if (list.loading && !list.data) return <Loading />;
  if (list.error) return <ErrorState error={list.error} retry={list.reload} />;
  const items = list.data?.items || [];
  return (
    <>
      <PageHeader title={t("nav.offer_templates")} subtitle={t("otpl.intro")} actions={
        <Button onClick={() => setF({ name_ar: "", name_en: "", body_ar: "", body_en: "", is_default: items.length === 0, is_active: true })}
          icon={<Plus size={24} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <Card>
        {items.length === 0 ? <Empty /> : (
          <ul className="divide-y divide-aeblack-100">
            {items.map((x) => (
              <li key={x.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
                <button type="button" className="font-semibold text-primary-700" onClick={() => setF({ ...x })}>{en ? x.name_en : x.name_ar}</button>
                <span className="flex gap-2">
                  {x.is_default && <Badge tone="gold">{t("otpl.default")}</Badge>}
                  {!x.is_active && <Badge>{t("common.inactive")}</Badge>}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? (en ? f.name_en : f.name_ar) : t("common.add")} footer={<>
          {f.id && <Button variant="outline" onClick={remove} icon={<Trash size={24} aria-hidden="true" />}>{t("common.delete")}</Button>}
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} onClick={save}>{t("common.save")}</Button>
        </>}>
          {error && <ErrorState error={error} />}
          <div className="grid gap-3 sm:grid-cols-2">
            <TextInput label={<span className="inline-flex items-center gap-2"><LangFlag lang="ar" />{t("setup.name_ar")}</span>} dir="rtl" value={f.name_ar} onChange={(e) => setF({ ...f, name_ar: e.target.value })} />
            <TextInput label={<span className="inline-flex items-center gap-2"><LangFlag lang="en" />{t("setup.name_en")}</span>} dir="ltr" value={f.name_en} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          </div>
          <div className="flex flex-wrap gap-6">
            <Toggle label={t("common.active")} checked={f.is_active} onChange={(v) => setF({ ...f, is_active: v, is_default: v && f.is_default })} />
            <Toggle label={t("otpl.default")} hint={t("otpl.default_hint")} checked={f.is_default} onChange={(v) => setF({ ...f, is_default: v, is_active: v || f.is_active })} />
          </div>
          <div className="grid gap-6 xl:grid-cols-2">
            <LetterText lang="ar" value={f.body_ar} placeholders={list.data?.placeholders || []} onChange={(v) => setF({ ...f, body_ar: v })} />
            <LetterText lang="en" value={f.body_en} placeholders={list.data?.placeholders || []} onChange={(v) => setF({ ...f, body_en: v })} />
          </div>
        </Modal>
      )}
    </>
  );
}
