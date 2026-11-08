import { CaretLeft, CaretRight, CheckCircle, CloudArrowUp, CloudCheck, CloudSlash, FileText, Timer, UploadSimple, WarningCircle } from "@phosphor-icons/react";
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

function Answer({ q, value, onChange, onUpload, disabled }: {
  q: AsmQuestion; value: unknown; onChange: (v: unknown, immediate?: boolean) => void; onUpload: (f: File) => void; disabled: boolean;
}) {
  const { t } = useSite();
  const radio = "flex cursor-pointer items-start gap-3 rounded-xl border-2 p-4 transition hover:border-primary-400";
  if (q.type === "Single Choice" || q.type === "True or False") {
    return (
      <fieldset className="space-y-3"><legend className="sr-only">{t("asm.your_answer")}</legend>
        {q.options.map((o) => (
          <label key={o.key} className={`${radio} ${value === o.key ? "border-primary-600 bg-primary-50" : "border-aeblack-100 bg-white"}`}>
            <input type="radio" name={q.key} className="mt-1 h-5 w-5 rounded-full accent-primary-600" checked={value === o.key} disabled={disabled} onChange={() => onChange(o.key, true)} />
            <span>{o.text}</span>
          </label>
        ))}
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
            <label key={o.key} className={`${radio} ${on ? "border-primary-600 bg-primary-50" : "border-aeblack-100 bg-white"}`}>
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
  const { t, lang, path, errorText } = useSite();
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

  if (error && !st) return <div className="container mx-auto max-w-3xl px-4 py-10"><Alert tone="error" title={errorText(error)} /></div>;
  if (!st) return <FullPageLoading />;

  const qs = st.section.questions;
  const q = qs[Math.min(index, qs.length - 1)];
  const answered = (k: string) => { const v = answers[k]; return !(v === undefined || v === null || v === "" || (Array.isArray(v) && !v.length)); };
  const unanswered = qs.filter((x) => !answered(x.key)).length;
  const last = st.section_index === st.section_count - 1;
  const low = remaining !== null && remaining <= 60;
  const SaveIcon = save === "saving" ? CloudArrowUp : save === "failed" ? CloudSlash : CloudCheck;

  return (
    <div className="container mx-auto max-w-4xl px-4 py-6">
      <div className="mc-surface sticky top-[4.5rem] z-10 mb-6 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-aeblack-100 bg-white/95 p-4 shadow-sm backdrop-blur md:top-[8.5rem]">
        <div>
          <p className="text-sm text-aeblack-600">{t("asm.section_of", { n: st.section_index + 1, count: st.section_count })}</p>
          <p className="font-heading text-lg font-semibold text-aeblack-900">{st.section.title}</p>
        </div>
        <div className="flex items-center gap-4">
          <span className={`inline-flex items-center gap-1.5 text-sm ${save === "failed" ? "text-aered-700" : "text-aeblack-600"}`} aria-live="polite">
            <SaveIcon size={20} aria-hidden="true" />{save === "saving" ? t("asm.saving") : save === "failed" ? t("asm.save_failed") : save === "saved" ? t("asm.saved") : ""}
          </span>
          {remaining !== null && (
            <span className={`inline-flex items-center gap-2 rounded-full px-4 py-2 font-mono text-lg font-bold ${low ? "bg-aered-50 text-aered-700" : "bg-primary-50 text-primary-800"}`}
              role="timer" aria-label={t("asm.time_left")}>
              <Timer size={24} aria-hidden="true" /><span dir="ltr">{formatClock(remaining)}</span>
            </span>
          )}
        </div>
      </div>

      {notice && <div className="mb-4"><Alert tone="warning" title={notice} /></div>}
      {error ? <div className="mb-4"><Alert tone="error" title={errorText(error)} /></div> : null}
      {st.section.instructions && index === 0 && <div className="mb-4"><Alert tone="info">{st.section.instructions}</Alert></div>}

      <nav aria-label={t("asm.questions_in_section")} className="mb-6 flex flex-wrap gap-2">
        {qs.map((x, i) => (
          <button key={x.key} type="button" onClick={() => setIndex(i)} aria-current={i === index ? "step" : undefined}
            aria-label={`${t("asm.question_of", { n: i + 1, count: qs.length })}${answered(x.key) ? `, ${t("asm.saved")}` : ""}`}
            className={`flex h-10 w-10 items-center justify-center rounded-lg border-2 text-sm font-bold ${i === index ? "border-primary-600 bg-primary-600 text-whitely-50" : answered(x.key) ? "border-primary-300 bg-primary-50 text-primary-800" : "border-aeblack-200 bg-white text-aeblack-700"}`}>
            {i + 1}
          </button>
        ))}
      </nav>

      <section className="mc-surface rounded-2xl border border-aeblack-100 bg-white p-6 md:p-8" aria-labelledby="question-title">
        <div className="mb-4 flex items-center justify-between gap-3 text-sm text-aeblack-600">
          <span>{t("asm.question_of", { n: index + 1, count: qs.length })}</span>
          <span>{t("asm.points", { points: q.points })}</span>
        </div>
        <div id="question-title" className="mc-prose mb-6 text-lg font-medium text-aeblack-900" dir="auto" dangerouslySetInnerHTML={{ __html: q.text }} />
        <Answer q={q} value={answers[q.key]} disabled={moving} onChange={(v, im) => change(q.key, v, im)} onUpload={(f) => upload(q.key, f)} />
      </section>

      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
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
            {unanswered > 0 && <p className="mt-2 font-semibold text-aered-700">{t("asm.unanswered", { count: unanswered })}</p>}
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
