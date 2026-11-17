import { CalendarBlank, Check, CheckCircle, Clock, Files, ListNumbers, Play, Repeat } from "@phosphor-icons/react";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, ApiError, AssessmentSummary } from "../api";
import { IconBox } from "../components/Bits";
import { Breadcrumb } from "../components/Breadcrumb";
import { Alert, Skeleton } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";
import NotFound from "./NotFound";
import { AsmStatusBadge, typeIcon, typeKey, useDeadline } from "./assessmentBits";

export default function AssessmentIntro() {
  const { id = "" } = useParams();
  const { t, lang, path, errorText, config } = useSite();
  const navigate = useNavigate();
  const deadline = useDeadline();
  const [ready, setReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // The language of the assessment itself (questions, instructions); the page around it stays in the portal's language.
  const [testLang, setTestLang] = useState<string | null>(null);
  const contentLang = testLang || lang;
  const { data: a, error: loadError } = useAsync((signal) => api<AssessmentSummary>(`/api/assessments/${encodeURIComponent(id)}?lang=${contentLang}`, { signal }), [id, contentLang]);
  const chosen = testLang || (a?.language && a.languages?.includes(a.language) && a.status === "in_progress" ? a.language : a?.languages?.includes(lang) ? lang : a?.language) || lang;
  const contentDir = config.languages.find((l) => l.code === chosen)?.dir || (chosen === "ar" ? "rtl" : "ltr");
  usePageTitle(a?.title || t("asm.title"));
  if (loadError instanceof ApiError && loadError.status === 404) return <NotFound />;
  if (loadError) return <div className="container mx-auto max-w-3xl px-4 py-10"><Alert tone="error" title={errorText(loadError)} /></div>;
  if (!a) return <div className="container mx-auto max-w-3xl space-y-4 px-4 py-10"><Skeleton className="h-10 w-1/2" /><Skeleton className="h-64" /></div>;

  const I = typeIcon(a.type);
  const resume = a.status === "in_progress";
  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      await api(`/api/assessments/${encodeURIComponent(a.id)}/start`, { body: { lang: chosen } });
      navigate(path(`/assessments/${encodeURIComponent(a.id)}/take`));
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  };
  const done = a.status === "submitted" || a.status === "completed";

  return (
    <div className="container mx-auto max-w-3xl px-4 py-10">
      <Breadcrumb items={[{ label: t("asm.title"), to: path("/assessments") }, { label: a.title }]} back={t("asm.back")} />
      <div className="flex items-start gap-4">
        <span className="inline-flex h-16 w-16 shrink-0 items-center justify-center rounded-xl bg-primary-600 text-whitely-50" aria-hidden="true"><I size={40} /></span>
        <div>
          <p className="text-sm font-medium text-primary-700">{t(typeKey(a.type))}</p>
          <h1 className="font-heading text-3xl font-bold text-aeblack-950" lang={chosen} dir={contentDir}>{a.title}</h1>
          {a.job_title && <p className="mt-1 text-aeblack-600">{t("asm.for_job", { job: a.job_title })}</p>}
          <div className="mt-2"><AsmStatusBadge status={a.status} /></div>
        </div>
      </div>

      <section className="mc-surface mt-6 grid gap-4 rounded-2xl border border-aeblack-100 bg-white p-6 sm:grid-cols-2">
        <div className="flex items-center gap-3"><IconBox icon={Clock} /><div><p className="text-sm text-aeblack-600">{t("asm.total_time")}</p><p className="font-semibold">{a.minutes ? t("asm.duration", { minutes: a.minutes }) : t("asm.untimed")}</p></div></div>
        <div className="flex items-center gap-3"><IconBox icon={ListNumbers} /><div><p className="text-sm text-aeblack-600">{t("asm.sections")}</p><p className="font-semibold">{a.sections.length}</p></div></div>
        {!done && a.deadline && <div className="flex items-center gap-3"><IconBox icon={CalendarBlank} /><div><p className="text-sm text-aeblack-600">{t("asm.deadline_label")}</p><p className="font-semibold">{deadline(a.deadline)}</p></div></div>}
        <div className="flex items-center gap-3"><IconBox icon={Repeat} /><div><p className="text-sm text-aeblack-600">{t("asm.attempts_label")}</p><p className="font-semibold">{t("asm.attempts_value", { used: a.attempts_used, allowed: a.attempts_allowed })}</p></div></div>
      </section>

      {done ? (
        <section className="mc-surface mt-6 overflow-hidden rounded-2xl border border-aeblack-100 bg-white" aria-labelledby="result-title">
          {/* The results screen: a clear confirmation, the score where the Ministry shows it, and what happens next. */}
          <div className="mc-hero flex flex-col items-center gap-3 px-6 py-8 text-center">
            {a.score !== null ? <ScoreRing value={a.score} label={t("asm.score")} /> : (
              <span className="flex h-20 w-20 items-center justify-center rounded-full bg-aegreen-100 text-aegreen-700" aria-hidden="true"><CheckCircle size={52} weight="duotone" /></span>
            )}
            <h2 id="result-title" className="font-heading text-2xl font-bold text-aeblack-950">{t("asm.done_title")}</h2>
            {a.result && <span className={`aegov-badge ${a.result === "Pass" ? "badge-success" : "badge-info"}`}>{t(a.result === "Pass" ? "asm.result_pass" : "asm.result_fail")}</span>}
            <p className="max-w-lg text-aeblack-700">{t("asm.done_body")}</p>
          </div>
          <div className="grid gap-6 border-t border-aeblack-100 p-6 md:grid-cols-2">
            <div>
              <h3 className="mb-3 font-heading text-base font-semibold">{t("asm.done_sections")}</h3>
              <ul className="space-y-2">
                {a.sections.map((sec, i) => (
                  <li key={i} className="flex items-center gap-3 text-sm">
                    <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary-600 text-white" aria-hidden="true"><Check size={14} weight="bold" /></span>
                    <span lang={chosen} dir={contentDir}>{sec.title}</span>
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <h3 className="mb-3 font-heading text-base font-semibold">{t("asm.next_title")}</h3>
              <ol className="space-y-2 text-sm text-aeblack-700">
                {["asm.next_1", "asm.next_2", "asm.next_3"].map((k, i) => (
                  <li key={k} className="flex gap-3"><span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary-50 text-xs font-bold text-primary-700">{i + 1}</span>{t(k)}</li>
                ))}
              </ol>
            </div>
          </div>
          <div className="flex flex-wrap gap-3 border-t border-aeblack-100 p-6">
            <Link to={path("/applications")} className="aegov-btn"><Files size={20} aria-hidden="true" />{t("asm.view_applications")}</Link>
            <Link to={path("/assessments")} className="aegov-btn btn-outline">{t("asm.back")}</Link>
          </div>
        </section>
      ) : (
        <>
          {(a.languages?.length ?? 0) > 1 && (
            <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6">
              <h2 id="asm-lang" className="mb-1 font-heading text-xl font-semibold">{t("asm.language")}</h2>
              <p className="mb-4 text-sm text-aeblack-600">{t("asm.language_hint")}</p>
              <div role="radiogroup" aria-labelledby="asm-lang" className="flex flex-wrap gap-3">
                {a.languages!.map((code) => {
                  const on = chosen === code;
                  const label = config.languages.find((l) => l.code === code)?.label || code;
                  return (
                    <button key={code} type="button" role="radio" aria-checked={on} onClick={() => setTestLang(code)} lang={code}
                      className={`min-w-36 rounded-xl border-2 px-5 py-3 text-start font-semibold transition ${on ? "border-primary-600 bg-primary-50 text-primary-800" : "border-aeblack-200 bg-white text-aeblack-700 hover:border-primary-300"}`}>
                      {label}
                    </button>
                  );
                })}
              </div>
            </section>
          )}
          <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6">
            <h2 className="mb-3 font-heading text-xl font-semibold">{t("asm.sections")}</h2>
            <ol className="divide-y divide-aeblack-100">
              {a.sections.map((s, i) => (
                <li key={i} className="flex items-center justify-between gap-3 py-3">
                  <span className="flex items-center gap-3"><span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary-50 text-sm font-bold text-primary-700">{i + 1}</span><span lang={chosen} dir={contentDir}>{s.title}</span></span>
                  <span className="text-sm text-aeblack-600">{s.minutes ? t("asm.duration", { minutes: s.minutes }) : t("asm.untimed")}</span>
                </li>
              ))}
            </ol>
            {a.extra_time_percent > 0 && <p className="mt-3 text-sm text-aeblack-600">{t("asm.extra_time", { percent: a.extra_time_percent })}</p>}
          </section>
          {a.instructions && (
            <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6">
              <h2 className="mb-3 font-heading text-xl font-semibold">{t("asm.instructions")}</h2>
              <div className="mc-prose" lang={chosen} dir={contentDir} dangerouslySetInnerHTML={{ __html: a.instructions }} />
            </section>
          )}
          <section className="mt-6">
            <Alert tone="info" title={t("asm.before_you_start")}>
              <ul className="mt-2 list-disc space-y-1 ps-5">
                <li>{t("asm.rule_sections")}</li><li>{t("asm.rule_timer")}</li><li>{t("asm.rule_save")}</li><li>{t("asm.rule_own")}</li>
              </ul>
            </Alert>
          </section>
          {error ? <div className="mt-4"><Alert tone="error" title={errorText(error)} /></div> : null}
          {!a.can_start && a.reason ? (
            <div className="mt-6"><Alert tone="warning" title={t(`error.${a.reason}`)} /></div>
          ) : (
            <div className="mt-6 space-y-4">
              {!resume && (
                <div className="aegov-check-item">
                  <input id="ready" type="checkbox" checked={ready} onChange={(e) => setReady(e.target.checked)} />
                  <label htmlFor="ready">{t("asm.ready")}</label>
                </div>
              )}
              <button type="button" className="aegov-btn btn-lg" disabled={busy || (!resume && !ready)} onClick={start}>
                <Play size={24} weight="fill" className="rtl:-scale-x-100" aria-hidden="true" />{resume ? t("asm.continue") : t("asm.start")}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

/** The score as a ring, with the number in the middle. */
function ScoreRing({ value, label }: { value: number; label: string }) {
  const r = 42;
  const c = 2 * Math.PI * r;
  const v = Math.max(0, Math.min(100, value));
  return (
    <figure className="relative h-28 w-28" aria-label={`${label}: ${v}%`} role="img">
      <svg viewBox="0 0 100 100" className="h-full w-full -rotate-90" aria-hidden="true">
        <circle cx="50" cy="50" r={r} fill="none" strokeWidth="9" className="stroke-primary-100" />
        <circle cx="50" cy="50" r={r} fill="none" strokeWidth="9" strokeLinecap="round" className="stroke-primary-600" strokeDasharray={c} strokeDashoffset={c * (1 - v / 100)} />
      </svg>
      <figcaption className="absolute inset-0 flex flex-col items-center justify-center" aria-hidden="true">
        <span className="font-heading text-2xl font-bold text-aeblack-950" dir="ltr">{v}%</span>
        <span className="text-[11px] text-aeblack-600">{label}</span>
      </figcaption>
    </figure>
  );
}
