import { Dialog, Menu } from "@base-ui/react";
import {
  Briefcase, CalendarCheck, CaretDown, ChatCircleDots, ClipboardText, Exam, FileText, Gear, Handshake, Key, List, ListChecks,
  Cube, EnvelopeSimple, HardDrives, Robot, Scroll, SignOut, SquaresFour, Target, Tray, Translate, TreeStructure, UserCircle, Users, UsersThree, X,
} from "@phosphor-icons/react";
import { ReactNode, useEffect, useState } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
import { api, useAuth, useLang } from "../core";
import { AgentChat } from "../agent/AgentChat";
import { ErrorBoundary } from "./ErrorBoundary";
import { LangFlag } from "./flags";

type Item = { to: string; label: string; icon: ReactNode; admin?: boolean; sys?: boolean };

function useNav(): { group?: string; items: Item[] }[] {
  const { t } = useLang();
  const i = (C: typeof Briefcase) => <C size={24} aria-hidden="true" />;
  return [
    { items: [{ to: "/", label: t("nav.dashboard"), icon: i(SquaresFour) }] },
    {
      group: t("nav.group.hiring"),
      items: [
        { to: "/jobs", label: t("nav.jobs"), icon: i(Briefcase) },
        { to: "/applicants", label: t("nav.applicants"), icon: i(Users) },
        { to: "/interviews", label: t("nav.interviews"), icon: i(CalendarCheck) },
        { to: "/offers", label: t("nav.offers"), icon: i(Handshake) },
        { to: "/setup/offer-templates", label: t("nav.offer_templates"), icon: i(Scroll), admin: true },
      ],
    },
    {
      group: t("nav.group.assessment"),
      items: [
        { to: "/tests", label: t("nav.assessments"), icon: i(Exam) },
        { to: "/questions", label: t("nav.questions"), icon: i(ClipboardText) },
        { to: "/invitations", label: t("nav.invitations"), icon: i(FileText) },
        { to: "/screening", label: t("nav.screening"), icon: i(ListChecks) },
        { to: "/role-profiles", label: t("nav.role_profiles"), icon: i(Target) },
      ],
    },
    {
      group: t("nav.group.assistant"),
      items: [
        { to: "/assistant", label: t("nav.assistant"), icon: i(ChatCircleDots) },
        { to: "/setup/components", label: t("nav.components"), icon: i(Cube), admin: true },
      ],
    },
    {
      group: t("nav.setup"),
      items: [
        { to: "/setup/departments", label: t("nav.departments"), icon: i(TreeStructure), admin: true },
        { to: "/setup/rounds", label: t("nav.rounds"), icon: i(UsersThree), admin: true },
        { to: "/setup/settings", label: t("nav.settings"), icon: i(Gear), admin: true },
        { to: "/setup/portal-text", label: t("nav.portal_text"), icon: i(Translate), admin: true },
        { to: "/setup/email-templates", label: t("nav.email_templates"), icon: i(EnvelopeSimple), admin: true },
      ],
    },
    {
      group: t("nav.group.system"),
      items: [
        { to: "/setup/staff", label: t("nav.staff"), icon: i(Key), sys: true },
        { to: "/setup/email-server", label: t("nav.email_server"), icon: i(HardDrives), sys: true },
        { to: "/setup/assistant", label: t("nav.agent_settings"), icon: i(Robot), sys: true },
        { to: "/setup/email-log", label: t("nav.email_log"), icon: i(Tray), sys: true },
      ],
    },
  ];
}

