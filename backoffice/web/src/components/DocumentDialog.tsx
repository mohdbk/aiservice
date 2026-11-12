import { Printer } from "@phosphor-icons/react";
import { useState } from "react";
import { api, useAsync, useLang } from "../core";
import { Bilingual, Button, ErrorState, LangSwitch, Loading, Modal, SelectInput } from "./ui";

type Doc = { template_id: number | null; template: string; lang: string; html: string };
type Tpl = { id: number; name_ar: string; name_en: string; is_default: boolean };

/** Prints HTML through a hidden frame (the browser's print window also offers "Save as PDF"). */
export function printHtml(html: string, lang: string, title: string) {
  const frame = document.createElement("iframe");
  frame.style.position = "fixed"; frame.style.width = "0"; frame.style.height = "0"; frame.style.border = "0";
  document.body.appendChild(frame);
  const doc = frame.contentDocument!;
  doc.open();
  doc.write(`<!doctype html><html lang="${lang}" dir="${lang === "ar" ? "rtl" : "ltr"}"><head><meta charset="utf-8"><title>${title}</title>
    <style>@page{margin:24mm} body{margin:0} div{max-width:none!important}</style></head><body>${html}</body></html>`);
  doc.close();
  frame.contentWindow!.focus();
  frame.contentWindow!.print();
  setTimeout(() => frame.remove(), 1000);
}

/**
 * A document made from a template for one record (an offer, an interview, an applicant): pick the template and language,
 * preview it, then print or save it as PDF. It opens in the candidate's language.
 */
export function DocumentDialog({ kind, recordId, title, onClose, chooseTemplate = true }: {
  kind: string; recordId: number; title: string; onClose: () => void; chooseTemplate?: boolean;
}) {
  const { t, lang } = useLang();
  const [l, setL] = useState<Bilingual | "">("");
  const [template, setTemplate] = useState<number | "">("");
  const templates = useAsync(() => (chooseTemplate ? api<{ items: Tpl[] }>(`/document-templates?kind=${kind}&active=true`) : Promise.resolve({ items: [] as Tpl[] })), [kind]);
  const res = useAsync(() => api<Doc>(`/documents/${kind}/${recordId}?${new URLSearchParams({ ...(l ? { lang: l } : {}), ...(template ? { template: String(template) } : {}) })}`),
    [kind, recordId, l, template]);
  const shown = (res.data?.lang || l || lang) as Bilingual;
  const choices = templates.data?.items || [];
  return (
    <Modal open onClose={onClose} title={title} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.close")}</Button>
      <Button onClick={() => res.data && printHtml(res.data.html, res.data.lang, title)} disabled={!res.data?.html} icon={<Printer size={24} aria-hidden="true" />}>{t("offer.print")}</Button>
    </>}>
      <div className="flex flex-wrap items-end justify-between gap-3">
        {choices.length > 1
          ? <SelectInput wrapClass="min-w-56 flex-1" label={t("otpl.template")} value={template || res.data?.template_id || ""} onChange={(e) => setTemplate(e.target.value ? Number(e.target.value) : "")}
              options={choices.map((x) => ({ value: x.id, label: lang === "en" ? x.name_en : x.name_ar }))} />
          : <span className="text-sm text-aeblack-500">{res.data?.template}</span>}
        <LangSwitch value={shown} onChange={setL} />
      </div>
      {res.loading && !res.data ? <Loading /> : res.error ? <ErrorState error={res.error} /> : !res.data?.html ? <p className="text-sm text-aeblack-500">{t("otpl.none")}</p>
        : <div className="max-h-[60vh] overflow-auto rounded-xl border border-aeblack-100 bg-white p-5" dangerouslySetInnerHTML={{ __html: res.data.html }} />}
      <p className="text-xs text-aeblack-500">{t("offer.print_hint")}</p>
    </Modal>
  );
}
