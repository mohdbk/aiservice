import { Check, LinkSimple, PaperPlaneTilt, SignIn as SignInIcon } from "@phosphor-icons/react";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, ApplicationSummary, Job } from "../api";
import { useAuth } from "../auth";
import { IconBox, META_ICONS, useSalary } from "../components/Bits";
import { Alert, Skeleton } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";
import NotFound from "./NotFound";
import { Breadcrumb } from "../components/Breadcrumb";

export default function JobPage() {
  const { id = "" } = useParams();
  const { t, lang, path, formatDate, errorText } = useSite();
  const { user } = useAuth();
  const salary = useSalary();
  const [copied, setCopied] = useState(false);
  const { data: job, error, reload } = useAsync((signal) => api<Job>(`/api/jobs/${encodeURIComponent(id)}?lang=${lang}`, { signal }), [id, lang]);
  const { data: mine } = useAsync(
    (signal) => (user ? api<{ applications: ApplicationSummary[] }>(`/api/applications?lang=${lang}`, { signal }) : Promise.resolve({ applications: [] })),
    [user?.email, lang],
  );
  usePageTitle(job?.title || t("site.portal"));
  const applied = mine?.applications.find((a) => a.job === id);

  if (error && (error as { status?: number }).status === 404) return <NotFound />;
  if (error) return <div className="container mx-auto px-4 py-10"><Alert tone="error" title={errorText(error)}><button type="button" className="aegov-btn btn-outline btn-sm mt-3" onClick={reload}>{t("common.retry")}</button></Alert></div>;
  if (!job) return <div className="container mx-auto space-y-4 px-4 py-10"><Skeleton className="h-8 w-2/3" /><Skeleton className="h-64" /></div>;

  const meta: [keyof typeof META_ICONS, string, string][] = [
    ["building", t("job.department"), job.department],
    ["pin", t("job.location"), job.location],
    ["clock", t("job.type"), job.employment_type],
    ["calendar", t("job.posted"), formatDate(job.posted_on)],
    ["calendar", t("job.closes"), formatDate(job.closes_on)],
    ["money", t("job.salary"), salary(job.salary)],
  ];

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(window.location.href);
      setCopied(true);
      setTimeout(() => setCopied(false), 2500);
    } catch {
      /* clipboard blocked */
    }
  };

  return (
    <div className="container mx-auto px-4 py-8 md:py-10">
      <Breadcrumb items={[{ label: t("nav.jobs"), to: path() }, { label: job.title }]} back={t("common.back_to_jobs")} />
      <div className="grid gap-8 lg:grid-cols-3">
        <article className="lg:col-span-2">
          <h1 className="font-heading text-3xl font-bold leading-tight text-aeblack-950 md:text-4xl" lang={job.content_lang} dir={job.content_dir}>{job.title}</h1>
          {job.department && <p className="mt-2 text-lg text-aeblack-600">{job.department}</p>}
          <section className="mc-surface mt-8 rounded-2xl border border-aeblack-100 bg-white p-6 md:p-8" aria-labelledby="about">
            <h2 id="about" className="mb-4 font-heading text-xl font-semibold text-aeblack-900">{t("job.about")}</h2>
            {job.description_html ? (
              <div className="mc-prose text-lg text-aeblack-800" lang={job.content_lang} dir={job.content_dir} dangerouslySetInnerHTML={{ __html: job.description_html }} />
            ) : <p className="text-aeblack-600">{t("job.no_description")}</p>}
          </section>
        </article>

        <aside className="lg:sticky lg:top-36 lg:self-start">
          <div className="mc-surface rounded-2xl border border-aeblack-100 bg-white p-6 shadow-sm">
            <h2 className="mb-4 font-heading text-lg font-semibold text-aeblack-900">{t("job.details")}</h2>
            <dl className="space-y-3">
              {meta.filter(([, , v]) => v).map(([icon, label, value]) => (
                <div key={label} className="relative flex min-h-10 flex-col justify-center ps-[3.25rem]">
                  <dt className="text-sm text-aeblack-600"><IconBox icon={META_ICONS[icon]} className="absolute start-0 top-1/2 -translate-y-1/2" />{label}</dt>
                  <dd className="font-medium text-aeblack-900">{value}</dd>
                </div>
              ))}
            </dl>
            <div className="mt-6 space-y-3 border-t border-aeblack-100 pt-6">
              {applied ? (
                <>
                  <Alert tone="success" title={t("job.already_applied")} />
                  <Link className="aegov-btn btn-outline btn-block" to={path(`/applications/${encodeURIComponent(applied.id)}`)}>{t("job.view_application")}</Link>
                </>
              ) : !job.accepting ? (
                <Alert tone="warning" title={t("job.closed")} />
              ) : user ? (
                <Link className="aegov-btn btn-block" to={path(`/jobs/${encodeURIComponent(job.id)}/apply`)}><PaperPlaneTilt size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("job.apply_now")}</Link>
              ) : (
                <>
                  <Link className="aegov-btn btn-block" to={`${path("/sign-in")}?next=${encodeURIComponent(path(`/jobs/${job.id}/apply`))}`}><SignInIcon size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("job.sign_in_to_apply")}</Link>
                  <p className="text-center text-sm text-aeblack-600">
                    <Link className="text-primary-700 underline" to={`${path("/create-account")}?next=${encodeURIComponent(path(`/jobs/${job.id}/apply`))}`}>{t("job.new_here")}</Link>
                  </p>
                </>
              )}
              <button type="button" className="aegov-btn btn-link btn-sm btn-block" onClick={copy} aria-live="polite">{copied ? <Check size={20} aria-hidden="true" /> : <LinkSimple size={20} aria-hidden="true" />}{copied ? t("job.copied") : t("job.share")}</button>
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
}
