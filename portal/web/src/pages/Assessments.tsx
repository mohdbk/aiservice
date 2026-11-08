import { CalendarBlank, CaretRight, Clock, Exam, Repeat } from "@phosphor-icons/react";
import { Link, useNavigate } from "react-router-dom";
import { api, AssessmentSummary } from "../api";
import { Alert, EmptyState, Skeleton } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";
import { AsmStatusBadge, typeIcon, typeKey, useDeadline } from "./assessmentBits";

export default function Assessments() {
  const { t, lang, path, errorText } = useSite();
  usePageTitle(t("asm.title"));
  const navigate = useNavigate();
  const deadline = useDeadline();
  const { data, error, reload } = useAsync((signal) => api<{ assessments: AssessmentSummary[] }>(`/api/assessments?lang=${lang}`, { signal }), [lang]);
  return (
    <div className="container mx-auto max-w-4xl px-4 py-10">
      <h1 className="mb-6 font-heading text-3xl font-bold text-aeblack-950">{t("asm.title")}</h1>
      {error ? (
        <Alert tone="error" title={errorText(error)}><button type="button" className="aegov-btn btn-outline btn-sm mt-3" onClick={reload}>{t("common.retry")}</button></Alert>
      ) : !data ? (
        <div className="space-y-4">{[0, 1].map((i) => <Skeleton key={i} className="h-32" />)}</div>
      ) : data.assessments.length === 0 ? (
        <EmptyState icon={Exam} title={t("asm.empty_title")} body={t("asm.empty_body")} />
      ) : (
        <ul className="space-y-4">
          {data.assessments.map((a) => {
            const I = typeIcon(a.type);
            const url = path(`/assessments/${encodeURIComponent(a.id)}`);
            return (
              <li key={a.id}>
                <article className="aegov-card card-bordered cursor-pointer bg-white transition hover:shadow-md"
                  onClick={(e) => { if (!(e.target as HTMLElement).closest("a")) navigate(url); }}>
                  <div className="flex flex-col gap-4 p-5 md:flex-row md:items-center">
                    <span className="inline-flex h-14 w-14 shrink-0 items-center justify-center rounded-xl bg-primary-50 text-primary-600" aria-hidden="true"><I size={32} /></span>
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-primary-700">{t(typeKey(a.type))}</p>
                      <h2 className="font-heading text-lg font-semibold text-aeblack-900"><Link to={url} className="hover:text-primary-700">{a.title}</Link></h2>
                      {a.job_title && <p className="text-sm text-aeblack-600">{t("asm.for_job", { job: a.job_title })}</p>}
                      <div className="mt-2 flex flex-wrap gap-x-5 gap-y-1 text-sm text-aeblack-700">
                        <span className="inline-flex items-center gap-1.5"><Clock size={20} className="text-primary-600" aria-hidden="true" />{a.minutes ? t("asm.duration", { minutes: a.minutes }) : t("asm.untimed")}</span>
                        {["to_do", "in_progress"].includes(a.status) && a.deadline && (
                          <span className="inline-flex items-center gap-1.5"><CalendarBlank size={20} className="text-primary-600" aria-hidden="true" />{t("asm.deadline", { date: deadline(a.deadline) })}</span>
                        )}
                        {a.attempts_allowed > 1 && (
                          <span className="inline-flex items-center gap-1.5"><Repeat size={20} className="text-primary-600" aria-hidden="true" />{t("asm.attempts", { used: a.attempts_used, allowed: a.attempts_allowed })}</span>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center gap-3">
                      <AsmStatusBadge status={a.status} />
                      <CaretRight size={20} className="text-primary-600 rtl:-scale-x-100" aria-hidden="true" />
                    </div>
                  </div>
                </article>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
