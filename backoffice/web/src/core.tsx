import { createContext, ReactNode, useCallback, useContext, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import ar from "./text/ar.json";
import en from "./text/en.json";

// ---------------- API ----------------
export class ApiError extends Error {
  constructor(public code: string, public status: number, public params: Record<string, unknown> = {}) {
    super(code);
  }
}

type Opts = { method?: string; body?: unknown };

/** Calls the backoffice API. Changes carry the X-Admin header the server requires. */
export async function api<T = any>(path: string, opts: Opts = {}): Promise<T> {
  const method = opts.method || (opts.body !== undefined ? "POST" : "GET");
  let res: Response;
  try {
    res = await fetch(`/api/backoffice${path}`, {
      method,
      credentials: "same-origin",
      headers: { Accept: "application/json", ...(method !== "GET" ? { "X-Backoffice": "1" } : {}), ...(opts.body !== undefined ? { "Content-Type": "application/json" } : {}) },
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
    });
  } catch {
    throw new ApiError("network", 0);
  }
  if (res.status === 401) {
    window.dispatchEvent(new Event("mh:signed-out"));
    throw new ApiError("signed_out", 401);
  }
  let data: any = null;
  try {
    data = await res.json();
  } catch {
    /* empty body */
  }
  if (!res.ok) throw new ApiError(data?.error?.code || (res.status === 403 ? "forbidden" : res.status === 404 ? "not_found" : "server"), res.status, data?.error?.params || {});
  return data as T;
}

export function useAsync<T>(fn: () => Promise<T>, deps: unknown[]) {
  const [state, setState] = useState<{ data?: T; error?: ApiError; loading: boolean }>({ loading: true });
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    setState((s) => ({ ...s, loading: true, error: undefined }));
    fn().then(
      (data) => live && setState({ data, loading: false }),
      (error) => live && setState({ error: error instanceof ApiError ? error : new ApiError("server", 500), loading: false }),
    );
    return () => {
      live = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);
  const reload = useCallback(() => setTick((n) => n + 1), []);
  return { ...state, reload, setData: (d: T) => setState({ data: d, loading: false }) };
}

// ---------------- language ----------------
export type Lang = "ar" | "en";
const TEXT: Record<Lang, Record<string, string>> = { ar, en };

export function translate(lang: Lang, key: string, params?: Record<string, string | number>) {
  let s = TEXT[lang][key] ?? TEXT.en[key] ?? key;
  for (const [k, v] of Object.entries(params || {})) s = s.replace(`{${k}}`, String(v));
  return s;
}

export function useLang() {
  const { lang } = useParams();
  const l: Lang = lang === "en" ? "en" : "ar";
  const t = useCallback((key: string, params?: Record<string, string | number>) => translate(l, key, params), [l]);
  const errorText = useCallback((e?: ApiError | null) => (e ? translate(l, `error.${e.code}`) === `error.${e.code}` ? translate(l, "error.server") : translate(l, `error.${e.code}`) : ""), [l]);
  const date = useCallback((iso?: string | null, withTime = false) => {
    if (!iso) return "";
    const d = new Date(iso.length === 10 ? iso + "T00:00:00" : iso.endsWith("Z") ? iso : iso + "Z");
    if (isNaN(d.getTime())) return iso;
    return new Intl.DateTimeFormat(l === "ar" ? "ar-AE-u-nu-latn" : "en-GB", {
      day: "numeric", month: "short", year: "numeric", ...(withTime ? { hour: "2-digit", minute: "2-digit", timeZone: "Asia/Dubai" } : {}),
    }).format(d);
  }, [l]);
  return { lang: l, t, errorText, date, dir: l === "ar" ? "rtl" : "ltr", to: (p: string) => `/${l}${p}` };
}

export function usePageTitle(title: string) {
  const { t } = useLang();
  useEffect(() => {
    document.title = `${title} | ${t("app.badge")}`;
  }, [title, t]);
}

// ---------------- signed-in staff ----------------
export type Me = { id: number; full_name: string; email: string; role: string; can_administer: boolean; is_administrator: boolean };
type AuthState = { me: Me | null | undefined; setMe: (m: Me | null) => void };
const AuthContext = createContext<AuthState>({ me: undefined, setMe: () => {} });

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null | undefined>(undefined);
  useEffect(() => {
    api<Me>("/me").then(setMe, () => setMe(null));
    const out = () => setMe(null);
    window.addEventListener("mh:signed-out", out);
    return () => window.removeEventListener("mh:signed-out", out);
  }, []);
  return <AuthContext.Provider value={{ me, setMe }}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);

export function useGo() {
  const nav = useNavigate();
  const { to } = useLang();
  return (p: string) => nav(to(p));
}

// ---------------- toasts ----------------
type Toast = { id: number; text: string; tone: "success" | "error" | "info" };
const ToastContext = createContext<(text: string, tone?: Toast["tone"]) => void>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<Toast[]>([]);
  const push = useCallback((text: string, tone: Toast["tone"] = "success") => {
    const id = Date.now() + Math.random();
    setItems((x) => [...x, { id, text, tone }]);
    setTimeout(() => setItems((x) => x.filter((i) => i.id !== id)), 5000);
  }, []);
  return (
    <ToastContext.Provider value={push}>
      {children}
      <div aria-live="polite" role="status" className="pointer-events-none fixed inset-x-4 bottom-4 z-[60] flex flex-col items-center gap-2 sm:inset-x-auto sm:end-4 sm:items-end">
        {items.map((i) => (
          <div key={i.id} className={`pointer-events-auto rounded-lg px-4 py-3 text-sm font-semibold text-white shadow-lg ${i.tone === "error" ? "bg-aered-600" : i.tone === "info" ? "bg-aeblack-700" : "bg-aegreen-600"}`}>
            {i.text}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export const useToast = () => useContext(ToastContext);
