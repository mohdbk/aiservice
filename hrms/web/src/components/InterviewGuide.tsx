import { ArrowClockwise, Printer, Sparkle } from "@phosphor-icons/react";
import { useState } from "react";
import { api, ApiError, useAsync, useLang } from "../core";
import { Button, Card, ErrorState, Loading } from "./ui";

type Question = { ar: string; en: string; listen_for_ar: string[]; listen_for_en: string[] };
type Guide = { sections: { skill: string; questions: Question[] }[] };

/**
 * The panel's interview guide: questions for each skill the round assesses and follow-ups on gaps in the CV, each
 * with what a strong answer covers. Prepared on request and kept with the interview; it can be printed.
 */
export function InterviewGuide({ id }: { id: number }) {
  const { t, lang } = useLang();
  const { data, loading, error, reload, setData } = useAsync(() => api<{ guide: Guide | null; ai: boolean }>(`/interviews/${id}/guide`), [id]);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState<ApiError | null>(null);
  const prepare = async () => {
    setBusy(true); setFailed(null);
    try { setData(await api<{ guide: Guide; ai: boolean }>(`/interviews/${id}/guide`, { method: "POST" })); }
    catch (e) { setFailed(e as ApiError); }
    setBusy(false);
  };
  const g = data?.guide;
  return (
    <Card title={t("guide.title")} actions={g && <div className="flex gap-1 print:hidden">
      <Button variant="link" size="sm" onClick={() => window.print()} icon={<Printer size={18} aria-hidden="true" />}>{t("guide.print")}</Button>
      <Button variant="link" size="sm" busy={busy} onClick={prepare} icon={<ArrowClockwise size={18} aria-hidden="true" />}>{t("guide.again")}</Button>
    </div>}>
      {loading ? <Loading /> : error ? <ErrorState error={error} retry={reload} /> : !g ? (
        <div className="flex flex-col items-start gap-3">
          <p className="text-sm text-aeblack-600">{t("guide.intro")}</p>
          {data?.ai ? <Button busy={busy} onClick={prepare} icon={<Sparkle size={20} aria-hidden="true" />}>{busy ? t("guide.preparing") : t("guide.prepare")}</Button>
            : <p className="text-sm text-aeblack-500">{t("guide.no_ai")}</p>}
        </div>
      ) : (
        <ol className="space-y-5">
          {g.sections.map((s, n) => (
            <li key={n}>
              <h3 className="mb-2 text-sm font-bold text-primary-800" dir="auto">{s.skill}</h3>
              <ol className="space-y-3">
                {s.questions.map((q, k) => {
                  const points = lang === "en" ? q.listen_for_en : q.listen_for_ar;
                  return (
                    <li key={k} className="rounded-xl border border-aeblack-100 p-3">
                      <p className="font-semibold" dir="auto">{lang === "en" ? q.en : q.ar}</p>
                      {points?.length > 0 && <>
                        <p className="mt-2 text-xs font-semibold text-aeblack-500">{t("guide.listen")}</p>
                        <ul className="mt-1 list-disc space-y-0.5 ps-5 text-sm text-aeblack-700">{points.map((p, x) => <li key={x} dir="auto">{p}</li>)}</ul>
                      </>}
                    </li>
                  );
                })}
              </ol>
            </li>
          ))}
        </ol>
      )}
      {failed && <div className="mt-3"><ErrorState error={failed} /></div>}
    </Card>
  );
}
