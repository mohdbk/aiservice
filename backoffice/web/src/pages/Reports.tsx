import { useSearchParams } from "react-router-dom";
import { Card, Empty, ErrorState, Loading, Note, PageHeader, SelectInput, Stat, TextInput } from "../components/ui";
import { api, useAsync, useLang, usePageTitle } from "../core";

type Report = {
  from: string; to: string; truncated: boolean;
  funnel: { stage: string; count: number }[];
  time: { to_interview_days: number | null; to_offer_days: number | null; to_hire_days: number | null; hires: number };
  tests: { assessment_id: number; title: string; invited: number; completed: number; passed: number; failed: number; expired: number }[];
  drop_off: Record<string, number>;
  departments: { department_id: number | null; name: string; applied: number; interviewed: number; offered: number; hired: number }[];
  weeks: { week: string; applied: number }[];
  fairness: {
    given: number; emirati_share_applied: number; emirati_share_hired: number | null;
    groups: { group: string; applied: number; interviewed: number; offered: number; hired: number; interview_rate: number; offer_rate: number; hire_rate: number;
      interview_ratio: number | null; offer_ratio: number | null; flagged: boolean }[];
  };
};

const pct = (a: number, b: number) => (b > 0 ? Math.round((a / b) * 100) : 0);

/** One horizontal bar per row (single series, so one hue); the value is written next to it, not only drawn. */
function Bars({ rows }: { rows: { label: string; value: number; note?: string }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.value));
  return (
    <ul className="space-y-3">
      {rows.map((r) => (
        <li key={r.label} className="grid grid-cols-[minmax(7rem,11rem)_1fr_auto] items-center gap-3 text-sm">
          <span className="truncate text-aeblack-700">{r.label}</span>
          <span className="h-5 rounded-e bg-aeblack-50" title={`${r.label}: ${r.value}`}>
            <span className="block h-full rounded-e bg-primary-500" style={{ width: `${Math.max(r.value > 0 ? 1.5 : 0, (r.value / max) * 100)}%` }} />
          </span>
          <span className="min-w-14 text-end tabular-nums text-aeblack-900" dir="ltr">{r.value}{r.note && <span className="ms-1 text-xs text-aeblack-500">{r.note}</span>}</span>
        </li>
      ))}
    </ul>
  );
}

/** Applications per week as columns, with the count on hover and the busiest week labelled. */
function Weeks({ weeks }: { weeks: Report["weeks"] }) {
  const { t, date } = useLang();
  const max = Math.max(1, ...weeks.map((w) => w.applied));
  const peak = weeks.reduce((p, w) => (w.applied > p.applied ? w : p), weeks[0]);
  const label = t("rep.weeks_label", { n: weeks.length, total: weeks.reduce((s, w) => s + w.applied, 0), peak: peak ? date(peak.week) : "-", max: peak?.applied ?? 0 });
  return (
    <div>
      <div className="flex h-40 items-end gap-0.5 border-b border-aeblack-200" dir="ltr" role="img" aria-label={label}>
        {weeks.map((w) => (
          <div key={w.week} className="group relative flex h-full flex-1 items-end" title={`${date(w.week)}: ${w.applied}`}>
            <div className="w-full rounded-t bg-primary-500 group-hover:bg-primary-700" style={{ height: `${(w.applied / max) * 100}%`, minHeight: w.applied ? 2 : 0 }} />
            {w === peak && w.applied > 0 && <span className="absolute -top-5 left-1/2 -translate-x-1/2 text-xs tabular-nums text-aeblack-700">{w.applied}</span>}
          </div>
        ))}
      </div>
      <div className="mt-1 flex justify-between text-xs text-aeblack-500" dir="ltr">
        <span>{weeks[0] && date(weeks[0].week)}</span><span>{weeks.length > 1 && date(weeks[weeks.length - 1].week)}</span>
      </div>
    </div>
  );
}

