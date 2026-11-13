import { Briefcase, Envelope, FileText, Flag, GlobeHemisphereEast, ListChecks, PaperPlaneTilt, Phone, UploadSimple, UserCircle } from "@phosphor-icons/react";
import { FormEvent, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, ApiError, fileToBase64, Job, ScreeningQuestion } from "../api";
import { useAuth } from "../auth";
import { Field, IconBox, Steps } from "../components/Bits";
import { Alert, Skeleton, Spinner } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";
import NotFound from "./NotFound";

const CV_RE = /\.(pdf|docx|jpe?g|png)$/i;
const empty = (v: unknown) => v === undefined || v === null || v === "" || (Array.isArray(v) && v.length === 0);

function ScreeningField({ q, value, invalid, onChange }: { q: ScreeningQuestion; value: unknown; invalid: boolean; onChange: (v: unknown) => void }) {
  const { t } = useSite();
  const id = `sq-${q.key}`;
  const label = (
    <span>{q.text}{!q.required ? <span className="ms-1 font-normal text-aeblack-500">({t("common.optional")})</span> : null}
      {q.required ? <span className="text-aered-600" aria-hidden="true"> *</span> : null}</span>
  );
  const ring = invalid ? "rounded-lg ring-2 ring-aered-500 ring-offset-2" : "";
  if (q.type === "Yes or No" || q.type === "Single Choice") {
    const opts = q.type === "Yes or No" ? [{ value: "Yes", label: t("common.yes") }, { value: "No", label: t("common.no") }] : q.options;
    return (
      <fieldset className={ring} aria-invalid={invalid}>
        <legend className="mb-2 text-sm font-medium text-aeblack-800">{label}</legend>
        <div className={q.type === "Yes or No" ? "flex flex-wrap gap-3" : "space-y-2"}>
          {opts.map((o) => (
            <label key={o.value} className={`flex cursor-pointer items-center gap-3 rounded-lg border-2 px-4 py-2.5 ${value === o.value ? "border-primary-600 bg-primary-50" : "border-aeblack-100"}`}>
              <input type="radio" name={id} className="h-5 w-5 rounded-full accent-primary-600" checked={value === o.value} onChange={() => onChange(o.value)} />
              <span>{o.label}</span>
            </label>
          ))}
        </div>
      </fieldset>
    );
  }
  if (q.type === "Multiple Choice") {
    const vals = Array.isArray(value) ? (value as string[]) : [];
    return (
      <fieldset className={ring} aria-invalid={invalid}>
        <legend className="mb-2 text-sm font-medium text-aeblack-800">{label}</legend>
        <p className="mb-2 text-sm text-aeblack-600">{t("asm.select_all")}</p>
        <div className="flex flex-wrap gap-3">
          {q.options.map((o) => {
            const on = vals.includes(o.value);
            return (
              <label key={o.value} className={`flex cursor-pointer items-center gap-3 rounded-lg border-2 px-4 py-2.5 ${on ? "border-primary-600 bg-primary-50" : "border-aeblack-100"}`}>
                <input type="checkbox" className="h-5 w-5 rounded accent-primary-600" checked={on} onChange={() => onChange(on ? vals.filter((v) => v !== o.value) : [...vals, o.value])} />
                <span>{o.label}</span>
              </label>
            );
          })}
        </div>
      </fieldset>
    );
  }
  const input = q.type === "Long Text"
    ? <textarea id={id} rows={4} maxLength={4000} dir="auto" value={(value as string) || ""} onChange={(e) => onChange(e.target.value)} aria-invalid={invalid} />
    : <input id={id} type={q.type === "Date" ? "date" : "text"} inputMode={q.type === "Number" ? "decimal" : undefined} dir={q.type === "Short Text" ? "auto" : "ltr"}
        maxLength={q.type === "Short Text" ? 300 : 20} value={(value as string) || ""} onChange={(e) => onChange(e.target.value)} aria-invalid={invalid} />;
  return (
    <div className={`aegov-form-control ${ring}`}>
      <label htmlFor={id}>{label}</label>
      <div className="form-control-input">{input}</div>
    </div>
  );
}

