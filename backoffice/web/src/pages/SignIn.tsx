import { Eye, EyeSlash, LockKey, SignIn as SignInIcon } from "@phosphor-icons/react";
import { FormEvent, useEffect, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { LangFlag } from "../components/flags";
import { Button, ErrorState, TextInput, useSaving } from "../components/ui";
import { api, Me, useAuth, useLang, usePageTitle } from "../core";

export default function SignIn() {
  const { t, lang, dir } = useLang();
  usePageTitle(t("auth.sign_in"));
  const { me, setMe } = useAuth();
  const nav = useNavigate();
  const loc = useLocation();
  const next = new URLSearchParams(loc.search).get("next") || "";
  const target = next.startsWith(`/${lang}`) && !next.startsWith("//") ? next : `/${lang}`;
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [show, setShow] = useState(false);
  const { busy, error, run } = useSaving();
  useEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = dir;
  }, [lang, dir]);
  useEffect(() => {
    if (me) nav(target, { replace: true });
  }, [me, nav, target]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const m = await run(() => api<Me>("/sign-in", { body: { email, password } }));
    if (m) setMe(m);
  };
  const other = lang === "ar" ? "en" : "ar";
  return (
    <div className="mc-hero flex min-h-screen flex-col">
      <header dir="ltr" className="flex items-center justify-between gap-3 px-4 py-4 sm:px-8">
        <img src={lang === "ar" ? "/logo-ar.png" : "/logo-en.png"} alt={t("app.ministry")} width={906} height={227} className="mh-logo h-10 sm:h-12" />
        <Link to={`/${other}/sign-in${loc.search}`} lang={other} className="inline-flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-semibold hover:bg-white/70">
          <LangFlag lang={other} />
          {t("lang.switch")}
        </Link>
      </header>
      <main className="flex flex-1 items-center justify-center px-4 pb-16">
        <form onSubmit={submit} className="mc-surface w-full max-w-md space-y-5 rounded-2xl border border-aeblack-100 bg-white p-6 shadow-sm sm:p-8">
          <div>
            <span className="mb-3 inline-flex rounded-full bg-primary-100 px-3 py-1 text-sm font-bold text-primary-800">{t("app.badge")}</span>
            <h1 className="flex items-center gap-2 text-2xl font-bold text-aeblack-900"><LockKey size={24} className="text-primary-600" aria-hidden="true" />{t("auth.title")}</h1>
            <p className="mt-2 text-sm text-aeblack-600">{t("auth.intro")}</p>
          </div>
          {error && <ErrorState error={error} />}
          <TextInput label={t("auth.email")} type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <div className="relative">
            <TextInput label={t("auth.password")} type={show ? "text" : "password"} dir="ltr" autoComplete="current-password" required style={{ paddingRight: "3.25rem" }} value={password} onChange={(e) => setPassword(e.target.value)} />
            <button type="button" onClick={() => setShow((s) => !s)} className="absolute right-3 top-9 rounded p-1 text-aeblack-500 hover:text-primary-700"
              aria-label={show ? t("auth.hide_password") : t("auth.show_password")} aria-pressed={show}>
              {show ? <EyeSlash size={24} /> : <Eye size={24} />}
            </button>
          </div>
          <Button type="submit" className="btn-block w-full" busy={busy} icon={<SignInIcon size={20} aria-hidden="true" />}>{t("auth.sign_in")}</Button>
        </form>
      </main>
    </div>
  );
}
