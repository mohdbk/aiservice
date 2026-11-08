import {
  ArrowCounterClockwise, ArrowSquareOut, Briefcase, CaretDown, CircleHalf, Files, Globe, List, PersonArmsSpread,
  SignIn as SignInIcon, SignOut, TextAa, UserCircle, UserPlus, X,
} from "@phosphor-icons/react";
import { ReactNode, RefObject, useEffect, useRef, useState } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../auth";
import { useSite } from "../site";

const store = {
  get: (k: string) => {
    try {
      return localStorage.getItem(k);
    } catch {
      return null;
    }
  },
  set: (k: string, v: string) => {
    try {
      localStorage.setItem(k, v);
    } catch {
      /* storage blocked */
    }
  },
};
const SIZES = ["sm", "md", "lg", "xl"] as const;

function applyA11y(size: string, contrast: boolean) {
  const html = document.documentElement;
  html.classList.remove("mc-text-sm", "mc-text-lg", "mc-text-xl");
  if (size !== "md") html.classList.add(`mc-text-${size}`);
  html.classList.toggle("mc-contrast", contrast);
}

function usePopover(open: boolean, close: () => void, ref: RefObject<HTMLElement>, button: RefObject<HTMLElement>) {
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) close(); };
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") { close(); button.current?.focus(); } };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("keydown", onKey); };
  }, [open, close, ref, button]);
}

const barBtn = "inline-flex items-center gap-1.5 rounded px-2 py-1 text-aeblack-700 hover:bg-aeblack-100 hover:text-primary-700";

function A11yTools() {
  const { t } = useSite();
  const [size, setSize] = useState(() => store.get("mc-text") || "md");
  const [contrast, setContrast] = useState(() => store.get("mc-contrast") === "1");
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    applyA11y(size, contrast);
    store.set("mc-text", size);
    store.set("mc-contrast", contrast ? "1" : "0");
  }, [size, contrast]);
  usePopover(open, () => setOpen(false), ref, buttonRef);
  const idx = SIZES.indexOf(size as (typeof SIZES)[number]);
  const step = (d: number) => setSize(SIZES[Math.max(0, Math.min(3, idx + d))]);
  const sizeBtn = "flex h-11 flex-1 items-center justify-center rounded-lg border font-semibold disabled:opacity-40";
  return (
    <div className="relative" ref={ref}>
      <button ref={buttonRef} type="button" onClick={() => setOpen((o) => !o)} aria-expanded={open} aria-controls="a11y-panel" className={barBtn}>
        <PersonArmsSpread size={20} aria-hidden="true" />
        <span className="hidden sm:inline">{t("a11y.tools")}</span>
        <span className="sr-only sm:hidden">{t("a11y.tools")}</span>
        <CaretDown size={16} className={`transition ${open ? "rotate-180" : ""}`} aria-hidden="true" />
      </button>
      {open && (
        <div id="a11y-panel" role="group" aria-label={t("a11y.tools")}
          className="fixed inset-x-4 top-12 z-40 rounded-xl border border-aeblack-100 bg-white p-4 text-aeblack-800 shadow-xl sm:absolute sm:inset-x-auto sm:end-0 sm:top-auto sm:mt-2 sm:w-72">
          <p className="mb-3 flex items-center gap-2 text-sm font-semibold"><TextAa size={24} className="text-primary-600" aria-hidden="true" />{t("a11y.text_size")}</p>
          <div className="flex gap-2" dir="ltr">
            <button type="button" className={`${sizeBtn} border-aeblack-200 text-sm hover:bg-primary-50`} onClick={() => step(-1)} disabled={idx <= 0} aria-label={t("a11y.smaller")}>A-</button>
            <button type="button" className={`${sizeBtn} ${size === "md" ? "border-primary-600 bg-primary-50 text-primary-800" : "border-aeblack-200 hover:bg-primary-50"}`} onClick={() => setSize("md")} aria-pressed={size === "md"} aria-label={t("a11y.default")}>A</button>
            <button type="button" className={`${sizeBtn} border-aeblack-200 text-lg hover:bg-primary-50`} onClick={() => step(1)} disabled={idx >= 3} aria-label={t("a11y.larger")}>A+</button>
          </div>
          <div className="mt-4 flex items-center justify-between gap-3 border-t border-aeblack-100 pt-4">
            <span className="flex items-center gap-2 text-sm font-semibold"><CircleHalf size={24} className="text-primary-600" aria-hidden="true" />{t("a11y.contrast")}</span>
            {/* UAE Design System toggle */}
            <label className="aegov-toggle">
              <input type="checkbox" role="switch" className="peer sr-only" checked={contrast} onChange={(e) => setContrast(e.target.checked)} aria-label={t("a11y.contrast")} />
              <span className="toggle-item" aria-hidden="true" />
            </label>
          </div>
          {(size !== "md" || contrast) && (
            <button type="button" className="aegov-btn btn-link btn-sm mt-3 w-full" onClick={() => { setSize("md"); setContrast(false); }}>
              <ArrowCounterClockwise size={20} aria-hidden="true" />{t("a11y.reset")}
            </button>
          )}
        </div>
      )}
    </div>
  );
}

