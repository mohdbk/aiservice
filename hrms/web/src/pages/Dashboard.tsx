import { ArrowRight, Briefcase, CalendarCheck, ChatCircleText, CheckCircle, Exam, Globe, Handshake, HourglassMedium, Lightbulb, Stamp, Translate, UserPlus, Users } from "@phosphor-icons/react";
import { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge, Card, Empty, ErrorState, Loading, PageHeader, Score } from "../components/ui";
import { api, useAsync, useAuth, useLang, usePageTitle } from "../core";
import { Bars, Weeks, type Report } from "./Reports";

type Dash = {
  open_jobs: number; published_jobs: number; new_applicants: number; to_review: number; upcoming_interviews: number; to_mark: number;
  offers_out: number; english_to_review: number; interviews_today: number; feedback_due: number; approvals_waiting: number; cv_waiting: number;
  recent: { id: number; full_name: string; job_title: string; created_at: string; score: number | null; status: string }[];
  interviews: { id: number; starts_at: string; applicant: string; round: string }[];
};

/** One piece of today's work: a count that links to the list to act on. Muted when there is nothing to do. */
function Todo({ icon, label, value, to, urgent = false }: { icon: ReactNode; label: string; value: number; to: string; urgent?: boolean }) {
  const on = value > 0;
  return (
    <Link to={to} className={`group flex items-center gap-3 rounded-2xl border p-3.5 no-underline transition [&_*]:no-underline ${on
      ? urgent ? "border-camel-300 bg-camel-50 hover:border-camel-500" : "border-primary-200 bg-white hover:border-primary-400 hover:shadow-sm"
      : "border-aeblack-100 bg-whitely-50 text-aeblack-500 hover:border-aeblack-200"}`}>
      <span className={`flex h-10 w-10 flex-none items-center justify-center rounded-xl ${on ? urgent ? "bg-camel-100 text-camel-900" : "bg-primary-50 text-primary-700" : "bg-aeblack-50 text-aeblack-400"}`}>{icon}</span>
      <span className="min-w-0 flex-1">
        <span className={`block text-xl font-bold tabular-nums ${on ? "text-aeblack-900" : "text-aeblack-500"}`}>{value}</span>
        <span className={`block text-sm leading-5 ${on ? "text-aeblack-700" : "text-aeblack-500"}`}>{label}</span>
      </span>
      {on && <ArrowRight size={18} className="shrink-0 text-aeblack-400 transition group-hover:translate-x-0.5 group-hover:text-primary-700 rtl:-scale-x-100 rtl:group-hover:-translate-x-0.5" aria-hidden="true" />}
    </Link>
  );
}

function Stat({ icon, label, value, to }: { icon: ReactNode; label: string; value: number; to: string }) {
  return (
    <Link to={to} className="mc-surface flex flex-col gap-2 rounded-2xl border border-aeblack-100 bg-white p-4 text-aeblack-900 no-underline transition hover:border-primary-300 hover:shadow-sm sm:flex-row sm:items-center sm:gap-4 sm:p-5 [&_*]:no-underline">
      <span className="flex h-10 w-10 flex-none items-center justify-center rounded-xl bg-primary-50 text-primary-700 sm:h-12 sm:w-12">{icon}</span>
      <span className="min-w-0">
        <span className="block text-2xl font-bold tabular-nums text-aeblack-900">{value}</span>
        <span className="block text-sm leading-5 text-aeblack-600">{label}</span>
      </span>
    </Link>
  );
}

