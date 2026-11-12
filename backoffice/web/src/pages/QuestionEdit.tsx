import { FloppyDisk, Plus, Trash } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  Bilingual, Button, Card, EnglishBadge, ErrorState, LangTabs, Loading, PageHeader, RichText, SelectInput, TextArea, TextInput, Tick, Toggle, useSaving,
} from "../components/ui";
import { MODELS } from "../components/personality";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Option = { text: string; text_en: string; correct: boolean };
type Q = {
  question_type: string; text: string; text_en: string; english_status: string; english_approved: boolean; category: string; job_family: string;
  difficulty: string; points: number; tolerance: number; rubric: string; max_words: number; is_active: boolean; options: Option[]; accepted_answers: string[];
  trait: string; reverse_keyed: boolean;
};

const EMPTY: Q = {
  question_type: "SingleChoice", text: "", text_en: "", english_status: "Missing", english_approved: false, category: "", job_family: "",
  difficulty: "Medium", points: 1, tolerance: 0, rubric: "", max_words: 0, is_active: true,
  options: [{ text: "", text_en: "", correct: true }, { text: "", text_en: "", correct: false }], accepted_answers: [], trait: "", reverse_keyed: false,
};
const CHOICE = ["SingleChoice", "MultipleChoice", "TrueFalse"];

