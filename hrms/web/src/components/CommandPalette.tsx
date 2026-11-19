import { Dialog } from "@base-ui/react";
import { ArrowRight, Briefcase, ChatCircleDots, Exam, MagnifyingGlass, Plus, UserCircle } from "@phosphor-icons/react";
import { ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api, useAuth, useLang } from "../core";

type Found = {
  applicants: { id: number; name: string; code: string; status: string; job: string }[];
  jobs: { id: number; code: string; title: string; status: string }[];
  tests: { id: number; title: string; status: string }[];
};
type Entry = { key: string; group: string; label: string; hint?: string; icon: ReactNode; to: string };
export type NavPage = { to: string; label: string; icon: ReactNode; admin?: boolean; sys?: boolean };

/**
 * Quick search, opened with Ctrl+K (Cmd+K on a Mac) or the search button: pages, common actions and records
 * (applicants, jobs, tests) by name, email, phone or code, all reachable with the keyboard. With nothing found, it offers
 * to ask the assistant.
 */
export function CommandPalette({ pages }: { pages: NavPage[] }) {
  const { t, to } = useLang();
  const { me } = useAuth();
  const nav = useNavigate();
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState("");
  const [found, setFound] = useState<Found | null>(null);
  const [active, setActive] = useState(0);
  const list = useRef<HTMLUListElement>(null);

  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") { e.preventDefault(); setOpen((o) => !o); }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, []);
  useEffect(() => { if (!open) { setQ(""); setFound(null); setActive(0); } }, [open]);
  // Records are searched as you type, a moment after the last key.
  useEffect(() => {
    const term = q.trim();
    if (term.length < 2) { setFound(null); return; }
    const timer = setTimeout(() => { api<Found>(`/search?q=${encodeURIComponent(term)}`).then(setFound).catch(() => setFound(null)); }, 200);
    return () => clearTimeout(timer);
  }, [q]);

  const entries = useMemo<Entry[]>(() => {
    const term = q.trim().toLowerCase();
    const match = (s: string) => !term || s.toLowerCase().includes(term);
    const i = (C: typeof Plus) => <C size={20} aria-hidden="true" />;
    const actions: Entry[] = [
      { key: "a-job", group: t("cmd.actions"), label: t("jobs.new"), icon: i(Plus), to: "/jobs/new" },
      { key: "a-test", group: t("cmd.actions"), label: t("test.new"), icon: i(Plus), to: "/tests/new" },
      { key: "a-q", group: t("cmd.actions"), label: t("q.new"), icon: i(Plus), to: "/questions/new" },
      { key: "a-asst", group: t("cmd.actions"), label: t("nav.assistant"), icon: i(ChatCircleDots), to: "/assistant" },
    ].filter((e) => match(e.label));
    const pageEntries: Entry[] = pages.filter((p) => (!p.admin || me?.can_administer) && (!p.sys || me?.is_administrator) && match(p.label))
      .slice(0, term ? 6 : 8).map((p) => ({ key: `p-${p.to}`, group: t("cmd.pages"), label: p.label, icon: p.icon, to: p.to }));
    const records: Entry[] = found ? [
      ...found.applicants.map((a) => ({ key: `ap-${a.id}`, group: t("nav.applicants"), label: a.name, hint: `${a.code} · ${a.job} · ${t(`app.status.${a.status}`)}`, icon: i(UserCircle), to: `/applicants/${a.id}` })),
      ...found.jobs.map((j) => ({ key: `j-${j.id}`, group: t("nav.jobs"), label: j.title, hint: j.code, icon: i(Briefcase), to: `/jobs/${j.id}` })),
      ...found.tests.map((x) => ({ key: `t-${x.id}`, group: t("nav.assessments"), label: x.title, icon: i(Exam), to: `/tests/${x.id}` })),
    ] : [];
    const ask: Entry[] = term.length >= 3 ? [{ key: "ask", group: t("nav.assistant"), label: t("cmd.ask", { q: q.trim() }), icon: i(ChatCircleDots), to: `/assistant?q=${encodeURIComponent(q.trim())}` }] : [];
    return [...records, ...pageEntries, ...actions, ...ask];
  }, [q, found, pages, me, t]);

  useEffect(() => setActive(0), [q, found]);
  const go = (e: Entry) => { setOpen(false); nav(to(e.to)); };
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((a) => Math.min(entries.length - 1, a + 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setActive((a) => Math.max(0, a - 1)); }
    else if (e.key === "Enter" && entries[active]) { e.preventDefault(); go(entries[active]); }
  };
  useEffect(() => { list.current?.querySelector(`[data-index="${active}"]`)?.scrollIntoView({ block: "nearest" }); }, [active]);

  let lastGroup = "";
  const mac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform);
  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger className="inline-flex items-center gap-2 rounded-lg border border-aeblack-200 px-2.5 py-1.5 text-sm text-aeblack-600 hover:border-primary-300 hover:text-primary-800" aria-label={t("cmd.open")}>
        <MagnifyingGlass size={18} aria-hidden="true" />
        <span className="hidden lg:inline">{t("cmd.search")}</span>
        <kbd className="hidden rounded border border-aeblack-200 bg-whitely-50 px-1.5 font-sans text-[11px] text-aeblack-500 lg:inline" dir="ltr">{mac ? "⌘K" : "Ctrl K"}</kbd>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Backdrop className="mh-backdrop" />
        <Dialog.Popup className="fixed inset-x-3 top-[10vh] z-[62] mx-auto max-w-xl overflow-hidden rounded-2xl bg-white shadow-2xl outline-none" aria-label={t("cmd.open")}>
          <div className="flex items-center gap-3 border-b-2 border-aeblack-100 px-4 focus-within:border-primary-500">
            <MagnifyingGlass size={22} className="shrink-0 text-aeblack-500" aria-hidden="true" />
            <input autoFocus value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={onKey} dir="auto"
              placeholder={t("cmd.placeholder")} aria-label={t("cmd.search")} role="combobox" aria-expanded="true" aria-controls="cmd-list"
              aria-activedescendant={entries[active] ? `cmd-${entries[active].key}` : undefined}
              className="mh-cmd-input min-w-0 flex-1 border-0 bg-transparent py-4 text-base outline-none" style={{ boxShadow: "none" }} />
            <kbd className="rounded border border-aeblack-200 px-1.5 text-[11px] text-aeblack-500">Esc</kbd>
          </div>
          <ul id="cmd-list" ref={list} role="listbox" className="max-h-[60vh] overflow-y-auto p-2">
            {entries.length === 0 && <li className="px-3 py-6 text-center text-sm text-aeblack-500">{t("cmd.none")}</li>}
            {entries.map((e, n) => {
              const head = e.group !== lastGroup ? e.group : "";
              lastGroup = e.group;
              return (
                <li key={e.key} role="presentation">
                  {head && <p className="px-3 pb-1 pt-3 text-[11px] font-bold uppercase tracking-wide text-aeblack-500">{head}</p>}
                  <button type="button" id={`cmd-${e.key}`} role="option" aria-selected={n === active} data-index={n}
                    onMouseMove={() => setActive(n)} onClick={() => go(e)}
                    className={`flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-start ${n === active ? "bg-primary-50 text-primary-900" : "text-aeblack-800"}`}>
                    <span className={n === active ? "text-primary-700" : "text-aeblack-500"}>{e.icon}</span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-semibold" dir="auto">{e.label}</span>
                      {e.hint && <span className="block truncate text-xs text-aeblack-500" dir="auto">{e.hint}</span>}
                    </span>
                    {n === active && <ArrowRight size={16} className="shrink-0 rtl:-scale-x-100" aria-hidden="true" />}
                  </button>
                </li>
              );
            })}
          </ul>
          <p className="border-t border-aeblack-100 px-4 py-2 text-[11px] text-aeblack-500">{t("cmd.help")}</p>
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
