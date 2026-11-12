import { FloppyDisk, Plus, Shuffle, Trash } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  Bilingual, Button, Card, EnglishBadge, ErrorState, LangTabs, Loading, PageHeader, RichText, SelectInput, TextArea, TextInput, Toggle, useSaving,
} from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
import { useLookups } from "./JobEdit";
import { plain } from "./Questions";

type Item = { question_id: number | null; question?: string; draw_category: string; draw_difficulty: string; draw_job_family: string; draw_count: number; points_override: number | null };
type Sec = { id?: number; title: string; title_en: string; instructions: string; instructions_en: string; minutes: number; pass_mark: number; items: Item[] };
type Test = {
  title: string; title_en: string; instructions: string; instructions_en: string; english_status: string; english_approved: boolean; assessment_type: string;
  status: string; pass_mark: number; max_attempts: number; score_policy: string; shuffle_questions: boolean; shuffle_options: boolean;
  show_score_to_candidate: boolean; default_deadline_days: number; question_count?: number; sections: Sec[];
};

const SECTION: Sec = { title: "", title_en: "", instructions: "", instructions_en: "", minutes: 20, pass_mark: 0, items: [] };
const EMPTY: Test = {
  title: "", title_en: "", instructions: "", instructions_en: "", english_status: "Missing", english_approved: false, assessment_type: "KnowledgeTest",
  status: "Draft", pass_mark: 60, max_attempts: 1, score_policy: "Highest", shuffle_questions: true, shuffle_options: true, show_score_to_candidate: false,
  default_deadline_days: 7, sections: [{ ...SECTION }],
};

