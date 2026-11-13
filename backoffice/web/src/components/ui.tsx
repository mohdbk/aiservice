import { Checkbox, Dialog, Menu, Switch, Tabs } from "@base-ui/react";
import {
  ArrowClockwise, CaretDown, Check, CircleNotch, Info, ListBullets, ListNumbers, MagicWand, Paragraph, TextB, TextH, TextItalic, Warning, X,
} from "@phosphor-icons/react";
import { ReactNode, forwardRef, useEffect, useId, useRef, useState } from "react";
import { ApiError, useLang } from "../core";
import { FlagUae, FlagUs, LangFlag } from "./flags";

// ---------------- buttons and layout ----------------
type BtnProps = React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "outline" | "link" | "danger"; size?: "sm" | "md"; busy?: boolean; icon?: ReactNode };

export function Button({ variant = "primary", size = "md", busy, icon, children, className = "", ...rest }: BtnProps) {
  const v = variant === "outline" ? "btn-outline" : variant === "link" ? "btn-link" : variant === "danger" ? "btn-outline !text-aered-700 !border-aered-300" : "";
  return (
    <button type="button" {...rest} disabled={rest.disabled || busy} className={`aegov-btn ${v} ${size === "sm" ? "btn-sm" : ""} ${className}`}>
      {busy ? <CircleNotch size={20} className="animate-spin" aria-hidden="true" /> : icon}
      {children}
    </button>
  );
}

