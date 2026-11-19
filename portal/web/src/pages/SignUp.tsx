import { CheckCircle, Envelope, FileText, LockSimple, SignIn as SignInIcon, UploadSimple, UserCircle, UserPlus } from "@phosphor-icons/react";
import { FormEvent, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, ApiError, fileToBase64 } from "../api";
import { useAuth } from "../auth";
import { Field, Steps } from "../components/Bits";
import { Alert, Spinner } from "../components/States";
import { CV_RE } from "../components/CvLibrary";
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

  // Starting with a CV: it is read, and the name and email found in it fill in the form (only what is still empty). It is
  // saved to the account on sign-up.
  type Draft = { status: string; full_name: string; email: string; phone: string };
  const [cvName, setCvName] = useState("");
  const [draftToken, setDraftToken] = useState("");
  const [draftState, setDraftState] = useState<"" | "uploading" | "reading" | "filled" | "none">("");
  const cvInput = useRef<HTMLInputElement>(null);
  const pickCv = async (f: File | null) => {
    if (!f) return;
    setError(null);
    if (!CV_RE.test(f.name)) return setError(new ApiError("cv_type", 400));
    if (f.size > config.max_cv_mb * 1024 * 1024) return setError(new ApiError("cv_too_large", 400, { max_mb: config.max_cv_mb }));
    setCvName(f.name);
    setDraftState("uploading");
    try {
      const { token } = await api<{ token: string }>("/api/cv-draft", { body: { cv: { name: f.name, type: f.type, data: await fileToBase64(f) } } });
      setDraftToken(token);
      setDraftState("reading");
      for (let i = 0; i < 20; i++) {
        const d = await api<Draft>(`/api/cv-draft/${token}`).catch(() => null);
        if (d && d.status !== "pending") {
          const found = d.status === "ready" && (d.full_name || d.email);
          setForm((cur) => ({ ...cur, full_name: cur.full_name || d.full_name || "", email: cur.email || d.email || "" }));
          setDraftState(found ? "filled" : "none");
          return;
        }
        await new Promise((res) => setTimeout(res, 2000));
      }
      setDraftState("none");
    } catch (err) {
      setError(err);
      setDraftState("");
      setCvName("");
    }
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (form.password.length < 8) return setError(new ApiError("password_short", 400));
    setBusy(true);
    setError(null);
    try {
      const r = await signUp(form.full_name.trim(), form.email.trim(), form.password, lang, next, draftToken);
      if (r.verification_sent) navigate(`${path("/check-email")}?${new URLSearchParams({ email: form.email.trim(), next })}`, { replace: true });
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
      <div className="mb-6"><Alert tone="info">{t("auth.account_note")}</Alert></div>
      {!config.allow_signup ? (
        <Alert tone="warning" title={t("error.signup_closed")} />
      ) : (
        <form onSubmit={submit} className="mc-surface space-y-5 rounded-2xl border border-aeblack-100 bg-white p-6" noValidate>
          <div ref={errRef}>{error ? <Alert tone="error" title={errorText(error)} /> : null}</div>
          <div className="rounded-xl border border-dashed border-primary-300 bg-primary-50 p-4">
            <p className="flex items-center gap-2 font-medium text-aeblack-900"><FileText size={24} className="text-primary-600" aria-hidden="true" />{t("signup.cv_title")}
              <span className="font-normal text-aeblack-500">({t("common.optional")})</span></p>
            <p className="mt-1 text-sm text-aeblack-700">{t("signup.cv_hint")}</p>
            <input ref={cvInput} id="signup-cv" type="file" className="sr-only" accept=".pdf,.docx,.jpg,.jpeg,.png" aria-label={t("signup.cv_upload")}
              onChange={(e) => { const f = e.target.files?.[0] || null; e.target.value = ""; pickCv(f); }} />
            {cvName && <p className="mt-3 text-sm text-aeblack-800"><bdi>{cvName}</bdi></p>}
            {(draftState === "uploading" || draftState === "reading") && <div className="mt-2 text-sm text-aeblack-700"><Spinner label={t("signup.cv_reading")} /></div>}
            {draftState === "filled" && <p className="mt-2 flex items-center gap-1 text-sm text-aegreen-700" role="status"><CheckCircle size={20} aria-hidden="true" />{t("signup.cv_filled")}</p>}
            {draftState === "none" && <p className="mt-2 text-sm text-aeblack-700" role="status">{t("signup.cv_none")}</p>}
            {draftToken && <p className="mt-1 text-sm text-aeblack-600">{t("signup.cv_saved")}</p>}
            <button type="button" className="aegov-btn btn-outline btn-sm mt-3" disabled={draftState === "uploading" || draftState === "reading"} onClick={() => cvInput.current?.click()}>
              <UploadSimple size={20} aria-hidden="true" />{cvName ? t("signup.cv_change") : t("signup.cv_upload")}
            </button>
          </div>
          <Field id="full_name" label={t("auth.full_name")} prefix={UserCircle}>
            <input id="full_name" value={form.full_name} onChange={(e) => set("full_name")(e.target.value)} autoComplete="name" required maxLength={140} />
          </Field>
          <Field id="email" label={t("auth.email")} prefix={Envelope}>
            <input id="email" type="email" value={form.email} onChange={(e) => set("email")(e.target.value)} autoComplete="email" required maxLength={200} inputMode="email" dir="ltr" className="rtl:text-right" />
          </Field>
          <Field id="password" label={t("auth.password")} hint={t("auth.password_hint")} prefix={LockSimple}>
            <PasswordInput id="password" value={form.password} onChange={set("password")} autoComplete="new-password" describedBy="password-hint" />
          </Field>
          <button type="submit" className="aegov-btn btn-block" disabled={busy || !form.full_name || !form.email || !form.password}><UserPlus size={20} aria-hidden="true" />{busy ? t("auth.working") : t("auth.signup_button")}</button>
        </form>
      )}
      <p className="mt-6 text-center text-aeblack-700">
        {t("auth.have_account")} <Link className="font-semibold text-primary-700 underline" to={`${path("/sign-in")}?next=${encodeURIComponent(next)}`}><SignInIcon size={20} className="me-1 inline align-text-bottom rtl:-scale-x-100" aria-hidden="true" />{t("auth.sign_in_link")}</Link>
      </p>
    </div>
  );
}
