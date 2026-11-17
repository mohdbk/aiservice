import { Menu } from "@base-ui/react";
import { CaretDown, DownloadSimple, Exam, Kanban, ListBullets } from "@phosphor-icons/react";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { DataTable, opts, useList } from "../components/DataTable";
import { ActionMenu, Badge, Button, PageHeader, Score, Segmented, TextInput } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
import { InviteDialog } from "./Applicant";
import { useLookups } from "./JobEdit";

type Row = {
  id: number; code: string; full_name: string; email: string; job_id: number; job_code: string; job_title: string; status: string;
  screening: string; country: string; score: number | null; ai_status: string | null; created_at: string; interviews: number;
};

export const appTone = (s: string) => (s === "Open" ? "blue" : s === "Accepted" ? "green" : s === "Rejected" ? "red" : s === "Hold" ? "amber" : "gray");
export const screenTone = (s: string) => (s === "Meets" ? "green" : s === "DoesNotMeet" ? "red" : "gray");

const STATUSES = ["Open", "Replied", "Hold", "Accepted", "Rejected", "Withdrawn"];
const WAIT = 6000;

/**
 * Status changes from the list wait a few seconds before they are saved, so Undo really undoes them (a "Not Selected"
 * email is never sent for a change taken back). Leaving the page saves what is still waiting.
 */
function useDeferredStatus(reload: () => void) {
  const { t } = useLang();
  const toast = useToast();
  const [shown, setShown] = useState<Record<number, string>>({});
  const waiting = useRef(new Map<number, { status: string; timer: number }>());
  const save = (id: number, status: string) => api(`/applicants/${id}/status`, { body: { status } });
  const flush = () => {
    for (const [id, w] of waiting.current) {
      clearTimeout(w.timer);
      fetch(`/api/backoffice/applicants/${id}/status`, { method: "POST", keepalive: true, credentials: "same-origin",
        headers: { "Content-Type": "application/json", "X-Backoffice": "1" }, body: JSON.stringify({ status: w.status }) });
    }
    waiting.current.clear();
  };
  useEffect(() => {
    window.addEventListener("pagehide", flush);
    return () => { window.removeEventListener("pagehide", flush); flush(); };
  }, []);
  const change = (ids: number[], status: string, before: Record<number, string>) => {
    setShown((s) => ({ ...s, ...Object.fromEntries(ids.map((id) => [id, status])) }));
    let done = 0;
    for (const id of ids) {
      const prev = waiting.current.get(id);
      if (prev) clearTimeout(prev.timer);
      const timer = window.setTimeout(async () => {
        waiting.current.delete(id);
        try { await save(id, status); } catch { toast(t("bulk.failed"), "error"); setShown((s) => ({ ...s, [id]: before[id] })); }
        if (++done === ids.length) reload();
      }, WAIT);
      waiting.current.set(id, { status, timer });
    }
    toast(ids.length === 1 ? t("bulk.status_one", { status: t(`app.status.${status}`) }) : t("bulk.status_many", { count: ids.length, status: t(`app.status.${status}`) }), "info", {
      label: t("bulk.undo"),
      run: () => {
        for (const id of ids) { const w = waiting.current.get(id); if (w) clearTimeout(w.timer); waiting.current.delete(id); }
        setShown((s) => ({ ...s, ...before }));
      },
    });
  };
  return { shown, change };
}

/** The status badge in a row, which opens a menu to change it. */
function StatusPick({ value, name, onPick }: { value: string; name: string; onPick: (s: string) => void }) {
  const { t } = useLang();
  return (
    <Menu.Root>
      <Menu.Trigger className="mh-status-pick" aria-label={t("bulk.change_for", { name, status: t(`app.status.${value}`) })}>
        <Badge tone={appTone(value)}>{t(`app.status.${value}`)}<CaretDown size={12} className="ms-1 inline" aria-hidden="true" /></Badge>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Positioner sideOffset={6} align="start" className="z-[80]">
          <Menu.Popup className="mh-popup">
            {STATUSES.filter((s) => s !== value).map((s) => (
              <Menu.Item key={s} className="mh-item" onClick={() => onPick(s)}>
                <Badge tone={appTone(s)}>{t(`app.status.${s}`)}</Badge>
              </Menu.Item>
            ))}
          </Menu.Popup>
        </Menu.Positioner>
      </Menu.Portal>
    </Menu.Root>
  );
}

/**
 * The pipeline as a board: one column per status, cards dragged between columns (or moved with the status menu on
 * each card, for the keyboard). Shows the list's current filters.
 */
