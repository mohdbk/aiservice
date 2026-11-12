import { Plus, Trash } from "@phosphor-icons/react";
import { useState } from "react";
import { DataTable, opts, useList } from "../components/DataTable";
import { Badge, Bilingual, Button, EnglishBadge, ErrorState, LangTabs, Modal, PageHeader, SelectInput, TextArea, TextInput, Toggle, useSaving } from "../components/ui";
import { api, useLang, usePageTitle, useToast } from "../core";

type Row = { id: number; text: string; answer_type: string; category: string; is_active: boolean; english: string | null; options: string[] };
type Option = { id?: number; text: string; text_en: string };
type Form = { id?: number; text: string; text_en: string; english_status: string; english_approved: boolean; answer_type: string; category: string; is_active: boolean; options: Option[] };

const TYPES = ["YesNo", "SingleChoice", "MultipleChoice", "Number", "Date", "ShortText", "LongText"];
const EMPTY: Form = { text: "", text_en: "", english_status: "Missing", english_approved: false, answer_type: "YesNo", category: "", is_active: true, options: [] };

export default function Screening() {
  const { t } = useLang();
  usePageTitle(t("sq.title"));
  const toast = useToast();
  const list = useList<Row>("/screening-questions", ["type", "active", "english"]);
  const [form, setForm] = useState<Form | null>(null);
  const [lang, setLang] = useState<Bilingual>("ar");
  const { busy, error, run, setError } = useSaving();
  const suggest = useSaving();
  const open = async (id?: number) => {
    setError(null);
    setLang("ar");
    if (!id) return setForm({ ...EMPTY });
    const f = await run(() => api<Form>(`/screening-questions/${id}`));
    if (f) setForm(f);
  };
  const save = async () => {
    if (!form) return;
    const r = await run(() => api(form.id ? `/screening-questions/${form.id}` : "/screening-questions", { method: form.id ? "PUT" : "POST", body: form }));
    if (r !== undefined) {
      toast(t("common.saved"));
      setForm(null);
      list.reload();
    }
  };
  const set = <K extends keyof Form>(k: K, v: Form[K]) => setForm((f) => (f ? { ...f, [k]: v } : f));
  const hasOptions = form && (form.answer_type === "SingleChoice" || form.answer_type === "MultipleChoice");
  return (
    <>
      <PageHeader title={t("sq.title")} actions={<Button onClick={() => open()} icon={<Plus size={20} aria-hidden="true" />}>{t("sq.new")}</Button>} />
      <DataTable list={list} rowKey={(x) => x.id}
        filters={[
          { key: "type", label: t("sq.answer_type"), options: opts(t, "at.", TYPES) },
          { key: "active", label: t("q.active_filter"), options: [{ value: "true", label: t("common.active") }, { value: "false", label: t("common.inactive") }] },
          { key: "english", label: t("common.english_state"), options: opts(t, "english.", ["Missing", "Suggested", "Approved"]) },
        ]}
        columns={[
          { key: "text", label: t("sq.text_ar"), sort: "text", render: (x) => <>
            <button type="button" className="text-start font-semibold text-primary-700" onClick={() => open(x.id)}>{x.text}</button>
            {x.options.length > 0 && <p className="text-xs text-aeblack-500">{x.options.join(" · ")}</p>}
            {!x.is_active && <Badge>{t("common.inactive")}</Badge>}
          </> },
          { key: "type", label: t("sq.answer_type"), sort: "type", render: (x) => t(`at.${x.answer_type}`) },
          { key: "cat", label: t("sq.category"), sort: "category", render: (x) => x.category },
          { key: "en", label: t("common.english"), render: (x) => <EnglishBadge state={x.english || "Missing"} /> },
        ]} />
      {form && (
        <Modal open onClose={() => setForm(null)} title={form.id ? t("common.edit") : t("sq.new")} footer={<>
          <Button variant="outline" onClick={() => setForm(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} onClick={save}>{t("common.save")}</Button>
        </>}>
          {(error || suggest.error) && <ErrorState error={error || suggest.error || undefined} />}
          <LangTabs value={lang} onChange={setLang} english={form.english_status} approved={form.english_approved} onApproved={(v) => set("english_approved", v)}
            onSuggest={form.id ? async () => { if (await suggest.run(() => api(`/suggest-english/screening/${form.id}`, { body: {} })) !== undefined) toast(t("common.queued"), "info"); } : undefined}
            suggesting={suggest.busy}>
            <div className="space-y-4" dir={lang === "ar" ? "rtl" : "ltr"} lang={lang}>
              <TextArea label={lang === "ar" ? t("sq.text_ar") : t("sq.text_en")} rows={2} value={lang === "ar" ? form.text : form.text_en}
                onChange={(e) => set(lang === "ar" ? "text" : "text_en", e.target.value)} />
              {hasOptions && (
                <fieldset>
                  <legend className="mb-2 text-sm font-semibold">{t("sq.options")}</legend>
                  <div className="space-y-2">
                    {form.options.map((o, i) => (
                      <div key={i} className="flex items-end gap-2">
                        <TextInput wrapClass="flex-1" label={`${lang === "ar" ? t("q.option_ar") : t("q.option_en")} ${i + 1}`} value={lang === "ar" ? o.text : o.text_en}
                          onChange={(e) => set("options", form.options.map((x, k) => (k === i ? { ...x, [lang === "ar" ? "text" : "text_en"]: e.target.value } : x)))} />
                        {lang === "ar" && <Button variant="link" className="pb-3" aria-label={t("common.remove")} onClick={() => set("options", form.options.filter((_, k) => k !== i))}><Trash size={20} /></Button>}
                      </div>
                    ))}
                  </div>
                  {lang === "ar" && <Button variant="outline" size="sm" className="mt-2" onClick={() => set("options", [...form.options, { text: "", text_en: "" }])} icon={<Plus size={20} aria-hidden="true" />}>{t("q.add_option")}</Button>}
                </fieldset>
              )}
            </div>
          </LangTabs>
          <div className="grid gap-3 sm:grid-cols-2">
            <SelectInput label={t("sq.answer_type")} value={form.answer_type} onChange={(e) => {
              const v = e.target.value;
              setForm((f) => (f ? { ...f, answer_type: v, options: (v === "SingleChoice" || v === "MultipleChoice") && f.options.length === 0 ? [{ text: "", text_en: "" }, { text: "", text_en: "" }] : f.options } : f));
            }} options={opts(t, "at.", TYPES)} />
            <TextInput label={t("sq.category")} value={form.category} onChange={(e) => set("category", e.target.value)} />
          </div>
          <Toggle label={t("common.active")} checked={form.is_active} onChange={(v) => set("is_active", v)} />
        </Modal>
      )}
    </>
  );
}
