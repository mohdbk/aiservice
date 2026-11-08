import { ArrowCounterClockwise, CaretLeft, CaretRight, MagnifyingGlass } from "@phosphor-icons/react";
import { FormEvent, MouseEvent, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, Job, JobList, qs } from "../api";
import { MetaIcon } from "../components/Bits";
import { Alert, EmptyState, Skeleton } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";

function JobCard({ job }: { job: Job }) {
  const { t, path, formatDate } = useSite();
  const navigate = useNavigate();
  const url = path(`/jobs/${encodeURIComponent(job.id)}`);
  // The whole card opens the job; the title stays the one link for keyboard and screen reader users.
  const open = (e: MouseEvent) => {
    if ((e.target as HTMLElement).closest("a") || window.getSelection()?.toString()) return;
    navigate(url);
  };
  return (
    <li>
      <article onClick={open} className="aegov-card card-bordered group relative h-full cursor-pointer bg-white transition hover:shadow-md">
        <div className="flex h-full flex-col gap-3 p-5 md:p-6">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            {job.department && <span className="aegov-badge badge-sm">{job.department}</span>}
            {job.employment_type && <span className="text-aeblack-600">{job.employment_type}</span>}
          </div>
          <h3 className="font-heading text-xl font-semibold leading-snug text-aeblack-900" lang={job.content_lang} dir={job.content_dir}>
            <Link to={url} className="hover:text-primary-700">{job.title}</Link>
          </h3>
          {job.summary && <p className="line-clamp-2 text-aeblack-700" lang={job.content_lang} dir={job.content_dir}>{job.summary}</p>}
          <div className="mt-auto flex flex-wrap items-center gap-x-5 gap-y-2 border-t border-aeblack-100 pt-3 text-sm text-aeblack-700">
            {job.location && <span className="inline-flex items-center gap-1.5"><MetaIcon name="pin" />{job.location}</span>}
            {job.closes_on && <span className="inline-flex items-center gap-1.5"><MetaIcon name="calendar" />{t("jobs.closes", { date: formatDate(job.closes_on) })}</span>}
            <Link to={url} tabIndex={-1} aria-hidden="true" className="ms-auto inline-flex items-center gap-1 font-semibold text-primary-700 hover:text-primary-800">
              {t("jobs.view")}
              <CaretRight size={20} className="rtl:-scale-x-100" />
            </Link>
          </div>
        </div>
      </article>
    </li>
  );
}

