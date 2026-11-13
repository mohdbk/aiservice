import { CaretDown, CaretLeft, CaretRight, Check, CheckCircle, CloudArrowUp, CloudCheck, CloudSlash, FileText, Flag, GridFour, Timer, UploadSimple, WarningCircle, X } from "@phosphor-icons/react";
import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api, ApiError, AsmQuestion, AsmState, fileToBase64 } from "../api";
import { Alert, FullPageLoading } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { formatClock } from "./assessmentBits";

type Save = "idle" | "saving" | "saved" | "failed";

function countWords(s: string) {
  return (s.match(/\S+/g) || []).length;
}

function Answer({ q, value, onChange, onUpload, onPicked, disabled }: {
  q: AsmQuestion; value: unknown; onChange: (v: unknown, immediate?: boolean) => void; onUpload: (f: File) => void; onPicked?: () => void; disabled: boolean;
}) {
  const { t } = useSite();
  // Only a tap or click moves on: arrow keys also change a radio, and moving then would skip questions.
  const pointer = useRef(false);
  const pick = (key: string) => { onChange(key, true); if (pointer.current) onPicked?.(); pointer.current = false; };
  const tap = { onPointerDown: () => { pointer.current = true; }, onKeyDown: () => { pointer.current = false; } };
  const radio = "flex cursor-pointer items-start gap-3 rounded-xl border-2 p-4 transition hover:border-primary-400";
  if (q.type === "Single Choice" || q.type === "True or False") {
    return (
      <fieldset className="space-y-3"><legend className="sr-only">{t("asm.your_answer")}</legend>
        {q.options.map((o) => (
          <label key={o.key} {...tap} className={`${radio} ${value === o.key ? "border-primary-600 bg-primary-50" : "border-aeblack-100 bg-white"}`}>
            <input type="radio" name={q.key} className="mt-1 h-5 w-5 rounded-full accent-primary-600" checked={value === o.key} disabled={disabled} onChange={() => pick(o.key)} />
            <span>{o.text}</span>
          </label>
        ))}
      </fieldset>
    );
  }
  if (q.type === "Agreement Scale") {
    // Five points from strongly disagree to strongly agree, side by side on wide screens and stacked on phones.
    return (
      <fieldset><legend className="sr-only">{t("asm.your_answer")}</legend>
        <div className="grid gap-2 sm:grid-cols-5">
          {q.options.map((o) => (
            <label key={o.key} {...tap} className={`flex cursor-pointer items-center gap-3 rounded-xl border-2 p-3 text-sm transition hover:border-primary-400 sm:flex-col sm:justify-center sm:text-center ${value === o.key ? "border-primary-600 bg-primary-50" : "border-aeblack-100 bg-white"}`}>
              <input type="radio" name={q.key} className="h-5 w-5 accent-primary-600" checked={value === o.key} disabled={disabled} onChange={() => pick(o.key)} />
              <span>{o.text}</span>
            </label>
          ))}
        </div>
      </fieldset>
    );
  }
  if (q.type === "Multiple Choice") {
    const vals = Array.isArray(value) ? (value as string[]) : [];
    return (
      <fieldset className="space-y-3"><legend className="mb-2 text-sm text-aeblack-600">{t("asm.select_all")}</legend>
        {q.options.map((o) => {
          const on = vals.includes(o.key);
          return (
            <label key={o.key} {...tap} className={`${radio} ${on ? "border-primary-600 bg-primary-50" : "border-aeblack-100 bg-white"}`}>
              <input type="checkbox" className="mt-1 h-5 w-5 rounded accent-primary-600" checked={on} disabled={disabled}
                onChange={() => onChange(on ? vals.filter((v) => v !== o.key) : [...vals, o.key], true)} />
              <span>{o.text}</span>
            </label>
          );
        })}
      </fieldset>
    );
  }
  if (q.type === "Short Answer" || q.type === "Number") {
    return (
      <div className="aegov-form-control">
        <label htmlFor={`a-${q.key}`}>{t("asm.your_answer")}</label>
        <div className="form-control-input">
          <input id={`a-${q.key}`} inputMode={q.type === "Number" ? "decimal" : "text"} dir={q.type === "Number" ? "ltr" : "auto"}
            value={(value as string) || ""} disabled={disabled} maxLength={q.type === "Number" ? 40 : 500} onChange={(e) => onChange(e.target.value)} />
        </div>
      </div>
    );
  }
  if (q.type === "Essay") {
    const text = (value as string) || "";
    const words = countWords(text);
    const over = q.max_words > 0 && words > q.max_words;
    return (
      <div className="aegov-form-control">
        <label htmlFor={`a-${q.key}`}>{t("asm.your_answer")}</label>
        <div className="form-control-input"><textarea id={`a-${q.key}`} rows={12} dir="auto" value={text} disabled={disabled} onChange={(e) => onChange(e.target.value)} /></div>
        <p className={`mt-1 text-sm ${over ? "font-semibold text-aered-700" : "text-aeblack-600"}`} aria-live="polite">
          {t("asm.words", { count: words })}{q.max_words ? ` · ${t("asm.word_limit", { max: q.max_words })}` : ""}
        </p>
      </div>
    );
  }
  if (q.type === "File Upload") {
    const f = value as { name?: string } | null;
    return (
      <div>
        <input id={`a-${q.key}`} type="file" className="peer sr-only" disabled={disabled} accept=".pdf,.docx,.xlsx,.pptx,.png,.jpg,.jpeg,.zip"
          onChange={(e) => { const file = e.target.files?.[0]; if (file) onUpload(file); }} />
        <label htmlFor={`a-${q.key}`} className="flex cursor-pointer flex-col items-center gap-2 rounded-xl border-2 border-dashed border-primary-300 bg-whitely-50 px-4 py-8 text-center hover:border-primary-500 peer-focus-visible:ring-2 peer-focus-visible:ring-primary-600">
          <span className="flex h-14 w-14 items-center justify-center rounded-full bg-primary-100 text-primary-700" aria-hidden="true">
            {f?.name ? <FileText size={32} weight="duotone" /> : <UploadSimple size={32} weight="duotone" />}
          </span>
          <span className="font-semibold text-primary-800"><bdi>{f?.name ? t("asm.uploaded", { name: f.name }) : t("asm.upload")}</bdi></span>
          <span className="text-sm text-aeblack-600">{t("asm.upload_hint")}</span>
        </label>
      </div>
    );
  }
  return null;
}

