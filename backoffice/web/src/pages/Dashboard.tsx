import { Briefcase, CalendarCheck, Exam, Globe, Handshake, Translate, UserPlus, Users } from "@phosphor-icons/react";
import { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge, Card, Empty, ErrorState, Loading, PageHeader, Score } from "../components/ui";
import { api, useAsync, useAuth, useLang, usePageTitle } from "../core";

type Dash = {
  open_jobs: number; published_jobs: number; new_applicants: number; to_review: number; upcoming_interviews: number; to_mark: number;
  offers_out: number; english_to_review: number;
  recent: { id: number; full_name: string; job_title: string; created_at: string; score: number | null; status: string }[];
  interviews: { id: number; starts_at: string; applicant: string; round: string }[];
};

function Stat({ icon, label, value, to }: { icon: ReactNode; label: string; value: number; to: string }) {
  return (
    <Link to={to} className="mc-surface flex items-center gap-4 rounded-2xl border border-aeblack-100 bg-white p-5 text-aeblack-900 no-underline transition hover:border-primary-300 hover:shadow-sm [&_*]:no-underline">
      <span className="flex h-12 w-12 flex-none items-center justify-center rounded-xl bg-primary-50 text-primary-700">{icon}</span>
      <span>
        <span className="block text-2xl font-bold tabular-nums text-aeblack-900">{value}</span>
        <span className="text-sm text-aeblack-600">{label}</span>
      </span>
    </Link>
  );
}

export default function Dashboard() {
  const { t, to, date } = useLang();
  const { me } = useAuth();
  usePageTitle(t("dash.title"));
  const { data, error, loading, reload } = useAsync(() => api<Dash>("/dashboard"), []);
  if (loading) return <Loading />;
  if (error || !data) return <ErrorState error={error} retry={reload} />;
  const i = (C: typeof Briefcase) => <C size={24} aria-hidden="true" />;
  return (
    <>
      <PageHeader title={t("dash.title")} subtitle={me?.full_name} />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat icon={i(Briefcase)} label={t("dash.open_jobs")} value={data.open_jobs} to={to("/jobs?status=Open")} />
        <Stat icon={i(Globe)} label={t("dash.published_jobs")} value={data.published_jobs} to={to("/jobs?status=Open")} />
        <Stat icon={i(UserPlus)} label={t("dash.new_applicants")} value={data.new_applicants} to={to("/applicants")} />
        <Stat icon={i(Users)} label={t("dash.to_review")} value={data.to_review} to={to("/applicants?status=Open")} />
        <Stat icon={i(CalendarCheck)} label={t("dash.upcoming_interviews")} value={data.upcoming_interviews} to={to("/interviews")} />
        <Stat icon={i(Exam)} label={t("dash.to_mark")} value={data.to_mark} to={to("/invitations?status=Submitted")} />
        <Stat icon={i(Handshake)} label={t("dash.offers_out")} value={data.offers_out} to={to("/offers?status=Sent")} />
        <Stat icon={i(Translate)} label={t("dash.english_to_review")} value={data.english_to_review} to={to("/jobs")} />
      </div>
      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Card title={t("dash.recent")} className="xl:col-span-2">
          {data.recent.length === 0 ? <Empty /> : (
            <ul className="divide-y divide-aeblack-100">
              {data.recent.map((a) => (
                <li key={a.id} className="flex items-center justify-between gap-3 py-3">
                  <div className="min-w-0">
                    <Link to={to(`/applicants/${a.id}`)} className="font-semibold text-primary-700 hover:underline">{a.full_name}</Link>
                    <p className="truncate text-sm text-aeblack-500">{a.job_title} · {date(a.created_at)}</p>
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
        <Card title={t("dash.next_interviews")}>
          {data.interviews.length === 0 ? <Empty /> : (
            <ul className="space-y-3">
              {data.interviews.map((iv) => (
                <li key={iv.id}>
                  <Link to={to(`/interviews/${iv.id}`)} className="block rounded-xl border border-aeblack-100 p-3 hover:border-primary-300">
                    <p className="font-semibold text-aeblack-900">{iv.applicant}</p>
                    <p className="text-sm text-aeblack-500">{iv.round} · {date(iv.starts_at, true)}</p>
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