export default function QuestionEdit() {
  const { id } = useParams();
  const isNew = !id;
  const { t, to } = useLang();
  const toast = useToast();
  const nav = useNavigate();
  const loaded = useAsync(() => (isNew ? Promise.resolve(EMPTY) : api<Q>(`/questions/${id}`)), [id]);
  const [q, setQ] = useState<Q>(EMPTY);
  const [lang, setLang] = useState<Bilingual>("ar");
  const { busy, error, run } = useSaving();
  const suggest = useSaving();
  usePageTitle(isNew ? t("q.new") : t("q.title"));
  useEffect(() => { if (loaded.data) setQ({ ...EMPTY, ...loaded.data }); }, [loaded.data]);
  if (loaded.loading) return <Loading />;
  if (loaded.error) return <ErrorState error={loaded.error} retry={loaded.reload} />;
  const set = <K extends keyof Q>(k: K, v: Q[K]) => setQ((x) => ({ ...x, [k]: v }));
  const setOpt = (i: number, patch: Partial<Option>) => set("options", q.options.map((o, k) => (k === i ? { ...o, ...patch } : patch.correct && q.question_type !== "MultipleChoice" ? { ...o, correct: false } : o)));
  const isChoice = CHOICE.includes(q.question_type);
  const likert = q.question_type === "Likert";

  const save = async () => {
    const r = await run(() => api<{ id: number }>(isNew ? "/questions" : `/questions/${id}`, {
      method: isNew ? "POST" : "PUT",
      body: { ...q, options: isChoice ? q.options : [], accepted_answers: q.accepted_answers.filter((a) => a.trim()) },
    }));
    if (!r) return;
    toast(t("common.saved"));
    if (isNew) nav(to(`/questions/${r.id}`), { replace: true });
    else loaded.reload();
  };
  const suggestEnglish = async () => {
    if (await suggest.run(() => api(`/suggest-english/question/${id}`, { body: {} })) !== undefined) toast(t("common.queued"), "info");
  };

  return (
    <>
      <PageHeader back={<Link to={to("/questions")} className="mb-2 inline-block text-sm text-primary-700">{t("q.title")}</Link>}
        title={isNew ? t("q.new") : t("q.title")} subtitle={!isNew && <EnglishBadge state={q.english_status} />}
        actions={<Button busy={busy} onClick={save} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>} />
      {(error || suggest.error) && <div className="mb-4"><ErrorState error={error || suggest.error || undefined} /></div>}
      <div className="grid gap-6 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <LangTabs value={lang} onChange={setLang} english={q.english_status} approved={q.english_approved} onApproved={(v) => set("english_approved", v)}
            onSuggest={isNew ? undefined : suggestEnglish} suggesting={suggest.busy}>
            <div className="space-y-5" dir={lang === "ar" ? "rtl" : "ltr"} lang={lang}>
              {lang === "ar"
                ? <RichText label={t("q.text_ar")} dir="rtl" value={q.text} onChange={(v) => set("text", v)} />
                : <RichText label={t("q.text_en")} dir="ltr" value={q.text_en} onChange={(v) => set("text_en", v)} />}
              {isChoice && (
                <fieldset>
                  <legend className="mb-2 text-sm font-semibold">{t("q.options")}</legend>
                  <div className="space-y-2">
                    {q.options.map((o, i) => (
                      <div key={i} className="flex items-end gap-3">
                        <TextInput wrapClass="flex-1" label={lang === "ar" ? `${t("q.option_ar")} ${i + 1}` : `${t("q.option_en")} ${i + 1}`}
                          value={lang === "ar" ? o.text : o.text_en} onChange={(e) => setOpt(i, lang === "ar" ? { text: e.target.value } : { text_en: e.target.value })} />
                        {lang === "ar" && <div className="pb-3"><Tick label={t("q.correct")} checked={o.correct} onChange={(v) => setOpt(i, { correct: v })} /></div>}
                        {lang === "ar" && q.options.length > 2 && (
                          <Button variant="link" className="pb-3" aria-label={t("common.remove")} onClick={() => set("options", q.options.filter((_, k) => k !== i))}><Trash size={20} /></Button>
                        )}
                      </div>
                    ))}
                  </div>
                  {lang === "ar" && q.question_type !== "TrueFalse" && (
                    <Button variant="outline" size="sm" className="mt-3" onClick={() => set("options", [...q.options, { text: "", text_en: "", correct: false }])} icon={<Plus size={20} aria-hidden="true" />}>{t("q.add_option")}</Button>
                  )}
                </fieldset>
              )}
            </div>
          </LangTabs>
        </Card>
        <Card>
          <div className="space-y-4">
            <SelectInput label={t("q.type")} value={q.question_type} onChange={(e) => {
              const v = e.target.value;
              setQ((x) => ({ ...x, question_type: v, options: v === "TrueFalse" ? [{ text: "صح", text_en: "True", correct: true }, { text: "خطأ", text_en: "False", correct: false }] : x.options }));
            }} options={["SingleChoice", "MultipleChoice", "TrueFalse", "ShortAnswer", "Number", "Essay", "FileUpload", "Likert"].map((v) => ({ value: v, label: t(`qt.${v}`) }))} />
            <TextInput label={t("q.category")} value={q.category} onChange={(e) => set("category", e.target.value)} />
            <TextInput label={t("q.job_family")} value={q.job_family} onChange={(e) => set("job_family", e.target.value)} />
            <SelectInput label={t("q.difficulty")} value={q.difficulty} onChange={(e) => set("difficulty", e.target.value)}
              options={["Easy", "Medium", "Hard"].map((v) => ({ value: v, label: t(`diff.${v}`) }))} />
            {likert ? (
              <>
                <SelectInput label={t("pers.trait")} value={q.trait} onChange={(e) => set("trait", e.target.value)} options={[
                  { value: "", label: t("common.choose") },
                  ...Object.entries(MODELS).filter(([m]) => m !== "WorkplaceBigFive").flatMap(([m, traits]) => traits.map((x) => ({ value: x.code, label: `${t(`pers.model.${m}`)}: ${x.code} ${lang === "en" ? x.en : x.ar}` })))
                    .filter((o, i, all) => all.findIndex((y) => y.label === o.label) === i),
                ]} />
                <Toggle label={t("pers.reverse")} hint={t("pers.reverse_hint")} checked={q.reverse_keyed} onChange={(v) => set("reverse_keyed", v)} />
                <p className="text-xs text-aeblack-500">{t("pers.likert_hint")}</p>
              </>
            ) : <TextInput label={t("q.points")} type="number" min={0.5} step={0.5} value={q.points} onChange={(e) => set("points", Number(e.target.value) || 1)} />}
            {(q.question_type === "ShortAnswer" || q.question_type === "Number") && (
              <TextArea label={t("q.accepted")} hint={t("q.accepted_hint")} value={q.accepted_answers.join("\n")} onChange={(e) => set("accepted_answers", e.target.value.split("\n"))} />
            )}
            {q.question_type === "Number" && <TextInput label={t("q.tolerance")} type="number" min={0} value={q.tolerance} onChange={(e) => set("tolerance", Number(e.target.value) || 0)} />}
            {(q.question_type === "Essay" || q.question_type === "FileUpload" || q.question_type === "ShortAnswer") && (
              <TextArea label={t("q.rubric")} rows={5} value={q.rubric} onChange={(e) => set("rubric", e.target.value)} />
            )}
            {q.question_type === "Essay" && <TextInput label={t("q.max_words")} type="number" min={0} value={q.max_words} onChange={(e) => set("max_words", Number(e.target.value) || 0)} />}
            <Toggle label={t("common.active")} checked={q.is_active} onChange={(v) => set("is_active", v)} />
          </div>
        </Card>
      </div>
    </>
  );
}
