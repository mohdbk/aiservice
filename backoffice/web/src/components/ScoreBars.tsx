import { useLang } from "../core";

export type ScoreRow = { label: string; percent: number; passMark?: number | null; passed?: boolean | null; strong?: boolean };

/**
 * Scores against pass marks (a bullet chart): one bar per row filling from the reading start to the score, and a dark
 * marker at the pass mark. Green when passed, red when not, gold when there is no pass mark or it is not decided yet.
 * The score, the pass mark and the result are also written beside each bar, so nothing depends on colour.
 */
export function ScoreBars({ rows }: { rows: ScoreRow[] }) {
  const { t } = useLang();
  const clamp = (n: number) => Math.max(0, Math.min(100, n));
  return (
    <ul className="space-y-4">
      {rows.map((r, i) => {
        const pm = r.passMark != null && r.passMark > 0 ? clamp(Number(r.passMark)) : null;
        const fill = r.passed === true ? "bg-aegreen-600" : r.passed === false ? "bg-aered-600" : "bg-primary-500";
        const result = r.passed === true ? t("res.Pass") : r.passed === false ? t("res.Fail") : "";
        return (
          <li key={i}>
            <div className="mb-1.5 flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
              <span className={r.strong ? "font-bold text-aeblack-900" : "text-sm font-semibold text-aeblack-800"} dir="auto">{r.label}</span>
              <span className="flex items-baseline gap-2 text-xs text-aeblack-600">
                {pm != null && <span>{t("score.pass_mark", { n: pm })}</span>}
                {result && <span className={`font-semibold ${r.passed ? "text-aegreen-700" : "text-aered-700"}`}>{result}</span>}
                <span className={`tabular-nums font-bold text-aeblack-900 ${r.strong ? "text-lg" : "text-sm"}`} dir="ltr">{Math.round(r.percent * 10) / 10}%</span>
              </span>
            </div>
            <div className={`relative rounded-full bg-aeblack-100 ${r.strong ? "h-4" : "h-3"}`} role="img"
              aria-label={`${r.label}: ${r.percent}%${pm != null ? `, ${t("score.pass_mark", { n: pm })}` : ""}${result ? `, ${result}` : ""}`}>
              <div className={`h-full rounded-full ${fill} transition-[width] duration-500`} style={{ width: `${clamp(r.percent)}%` }} />
              {pm != null && (
                <span className="absolute -top-1 -bottom-1 w-[3px] -translate-x-1/2 rounded-full bg-aeblack-800 rtl:translate-x-1/2"
                  style={{ insetInlineStart: `${pm}%` }} aria-hidden="true" />
              )}
            </div>
          </li>
        );
      })}
      {rows.some((r) => r.passMark) && (
        <li className="flex items-center gap-2 text-xs text-aeblack-500" aria-hidden="true">
          <span className="inline-block h-3.5 w-[3px] rounded-full bg-aeblack-800" />{t("score.marker")}
        </li>
      )}
    </ul>
  );
}
