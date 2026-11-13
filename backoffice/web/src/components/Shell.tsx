import { Dialog, Menu } from "@base-ui/react";
import {
  Briefcase, CalendarCheck, CaretDown, ChatCircleDots, ClipboardText, Exam, FileText, Gear, Handshake, Key, List, ListChecks,
  Cube, EnvelopeSimple, HardDrives, Robot, Scroll, SignOut, SquaresFour, Target, Tray, Translate, TreeStructure, UserCircle, Users, UsersThree, X, Brain, ChartBar, ClockCounterClockwise, Pulse, ChartLine, ShieldCheck, ChatText, UsersFour, ArrowsLeftRight, MapPin, Stamp, DeviceMobile, GlobeHemisphereEast, IdentificationCard, CaretUpDown, SidebarSimple } from "@phosphor-icons/react";
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
    { items: [{ to: "/", label: t("nav.dashboard"), icon: i(SquaresFour) }, { to: "/reports", label: t("nav.reports"), icon: i(ChartBar) }] },
    {
      group: t("nav.group.hiring"),
      items: [
        { to: "/jobs", label: t("nav.jobs"), icon: i(Briefcase) },
        { to: "/applicants", label: t("nav.applicants"), icon: i(Users) },
        { to: "/interviews", label: t("nav.interviews"), icon: i(CalendarCheck) },
        { to: "/offers", label: t("nav.offers"), icon: i(Handshake) },
        { to: "/talent-pool", label: t("nav.talent_pool"), icon: i(UsersFour) },
        { to: "/candidates", label: t("nav.candidates"), icon: i(IdentificationCard), admin: true },
        { to: "/screening", label: t("nav.screening"), icon: i(ListChecks) },
        { to: "/requisitions", label: t("nav.requisitions"), icon: i(ClipboardText) },
        { to: "/approvals", label: t("nav.approvals"), icon: i(Stamp) },
      ],
    },
    {
      group: t("nav.group.assessment"),
      items: [
        { to: "/tests", label: t("nav.assessments"), icon: i(Exam) },
        { to: "/invitations", label: t("nav.invitations"), icon: i(FileText) },
        { to: "/questions", label: t("nav.questions"), icon: i(ClipboardText) },
        { to: "/role-profiles", label: t("nav.role_profiles"), icon: i(Target) },
        { to: "/personality-settings", label: t("nav.personality_settings"), icon: i(Brain) },
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
        { to: "/setup/locations", label: t("nav.locations"), icon: i(MapPin), admin: true },
        { to: "/setup/countries", label: t("nav.countries"), icon: i(GlobeHemisphereEast), admin: true },
        { to: "/setup/rounds", label: t("nav.rounds"), icon: i(UsersThree), admin: true },
        { to: "/setup/settings", label: t("nav.settings"), icon: i(Gear), admin: true },
        { to: "/setup/portal-text", label: t("nav.portal_text"), icon: i(Translate), admin: true },
        { to: "/setup/email-templates", label: t("nav.email_templates"), icon: i(EnvelopeSimple), admin: true },
        { to: "/setup/document-templates", label: t("nav.document_templates"), icon: i(Scroll), admin: true },
      ],
    },
    {
      group: t("nav.group.system"),
      items: [
        { to: "/setup/staff", label: t("nav.staff"), icon: i(Key), sys: true },
        { to: "/setup/email-server", label: t("nav.email_server"), icon: i(HardDrives), sys: true },
        { to: "/setup/texts", label: t("nav.texts"), icon: i(ChatText), sys: true },
        { to: "/setup/handoff", label: t("nav.handoff"), icon: i(ArrowsLeftRight), sys: true },
        { to: "/setup/assistant", label: t("nav.agent_settings"), icon: i(Robot), sys: true },
        { to: "/setup/email-log", label: t("nav.email_log"), icon: i(Tray), sys: true },
        { to: "/setup/assistant-usage", label: t("nav.assistant_usage"), icon: i(ChartLine), sys: true },
        { to: "/setup/audit", label: t("nav.audit"), icon: i(ClockCounterClockwise), sys: true },
        { to: "/setup/data-protection", label: t("nav.data_protection"), icon: i(ShieldCheck), sys: true },
        { to: "/setup/health", label: t("nav.health"), icon: i(Pulse), sys: true },
      ],
    },
  ];
}

