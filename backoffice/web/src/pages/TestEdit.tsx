import { ArrowDown, ArrowUp, CheckCircle, Circle, FloppyDisk, Plus, RocketLaunch, Shuffle, Trash } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  Bilingual, Button, Card, Empty, EnglishBadge, ErrorState, FlagLabel, LangTabs, Loading, Modal, Note, PageHeader, Pager, RichText, SectionTabs, SelectInput, TextArea, TextInput, Toggle, useSaving,
} from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
import { BIG_FIVE, MODEL_KEYS, NORMED, MODELS } from "../components/personality";
import { useLookups } from "./JobEdit";
import { plain } from "./Questions";

type Item = { question_id: number | null; question?: string; question_type?: string | null; points?: number | null; draw_category: string; draw_difficulty: string; draw_job_family: string; draw_count: number; points_override: number | null };
type Sec = { id?: number; title: string; title_en: string; instructions: string; instructions_en: string; minutes: number; pass_mark: number; items: Item[] };
type Test = {
  title: string; title_en: string; instructions: string; instructions_en: string; english_status: string; english_approved: boolean; assessment_type: string;
  status: string; pass_mark: number; max_attempts: number; score_policy: string; shuffle_questions: boolean; shuffle_options: boolean;
  show_score_to_candidate: boolean; default_deadline_days: number; question_count?: number; sections: Sec[];
  english_questions?: { ready: number; total: number };
  personality_model: string; trait_norms: Record<string, { mean: number; sd: number }>;
};

const SECTION: Sec = { title: "", title_en: "", instructions: "", instructions_en: "", minutes: 20, pass_mark: 0, items: [] };
const EMPTY: Test = {
  title: "", title_en: "", instructions: "", instructions_en: "", english_status: "Missing", english_approved: false, assessment_type: "KnowledgeTest",
  status: "Draft", pass_mark: 60, max_attempts: 1, score_policy: "Highest", shuffle_questions: true, shuffle_options: true, show_score_to_candidate: false,
  default_deadline_days: 7, sections: [{ ...SECTION }], personality_model: "None", trait_norms: {},
};

