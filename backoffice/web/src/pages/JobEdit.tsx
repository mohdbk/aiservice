import { ArrowDown, ArrowUp, FloppyDisk, Trash, Users } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  Bilingual, Button, Card, EnglishBadge, ErrorState, LangTabs, Loading, Note, PageHeader, RichText, SectionTabs, SelectInput, TextInput, Toggle, Tick, useSaving,
} from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
import { DEFAULT_WEIGHTS, JobRanking, SelectionSettings, type Weights } from "../components/Selection";

export type Lookups = {
  departments: { id: number; name: string; name_en: string }[];
  rounds: { id: number; name: string }[];
  staff: { id: number; name: string; email: string }[];
  assessments: { id: number; title: string }[];
  screening_questions: { id: number; text: string; answer_type: string; options: { id: number; text: string }[] }[];
  categories: string[];
  job_families: string[];
};

type Screening = { question_id: number; required: boolean; rule: string | null; expected: string | null };
type Stage = { assessment_id: number; send_when: string; deadline_days: number };
type Job = {
  id?: number; code?: string; title: string; description: string; department_id: number | null; location: string; employment_type: string;
  status: string; is_published: boolean; posted_on: string; closes_on: string; salary_min: number | null; salary_max: number | null;
  salary_currency: string; salary_period: string; show_salary: boolean; title_en: string; description_en: string; english: string;
  english_approved: boolean; screening: Screening[]; stages: Stage[]; criteria: { criterion: string; weight: number; is_must_have: boolean }[];
  criteria_current: boolean; row_version: string | null; role_profile_id: number | null; weights: Weights;
};

/** The emirates, stored by their Arabic name (shown in the reader's language on the portal). */
const EMIRATES = [
  ["أبوظبي", "Abu Dhabi"], ["دبي", "Dubai"], ["الشارقة", "Sharjah"], ["عجمان", "Ajman"],
  ["أم القيوين", "Umm Al Quwain"], ["رأس الخيمة", "Ras Al Khaimah"], ["الفجيرة", "Fujairah"],
] as const;

const EMPTY: Job = {
  title: "", description: "", department_id: null, location: "", employment_type: "FullTime", status: "Draft", is_published: false,
  posted_on: "", closes_on: "", salary_min: null, salary_max: null, salary_currency: "AED", salary_period: "Month", show_salary: false,
  title_en: "", description_en: "", english: "Missing", english_approved: false, screening: [], stages: [], criteria: [], criteria_current: false, row_version: null,
  role_profile_id: null, weights: DEFAULT_WEIGHTS,
};

export function useLookups() {
  return useAsync(() => api<Lookups>("/lookups"), []);
}

function move<T>(list: T[], i: number, d: number) {
  const j = i + d;
  if (j < 0 || j >= list.length) return list;
  const copy = [...list];
  [copy[i], copy[j]] = [copy[j], copy[i]];
  return copy;
}

