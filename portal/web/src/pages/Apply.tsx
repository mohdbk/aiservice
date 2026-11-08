import { FormEvent, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, ApiError, fileToBase64, Job } from "../api";
import { useAuth } from "../auth";
import { Field, Steps } from "../components/Bits";
import { Alert, Skeleton, Spinner } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";
import NotFound from "./NotFound";

const CV_RE = /\.(pdf|docx|jpe?g|png)$/i;

export default function Apply() {
  const { id = "" } = useParams();
  const { t, lang, path, config, errorText } = useSite();
  const { user, clear } = useAuth();
  const navigate = useNavigate();
  usePageTitle(t("apply.title"));
  const { data: job, error: loadError } = useAsync((signal) => api<Job>(`/api/jobs/${encodeURIComponent(id)}?lang=${lang}`, { signal }), [id, lang]);
  const [phone, setPhone] = useState("");
  const [country, setCountry] = useState("");
  const [note, setNote] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [declared, setDeclared] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const errRef = useRef<HTMLDivElement>(null);
  useEffect(() => { if (error) errRef.current?.querySelector<HTMLElement>("[role=alert]")?.focus(); }, [error]);

  if (loadError && (loadError as ApiError).status === 404) return <NotFound />;
  if (!job) return <div className="container mx-auto max-w-3xl space-y-4 px-4 py-10"><Skeleton className="h-10 w-1/2" /><Skeleton className="h-96" /></div>;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!/^\+?[0-9][0-9 ()-]{6,19}$/.test(phone.trim())) return setError(new ApiError("phone_invalid", 400));
    if (!file) return setError(new ApiError("cv_required", 400));
    if (!CV_RE.test(file.name)) return setError(new ApiError("cv_type", 400));
    if (file.size > config.max_cv_mb * 1024 * 1024) return setError(new ApiError("cv_too_large", 400, { max_mb: config.max_cv_mb }));
    if (!declared) return setError(new ApiError("declaration_required", 400));
    setBusy(true);
    try {
      const data = await fileToBase64(file);
      const r = await api<{ application: string }>("/api/applications", {
        body: { job: job.id, phone: phone.trim(), country, cover_note: note, declaration: true, lang, cv: { name: file.name, type: file.type, data } },
      });
      navigate(`${path(`/applications/${encodeURIComponent(r.application)}`)}?new=1`);
    } catch (err) {
      if (err instanceof ApiError && err.code === "signed_out") clear();
      setError(err);
      setBusy(false);
    }
  };

  return (
    <div className="container mx-auto max-w-3xl px-4 py-10">
      <Steps current={2} />
      <h1 className="font-heading text-3xl font-bold text-aeblack-950">{t("apply.title")}</h1>
      <div className="mc-surface mt-4 rounded-xl border border-primary-200 bg-primary-50 px-5 py-4">
        <p className="text-sm text-aeblack-600">{t("apply.for")}</p>
        <p className="font-heading text-lg font-semibold text-aeblack-900" lang={job.content_lang} dir={job.content_dir}>{job.title}</p>
      </div>

      {!job.accepting ? (
        <div className="mt-6"><Alert tone="warning" title={t("job.closed")} /></div>
      ) : (
        <form onSubmit={submit} className="mc-surface mt-6 space-y-6 rounded-2xl border border-aeblack-100 bg-white p-6 md:p-8" noValidate aria-busy={busy}>
          <h2 className="font-heading text-xl font-semibold text-aeblack-900">{t("apply.your_details")}</h2>
          <dl className="grid gap-4 rounded-xl bg-whitely-100 p-4 sm:grid-cols-2">
            <div><dt className="text-sm text-aeblack-600">{t("apply.name")}</dt><dd className="font-medium text-aeblack-900">{user?.full_name}</dd></div>
            <div><dt className="text-sm text-aeblack-600">{t("apply.email")}</dt><dd className="font-medium text-aeblack-900"><bdi>{user?.email}</bdi></dd></div>
          </dl>
          <div className="grid gap-5 md:grid-cols-2">
            <Field id="phone" label={t("apply.phone")} hint={t("apply.phone_hint")}>
              <input id="phone" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} autoComplete="tel" required maxLength={30} dir="ltr" aria-describedby="phone-hint" />
            </Field>
            <Field id="country" label={t("apply.country")}>
              <select id="country" value={country} onChange={(e) => setCountry(e.target.value)} autoComplete="country-name">
                <option value="">{t("apply.country_choose")}</option>
                {config.countries.map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
            </Field>
          </div>

          <div>
            <p className="mb-1 block text-sm font-medium leading-6 text-aeblack-800" id="cv-label">{t("apply.cv")}</p>
            <input id="cv" type="file" className="peer sr-only" accept=".pdf,.docx,.jpg,.jpeg,.png" aria-labelledby="cv-label cv-choose" aria-describedby="cv-hint"
              onChange={(e) => setFile(e.target.files?.[0] || null)} />
            <label htmlFor="cv" className={`flex cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed px-4 py-8 text-center transition peer-focus-visible:ring-2 peer-focus-visible:ring-primary-600 ${file ? "border-aegreen-500 bg-aegreen-50" : "border-primary-300 bg-whitely-50 hover:border-primary-500 hover:bg-primary-50"}`}>
              {file ? (
                <svg className="h-9 w-9 text-aegreen-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8l-5-5Z" /><path d="M14 3v5h5M9 14l2 2 4-4" /></svg>
              ) : (
                <svg className="h-9 w-9 text-primary-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M12 16V4m0 0-4 4m4-4 4 4M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3" /></svg>
              )}
              <span id="cv-choose" className="font-semibold text-primary-800"><bdi>{file ? t("apply.cv_selected", { name: file.name }) : t("apply.cv_choose")}</bdi></span>
              <span className="text-sm text-aeblack-600" id="cv-hint">{t("apply.cv_hint", { max: config.max_cv_mb })}</span>
            </label>
          </div>

          <Field id="note" label={t("apply.note")} optional>
            <textarea id="note" rows={5} maxLength={4000} value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>

          <div className="aegov-check-item">
            <input id="declaration" type="checkbox" checked={declared} onChange={(e) => setDeclared(e.target.checked)} required />
            <label htmlFor="declaration">{t("apply.declaration")}</label>
          </div>

          <div ref={errRef}>{error ? <Alert tone="error" title={errorText(error)} /> : null}</div>

          <div className="flex flex-wrap items-center gap-3">
            <button type="submit" className="aegov-btn" disabled={busy}>{t("apply.submit")}</button>
            <Link to={path(`/jobs/${encodeURIComponent(job.id)}`)} className="aegov-btn btn-link">{t("apply.cancel")}</Link>
            {busy && <Spinner label={t("apply.submitting")} />}
          </div>
        </form>
      )}
    </div>
  );
}
