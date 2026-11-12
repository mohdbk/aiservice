import { Plus, Trash } from "@phosphor-icons/react";
import { useState } from "react";
import {
  Badge, Button, Card, Empty, ErrorState, FlagLabel, Loading, Modal, PageHeader, Row, RowList, SelectInput, TemplateBody, TextInput, Toggle, textToHtml, useSaving,
} from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Kind = { key: string; name_ar: string; name_en: string; placeholders: string[] };
type Template = {
  id?: number; kind: string; name_ar: string; name_en: string; body_ar: string; body_en: string; body_html_ar: string; body_html_en: string;
  is_default: boolean; is_active: boolean; row_version?: string; designed?: boolean;
};

/**
 * Document templates of every kind (offer letters, interview letters, letters for applicants): plain text in Arabic and
 * English, optionally a designed HTML version. Documents are made from them on the offer, interview or applicant page.
 */
export default function DocumentTemplates() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("nav.document_templates"));
  const toast = useToast();
  const list = useAsync(() => api<{ items: Template[]; kinds: Kind[] }>("/document-templates"), []);
  const [f, setF] = useState<Template | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!f) return;
    const body = { ...f, body_html_ar: f.designed ? f.body_html_ar : "", body_html_en: f.designed ? f.body_html_en : "" };
    if (await run(() => api(f.id ? `/document-templates/${f.id}` : "/document-templates", { method: f.id ? "PUT" : "POST", body })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  const remove = async () => {
    if (!f?.id || !window.confirm(t("otpl.delete_confirm"))) return;
    if (await run(() => api(`/document-templates/${f.id}`, { method: "DELETE" })) !== undefined) { setF(null); list.reload(); }
  };
  if (list.loading && !list.data) return <Loading />;
  if (list.error) return <ErrorState error={list.error} retry={list.reload} />;
  const items = list.data?.items || [];
  const kinds = list.data?.kinds || [];
  const kind = (k: string) => kinds.find((x) => x.key === k);
  const blank = (k: string): Template => ({ kind: k, name_ar: "", name_en: "", body_ar: "", body_en: "", body_html_ar: "", body_html_en: "",
    is_default: !items.some((x) => x.kind === k), is_active: true });
  const placeholders = f ? kind(f.kind)?.placeholders || [] : [];
  return (
    <>
      <PageHeader title={t("nav.document_templates")} subtitle={t("otpl.intro")} actions={
        <Button onClick={() => setF(blank(kinds[0]?.key || "OfferLetter"))} icon={<Plus size={24} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <div className="space-y-6">
        {kinds.map((k) => {
          const rows = items.filter((x) => x.kind === k.key);
          return (
            <Card key={k.key} title={en ? k.name_en : k.name_ar} actions={<Button variant="link" size="sm" onClick={() => setF(blank(k.key))} icon={<Plus size={20} aria-hidden="true" />}>{t("common.add")}</Button>}>
              <p className="mb-4 text-sm text-aeblack-500">{t(`otpl.kind_hint.${k.key}`)}</p>
              {rows.length === 0 ? <Empty /> : (
                <RowList>
                  {rows.map((x) => (
                    <Row key={x.id} title={en ? x.name_en : x.name_ar} onOpen={() => setF({ ...x, designed: !!(x.body_html_ar || x.body_html_en) })} aside={<>
                      {(x.body_html_ar || x.body_html_en) && <Badge tone="blue">HTML</Badge>}
                      {x.is_default && <Badge tone="gold">{t("otpl.default")}</Badge>}
                      {!x.is_active && <Badge>{t("common.inactive")}</Badge>}
                    </>} />
                  ))}
                </RowList>
              )}
            </Card>
          );
        })}
      </div>
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? (en ? f.name_en : f.name_ar) : t("common.add")} footer={<>
          {f.id && <Button variant="outline" onClick={remove} icon={<Trash size={24} aria-hidden="true" />}>{t("common.delete")}</Button>}
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} onClick={save}>{t("common.save")}</Button>
        </>}>
          {error && <ErrorState error={error} />}
          <SelectInput required label={t("otpl.kind")} value={f.kind} disabled={!!f.id} onChange={(e) => setF({ ...f, kind: e.target.value })}
            options={kinds.map((k) => ({ value: k.key, label: en ? k.name_en : k.name_ar }))} />
          <div className="grid gap-3 sm:grid-cols-2">
            <TextInput required label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={f.name_ar} onChange={(e) => setF({ ...f, name_ar: e.target.value })} />
            <TextInput required label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} dir="ltr" value={f.name_en} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          </div>
          <div className="flex flex-wrap gap-6">
            <Toggle label={t("common.active")} checked={f.is_active} onChange={(v) => setF({ ...f, is_active: v, is_default: v && f.is_default })} />
            <Toggle label={t("otpl.default")} hint={t("otpl.default_hint")} checked={f.is_default} onChange={(v) => setF({ ...f, is_default: v, is_active: v || f.is_active })} />
            <Toggle label={t("mail.designed")} hint={t("otpl.designed_hint")} checked={!!f.designed} onChange={(v) => setF({
              ...f, designed: v, body_html_ar: v && !f.body_html_ar ? textToHtml(f.body_ar) : f.body_html_ar, body_html_en: v && !f.body_html_en ? textToHtml(f.body_en) : f.body_html_en,
            })} />
          </div>
          <div className="grid gap-6 xl:grid-cols-2">
            {(["ar", "en"] as const).map((l) => (
              <TemplateBody key={l} lang={l} designed={!!f.designed} placeholders={placeholders} rows={16}
                text={l === "ar" ? f.body_ar : f.body_en} html={l === "ar" ? f.body_html_ar : f.body_html_en}
                onText={(v) => setF((x) => x && (l === "ar" ? { ...x, body_ar: v } : { ...x, body_en: v }))}
                onHtml={(v) => setF((x) => x && (l === "ar" ? { ...x, body_html_ar: v } : { ...x, body_html_en: v }))}
                textLabel={<FlagLabel lang={l}>{t(f.designed ? `otpl.text_${l}` : `otpl.body_${l}`)}</FlagLabel>}
                htmlLabel={<FlagLabel lang={l}>{t(`otpl.html_${l}`)}</FlagLabel>} hint={t(f.designed ? "otpl.text_hint" : "otpl.body_hint")} />
            ))}
          </div>
        </Modal>
      )}
    </>
  );
}