function LanguageSwitcher() {
  const { config, lang, t } = useSite();
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  usePopover(open, () => setOpen(false), ref, buttonRef);
  const others = config.languages.filter((l) => l.code !== lang);
  const to = (code: string) => `/${code}${location.pathname.slice(lang.length + 1)}${location.search}`;
  if (!others.length) return null;
  if (others.length === 1) {
    const o = others[0];
    return (
      <Link to={to(o.code)} lang={o.code} hrefLang={o.code} className={`${barBtn} font-semibold text-aeblack-900`}>
        <Globe size={20} aria-hidden="true" />{o.label}
      </Link>
    );
  }
  return (
    <div className="relative" ref={ref}>
      <button ref={buttonRef} type="button" className={`${barBtn} font-semibold`} aria-expanded={open} aria-label={t("a11y.language")} onClick={() => setOpen((o) => !o)}>
        <Globe size={20} aria-hidden="true" />{config.languages.find((l) => l.code === lang)?.label}<CaretDown size={16} aria-hidden="true" />
      </button>
      {open && (
        <ul className="fixed inset-x-4 top-12 z-40 rounded-xl border border-aeblack-100 bg-white py-1 shadow-xl sm:absolute sm:inset-x-auto sm:end-0 sm:top-auto sm:mt-2 sm:min-w-40">
          {others.map((o) => (
            <li key={o.code}><Link to={to(o.code)} lang={o.code} hrefLang={o.code} className="block px-4 py-2 hover:bg-primary-50">{o.label}</Link></li>
          ))}
        </ul>
      )}
    </div>
  );
}

// Official ministry logo per language (Arabic for /ar, English otherwise). A logo uploaded in
// MoET Careers Settings replaces it.
export function logoSrc(lang: string, uploaded: string, dark = false) {
  if (uploaded) return dark ? "/api/logo?dark=1" : "/api/logo";
  return lang === "ar" ? "/logo-ar.png" : "/logo-en.png";
}

function Brand() {
  const { config, t, path, lang } = useSite();
  return (
    <Link to={path()} className="flex min-w-0 items-center gap-2 sm:gap-3 md:gap-4" aria-label={`${t("site.portal")}, ${t("site.ministry")}`}>
      <img src={logoSrc(lang, config.logo)} alt={t("site.ministry")} className="h-9 w-auto max-w-[40vw] sm:h-14 sm:max-w-none md:h-[72px]" width={907} height={228} />
      <span className="hidden h-10 w-px shrink-0 bg-aeblack-200 sm:block md:h-12" aria-hidden="true" />
      <span className="inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full bg-primary-600 px-2.5 py-1 text-xs font-bold text-whitely-50 shadow-sm sm:gap-2 sm:px-3 sm:py-1.5 sm:text-sm md:px-4 md:py-2 md:text-lg" dir={config.dir}>
        <Briefcase size={24} className="hidden sm:block" aria-hidden="true" />
        {t("site.portal")}
      </span>
    </Link>
  );
}

function Nav({ onNavigate }: { onNavigate?: () => void }) {
  const { t, path } = useSite();
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const link = ({ isActive }: { isActive: boolean }) =>
    `flex items-center gap-2 rounded-lg px-3 py-2 font-medium hover:bg-primary-50 ${isActive ? "bg-primary-50 text-primary-700" : "text-aeblack-800"}`;
  return (
    <ul className="flex flex-col gap-1 md:flex-row md:items-center md:gap-2">
      <li><NavLink end to={path()} className={link} onClick={onNavigate}><Briefcase size={24} aria-hidden="true" />{t("nav.jobs")}</NavLink></li>
      {user ? (
        <>
          <li><NavLink to={path("/applications")} className={link} onClick={onNavigate}><Files size={24} aria-hidden="true" />{t("nav.my_applications")}</NavLink></li>
          <li className="flex items-center gap-2 px-3 py-2 text-sm text-aeblack-600 md:max-w-56" title={user.email}>
            <UserCircle size={24} className="shrink-0 text-primary-600" aria-hidden="true" /><span className="truncate">{user.full_name || user.email}</span>
          </li>
          <li>
            <button type="button" className="aegov-btn btn-outline btn-sm" onClick={async () => { await signOut(); onNavigate?.(); navigate(path()); }}>
              <SignOut size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("nav.sign_out")}
            </button>
          </li>
        </>
      ) : (
        <>
          <li><Link className="aegov-btn btn-outline btn-sm" to={path("/sign-in")} onClick={onNavigate}><SignInIcon size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("nav.sign_in")}</Link></li>
          <li><Link className="aegov-btn btn-sm" to={path("/create-account")} onClick={onNavigate}><UserPlus size={20} aria-hidden="true" />{t("nav.create_account")}</Link></li>
        </>
      )}
    </ul>
  );
}

