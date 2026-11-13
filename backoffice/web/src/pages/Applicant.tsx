import { ArrowClockwise, CalendarDots, CalendarPlus, Plus, Trash, DownloadSimple, Envelope, Exam, FileText, Handshake, Phone } from "@phosphor-icons/react";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  ActionMenu, Badge, Button, Card, Empty, ErrorState, Loading, Modal, Note, PageHeader, Score, SectionTabs, SelectInput, Table, TextArea, TextInput, Tick, useSaving,
} from "../components/ui";
import { DocumentDialog } from "../components/DocumentDialog";
import { SelectionCard, type SelectionView } from "../components/Selection";
import { TraitProfile, parseProfile, type PersonalitySummary } from "../components/personality";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
import { appTone, screenTone } from "./Applicants";
import { useLookups } from "./JobEdit";

type Detail = {
  id: number; code: string; reference: string; full_name: string; email: string; phone: string; country: string; nationality?: string; cover_note: string; status: string;
  language: string; created_at: string; job: { id: number; code: string; title: string };
  cv: { file_name: string; content_type: string; size: number; url: string } | null;
  screening: { outcome: string; answers: { question: string; answer: string; meets: boolean | null }[] };
  analysis: null | {
    status: string; score: number | null; summary: string; summary_ar: string; gaps: string; years: number | null; cv_language: string; read_method: string;
    model: string; scored_at: string | null; error: string; criteria: { criterion: string; weight: number; is_must_have: boolean; score: number; evidence: string }[];
  };
  notes: { id: number; kind: string; text: string; created_at: string; author: string }[];
  interviews: { id: number; code: string; round: string; starts_at: string; ends_at: string; status: string; feedback: number }[];
  offers: { id: number; code: string; position_title: string; status: string; offer_date: string }[];
  personality?: Record<string, PersonalitySummary | null>;
  selection?: SelectionView | null;
  duplicates?: { id: number; code: string; full_name: string; email: string; created_at: string; same_cv: boolean; same_phone: boolean; same_name: boolean }[];
  invitations: {
    id: number; code: string; assessment_id: number; title: string; status: string; deadline_at: string; attempts_used: number; attempts_allowed: number;
    score: number | null; result: string | null; personality_model: string;
    attempts: {
      id: number; attempt_no: number; status: string; percent: number; passed: boolean | null; tab_switches: number; pastes: number; submitted_at: string | null;
      trait_profile: string | null;
    }[];
  }[];
};

export const invTone = (s: string) => (s === "Marked" ? "green" : s === "Submitted" ? "amber" : s === "InProgress" ? "blue" : s === "Invited" ? "gold" : "gray");

