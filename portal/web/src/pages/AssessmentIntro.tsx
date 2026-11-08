import { CalendarBlank, CheckCircle, Clock, ListNumbers, Play, Repeat } from "@phosphor-icons/react";
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
  const { t, lang, path, errorText } = useSite();
  const navigate = useNavigate();
  const deadline = useDeadline();
  const [ready, setReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const { data: a, error: loadError } = useAsync((signal) => api<AssessmentSummary>(`/api/assessments/${encodeURIComponent(id)}?lang=${lang}`, { signal }), [id, lang]);
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
      await api(`/api/assessments/${encodeURIComponent(a.id)}/start`, { body: { lang } });
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
          <h1 className="font-heading text-3xl font-bold text-aeblack-950">{a.title}</h1>
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
        <section className="mt-6 space-y-4">
          <Alert tone="success" title={t("asm.done_title")}>{t("asm.done_body")}</Alert>
          {a.score !== null && (
            <div className="mc-surface flex items-center gap-4 rounded-2xl border border-aeblack-100 bg-white p-6">
              <CheckCircle size={48} weight="duotone" className="text-aegreen-600" aria-hidden="true" />
              <div><p className="text-sm text-aeblack-600">{t("asm.score")}</p><p className="font-heading text-3xl font-bold">{a.score}%</p>
                {a.result && <p className="font-semibold">{t(a.result === "Pass" ? "asm.result_pass" : "asm.result_fail")}</p>}</div>
            </div>
          )}
          <Link to={path("/assessments")} className="aegov-btn btn-outline">{t("asm.back")}</Link>
        </section>
      ) : (
        <>
          <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6">
            <h2 className="mb-3 font-heading text-xl font-semibold">{t("asm.sections")}</h2>
            <ol className="divide-y divide-aeblack-100">
              {a.sections.map((s, i) => (
                <li key={i} className="flex items-center justify-between gap-3 py-3">
                  <span className="flex items-center gap-3"><span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary-50 text-sm font-bold text-primary-700">{i + 1}</span>{s.title}</span>
                  <span className="text-sm text-aeblack-600">{s.minutes ? t("asm.duration", { minutes: s.minutes }) : t("asm.untimed")}</span>
                </li>
              ))}
            </ol>
            {a.extra_time_percent > 0 && <p className="mt-3 text-sm text-aeblack-600">{t("asm.extra_time", { percent: a.extra_time_percent })}</p>}
          </section>
          {a.instructions && (
            <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6">
              <h2 className="mb-3 font-heading text-xl font-semibold">{t("asm.instructions")}</h2>
              <div className="mc-prose" dangerouslySetInnerHTML={{ __html: a.instructions }} />
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