export default function TestEdit() {
  const { id } = useParams();
  const isNew = !id;
  const { t, to } = useLang();
  const toast = useToast();
  const nav = useNavigate();
  const lk = useLookups();
  const questions = useAsync(() => api<{ items: { id: number; text: string; category: string }[] }>("/questions?size=100&active=true&sort=text&dir=asc"), []);
  const loaded = useAsync(() => (isNew ? Promise.resolve(EMPTY) : api<Test>(`/assessments/${id}`)), [id]);
  const [x, setX] = useState<Test>(EMPTY);
  const [lang, setLang] = useState<Bilingual>("ar");
  const { busy, error, run } = useSaving();
  const suggest = useSaving();
  usePageTitle(isNew ? t("test.new") : x.title || t("test.title"));
  useEffect(() => { if (loaded.data) setX(loaded.data); }, [loaded.data]);
  if (loaded.loading) return <Loading />;
  if (loaded.error) return <ErrorState error={loaded.error} retry={loaded.reload} />;
  const set = <K extends keyof Test>(k: K, v: Test[K]) => setX((p) => ({ ...p, [k]: v }));
  const setSec = (i: number, patch: Partial<Sec>) => set("sections", x.sections.map((s, k) => (k === i ? { ...s, ...patch } : s)));
  const setItem = (si: number, ii: number, patch: Partial<Item>) => setSec(si, { items: x.sections[si].items.map((it, k) => (k === ii ? { ...it, ...patch } : it)) });

  const save = async () => {
    const r = await run(() => api<{ id: number }>(isNew ? "/assessments" : `/assessments/${id}`, { method: isNew ? "POST" : "PUT", body: x }));
    if (!r) return;
    toast(t("common.saved"));
    if (isNew) nav(to(`/tests/${r.id}`), { replace: true });
    else loaded.reload();
  };
  const suggestEnglish = async () => {
    if (await suggest.run(() => api(`/suggest-english/assessment/${id}`, { body: {} })) !== undefined) toast(t("common.queued"), "info");
  };
  const ar = lang === "ar";

  return (
    <>
      <PageHeader back={<Link to={to("/tests")} className="mb-2 inline-block text-sm text-primary-700">{t("test.title")}</Link>}
        title={isNew ? t("test.new") : x.title}
        subtitle={!isNew && <span className="flex flex-wrap items-center gap-2"><EnglishBadge state={x.english_status} />
          {x.question_count !== undefined && <span className="text-sm">{t("test.question_count", { count: x.question_count })}</span>}</span>}
        actions={<Button busy={busy} onClick={save} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>} />
      {(error || suggest.error) && <div className="mb-4"><ErrorState error={error || suggest.error || undefined} /></div>}
      <div className="grid gap-6 xl:grid-cols-3">
        <div className="space-y-6 xl:col-span-2">
          <Card>
            <LangTabs value={lang} onChange={setLang} english={x.english_status} approved={x.english_approved} onApproved={(v) => set("english_approved", v)}
              onSuggest={isNew ? undefined : suggestEnglish} suggesting={suggest.busy}>
              <div className="space-y-5" dir={ar ? "rtl" : "ltr"} lang={lang}>
                <TextInput label={ar ? t("test.name_ar") : t("test.name_en")} value={ar ? x.title : x.title_en} onChange={(e) => set(ar ? "title" : "title_en", e.target.value)} />
                <RichText label={ar ? t("test.instructions_ar") : t("test.instructions_en")} dir={ar ? "rtl" : "ltr"}
                  value={ar ? x.instructions : x.instructions_en} onChange={(v) => set(ar ? "instructions" : "instructions_en", v)} />
                {x.sections.map((s, si) => (
                  <fieldset key={si} className="rounded-xl border border-aeblack-100 p-4">
                    <legend className="px-2 text-sm font-bold text-primary-800">{t("test.section", { n: si + 1 })}</legend>
                    <div className="space-y-3">
                      <TextInput label={ar ? t("test.name_ar") : t("test.name_en")} value={ar ? s.title : s.title_en} onChange={(e) => setSec(si, ar ? { title: e.target.value } : { title_en: e.target.value })} />
                      <TextArea label={ar ? t("test.instructions_ar") : t("test.instructions_en")} rows={2} value={ar ? s.instructions : s.instructions_en}
                        onChange={(e) => setSec(si, ar ? { instructions: e.target.value } : { instructions_en: e.target.value })} />
                    </div>
                  </fieldset>
                ))}
              </div>
            </LangTabs>
          </Card>

          <Card title={t("test.sections")} actions={<Button variant="outline" size="sm" onClick={() => set("sections", [...x.sections, { ...SECTION }])} icon={<Plus size={20} aria-hidden="true" />}>{t("test.add_section")}</Button>}>
            <div className="space-y-6">
              {x.sections.map((s, si) => (
                <div key={si} className="rounded-xl border border-aeblack-100 p-4">
                  <div className="mb-3 flex flex-wrap items-end gap-3">
                    <p className="flex-1 font-bold">{t("test.section", { n: si + 1 })}{s.title && `: ${s.title}`}</p>
                    {x.sections.length > 1 && <Button variant="link" aria-label={t("common.remove")} onClick={() => set("sections", x.sections.filter((_, k) => k !== si))}><Trash size={20} /></Button>}
                  </div>
                  <div className="mb-4 grid gap-3 sm:grid-cols-2">
                    <TextInput label={t("test.minutes")} type="number" min={0} max={600} value={s.minutes} onChange={(e) => setSec(si, { minutes: Number(e.target.value) || 0 })} />
                    <TextInput label={t("test.section_pass")} type="number" min={0} max={100} value={s.pass_mark} onChange={(e) => setSec(si, { pass_mark: Number(e.target.value) || 0 })} />
                  </div>
                  <p className="mb-2 text-sm font-semibold">{t("test.items")}</p>
                  <ul className="space-y-2">
                    {s.items.map((it, ii) => (
                      <li key={ii} className="grid gap-2 rounded-lg bg-whitely-100 p-3 sm:grid-cols-[1fr_6rem_auto] sm:items-end">
                        {it.question_id ? (
                          <SelectInput label={t("q.pick")} value={it.question_id} onChange={(e) => setItem(si, ii, { question_id: Number(e.target.value) })}
                            options={(questions.data?.items || []).map((q) => ({ value: q.id, label: plain(q.text).slice(0, 120) }))} />
                        ) : (
                          <div className="grid gap-2 sm:grid-cols-4">
                            <SelectInput label={t("q.category")} value={it.draw_category} onChange={(e) => setItem(si, ii, { draw_category: e.target.value })}
                              options={[{ value: "", label: t("test.any") }, ...(lk.data?.categories || []).map((c) => ({ value: c, label: c }))]} />
                            <SelectInput label={t("q.difficulty")} value={it.draw_difficulty} onChange={(e) => setItem(si, ii, { draw_difficulty: e.target.value })}
                              options={[{ value: "", label: t("test.any") }, ...["Easy", "Medium", "Hard"].map((d) => ({ value: d, label: t(`diff.${d}`) }))]} />
                            <SelectInput label={t("q.job_family")} value={it.draw_job_family} onChange={(e) => setItem(si, ii, { draw_job_family: e.target.value })}
                              options={[{ value: "", label: t("test.any") }, ...(lk.data?.job_families || []).map((c) => ({ value: c, label: c }))]} />
                            <TextInput label={t("test.draw_count")} type="number" min={1} max={200} value={it.draw_count} onChange={(e) => setItem(si, ii, { draw_count: Number(e.target.value) || 1 })} />
                          </div>
                        )}
                        <TextInput label={t("test.points_override")} type="number" min={0} step={0.5} value={it.points_override ?? ""}
                          onChange={(e) => setItem(si, ii, { points_override: e.target.value ? Number(e.target.value) : null })} />
                        <Button variant="link" aria-label={t("common.remove")} onClick={() => setSec(si, { items: s.items.filter((_, k) => k !== ii) })}><Trash size={20} /></Button>
                      </li>
                    ))}
                  </ul>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Button variant="outline" size="sm" disabled={!questions.data?.items.length}
                      onClick={() => setSec(si, { items: [...s.items, { question_id: questions.data!.items[0].id, draw_category: "", draw_difficulty: "", draw_job_family: "", draw_count: 0, points_override: null }] })}
                      icon={<Plus size={20} aria-hidden="true" />}>{t("test.add_fixed")}</Button>
                    <Button variant="outline" size="sm" onClick={() => setSec(si, { items: [...s.items, { question_id: null, draw_category: "", draw_difficulty: "", draw_job_family: "", draw_count: 5, points_override: null }] })}
                      icon={<Shuffle size={20} aria-hidden="true" />}>{t("test.add_draw")}</Button>
                  </div>
                </div>
              ))}
            </div>
          </Card>
        </div>
        <Card>
          <div className="space-y-4">
            <SelectInput label={t("common.status")} value={x.status} onChange={(e) => set("status", e.target.value)}
              options={["Draft", "Published", "Archived"].map((v) => ({ value: v, label: t(`status.${v}`) }))} />
            <SelectInput label={t("test.type")} value={x.assessment_type} onChange={(e) => set("assessment_type", e.target.value)}
              options={["KnowledgeTest", "LanguageCheck", "SituationalJudgement", "WorkSample", "Aptitude", "Other"].map((v) => ({ value: v, label: t(`type.${v}`) }))} />
            <TextInput label={t("test.pass_mark")} type="number" min={0} max={100} value={x.pass_mark} onChange={(e) => set("pass_mark", Number(e.target.value) || 0)} />
            <TextInput label={t("test.max_attempts")} type="number" min={1} max={10} value={x.max_attempts} onChange={(e) => set("max_attempts", Number(e.target.value) || 1)} />
            <SelectInput label={t("test.score_policy")} value={x.score_policy} onChange={(e) => set("score_policy", e.target.value)}
              options={["Highest", "Latest"].map((v) => ({ value: v, label: t(`policy.${v}`) }))} />
            <TextInput label={t("test.deadline_days")} type="number" min={1} max={90} value={x.default_deadline_days} onChange={(e) => set("default_deadline_days", Number(e.target.value) || 7)} />
            <Toggle label={t("test.shuffle_questions")} checked={x.shuffle_questions} onChange={(v) => set("shuffle_questions", v)} />
            <Toggle label={t("test.shuffle_options")} checked={x.shuffle_options} onChange={(v) => set("shuffle_options", v)} />
            <Toggle label={t("test.show_score")} checked={x.show_score_to_candidate} onChange={(v) => set("show_score_to_candidate", v)} />
          </div>
        </Card>
      </div>
    </>
  );
}
