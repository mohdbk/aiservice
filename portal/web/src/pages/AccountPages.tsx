// Email activation and password reset pages (all show the account-purpose note).
import { ArrowLeft, CheckCircle, Envelope, EnvelopeOpen, Key, LockSimple, PaperPlaneTilt } from "@phosphor-icons/react";
import { FormEvent, ReactNode, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, ApiError, User } from "../api";
import { useAuth } from "../auth";
import { Field } from "../components/Bits";
import { Alert, Spinner } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { PasswordInput, safeNext } from "./SignIn";

function AccountShell({ icon: I, title, intro, children }: { icon: typeof Envelope; title: string; intro?: ReactNode; children: ReactNode }) {
  const { t } = useSite();
  usePageTitle(title);
  return (
    <div className="container mx-auto max-w-md px-4 py-10">
      <span className="mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-primary-50 text-primary-600" aria-hidden="true"><I size={40} weight="duotone" /></span>
      <h1 className="font-heading text-3xl font-bold text-aeblack-950">{title}</h1>
      {intro && <div className="mt-2 text-aeblack-700">{intro}</div>}
      <div className="my-6"><Alert tone="info">{t("auth.account_note")}</Alert></div>
      {children}
    </div>
  );
}

function BackToSignIn() {
  const { t, path } = useSite();
  return (
    <p className="mt-6 text-center">
      <Link className="inline-flex items-center gap-1 font-semibold text-primary-700 underline" to={path("/sign-in")}>
        <ArrowLeft size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("auth.back_to_sign_in")}
      </Link>
    </p>
  );
}

function ResendButton({ email, next }: { email: string; next?: string }) {
  const { t, lang } = useSite();
  const [state, setState] = useState<"idle" | "busy" | "done">("idle");
  const send = async () => {
    setState("busy");
    try { await api("/api/resend-verification", { body: { email, lang, next } }); } catch { /* same answer either way */ }
    setState("done");
  };
  if (state === "done") return <Alert tone="success" title={t("auth.resend_done")} />;
  return (
    <button type="button" className="aegov-btn btn-outline btn-block" onClick={send} disabled={state === "busy" || !email}>
      <PaperPlaneTilt size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("auth.resend_button")}
    </button>
  );
}

export function CheckEmail() {
  const { t, path } = useSite();
  const [params] = useSearchParams();
  const email = params.get("email") || "";
  const next = safeNext(params.get("next"), path());
  return (
    <AccountShell icon={EnvelopeOpen} title={t("auth.verify_title")} intro={<p>{t("auth.verify_sent", { email })}</p>}>
      <div className="mc-surface space-y-4 rounded-2xl border border-aeblack-100 bg-white p-6">
        <p className="text-aeblack-700">{t("auth.verify_spam")}</p>
        <ResendButton email={email} next={next} />
      </div>
      <BackToSignIn />
    </AccountShell>
  );
}

export function VerifyEmail() {
  const { t, path, errorText } = useSite();
  const { adopt } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const token = params.get("token") || "";
  const [state, setState] = useState<{ done?: boolean; next?: string; error?: unknown }>({});
  const [email, setEmail] = useState("");
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    if (!token) return setState({ error: new ApiError("token_invalid", 400) });
    api<{ user: User; next: string }>("/api/verify-email", { body: { token } })
      .then((r) => { adopt(r.user); setState({ done: true, next: safeNext(r.next, path()) }); })
      .catch((e) => setState({ error: e }));
  }, [token, adopt, path]);
  if (state.done) {
    return (
      <AccountShell icon={CheckCircle} title={t("auth.verified_title")} intro={<p>{t("auth.verified_body")}</p>}>
        <button type="button" className="aegov-btn btn-block" onClick={() => navigate(state.next || path(), { replace: true })}>{t("auth.continue")}</button>
      </AccountShell>
    );
  }
  if (state.error) {
    return (
      <AccountShell icon={Envelope} title={t("auth.link_problem_title")}>
        <div className="mc-surface space-y-4 rounded-2xl border border-aeblack-100 bg-white p-6">
          <Alert tone="warning" title={errorText(state.error)} />
          <Field id="email" label={t("auth.email")} prefix={Envelope}>
            <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" dir="ltr" className="rtl:text-right" maxLength={200} />
          </Field>
          <ResendButton email={email.trim()} />
        </div>
        <BackToSignIn />
      </AccountShell>
    );
  }
  return (
    <AccountShell icon={EnvelopeOpen} title={t("auth.verify_title")}>
      <Spinner label={t("auth.verifying")} />
    </AccountShell>
  );
}

