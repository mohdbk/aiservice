import { Checkbox, Dialog, Menu, Switch, Tabs } from "@base-ui/react";
import {
  ArrowClockwise, CaretDown, Check, CircleNotch, Info, ListBullets, ListNumbers, MagicWand, TextB, TextItalic, Warning, X,
} from "@phosphor-icons/react";
import { ReactNode, forwardRef, useEffect, useId, useRef, useState } from "react";
import { ApiError, useLang } from "../core";
import { FlagUae, FlagUs } from "./flags";

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
export function Field({ label, hint, error, children, className = "", id }: { label: ReactNode; hint?: ReactNode; error?: string; children: ReactNode; className?: string; id?: string }) {
  return (
    <div className={`aegov-form-control ${error ? "control-error" : ""} ${className}`}>
      <label htmlFor={id} className="mb-1 block text-sm font-semibold text-aeblack-800">{label}</label>
      <div className="form-control-input">{children}</div>
      {hint && <p className="mt-1 text-xs text-aeblack-500">{hint}</p>}
      {error && <p className="mt-1 text-sm text-aered-700" role="alert">{error}</p>}
    </div>
  );
}

type InputProps = React.InputHTMLAttributes<HTMLInputElement> & { label: ReactNode; hint?: ReactNode; error?: string; wrapClass?: string };

export function TextInput({ label, hint, error, wrapClass, ...rest }: InputProps) {
  const id = useId();
  const ltr = rest.type === "email" || rest.type === "password" || rest.type === "number" || rest.type === "date" || rest.type === "time" || rest.type === "url";
  return (
    <Field label={label} hint={hint} error={error} id={id} className={wrapClass}>
      <input id={id} dir={ltr ? "ltr" : rest.dir} {...rest} />
    </Field>
  );
}

type TextAreaProps = React.TextareaHTMLAttributes<HTMLTextAreaElement> & { label: ReactNode; hint?: ReactNode; error?: string; wrapClass?: string };

export const TextArea = forwardRef<HTMLTextAreaElement, TextAreaProps>(function TextArea({ label, hint, error, wrapClass, ...rest }, ref) {
  const id = useId();
  return (
    <Field label={label} hint={hint} error={error} id={id} className={wrapClass}>
      <textarea id={id} ref={ref} {...rest} />
    </Field>
  );
});

export function SelectInput({ label, hint, options, wrapClass, ...rest }: React.SelectHTMLAttributes<HTMLSelectElement> & { label: ReactNode; hint?: ReactNode; options: { value: string | number; label: string }[]; wrapClass?: string }) {
  const id = useId();
  return (
    <Field label={label} hint={hint} id={id} className={wrapClass}>
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
        <label htmlFor={id} className="cursor-pointer font-semibold text-aeblack-800">{label}</label>
        {hint && <p className="text-xs text-aeblack-500">{hint}</p>}
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
      <label htmlFor={id} className="cursor-pointer text-sm">{label}</label>
    </span>
  );
}

/** Small rich text editor for job descriptions and instructions (the server cleans the HTML). */
export function RichText({ label, hint, value, onChange, dir }: { label: ReactNode; hint?: ReactNode; value: string; onChange: (v: string) => void; dir: "rtl" | "ltr" }) {
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
      <p id={id} className="mb-1 text-sm font-semibold text-aeblack-800">{label}</p>
      <div className="rounded-lg border border-aeblack-200 bg-white focus-within:border-primary-500">
        <div className="flex gap-1 border-b border-aeblack-100 p-1" role="toolbar" aria-labelledby={id}>
          <button type="button" className={tool} onClick={() => cmd("bold")} aria-label="Bold"><TextB size={20} /></button>
          <button type="button" className={tool} onClick={() => cmd("italic")} aria-label="Italic"><TextItalic size={20} /></button>
          <button type="button" className={tool} onClick={() => cmd("insertUnorderedList")} aria-label="Bullet list"><ListBullets size={20} /></button>
          <button type="button" className={tool} onClick={() => cmd("insertOrderedList")} aria-label="Numbered list"><ListNumbers size={20} /></button>
        </div>
        <div ref={ref} dir={dir} contentEditable role="textbox" aria-multiline="true" aria-labelledby={id} className="mh-editor mc-prose"
          onInput={(e) => onChange((e.target as HTMLDivElement).innerHTML)} suppressContentEditableWarning />
      </div>
      {hint && <p className="mt-1 text-xs text-aeblack-500">{hint}</p>}
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
        <Menu.Positioner sideOffset={6} align="end">
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
  if (value === null || value === undefined) return <span className="text-aeblack-400">-</span>;
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