function NavList({ onPick }: { onPick?: () => void }) {
  const { to } = useLang();
  const { me } = useAuth();
  const groups = useNav();
  return (
    <nav className="space-y-6 p-4" aria-label="Main">
      {groups.map((g, gi) => {
        const items = g.items.filter((it) => (!it.admin || me?.can_administer) && (!it.sys || me?.is_administrator));
        if (!items.length) return null;
        return (
          <div key={gi}>
            {g.group && <p className="mb-2 px-3 text-xs font-bold uppercase tracking-wide text-aeblack-400">{g.group}</p>}
            <ul className="space-y-1">
              {items.map((it) => (
                <li key={it.to}>
                  <NavLink to={to(it.to)} end={it.to === "/"} onClick={onPick}
                    className={({ isActive }) => `flex items-center gap-3 rounded-lg px-3 py-2.5 font-semibold transition ${isActive ? "bg-primary-50 text-primary-800" : "text-aeblack-700 hover:bg-aeblack-50 hover:text-primary-700"}`}>
                    {it.icon}
                    <span>{it.label}</span>
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </nav>
  );
}

function Brand() {
  const { t, lang, to } = useLang();
  return (
    <Link to={to("/")} className="flex min-w-0 shrink items-center gap-3" dir="ltr" aria-label={`${t("app.ministry")}, ${t("app.badge")}`}>
      <img src={lang === "ar" ? "/logo-ar.png" : "/logo-en.png"} alt="" width={906} height={227} className="mh-logo h-9 sm:h-12" />
      <span className="hidden h-8 w-px bg-aeblack-200 sm:block" aria-hidden="true" />
      <span dir={lang === "ar" ? "rtl" : "ltr"} className="hidden rounded-full bg-primary-100 px-3 py-1 text-sm font-bold text-primary-800 sm:inline">{t("app.badge")}</span>
    </Link>
  );
}

function LanguageSwitch() {
  const { lang, t } = useLang();
  const loc = useLocation();
  const other = lang === "ar" ? "en" : "ar";
  const path = loc.pathname.replace(/^\/(ar|en)/, `/${other}`) + loc.search;
  return (
    <Link to={path} lang={other} aria-label={t("lang.switch")} className="inline-flex items-center gap-2 rounded-lg px-2 py-2 text-sm font-semibold text-aeblack-700 hover:bg-aeblack-100 sm:px-3">
      <LangFlag lang={other} />
      <span className="hidden sm:inline">{t("lang.switch")}</span>
    </Link>
  );
}

function UserMenu() {
  const { t, to } = useLang();
  const { me, setMe } = useAuth();
  const nav = useNavigate();
  if (!me) return null;
  const signOut = async () => {
    try {
      await api("/sign-out", { body: {} });
    } finally {
      setMe(null);
      nav(to("/sign-in"));
    }
  };
  return (
    <Menu.Root>
      <Menu.Trigger className="inline-flex items-center gap-2 rounded-lg px-2 py-2 text-sm font-semibold text-aeblack-800 hover:bg-aeblack-100">
        <UserCircle size={24} aria-hidden="true" />
        <span className="hidden max-w-40 truncate md:inline">{me.full_name}</span>
        <CaretDown size={16} className="hidden sm:block" aria-hidden="true" />
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Positioner sideOffset={6} align="end">
          <Menu.Popup className="mh-popup">
            <div className="px-3 py-2 text-xs text-aeblack-500">
              <p className="font-semibold text-aeblack-800">{me.full_name}</p>
              <p dir="ltr" className="text-start">{me.email}</p>
              <p>{t(`role.${me.role}`)}</p>
            </div>
            <Menu.Separator className="my-1 h-px bg-aeblack-100" />
            <Menu.Item className="mh-item" onClick={() => nav(to("/password"))}><Key size={20} aria-hidden="true" />{t("auth.change_password")}</Menu.Item>
            <Menu.Item className="mh-item" onClick={signOut}><SignOut size={20} aria-hidden="true" />{t("auth.sign_out")}</Menu.Item>
          </Menu.Popup>
        </Menu.Positioner>
      </Menu.Portal>
    </Menu.Root>
  );
}

/** The assistant as a side panel on every page; it sees the screen behind it. */
function AssistantPanel() {
  const { t } = useLang();
  const [open, setOpen] = useState(false);
  return (
    <Dialog.Root open={open} onOpenChange={setOpen} modal={false}>
      <Dialog.Trigger className="aegov-btn mh-assistant-fab" aria-label={t("asst.title")}>
        <ChatCircleDots size={24} aria-hidden="true" />
        <span className="hidden sm:inline">{t("nav.assistant")}</span>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Popup className="mh-chat-drawer">
          <div className="flex items-center justify-between border-b border-aeblack-100 bg-white p-3">
            <Dialog.Title className="inline-flex items-center gap-2 text-lg font-bold"><ChatCircleDots size={24} className="text-primary-600" aria-hidden="true" />{t("asst.title")}</Dialog.Title>
            <Dialog.Close className="rounded p-1 hover:bg-aeblack-100" aria-label={t("app.close")}><X size={24} /></Dialog.Close>
          </div>
          <div className="min-h-0 flex-1"><ErrorBoundary compact><AgentChat panel /></ErrorBoundary></div>
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function Shell({ children }: { children: ReactNode }) {
  const { t, lang, dir } = useLang();
  const [open, setOpen] = useState(false);
  const loc = useLocation();
  useEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = dir;
  }, [lang, dir]);
  useEffect(() => setOpen(false), [loc.pathname]);
  const menu = (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger className="rounded-lg p-2 text-aeblack-700 hover:bg-aeblack-100 lg:hidden" aria-label={t("app.menu")}><List size={24} /></Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Backdrop className="mh-backdrop" />
        <Dialog.Popup className="mh-drawer">
          <div className="flex items-center justify-between border-b border-aeblack-100 p-4">
            <Dialog.Title className="text-lg font-bold">{t("app.menu")}</Dialog.Title>
            <Dialog.Close className="rounded p-1 hover:bg-aeblack-100" aria-label={t("app.close")}><X size={24} /></Dialog.Close>
          </div>
          <NavList onPick={() => setOpen(false)} />
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
  return (
    <div className="min-h-screen">
      <a href="#main" className="mc-skip aegov-btn btn-sm">{t("app.skip")}</a>
      <header className="sticky top-0 z-30 border-b border-aeblack-100 bg-white">
        {/* The logo is always on the left, in Arabic and English: the bar is laid out left to right. */}
        {/* The menu button sits on the reading-start side: the far left in English (before the logo), the far right in Arabic. */}
        <div dir="ltr" className="flex h-16 items-center justify-between gap-2 px-3 sm:h-20 sm:gap-3 sm:px-6">
          <div className="flex min-w-0 items-center gap-1">
            {dir === "ltr" && menu}
            <Brand />
          </div>
          <div dir={dir} className="flex shrink-0 items-center gap-0.5 sm:gap-1">
            {dir === "rtl" && menu}
            <LanguageSwitch />
            <UserMenu />
          </div>
        </div>
      </header>
      <div className="flex">
        <aside className="sticky top-20 hidden h-[calc(100vh-5rem)] w-64 flex-none overflow-y-auto border-e border-aeblack-100 bg-white lg:block">
          <NavList />
        </aside>
        <main id="main" className="min-w-0 flex-1 px-4 py-6 sm:px-8 sm:py-8"><ErrorBoundary key={loc.pathname}>{children}</ErrorBoundary></main>
      </div>
      {!/\/assistant$/.test(loc.pathname) && <AssistantPanel />}
    </div>
  );
}
