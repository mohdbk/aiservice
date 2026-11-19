import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useLang } from "../core";
import { Modal } from "./ui";

/** "g" then a letter goes to a page. */
const GO: Record<string, string> = { d: "/", a: "/applicants", j: "/jobs", i: "/interviews", t: "/tests", o: "/offers", r: "/reports", s: "/assistant" };

const typing = (e: KeyboardEvent) => {
  const el = e.target as HTMLElement | null;
  return !!el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));
};

/**
 * Keyboard shortcuts for people who work all day in the backoffice: "/" searches the list on the page, "g" then a
 * letter opens a page, "n" starts a new record where the page has a New button, and "?" lists them all.
 */
export function Shortcuts() {
  const { t, to } = useLang();
  const nav = useNavigate();
  const [help, setHelp] = useState(false);
  const pending = useRef(0);
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey || typing(e) || document.querySelector("[role=dialog]")) return;
      const k = e.key.toLowerCase();
      if (pending.current && Date.now() - pending.current < 1200) {
        pending.current = 0;
        if (GO[k]) { e.preventDefault(); nav(to(GO[k])); }
        return;
      }
      if (k === "g") { pending.current = Date.now(); return; }
      if (e.key === "?") { e.preventDefault(); setHelp(true); return; }
      if (k === "/") {
        const search = document.querySelector<HTMLInputElement>("#list-q");
        if (search) { e.preventDefault(); search.focus(); search.select(); }
        return;
      }
      if (k === "n") {
        const add = document.querySelector<HTMLElement>("main [data-shortcut=new]");
        if (add) { e.preventDefault(); add.click(); }
      }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, [nav, to]);
  const rows: [string[], string][] = [
    [["Ctrl", "K"], t("keys.search_all")],
    [["/"], t("keys.search_list")],
    [["N"], t("keys.new")],
    ...Object.entries(GO).map(([k, p]): [string[], string] => [["G", k.toUpperCase()], t(`keys.go.${p === "/" ? "dashboard" : p.slice(1)}`)]),
    [["?"], t("keys.help")],
  ];
  return (
    <Modal open={help} onClose={() => setHelp(false)} title={t("keys.title")}>
      <dl className="divide-y divide-aeblack-100 text-sm">
        {rows.map(([keys, label]) => (
          <div key={label} className="flex items-center justify-between gap-4 py-2">
            <dt>{label}</dt>
            <dd className="flex gap-1" dir="ltr">{keys.map((k) => <kbd key={k} className="min-w-7 rounded-md border border-aeblack-200 bg-whitely-50 px-1.5 py-0.5 text-center font-sans text-xs font-semibold">{k}</kbd>)}</dd>
          </div>
        ))}
      </dl>
    </Modal>
  );
}
