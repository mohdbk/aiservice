import { CalendarBlank, FileText, MapPin, PencilSimple, Star } from "@phosphor-icons/react";
import { DocumentDialog } from "../components/DocumentDialog";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { ActionMenu, Badge, Button, Card, Empty, ErrorState, Loading, PageHeader, SelectInput, TextArea, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
import { InterviewDialog } from "./Applicant";
import { intTone } from "./Interviews";
import { useLookups } from "./JobEdit";

type Detail = {
  id: number; code: string; starts_at: string; ends_at: string; status: string; location: string; notes: string;
  round: { id: number; name: string; skills: string[] }; applicant: { id: number; name: string; job_title: string };
  panel: { id: number; name: string }[];
  feedback: { staff_id: number; staff: string; recommendation: string; comments: string; submitted_at: string; ratings: { skill: string; rating: number }[] }[];
  can_give_feedback: boolean;
};

export default function Interview() {
  const { id } = useParams();
  const { t, to, date } = useLang();
  const toast = useToast();
  const { data, error, loading, reload } = useAsync(() => api<Detail>(`/interviews/${id}`), [id]);
  const lookups = useLookups();
  const [edit, setEdit] = useState(false);
  const [letter, setLetter] = useState(false);
  const [rec, setRec] = useState("Cleared");
  const [comments, setComments] = useState("");
  const [ratings, setRatings] = useState<Record<string, number>>({});
  const act = useSaving();
  usePageTitle(data ? `${t("int.title")}: ${data.applicant.name}` : t("int.title"));
  if (loading) return <Loading />;
  if (error || !data) return <ErrorState error={error} retry={reload} />;
  const i = data;
  const local = (iso: string) => {
    const d = new Date(iso.endsWith("Z") ? iso : iso + "Z");
    const p = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Dubai", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false }).formatToParts(d);
    const g = (k: string) => p.find((x) => x.type === k)?.value || "";
    return { date: `${g("year")}-${g("month")}-${g("day")}`, time: `${g("hour")}:${g("minute")}` };
  };
  const save = async (path: string, body: unknown) => {
    const r = await act.run(() => api(path, { body }));
    if (r !== undefined) {
      toast(t("common.saved"));
      setEdit(false);
      reload();
    }
  };
  return (
    <>
      <PageHeader
        back={<Link to={to("/interviews")} className="mb-2 inline-block text-sm text-primary-700">{t("int.title")}</Link>}
        title={<Link to={to(`/applicants/${i.applicant.id}`)}>{i.applicant.name}</Link>}
        subtitle={<span className="flex flex-wrap items-center gap-2">{i.round.name} · {i.applicant.job_title} <Badge tone={intTone(i.status)}>{t(`int.status.${i.status}`)}</Badge></span>}
        actions={<>
          <Button variant="outline" size="sm" onClick={() => setEdit(true)} icon={<PencilSimple size={20} aria-hidden="true" />}>{t("common.edit")}</Button>
          <Button variant="outline" size="sm" onClick={() => setLetter(true)} icon={<FileText size={20} aria-hidden="true" />}>{t("int.letter")}</Button>
          <ActionMenu label={t("common.status")} items={["Scheduled", "UnderReview", "Cleared", "Rejected", "Cancelled"].filter((s) => s !== i.status)
            .map((s) => ({ label: t(`int.status.${s}`), onClick: () => save(`/interviews/${i.id}/status`, { status: s }) }))} />
        </>}
      />
      {letter && <DocumentDialog kind="InterviewLetter" recordId={i.id} title={t("int.letter")} onClose={() => setLetter(false)} />}
      {act.error && <div className="mb-4"><ErrorState error={act.error} /></div>}
      <div className="grid gap-6 xl:grid-cols-3">
        <div className="space-y-6 xl:col-span-2">
          <Card title={t("int.feedback")}>
            {i.feedback.length === 0 ? <Empty /> : (
              <ul className="space-y-4">
                {i.feedback.map((f) => (
                  <li key={f.staff_id} className="rounded-xl border border-aeblack-100 p-4">
                    <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                      <p className="font-semibold">{f.staff}</p>
                      <Badge tone={f.recommendation === "Cleared" ? "green" : "red"}>{t(`rec.${f.recommendation}`)}</Badge>
                    </div>
                    <p className="mb-3 whitespace-pre-line text-sm" dir="auto">{f.comments}</p>
                    {f.ratings.length > 0 && (
                      <ul className="flex flex-wrap gap-2 text-xs">
                        {f.ratings.map((r) => <li key={r.skill} className="rounded-full bg-whitely-100 px-3 py-1">{r.skill}: <strong dir="ltr">{r.rating}/5</strong></li>)}
                      </ul>
                    )}
                    <p className="mt-2 text-xs text-aeblack-500">{date(f.submitted_at, true)}</p>
                  </li>
                ))}
              </ul>
            )}
          </Card>
          {i.can_give_feedback && (
            <Card title={t("int.give_feedback")}>
              <form className="space-y-4" onSubmit={(e) => {
                e.preventDefault();
                save(`/interviews/${i.id}/feedback`, { recommendation: rec, comments, ratings: Object.entries(ratings).map(([skill, rating]) => ({ skill, rating })) });
              }}>
                <SelectInput label={t("int.recommendation")} value={rec} onChange={(e) => setRec(e.target.value)}
                  options={["Cleared", "Rejected"].map((r) => ({ value: r, label: t(`rec.${r}`) }))} />
                {i.round.skills.length > 0 && (
                  <fieldset>
                    <legend className="mb-2 text-sm font-semibold">{t("int.ratings")}</legend>
                    <div className="space-y-2">
                      {i.round.skills.map((s) => (
                        <div key={s} className="flex flex-wrap items-center justify-between gap-3">
                          <span className="text-sm">{s}</span>
                          <div className="flex gap-1" role="radiogroup" aria-label={s}>
                            {[1, 2, 3, 4, 5].map((n) => (
                              <button key={n} type="button" role="radio" aria-checked={ratings[s] === n} aria-label={`${n}`}
                                onClick={() => setRatings((r) => ({ ...r, [s]: n }))} className="rounded p-0.5 text-primary-600">
                                <Star size={24} weight={(ratings[s] || 0) >= n ? "fill" : "regular"} />
                              </button>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  </fieldset>
                )}
                <TextArea label={t("int.comments")} required value={comments} onChange={(e) => setComments(e.target.value)} />
                <Button type="submit" busy={act.busy}>{t("common.save")}</Button>
              </form>
            </Card>
          )}
        </div>
        <Card title={t("common.details")}>
          <ul className="space-y-3 text-sm">
            <li className="flex items-center gap-2"><CalendarBlank size={20} aria-hidden="true" />{date(i.starts_at, true)}</li>
            {i.location && <li className="flex items-center gap-2"><MapPin size={20} aria-hidden="true" /><span dir="auto">{i.location}</span></li>}
            <li><p className="mb-1 font-semibold">{t("int.panel")}</p><ul className="list-disc ps-5">{i.panel.map((p) => <li key={p.id}>{p.name}</li>)}</ul></li>
            {i.notes && <li><p className="mb-1 font-semibold">{t("int.notes")}</p><p className="whitespace-pre-line" dir="auto">{i.notes}</p></li>}
          </ul>
        </Card>
      </div>
      {edit && (
        <InterviewDialog open onClose={() => setEdit(false)} lookups={lookups.data} busy={act.busy}
          initial={{ round_id: i.round.id, date: local(i.starts_at).date, from: local(i.starts_at).time, to: local(i.ends_at).time, location: i.location, notes: i.notes, panelists: i.panel.map((p) => p.id) }}
          onSave={(b) => act.run(() => api(`/interviews/${i.id}`, { method: "PUT", body: { ...b, application_id: i.applicant.id } })).then((r) => { if (r !== undefined) { setEdit(false); reload(); toast(t("common.saved")); } })} />
      )}
    </>
  );
}