export default function Applicant() {
  const { id } = useParams();
  const { t, to, date, lang } = useLang();
  const toast = useToast();
  const { data, error, loading, reload } = useAsync(() => api<Detail>(`/applicants/${id}`), [id]);
  const lookups = useLookups();
  const [tab, setTab] = useState("overview");
  const [dialog, setDialog] = useState<"" | "interview" | "offer" | "invite" | "reexam" | "document" | "times">("");
  const [bookingsKey, setBookingsKey] = useState(0);
  const [reexamFor, setReexamFor] = useState<number | null>(null);
  const [note, setNote] = useState("");
  const act = useSaving();
  usePageTitle(data?.full_name || t("apps.title"));
  if (loading) return <Loading />;
  if (error || !data) return <ErrorState error={error} retry={reload} />;
  const a = data;
  const lk = lookups.data;

  const post = async (path: string, body: unknown = {}, ok?: string) => {
    const r = await act.run(() => api(path, { body }));
    if (r !== undefined) {
      if (ok) toast(ok);
      setDialog("");
      reload();
    }
    return r;
  };

  const c = a.analysis;
  return (
    <>
      <PageHeader
        back={<Link to={to(`/applicants?job=${a.job.id}`)} className="mb-2 inline-block text-sm text-primary-700">{a.job.title}</Link>}
        title={a.full_name}
        subtitle={<span className="flex flex-wrap items-center gap-2">
          <span dir="ltr">{a.code}</span>
          <Badge tone={appTone(a.status)}>{t(`app.status.${a.status}`)}</Badge>
          <Badge tone={screenTone(a.screening.outcome)}>{t(`screen.${a.screening.outcome}`)}</Badge>
        </span>}
        actions={<>
          <ActionMenu label={t("app.change_status")} items={["Open", "Replied", "Hold", "Accepted", "Rejected"].filter((s) => s !== a.status).map((s) => ({
            label: t(`app.status.${s}`), onClick: () => post(`/applicants/${a.id}/status`, { status: s }, t("common.saved")),
          }))} />
          <Button variant="outline" size="sm" onClick={() => setDialog("invite")} icon={<Exam size={20} aria-hidden="true" />}>{t("app.invite")}</Button>
          <ActionMenu label={t("app.interview")} icon={<CalendarPlus size={20} aria-hidden="true" />} items={[
            { label: t("app.schedule"), onClick: () => setDialog("interview") },
            { label: t("book.offer_times"), onClick: () => setDialog("times") },
          ]} />
          <Button variant="outline" size="sm" onClick={() => setDialog("document")} icon={<FileText size={20} aria-hidden="true" />}>{t("app.letter")}</Button>
          <Button size="sm" onClick={() => setDialog("offer")} icon={<Handshake size={20} aria-hidden="true" />}>{t("app.make_offer")}</Button>
        </>}
      />
      {dialog === "document" && <DocumentDialog kind="ApplicantLetter" recordId={a.id} title={t("app.letter")} onClose={() => setDialog("")} />}
      {act.error && <div className="mb-4"><ErrorState error={act.error} /></div>}
      <SectionTabs value={tab} onChange={setTab} tabs={[
        { value: "overview", label: t("app.overview") }, { value: "cv", label: t("app.cv") },
        { value: "screening", label: t("app.screening") }, { value: "tests", label: `${t("app.tests")} (${a.invitations.length})` },
        { value: "interviews", label: `${t("app.interviews")} (${a.interviews.length})` }, { value: "offers", label: `${t("app.offers")} (${a.offers.length})` },
        { value: "notes", label: t("app.notes") },
      ]} />

      {tab === "overview" && (a.duplicates?.length ?? 0) > 0 && (
        <div className="mb-6"><Note tone="warning">
          <p className="m-0 font-semibold">{t("dup.title")}</p>
          <ul className="mt-2 space-y-1">
            {a.duplicates!.map((d) => (
              <li key={d.id}>
                <Link to={to(`/applicants/${d.id}`)} className="font-semibold">{d.full_name}</Link> <span dir="ltr">{d.email}</span>
                <span className="text-xs"> · {[d.same_cv && t("dup.cv"), d.same_phone && t("dup.phone"), d.same_name && t("dup.name")].filter(Boolean).join(" · ")} · {date(d.created_at)}</span>
              </li>
            ))}
          </ul>
        </Note></div>
      )}
      {tab === "overview" && <div className="mb-6"><SelectionCard s={a.selection} /></div>}
      {tab === "overview" && (
        <div className="grid gap-6 xl:grid-cols-3">
          <Card className="xl:col-span-2" title={t("app.score")}
            actions={<Button variant="outline" size="sm" busy={act.busy} onClick={() => post(`/applicants/${a.id}/rescore`, {}, t("common.queued"))} icon={<ArrowClockwise size={20} aria-hidden="true" />}>{t("app.rescore")}</Button>}>
            <p className="mb-4 text-sm text-aeblack-500">{t("app.score_hint")}</p>
            {!c || c.status !== "Scored" ? (
              <Note tone={c?.status === "Failed" ? "warning" : "info"}>{c ? `${t(`ai.${c.status}`)}${c.error ? `: ${c.error}` : ""}` : t("app.no_analysis")}</Note>
            ) : (
              <div className="space-y-5">
                <div className="flex flex-wrap items-center gap-6">
                  <div className="text-5xl"><Score value={c.score} /></div>
                  <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm">
                    <dt className="text-aeblack-500">{t("app.years")}</dt><dd className="font-semibold" dir="ltr">{c.years ?? "-"}</dd>
                    <dt className="text-aeblack-500">{t("app.read_as")}</dt><dd>{c.read_method}</dd>
                  </dl>
                </div>
                <p className="leading-7">{lang === "ar" && c.summary_ar ? c.summary_ar : c.summary}</p>
                {c.gaps && <Note tone="warning"><strong>{t("app.gaps")}:</strong> {c.gaps}</Note>}
                <Table head={[t("app.criteria"), t("jobs.weight"), t("apps.score"), t("app.evidence")]}>
                  {c.criteria.map((r, i) => (
                    <tr key={i}>
                      <td className="font-semibold">{r.criterion}{r.is_must_have && <Badge tone="gold">{t("jobs.must")}</Badge>}</td>
                      <td className="tabular-nums">{r.weight}</td>
                      <td className="tabular-nums font-bold" dir="ltr">{r.score}/10</td>
                      <td className="text-sm text-aeblack-600">{r.evidence}</td>
                    </tr>
                  ))}
                </Table>
              </div>
            )}
          </Card>
          <div className="space-y-6">
            <Card title={t("app.contact")}>
              <ul className="space-y-2 text-sm">
                <li className="flex items-center gap-2"><Envelope size={20} aria-hidden="true" /><a href={`mailto:${a.email}`} dir="ltr">{a.email}</a></li>
                <li className="flex items-center gap-2"><Phone size={20} aria-hidden="true" /><span dir="ltr">{a.phone}</span></li>
                <li><span className="text-aeblack-500">{t("app.country")}:</span> {a.country}</li>
                {a.nationality && <li><span className="text-aeblack-500">{t("apps.nationality")}:</span> {a.nationality}</li>}
                <li><span className="text-aeblack-500">{t("app.reference")}:</span> <span dir="ltr">{a.reference}</span></li>
                <li><span className="text-aeblack-500">{t("apps.applied")}:</span> {date(a.created_at, true)}</li>
              </ul>
            </Card>
            {a.cover_note && <Card title={t("app.cover_note")}><p className="whitespace-pre-line text-sm leading-6" dir="auto">{a.cover_note}</p></Card>}
          </div>
        </div>
      )}

      {tab === "cv" && (
        <Card title={a.cv?.file_name} actions={a.cv && <a className="aegov-btn btn-outline btn-sm" href={`${a.cv.url}?download=true`}><DownloadSimple size={20} aria-hidden="true" />{t("app.download_cv")}</a>}>
          {!a.cv ? <Empty /> : a.cv.content_type === "application/pdf" ? (
            <iframe title={a.cv.file_name} src={`/api/backoffice/applicants/${a.id}/cv`} className="h-[75vh] w-full rounded-lg border border-aeblack-100" />
          ) : a.cv.content_type.startsWith("image/") ? (
            <img src={`/api/backoffice/applicants/${a.id}/cv`} alt={a.cv.file_name} className="max-w-full rounded-lg border" />
          ) : <Note>{t("app.download_cv")}</Note>}
        </Card>
      )}

      {tab === "screening" && (
        <Card title={t("app.screening")}>
          {a.screening.answers.length === 0 ? <Empty /> : (
            <dl className="divide-y divide-aeblack-100">
              {a.screening.answers.map((s, i) => (
                <div key={i} className="grid gap-1 py-3 sm:grid-cols-2">
                  <dt className="font-semibold">{s.question}</dt>
                  <dd className="flex flex-wrap items-center gap-2"><span dir="auto">{s.answer || "-"}</span>
                    {s.meets !== null && <Badge tone={s.meets ? "green" : "red"}>{s.meets ? t("app.meets") : t("app.does_not_meet")}</Badge>}</dd>
                </div>
              ))}
            </dl>
          )}
        </Card>
      )}

      {tab === "tests" && (
        <div className="space-y-4">
          {a.invitations.length === 0 ? <Empty /> : a.invitations.map((inv) => (
            <Card key={inv.id} title={inv.title} actions={<>
              <Badge tone={invTone(inv.status)}>{t(`inv.status.${inv.status}`)}</Badge>
              <ActionMenu label={t("common.actions")} items={[
                { label: t("app.reexam"), onClick: () => { setReexamFor(inv.id); setDialog("reexam"); } },
                { label: t("app.cancel_invite"), onClick: () => post(`/invitations/${inv.id}/cancel`, {}, t("common.saved")), disabled: inv.status === "Cancelled" },
              ]} />
            </>}>
              <dl className="mb-4 grid gap-2 text-sm sm:grid-cols-4">
                <div><dt className="text-aeblack-500">{t("app.deadline")}</dt><dd>{date(inv.deadline_at, true)}</dd></div>
                <div><dt className="text-aeblack-500">{t("app.attempts")}</dt><dd dir="ltr" className="text-start">{inv.attempts_used} / {inv.attempts_allowed}</dd></div>
                {inv.personality_model === "None" && <div><dt className="text-aeblack-500">{t("apps.score")}</dt><dd><Score value={inv.score} /></dd></div>}
                <div><dt className="text-aeblack-500">{t("app.result")}</dt><dd>{inv.result ? t(`res.${inv.result}`) : "-"}</dd></div>
              </dl>
              {inv.personality_model !== "None" && (() => {
                const last = [...inv.attempts].reverse().find((at) => at.trait_profile);
                return last ? <div className="mb-4"><TraitProfile profile={parseProfile(last.trait_profile)} summary={a.personality?.[String(inv.id)]} /></div> : null;
              })()}
              {inv.attempts.length > 0 && (
                <ul className="space-y-2">
                  {inv.attempts.map((at) => (
                    <li key={at.id} className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-whitely-100 p-3 text-sm">
                      <span>#{at.attempt_no} · {at.status === "InProgress" ? t("inv.status.InProgress") : at.status === "Submitted" ? t("inv.status.Submitted") : t("inv.status.Marked")}
                        {inv.personality_model === "None" && <>{" · "}<span dir="ltr">{at.percent}%</span></>}{" · "}{t("app.integrity", { tabs: at.tab_switches, pastes: at.pastes })}</span>
                      {at.status !== "InProgress" && <Link className="aegov-btn btn-outline btn-sm" to={to(`/marking/${at.id}`)}>{inv.personality_model === "None" ? t("app.mark") : t("pers.view_answers")}</Link>}
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          ))}
        </div>
      )}

      {tab === "interviews" && <Bookings key={bookingsKey} applicantId={a.id} onChange={reload} />}
      {tab === "interviews" && (
        a.interviews.length === 0 ? <Empty /> : (
          <Table head={[t("int.round"), t("int.date"), t("common.status"), t("int.feedback")]}>
            {a.interviews.map((iv) => (
              <tr key={iv.id}>
                <td><Link to={to(`/interviews/${iv.id}`)}>{iv.round}</Link></td>
                <td>{date(iv.starts_at, true)}</td>
                <td><Badge>{t(`int.status.${iv.status}`)}</Badge></td>
                <td className="tabular-nums">{iv.feedback}</td>
              </tr>
            ))}
          </Table>
        )
      )}

      {tab === "offers" && (
        a.offers.length === 0 ? <Empty /> : (
          <Table head={[t("common.code"), t("offer.position"), t("offer.date"), t("common.status")]}>
            {a.offers.map((o) => (
              <tr key={o.id}>
                <td dir="ltr"><Link to={to(`/offers?open=${o.id}`)}>{o.code}</Link></td>
                <td>{o.position_title}</td>
                <td>{date(o.offer_date)}</td>
                <td><Badge>{t(`offer.status.${o.status}`)}</Badge></td>
              </tr>
            ))}
          </Table>
        )
      )}

      {tab === "notes" && (
        <Card title={t("app.notes")}>
          <form className="mb-6 space-y-3" onSubmit={async (e) => { e.preventDefault(); if (await post(`/applicants/${a.id}/notes`, { text: note }, t("common.saved")) !== undefined) setNote(""); }}>
            <TextArea label={t("app.add_note")} placeholder={t("app.note_placeholder")} value={note} onChange={(e) => setNote(e.target.value)} />
            <Button type="submit" size="sm" busy={act.busy} disabled={!note.trim()}>{t("app.add_note")}</Button>
          </form>
          <ol className="space-y-3">
            {a.notes.map((n) => (
              <li key={n.id} className={`rounded-xl p-3 text-sm ${n.kind === "System" ? "bg-whitely-100 text-aeblack-600" : n.kind === "Correction" ? "bg-camel-50" : "border border-aeblack-100"}`}>
                <p className="mb-1 text-xs text-aeblack-500">{n.author} · {date(n.created_at, true)}</p>
                <p className="whitespace-pre-line" dir="auto">{n.text}</p>
              </li>
            ))}
          </ol>
        </Card>
      )}

      <InviteDialog open={dialog === "invite"} onClose={() => setDialog("")} tests={lk?.assessments || []} busy={act.busy}
        onSave={(body) => post(`/applicants/${a.id}/invite`, body, t("common.saved"))} />
      <ReexamDialog open={dialog === "reexam"} onClose={() => setDialog("")} busy={act.busy}
        onSave={(body) => post(`/invitations/${reexamFor}/reexam`, body, t("common.saved"))} />
      <InterviewDialog open={dialog === "interview"} onClose={() => setDialog("")} lookups={lk} busy={act.busy}
        onSave={(body) => post("/interviews", { ...body, application_id: a.id }, t("common.saved"))} />
      {dialog === "times" && <BookingDialog onClose={() => setDialog("")} lookups={lk} busy={act.busy}
        onSave={async (body) => { const r = await post("/bookings", { ...body, application_id: a.id }, t("book.sent")); if (r !== undefined) { setTab("interviews"); setBookingsKey((k) => k + 1); } }} />}
      <OfferDialog open={dialog === "offer"} onClose={() => setDialog("")} busy={act.busy} defaultTitle={a.job.title}
        onSave={(body) => post("/offers", { ...body, application_id: a.id }, t("common.saved"))} />
    </>
  );
}

function InviteDialog({ open, onClose, tests, onSave, busy }: { open: boolean; onClose: () => void; tests: { id: number; title: string }[]; onSave: (b: unknown) => void; busy: boolean }) {
  const { t } = useLang();
  const [test, setTest] = useState("");
  const [days, setDays] = useState(7);
  return (
    <Modal open={open} onClose={onClose} title={t("app.invite")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
      <Button busy={busy} disabled={!test} onClick={() => onSave({ assessment_id: Number(test), deadline_days: days })}>{t("app.invite")}</Button>
    </>}>
      <SelectInput required label={t("app.test")} value={test} onChange={(e) => setTest(e.target.value)} options={[{ value: "", label: "" }, ...tests.map((x) => ({ value: x.id, label: x.title }))]} />
      <TextInput required label={t("jobs.deadline_days")} type="number" min={1} max={90} value={days} onChange={(e) => setDays(Number(e.target.value) || 7)} />
    </Modal>
  );
}

function ReexamDialog({ open, onClose, onSave, busy }: { open: boolean; onClose: () => void; onSave: (b: unknown) => void; busy: boolean }) {
  const { t } = useLang();
  const [extra, setExtra] = useState(1);
  const [days, setDays] = useState(7);
  const [notify, setNotify] = useState(true);
  return (
    <Modal open={open} onClose={onClose} title={t("app.reexam")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
      <Button busy={busy} onClick={() => onSave({ extra_attempts: extra, deadline_days: days, notify })}>{t("common.confirm")}</Button>
    </>}>
      <TextInput required label={t("inv.extra_attempts")} type="number" min={1} max={5} value={extra} onChange={(e) => setExtra(Number(e.target.value) || 1)} />
      <TextInput required label={t("jobs.deadline_days")} type="number" min={1} max={90} value={days} onChange={(e) => setDays(Number(e.target.value) || 7)} />
      <Tick label={t("inv.notify")} checked={notify} onChange={setNotify} />
    </Modal>
  );
}

export function InterviewDialog({ open, onClose, lookups, onSave, busy, initial }: {
  open: boolean; onClose: () => void; lookups?: { rounds: { id: number; name: string }[]; staff: { id: number; name: string }[] }; onSave: (b: any) => void; busy: boolean;
  initial?: { round_id: number; date: string; from: string; to: string; location: string; notes: string; panelists: number[] };
}) {
  const { t } = useLang();
  const [f, setF] = useState(initial || { round_id: 0, date: "", from: "10:00", to: "11:00", location: "", notes: "", panelists: [] as number[] });
  const set = (k: string, v: unknown) => setF((x) => ({ ...x, [k]: v }));
  return (
    <Modal open={open} onClose={onClose} title={t("app.schedule")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
      <Button busy={busy} disabled={!f.round_id || !f.date} onClick={() => onSave(f)}>{t("common.save")}</Button>
    </>}>
      <SelectInput required label={t("int.round")} value={f.round_id || ""} onChange={(e) => set("round_id", Number(e.target.value))}
        options={[{ value: "", label: "" }, ...(lookups?.rounds || []).map((r) => ({ value: r.id, label: r.name }))]} />
      <div className="grid gap-3 sm:grid-cols-3">
        <TextInput required label={t("int.date")} type="date" value={f.date} onChange={(e) => set("date", e.target.value)} />
        <TextInput required label={t("int.from")} type="time" value={f.from} onChange={(e) => set("from", e.target.value)} />
        <TextInput required label={t("int.to")} type="time" value={f.to} onChange={(e) => set("to", e.target.value)} />
      </div>
      <TextInput label={t("int.location")} value={f.location} onChange={(e) => set("location", e.target.value)} />
      <fieldset>
        <legend className="mb-2 text-sm font-semibold">{t("int.panel")}</legend>
        <div className="grid gap-2 sm:grid-cols-2">
          {(lookups?.staff || []).map((s) => (
            <Tick key={s.id} label={s.name} checked={f.panelists.includes(s.id)}
              onChange={(v) => set("panelists", v ? [...f.panelists, s.id] : f.panelists.filter((x) => x !== s.id))} />
          ))}
        </div>
      </fieldset>
      <TextArea label={t("int.notes")} value={f.notes} onChange={(e) => set("notes", e.target.value)} />
    </Modal>
  );
}

export function OfferDialog({ open, onClose, onSave, busy, defaultTitle, initial }: {
  open: boolean; onClose: () => void; onSave: (b: any) => void; busy: boolean; defaultTitle: string;
  initial?: { position_title: string; offer_date: string; start_date: string; monthly_salary: number | null; currency: string; terms: string; offer_template_id?: number | null };
}) {
  const { t, lang } = useLang();
  const today = new Date().toISOString().slice(0, 10);
  const [f, setF] = useState(initial || { position_title: defaultTitle, offer_date: today, start_date: "", monthly_salary: null as number | null, currency: "AED", terms: "", offer_template_id: null as number | null });
  // Letter templates: the default is used when none is picked.
  const templates = useAsync(() => (open ? api<{ items: { id: number; name_ar: string; name_en: string; is_default: boolean }[] }>("/document-templates?kind=OfferLetter&active=true") : Promise.resolve({ items: [] })), [open]);
  const set = (k: string, v: unknown) => setF((x) => ({ ...x, [k]: v }));
  return (
    <Modal open={open} onClose={onClose} title={t("app.make_offer")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
      <Button busy={busy} disabled={!f.position_title} onClick={() => onSave({ ...f, start_date: f.start_date || null })}>{t("common.save")}</Button>
    </>}>
      <TextInput required label={t("offer.position")} value={f.position_title} onChange={(e) => set("position_title", e.target.value)} />
      <div className="grid gap-3 sm:grid-cols-2">
        <TextInput required label={t("offer.date")} type="date" value={f.offer_date} onChange={(e) => set("offer_date", e.target.value)} />
        <TextInput label={t("offer.start")} type="date" value={f.start_date} onChange={(e) => set("start_date", e.target.value)} />
        <TextInput label={t("offer.salary")} type="number" min={0} value={f.monthly_salary ?? ""} onChange={(e) => set("monthly_salary", e.target.value ? Number(e.target.value) : null)} />
        <TextInput label={t("offer.currency")} value={f.currency} maxLength={3} onChange={(e) => set("currency", e.target.value.toUpperCase())} dir="ltr" />
      </div>
      <TextArea label={t("offer.terms")} rows={6} value={f.terms} onChange={(e) => set("terms", e.target.value)} hint={t("offer.terms_hint")} />
      {(templates.data?.items.length ?? 0) > 0 && (
        <SelectInput label={t("offer.letter_template")} value={f.offer_template_id ?? ""} onChange={(e) => set("offer_template_id", e.target.value ? Number(e.target.value) : null)}
          options={[{ value: "", label: t("offer.template_default") }, ...(templates.data?.items || []).map((x) => ({ value: x.id, label: lang === "en" ? x.name_en : x.name_ar }))]} />
      )}
    </Modal>
  );
}

type Booking = { id: number; status: string; location: string; round: string; interview_id: number | null; created_at: string; slots: { id: number; starts_at: string; ends_at: string }[] };

/** Interview times offered to the candidate, waiting for their pick or already picked. */
function Bookings({ applicantId, onChange }: { applicantId: number; onChange: () => void }) {
  const { t, date, to } = useLang();
  const toast = useToast();
  const list = useAsync(() => api<Booking[]>(`/applicants/${applicantId}/bookings`), [applicantId]);
  const act = useSaving();
  const items = (list.data || []).filter((b) => b.status !== "Cancelled");
  if (items.length === 0) return null;
  const cancel = async (id: number) => {
    if (await act.run(() => api(`/bookings/${id}/cancel`, { body: {} })) !== undefined) { toast(t("common.saved")); list.reload(); onChange(); }
  };
  return (
    <Card title={t("book.title")} className="mb-6">
      {act.error && <div className="mb-3"><ErrorState error={act.error} /></div>}
      <ul className="space-y-3">
        {items.map((b) => (
          <li key={b.id} className="rounded-xl border border-aeblack-100 p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="font-semibold">{b.round}{b.location && <span className="font-normal text-aeblack-600"> · {b.location}</span>}</p>
              <span className="flex items-center gap-2">
                <Badge tone={b.status === "Open" ? "amber" : "green"}>{t(`book.status.${b.status}`)}</Badge>
                {b.status === "Open" && <Button variant="link" size="sm" busy={act.busy} onClick={() => cancel(b.id)}>{t("book.withdraw")}</Button>}
                {b.interview_id && <Link className="text-sm text-primary-700" to={to(`/interviews/${b.interview_id}`)}>{t("book.open_interview")}</Link>}
              </span>
            </div>
            <ul className="mt-2 flex flex-wrap gap-2 text-sm">
              {b.slots.map((s) => <li key={s.id} className="rounded-lg bg-whitely-100 px-2 py-1" dir="auto">{date(s.starts_at, true)}</li>)}
            </ul>
          </li>
        ))}
      </ul>
    </Card>
  );
}

/** Offer the candidate several interview times to choose from (in the portal); the round, place and panel are set here. */
function BookingDialog({ onClose, lookups, onSave, busy }: {
  onClose: () => void; lookups?: { rounds: { id: number; name: string }[]; staff: { id: number; name: string }[] }; onSave: (b: any) => void; busy: boolean;
}) {
  const { t } = useLang();
  const [f, setF] = useState({ round_id: 0, location: "", panelists: [] as number[], slots: [{ date: "", from: "10:00", to: "11:00" }] });
  const setSlot = (i: number, k: string, v: string) => setF((x) => ({ ...x, slots: x.slots.map((s, j) => (j === i ? { ...s, [k]: v } : s)) }));
  const ready = !!f.round_id && f.slots.length > 0 && f.slots.every((s) => s.date && s.from && s.to);
  return (
    <Modal open onClose={onClose} title={t("book.offer_times")} footer={<>
      <Button variant="outline" onClick={onClose}>{t("common.cancel")}</Button>
      <Button busy={busy} disabled={!ready} onClick={() => onSave(f)}>{t("book.send")}</Button>
    </>}>
      <p className="text-sm text-aeblack-600">{t("book.hint")}</p>
      <SelectInput required label={t("int.round")} value={f.round_id || ""} onChange={(e) => setF({ ...f, round_id: Number(e.target.value) })}
        options={[{ value: "", label: "" }, ...(lookups?.rounds || []).map((r) => ({ value: r.id, label: r.name }))]} />
      <fieldset>
        <legend className="mb-2 text-sm font-semibold">{t("book.times")}</legend>
        <div className="space-y-2">
          {f.slots.map((s, i) => (
            <div key={i} className="grid grid-cols-[1fr_auto] items-end gap-2 sm:grid-cols-[1fr_7rem_7rem_auto]">
              <TextInput required label={t("int.date")} type="date" value={s.date} onChange={(e) => setSlot(i, "date", e.target.value)} wrapClass="col-span-2 sm:col-span-1" />
              <TextInput required label={t("int.from")} type="time" value={s.from} onChange={(e) => setSlot(i, "from", e.target.value)} />
              <TextInput required label={t("int.to")} type="time" value={s.to} onChange={(e) => setSlot(i, "to", e.target.value)} />
              <Button variant="link" size="sm" aria-label={t("common.remove")} disabled={f.slots.length === 1}
                onClick={() => setF({ ...f, slots: f.slots.filter((_, j) => j !== i) })}><Trash size={20} aria-hidden="true" /></Button>
            </div>
          ))}
        </div>
        {f.slots.length < 10 && (
          <Button variant="outline" size="sm" className="mt-2" icon={<Plus size={20} aria-hidden="true" />}
            onClick={() => setF({ ...f, slots: [...f.slots, { ...f.slots[f.slots.length - 1] }] })}>{t("book.add_time")}</Button>
        )}
      </fieldset>
      <TextInput label={t("int.location")} value={f.location} onChange={(e) => setF({ ...f, location: e.target.value })} />
      <fieldset>
        <legend className="mb-2 text-sm font-semibold">{t("int.panel")}</legend>
        <div className="grid gap-2 sm:grid-cols-2">
          {(lookups?.staff || []).map((s) => (
            <Tick key={s.id} label={s.name} checked={f.panelists.includes(s.id)}
              onChange={(v) => setF({ ...f, panelists: v ? [...f.panelists, s.id] : f.panelists.filter((x) => x !== s.id) })} />
          ))}
        </div>
      </fieldset>
    </Modal>
  );
}
