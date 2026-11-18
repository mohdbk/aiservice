import { DownloadSimple, FileText, Star, Trash, UploadSimple } from "@phosphor-icons/react";
import { useRef, useState } from "react";
import { api, ApiError, useAsync, useLang, useToast } from "../core";
import { Badge, Button, ErrorState } from "./ui";

type Cv = { id: number; file_name: string; size: number; is_default: boolean; status: string; uploaded_at: string; by_staff: boolean; years: number | null; summary: string };

/**
 * A candidate's CV library, for HR: see and download their CVs, upload one for them (for example a CV sent by email,
 * which also puts them in reach of the talent pool search), choose the default and delete.
 */
export function CandidateCvs({ id }: { id: number }) {
  const { t, date } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<{ cvs: Cv[]; max: number }>(`/candidates/${id}/cvs`), [id]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const act = async (fn: () => Promise<unknown>, ok: string) => {
    setBusy(true); setError(null);
    try { await fn(); toast(ok); data.reload(); } catch (e) { setError(e as ApiError); }
    setBusy(false);
  };
  const upload = (f: File) => {
    const form = new FormData();
    form.append("file", f, f.name);
    return act(() => api(`/candidates/${id}/cvs`, { body: form }), t("ccv.uploaded"));
  };
  const cvs = data.data?.cvs || [];
  return (
    <div>
      <p className="mb-2 text-sm font-semibold">{t("ccv.title")}</p>
      {error && <div className="mb-2"><ErrorState error={error} /></div>}
      {data.error ? <ErrorState error={data.error} retry={data.reload} /> : cvs.length === 0 ? <p className="text-sm text-aeblack-500">{t("ccv.none")}</p> : (
        <ul className="divide-y divide-aeblack-100 text-sm">
          {cvs.map((c) => (
            <li key={c.id} className="flex flex-wrap items-center gap-2 py-2">
              <FileText size={20} className="shrink-0 text-primary-600" aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <a href={`/api/backoffice/candidates/${id}/cvs/${c.id}/file`} target="_blank" rel="noreferrer" className="block truncate font-semibold text-primary-700 hover:underline" dir="auto">{c.file_name}</a>
                <p className="text-xs text-aeblack-500">
                  {date(c.uploaded_at)}{c.by_staff ? ` · ${t("ccv.by_staff")}` : ""}{c.years != null ? ` · ${t("ccv.years", { n: c.years })}` : ""}
                  {c.status === "Pending" ? ` · ${t("ccv.reading")}` : c.status === "Unreadable" ? ` · ${t("ccv.unreadable")}` : ""}
                </p>
                {c.summary && <p className="mt-0.5 line-clamp-2 text-xs text-aeblack-600" dir="auto">{c.summary}</p>}
              </div>
              {c.is_default ? <Badge tone="green">{t("ccv.default")}</Badge> : (
                <Button size="sm" variant="link" disabled={busy} icon={<Star size={16} aria-hidden="true" />}
                  onClick={() => act(() => api(`/candidates/${id}/cvs/${c.id}/default`, { body: {} }), t("ccv.made_default"))}>{t("ccv.make_default")}</Button>
              )}
              <a className="rounded p-1.5 text-aeblack-500 hover:bg-primary-50 hover:text-primary-700" href={`/api/backoffice/candidates/${id}/cvs/${c.id}/file?download=true`} aria-label={t("ccv.download", { name: c.file_name })}>
                <DownloadSimple size={18} aria-hidden="true" />
              </a>
              <button type="button" className="rounded p-1.5 text-aeblack-500 hover:bg-aered-50 hover:text-aered-700" disabled={busy} aria-label={t("ccv.delete", { name: c.file_name })}
                onClick={() => window.confirm(t("ccv.delete_confirm")) && act(() => api(`/candidates/${id}/cvs/${c.id}`, { method: "DELETE" }), t("ccv.deleted"))}>
                <Trash size={18} aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}
      <input ref={input} type="file" className="sr-only" accept=".pdf,.docx,.jpg,.jpeg,.png" aria-label={t("ccv.upload")}
        onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ""; if (f) upload(f); }} />
      <Button size="sm" variant="outline" className="mt-2" busy={busy} disabled={cvs.length >= (data.data?.max ?? 5)} onClick={() => input.current?.click()}
        icon={<UploadSimple size={18} aria-hidden="true" />}>{t("ccv.upload")}</Button>
    </div>
  );
}