export default function Layout({ children }: { children: ReactNode }) {
  const { t, config, path, lang } = useSite();
  const [open, setOpen] = useState(false);
  const { pathname } = useLocation();
  useEffect(() => setOpen(false), [pathname]);
  return (
    <div className="flex min-h-screen flex-col">
      <a className="mc-skip aegov-btn btn-sm" href="#main">{t("a11y.skip")}</a>
      <header className="mc-surface sticky top-0 z-20 border-b border-aeblack-100 bg-white/95 backdrop-blur">
        <div className="border-b border-aeblack-100 bg-aeblack-50">
          <div className="container mx-auto flex items-center justify-between gap-2 px-4 py-1 text-sm">
            <p className="m-0 hidden truncate font-medium leading-none text-aeblack-800 sm:block">{t("site.official_note")}</p>
            <div className="ms-auto flex shrink-0 items-center gap-1">
              {config.main_site_url && (
                <>
                  <a className={barBtn} href={config.main_site_url} target="_blank" rel="noopener">
                    <ArrowSquareOut size={20} className="rtl:-scale-x-100" aria-hidden="true" />
                    <span className="hidden sm:inline">{t("nav.go_to_ministry")}</span>
                    <span className="sr-only sm:hidden">{t("nav.go_to_ministry")}</span>
                    <span className="sr-only">({t("nav.opens_new_tab")})</span>
                  </a>
                  <span className="h-4 w-px bg-aeblack-200" aria-hidden="true" />
                </>
              )}
              <A11yTools />
              <span className="h-4 w-px bg-aeblack-200" aria-hidden="true" />
              <LanguageSwitcher />
            </div>
          </div>
        </div>
        <div className="container mx-auto flex items-center justify-between gap-4 px-4 py-3" dir="ltr">
          <Brand />
          <nav aria-label={t("nav.main")} className="hidden md:block" dir={config.dir}><Nav /></nav>
          <button type="button" className="rounded-lg p-2 text-aeblack-800 hover:bg-aeblack-100 md:hidden" aria-expanded={open} aria-controls="mobile-nav" onClick={() => setOpen((o) => !o)}>
            {open ? <X size={28} aria-hidden="true" /> : <List size={28} aria-hidden="true" />}
            <span className="sr-only">{t("nav.menu")}</span>
          </button>
        </div>
        {open && (
          <nav id="mobile-nav" aria-label={t("nav.main")} className="border-t border-aeblack-100 px-4 py-3 md:hidden">
            <Nav onNavigate={() => setOpen(false)} />
          </nav>
        )}
      </header>

      <main id="main" tabIndex={-1} className="flex-1 focus:outline-none">{children}</main>

      <footer className="mc-surface bg-aeblack-900 text-aeblack-200">
        <div className="container mx-auto grid gap-8 px-4 py-10 md:grid-cols-2" dir="ltr">
          <div className="flex flex-col items-start">
            <img src={logoSrc(lang, config.logo_dark || config.logo, Boolean(config.logo_dark))} alt={t("site.ministry")} className="mb-4 h-16 w-auto max-w-full md:h-20" width={907} height={228} />
            <p className="max-w-md text-sm leading-6" dir={config.dir}>{t("site.tagline")}</p>
          </div>
          <nav aria-label={t("footer.portal_links")} dir={config.dir} className="md:justify-self-end">
            <p className="mb-3 font-semibold text-whitely-50">{t("footer.portal_links")}</p>
            <ul className="space-y-2 text-sm">
              <li><Link className="inline-flex items-center gap-2 hover:text-primary-300" to={path()}><Briefcase size={20} aria-hidden="true" />{t("nav.jobs")}</Link></li>
              <li><Link className="inline-flex items-center gap-2 hover:text-primary-300" to={path("/applications")}><Files size={20} aria-hidden="true" />{t("nav.my_applications")}</Link></li>
              {config.main_site_url && (
                <li>
                  <a className="inline-flex items-center gap-2 hover:text-primary-300" href={config.main_site_url} target="_blank" rel="noopener">
                    <ArrowSquareOut size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("nav.ministry_website")}<span className="sr-only">({t("nav.opens_new_tab")})</span>
                  </a>
                </li>
              )}
            </ul>
          </nav>
        </div>
      </footer>
    </div>
  );
}