export function ForgotPassword() {
  const { t, lang } = useSite();
  const [params] = useSearchParams();
  const [email, setEmail] = useState(params.get("email") || "");
  const [state, setState] = useState<"idle" | "busy" | "sent">("idle");
  const [error, setError] = useState<unknown>(null);
  const { errorText } = useSite();
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setState("busy");
    try {
      await api("/api/forgot-password", { body: { email: email.trim(), lang } });
      setState("sent");
    } catch (err) {
      setError(err);
      setState("idle");
    }
  };
  return (
    <AccountShell icon={Key} title={t("auth.forgot_title")} intro={<p>{t("auth.forgot_intro")}</p>}>
      {state === "sent" ? (
        <Alert tone="success" title={t("auth.forgot_sent", { email: email.trim() })} />
      ) : (
        <form onSubmit={submit} className="mc-surface space-y-5 rounded-2xl border border-aeblack-100 bg-white p-6" noValidate>
          {error ? <Alert tone="error" title={errorText(error)} /> : null}
          <Field id="email" label={t("auth.email")} prefix={Envelope}>
            <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required maxLength={200} inputMode="email" dir="ltr" className="rtl:text-right" />
          </Field>
          <button type="submit" className="aegov-btn btn-block" disabled={state === "busy" || !email.includes("@")}>
            <PaperPlaneTilt size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("auth.forgot_button")}
          </button>
        </form>
      )}
      <BackToSignIn />
    </AccountShell>
  );
}

export function ResetPassword() {
  const { t, path, errorText } = useSite();
  const { clear } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const token = params.get("token") || "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (password.length < 8) return setError(new ApiError("password_short", 400));
    if (password !== confirm) return setError(new ApiError("password_mismatch", 400));
    setBusy(true);
    try {
      await api("/api/reset-password", { body: { token, password } });
      clear();
      navigate(`${path("/sign-in")}?reset=1`, { replace: true });
    } catch (err) {
      setError(err);
      setBusy(false);
    }
  };
  const tokenProblem = error instanceof ApiError && error.code.startsWith("token_");
  return (
    <AccountShell icon={LockSimple} title={t("auth.reset_title")}>
      <form onSubmit={submit} className="mc-surface space-y-5 rounded-2xl border border-aeblack-100 bg-white p-6" noValidate>
        {error ? (
          <Alert tone="error" title={errorText(error)}>
            {tokenProblem && <Link className="mt-2 inline-block font-semibold underline" to={path("/forgot-password")}>{t("auth.forgot_button")}</Link>}
          </Alert>
        ) : null}
        <Field id="password" label={t("auth.new_password")} hint={t("auth.password_hint")} prefix={LockSimple}>
          <PasswordInput id="password" value={password} onChange={setPassword} autoComplete="new-password" describedBy="password-hint" />
        </Field>
        <Field id="confirm" label={t("auth.confirm_password")} prefix={LockSimple}>
          <PasswordInput id="confirm" value={confirm} onChange={setConfirm} autoComplete="new-password" />
        </Field>
        <button type="submit" className="aegov-btn btn-block" disabled={busy || !password || !confirm || !token}>
          <Key size={20} aria-hidden="true" />{t("auth.reset_button")}
        </button>
      </form>
      <BackToSignIn />
    </AccountShell>
  );
}
