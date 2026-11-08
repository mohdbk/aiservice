// Language, direction and wording for the current /<lang> route. Wording comes from Frappe (HR can edit it).
import { createContext, ReactNode, useContext, useEffect, useMemo } from "react";
import { ApiError, SiteConfig } from "./api";

type Site = {
  config: SiteConfig;
  lang: string;
  dir: "rtl" | "ltr";
  t: (key: string, params?: Record<string, unknown>) => string;
  path: (p?: string) => string;
  errorText: (e: unknown) => string;
  formatDate: (iso: string) => string;
  formatNumber: (n: number) => string;
};

const SiteContext = createContext<Site | null>(null);

export function SiteProvider({ config, children }: { config: SiteConfig; children: ReactNode }) {
  const value = useMemo<Site>(() => {
    const lang = config.lang;
    const locale = `${lang}-u-nu-latn`;
    const t = (key: string, params?: Record<string, unknown>) => {
      let s = config.text[key] ?? key;
      if (params) for (const [k, v] of Object.entries(params)) s = s.split(`{${k}}`).join(String(v));
      return s;
    };
    const dateFmt = (() => {
      try {
        return new Intl.DateTimeFormat(locale, { day: "numeric", month: "long", year: "numeric" });
      } catch {
        return new Intl.DateTimeFormat("en", { day: "numeric", month: "long", year: "numeric" });
      }
    })();
    return {
      config,
      lang,
      dir: config.dir,
      t,
      path: (p = "") => `/${lang}${p}`,
      errorText: (e: unknown) => {
        const code = e instanceof ApiError ? e.code : "server";
        const key = `error.${code}`;
        return config.text[key] ? t(key, (e as ApiError).params) : t("error.server");
      },
      formatDate: (iso: string) => {
        if (!iso) return "";
        const d = new Date(`${iso.slice(0, 10)}T00:00:00`);
        return isNaN(d.getTime()) ? iso : dateFmt.format(d);
      },
      formatNumber: (n: number) => new Intl.NumberFormat("en").format(n),
    };
  }, [config]);

  useEffect(() => {
    document.documentElement.lang = config.lang;
    document.documentElement.dir = config.dir;
  }, [config.lang, config.dir]);

  return <SiteContext.Provider value={value}>{children}</SiteContext.Provider>;
}

export function useSite(): Site {
  const s = useContext(SiteContext);
  if (!s) throw new Error("useSite outside SiteProvider");
  return s;
}

export function usePageTitle(title: string) {
  const { t } = useSite();
  useEffect(() => {
    document.title = title === t("site.portal") ? `${t("site.portal")} | ${t("site.ministry")}` : `${title} | ${t("site.portal")} | ${t("site.ministry")}`;
  }, [title, t]);
}
