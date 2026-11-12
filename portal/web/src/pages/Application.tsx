import { Briefcase, CalendarCheck, Certificate, ChatText, Check, GraduationCap, Hourglass, Icon, PaperPlaneTilt, Toolbox, Translate } from "@phosphor-icons/react";
import { FormEvent, ReactNode, useEffect, useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import { api, ApiError, ApplicationDetail, Booking, Details } from "../api";
import { IconBox, StatusBadge, Steps } from "../components/Bits";
import { Breadcrumb } from "../components/Breadcrumb";
import { Alert, Skeleton, Spinner } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";
import NotFound from "./NotFound";

function Progress({ code }: { code: string }) {
  const { t } = useSite();
  const reached = code === "under_review" || code === "contacted" ? 2 : code === "interview" ? 3 : code === "successful" || code === "not_selected" ? 4 : 1;
  const stages = [t("app.stage_submitted"), t("app.stage_review"), t("app.stage_interview"), t("app.stage_outcome")];
  return (
    <ol className="mt-4 grid grid-cols-4 gap-2" aria-label={t("app.progress")}>
      {stages.map((s, i) => (
        <li key={s} className="flex flex-col gap-2" aria-current={i + 1 === reached ? "step" : undefined}>
          <span className={`h-1.5 rounded-full ${i < reached ? "bg-primary-600" : "bg-aeblack-100"}`} aria-hidden="true" />
          <span className={`flex items-center gap-1 text-xs sm:text-sm ${i + 1 === reached ? "font-semibold text-aeblack-900" : "text-aeblack-600"}`}>
            {i + 1 < reached && <Check size={16} weight="bold" className="text-primary-600" aria-hidden="true" />}{s}
          </span>
        </li>
      ))}
    </ol>
  );
}

function DetailsView({ d }: { d: Details }) {
  const { t } = useSite();
  const none = <span className="text-aeblack-500">{t("app.not_found_in_cv")}</span>;
  const row = (icon: Icon, label: string, value: ReactNode) => (
    <div className="grid gap-2 border-b border-aeblack-100 py-4 last:border-0 md:grid-cols-3 md:items-start">
      <dt className="flex items-center gap-3 text-sm font-medium text-aeblack-700"><IconBox icon={icon} />{label}</dt>
      <dd className="md:col-span-2">{value}</dd>
    </div>
  );
  return (
    <dl className="mt-4">
      {row(Hourglass, t("app.years"), d.years ? t("app.years_value", { years: d.years }) : none)}
      {row(Briefcase, t("app.experience"), d.work.length ? (
        <ul className="space-y-2">{d.work.map((w, i) => (
          <li key={i}><bdi className="block font-medium">{[w.position, w.employer].filter(Boolean).join(", ")}</bdi>
            {w.start && <span className="block text-sm text-aeblack-600"><bdi>{w.start}</bdi> - <bdi>{w.end || t("app.present")}</bdi></span>}</li>
        ))}</ul>) : none)}
      {row(GraduationCap, t("app.education"), d.education.length ? <ul className="space-y-2">{d.education.map((e, i) => <li key={i}><bdi>{[e.degree, e.institution, e.end].filter(Boolean).join(", ")}</bdi></li>)}</ul> : none)}
      {row(Toolbox, t("app.skills"), d.skills.length ? <div className="flex flex-wrap gap-2">{d.skills.map((s) => <span key={s} className="aegov-badge badge-sm">{s}</span>)}</div> : none)}
      {row(Translate, t("app.languages"), d.languages.length ? <bdi>{d.languages.join(", ")}</bdi> : none)}
      {row(Certificate, t("app.certificates"), d.certificates.length ? <bdi>{d.certificates.join(", ")}</bdi> : none)}
    </dl>
  );
}

/** The interview times HR offered: the candidate picks one and it is booked. */
function ChooseTime({ booking, appId, onBooked }: { booking: Booking; appId: string; onBooked: (msg: string) => void }) {
  const { t, formatDate, errorText } = useSite();
  const [slot, setSlot] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const book = async (e: FormEvent) => {
    e.preventDefault();
    if (slot === null) return;
    setBusy(true);
    setError(null);
    try {
      const r = await api<{ date: string; from: string; to: string }>(`/api/applications/${encodeURIComponent(appId)}/book`, { body: { slot } });
      onBooked(t("book.booked", { date: formatDate(r.date), from: r.from, to: r.to }));
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="mc-surface mt-6 rounded-2xl border-2 border-primary-200 bg-white p-6" aria-labelledby="book-title">
      <h2 id="book-title" className="flex items-center gap-3 font-heading text-lg font-semibold text-aeblack-900"><IconBox icon={CalendarCheck} />{t("book.title")}</h2>
      <p className="mt-2 text-aeblack-700">{t("book.intro", { round: booking.round })}</p>
      {booking.location && <p className="mt-1 text-sm text-aeblack-600">{t("book.location", { location: booking.location })}</p>}
      <form onSubmit={book} className="mt-4 space-y-4">
        <fieldset>
          <legend className="sr-only">{t("book.title")}</legend>
          <div className="grid gap-2 sm:grid-cols-2">
            {booking.slots.map((s) => (
              <label key={s.id} className={`flex cursor-pointer items-center gap-3 rounded-xl border p-3 ${slot === s.id ? "border-primary-600 bg-primary-50" : "border-aeblack-200"}`}>
                <input type="radio" name="slot" className="h-5 w-5 accent-primary-600" checked={slot === s.id} onChange={() => setSlot(s.id)} />
                <span><span className="block font-medium">{formatDate(s.date)}</span><span className="text-sm text-aeblack-600" dir="ltr">{s.from} - {s.to}</span></span>
              </label>
            ))}
          </div>
        </fieldset>
        {error ? <Alert tone="error" title={errorText(error)} /> : null}
        <button type="submit" className="aegov-btn" disabled={slot === null || busy}>{t("book.confirm")}</button>
      </form>
    </section>
  );
}

export default function Application() {
  const { id = "" } = useParams();
  const [params] = useSearchParams();
  const { t, lang, path, formatDate, errorText } = useSite();
  const { data, error, reload } = useAsync((signal) => api<ApplicationDetail>(`/api/applications/${encodeURIComponent(id)}?lang=${lang}`, { signal }), [id, lang]);
  usePageTitle(data?.title || t("apps.title"));
  const [polls, setPolls] = useState(0);
  const [note, setNote] = useState("");
  const [sent, setSent] = useState(false);
  const [noteError, setNoteError] = useState<unknown>(null);
  const [booked, setBooked] = useState("");

  // While the CV details are being prepared, check again every few seconds (up to 3 minutes).
  useEffect(() => {
    if (data?.details_state !== "preparing" || polls >= 45) return;
    const timer = setTimeout(() => { setPolls((n) => n + 1); reload(); }, 4000);
    return () => clearTimeout(timer);
  }, [data, polls, reload]);

  if (error instanceof ApiError && error.status === 404) return <NotFound />;
  if (error && !data) return <div className="container mx-auto max-w-4xl px-4 py-10"><Alert tone="error" title={errorText(error)} /></div>;
  if (!data) return <div className="container mx-auto max-w-4xl space-y-4 px-4 py-10"><Skeleton className="h-10 w-1/2" /><Skeleton className="h-40" /><Skeleton className="h-72" /></div>;

  const sendNote = async (e: FormEvent) => {
    e.preventDefault();
    setNoteError(null);
    try {
      await api(`/api/applications/${encodeURIComponent(id)}/corrections`, { body: { note: note.trim() } });
      setNote("");
      setSent(true);
    } catch (err) {
      setNoteError(err);
    }
  };

  return (
    <div className="container mx-auto max-w-4xl px-4 py-10">
      <Steps current={3} />
      {params.get("new") === "1" && (
        <div className="mb-6"><Alert tone="success" title={t("app.success_title")}>{t("app.success_body")}</Alert></div>
      )}
      <Breadcrumb items={[{ label: t("apps.title"), to: path("/applications") }, { label: data.title }]} />
      <h1 className="font-heading text-3xl font-bold text-aeblack-950">{data.title}</h1>

      {booked && <div className="mt-6" role="status"><Alert tone="success" title={booked} /></div>}
      {data.booking && !booked && <ChooseTime booking={data.booking} appId={id} onBooked={(m) => { setBooked(m); reload(); }} />}

      <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6" aria-labelledby="status-title">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="status-title" className="font-heading text-lg font-semibold text-aeblack-900">{t("app.status")}</h2>
          <StatusBadge status={data.status} large />
        </div>
        <Progress code={data.status.code} />
        <dl className="mt-6 grid gap-4 border-t border-aeblack-100 pt-4 sm:grid-cols-2">
          <div><dt className="text-sm text-aeblack-600">{t("app.submitted_on")}</dt><dd className="font-medium">{formatDate(data.submitted)}</dd></div>
          <div><dt className="text-sm text-aeblack-600">{t("app.reference")}</dt><dd className="font-medium" dir="ltr">{data.reference}</dd></div>
        </dl>
      </section>

      <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6" aria-labelledby="details-title">
        <h2 id="details-title" className="font-heading text-lg font-semibold text-aeblack-900">{t("app.details_title")}</h2>
        <p className="mt-1 text-aeblack-600">{t("app.details_intro")}</p>
        <div aria-live="polite">
          {data.details_state === "ready" && data.details ? <DetailsView d={data.details} />
            : data.details_state === "preparing" && polls < 45 ? <div className="mt-6 text-aeblack-700"><Spinner label={t("app.preparing")} /></div>
            : <div className="mt-6"><Alert tone="info">{t("app.unavailable")}</Alert></div>}
        </div>
        <form onSubmit={sendNote} className="mt-6 space-y-3 border-t border-aeblack-100 pt-6" noValidate>
          <div className="aegov-form-control">
            <label htmlFor="note" className="flex items-center gap-2"><ChatText size={24} className="text-primary-600" aria-hidden="true" />{t("app.correction_label")}</label>
            <div className="form-control-input"><textarea id="note" rows={3} maxLength={2000} value={note} onChange={(e) => { setNote(e.target.value); setSent(false); }} /></div>
          </div>
          {noteError ? <Alert tone="error" title={errorText(noteError)} /> : null}
          <button type="submit" className="aegov-btn btn-outline btn-sm" disabled={note.trim().length < 3}><PaperPlaneTilt size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("app.correction_send")}</button>
          {sent && <p className="text-aegreen-700" role="status">{t("app.correction_sent")}</p>}
        </form>
      </section>
    </div>
  );
}
