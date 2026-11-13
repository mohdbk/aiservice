import { Check, MagnifyingGlass, X } from "@phosphor-icons/react";
import { KeyboardEvent, ReactNode, useId, useMemo, useRef, useState } from "react";
import { useLang } from "../core";

export type MultiOption = { value: number; label: string; hint?: string };

/**
 * A searchable multi-select for long lists (for example the interview panel): the chosen people as removable chips, a
 * search box that filters by name or email, and the matching options to tick. Keyboard: arrows move, Enter picks,
 * Backspace in an empty box removes the last chip, Escape clears the search.
 */
export function MultiSelect({ label, hint, options, value, onChange, placeholder, max = 8 }: {
  label: ReactNode; hint?: ReactNode; options: MultiOption[]; value: number[]; onChange: (v: number[]) => void; placeholder?: string; max?: number;
}) {
  const { t } = useLang();
  const id = useId();
  const listId = `${id}-list`;
  const [q, setQ] = useState("");
  const [focus, setFocus] = useState(0);
  const input = useRef<HTMLInputElement>(null);
  const chosen = value.map((v) => options.find((o) => o.value === v)).filter((o): o is MultiOption => !!o);
  const norm = (s: string) => s.toLocaleLowerCase().normalize("NFKD").replace(/[ً-ٰٟ]/g, "");
  const matches = useMemo(() => {
    const term = norm(q.trim());
    const list = term ? options.filter((o) => norm(`${o.label} ${o.hint ?? ""}`).includes(term)) : options;
    return list.slice(0, 50);
  }, [q, options]);
  const toggle = (v: number) => onChange(value.includes(v) ? value.filter((x) => x !== v) : [...value, v]);
  const key = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown") { e.preventDefault(); setFocus((f) => Math.min(f + 1, matches.length - 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setFocus((f) => Math.max(f - 1, 0)); }
    else if (e.key === "Enter") { e.preventDefault(); if (matches[focus]) toggle(matches[focus].value); }
    else if (e.key === "Escape" && q) { e.preventDefault(); e.stopPropagation(); setQ(""); }
    else if (e.key === "Backspace" && !q && value.length) onChange(value.slice(0, -1));
  };

  return (
    <div className="space-y-2">
      <label htmlFor={id} className="block text-sm font-semibold text-aeblack-800">{label}</label>
      {hint && <p className="text-xs text-aeblack-500">{hint}</p>}
      {chosen.length > 0 && (
        <ul className="flex flex-wrap gap-2" aria-label={t("multi.chosen", { n: chosen.length })}>
          {chosen.map((o) => (
            <li key={o.value} className="inline-flex items-center gap-1 rounded-full bg-primary-100 py-1 ps-3 pe-1 text-sm text-primary-900">
              {o.label}
              <button type="button" onClick={() => toggle(o.value)} className="rounded-full p-1 hover:bg-primary-200 focus-visible:outline-2"
                aria-label={t("multi.remove", { name: o.label })}><X size={14} aria-hidden="true" /></button>
            </li>
          ))}
        </ul>
      )}
      <div className="rounded-lg border border-aeblack-200 focus-within:border-primary-600 focus-within:ring-2 focus-within:ring-primary-100">
        <div className="flex items-center gap-2 border-b border-aeblack-100 px-3">
          <MagnifyingGlass size={20} className="text-aeblack-500" aria-hidden="true" />
          <input ref={input} id={id} value={q} onChange={(e) => { setQ(e.target.value); setFocus(0); }} onKeyDown={key}
            placeholder={placeholder ?? t("multi.search")} autoComplete="off" role="combobox" aria-expanded="true" aria-controls={listId}
            aria-activedescendant={matches[focus] ? `${id}-o-${matches[focus].value}` : undefined}
            className="w-full bg-transparent py-2.5 text-sm outline-none" />
          {q && <button type="button" onClick={() => { setQ(""); input.current?.focus(); }} className="rounded p-1 text-aeblack-500 hover:text-aeblack-800" aria-label={t("multi.clear")}><X size={16} aria-hidden="true" /></button>}
        </div>
        <ul id={listId} role="listbox" aria-multiselectable="true" className="overflow-y-auto py-1" style={{ maxHeight: `${max * 2.75}rem` }}>
          {matches.length === 0 && <li className="px-3 py-2 text-sm text-aeblack-500">{t("multi.none")}</li>}
          {matches.map((o, i) => {
            const on = value.includes(o.value);
            return (
              <li key={o.value} id={`${id}-o-${o.value}`} role="option" aria-selected={on} onMouseDown={(e) => e.preventDefault()}
                onClick={() => { toggle(o.value); setFocus(i); input.current?.focus(); }} onMouseEnter={() => setFocus(i)}
                className={`flex cursor-pointer items-center gap-3 px-3 py-2 text-sm ${i === focus ? "bg-primary-50" : ""}`}>
                <span className={`flex h-5 w-5 flex-none items-center justify-center rounded border-2 ${on ? "border-primary-600 bg-primary-600 text-white" : "border-aeblack-300 bg-white"}`} aria-hidden="true">
                  {on && <Check size={12} weight="bold" />}
                </span>
                <span className="min-w-0">
                  <span className="block truncate font-medium text-aeblack-900">{o.label}</span>
                  {o.hint && <span className="block truncate text-xs text-aeblack-500" dir="ltr">{o.hint}</span>}
                </span>
              </li>
            );
          })}
        </ul>
      </div>
      <p className="text-xs text-aeblack-500" aria-live="polite">{t("multi.count", { n: chosen.length, total: options.length })}</p>
    </div>
  );
}
