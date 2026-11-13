import { useLang, timeZone } from "../core";
import { Segmented, TextInput } from "./ui";

export type Deadline = { mode: "days" | "exact"; days: number; at: string };
export const DEFAULT_DEADLINE: Deadline = { mode: "days", days: 7, at: "" };

/** The request fields for a deadline: a number of days, or an exact local date and time (in the ministry's time zone). */
export const deadlineBody = (d: Deadline) => (d.mode === "exact" ? { deadline_days: 7, deadline_at: d.at } : { deadline_days: d.days, deadline_at: null });

/** The local date and time now plus some days, as a datetime-local value in the ministry's time zone. */
export function localIn(days: number, hour = 17) {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: timeZone(), year: "numeric", month: "2-digit", day: "2-digit" })
    .formatToParts(new Date(Date.now() + days * 86400000));
  const get = (k: string) => parts.find((p) => p.type === k)?.value ?? "";
  return `${get("year")}-${get("month")}-${get("day")}T${String(hour).padStart(2, "0")}:00`;
}

/** A deadline as a number of days from now, or an exact date and time; the exact one is in the ministry's time zone. */
export function DeadlinePicker({ value, onChange, exactOnly = false }: { value: Deadline; onChange: (d: Deadline) => void; exactOnly?: boolean }) {
  const { t } = useLang();
  const mode = exactOnly ? "exact" : value.mode;
  return (
    <div className="space-y-3">
      {!exactOnly && (
        <Segmented label={t("deadline.label")} value={value.mode} onChange={(m) => onChange({ ...value, mode: m, at: value.at || localIn(value.days) })}
          options={[{ value: "days", label: t("deadline.in_days") }, { value: "exact", label: t("deadline.exact") }]} />
      )}
      {mode === "days"
        ? <TextInput required label={t("jobs.deadline_days")} type="number" min={1} max={90} value={value.days} onChange={(e) => onChange({ ...value, days: Number(e.target.value) || 7 })} />
        : <TextInput required label={t("deadline.at")} hint={t("deadline.zone", { zone: timeZone() })} type="datetime-local" min={localIn(0, 0)}
            value={value.at} onChange={(e) => onChange({ ...value, at: e.target.value })} />}
    </div>
  );
}
