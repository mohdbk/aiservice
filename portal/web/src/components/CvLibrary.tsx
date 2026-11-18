import { CheckCircle, FileText, Star, Trash, UploadSimple } from "@phosphor-icons/react";
import { useRef, useState } from "react";
import { api, ApiError, fileToBase64 } from "../api";
import { useSite } from "../site";
import { useAsync } from "../useAsync";
import { Alert, Skeleton } from "./States";

export type SavedCv = { id: number; file_name: string; size: number; is_default: boolean; status: string; uploaded_at: string; by_staff: boolean; years: number | null; summary: string };
export const CV_RE = /\.(pdf|docx|jpe?g|png)$/i;

/** Uploads a CV to the candidate's library and returns its id. */
export async function uploadCv(file: File, maxMb: number, makeDefault = false): Promise<number> {
  if (!CV_RE.test(file.name)) throw new ApiError("cv_type", 400);
  if (file.size > maxMb * 1024 * 1024) throw new ApiError("cv_too_large", 400, { max_mb: maxMb });
  const r = await api<{ id: number }>("/api/cvs", { body: { cv: { name: file.name, type: file.type, data: await fileToBase64(file) }, default: makeDefault } });
  return r.id;
}

/**
 * "My CVs" on the account page: the candidate keeps up to five CVs, picks the one used by default when applying, and
 * deletes the ones they no longer want. Applications already sent keep the CV they were sent with.
 */
export function CvLibrary() {
  const { t, formatDate, errorText, config } = useSite();
  const { data, error, reload } = useAsync((signal) => api<{ cvs: SavedCv[]; max: number }>("/api/cvs", { signal }), []);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<unknown>(null);
  const [done, setDone] = useState("");
  const input = useRef<HTMLInputElement>(null);
  const act = async (fn: () => Promise<unknown>, ok: string) => {
    setBusy(true); setProblem(null); setDone("");
    try { await fn(); setDone(ok); reload(); } catch (e) { setProblem(e); }
    setBusy(false);
  };
  const cvs = data?.cvs || [];
  const full = cvs.length >= (data?.max ?? 5);
  return (
    <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6" aria-labelledby="cvs-title">
      <h2 id="cvs-title" className="font-heading text-lg font-semibold text-aeblack-900">{t("cvs.title")}</h2>
      <p className="mt-1 text-aeblack-600">{t("cvs.intro", { max: data?.max ?? 5 })}</p>
      {error ? <div className="mt-4"><Alert tone="error" title={errorText(error)} /></div> : null}
      {!data && !error ? <Skeleton className="mt-4 h-20" /> : (
        <ul className="mt-4 space-y-2">
          {cvs.length === 0 && <li className="rounded-xl border border-dashed border-aeblack-200 p-4 text-sm text-aeblack-600">{t("cvs.none")}</li>}
          {cvs.map((c) => (
            <li key={c.id} className={`flex flex-wrap items-center gap-3 rounded-xl border p-3 ${c.is_default ? "border-primary-300 bg-primary-50" : "border-aeblack-100"}`}>
              <FileText size={28} className="shrink-0 text-primary-600" aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <p className="truncate font-medium text-aeblack-900"><bdi>{c.file_name}</bdi></p>
                <p className="text-sm text-aeblack-600">
                  {t("cvs.uploaded", { date: formatDate(c.uploaded_at.slice(0, 10)) })}{c.by_staff ? ` · ${t("cvs.by_staff")}` : ""}
                  {c.status === "unreadable" || c.status === "Unreadable" ? ` · ${t("cvs.unreadable")}` : ""}
                </p>
              </div>
              {c.is_default ? (
                <span className="aegov-badge badge-success inline-flex items-center gap-1"><CheckCircle size={16} aria-hidden="true" />{t("cvs.default")}</span>
              ) : (
                <button type="button" className="aegov-btn btn-link btn-sm" disabled={busy} onClick={() => act(() => api(`/api/cvs/${c.id}/default`, { body: {} }), t("cvs.made_default"))}>
                  <Star size={18} aria-hidden="true" />{t("cvs.make_default")}
                </button>
              )}
              <button type="button" className="aegov-btn btn-link btn-sm text-aered-700" disabled={busy} aria-label={t("cvs.delete_one", { name: c.file_name })}
                onClick={() => window.confirm(t("cvs.delete_confirm")) && act(() => api(`/api/cvs/${c.id}`, { method: "DELETE" }), t("cvs.deleted"))}>
                <Trash size={18} aria-hidden="true" />{t("cvs.delete")}
              </button>
            </li>
          ))}
        </ul>
      )}
      <input ref={input} id="cv-upload" type="file" className="sr-only" accept=".pdf,.docx,.jpg,.jpeg,.png" aria-label={t("cvs.upload")}
        onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ""; if (f) act(() => uploadCv(f, config.max_cv_mb, cvs.length === 0), t("cvs.uploaded_ok")); }} />
      <button type="button" className="aegov-btn btn-outline mt-4" disabled={busy || full} onClick={() => input.current?.click()}>
        <UploadSimple size={22} aria-hidden="true" />{t("cvs.upload")}
      </button>
      {full && <p className="mt-2 text-sm text-aeblack-600">{t("cvs.full", { max: data?.max ?? 5 })}</p>}
      {problem ? <div className="mt-3"><Alert tone="error" title={errorText(problem)} /></div> : null}
      {done && <p className="mt-3 text-aegreen-700" role="status">{done}</p>}
    </section>
  );
}
