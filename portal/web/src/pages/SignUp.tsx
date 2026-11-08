import { FormEvent, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { ApiError } from "../api";
import { useAuth } from "../auth";
import { Field, Steps } from "../components/Bits";
import { Alert } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { PasswordInput, safeNext } from "./SignIn";

export default function SignUp() {
  const { t, lang, path, config, errorText } = useSite();
  usePageTitle(t("auth.signup_title"));
  const { user, signUp } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const next = safeNext(params.get("next"), path());
  const [form, setForm] = useState({ full_name: "", email: "", password: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const errRef = useRef<HTMLDivElement>(null);
  useEffect(() => { if (user) navigate(next, { replace: true }); }, [user, next, navigate]);
  useEffect(() => { if (error) errRef.current?.querySelector<HTMLElement>("[role=alert]")?.focus(); }, [error]);
  const set = (k: keyof typeof form) => (v: string) => setForm((f) => ({ ...f, [k]: v }));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (form.password.length < 8) return setError(new ApiError("password_short", 400));
    setBusy(true);
    setError(null);
    try {
      await signUp(form.full_name.trim(), form.email.trim(), form.password, lang);
    } catch (err) {
      setError(err);
      setBusy(false);
    }
  };

  return (
    <div className="container mx-auto max-w-md px-4 py-10">
      <Steps current={1} />
      <h1 className="font-heading text-3xl font-bold text-aeblack-950">{t("auth.signup_title")}</h1>
      <p className="mb-6 mt-2 text-aeblack-600">{t("auth.signup_intro")}</p>
      {!config.allow_signup ? (
        <Alert tone="warning" title={t("error.signup_closed")} />
      ) : (
        <form onSubmit={submit} className="mc-surface space-y-5 rounded-2xl border border-aeblack-100 bg-white p-6" noValidate>
          <div ref={errRef}>{error ? <Alert tone="error" title={errorText(error)} /> : null}</div>
          <Field id="full_name" label={t("auth.full_name")}>
            <input id="full_name" value={form.full_name} onChange={(e) => set("full_name")(e.target.value)} autoComplete="name" required maxLength={140} />
          </Field>
          <Field id="email" label={t("auth.email")}>
            <input id="email" type="email" value={form.email} onChange={(e) => set("email")(e.target.value)} autoComplete="email" required maxLength={200} inputMode="email" dir="ltr" />
          </Field>
          <Field id="password" label={t("auth.password")} hint={t("auth.password_hint")}>
            <PasswordInput id="password" value={form.password} onChange={set("password")} autoComplete="new-password" describedBy="password-hint" />
          </Field>
          <button type="submit" className="aegov-btn btn-block" disabled={busy || !form.full_name || !form.email || !form.password}>{busy ? t("auth.working") : t("auth.signup_button")}</button>
        </form>
      )}
      <p className="mt-6 text-center text-aeblack-700">
        {t("auth.have_account")} <Link className="font-semibold text-primary-700 underline" to={`${path("/sign-in")}?next=${encodeURIComponent(next)}`}>{t("auth.sign_in_link")}</Link>
      </p>
    </div>
  );
}