export default function Reports() {
  const { t } = useLang();
  usePageTitle(t("nav.reports"));
  const [params, setParams] = useSearchParams();
  const get = (k: string) => params.get(k) || "";
  const set = (k: string, v: string) => { const p = new URLSearchParams(params); if (v) p.set(k, v); else p.delete(k); setParams(p, { replace: true }); };
  const qs = new URLSearchParams([...params.entries()].filter(([k]) => ["from", "to", "job", "department"].includes(k))).toString();
  const report = useAsync(() => api<Report>(`/reports/hiring${qs ? `?${qs}` : ""}`), [qs]);
  const jobs = useAsync(() => api<{ items: { id: number; title: string }[] }>("/jobs?size=100&sort=created&dir=desc"), []);
  const lookups = useAsync(() => api<{ departments: { id: number; name: string }[] }>("/lookups"), []);
  const r = report.data;
  const applied = r?.funnel[0]?.count || 0;
  const days = (v: number | null) => (v === null ? "-" : t("rep.days", { n: v }));

  return (
    <>
      <PageHeader title={t("nav.reports")} subtitle={t("rep.subtitle")} actions={
        <a className="aegov-btn btn-outline btn-sm" href={`/api/backoffice/reports/hires.csv?${new URLSearchParams([["from", get("from") || r?.from || ""], ["to", get("to") || r?.to || ""]]).toString()}`}>
          {t("rep.export_hires")}
        </a>} />
      <div className="mb-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <TextInput label={t("rep.from")} type="date" value={get("from") || r?.from || ""} onChange={(e) => set("from", e.target.value)} />
        <TextInput label={t("rep.to")} type="date" value={get("to") || r?.to || ""} onChange={(e) => set("to", e.target.value)} />
        <SelectInput label={t("apps.job")} value={get("job")} onChange={(e) => set("job", e.target.value)}
          options={[{ value: "", label: t("common.all") }, ...(jobs.data?.items || []).map((j) => ({ value: j.id, label: j.title }))]} />
        <SelectInput label={t("jobs.department")} value={get("department")} onChange={(e) => set("department", e.target.value)}
          options={[{ value: "", label: t("common.all") }, ...(lookups.data?.departments || []).map((d) => ({ value: d.id, label: d.name }))]} />
      </div>
      {report.loading && !r ? <Loading /> : report.error ? <ErrorState error={report.error} retry={report.reload} /> : r && (
        <div className="space-y-6">
          {r.truncated && <Note tone="warning">{t("rep.truncated")}</Note>}
          <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
            <Stat label={t("rep.applied")} value={applied} />
            <Stat label={t("rep.hired")} value={r.funnel.find((f) => f.stage === "hired")?.count ?? 0} hint={t("rep.of_applied", { n: pct(r.funnel.find((f) => f.stage === "hired")?.count ?? 0, applied) })} />
            <Stat label={t("rep.time_to_interview")} value={days(r.time.to_interview_days)} hint={t("rep.median")} />
            <Stat label={t("rep.time_to_hire")} value={days(r.time.to_hire_days)} hint={t("rep.median")} />
          </div>
          {applied === 0 ? <Card><Empty /></Card> : (
            <>
              <div className="grid gap-6 xl:grid-cols-2">
                <Card title={t("rep.funnel")}>
                  <Bars rows={r.funnel.map((f) => ({ label: t(`rep.stage.${f.stage}`), value: f.count, note: f.stage === "applied" ? undefined : `${pct(f.count, applied)}%` }))} />
                </Card>
                <Card title={t("rep.weekly")}><Weeks weeks={r.weeks} /></Card>
              </div>
              <div className="grid gap-6 xl:grid-cols-2">
                <Card title={t("rep.drop_off")}>
                  <Bars rows={Object.entries(r.drop_off).map(([k, v]) => ({ label: t(`rep.drop.${k}`), value: v }))} />
                </Card>
                <Card title={t("rep.time")}>
                  <dl className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                    {([["to_interview_days", "rep.time_to_interview"], ["to_offer_days", "rep.time_to_offer"], ["to_hire_days", "rep.time_to_hire"]] as const).map(([k, l]) => (
                      <div key={k} className="rounded-xl border border-aeblack-100 p-3">
                        <dt className="text-xs text-aeblack-500">{t(l)}</dt>
                        <dd className="mt-1 text-xl font-bold tabular-nums">{days(r.time[k])}</dd>
                      </div>
                    ))}
                  </dl>
                  <p className="mt-3 text-xs text-aeblack-500">{t("rep.time_hint")}</p>
                </Card>
              </div>
              <Card title={t("rep.tests")}>
                {r.tests.length === 0 ? <Empty /> : (
                  <div className="overflow-x-auto">
                    <table className="w-full min-w-[36rem] text-sm">
                      <thead><tr className="border-b border-aeblack-100 text-start text-aeblack-500">
                        {["rep.test", "rep.invited", "rep.completed", "rep.pass_rate", "rep.expired"].map((k) => <th key={k} className="py-2 text-start font-semibold">{t(k)}</th>)}
                      </tr></thead>
                      <tbody>
                        {r.tests.map((x) => (
                          <tr key={x.assessment_id} className="border-b border-aeblack-50">
                            <td className="py-2">{x.title}</td>
                            <td className="tabular-nums">{x.invited}</td>
                            <td className="tabular-nums">{x.completed} <span className="text-xs text-aeblack-500" dir="ltr">({pct(x.completed, x.invited)}%)</span></td>
                            <td className="tabular-nums" dir="ltr">{x.passed + x.failed > 0 ? `${pct(x.passed, x.passed + x.failed)}%` : "-"}</td>
                            <td className="tabular-nums">{x.expired}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </Card>
              <Card title={t("rep.departments")}>
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[32rem] text-sm">
                    <thead><tr className="border-b border-aeblack-100 text-aeblack-500">
                      {["jobs.department", "rep.stage.applied", "rep.stage.interviewed", "rep.stage.offered", "rep.stage.hired"].map((k) => <th key={k} className="py-2 text-start font-semibold">{t(k)}</th>)}
                    </tr></thead>
                    <tbody>
                      {r.departments.map((d) => (
                        <tr key={d.department_id ?? 0} className="border-b border-aeblack-50">
                          <td className="py-2">{d.name || t("rep.no_department")}</td>
                          <td className="tabular-nums">{d.applied}</td><td className="tabular-nums">{d.interviewed}</td>
                          <td className="tabular-nums">{d.offered}</td><td className="tabular-nums">{d.hired}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Card>
              {r.fairness && <Fairness f={r.fairness} total={r.funnel[0]?.count ?? 0} />}
            </>
          )}
        </div>
      )}
    </>
  );
}

/** Emiratisation and fairness: each nationality group's progress through the stages, and how it compares with the best group. */
function Fairness({ f, total }: { f: Report["fairness"]; total: number }) {
  const { t } = useLang();
  const ratio = (v: number | null) => (v == null ? "-" : v.toFixed(2));
  return (
    <Card title={t("rep.fairness")}>
      <div className="mb-4"><Note>{t("rep.fairness_hint")}</Note></div>
      <div className="mb-4 grid gap-4 sm:grid-cols-3">
        <Stat label={t("rep.emirati_applied")} value={`${f.emirati_share_applied}%`} />
        <Stat label={t("rep.emirati_hired")} value={f.emirati_share_hired == null ? "-" : `${f.emirati_share_hired}%`} />
        <Stat label={t("rep.nationality_given")} value={`${f.given} / ${total}`} />
      </div>
      {f.groups.some((g) => g.flagged) && <div className="mb-4"><Note tone="warning">{t("rep.fairness_flag")}</Note></div>}
      <div className="overflow-x-auto">
        <table className="w-full min-w-[40rem] text-sm">
          <thead><tr className="border-b border-aeblack-100 text-aeblack-500">
            {["rep.group", "rep.stage.applied", "rep.stage.interviewed", "rep.stage.offered", "rep.stage.hired", "rep.interview_ratio", "rep.offer_ratio"].map((k) => <th key={k} className="py-2 text-start font-semibold">{t(k)}</th>)}
          </tr></thead>
          <tbody>
            {f.groups.map((g) => (
              <tr key={g.group} className={`border-b border-aeblack-50 ${g.flagged ? "bg-aered-50" : ""}`}>
                <td className="py-2 font-semibold">{t(`rep.group.${g.group}`)}{g.flagged && <span className="ms-2 text-xs font-semibold text-aered-700">{t("rep.flagged")}</span>}</td>
                <td className="tabular-nums">{g.applied}</td>
                <td className="tabular-nums">{g.interviewed} <span className="text-aeblack-500">({g.interview_rate}%)</span></td>
                <td className="tabular-nums">{g.offered} <span className="text-aeblack-500">({g.offer_rate}%)</span></td>
                <td className="tabular-nums">{g.hired} <span className="text-aeblack-500">({g.hire_rate}%)</span></td>
                <td className="tabular-nums" dir="ltr">{ratio(g.interview_ratio)}</td>
                <td className="tabular-nums" dir="ltr">{ratio(g.offer_ratio)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