export function PageHeader({ title, subtitle, actions, back }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; back?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        {back}
        <h1 className="text-2xl font-bold text-aeblack-900 sm:text-3xl">{title}</h1>
        {subtitle && <div className="mt-1 text-aeblack-600">{subtitle}</div>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}

export function Card({ title, actions, children, className = "" }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`mc-surface rounded-2xl border border-aeblack-100 bg-white p-5 sm:p-6 ${className}`}>
      {(title || actions) && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          {title && <h2 className="text-lg font-bold text-aeblack-900">{title}</h2>}
          {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

const TONES: Record<string, string> = {
  gray: "bg-aeblack-100 text-aeblack-700", gold: "bg-primary-100 text-primary-800", green: "bg-aegreen-100 text-aegreen-800",
  red: "bg-aered-100 text-aered-800", blue: "bg-techblue-100 text-techblue-800", amber: "bg-camel-100 text-camel-800",
};

export function Badge({ tone = "gray", children }: { tone?: keyof typeof TONES | string; children: ReactNode }) {
  return <span className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold ${TONES[tone] || TONES.gray}`}>{children}</span>;
}

export function EnglishBadge({ state }: { state: string }) {
  const { t } = useLang();
  return <Badge tone={state === "Approved" ? "green" : state === "Suggested" ? "amber" : "gray"}><span lang="en">EN</span> {t(`english.${state}`)}</Badge>;
}

// ---------------- states ----------------
export function Loading() {
  const { t } = useLang();
  return (
    <div className="space-y-3" aria-busy="true" aria-label={t("common.loading")}>
      <div className="mc-skeleton h-8 w-1/3" />
      <div className="mc-skeleton h-24" />
      <div className="mc-skeleton h-24" />
    </div>
  );
}

export function ErrorState({ error, retry }: { error?: ApiError; retry?: () => void }) {
  const { t, errorText } = useLang();
  return (
    <div role="alert" className="flex flex-wrap items-center gap-3 rounded-xl border border-aered-200 bg-aered-50 p-4 text-aered-800">
      <Warning size={24} aria-hidden="true" />
      <span className="flex-1">{errorText(error)}</span>
      {retry && <Button variant="outline" size="sm" onClick={retry} icon={<ArrowClockwise size={20} aria-hidden="true" />}>{t("common.retry")}</Button>}
    </div>
  );
}

export function Empty({ children }: { children?: ReactNode }) {
  const { t } = useLang();
  return <p className="rounded-xl border border-dashed border-aeblack-200 p-8 text-center text-aeblack-500">{children || t("common.empty")}</p>;
}

export function Note({ children, tone = "info" }: { children: ReactNode; tone?: "info" | "warning" }) {
  return (
    <div className={`flex gap-3 rounded-xl p-4 text-sm ${tone === "warning" ? "bg-camel-50 text-camel-900" : "bg-techblue-50 text-techblue-900"}`}>
      {tone === "warning" ? <Warning size={24} className="flex-none" aria-hidden="true" /> : <Info size={24} className="flex-none" aria-hidden="true" />}
      <div>{children}</div>
    </div>
  );
}

// ---------------- form fields ----------------
/** A labelled field. Required fields carry a red asterisk (and "required" for screen readers, from the input itself). */
export function Field({ label, hint, error, children, className = "", id, required }: { label: ReactNode; hint?: ReactNode; error?: string; children: ReactNode; className?: string; id?: string; required?: boolean }) {
  return (
    <div className={`aegov-form-control ${error ? "control-error" : ""} ${className}`}>
      <label htmlFor={id} className="mh-ui mb-1 block text-sm font-semibold text-aeblack-800">
        {label}{required && <span className="ms-1 text-aered-600" aria-hidden="true">*</span>}
      </label>
      <div className="form-control-input">{children}</div>
      {hint && <p className="mh-ui mt-1 text-xs text-aeblack-500">{hint}</p>}
      {error && <p className="mt-1 text-sm text-aered-700" role="alert">{error}</p>}
    </div>
  );
}

type InputProps = React.InputHTMLAttributes<HTMLInputElement> & { label: ReactNode; hint?: ReactNode; error?: string; wrapClass?: string };

export function TextInput({ label, hint, error, wrapClass, ...rest }: InputProps) {
  const id = useId();
  const ltr = rest.type === "email" || rest.type === "password" || rest.type === "number" || rest.type === "date" || rest.type === "time" || rest.type === "datetime-local" || rest.type === "url";
  return (
    <Field label={label} hint={hint} error={error} id={id} className={wrapClass} required={rest.required}>
      <input id={id} dir={ltr ? "ltr" : rest.dir} {...rest} />
    </Field>
  );
}

type TextAreaProps = React.TextareaHTMLAttributes<HTMLTextAreaElement> & { label: ReactNode; hint?: ReactNode; error?: string; wrapClass?: string };

export const TextArea = forwardRef<HTMLTextAreaElement, TextAreaProps>(function TextArea({ label, hint, error, wrapClass, ...rest }, ref) {
  const id = useId();
  return (
    <Field label={label} hint={hint} error={error} id={id} className={wrapClass} required={rest.required}>
      <textarea id={id} ref={ref} {...rest} />
    </Field>
  );
});

export function SelectInput({ label, hint, options, wrapClass, ...rest }: React.SelectHTMLAttributes<HTMLSelectElement> & { label: ReactNode; hint?: ReactNode; options: { value: string | number; label: string }[]; wrapClass?: string }) {
  const id = useId();
  return (
    <Field label={label} hint={hint} id={id} className={wrapClass} required={rest.required}>
      <select id={id} {...rest}>
        {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </Field>
  );
}

/** On/off setting (Base UI Switch). */
export function Toggle({ label, hint, checked, onChange }: { label: ReactNode; hint?: ReactNode; checked: boolean; onChange: (v: boolean) => void }) {
  const id = useId();
  return (
    <div className="flex items-start gap-3">
      <Switch.Root id={id} checked={checked} onCheckedChange={(v) => onChange(!!v)} className="mh-switch mt-0.5">
        <Switch.Thumb className="mh-switch-thumb" />
      </Switch.Root>
      <div>
        <label htmlFor={id} className="mh-ui cursor-pointer font-semibold text-aeblack-800">{label}</label>
        {hint && <p className="mh-ui text-xs text-aeblack-500">{hint}</p>}
      </div>
    </div>
  );
}

/** Checkbox (Base UI Checkbox). */
export function Tick({ label, checked, onChange }: { label: ReactNode; checked: boolean; onChange: (v: boolean) => void }) {
  const id = useId();
  return (
    <span className="inline-flex items-center gap-2">
      <Checkbox.Root id={id} checked={checked} onCheckedChange={(v) => onChange(!!v)} className="mh-check">
        <Checkbox.Indicator><Check size={14} weight="bold" aria-hidden="true" /></Checkbox.Indicator>
      </Checkbox.Root>
      <label htmlFor={id} className="mh-ui cursor-pointer text-sm">{label}</label>
    </span>
  );
}

/** Small rich text editor for job descriptions and instructions (the server cleans the HTML). */
export function RichText({ label, hint, value, onChange, dir, headings = false, after, required }: {
  label: ReactNode; hint?: ReactNode; value: string; onChange: (v: string) => void; dir: "rtl" | "ltr"; headings?: boolean; after?: ReactNode; required?: boolean;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const id = useId();
  useEffect(() => {
    if (ref.current && ref.current.innerHTML !== value) ref.current.innerHTML = value || "";
  }, [value]);
  const cmd = (c: string) => {
    document.execCommand(c);
    onChange(ref.current?.innerHTML || "");
    ref.current?.focus();
  };
  const tool = "rounded p-1.5 text-aeblack-600 hover:bg-primary-50 hover:text-primary-700";
  return (
    <div>
      <p id={id} className="mh-ui mb-1 text-sm font-semibold text-aeblack-800">{label}{required && <span className="ms-1 text-aered-600" aria-hidden="true">*</span>}</p>
      <div className="rounded-lg border border-aeblack-200 bg-white focus-within:border-primary-500">
        <div className="flex gap-1 border-b border-aeblack-100 p-1" role="toolbar" aria-labelledby={id}>
          <button type="button" className={tool} onClick={() => cmd("bold")} aria-label="Bold"><TextB size={20} /></button>
          <button type="button" className={tool} onClick={() => cmd("italic")} aria-label="Italic"><TextItalic size={20} /></button>
          <button type="button" className={tool} onClick={() => cmd("insertUnorderedList")} aria-label="Bullet list"><ListBullets size={20} /></button>
          <button type="button" className={tool} onClick={() => cmd("insertOrderedList")} aria-label="Numbered list"><ListNumbers size={20} /></button>
          {headings && <button type="button" className={tool} onClick={() => { document.execCommand("formatBlock", false, "h2"); onChange(ref.current?.innerHTML || ""); ref.current?.focus(); }} aria-label="Heading"><TextH size={20} /></button>}
          {headings && <button type="button" className={tool} onClick={() => { document.execCommand("formatBlock", false, "p"); onChange(ref.current?.innerHTML || ""); ref.current?.focus(); }} aria-label="Paragraph"><Paragraph size={20} /></button>}
        </div>
        <div ref={ref} dir={dir} contentEditable role="textbox" aria-multiline="true" aria-required={required || undefined} aria-labelledby={id} className="mh-editor mc-prose"
          onInput={(e) => onChange((e.currentTarget as HTMLDivElement).innerHTML)} suppressContentEditableWarning />
      </div>
      {after}
      {hint && <p className="mh-ui mt-1 text-xs text-aeblack-500">{hint}</p>}
    </div>
  );
}

// ---------------- language tabs ----------------
export type Bilingual = "ar" | "en";

/**
 * One pair of tabs per form: العربية | English. The Arabic tab holds the original text; the English tab shows
 * the English, its review state, Suggest English and the English Approved switch.
 */
export function LangTabs({ value, onChange, english, approved, onApproved, onSuggest, suggesting, children }: {
  value: Bilingual; onChange: (l: Bilingual) => void; english: string; approved: boolean; onApproved: (v: boolean) => void;
  onSuggest?: () => void; suggesting?: boolean; children: ReactNode;
}) {
  const { t } = useLang();
  return (
    <Tabs.Root value={value} onValueChange={(v) => onChange(v as Bilingual)}>
      <Tabs.List className="mh-tabs" aria-label={t("english.title")}>
        <Tabs.Tab value="ar" className="mh-tab"><FlagUae /><span lang="ar">العربية</span></Tabs.Tab>
        <Tabs.Tab value="en" className="mh-tab">
          <FlagUs />
          <span lang="en">English</span>
          <span className={`h-2 w-2 rounded-full ${english === "Approved" ? "bg-aegreen-500" : english === "Suggested" ? "bg-camel-500" : "bg-aeblack-300"}`} aria-hidden="true" />
        </Tabs.Tab>
      </Tabs.List>
      <div className="pt-5">
        {value === "en" && (
          <div className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-xl bg-whitely-100 p-4">
            <div className="flex flex-wrap items-center gap-3">
              <EnglishBadge state={english} />
              <Toggle label={t("english.approved")} hint={t("english.approved_hint")} checked={approved} onChange={onApproved} />
            </div>
            {onSuggest && <Button variant="outline" size="sm" busy={suggesting} onClick={onSuggest} icon={<MagicWand size={20} aria-hidden="true" />}>{t("english.suggest")}</Button>}
          </div>
        )}
        {children}
      </div>
    </Tabs.Root>
  );
}

/** Section tabs (Base UI Tabs) for long pages. */
export function SectionTabs({ tabs, value, onChange }: { tabs: { value: string; label: ReactNode }[]; value: string; onChange: (v: string) => void }) {
  return (
    <Tabs.Root value={value} onValueChange={(v) => onChange(String(v))}>
      <Tabs.List className="mh-tabs mb-6 overflow-x-auto">
        {tabs.map((tb) => <Tabs.Tab key={tb.value} value={tb.value} className="mh-tab whitespace-nowrap">{tb.label}</Tabs.Tab>)}
      </Tabs.List>
    </Tabs.Root>
  );
}

// ---------------- dialogs and menus ----------------
export function Modal({ open, onClose, title, children, footer }: { open: boolean; onClose: () => void; title: ReactNode; children: ReactNode; footer?: ReactNode }) {
  const { t } = useLang();
  return (
    <Dialog.Root open={open} onOpenChange={(o) => !o && onClose()}>
      <Dialog.Portal>
        <Dialog.Backdrop className="mh-backdrop" />
        <Dialog.Popup className="mh-dialog">
          <div className="mb-4 flex items-start justify-between gap-4">
            <Dialog.Title className="text-xl font-bold text-aeblack-900">{title}</Dialog.Title>
            <Dialog.Close className="rounded p-1 text-aeblack-500 hover:bg-aeblack-100" aria-label={t("app.close")}><X size={24} /></Dialog.Close>
          </div>
          <div className="space-y-4">{children}</div>
          {footer && <div className="mt-6 flex flex-wrap justify-end gap-2">{footer}</div>}
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function ActionMenu({ label, items, icon }: { label: ReactNode; icon?: ReactNode; items: { label: ReactNode; onClick: () => void; disabled?: boolean }[] }) {
  return (
    <Menu.Root>
      <Menu.Trigger className="aegov-btn btn-outline btn-sm">
        {icon}
        {label}
        <CaretDown size={16} aria-hidden="true" />
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Positioner sideOffset={6} align="end" className="z-[80]">
          <Menu.Popup className="mh-popup">
            {items.map((it, i) => (
              <Menu.Item key={i} className="mh-item" disabled={it.disabled} onClick={it.onClick}>{it.label}</Menu.Item>
            ))}
          </Menu.Popup>
        </Menu.Positioner>
      </Menu.Portal>
    </Menu.Root>
  );
}

// ---------------- tables ----------------
export function Table({ head, children }: { head: ReactNode[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto rounded-2xl border border-aeblack-100 bg-white">
      <table className="mh-table">
        <thead><tr>{head.map((h, i) => <th key={i} scope="col">{h}</th>)}</tr></thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

export function Pager({ page, pages, onPage }: { page: number; pages: number; onPage: (p: number) => void }) {
  const { t } = useLang();
  if (pages <= 1) return null;
  return (
    <nav className="mt-4 flex items-center justify-center gap-3" aria-label="Pagination">
      <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>{t("common.previous")}</Button>
      <span className="text-sm text-aeblack-600">{t("common.page", { page, pages })}</span>
      <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>{t("common.next")}</Button>
    </nav>
  );
}

/** Score as a colored number out of 100. */
export function Score({ value }: { value?: number | null }) {
  if (value === null || value === undefined) return <span className="text-aeblack-500">-</span>;
  const tone = value >= 70 ? "text-aegreen-700" : value >= 50 ? "text-camel-700" : "text-aered-700";
  return <span className={`font-bold tabular-nums ${tone}`} dir="ltr">{Math.round(value)}</span>;
}

export function useSaving() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const run = async <T,>(fn: () => Promise<T>): Promise<T | undefined> => {
    setBusy(true);
    setError(null);
    try {
      return await fn();
    } catch (e) {
      setError(e instanceof ApiError ? e : new ApiError("server", 500));
      return undefined;
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, run, setError };
}

// ---------------- shared building blocks ----------------

/** A field label with the language's flag, for Arabic and English versions of the same text. */
export function FlagLabel({ lang, children }: { lang: "ar" | "en"; children: ReactNode }) {
  return <span className="inline-flex items-center gap-2"><LangFlag lang={lang} />{children}</span>;
}

/** A small set of mutually exclusive choices shown as pills (a radio group). */
export function Segmented<T extends string>({ label, value, options, onChange }: {
  label: string; value: T; options: { value: T; label: ReactNode }[]; onChange: (v: T) => void;
}) {
  return (
    <div role="radiogroup" aria-label={label} className="inline-flex rounded-xl bg-aeblack-50 p-1">
      {options.map((o) => (
        <button key={o.value} type="button" role="radio" aria-checked={value === o.value} onClick={() => onChange(o.value)}
          className={`inline-flex items-center gap-2 rounded-lg px-3 py-1.5 text-sm font-semibold transition ${value === o.value ? "bg-white text-primary-800 shadow-sm" : "text-aeblack-600 hover:text-aeblack-900"}`}>
          {o.label}
        </button>
      ))}
    </div>
  );
}

/** Arabic or English, with flags. */
export function LangSwitch({ value, onChange }: { value: Bilingual; onChange: (l: Bilingual) => void }) {
  const { t } = useLang();
  return <Segmented label={t("english.title")} value={value} onChange={onChange}
    options={[{ value: "ar", label: <FlagLabel lang="ar"><span lang="ar">العربية</span></FlagLabel> }, { value: "en", label: <FlagLabel lang="en"><span lang="en">English</span></FlagLabel> }]} />;
}

/** A list of records inside a card: each row has a title (a button when it opens something), a line under it and badges. */
export function RowList({ children }: { children: ReactNode }) {
  return <ul className="-my-3 divide-y divide-aeblack-100">{children}</ul>;
}

export function Row({ title, sub, onOpen, aside }: { title: ReactNode; sub?: ReactNode; onOpen?: () => void; aside?: ReactNode }) {
  return (
    <li className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 py-3">
      <div className="min-w-0 flex-1">
        {onOpen ? <button type="button" onClick={onOpen} className="text-start font-semibold text-primary-700 hover:underline">{title}</button>
          : <span className="font-semibold text-aeblack-900">{title}</span>}
        {sub && <div className="mt-0.5 text-sm text-aeblack-600">{sub}</div>}
      </div>
      {aside && <div className="flex flex-wrap items-center gap-2">{aside}</div>}
    </li>
  );
}

/** A field that opens a picker (for example the question picker): looks like an input, shows the current choice. */
export function PickerField({ label, value, placeholder, onClick }: { label: ReactNode; value?: ReactNode; placeholder?: string; onClick: () => void }) {
  const id = useId();
  return (
    <div>
      <p id={id} className="mb-1 block text-sm font-semibold text-aeblack-800">{label}</p>
      <button type="button" aria-labelledby={id} onClick={onClick}
        className="flex w-full items-center justify-between gap-2 rounded-lg border border-aeblack-200 bg-white px-3 py-2.5 text-start text-sm hover:border-primary-500 focus-visible:border-primary-500">
        <span className={`line-clamp-2 ${value ? "" : "text-aeblack-500"}`}>{value || placeholder}</span>
        <CaretDown size={16} className="flex-none text-aeblack-500" aria-hidden="true" />
      </button>
    </div>
  );
}

/** A number with its label, for summaries at the top of a page. */
export function Stat({ label, value, hint }: { label: ReactNode; value: ReactNode; hint?: ReactNode }) {
  return (
    <div className="mc-surface rounded-2xl border border-aeblack-100 bg-white p-4">
      <p className="text-sm text-aeblack-500">{label}</p>
      <p className="mt-1 text-3xl font-bold tabular-nums text-aeblack-900" dir="ltr">{value}</p>
      {hint && <p className="mt-1 text-xs text-aeblack-500">{hint}</p>}
    </div>
  );
}

/**
 * Plain text with {placeholders}: the text box, one chip per placeholder (inserted at the cursor) and a live preview
 * (blank line = paragraph, "# " = heading). Used by the email and offer letter templates.
 */
export function PlaceholderText({ label, hint, lang, value, onChange, placeholders, rows = 10, sample }: {
  label: ReactNode; hint?: ReactNode; lang: Bilingual; value: string; onChange: (v: string) => void; placeholders: string[]; rows?: number;
  sample?: (text: string) => string;
}) {
  const { t } = useLang();
  const ref = useRef<HTMLTextAreaElement>(null);
  const dir = lang === "ar" ? "rtl" : "ltr";
  const insert = (p: string) => {
    const el = ref.current, token = `{${p}}`;
    if (!el) return onChange(value + token);
    const start = el.selectionStart ?? value.length, end = el.selectionEnd ?? value.length;
    onChange(value.slice(0, start) + token + value.slice(end));
    requestAnimationFrame(() => { el.focus(); el.setSelectionRange(start + token.length, start + token.length); });
  };
  const shown = sample ? sample(value) : value;
  return (
    <div className="space-y-3">
      <TextArea required ref={ref} label={label} hint={hint} dir={dir} rows={rows} value={value} onChange={(e) => onChange(e.target.value)} />
      {placeholders.length > 0 && (
        <div className="flex flex-wrap gap-1.5" role="group" aria-label={t("mail.placeholders")}>
          {placeholders.map((p) => (
            <button key={p} type="button" onClick={() => insert(p)} dir="ltr"
              className="rounded-md bg-aeblack-50 px-2 py-1 font-mono text-xs text-aeblack-700 hover:bg-primary-50 hover:text-primary-800">{`{${p}}`}</button>
          ))}
        </div>
      )}
      <div className="rounded-xl border border-aeblack-100 bg-whitely-50 p-4 text-sm" dir={dir} lang={lang}>
        <p className="mb-2 text-xs font-semibold text-aeblack-500">{t("mail.preview")}</p>
        {shown.split(/\n\s*\n/).filter((x) => x.trim()).map((para, i) => para.trim().startsWith("# ")
          ? <h3 key={i} className="mb-3 text-lg font-bold">{para.trim().slice(2)}</h3>
          : <p key={i} className="mb-3 whitespace-pre-line last:mb-0">{para}</p>)}
      </div>
    </div>
  );
}

/** Placeholder chips for a rich text editor: clicking one types {placeholder} where the cursor is (the editor keeps focus). */
function RichChips({ placeholders }: { placeholders: string[] }) {
  const { t } = useLang();
  if (placeholders.length === 0) return null;
  return (
    <div className="mt-2 flex flex-wrap gap-1.5" role="group" aria-label={t("mail.placeholders")}>
      {placeholders.map((p) => (
        <button key={p} type="button" dir="ltr" onMouseDown={(e) => e.preventDefault()} onClick={() => document.execCommand("insertText", false, `{${p}}`)}
          className="rounded-md bg-aeblack-50 px-2 py-1 font-mono text-xs text-aeblack-700 hover:bg-primary-50 hover:text-primary-800">{`{${p}}`}</button>
      ))}
    </div>
  );
}

/** Plain text turned into simple HTML (paragraphs, line breaks, "# " headings), to start a designed version from. */
export function textToHtml(text: string) {
  const esc = (x: string) => x.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  return text.split(/\n\s*\n/).filter((p) => p.trim()).map((p) => (p.trim().startsWith("# ")
    ? `<h2>${esc(p.trim().slice(2))}</h2>` : `<p>${esc(p).replace(/\n/g, "<br>")}</p>`)).join("");
}

/**
 * One language of a template: the plain text (always kept, for mail clients that show text only and as the fallback) and,
 * when switched on, a designed HTML version with headings, bold and lists.
 */
export function TemplateBody({ lang, text, html, designed, onText, onHtml, placeholders, textLabel, htmlLabel, hint, rows = 10, sample }: {
  lang: Bilingual; text: string; html: string; designed: boolean; onText: (v: string) => void; onHtml: (v: string) => void; placeholders: string[];
  textLabel: ReactNode; htmlLabel: ReactNode; hint?: ReactNode; rows?: number; sample?: (x: string) => string;
}) {
  return (
    <div className="space-y-4">
      {designed && <RichText label={htmlLabel} dir={lang === "ar" ? "rtl" : "ltr"} headings value={html} onChange={onHtml} after={<RichChips placeholders={placeholders} />} />}
      <PlaceholderText label={textLabel} hint={hint} lang={lang} value={text} onChange={onText} placeholders={placeholders} rows={designed ? 6 : rows} sample={sample} />
    </div>
  );
}
