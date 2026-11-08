import { ReactNode, useEffect, useState } from "react";
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

function A11yTools() {
  const { t } = useSite();
  const [size, setSize] = useState(() => store.get("mc-text") || "md");
  const [contrast, setContrast] = useState(() => store.get("mc-contrast") === "1");
  useEffect(() => {
    applyA11y(size, contrast);
    store.set("mc-text", size);
    store.set("mc-contrast", contrast ? "1" : "0");
  }, [size, contrast]);
  const step = (d: number) => setSize((s) => SIZES[Math.max(0, Math.min(3, SIZES.indexOf(s as (typeof SIZES)[number]) + d))]);
  const btn = "rounded px-1.5 py-0.5 text-aeblack-700 hover:bg-aeblack-100 hover:text-primary-700";
  return (
    <div className="flex items-center gap-1" role="group" aria-label={t("a11y.tools")}>
      <button type="button" className={btn} onClick={() => step(-1)} aria-label={t("a11y.smaller")}>A-</button>
      <button type="button" className={btn} onClick={() => setSize("md")} aria-label={t("a11y.default")}>A</button>
      <button type="button" className={btn} onClick={() => step(1)} aria-label={t("a11y.larger")}>A+</button>
      <button type="button" className={btn} onClick={() => setContrast((c) => !c)} aria-pressed={contrast} title={t("a11y.contrast")}>
        <svg className="h-4 w-4" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="2" /><path d="M12 3a9 9 0 0 1 0 18Z" fill="currentColor" /></svg>
        <span className="sr-only">{t("a11y.contrast")}</span>
      </button>
    </div>
  );
}

function LanguageSwitcher() {
  const { config, lang, t } = useSite();
  const location = useLocation();
  const others = config.languages.filter((l) => l.code !== lang);
  const to = (code: string) => `/${code}${location.pathname.slice(lang.length + 1)}${location.search}`;
  if (!others.length) return null;
  if (others.length === 1) {
    const o = others[0];
    return (
      <Link to={to(o.code)} lang={o.code} hrefLang={o.code} className="rounded px-2 py-0.5 font-semibold text-aeblack-800 hover:bg-aeblack-100 hover:text-primary-700">
        {o.label}
      </Link>
    );
  }
  return (
    <details className="relative">
      <summary className="cursor-pointer list-none rounded px-2 py-0.5 font-semibold text-aeblack-800 hover:bg-aeblack-100" aria-label={t("a11y.language")}>
        {config.languages.find((l) => l.code === lang)?.label} ▾
      </summary>
      <ul className="absolute end-0 z-30 mt-1 min-w-36 rounded-lg border border-aeblack-100 bg-white py-1 shadow-lg">
        {others.map((o) => (
          <li key={o.code}>
            <Link to={to(o.code)} lang={o.code} hrefLang={o.code} className="block px-3 py-1.5 hover:bg-primary-50">{o.label}</Link>
          </li>
        ))}
      </ul>
    </details>
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
    <Link to={path()} className="flex min-w-0 items-center gap-3" aria-label={`${t("site.ministry")}, ${t("site.portal")}`}>
      <img src={logoSrc(lang, config.logo)} alt={t("site.ministry")} className="h-10 w-auto max-w-[60vw] sm:h-12 md:h-14" width={907} height={228} />
      <span className="hidden h-9 w-px bg-aeblack-200 sm:block" aria-hidden="true" />
      <span className="hidden font-heading text-base font-semibold text-primary-700 sm:block md:text-lg" dir={config.dir}>{t("site.portal")}</span>
    </Link>
  );
}

function Nav({ onNavigate }: { onNavigate?: () => void }) {
  const { t, path } = useSite();
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const link = ({ isActive }: { isActive: boolean }) =>
    `block rounded-lg px-3 py-2 font-medium hover:bg-primary-50 ${isActive ? "text-primary-700 bg-primary-50" : "text-aeblack-800"}`;
  return (
    <ul className="flex flex-col gap-1 md:flex-row md:items-center md:gap-2">
      <li><NavLink end to={path()} className={link} onClick={onNavigate}>{t("nav.jobs")}</NavLink></li>
      {user ? (
        <>
          <li><NavLink to={path("/applications")} className={link} onClick={onNavigate}>{t("nav.my_applications")}</NavLink></li>
          <li className="px-3 py-2 text-sm text-aeblack-600 md:max-w-48 md:truncate" title={user.email}>{user.full_name || user.email}</li>
          <li>
            <button type="button" className="aegov-btn btn-outline btn-sm" onClick={async () => { await signOut(); onNavigate?.(); navigate(path()); }}>
              {t("nav.sign_out")}
            </button>
          </li>
        </>
      ) : (
        <>
          <li><Link className="aegov-btn btn-outline btn-sm" to={path("/sign-in")} onClick={onNavigate}>{t("nav.sign_in")}</Link></li>
          <li><Link className="aegov-btn btn-sm" to={path("/create-account")} onClick={onNavigate}>{t("nav.create_account")}</Link></li>
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
            {config.main_site_url ? (
              <a className="truncate text-aeblack-700 hover:text-primary-700" href={config.main_site_url}>{t("site.ministry")}</a>
            ) : <span className="truncate text-aeblack-700">{t("site.ministry")}</span>}
            <div className="flex shrink-0 items-center gap-2">
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
            <svg className="h-6 w-6" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              {open ? <path d="M6 6l12 12M18 6L6 18" /> : <path d="M4 7h16M4 12h16M4 17h16" />}
            </svg>
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
            <img src={logoSrc(lang, config.logo_dark || config.logo, Boolean(config.logo_dark))} alt={t("site.ministry")} className="mb-4 h-14 w-auto max-w-full rounded-lg bg-white p-2" width={907} height={228} />
            <p className="max-w-md text-sm leading-6" dir={config.dir}>{t("site.tagline")}</p>
          </div>
          <nav aria-label={t("footer.portal_links")} dir={config.dir} className="md:justify-self-end">
            <p className="mb-3 font-semibold text-white">{t("footer.portal_links")}</p>
            <ul className="space-y-2 text-sm">
              <li><Link className="hover:text-primary-300" to={path()}>{t("nav.jobs")}</Link></li>
              <li><Link className="hover:text-primary-300" to={path("/applications")}>{t("nav.my_applications")}</Link></li>
              {config.main_site_url && <li><a className="hover:text-primary-300" href={config.main_site_url}>{t("nav.ministry_website")}</a></li>}
            </ul>
          </nav>
        </div>
      </footer>
    </div>
  );
}
