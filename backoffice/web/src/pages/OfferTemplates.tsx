import { Plus, Trash } from "@phosphor-icons/react";
import { useState } from "react";
import { Badge, Button, Card, Empty, ErrorState, FlagLabel, Loading, Modal, PageHeader, PlaceholderText, Row, RowList, TextInput, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Template = { id?: number; name_ar: string; name_en: string; body_ar: string; body_en: string; is_default: boolean; is_active: boolean; row_version?: string };

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
          <RowList>
            {items.map((x) => (
              <Row key={x.id} title={en ? x.name_en : x.name_ar} onOpen={() => setF({ ...x })} aside={<>
                {x.is_default && <Badge tone="gold">{t("otpl.default")}</Badge>}
                {!x.is_active && <Badge>{t("common.inactive")}</Badge>}
              </>} />
            ))}
          </RowList>
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
            <TextInput label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={f.name_ar} onChange={(e) => setF({ ...f, name_ar: e.target.value })} />
            <TextInput label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} dir="ltr" value={f.name_en} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          </div>
          <div className="flex flex-wrap gap-6">
            <Toggle label={t("common.active")} checked={f.is_active} onChange={(v) => setF({ ...f, is_active: v, is_default: v && f.is_default })} />
            <Toggle label={t("otpl.default")} hint={t("otpl.default_hint")} checked={f.is_default} onChange={(v) => setF({ ...f, is_default: v, is_active: v || f.is_active })} />
          </div>
          <div className="grid gap-6 xl:grid-cols-2">
            <PlaceholderText label={<FlagLabel lang="ar">{t("otpl.body_ar")}</FlagLabel>} hint={t("otpl.body_hint")} lang="ar" rows={16} value={f.body_ar}
              placeholders={list.data?.placeholders || []} onChange={(v) => setF({ ...f, body_ar: v })} />
            <PlaceholderText label={<FlagLabel lang="en">{t("otpl.body_en")}</FlagLabel>} hint={t("otpl.body_hint")} lang="en" rows={16} value={f.body_en}
              placeholders={list.data?.placeholders || []} onChange={(v) => setF({ ...f, body_en: v })} />
          </div>
        </Modal>
      )}
    </>
  );
}