export default function Jobs() {
  const { t, lang, errorText } = useSite();
  usePageTitle(t("site.portal"));
  const [params, setParams] = useSearchParams();
  const q = params.get("q") || "";
  const department = params.get("department") || "";
  const location = params.get("location") || "";
  const page = Number(params.get("page")) || 1;
  const [draft, setDraft] = useState(q);
  const { data, error, loading, reload } = useAsync(
    (signal) => api<JobList>(`/api/jobs?${qs({ lang, q, department, location, page })}`, { signal }),
    [lang, q, department, location, page],
  );
  const update = (next: Record<string, string>) => {
    const p = new URLSearchParams(params);
    for (const [k, v] of Object.entries(next)) (v ? p.set(k, v) : p.delete(k));
    if (!("page" in next)) p.delete("page");
    setParams(p);
  };
  const submit = (e: FormEvent) => {
    e.preventDefault();
    update({ q: draft.trim() });
  };
  const filtered = Boolean(q || department || location);

  return (
    <>
      <section className="mc-hero border-b border-aeblack-100">
        <div className="container mx-auto px-4 py-12 md:py-16">
          <h1 className="max-w-3xl font-heading text-3xl font-bold leading-tight text-aeblack-950 md:text-5xl">{t("jobs.hero_title")}</h1>
          <p className="mt-4 max-w-2xl text-lg text-aeblack-700">{t("jobs.hero_body")}</p>

          <form role="search" onSubmit={submit} className="mc-surface mt-8 rounded-2xl border border-aeblack-100 bg-white p-4 shadow-sm md:p-5">
            <div className="grid gap-4 md:grid-cols-12 md:items-end">
              <div className="aegov-form-control md:col-span-5">
                <label htmlFor="q">{t("jobs.search_label")}</label>
                <div className="form-control-input">
                  <span className="control-prefix" aria-hidden="true"><MagnifyingGlass size={24} /></span>
                  <input id="q" type="search" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder={t("jobs.search_placeholder")} maxLength={100} />
                </div>
              </div>
              <div className="aegov-form-control md:col-span-3">
                <label htmlFor="department">{t("jobs.department")}</label>
                <div className="form-control-input">
                  <select id="department" value={department} onChange={(e) => update({ department: e.target.value })}>
                    <option value="">{t("jobs.all")}</option>
                    {(data?.filters.departments || (department ? [department] : [])).map((d) => <option key={d} value={d}>{d}</option>)}
                  </select>
                </div>
              </div>
              <div className="aegov-form-control md:col-span-2">
                <label htmlFor="location">{t("jobs.location")}</label>
                <div className="form-control-input">
                  <select id="location" value={location} onChange={(e) => update({ location: e.target.value })}>
                    <option value="">{t("jobs.all")}</option>
                    {(data?.filters.locations || (location ? [location] : [])).map((d) => <option key={d} value={d}>{d}</option>)}
                  </select>
                </div>
              </div>
              <div className="md:col-span-2">
                <button type="submit" className="aegov-btn btn-block"><MagnifyingGlass size={20} aria-hidden="true" />{t("jobs.search_button")}</button>
              </div>
            </div>
          </form>
        </div>
      </section>

      <section className="container mx-auto px-4 py-10" aria-labelledby="results-title" aria-busy={loading}>
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
          <h2 id="results-title" className="font-heading text-xl font-semibold text-aeblack-900" aria-live="polite">
            {data ? (data.total === 1 ? t("jobs.count_one") : t("jobs.count", { count: data.total })) : t("common.loading")}
          </h2>
          {filtered && (
            <button type="button" className="aegov-btn btn-link btn-sm" onClick={() => { setDraft(""); setParams(new URLSearchParams()); }}>
              <ArrowCounterClockwise size={20} aria-hidden="true" />{t("jobs.clear")}
            </button>
          )}
        </div>

        {error ? (
          <Alert tone="error" title={errorText(error)}>
            <button type="button" className="aegov-btn btn-outline btn-sm mt-3" onClick={reload}>{t("common.retry")}</button>
          </Alert>
        ) : !data ? (
          <ul className="grid gap-5 md:grid-cols-2">
            {[0, 1, 2, 3].map((i) => <li key={i}><Skeleton className="h-52" /></li>)}
          </ul>
        ) : data.jobs.length === 0 ? (
          filtered ? <EmptyState icon={MagnifyingGlass} title={t("jobs.none_title")} body={t("jobs.none_body")} /> : <EmptyState title={t("jobs.no_jobs_title")} body={t("jobs.no_jobs_body")} />
        ) : (
          <>
            <ul className={`grid gap-5 md:grid-cols-2 ${loading ? "opacity-60" : ""}`}>
              {data.jobs.map((j) => <JobCard key={j.id} job={j} />)}
            </ul>
            {data.pages > 1 && (
              <nav className="mt-10 flex justify-center" aria-label={t("jobs.pagination")}>
                {/* UAE Design System pagination */}
                <div className="aegov-pagination">
                  <button type="button" className="aegov-pagination-previous disabled:opacity-40" disabled={page <= 1} onClick={() => update({ page: String(page - 1) })}>
                    <CaretLeft size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("jobs.previous")}
                  </button>
                  <span className="pages">
                    {Array.from({ length: data.pages }, (_, i) => i + 1).filter((n) => n === 1 || n === data.pages || Math.abs(n - page) <= 1).map((n, i, arr) => (
                      <span key={n} className="inline-flex items-center">
                        {i > 0 && n - arr[i - 1] > 1 && <span className="aegov-pagination-extend">...</span>}
                        {n === page
                          ? <span className="aegov-pagination-current" aria-current="page">{n}</span>
                          : <button type="button" className="aegov-pagination-page" onClick={() => update({ page: String(n) })} aria-label={t("jobs.page", { page: n, pages: data.pages })}>{n}</button>}
                      </span>
                    ))}
                  </span>
                  <button type="button" className="aegov-pagination-next disabled:opacity-40" disabled={page >= data.pages} onClick={() => update({ page: String(page + 1) })}>
                    {t("jobs.next")}<CaretRight size={20} className="rtl:-scale-x-100" aria-hidden="true" />
                  </button>
                </div>
              </nav>
            )}
          </>
        )}
      </section>
    </>
  );
}
