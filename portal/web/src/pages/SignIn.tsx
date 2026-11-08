import { FormEvent, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth";
import { Field, Steps } from "../components/Bits";
import { Alert } from "../components/States";
import { usePageTitle, useSite } from "../site";

// Only same-site paths are allowed after sign-in (no open redirects).
export function safeNext(next: string | null, fallback: string) {
  return next && next.startsWith("/") && !next.startsWith("//") && !next.includes("\\") ? next : fallback;
}

export function PasswordInput({ id, value, onChange, autoComplete, describedBy }: { id: string; value: string; onChange: (v: string) => void; autoComplete: string; describedBy?: string }) {
  const { t } = useSite();
  const [show, setShow] = useState(false);
  return (
    <>
      <input id={id} type={show ? "text" : "password"} value={value} onChange={(e) => onChange(e.target.value)} autoComplete={autoComplete} required minLength={8} maxLength={128} aria-describedby={describedBy} />
      <button type="button" className="control-suffix rounded-lg px-3 text-aeblack-600 hover:text-primary-700 focus-visible:outline-2" onClick={() => setShow((s) => !s)}
        aria-pressed={show} aria-label={show ? t("auth.hide_password") : t("auth.show_password")} title={show ? t("auth.hide_password") : t("auth.show_password")}>
        {show ? (
          <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M3 3l18 18M10.6 10.6a2 2 0 0 0 2.8 2.8M9.9 5.1A10.4 10.4 0 0 1 12 5c6 0 10 7 10 7a17.6 17.6 0 0 1-3.2 3.9M6.6 6.6A17.4 17.4 0 0 0 2 12s4 7 10 7a9.8 9.8 0 0 0 5.4-1.6" />
          </svg>
        ) : (
          <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Z" /><circle cx="12" cy="12" r="3" />
          </svg>
        )}
      </button>
    </>
  );
}

export default function SignIn() {
  const { t, path, errorText } = useSite();
  usePageTitle(t("auth.sign_in_title"));
  const { user, signIn } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const next = safeNext(params.get("next"), path());
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const errRef = useRef<HTMLDivElement>(null);
  useEffect(() => { if (user) navigate(next, { replace: true }); }, [user, next, navigate]);
  useEffect(() => { if (error) errRef.current?.querySelector<HTMLElement>("[role=alert]")?.focus(); }, [error]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await signIn(email.trim(), password);
    } catch (err) {
      setError(err);
      setBusy(false);
    }
  };

  return (
    <div className="container mx-auto max-w-md px-4 py-10">
      {next.includes("/apply") && <Steps current={1} />}
      <h1 className="font-heading text-3xl font-bold text-aeblack-950">{t("auth.sign_in_title")}</h1>
      <p className="mb-6 mt-2 text-aeblack-600">{t("auth.sign_in_intro")}</p>
      <div className="mb-6"><Alert tone="info">{t("auth.account_note")}</Alert></div>
      <form onSubmit={submit} className="mc-surface space-y-5 rounded-2xl border border-aeblack-100 bg-white p-6" noValidate>
        <div ref={errRef}>{error ? <Alert tone="error" title={errorText(error)} /> : null}</div>
        <Field id="email" label={t("auth.email")}>
          <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required maxLength={200} inputMode="email" dir="ltr" />
        </Field>
        <Field id="password" label={t("auth.password")}>
          <PasswordInput id="password" value={password} onChange={setPassword} autoComplete="current-password" />
        </Field>
        <button type="submit" className="aegov-btn btn-block" disabled={busy || !email || !password}>{busy ? t("auth.working") : t("auth.sign_in_button")}</button>
      </form>
      <p className="mt-6 text-center text-aeblack-700">
        {t("auth.no_account")} <Link className="font-semibold text-primary-700 underline" to={`${path("/create-account")}?next=${encodeURIComponent(next)}`}>{t("auth.create_link")}</Link>
      </p>
    </div>
  );
}