export default function TestEdit() {
  const { id } = useParams();
  const isNew = !id;
  const { t, to, lang: panel } = useLang();
  const toast = useToast();
  const nav = useNavigate();
  const lk = useLookups();
  const [picking, setPicking] = useState<number | null>(null);
  const [tab, setTab] = useState("details");
  const loaded = useAsync(() => (isNew ? Promise.resolve(EMPTY) : api<Test>(`/assessments/${id}`)), [id]);
  const [x, setX] = useState<Test>(EMPTY);
  const [lang, setLang] = useState<Bilingual>(panel === "en" ? "en" : "ar");
  const { busy, error, run } = useSaving();
  const suggest = useSaving();
  usePageTitle(isNew ? t("test.new") : x.title || t("test.title"));
  useEffect(() => { if (loaded.data) setX({ ...EMPTY, ...loaded.data }); }, [loaded.data]);
  if (loaded.loading) return <Loading />;
  if (loaded.error) return <ErrorState error={loaded.error} retry={loaded.reload} />;
  const set = <K extends keyof Test>(k: K, v: Test[K]) => setX((p) => ({ ...p, [k]: v }));
  const setSec = (i: number, patch: Partial<Sec>) => set("sections", x.sections.map((s, k) => (k === i ? { ...s, ...patch } : s)));
  const setItem = (si: number, ii: number, patch: Partial<Item>) => setSec(si, { items: x.sections[si].items.map((it, k) => (k === ii ? { ...it, ...patch } : it)) });

  const save = async (override?: Partial<Test>) => {
    const body = { ...x, ...override };
    const r = await run(() => api<{ id: number }>(isNew ? "/assessments" : `/assessments/${id}`, { method: isNew ? "POST" : "PUT", body }));
    if (!r) return;
    toast(t("common.saved"));
    if (isNew) nav(to(`/tests/${r.id}`), { replace: true });
    else loaded.reload();
  };
  const suggestEnglish = async () => {
    if (await suggest.run(() => api(`/suggest-english/assessment/${id}`, { body: {} })) !== undefined) toast(t("common.queued"), "info");
  };
  const ar = lang === "ar";
  const personality = x.assessment_type === "Personality";
  const move = (si: number, ii: number, d: number) => {
    const items = [...x.sections[si].items];
    const j = ii + d;
    if (j < 0 || j >= items.length) return;
    [items[ii], items[j]] = [items[j], items[ii]];
    setSec(si, { items });
  };
  // What makes the assessment ready for candidates, shown as a checklist at the top.
  const totalItems = x.sections.reduce((n, s) => n + s.items.length, 0);
  const checks = [
    { ok: !!x.title.trim(), label: t("test.check.name") },
    { ok: totalItems > 0 && x.sections.every((s) => s.items.length > 0), label: t("test.check.questions") },
    ...(personality ? [] : [{ ok: x.pass_mark > 0, label: t("test.check.pass_mark") }]),
    { ok: x.english_approved, label: t("test.check.english"), optional: true },
  ];
  const ready = checks.filter((c) => !c.optional).every((c) => c.ok);
  const setNorm = (code: string, key: "mean" | "sd", value: string) => {
    const next = { ...x.trait_norms };
    const cur = next[code] ?? { mean: 0, sd: 0 };
    const n = { ...cur, [key]: Number(value) || 0 };
    if (!n.mean && !n.sd) delete next[code];
    else next[code] = n;
    set("trait_norms", next);
  };

  return (
    <>
      <PageHeader back={<Link to={to("/tests")} className="mb-2 inline-block text-sm text-primary-700">{t("test.title")}</Link>}
        title={isNew ? t("test.new") : x.title}
        subtitle={!isNew && <span className="flex flex-wrap items-center gap-2"><EnglishBadge state={x.english_status} />
          {x.question_count !== undefined && <span className="text-sm">{t("test.question_count", { count: x.question_count })}</span>}</span>}
        actions={<Button busy={busy} onClick={() => save()} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>} />
      {(error || suggest.error) && <div className="mb-4"><ErrorState error={error || suggest.error || undefined} /></div>}
      <div className="mb-6 flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-aeblack-100 bg-white p-4">
        <ul className="flex flex-wrap gap-x-5 gap-y-2 text-sm">
          {checks.map((c) => (
            <li key={c.label} className={`inline-flex items-center gap-1.5 ${c.ok ? "text-aegreen-700" : c.optional ? "text-aeblack-500" : "text-aeblack-800"}`}>
              {c.ok ? <CheckCircle size={20} weight="fill" aria-hidden="true" /> : <Circle size={20} aria-hidden="true" />}{c.label}{c.optional && !c.ok && <span className="text-xs">({t("common.optional")})</span>}
            </li>
          ))}
        </ul>
        {x.status === "Published"
          ? <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-aegreen-700"><CheckCircle size={20} weight="fill" aria-hidden="true" />{t("test.live")}</span>
          : <Button busy={busy} disabled={!ready} onClick={() => save({ status: "Published" })} icon={<RocketLaunch size={20} aria-hidden="true" />}
              title={ready ? undefined : t("test.publish_hint")}>{t("test.publish")}</Button>}
      </div>
      <SectionTabs value={tab} onChange={setTab} tabs={[
        { value: "details", label: t("test.tab.details") },
        { value: "questions", label: <>{t("test.tab.questions")} <span className="ms-1 rounded-full bg-aeblack-50 px-2 text-xs tabular-nums">{totalItems}</span></> },
        { value: "rules", label: t("test.tab.rules") },
      ]} />
      <div className="mt-6 grid gap-6">
        <div className="space-y-6">
          {tab === "details" && <>
          <Card>
            <div className="mb-5 grid gap-4 sm:grid-cols-2">
              <SelectInput label={t("test.type")} hint={t("test.type_hint")} value={x.assessment_type} onChange={(e) => {
                const v = e.target.value;
                setX((p) => ({ ...p, assessment_type: v, personality_model: v === "Personality" ? (p.personality_model === "None" ? "WorkplaceBigFive" : p.personality_model) : "None",
                  ...(v === "Personality" ? { shuffle_options: false, show_score_to_candidate: false, score_policy: "Latest" } : {}) }));
              }} options={["KnowledgeTest", "LanguageCheck", "SituationalJudgement", "WorkSample", "Aptitude", "Personality", "Other"].map((v) => ({ value: v, label: t(`type.${v}`) }))} />
              <SelectInput label={t("common.status")} value={x.status} onChange={(e) => set("status", e.target.value)}
                options={["Draft", "Published", "Archived"].map((v) => ({ value: v, label: t(`status.${v}`) }))} />
            </div>
            <LangTabs value={lang} onChange={setLang} english={x.english_status} approved={x.english_approved} onApproved={(v) => set("english_approved", v)}
              onSuggest={isNew ? undefined : suggestEnglish} suggesting={suggest.busy}>
              {!ar && !isNew && x.english_questions && x.english_questions.total > 0 && (
                <div className="mb-5">
                  {x.english_approved && x.english_questions.ready === x.english_questions.total
                    ? <Note>{t("test.english_ready")}</Note>
                    : <Note tone="warning">{t("test.english_not_ready", { ready: x.english_questions.ready, total: x.english_questions.total })}</Note>}
                </div>
              )}
              <div className="space-y-5" dir={ar ? "rtl" : "ltr"} lang={lang}>
                <TextInput required={ar} label={ar ? t("test.name_ar") : t("test.name_en")} value={ar ? x.title : x.title_en} onChange={(e) => set(ar ? "title" : "title_en", e.target.value)} />
                <RichText label={ar ? t("test.instructions_ar") : t("test.instructions_en")} dir={ar ? "rtl" : "ltr"}
                  value={ar ? x.instructions : x.instructions_en} onChange={(v) => set(ar ? "instructions" : "instructions_en", v)} />
              </div>
            </LangTabs>
          </Card>
          </>}

          {tab === "questions" && <>
          <Note>{t(personality ? "test.questions_hint_personality" : "test.questions_hint")}</Note>
          <Card title={t("test.sections")} actions={<Button variant="outline" size="sm" onClick={() => set("sections", [...x.sections, { ...SECTION }])} icon={<Plus size={20} aria-hidden="true" />}>{t("test.add_section")}</Button>}>
            <div className="space-y-6">
              {x.sections.map((s, si) => (
                <div key={si} className="rounded-xl border border-aeblack-100 p-4">
                  <div className="mb-3 flex flex-wrap items-end gap-3">
                    <p className="flex-1 font-bold">{t("test.section", { n: si + 1 })}{s.title && `: ${s.title}`}</p>
                    {x.sections.length > 1 && <Button variant="link" aria-label={t("common.remove")} onClick={() => set("sections", x.sections.filter((_, k) => k !== si))}><Trash size={20} /></Button>}
                  </div>
                  <div className="mb-3 grid gap-3 sm:grid-cols-2">
                    <TextInput label={<FlagLabel lang="ar">{t("test.section_name")}</FlagLabel>} dir="rtl" value={s.title} onChange={(e) => setSec(si, { title: e.target.value })} />
                    <TextInput label={<FlagLabel lang="en">{t("test.section_name")}</FlagLabel>} dir="ltr" value={s.title_en} onChange={(e) => setSec(si, { title_en: e.target.value })} />
                  </div>
                  <details className="mb-3 rounded-lg bg-whitely-50 px-3 py-2">
                    <summary className="cursor-pointer text-sm font-semibold text-aeblack-700">{t("test.section_instructions")}</summary>
                    <div className="mt-3 grid gap-3 sm:grid-cols-2">
                      <TextArea label={<FlagLabel lang="ar">{t("test.instructions_ar")}</FlagLabel>} dir="rtl" rows={2} value={s.instructions} onChange={(e) => setSec(si, { instructions: e.target.value })} />
                      <TextArea label={<FlagLabel lang="en">{t("test.instructions_en")}</FlagLabel>} dir="ltr" rows={2} value={s.instructions_en} onChange={(e) => setSec(si, { instructions_en: e.target.value })} />
                    </div>
                  </details>
                  <div className="mb-4 grid gap-3 sm:grid-cols-2">
                    <TextInput label={t("test.minutes")} type="number" min={0} max={600} value={s.minutes} onChange={(e) => setSec(si, { minutes: Number(e.target.value) || 0 })} />
                    {!personality && <TextInput label={t("test.section_pass")} type="number" min={0} max={100} value={s.pass_mark} onChange={(e) => setSec(si, { pass_mark: Number(e.target.value) || 0 })} />}
                  </div>
                  <p className="mb-2 text-sm font-semibold">{t("test.items")} <span className="font-normal text-aeblack-500">({s.items.length})</span></p>
                  {s.items.length === 0 && <p className="mb-2 rounded-lg border border-dashed border-aeblack-200 p-4 text-center text-sm text-aeblack-500">{t("test.no_items")}</p>}
                  <ol className="space-y-2">
                    {s.items.map((it, ii) => (
                      <li key={ii} className={`grid gap-2 rounded-lg bg-whitely-100 p-3 sm:items-center ${it.question_id ? "sm:grid-cols-[2rem_1fr_6rem_auto]" : "sm:grid-cols-[2rem_1fr_6rem_auto]"}`}>
                        <span className="text-sm tabular-nums text-aeblack-500">{ii + 1}.</span>
                        {it.question_id ? (
                          <div className="min-w-0">
                            <p className="text-sm text-aeblack-900" dir="auto">{it.question ? plain(it.question) : `#${it.question_id}`}</p>
                            <p className="text-xs text-aeblack-500">{it.question_type ? t(`qt.${it.question_type}`) : ""}</p>
                          </div>
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
                        {personality ? <span /> : <TextInput label={t("test.points_override")} type="number" min={0} step={0.5} value={it.points_override ?? ""} placeholder={it.points != null ? String(it.points) : ""}
                          onChange={(e) => setItem(si, ii, { points_override: e.target.value ? Number(e.target.value) : null })} />}
                        <span className="flex items-center gap-1">
                          <button type="button" onClick={() => move(si, ii, -1)} disabled={ii === 0} className="rounded p-1.5 text-aeblack-500 hover:bg-white disabled:opacity-30" aria-label={t("test.move_up")}><ArrowUp size={18} /></button>
                          <button type="button" onClick={() => move(si, ii, 1)} disabled={ii === s.items.length - 1} className="rounded p-1.5 text-aeblack-500 hover:bg-white disabled:opacity-30" aria-label={t("test.move_down")}><ArrowDown size={18} /></button>
                          <button type="button" onClick={() => setSec(si, { items: s.items.filter((_, k) => k !== ii) })} className="rounded p-1.5 text-aeblack-500 hover:bg-aered-50 hover:text-aered-700" aria-label={t("common.remove")}><Trash size={18} /></button>
                        </span>
                      </li>
                    ))}
                  </ol>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Button variant="outline" size="sm" onClick={() => setPicking(si)}
                      icon={<Plus size={20} aria-hidden="true" />}>{t("test.add_questions")}</Button>
                    <Button variant="outline" size="sm" onClick={() => setSec(si, { items: [...s.items, { question_id: null, draw_category: "", draw_difficulty: "", draw_job_family: "", draw_count: 5, points_override: null }] })}
                      icon={<Shuffle size={20} aria-hidden="true" />}>{t("test.add_draw")}</Button>
                  </div>
                </div>
              ))}
            </div>
          </Card>
          </>}

          {tab === "rules" && <div className="grid gap-6 lg:grid-cols-2">
        <Card title={personality ? t("test.rules.personality") : t("test.rules.scoring")}>
          <div className="space-y-4">
            {personality && <>
              <SelectInput label={t("pers.model")} value={BIG_FIVE.includes(x.personality_model) ? "BigFive" : x.personality_model}
                onChange={(e) => { const v = e.target.value; setX((p) => ({ ...p, personality_model: v === "BigFive" ? (BIG_FIVE.includes(p.personality_model) ? p.personality_model : "WorkplaceBigFive") : v, trait_norms: {} })); }}
                options={[...MODEL_KEYS, ...(MODEL_KEYS.includes(x.personality_model) || BIG_FIVE.includes(x.personality_model) ? [] : [x.personality_model])].map((v) => ({ value: v, label: t(`pers.model.${v}`) }))} />
              {BIG_FIVE.includes(x.personality_model) && (
                <SelectInput label={t("pers.scoring")} value={x.personality_model} onChange={(e) => setX((p) => ({ ...p, personality_model: e.target.value, trait_norms: {} }))}
                  options={BIG_FIVE.map((v) => ({ value: v, label: t(`pers.scoring.${v}`) }))} />
              )}
              <p className="text-xs text-aeblack-500">{t(`pers.model_hint.${x.personality_model}`)}</p>
              {NORMED.includes(x.personality_model) && <details className="rounded-xl bg-whitely-100 p-3">
                <summary className="cursor-pointer text-sm font-semibold">{t("pers.norms")}</summary>
                <p className="my-2 text-xs text-aeblack-500">{t("pers.norms_hint")}</p>
                <div className="space-y-2">
                  {(MODELS[x.personality_model] || []).map((tr) => (
                    <div key={tr.code} className="grid grid-cols-[1fr_5rem_5rem] items-end gap-2">
                      <span className="pb-3 text-sm">{tr.code} {ar ? tr.ar : tr.en}</span>
                      <TextInput label={t("pers.mean")} type="number" step={0.01} min={1} max={5} value={x.trait_norms[tr.code]?.mean ?? ""} onChange={(e) => setNorm(tr.code, "mean", e.target.value)} />
                      <TextInput label={t("pers.sd")} type="number" step={0.01} min={0} max={4} value={x.trait_norms[tr.code]?.sd ?? ""} onChange={(e) => setNorm(tr.code, "sd", e.target.value)} />
                    </div>
                  ))}
                </div>
              </details>}
            </>}
            {!personality && <TextInput label={t("test.pass_mark")} hint={t("test.pass_mark_hint")} type="number" min={0} max={100} value={x.pass_mark} onChange={(e) => set("pass_mark", Number(e.target.value) || 0)} />}
            {!personality && <Toggle label={t("test.show_score")} checked={x.show_score_to_candidate} onChange={(v) => set("show_score_to_candidate", v)} />}
          </div>
        </Card>
        <Card title={t("test.rules.taking")}>
          <div className="space-y-4">
            <TextInput label={t("test.max_attempts")} type="number" min={1} max={10} value={x.max_attempts} onChange={(e) => set("max_attempts", Number(e.target.value) || 1)} />
            <SelectInput label={t("test.score_policy")} value={x.score_policy} onChange={(e) => set("score_policy", e.target.value)}
              options={["Highest", "Latest"].map((v) => ({ value: v, label: t(`policy.${v}`) }))} />
            <TextInput label={t("test.deadline_days")} type="number" min={1} max={90} value={x.default_deadline_days} onChange={(e) => set("default_deadline_days", Number(e.target.value) || 7)} />
            <Toggle label={t("test.shuffle_questions")} checked={x.shuffle_questions} onChange={(v) => set("shuffle_questions", v)} />
            {!personality && <Toggle label={t("test.shuffle_options")} checked={x.shuffle_options} onChange={(v) => set("shuffle_options", v)} />}
          </div>
        </Card>
          </div>}
        </div>
      </div>
      {picking !== null && (
        <QuestionPicker personality={personality} taken={x.sections[picking].items.map((i) => i.question_id).filter((v): v is number => !!v)}
          onClose={() => setPicking(null)} onPick={(qs) => {
            const sec = x.sections[picking];
            setSec(picking, { items: [...sec.items, ...qs.map((q) => ({ question_id: q.id, question: q.text, question_type: q.question_type, points: q.points ?? null,
              draw_category: "", draw_difficulty: "", draw_job_family: "", draw_count: 0, points_override: null }))] });
            setPicking(null);
          }} />
      )}
    </>
  );
}

type BankQ = { id: number; text: string; category: string; question_type: string; points?: number };

/** Picks several questions from the bank at once: search, filter by category and type, tick, then add them all. */
function QuestionPicker({ onPick, onClose, taken, personality }: { onPick: (q: BankQ[]) => void; onClose: () => void; taken: number[]; personality: boolean }) {
  const { t } = useLang();
  const lk = useLookups();
  const [term, setTerm] = useState("");
  const [q, setQ] = useState("");
  const [category, setCategory] = useState("");
  const [type, setType] = useState(personality ? "Likert" : "");
  const [page, setPage] = useState(1);
  const [chosen, setChosen] = useState<Map<number, BankQ>>(new Map());
  useEffect(() => { const h = setTimeout(() => { setQ(term); setPage(1); }, 300); return () => clearTimeout(h); }, [term]);
  const res = useAsync(() => api<{ items: BankQ[]; page: number; pages: number; total: number }>(
    `/questions?active=true&size=25&sort=text&dir=asc&page=${page}${q ? `&q=${encodeURIComponent(q)}` : ""}${category ? `&category=${encodeURIComponent(category)}` : ""}${type ? `&type=${type}` : ""}`), [q, page, category, type]);
  const toggle = (x: BankQ) => setChosen((m) => { const n = new Map(m); if (n.has(x.id)) n.delete(x.id); else n.set(x.id, x); return n; });
  const visible = (res.data?.items || []).filter((x) => !taken.includes(x.id));
  const allOn = visible.length > 0 && visible.every((x) => chosen.has(x.id));
  return (
    <Modal open onClose={onClose} title={t("test.add_questions")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
      <Button disabled={chosen.size === 0} onClick={() => onPick([...chosen.values()])}>{t("test.add_n", { n: chosen.size })}</Button></>}>
      <TextInput label={t("common.search")} value={term} onChange={(e) => setTerm(e.target.value)} autoFocus />
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectInput label={t("q.category")} value={category} onChange={(e) => { setCategory(e.target.value); setPage(1); }}
          options={[{ value: "", label: t("common.all") }, ...(lk.data?.categories || []).map((c) => ({ value: c, label: c }))]} />
        <SelectInput label={t("q.type")} value={type} onChange={(e) => { setType(e.target.value); setPage(1); }}
          options={[{ value: "", label: t("common.all") }, ...["SingleChoice", "MultipleChoice", "TrueFalse", "ShortAnswer", "Number", "Essay", "FileUpload", "Likert"].map((v) => ({ value: v, label: t(`qt.${v}`) }))]} />
      </div>
      {res.loading && !res.data ? <Loading /> : res.error ? <ErrorState error={res.error} /> : (res.data?.items.length ?? 0) === 0 ? <Empty /> : (
        <>
          <div className="flex items-center justify-between text-sm">
            <button type="button" className="font-semibold text-primary-700 hover:underline disabled:opacity-40" disabled={visible.length === 0}
              onClick={() => setChosen((m) => { const n = new Map(m); for (const x of visible) { if (allOn) n.delete(x.id); else n.set(x.id, x); } return n; })}>
              {allOn ? t("test.unselect_page") : t("test.select_page")}
            </button>
            <span className="text-aeblack-500">{t("multi.count", { n: chosen.size, total: res.data!.total })}</span>
          </div>
          <ul className="max-h-[45vh] divide-y divide-aeblack-100 overflow-auto rounded-xl border border-aeblack-100">
            {res.data!.items.map((x) => {
              const added = taken.includes(x.id);
              const on = chosen.has(x.id);
              return (
                <li key={x.id}>
                  <label className={`flex items-start gap-3 px-3 py-2.5 text-sm ${added ? "opacity-50" : "cursor-pointer hover:bg-primary-50"}`}>
                    <input type="checkbox" className="mt-0.5" checked={added || on} disabled={added} onChange={() => toggle(x)} />
                    <span className="min-w-0">
                      <span className="line-clamp-2" dir="auto">{plain(x.text)}</span>
                      <span className="text-xs text-aeblack-500">{t(`qt.${x.question_type}`)}{x.category && ` · ${x.category}`}{added && ` · ${t("test.already_added")}`}</span>
                    </span>
                  </label>
                </li>
              );
            })}
          </ul>
        </>
      )}
      {res.data && <Pager page={page} pages={res.data.pages} onPage={setPage} />}
    </Modal>
  );
}
