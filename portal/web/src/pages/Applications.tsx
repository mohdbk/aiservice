import { CaretRight, FileText, Files } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { api, ApplicationSummary } from "../api";
import { IconBox, StatusBadge } from "../components/Bits";
import { Alert, EmptyState, Skeleton } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";

export default function Applications() {
  const { t, lang, path, formatDate, errorText } = useSite();
  usePageTitle(t("apps.title"));
  const { data, error, reload } = useAsync((signal) => api<{ applications: ApplicationSummary[] }>(`/api/applications?lang=${lang}`, { signal }), [lang]);
  return (
    <div className="container mx-auto max-w-4xl px-4 py-10">
      <h1 className="mb-6 font-heading text-3xl font-bold text-aeblack-950">{t("apps.title")}</h1>
      {error ? (
        <Alert tone="error" title={errorText(error)}><button type="button" className="aegov-btn btn-outline btn-sm mt-3" onClick={reload}>{t("common.retry")}</button></Alert>
      ) : !data ? (
        <div className="space-y-4">{[0, 1].map((i) => <Skeleton key={i} className="h-28" />)}</div>
      ) : data.applications.length === 0 ? (
        <EmptyState icon={Files} title={t("apps.empty_title")} body={t("apps.empty_body")} action={<Link className="aegov-btn" to={path()}>{t("apps.browse")}</Link>} />
      ) : (
        <ul className="space-y-4">
          {data.applications.map((a) => (
            <li key={a.id}>
              <article className="aegov-card card-bordered relative bg-white transition hover:shadow-md">
                <div className="flex flex-col gap-3 p-5 md:flex-row md:items-center md:justify-between">
                  <div className="flex items-start gap-4">
                  <IconBox icon={FileText} />
                  <div>
                    <h2 className="font-heading text-lg font-semibold text-aeblack-900">
                      <Link to={path(`/applications/${encodeURIComponent(a.id)}`)} className="after:absolute after:inset-0 hover:text-primary-700">{a.title}</Link>
                    </h2>
                    <p className="mt-1 text-sm text-aeblack-600">
                      {t("apps.submitted", { date: formatDate(a.submitted) })}
                      <span className="mx-2" aria-hidden="true">·</span>
                      <span dir="ltr">{t("apps.reference", { ref: a.reference })}</span>
                    </p>
                  </div>
                  </div>
                  <div className="flex items-center gap-3"><StatusBadge status={a.status} /><CaretRight size={20} className="text-primary-600 rtl:-scale-x-100" aria-hidden="true" /></div>
                </div>
              </article>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