export default function Dashboard() {
  const { t, to, date, lang } = useLang();
  const { me } = useAuth();
  usePageTitle(t("dash.title"));
  const { data, error, loading, reload } = useAsync(() => api<Dash>("/dashboard"), []);
  // The last eight weeks, from the hiring report: applications per week and how far they got.
  const from = new Date(Date.now() - 55 * 86400000).toISOString().slice(0, 10);
  const report = useAsync(() => api<Report>(`/reports/hiring?from=${from}`).catch(() => null), []);
  const suggestions = useAsync(() => api<{ key: string; text_ar: string; text_en: string; ask_ar: string; ask_en: string }[]>("/agent/suggestions").catch(() => []), []);
  if (loading) return <Loading />;
  if (error || !data) return <ErrorState error={error} retry={reload} />;
  const i = (C: typeof Briefcase) => <C size={24} aria-hidden="true" />;
  return (
    <>
      <PageHeader title={t("dash.title")} subtitle={me?.full_name} />
      <section aria-labelledby="dash-today" className="mb-6">
        <h2 id="dash-today" className="mb-3 text-base font-bold text-aeblack-900">{t("dash.today")}</h2>
        {[data.interviews_today, data.to_mark, data.feedback_due, data.approvals_waiting, data.offers_out, data.cv_waiting, data.english_to_review].every((n) => n === 0) ? (
          <p className="flex items-center gap-2 rounded-2xl border border-aegreen-200 bg-aegreen-50 p-4 text-sm font-semibold text-aegreen-800">
            <CheckCircle size={22} aria-hidden="true" />{t("dash.all_clear")}
          </p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Todo icon={i(CalendarCheck)} label={t("dash.interviews_today")} value={data.interviews_today} to={to("/interviews")} />
            <Todo icon={i(Exam)} label={t("dash.to_mark")} value={data.to_mark} to={to("/invitations?status=Submitted")} urgent />
            <Todo icon={i(ChatCircleText)} label={t("dash.feedback_due")} value={data.feedback_due} to={to("/interviews?scope=mine")} urgent />
            {me?.can_administer && <Todo icon={i(Stamp)} label={t("dash.approvals_waiting")} value={data.approvals_waiting} to={to("/approvals")} urgent />}
            <Todo icon={i(Handshake)} label={t("dash.offers_out")} value={data.offers_out} to={to("/offers?status=Sent")} />
            <Todo icon={i(HourglassMedium)} label={t("dash.cv_waiting")} value={data.cv_waiting} to={to("/jobs")} />
            <Todo icon={i(Translate)} label={t("dash.english_to_review")} value={data.english_to_review} to={to("/jobs?english=Suggested")} />
          </div>
        )}
      </section>

      <section aria-labelledby="dash-overview">
        <h2 id="dash-overview" className="mb-3 text-base font-bold text-aeblack-900">{t("dash.overview")}</h2>
        <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
          <Stat icon={i(Briefcase)} label={t("dash.open_jobs")} value={data.open_jobs} to={to("/jobs?status=Open")} />
          <Stat icon={i(Globe)} label={t("dash.published_jobs")} value={data.published_jobs} to={to("/jobs?status=Open")} />
          <Stat icon={i(UserPlus)} label={t("dash.new_applicants")} value={data.new_applicants} to={to("/applicants")} />
          <Stat icon={i(Users)} label={t("dash.to_review")} value={data.to_review} to={to("/applicants?status=Open")} />
        </div>
      </section>

      {report.data && (
        <div className="mt-6 grid gap-6 lg:grid-cols-2">
          <Card title={t("dash.per_week")} actions={<Link to={to("/reports")} className="text-sm font-semibold text-primary-700 hover:underline">{t("dash.open_report")}</Link>}>
            <Weeks weeks={report.data.weeks} />
          </Card>
          <Card title={t("dash.funnel")}>
            <Bars rows={report.data.funnel.map((f) => ({ label: t(`rep.stage.${f.stage}`), value: f.count,
              note: f.stage === "applied" || !(report.data?.funnel[0]?.count || 0) ? undefined : `${Math.round((f.count / (report.data?.funnel[0]?.count || 0)) * 100)}%` }))} />
          </Card>
        </div>
      )}

      {(suggestions.data?.length ?? 0) > 0 && (
        <Card title={<span className="flex items-center gap-2"><Lightbulb size={24} className="text-primary-600" aria-hidden="true" />{t("dash.suggested")}</span>} className="mt-6">
          <ul className="grid gap-2 md:grid-cols-2">
            {suggestions.data!.map((x) => (
              <li key={x.key}>
                <Link to={to(`/assistant?q=${encodeURIComponent(lang === "en" ? x.ask_en : x.ask_ar)}`)}
                  className="flex items-center justify-between gap-3 rounded-xl border border-aeblack-100 p-3 text-aeblack-900 no-underline hover:border-primary-300 hover:bg-primary-50 [&_*]:no-underline">
                  <span>{lang === "en" ? x.text_en : x.text_ar}</span>
                  <span className="flex-none text-sm font-semibold text-primary-700">{t("dash.ask_assistant")}</span>
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      )}
      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Card title={t("dash.recent")} className="xl:col-span-2">
          {data.recent.length === 0 ? <Empty /> : (
            <ul className="divide-y divide-aeblack-100">
              {data.recent.map((a) => (
                <li key={a.id} className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 py-3">
                  <div className="min-w-0">
                    <Link to={to(`/applicants/${a.id}`)} className="font-semibold text-primary-700 hover:underline">{a.full_name}</Link>
                    <p className="truncate text-sm text-aeblack-500"><bdi>{a.job_title}</bdi> · <bdi>{date(a.created_at)}</bdi></p>
                  </div>
                  <div className="flex items-center gap-3">
                    <Badge tone={a.status === "Open" ? "blue" : "gray"}>{t(`app.status.${a.status}`)}</Badge>
                    <Score value={a.score} />
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title={t("dash.next_interviews")} className="xl:self-start">
          {data.interviews.length === 0 ? <Empty /> : (
            <ul className="space-y-3">
              {data.interviews.map((iv) => (
                <li key={iv.id}>
                  <Link to={to(`/interviews/${iv.id}`)} className="block rounded-xl border border-aeblack-100 p-3 no-underline hover:border-primary-300 [&_*]:no-underline">
                    <p className="font-semibold text-aeblack-900">{iv.applicant}</p>
                    <p className="text-sm text-aeblack-500"><bdi>{iv.round}</bdi> · <bdi>{date(iv.starts_at, true)}</bdi></p>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </>
  );
}