export default function TakeAssessment() {
  const { id = "" } = useParams();
  const { t, lang, path, errorText, config } = useSite();
  const navigate = useNavigate();
  const [st, setSt] = useState<AsmState | null>(null);
  const [answers, setAnswers] = useState<Record<string, unknown>>({});
  const [index, setIndex] = useState(0);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [save, setSave] = useState<Save>("idle");
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState("");
  const [confirm, setConfirm] = useState(false);
  const [moving, setMoving] = useState(false);
  const timers = useRef<Record<string, number>>({});
  // Questions the candidate marked to come back to (kept in this browser tab only) and the review panel.
  const flagKey = `asm-flags-${id}`;
  const [flags, setFlags] = useState<string[]>(() => { try { return JSON.parse(sessionStorage.getItem(flagKey) || "[]"); } catch { return []; } });
  useEffect(() => { try { sessionStorage.setItem(flagKey, JSON.stringify(flags)); } catch { /* not kept */ } }, [flagKey, flags]);
  const toggleFlag = (k: string) => setFlags((f) => (f.includes(k) ? f.filter((x) => x !== k) : [...f, k]));
  const [review, setReview] = useState(false);
  // Time warnings for screen readers, once each, at five minutes and one minute left.
  const [warn, setWarn] = useState("");
  const warned = useRef<Set<string>>(new Set());
  const sectionRef = useRef(-1);
  const base = `/api/assessments/${encodeURIComponent(id)}`;
  usePageTitle(st?.section.title || t("asm.title"));

  const apply = useCallback((s: AsmState) => {
    if (s.finished) {
      navigate(path(`/assessments/${encodeURIComponent(id)}`), { replace: true });
      return;
    }
    if (sectionRef.current !== -1 && s.section_index !== sectionRef.current) {
      setIndex(0);
      setConfirm(false);
    }
    sectionRef.current = s.section_index;
    setSt(s);
    setAnswers(s.answers || {});
    setRemaining(s.section.remaining_seconds);
  }, [id, navigate, path]);

  const load = useCallback(async () => {
    try {
      apply(await api<AsmState>(`${base}/state?lang=${lang}`));
    } catch (e) {
      setError(e);
    }
  }, [apply, base, lang]);

  useEffect(() => { load(); }, [load]);

  // Countdown: when the section's time is up the server moves on; fetch the new state.
  useEffect(() => {
    if (remaining === null) return;
    if (remaining <= 0) {
      setNotice(t("asm.time_up"));
      load();
      return;
    }
    const timer = setTimeout(() => setRemaining((r) => (r === null ? r : r - 1)), 1000);
    return () => clearTimeout(timer);
  }, [remaining, load, t]);

  // Integrity signals: logged, never blocking.
  useEffect(() => {
    const onVis = () => { if (document.visibilityState === "hidden") api(`${base}/event`, { body: { kind: "hidden" } }).catch(() => {}); };
    const onPaste = () => { api(`${base}/event`, { body: { kind: "paste" } }).catch(() => {}); };
    document.addEventListener("visibilitychange", onVis);
    document.addEventListener("paste", onPaste);
    return () => { document.removeEventListener("visibilitychange", onVis); document.removeEventListener("paste", onPaste); };
  }, [base]);

  const persist = useCallback(async (key: string, value: unknown) => {
    if (!st) return;
    setSave("saving");
    try {
      await api(`${base}/answer`, { body: { key, answer: value ?? null, section: st.section_index } });
      setSave("saved");
    } catch (e) {
      setSave("failed");
      if (e instanceof ApiError && (e.code === "section_closed" || e.code === "question_closed")) { setNotice(t("error.section_closed")); load(); }
      else if (e instanceof ApiError && e.code !== "connection") setError(e);
    }
  }, [st, base, load, t]);

  const [autoNext, setAutoNext] = useState(() => { try { return localStorage.getItem("asm.auto_next") !== "0"; } catch { return true; } });
  const toggleAuto = (v: boolean) => { setAutoNext(v); try { localStorage.setItem("asm.auto_next", v ? "1" : "0"); } catch { /* not kept */ } };
  /** After a choice is tapped, show the next question (a short pause so the choice is seen). Not after the last question. */
  const advance = (from: number) => {
    if (!autoNext) return;
    window.setTimeout(() => {
      setIndex((i) => (i === from && i < (st?.section.questions.length ?? 0) - 1 ? i + 1 : i));
      window.requestAnimationFrame(() => document.getElementById("question-title")?.focus({ preventScroll: false }));
    }, 350);
  };

  const change = (key: string, value: unknown, immediate = false) => {
    setAnswers((a) => ({ ...a, [key]: value }));
    window.clearTimeout(timers.current[key]);
    timers.current[key] = window.setTimeout(() => persist(key, value), immediate ? 0 : 800);
  };

  const upload = async (key: string, file: File) => {
    if (!st) return;
    setSave("saving");
    try {
      const data = await fileToBase64(file);
      const r = await api<{ name: string }>(`${base}/file`, { body: { key, section: st.section_index, file: { name: file.name, type: file.type, data } } });
      setAnswers((a) => ({ ...a, [key]: { name: r.name } }));
      setSave("saved");
    } catch (e) {
      setSave("failed");
      setError(e);
    }
  };

  const finishSection = async () => {
    if (!st) return;
    setMoving(true);
    for (const k of Object.keys(timers.current)) window.clearTimeout(timers.current[k]);
    await Promise.all(Object.entries(answers).filter(([k]) => st.section.questions.some((q) => q.key === k)).map(([k, v]) =>
      api(`${base}/answer`, { body: { key: k, answer: v ?? null, section: st.section_index } }).catch(() => {})));
    try {
      apply(await api<AsmState>(`${base}/next`, { body: { section: st.section_index, lang } }));
      setNotice("");
    } catch (e) {
      setError(e);
    } finally {
      setMoving(false);
      setConfirm(false);
    }
  };

  useEffect(() => {
    if (remaining === null) return;
    const key = remaining <= 60 ? "1" : remaining <= 300 ? "5" : "";
    if (key && !warned.current.has(`${sectionRef.current}-${key}`)) {
      warned.current.add(`${sectionRef.current}-${key}`);
      setWarn(t(key === "1" ? "asm.one_minute" : "asm.five_minutes"));
    }
  }, [remaining, t]);
  const go = (i: number) => {
    setIndex(i);
    setReview(false);
    setConfirm(false);
    window.requestAnimationFrame(() => document.getElementById("question-title")?.focus());
  };

  if (error && !st) return <div className="container mx-auto max-w-3xl px-4 py-10"><Alert tone="error" title={errorText(error)} /></div>;
  if (!st) return <FullPageLoading />;

  // Questions are in the language the candidate chose; buttons and messages stay in the portal's language.
  const cl = st.language || lang;
  const cdir = config.languages.find((l) => l.code === cl)?.dir || (cl === "ar" ? "rtl" : "ltr");
  const qs = st.section.questions;
  const q = qs[Math.min(index, qs.length - 1)];
  const answered = (k: string) => { const v = answers[k]; return !(v === undefined || v === null || v === "" || (Array.isArray(v) && !v.length)); };
  const unanswered = qs.filter((x) => !answered(x.key)).length;
  const last = st.section_index === st.section_count - 1;
  const low = remaining !== null && remaining <= 60;
  const soon = remaining !== null && remaining <= 300;
  const done = qs.length - unanswered;
  const flagged = qs.map((x, i) => ({ x, i })).filter(({ x }) => flags.includes(x.key));
  const missing = qs.map((x, i) => ({ x, i })).filter(({ x }) => !answered(x.key));
  const SaveIcon = save === "saving" ? CloudArrowUp : save === "failed" ? CloudSlash : CloudCheck;

  return (
    <div className="container mx-auto max-w-4xl px-4 py-6">
      {/* Progress header: section, time, saving, and how far through the section the candidate is. Stays at the top. */}
      <div className="mc-surface sticky top-[4.5rem] z-10 mb-4 rounded-2xl border border-aeblack-100 bg-white/95 shadow-sm backdrop-blur md:top-[8.5rem]">
        <div className="flex flex-wrap items-center justify-between gap-3 p-4 pb-3">
          <div className="min-w-0">
            <p className="text-sm text-aeblack-600">{t("asm.section_of", { n: st.section_index + 1, count: st.section_count })}</p>
            <p className="truncate font-heading text-lg font-semibold text-aeblack-900" lang={cl} dir={cdir}>{st.section.title}</p>
          </div>
          <div className="flex items-center gap-3">
            <span className={`inline-flex items-center gap-1.5 text-sm ${save === "failed" ? "text-aered-700" : "text-aeblack-600"}`} aria-live="polite">
              <SaveIcon size={20} aria-hidden="true" /><span className="hidden sm:inline">{save === "saving" ? t("asm.saving") : save === "failed" ? t("asm.save_failed") : save === "saved" ? t("asm.saved") : ""}</span>
            </span>
            {remaining !== null && (
              <span className={`inline-flex items-center gap-2 rounded-full px-4 py-2 font-mono text-lg font-bold transition-colors ${low ? "motion-safe:animate-pulse bg-aered-50 text-aered-700" : soon ? "bg-camel-50 text-camel-900" : "bg-primary-50 text-primary-800"}`}
                role="timer" aria-label={t("asm.time_left")}>
                <Timer size={24} aria-hidden="true" /><span dir="ltr">{formatClock(remaining)}</span>
              </span>
            )}
          </div>
        </div>
        <div className="flex items-center gap-3 border-t border-aeblack-100 px-4 py-2.5">
          <div className="min-w-0 flex-1">
            <div className="mb-1.5 flex flex-wrap items-center justify-between gap-x-3 text-xs font-semibold text-aeblack-600">
              <span>{t("asm.question_of", { n: index + 1, count: qs.length })}</span>
              <span>{t("asm.answered_count", { answered: done, count: qs.length })}</span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-aeblack-100" role="progressbar" aria-valuemin={0} aria-valuemax={qs.length} aria-valuenow={done}
              aria-label={t("asm.answered_count", { answered: done, count: qs.length })}>
              <div className="h-full rounded-full bg-primary-500 transition-[width] duration-300" style={{ width: `${qs.length ? (done / qs.length) * 100 : 0}%` }} />
            </div>
          </div>
          <button type="button" onClick={() => setReview((r) => !r)} aria-expanded={review} aria-controls="asm-review"
            className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-primary-200 px-3 py-1.5 text-sm font-semibold text-primary-800 hover:bg-primary-50">
            <GridFour size={18} aria-hidden="true" /><span className="hidden sm:inline">{t("asm.review")}</span>
            {flagged.length > 0 && <span className="inline-flex items-center gap-0.5 rounded-full bg-camel-100 px-1.5 text-xs text-camel-900"><Flag size={12} aria-hidden="true" />{flagged.length}</span>}
            <CaretDown size={14} className={`transition ${review ? "rotate-180" : ""}`} aria-hidden="true" />
          </button>
        </div>
      </div>
      <p className="sr-only" role="status" aria-live="assertive">{warn}</p>

      {review && (
        <>
          <div className="fixed inset-0 z-40 bg-aeblack-950/40 md:hidden" aria-hidden="true" onClick={() => setReview(false)} />
          <div id="asm-review" role="region" aria-label={t("asm.review")}
            className="fixed inset-x-0 bottom-0 z-50 max-h-[75vh] overflow-y-auto rounded-t-3xl bg-white p-5 shadow-2xl md:static md:z-auto md:mb-4 md:max-h-none md:rounded-2xl md:border md:border-aeblack-100 md:shadow-sm">
            <div className="mb-3 flex items-center justify-between gap-3">
              <h2 className="font-heading text-base font-semibold">{t("asm.review")}</h2>
              <button type="button" className="rounded-full p-1.5 text-aeblack-600 hover:bg-aeblack-50" aria-label={t("asm.close")} onClick={() => setReview(false)}><X size={20} /></button>
            </div>
            <QuestionGrid qs={qs} index={index} answered={answered} flags={flags} onGo={go} t={t} />
            <ul className="mt-4 flex flex-wrap gap-x-4 gap-y-2 text-xs text-aeblack-600">
              <li className="inline-flex items-center gap-1.5"><span className="h-3.5 w-3.5 rounded bg-primary-600" aria-hidden="true" />{t("asm.legend_current")}</li>
              <li className="inline-flex items-center gap-1.5"><span className="h-3.5 w-3.5 rounded border-2 border-primary-300 bg-primary-50" aria-hidden="true" />{t("asm.legend_answered")}</li>
              <li className="inline-flex items-center gap-1.5"><span className="h-3.5 w-3.5 rounded border-2 border-aeblack-200 bg-white" aria-hidden="true" />{t("asm.legend_unanswered")}</li>
              <li className="inline-flex items-center gap-1.5"><Flag size={14} className="text-camel-700" aria-hidden="true" />{t("asm.flagged")}</li>
            </ul>
          </div>
        </>
      )}

      {notice && <div className="mb-4"><Alert tone="warning" title={notice} /></div>}
      {error ? <div className="mb-4"><Alert tone="error" title={errorText(error)} /></div> : null}
      {st.section.instructions && index === 0 && <div className="mb-4"><Alert tone="info"><span lang={cl} dir={cdir} className="block">{st.section.instructions}</span></Alert></div>}

      <section className="mc-surface rounded-2xl border border-aeblack-100 bg-white p-6 md:p-8" aria-labelledby="question-title">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 text-sm text-aeblack-600">
          <span className="font-semibold">{t("asm.question_of", { n: index + 1, count: qs.length })}{q.type !== "Agreement Scale" && <span className="font-normal"> · {t("asm.points", { points: q.points })}</span>}</span>
          <button type="button" onClick={() => toggleFlag(q.key)} aria-pressed={flags.includes(q.key)}
            className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold transition ${flags.includes(q.key) ? "border-camel-300 bg-camel-50 text-camel-900" : "border-aeblack-200 text-aeblack-600 hover:border-camel-300 hover:text-camel-900"}`}>
            <Flag size={14} aria-hidden="true" />{flags.includes(q.key) ? t("asm.flagged") : t("asm.flag")}
          </button>
        </div>
        <div id="question-title" tabIndex={-1} className="mc-prose mb-6 outline-none text-lg font-medium text-aeblack-900" lang={cl} dir={cdir} dangerouslySetInnerHTML={{ __html: q.text }} />
        <div lang={cl} dir={cdir}><Answer q={q} value={answers[q.key]} disabled={moving} onChange={(v, im) => change(q.key, v, im)} onUpload={(f) => upload(q.key, f)} onPicked={() => advance(index)} /></div>
      </section>

      <label className="mt-4 flex w-fit cursor-pointer items-center gap-2 text-sm text-aeblack-700">
        <input type="checkbox" className="h-4 w-4 accent-primary-600" checked={autoNext} onChange={(e) => toggleAuto(e.target.checked)} />
        {t("asm.auto_next")}
      </label>

      <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
        <button type="button" className="aegov-btn btn-outline" disabled={index === 0} onClick={() => setIndex((i) => i - 1)}>
          <CaretLeft size={20} className="rtl:-scale-x-100" aria-hidden="true" />{t("asm.previous")}
        </button>
        {index < qs.length - 1 ? (
          <button type="button" className="aegov-btn" onClick={() => setIndex((i) => i + 1)}>
            {t("asm.next")}<CaretRight size={20} className="rtl:-scale-x-100" aria-hidden="true" />
          </button>
        ) : (
          <button type="button" className="aegov-btn" onClick={() => setConfirm(true)} disabled={moving}>
            <CheckCircle size={20} aria-hidden="true" />{last ? t("asm.submit") : t("asm.finish_section")}
          </button>
        )}
      </div>

      {confirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-aeblack-950/50 p-4" role="dialog" aria-modal="true" aria-labelledby="confirm-title">
          <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-xl">
            <WarningCircle size={40} weight="duotone" className="mb-3 text-primary-600" aria-hidden="true" />
            <h2 id="confirm-title" className="font-heading text-xl font-semibold">{last ? t("asm.submit") : t("asm.finish_section")}</h2>
            <p className="mt-2 text-aeblack-700">{last ? t("asm.confirm_submit") : t("asm.confirm_section")}</p>
            {missing.length > 0 && (
              <div className="mt-4">
                <p className="mb-2 text-sm font-semibold text-aered-700">{t("asm.unanswered", { count: missing.length })}</p>
                <ul className="flex flex-wrap gap-1.5">{missing.map(({ x, i }) => (
                  <li key={x.key}><button type="button" onClick={() => go(i)} aria-label={t("asm.question_of", { n: i + 1, count: qs.length })}
                    className="h-9 min-w-9 rounded-lg border-2 border-aeblack-200 px-2 text-sm font-bold text-aeblack-700 hover:border-primary-400">{i + 1}</button></li>
                ))}</ul>
              </div>
            )}
            {flagged.length > 0 && (
              <div className="mt-4">
                <p className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-camel-900"><Flag size={16} aria-hidden="true" />{t("asm.flagged_list", { count: flagged.length })}</p>
                <ul className="flex flex-wrap gap-1.5">{flagged.map(({ x, i }) => (
                  <li key={x.key}><button type="button" onClick={() => go(i)} aria-label={t("asm.question_of", { n: i + 1, count: qs.length })}
                    className="h-9 min-w-9 rounded-lg border-2 border-camel-300 bg-camel-50 px-2 text-sm font-bold text-camel-900 hover:border-camel-500">{i + 1}</button></li>
                ))}</ul>
              </div>
            )}
            <div className="mt-6 flex flex-wrap gap-3">
              <button type="button" className="aegov-btn" onClick={finishSection} disabled={moving} autoFocus>{t("asm.confirm_yes")}</button>
              <button type="button" className="aegov-btn btn-outline" onClick={() => setConfirm(false)} disabled={moving}>{t("asm.confirm_no")}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/** Every question of the section as a numbered square: current, answered or not, and marked for review. */
function QuestionGrid({ qs, index, answered, flags, onGo, t }: {
  qs: AsmQuestion[]; index: number; answered: (k: string) => boolean; flags: string[]; onGo: (i: number) => void; t: (k: string, p?: Record<string, string | number>) => string;
}) {
  return (
    <ol className="grid grid-cols-[repeat(auto-fill,minmax(2.75rem,1fr))] gap-2" aria-label={t("asm.questions_in_section")}>
      {qs.map((x, i) => {
        const on = i === index, ok = answered(x.key), flag = flags.includes(x.key);
        const state = [ok ? t("asm.legend_answered") : t("asm.legend_unanswered"), flag ? t("asm.flagged") : ""].filter(Boolean).join(", ");
        return (
          <li key={x.key}>
            <button type="button" onClick={() => onGo(i)} aria-current={on ? "step" : undefined} aria-label={`${t("asm.question_of", { n: i + 1, count: qs.length })}, ${state}`}
              className={`relative flex h-11 w-full items-center justify-center rounded-lg border-2 text-sm font-bold transition ${on ? "border-primary-600 bg-primary-600 text-whitely-50 shadow"
                : ok ? "border-primary-300 bg-primary-50 text-primary-800 hover:border-primary-500" : "border-aeblack-200 bg-white text-aeblack-700 hover:border-primary-400"}`}>
              {i + 1}
              {ok && !on && <Check size={12} className="absolute bottom-0.5 end-0.5 text-primary-600" aria-hidden="true" />}
              {flag && <Flag size={12} className={`absolute -top-1.5 -end-1.5 rounded-full bg-white p-0.5 ${on ? "text-camel-700" : "text-camel-700"} shadow-sm`} aria-hidden="true" />}
            </button>
          </li>
        );
      })}
    </ol>
  );
}