export default function Apply() {
  const { id = "" } = useParams();
  const { t, lang, path, config, errorText } = useSite();
  const { user, clear } = useAuth();
  const navigate = useNavigate();
  usePageTitle(t("apply.title"));
  const { data: job, error: loadError } = useAsync((signal) => api<Job>(`/api/jobs/${encodeURIComponent(id)}?lang=${lang}`, { signal }), [id, lang]);
  const [phone, setPhone] = useState("");
  const [country, setCountry] = useState("");
  const [nationality, setNationality] = useState("");
  const [note, setNote] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [declared, setDeclared] = useState(false);
  const [talentPool, setTalentPool] = useState(false);
  const [screening, setScreening] = useState<Record<string, unknown>>({});
  const [badQuestions, setBadQuestions] = useState<string[]>([]);
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
    const missing = (job.screening || []).filter((q) => q.required && empty(screening[q.key])).map((q) => q.key);
    setBadQuestions(missing);
    if (missing.length) return setError(new ApiError("screening_required", 400));
    if (!declared) return setError(new ApiError("declaration_required", 400));
    setBusy(true);
    try {
      const data = await fileToBase64(file);
      const r = await api<{ application: string }>("/api/applications", {
        body: { job: job.id, phone: phone.trim(), country, nationality, cover_note: note, declaration: true, talent_pool: talentPool, lang, screening, cv: { name: file.name, type: file.type, data } },
      });
      navigate(`${path(`/applications/${encodeURIComponent(r.application)}`)}?new=1`);
    } catch (err) {
      if (err instanceof ApiError && err.code === "signed_out") clear();
      if (err instanceof ApiError && Array.isArray(err.params.questions)) setBadQuestions(err.params.questions as string[]);
      setError(err);
      setBusy(false);
    }
  };

  return (
    <div className="container mx-auto max-w-3xl px-4 py-10">
      <Steps current={2} />
      <h1 className="font-heading text-3xl font-bold text-aeblack-950">{t("apply.title")}</h1>
      <div className="mc-surface mt-4 flex items-center gap-4 rounded-xl border border-primary-200 bg-primary-50 px-5 py-4">
        <span className="inline-flex shrink-0 items-center justify-center rounded-lg bg-primary-600 p-2 text-whitely-50" aria-hidden="true"><Briefcase size={28} /></span>
        <div>
          <p className="text-sm text-aeblack-600">{t("apply.for")}</p>
          <p className="font-heading text-lg font-semibold text-aeblack-900" lang={job.content_lang} dir={job.content_dir}>{job.title}</p>
        </div>
      </div>

      {!job.accepting ? (
        <div className="mt-6"><Alert tone="warning" title={t("job.closed")} /></div>
      ) : (
        <form onSubmit={submit} className="mc-surface mt-6 space-y-6 rounded-2xl border border-aeblack-100 bg-white p-6 md:p-8" noValidate aria-busy={busy}>
          <h2 className="font-heading text-xl font-semibold text-aeblack-900">{t("apply.your_details")}</h2>
          <dl className="grid gap-4 rounded-xl bg-whitely-100 p-4 sm:grid-cols-2">
            <div className="relative flex min-h-10 flex-col justify-center ps-[3.25rem]"><dt className="text-sm text-aeblack-600"><IconBox icon={UserCircle} className="absolute start-0 top-1/2 -translate-y-1/2" />{t("apply.name")}</dt><dd className="font-medium text-aeblack-900">{user?.full_name}</dd></div>
            <div className="relative flex min-h-10 min-w-0 flex-col justify-center ps-[3.25rem]"><dt className="text-sm text-aeblack-600"><IconBox icon={Envelope} className="absolute start-0 top-1/2 -translate-y-1/2" />{t("apply.email")}</dt><dd className="truncate font-medium text-aeblack-900"><bdi>{user?.email}</bdi></dd></div>
          </dl>
          <div className="grid gap-5 md:grid-cols-2">
            <Field id="phone" label={t("apply.phone")} hint={t("apply.phone_hint")} prefix={Phone}>
              <input id="phone" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} autoComplete="tel" required maxLength={30} dir="ltr" className="rtl:text-right" aria-describedby="phone-hint" />
            </Field>
            <Field id="country" label={t("apply.country")} prefix={GlobeHemisphereEast}>
              <select id="country" value={country} onChange={(e) => setCountry(e.target.value)} autoComplete="country-name">
                <option value="">{t("apply.country_choose")}</option>
                {config.countries.map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
            </Field>
            <Field id="nationality" label={t("apply.nationality")} optional prefix={Flag}>
              <select id="nationality" value={nationality} onChange={(e) => setNationality(e.target.value)}>
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
              <span className={`flex h-16 w-16 items-center justify-center rounded-full ${file ? "bg-aegreen-100 text-aegreen-700" : "bg-primary-100 text-primary-700"}`} aria-hidden="true">
                {file ? <FileText size={40} weight="duotone" /> : <UploadSimple size={40} weight="duotone" />}
              </span>
              <span id="cv-choose" className="font-semibold text-primary-800"><bdi>{file ? t("apply.cv_selected", { name: file.name }) : t("apply.cv_choose")}</bdi></span>
              <span className="text-sm text-aeblack-600" id="cv-hint">{t("apply.cv_hint", { max: config.max_cv_mb })}</span>
            </label>
          </div>

          {job.screening && job.screening.length > 0 && (
            <fieldset className="space-y-5 rounded-xl border border-aeblack-100 p-5">
              <legend className="flex items-center gap-2 px-2 font-heading text-lg font-semibold text-aeblack-900">
                <ListChecks size={24} className="text-primary-600" aria-hidden="true" />{t("apply.screening_title")}
              </legend>
              {job.screening.map((q) => (
                <ScreeningField key={q.key} q={q} value={screening[q.key]} invalid={badQuestions.includes(q.key)}
                  onChange={(v) => { setScreening((s) => ({ ...s, [q.key]: v })); setBadQuestions((b) => b.filter((k) => k !== q.key)); }} />
              ))}
            </fieldset>
          )}

          <Field id="note" label={t("apply.note")} optional>
            <textarea id="note" rows={5} maxLength={4000} value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>

          <div className="aegov-check-item">
            <input id="talent-pool" type="checkbox" checked={talentPool} onChange={(e) => setTalentPool(e.target.checked)} />
            <label htmlFor="talent-pool">{t("apply.talent_pool")}</label>
          </div>

          <div className="aegov-check-item">
            <input id="declaration" type="checkbox" checked={declared} onChange={(e) => setDeclared(e.target.checked)} required />
            <label htmlFor="declaration">{t("apply.declaration")}</label>
          </div>

          <div ref={errRef}>{error ? <Alert tone="error" title={errorText(error)} /> : null}</div>

          <div className="flex flex-wrap items-center gap-3">
            <button type="submit" className="aegov-btn" disabled={busy}><PaperPlaneTilt size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("apply.submit")}</button>
            <Link to={path(`/jobs/${encodeURIComponent(job.id)}`)} className="aegov-btn btn-link">{t("apply.cancel")}</Link>
            {busy && <Spinner label={t("apply.submitting")} />}
          </div>
        </form>
      )}
    </div>
  );
}
