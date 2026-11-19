import { Popover } from "@base-ui/react";
import { Bell, CalendarCheck, ChatCircleText, CheckCircle, ClipboardText, Handshake, Sparkle, UserPlus, XCircle } from "@phosphor-icons/react";
import { ReactNode, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, useLang } from "../core";
import { Toggle } from "./ui";

type Item = { kind: string; name: string; at: string; link: string; count: number };
type Data = { items: Item[]; daily_digest: boolean; weekly_report: boolean; email: boolean };

const ICONS: Record<string, ReactNode> = {
  interview_soon: <CalendarCheck size={20} />, feedback_due: <ChatCircleText size={20} />, approval: <CheckCircle size={20} />,
  marking: <ClipboardText size={20} />, new_applications: <UserPlus size={20} />, offer_accepted: <Handshake size={20} />, offer_declined: <XCircle size={20} />,
};
const SEEN = "mh-notes-seen";
const seen = () => { try { return Number(localStorage.getItem(SEEN) || 0); } catch { return 0; } };

/**
 * The notifications bell: what needs this person (interviews soon, feedback owed, approvals, marking, new applications,
 * offer replies), refreshed every two minutes. The dot counts what arrived since the bell was last opened. The digest
 * and weekly summary emails are switched on here.
 */
export function NotificationBell() {
  const { t, to, date } = useLang();
  const [data, setData] = useState<Data | null>(null);
  const [last, setLast] = useState(seen);
  const [open, setOpen] = useState(false);
  const load = () => api<Data>("/notifications").then(setData).catch(() => { /* the bell stays quiet */ });
  useEffect(() => {
    load();
    const timer = setInterval(() => { if (!document.hidden) load(); }, 120_000);
    return () => clearInterval(timer);
  }, []);
  const fresh = (data?.items || []).filter((i) => new Date(i.at).getTime() > last).length;
  const onOpen = (o: boolean) => {
    setOpen(o);
    if (o) { load(); const now = Date.now(); try { localStorage.setItem(SEEN, String(now)); } catch { /* private window */ } setTimeout(() => setLast(now), 1500); }
  };
  const prefs = async (p: Partial<Pick<Data, "daily_digest" | "weekly_report">>) => {
    if (!data) return;
    const next = { daily_digest: data.daily_digest, weekly_report: data.weekly_report, ...p };
    setData({ ...data, ...next });
    await api("/notifications/prefs", { method: "PUT", body: next }).catch(() => load());
  };
  const label = (i: Item) => {
    const [name, time] = i.name.split("|");
    return t(`notes.${i.kind}`, { name, time: time || "", count: i.count });
  };
  return (
    <Popover.Root open={open} onOpenChange={onOpen}>
      <Popover.Trigger className="relative inline-flex size-10 items-center justify-center rounded-lg text-aeblack-700 hover:bg-whitely-100"
        aria-label={fresh > 0 ? t("notes.open_new", { count: fresh }) : t("notes.open")}>
        <Bell size={22} aria-hidden="true" />
        {fresh > 0 && <span className="absolute end-1.5 top-1.5 min-w-4 rounded-full bg-aered-600 px-1 text-center text-[10px] font-bold leading-4 text-white" dir="ltr">{fresh > 9 ? "9+" : fresh}</span>}
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Positioner sideOffset={8} align="end" className="z-[80]">
          <Popover.Popup className="mh-popup w-[min(24rem,calc(100vw-1.5rem))] p-0">
            <div className="flex items-center justify-between border-b border-aeblack-100 px-4 py-3">
              <Popover.Title className="text-base font-bold">{t("notes.title")}</Popover.Title>
              {data && <span className="text-xs text-aeblack-500">{t("notes.count", { count: data.items.length })}</span>}
            </div>
            <ul className="max-h-[60vh] overflow-y-auto p-2">
              {data && data.items.length === 0 && (
                <li className="flex flex-col items-center gap-2 px-4 py-8 text-center text-sm text-aeblack-500"><Sparkle size={28} className="text-primary-500" aria-hidden="true" />{t("notes.none")}</li>
              )}
              {data?.items.map((i, n) => {
                const isNew = new Date(i.at).getTime() > last;
                return (
                  <li key={n}>
                    <Link to={to(i.link)} onClick={() => setOpen(false)} className="flex items-start gap-3 rounded-xl px-3 py-2.5 text-aeblack-800 no-underline hover:bg-primary-50">
                      <span className={`mt-0.5 shrink-0 ${isNew ? "text-primary-700" : "text-aeblack-400"}`} aria-hidden="true">{ICONS[i.kind]}</span>
                      <span className="min-w-0 flex-1">
                        <span className={`block text-sm ${isNew ? "font-bold" : "font-medium"}`} dir="auto">{label(i)}</span>
                        <span className="block text-xs text-aeblack-500">{date(i.at, true)}</span>
                      </span>
                      {isNew && <span className="mt-2 size-2 shrink-0 rounded-full bg-primary-600" aria-label={t("notes.new")} />}
                    </Link>
                  </li>
                );
              })}
            </ul>
            {data && (
              <div className="space-y-1 border-t border-aeblack-100 px-4 py-3">
                <p className="text-xs font-bold uppercase tracking-wide text-aeblack-500">{t("notes.emails")}</p>
                <Toggle label={t("notes.daily")} checked={data.daily_digest} onChange={(v) => prefs({ daily_digest: v })} />
                <Toggle label={t("notes.weekly")} checked={data.weekly_report} onChange={(v) => prefs({ weekly_report: v })} />
                {!data.email && <p className="text-xs text-aeblack-500">{t("notes.no_email")}</p>}
              </div>
            )}
          </Popover.Popup>
        </Popover.Positioner>
      </Popover.Portal>
    </Popover.Root>
  );
}