function NavList({ onPick, collapsed = false }: { onPick?: () => void; collapsed?: boolean }) {
  const { to, t } = useLang();
  const { me } = useAuth();
  const groups = useNav();
  return (
    <nav className={collapsed ? "space-y-3 px-2 py-4" : "space-y-6 p-4"} aria-label={t("app.menu")}>
      {groups.map((g, gi) => {
        const items = g.items.filter((it) => (!it.admin || me?.can_administer) && (!it.sys || me?.is_administrator));
        if (!items.length) return null;
        return (
          <div key={gi}>
            {g.group && (collapsed
              ? gi > 0 && <hr className="mx-2 mb-3 border-aeblack-100" aria-hidden="true" />
              : <p className="mb-2 px-3 text-xs font-bold uppercase tracking-wide text-aeblack-500">{g.group}</p>)}
            <ul className="space-y-1">
              {items.map((it) => (
                <li key={it.to}>
                  <NavLink to={to(it.to)} end={it.to === "/"} onClick={onPick} title={collapsed ? it.label : undefined}
                    className={({ isActive }) => `mh-nav-item ${collapsed ? "is-collapsed" : ""} ${isActive ? "is-active" : ""}`}>
                    {it.icon}
                    <span className={collapsed ? "sr-only" : "min-w-0"}>{it.label}</span>
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

/** The menu's width: full, or icons only. Remembered in this browser. */
function useCollapsed() {
  const [collapsed, set] = useState(() => { try { return localStorage.getItem("mh-nav-collapsed") === "1"; } catch { return false; } });
  const toggle = () => set((c) => { try { localStorage.setItem("mh-nav-collapsed", c ? "0" : "1"); } catch { /* not kept */ } return !c; });
  return [collapsed, toggle] as const;
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

function UserMenu({ placement = "header", collapsed = false }: { placement?: "header" | "sidebar"; collapsed?: boolean }) {
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
  const initials = me.full_name.trim().split(/\s+/).slice(0, 2).map((w) => w[0]).join("").toUpperCase();
  const sidebar = placement === "sidebar";
  return (
    <Menu.Root>
      {sidebar ? (
        <Menu.Trigger className={`mh-user ${collapsed ? "is-collapsed" : ""}`} aria-label={`${me.full_name}, ${t(`role.${me.role}`)}`} title={collapsed ? me.full_name : undefined}>
          <span className="grid size-9 shrink-0 place-items-center rounded-full bg-primary-600 text-sm font-bold text-white" aria-hidden="true">{initials}</span>
          {!collapsed && <>
            <span className="min-w-0 flex-1 text-start leading-tight">
              <span className="block truncate text-sm font-bold text-aeblack-900">{me.full_name}</span>
              <span className="block truncate text-xs text-aeblack-500">{t(`role.${me.role}`)}</span>
            </span>
            <CaretUpDown size={16} className="shrink-0 text-aeblack-500" aria-hidden="true" />
          </>}
        </Menu.Trigger>
      ) : (
        <Menu.Trigger className="inline-flex items-center gap-2 rounded-lg px-2 py-2 text-sm font-semibold text-aeblack-800 hover:bg-aeblack-100" aria-label={me.full_name}>
          <UserCircle size={24} aria-hidden="true" />
          <CaretDown size={16} className="hidden sm:block" aria-hidden="true" />
        </Menu.Trigger>
      )}
      <Menu.Portal>
        <Menu.Positioner sideOffset={8} side={sidebar ? "top" : "bottom"} align={sidebar ? "start" : "end"} className="z-[80]">
          <Menu.Popup className="mh-popup">
            <div className="flex items-center gap-3 px-3 py-3">
              <span className="grid size-10 shrink-0 place-items-center rounded-full bg-primary-100 text-sm font-bold text-primary-800" aria-hidden="true">
                {initials}
              </span>
              <div className="min-w-0 leading-tight">
                <div className="truncate text-sm font-bold text-aeblack-900">{me.full_name}</div>
                <div dir="ltr" className="mt-0.5 truncate text-left rtl:text-right text-xs text-aeblack-500">{me.email}</div>
                <span className="mt-1.5 inline-flex rounded-full bg-primary-50 px-2 py-0.5 text-xs font-semibold text-primary-800">{t(`role.${me.role}`)}</span>
              </div>
            </div>
            <Menu.Separator className="my-1 h-px bg-aeblack-100" />
            <Menu.Item className="mh-item" onClick={() => nav(to("/password"))}><Key size={20} aria-hidden="true" />{t("auth.change_password")}</Menu.Item>
            <Menu.Item className="mh-item" onClick={() => nav(to("/two-step"))}><DeviceMobile size={20} aria-hidden="true" />{t("twostep.title")}</Menu.Item>
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
          <Dialog.Title className="sr-only">{t("asst.title")}</Dialog.Title>
          <div className="min-h-0 flex-1"><ErrorBoundary compact><AgentChat panel onClose={() => setOpen(false)} /></ErrorBoundary></div>
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
  const [collapsed, toggleCollapsed] = useCollapsed();
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
            {/* On wide screens the account sits at the bottom of the side menu. */}
            <div className="lg:hidden"><UserMenu /></div>
          </div>
        </div>
      </header>
      {!/\/assistant$/.test(loc.pathname) && <AssistantPanel />}
      <div className="flex">
        <aside className={`sticky top-20 hidden h-[calc(100vh-5rem)] flex-none flex-col border-e border-aeblack-100 bg-white transition-[width] duration-200 lg:flex ${collapsed ? "w-[4.5rem]" : "w-64"}`}>
          <div className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden"><NavList collapsed={collapsed} /></div>
          <div className={`space-y-1 border-t border-aeblack-100 ${collapsed ? "p-2" : "p-3"}`}>
            <button type="button" onClick={toggleCollapsed} aria-expanded={!collapsed} title={collapsed ? t("app.expand_menu") : undefined}
              className={`mh-nav-item w-full ${collapsed ? "is-collapsed" : ""}`}>
              <SidebarSimple size={22} className="rtl:-scale-x-100" aria-hidden="true" />
              <span className={collapsed ? "sr-only" : ""}>{collapsed ? t("app.expand_menu") : t("app.collapse_menu")}</span>
            </button>
            <UserMenu placement="sidebar" collapsed={collapsed} />
          </div>
        </aside>
        <main id="main" className="mh-main min-w-0 flex-1 px-4 py-6 sm:px-8 sm:py-8"><ErrorBoundary key={loc.pathname}>{children}</ErrorBoundary></main>
      </div>
    </div>
  );
}