export default function JobEdit() {
  const { id } = useParams();
  const { t, to, lang: panel } = useLang();
  const toast = useToast();
  const nav = useNavigate();
  const isNew = !id;
  const lookups = useLookups();
  const loaded = useAsync(async () => {
    if (isNew) return EMPTY;
    const j = await api<any>(`/jobs/${id}`);
    const en = (j.translations || []).find((x: any) => x.lang === "en");
    return { ...EMPTY, ...j, posted_on: j.posted_on || "", closes_on: j.closes_on || "", title_en: en?.title || "", description_en: en?.description || "",
      english: en?.review_status || "Missing", english_approved: !!en?.approved } as Job;
  }, [id]);
  const [job, setJob] = useState<Job>(EMPTY);
  const [lang, setLang] = useState<Bilingual>(panel === "en" ? "en" : "ar");
  const [tab, setTab] = useState("details");
  const { busy, error, run } = useSaving();
  const suggest = useSaving();
  usePageTitle(isNew ? t("jobs.new") : job.title || t("jobs.edit"));
  useEffect(() => { if (loaded.data) setJob(loaded.data); }, [loaded.data]);
  const set = <K extends keyof Job>(k: K, v: Job[K]) => setJob((j) => ({ ...j, [k]: v }));

  if (loaded.loading || lookups.loading) return <Loading />;
  if (loaded.error) return <ErrorState error={loaded.error} retry={loaded.reload} />;
  const lk = lookups.data;

  const save = async () => {
    const body = {
      title: job.title, description: job.description, department_id: job.department_id, location: job.location, employment_type: job.employment_type,
      status: job.status, is_published: job.is_published, posted_on: job.posted_on || null, closes_on: job.closes_on || null,
      salary_min: job.salary_min, salary_max: job.salary_max, salary_currency: job.salary_currency, salary_period: job.salary_period, show_salary: job.show_salary,
      translations: [{ lang: "en", title: job.title_en, description: job.description_en, approved: job.english_approved }],
      screening: job.screening, stages: job.stages, row_version: job.row_version,
      role_profile_id: job.role_profile_id ?? 0, weights: job.weights,
    };
    const r = await run(() => api<{ id: number }>(isNew ? "/jobs" : `/jobs/${id}`, { method: isNew ? "POST" : "PUT", body }));
    if (!r) return;
    toast(t("jobs.saved"));
    if (isNew) nav(to(`/jobs/${r.id}`), { replace: true });
    else loaded.reload();
  };
  const suggestEnglish = async () => {
    if (isNew) return;
    const r = await suggest.run(() => api(`/jobs/${id}/suggest-english`, { body: {} }));
    if (r) toast(t("common.queued"), "info");
  };
  const sqText = (qid: number) => lk?.screening_questions.find((q) => q.id === qid);

  return (
    <>
      <PageHeader
        back={<Link to={to("/jobs")} className="mb-2 inline-block text-sm text-primary-700">{t("jobs.title")}</Link>}
        title={isNew ? t("jobs.new") : job.title}
        subtitle={!isNew && <span className="flex flex-wrap items-center gap-2"><span dir="ltr">{job.code}</span><EnglishBadge state={job.english} /></span>}
        actions={<>
          {!isNew && <Link className="aegov-btn btn-outline" to={to(`/applicants?job=${id}`)}><Users size={20} aria-hidden="true" />{t("jobs.view_applicants")}</Link>}
          <Button busy={busy} onClick={save} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>
        </>}
      />
      {error && <div className="mb-4"><ErrorState error={error} /></div>}
      {suggest.error && <div className="mb-4"><ErrorState error={suggest.error} /></div>}
      <SectionTabs value={tab} onChange={setTab} tabs={[
        { value: "details", label: t("jobs.tab.details") }, { value: "screening", label: t("jobs.tab.screening") },
        { value: "tests", label: t("jobs.tab.tests") }, { value: "selection", label: t("jobs.tab.selection") },
        ...(isNew ? [] : [{ value: "criteria", label: t("jobs.tab.criteria") }, { value: "ranking", label: t("jobs.tab.ranking") }]),
      ]} />

      {tab === "details" && (
        <div className="grid gap-6 xl:grid-cols-3">
          <Card className="xl:col-span-2">
            <LangTabs value={lang} onChange={setLang} english={job.english} approved={job.english_approved}
              onApproved={(v) => set("english_approved", v)} onSuggest={isNew ? undefined : suggestEnglish} suggesting={suggest.busy}>
              {lang === "ar" ? (
                <div className="space-y-5" dir="rtl" lang="ar">
                  <TextInput label={t("jobs.title_ar")} value={job.title} maxLength={140} required onChange={(e) => set("title", e.target.value)} />
                  <RichText required label={t("jobs.description_ar")} hint={t("jobs.description_hint")} dir="rtl" value={job.description} onChange={(v) => set("description", v)} />
                </div>
              ) : (
                <div className="space-y-5" dir="ltr" lang="en">
                  <TextInput label={t("jobs.title_en")} value={job.title_en} maxLength={140} onChange={(e) => set("title_en", e.target.value)} />
                  <RichText label={t("jobs.description_en")} dir="ltr" value={job.description_en} onChange={(v) => set("description_en", v)} />
                </div>
              )}
            </LangTabs>
          </Card>
          <Card>
            <div className="space-y-4">
              <SelectInput required label={t("jobs.status")} value={job.status} onChange={(e) => set("status", e.target.value)}
                options={["Draft", "Open", "Closed"].map((s) => ({ value: s, label: t(`status.${s}`) }))} />
              <Toggle label={t("jobs.published")} checked={job.is_published} onChange={(v) => set("is_published", v)} />
              <SelectInput label={t("jobs.department")} value={job.department_id ?? ""} onChange={(e) => set("department_id", e.target.value ? Number(e.target.value) : null)}
                options={[{ value: "", label: t("common.none") }, ...(lk?.departments || []).map((d) => ({ value: d.id, label: d.name }))]} />
              {(() => {
                const known = EMIRATES.find(([ar, en]) => ar === job.location || en.toLowerCase() === job.location.trim().toLowerCase());
                const other = !known && job.location !== "";
                return (
                  <>
                    <SelectInput label={t("jobs.location")} value={known ? known[0] : other ? "__other" : ""}
                      onChange={(e) => set("location", e.target.value === "__other" ? " " : e.target.value)}
                      options={[{ value: "", label: "" }, ...EMIRATES.map(([ar, en]) => ({ value: ar, label: panel === "en" ? en : ar })), { value: "__other", label: t("jobs.location_other") }]} />
                    {other && <TextInput label={t("jobs.location_other")} hint={t("jobs.location_other_hint")} value={job.location.trim()} onChange={(e) => set("location", e.target.value || " ")} />}
                  </>
                );
              })()}
              <SelectInput required label={t("jobs.employment_type")} value={job.employment_type} onChange={(e) => set("employment_type", e.target.value)}
                options={["FullTime", "PartTime", "Contract", "Temporary", "Internship"].map((s) => ({ value: s, label: t(`emp.${s}`) }))} />
              <div className="grid grid-cols-2 gap-3">
                <TextInput label={t("jobs.posted_on")} type="date" value={job.posted_on} onChange={(e) => set("posted_on", e.target.value)} />
                <TextInput label={t("jobs.closes_on")} type="date" value={job.closes_on} onChange={(e) => set("closes_on", e.target.value)} />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <TextInput label={t("jobs.salary_min")} type="number" min={0} value={job.salary_min ?? ""} onChange={(e) => set("salary_min", e.target.value ? Number(e.target.value) : null)} />
                <TextInput label={t("jobs.salary_max")} type="number" min={0} value={job.salary_max ?? ""} onChange={(e) => set("salary_max", e.target.value ? Number(e.target.value) : null)} />
              </div>
              <SelectInput required label={t("jobs.salary_period")} value={job.salary_period} onChange={(e) => set("salary_period", e.target.value)}
                options={["Month", "Year"].map((s) => ({ value: s, label: t(`period.${s}`) }))} />
              <Toggle label={t("jobs.show_salary")} checked={job.show_salary} onChange={(v) => set("show_salary", v)} />
            </div>
          </Card>
        </div>
      )}

      {tab === "screening" && (
        <Card title={t("jobs.tab.screening")}>
          <div className="mb-4"><Note>{t("jobs.screening_hint")}</Note></div>
          <div className="space-y-3">
            {job.screening.map((s, i) => {
              const q = sqText(s.question_id);
              return (
                <div key={i} className="rounded-xl border border-aeblack-100 p-4">
                  <div className="mb-3 flex items-start justify-between gap-3">
                    <p className="font-semibold">{i + 1}. {q?.text || "?"} <span className="text-xs text-aeblack-500">({t(`at.${q?.answer_type}`)})</span></p>
                    <div className="flex gap-1">
                      <Button variant="link" size="sm" aria-label="Up" onClick={() => set("screening", move(job.screening, i, -1))}><ArrowUp size={20} /></Button>
                      <Button variant="link" size="sm" aria-label="Down" onClick={() => set("screening", move(job.screening, i, 1))}><ArrowDown size={20} /></Button>
                      <Button variant="link" size="sm" aria-label={t("common.remove")} onClick={() => set("screening", job.screening.filter((_, k) => k !== i))}><Trash size={20} /></Button>
                    </div>
                  </div>
                  <div className="grid gap-3 sm:grid-cols-3 sm:items-end">
                    <Tick label={t("jobs.required_answer")} checked={s.required} onChange={(v) => set("screening", job.screening.map((x, k) => (k === i ? { ...x, required: v } : x)))} />
                    <SelectInput label={t("jobs.requirement")} value={s.rule || ""}
                      onChange={(e) => set("screening", job.screening.map((x, k) => (k === i ? { ...x, rule: e.target.value || null } : x)))}
                      options={[{ value: "", label: t("rule.none") }, ...(q?.answer_type === "Number" ? ["AtLeast", "AtMost"] : ["MustBe"]).map((r) => ({ value: r, label: t(`rule.${r}`) }))]} />
                    {s.rule && (q?.answer_type === "SingleChoice" || q?.answer_type === "MultipleChoice" || q?.answer_type === "YesNo" ? (
                      <SelectInput label={t("jobs.expected")} value={s.expected || ""} onChange={(e) => set("screening", job.screening.map((x, k) => (k === i ? { ...x, expected: e.target.value } : x)))}
                        options={[{ value: "", label: "" }, ...(q.answer_type === "YesNo" ? [{ value: "Yes", label: t("common.yes") }, { value: "No", label: t("common.no") }] : q.options.map((o) => ({ value: String(o.id), label: o.text })))]} />
                    ) : (
                      <TextInput label={t("jobs.expected")} value={s.expected || ""} onChange={(e) => set("screening", job.screening.map((x, k) => (k === i ? { ...x, expected: e.target.value } : x)))} />
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
          <div className="mt-4 max-w-md">
            <SelectInput label={t("jobs.add_question")} value="" onChange={(e) => e.target.value && set("screening", [...job.screening, { question_id: Number(e.target.value), required: true, rule: null, expected: null }])}
              options={[{ value: "", label: "" }, ...(lk?.screening_questions || []).filter((q) => !job.screening.some((s) => s.question_id === q.id)).map((q) => ({ value: q.id, label: q.text }))]} />
          </div>
        </Card>
      )}

      {tab === "tests" && (
        <Card title={t("jobs.tab.tests")}>
          <div className="mb-4"><Note>{t("jobs.tests_hint")}</Note></div>
          <div className="space-y-3">
            {job.stages.map((s, i) => (
              <div key={i} className="grid gap-3 rounded-xl border border-aeblack-100 p-4 sm:grid-cols-[1fr_12rem_10rem_auto] sm:items-end">
                <p className="font-semibold">{lk?.assessments.find((a) => a.id === s.assessment_id)?.title || `#${s.assessment_id}`}</p>
                <SelectInput required label={t("jobs.send_when")} value={s.send_when} onChange={(e) => set("stages", job.stages.map((x, k) => (k === i ? { ...x, send_when: e.target.value } : x)))}
                  options={["OnApply", "Manual"].map((v) => ({ value: v, label: t(`send.${v}`) }))} />
                <TextInput required label={t("jobs.deadline_days")} type="number" min={1} max={90} value={s.deadline_days}
                  onChange={(e) => set("stages", job.stages.map((x, k) => (k === i ? { ...x, deadline_days: Number(e.target.value) || 7 } : x)))} />
                <Button variant="link" aria-label={t("common.remove")} onClick={() => set("stages", job.stages.filter((_, k) => k !== i))}><Trash size={20} /></Button>
              </div>
            ))}
          </div>
          <div className="mt-4 max-w-md">
            <SelectInput label={t("jobs.add_test")} value="" onChange={(e) => e.target.value && set("stages", [...job.stages, { assessment_id: Number(e.target.value), send_when: "Manual", deadline_days: 7 }])}
              options={[{ value: "", label: "" }, ...(lk?.assessments || []).filter((a) => !job.stages.some((s) => s.assessment_id === a.id)).map((a) => ({ value: a.id, label: a.title }))]} />
          </div>
        </Card>
      )}

      {tab === "selection" && (
        <SelectionSettings roleProfileId={job.role_profile_id} weights={job.weights}
          onChange={(rp, w) => setJob((j) => ({ ...j, role_profile_id: rp, weights: w }))} />
      )}

      {tab === "ranking" && id && <JobRanking jobId={id} />}

      {tab === "criteria" && (
        <Card title={t("jobs.tab.criteria")}>
          <div className="mb-4"><Note>{t("jobs.criteria_hint")}</Note></div>
          {job.criteria.length === 0 ? <p className="text-aeblack-500">{t("jobs.criteria_none")}</p> : (
            <>
              {!job.criteria_current && <div className="mb-4"><Note tone="warning">{t("jobs.criteria_stale")}</Note></div>}
              <ul className="space-y-2">
                {job.criteria.map((c, i) => (
                  <li key={i} className="flex items-center justify-between gap-3 rounded-xl border border-aeblack-100 p-3">
                    <span>{c.criterion} {c.is_must_have && <span className="ms-1 rounded-full bg-primary-100 px-2 text-xs font-semibold text-primary-800">{t("jobs.must")}</span>}</span>
                    <span className="text-sm tabular-nums text-aeblack-500">{t("jobs.weight")} {c.weight}</span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </Card>
      )}
      {tab !== "ranking" && <div className="mt-6 flex justify-end">
        <Button busy={busy} onClick={save} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>
      </div>}
    </>
  );
}
