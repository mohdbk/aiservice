import { CheckCircle, FileArrowDown, MagicWand, XCircle } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { TraitProfile, parseProfile } from "../components/personality";
import { Badge, Button, Card, ErrorState, Loading, Note, PageHeader, TextInput, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Q = {
  id: number; key: string; question_type: string; text: string; points: number; rubric: string; answer: string; file_url: string | null;
  options: { key: string; text: string; correct: boolean; selected: boolean }[]; accepted: string[]; auto_points: number; needs_marking: boolean;
  marker_points: number | null; marker_comment: string; suggested_points: number | null; suggestion_reason: string;
};
type Attempt = {
  id: number; attempt_no: number; status: string; percent: number; earned: number; total: number; passed: boolean | null; tab_switches: number; pastes: number;
  invitation: { id: number; code: string; assessment: string; pass_mark: number }; applicant: { id: number; name: string };
  sections: { section_no: number; title: string; earned: number; total: number; percent: number; passed: boolean | null }[]; questions: Q[];
  personality_model: string; trait_profile: unknown;
};

export default function Marking() {
  const { id } = useParams();
  const { t, to } = useLang();
  const toast = useToast();
  const { data, error, loading, reload } = useAsync(() => api<Attempt>(`/attempts/${id}`), [id]);
  const [marks, setMarks] = useState<Record<number, { points: string; comment: string }>>({});
  const save = useSaving();
  const suggest = useSaving();
  usePageTitle(t("mark.title"));
  useEffect(() => {
    if (data) setMarks(Object.fromEntries(data.questions.map((q) => [q.id, { points: q.marker_points?.toString() ?? "", comment: q.marker_comment }])));
  }, [data]);
  if (loading) return <Loading />;
  if (error || !data) return <ErrorState error={error} retry={reload} />;
  const a = data;
  const personality = a.personality_model !== "None";
  const submit = async () => {
    const body = { marks: a.questions.filter((q) => q.needs_marking || marks[q.id]?.points !== (q.marker_points?.toString() ?? ""))
      .map((q) => ({ question_id: q.id, points: marks[q.id]?.points === "" ? null : Number(marks[q.id]?.points), comment: marks[q.id]?.comment || "" })) };
    if (await save.run(() => api(`/attempts/${a.id}/marks`, { body })) !== undefined) {
      toast(t("common.saved"));
      reload();
    }
  };
  return (
    <>
      <PageHeader back={<Link to={to(`/applicants/${a.applicant.id}`)} className="mb-2 inline-block text-sm text-primary-700">{a.applicant.name}</Link>}
        title={`${personality ? t("pers.answers") : t("mark.title")}: ${a.invitation.assessment}`}
        subtitle={<span className="flex flex-wrap items-center gap-2">#{a.attempt_no}
          <Badge tone={a.status === "Marked" ? "green" : "amber"}>{a.status === "Marked" ? t("inv.status.Marked") : t("inv.status.Submitted")}</Badge>
          <span>{t("app.integrity", { tabs: a.tab_switches, pastes: a.pastes })}</span></span>}
        actions={personality ? undefined : <>
          <Button variant="outline" busy={suggest.busy} onClick={async () => { if (await suggest.run(() => api(`/attempts/${a.id}/suggest-marks`, { body: {} })) !== undefined) toast(t("common.queued"), "info"); }}
            icon={<MagicWand size={20} aria-hidden="true" />}>{t("mark.suggest")}</Button>
          <Button busy={save.busy} onClick={submit}>{t("mark.save")}</Button>
        </>} />
      {(save.error || suggest.error) && <div className="mb-4"><ErrorState error={save.error || suggest.error || undefined} /></div>}
      {personality && <Card className="mb-6" title={t("pers.profile")}><TraitProfile profile={parseProfile(a.trait_profile)} /></Card>}
      {!personality && <Card className="mb-6" title={t("mark.overall")}>
        <div className="flex flex-wrap items-center gap-6">
          <p className="text-4xl font-bold tabular-nums" dir="ltr">{a.percent}%</p>
          <p className="text-sm text-aeblack-600" dir="ltr">{a.earned} / {a.total}</p>
          {a.passed !== null && <Badge tone={a.passed ? "green" : "red"}>{a.passed ? t("res.Pass") : t("res.Fail")}</Badge>}
        </div>
        {a.sections.length > 1 && (
          <ul className="mt-4 grid gap-2 sm:grid-cols-2">
            {a.sections.map((s) => (
              <li key={s.section_no} className="flex items-center justify-between rounded-lg bg-whitely-100 p-3 text-sm">
                <span>{s.title}</span><span className="tabular-nums font-semibold" dir="ltr">{s.percent}%</span>
              </li>
            ))}
          </ul>
        )}
      </Card>}
      <ol className="space-y-4">
        {a.questions.map((q, i) => (
          <li key={q.id}>
            <Card className={q.needs_marking && q.marker_points === null ? "border-camel-300" : ""}>
              <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <p className="mb-1 text-xs text-aeblack-500">{i + 1} · {t(`qt.${q.question_type}`)}{personality ? "" : ` · ${q.points} ${t("q.points")}`}</p>
                  <div className="mc-prose" dangerouslySetInnerHTML={{ __html: q.text }} />
                </div>
                {!personality && <span className="text-sm text-aeblack-500">{t("mark.auto")}: <strong dir="ltr">{q.auto_points}</strong></span>}
              </div>
              {q.options.length > 0 ? (
                <ul className="mb-3 space-y-1">
                  {q.options.map((o) => (
                    <li key={o.key} className={`flex items-center gap-2 rounded-lg px-3 py-2 text-sm ${o.selected ? "bg-primary-50" : ""}`}>
                      {q.question_type === "Likert" ? null : o.correct ? <CheckCircle size={20} className="text-aegreen-600" aria-label={t("mark.correct_answer")} /> : <XCircle size={20} className="text-aeblack-300" aria-hidden="true" />}
                      <span className="flex-1">{o.text}</span>
                      {o.selected && <Badge tone="gold">{t("mark.chosen")}</Badge>}
                    </li>
                  ))}
                </ul>
              ) : (
                <div className="mb-3 rounded-lg bg-whitely-100 p-3">
                  <p className="mb-1 text-xs font-semibold text-aeblack-500">{t("mark.answer")}</p>
                  {q.file_url ? <a className="inline-flex items-center gap-2 font-semibold text-primary-700" href={q.file_url}><FileArrowDown size={20} aria-hidden="true" />{q.answer || t("mark.file")}</a>
                    : <p className="whitespace-pre-line text-sm" dir="auto">{q.answer || t("mark.no_answer")}</p>}
                </div>
              )}
              {q.accepted.length > 0 && <p className="mb-3 text-sm text-aeblack-600">{t("mark.accepted", { answers: q.accepted.join("، ") })}</p>}
              {q.rubric && <div className="mb-3"><Note><strong>{t("mark.rubric")}:</strong> <span className="whitespace-pre-line">{q.rubric}</span></Note></div>}
              {q.suggested_points !== null && <div className="mb-3"><Note tone="warning"><strong>{t("mark.suggested", { points: q.suggested_points })}</strong> {q.suggestion_reason}</Note></div>}
              {!personality && <div className="grid gap-3 sm:grid-cols-[10rem_1fr]">
                <TextInput label={t("mark.points")} type="number" min={0} max={q.points} step={0.5} value={marks[q.id]?.points ?? ""}
                  onChange={(e) => setMarks((m) => ({ ...m, [q.id]: { ...m[q.id], points: e.target.value } }))} />
                <TextInput label={t("mark.comment")} value={marks[q.id]?.comment ?? ""} onChange={(e) => setMarks((m) => ({ ...m, [q.id]: { ...m[q.id], comment: e.target.value } }))} />
              </div>}
            </Card>
          </li>
        ))}
      </ol>
      {!personality && <div className="mt-6 flex justify-end"><Button busy={save.busy} onClick={submit}>{t("mark.save")}</Button></div>}
    </>
  );
}