function Board({ rows, statusOf, onMove }: { rows: Row[]; statusOf: (a: Row) => string; onMove: (a: Row, s: string) => void }) {
  const { t, to } = useLang();
  const [over, setOver] = useState("");
  return (
    <div className="mh-board -mx-1 flex snap-x gap-3 overflow-x-auto px-1 pb-3">
      {STATUSES.map((s) => {
        const items = rows.filter((a) => statusOf(a) === s);
        return (
          <section key={s} aria-labelledby={`col-${s}`}
            onDragOver={(e) => { e.preventDefault(); setOver(s); }} onDragLeave={() => setOver("")}
            onDrop={(e) => { e.preventDefault(); setOver(""); const a = rows.find((r) => String(r.id) === e.dataTransfer.getData("text/plain")); if (a && statusOf(a) !== s) onMove(a, s); }}
            className={`flex w-72 flex-none snap-start flex-col rounded-2xl border p-2 transition ${over === s ? "border-primary-400 bg-primary-50" : "border-aeblack-100 bg-whitely-50"}`}>
            <h2 id={`col-${s}`} className="flex items-center justify-between px-2 py-1.5 text-sm font-bold">
              <Badge tone={appTone(s)}>{t(`app.status.${s}`)}</Badge>
              <span className="text-aeblack-500" dir="ltr">{items.length}</span>
            </h2>
            <ul className="mt-1 min-h-24 space-y-2">
              {items.map((a) => (
                <li key={a.id} draggable onDragStart={(e) => { e.dataTransfer.setData("text/plain", String(a.id)); e.dataTransfer.effectAllowed = "move"; }}
                  className="cursor-grab rounded-xl border border-aeblack-100 bg-white p-3 shadow-sm active:cursor-grabbing">
                  <div className="flex items-start justify-between gap-2">
                    <Link to={to(`/applicants/${a.id}`)} className="min-w-0 font-semibold text-primary-800">{a.full_name}</Link>
                    {a.score != null && <Score value={a.score} />}
                  </div>
                  <p className="mt-1 line-clamp-1 text-xs text-aeblack-500">{a.job_title}</p>
                  <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                    <Badge tone={screenTone(a.screening)}>{t(`screen.${a.screening}`)}</Badge>
                    <StatusPick value={statusOf(a)} name={a.full_name} onPick={(next) => onMove(a, next)} />
                  </div>
                </li>
              ))}
              {items.length === 0 && <li className="rounded-xl border border-dashed border-aeblack-200 px-3 py-6 text-center text-xs text-aeblack-500">{t("board.drop")}</li>}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

const csvCell = (v: unknown) => `"${String(v ?? "").replace(/"/g, '""')}"`;

export default function Applicants() {
  const { t, to, date, lang } = useLang();
  const toast = useToast();
  usePageTitle(t("apps.title"));
  const lookups = useLookups();
  const [picked, setPicked] = useState<(string | number)[]>([]);
  const [inviting, setInviting] = useState(false);
  const [busy, setBusy] = useState(false);
  const jobs = useAsync(() => api<{ items: { id: number; title: string; title_en: string }[] }>("/jobs?size=100&sort=created&dir=desc"), []);
  const list = useList<Row>("/applicants", ["job", "status", "screening", "ai", "minScore", "from", "to", "view"]);
  const board = list.get("view") === "board";
  const status = useDeferredStatus(list.reload);
  const rows = list.data?.items || [];
  const statusOf = (a: Row) => status.shown[a.id] || a.status;
  const chosen = rows.filter((a) => picked.includes(a.id));
  const setStatus = (items: Row[], s: string) => status.change(items.map((a) => a.id), s, Object.fromEntries(items.map((a) => [a.id, statusOf(a)])));
  const invite = async (body: unknown) => {
    setBusy(true);
    let ok = 0;
    for (const a of chosen) { try { await api(`/applicants/${a.id}/invite`, { body }); ok++; } catch { /* counted below */ } }
    setBusy(false);
    setInviting(false);
    toast(t("bulk.invited", { ok, count: chosen.length }), ok === chosen.length ? "success" : "error");
    list.reload();
  };
  const exportCsv = () => {
    const head = [t("common.name"), t("auth.email"), "Code", t("apps.job"), t("common.status"), t("apps.screening"), t("apps.score"), t("apps.applied")];
    const lines = chosen.map((a) => [a.full_name, a.email, a.code, a.job_title, t(`app.status.${statusOf(a)}`), t(`screen.${a.screening}`), a.score ?? "", a.created_at.slice(0, 10)]);
    const blob = new Blob(["\ufeff" + [head, ...lines].map((l) => l.map(csvCell).join(",")).join("\r\n")], { type: "text/csv;charset=utf-8" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `applicants-${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  };
  return (
    <>
      <PageHeader title={t("apps.title")} subtitle={list.data && t("apps.total", { count: list.data.total })}
        actions={<Segmented label={t("board.view")} value={board ? "board" : "list"} onChange={(v) => list.set({ view: v === "board" ? "board" : null, size: v === "board" ? "100" : null })}
          options={[{ value: "list", label: <><ListBullets size={18} aria-hidden="true" />{t("board.list")}</> }, { value: "board", label: <><Kanban size={18} aria-hidden="true" />{t("board.board")}</> }]} />} />
      <DataTable list={list} rowKey={(a) => a.id} emptyHint={t("empty.applicants")} searchLabel={t("apps.search_hint")}
        body={board && list.data ? <>
          {list.data.total > list.data.items.length && <p className="mb-3 text-sm text-aeblack-600">{t("board.partial", { shown: list.data.items.length, total: list.data.total })}</p>}
          <Board rows={rows} statusOf={statusOf} onMove={(a, s) => setStatus([a], s)} />
        </> : undefined}
        selection={board ? undefined : { selected: picked, onChange: setPicked, label: (a) => (a as Row).full_name }}
        bulk={<>
          <ActionMenu label={t("app.change_status")} items={STATUSES.map((s) => ({ label: t(`app.status.${s}`), onClick: () => setStatus(chosen, s) }))} />
          <Button variant="outline" size="sm" onClick={() => setInviting(true)} icon={<Exam size={20} aria-hidden="true" />}>{t("app.invite")}</Button>
          <Button variant="outline" size="sm" onClick={exportCsv} icon={<DownloadSimple size={20} aria-hidden="true" />}>{t("bulk.export")}</Button>
        </>}
        filters={[
          { key: "job", label: t("apps.job"), options: (jobs.data?.items || []).map((j) => ({ value: String(j.id), label: lang === "en" && j.title_en ? j.title_en : j.title })) },
          { key: "status", label: t("common.status"), options: opts(t, "app.status.", STATUSES) },
          { key: "screening", label: t("apps.screening"), options: opts(t, "screen.", ["Meets", "DoesNotMeet", "NoRequirements", "NoQuestions"]) },
          { key: "ai", label: t("apps.ai"), options: opts(t, "ai.", ["Scored", "Waiting", "Pending", "Processing", "Failed", "Skipped"]) },
          { key: "minScore", label: t("apps.min_score"), options: ["50", "60", "70", "80", "90"].map((v) => ({ value: v, label: `${v}+` })) },
        ]}
        toolbar={<>
          <TextInput wrapClass="w-40" label={t("apps.from")} type="date" value={list.get("from")} onChange={(e) => list.set({ from: e.target.value || null })} />
          <TextInput wrapClass="w-40" label={t("apps.to")} type="date" value={list.get("to")} onChange={(e) => list.set({ to: e.target.value || null })} />
        </>}
        columns={[
          { key: "name", label: t("common.name"), sort: "name", render: (a) => <>
            <Link to={to(`/applicants/${a.id}`)}>{a.full_name}</Link>
            <p className="text-xs text-aeblack-500" dir="ltr">{a.code}</p>
          </> },
          { key: "job", label: t("apps.job"), sort: "job", className: "max-w-64", render: (a) => <span className="line-clamp-2">{a.job_title}</span> },
          { key: "status", label: t("common.status"), sort: "status", render: (a) => <StatusPick value={statusOf(a)} name={a.full_name} onPick={(s) => setStatus([a], s)} /> },
          { key: "screening", label: t("apps.screening"), sort: "screening", render: (a) => <Badge tone={screenTone(a.screening)}>{t(`screen.${a.screening}`)}</Badge> },
          { key: "score", label: t("apps.score"), sort: "score", render: (a) => a.ai_status && a.ai_status !== "Scored"
            ? <span className="text-xs text-aeblack-500">{t(`ai.${a.ai_status}`)}</span> : <Score value={a.score} /> },
          { key: "created", label: t("apps.applied"), sort: "created", className: "whitespace-nowrap", render: (a) => date(a.created_at) },
        ]} />
      {inviting && <InviteDialog open onClose={() => setInviting(false)} tests={lookups.data?.assessments || []} busy={busy} onSave={invite} />}
    </>
  );
}
