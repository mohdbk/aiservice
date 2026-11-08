import { ReactNode } from "react";
import { Salary, Status } from "../api";
import { useSite } from "../site";

export function StatusBadge({ status, large }: { status: Status; large?: boolean }) {
  const { t } = useSite();
  return <span className={`aegov-badge badge-${status.tone}${large ? " badge-lg" : ""}`}>{t(`status.${status.code}`)}</span>;
}

export function Steps({ current }: { current: 1 | 2 | 3 }) {
  const { t } = useSite();
  const items = [t("steps.account"), t("steps.apply"), t("steps.track")];
  return (
    <nav aria-label={t("steps.label")} className="mb-8">
      <ol className="flex items-center gap-2 sm:gap-4">
        {items.map((label, i) => {
          const n = i + 1;
          const state = n < current ? "done" : n === current ? "current" : "upcoming";
          return (
            <li key={label} className="flex flex-1 items-center gap-2" aria-current={state === "current" ? "step" : undefined}>
              <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full border-2 text-sm font-bold ${
                state === "upcoming" ? "border-aeblack-200 bg-white text-aeblack-500" : state === "current" ? "border-primary-600 bg-primary-600 text-white" : "border-primary-600 bg-primary-50 text-primary-700"}`}>
                {state === "done" ? <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" aria-hidden="true"><path d="M5 12l5 5L20 7" /></svg> : n}
              </span>
              <span className={`text-sm ${state === "current" ? "font-semibold text-aeblack-900" : "text-aeblack-600"}`}>{label}</span>
              {n < 3 && <span className="hidden h-px flex-1 bg-aeblack-200 sm:block" aria-hidden="true" />}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

export function Field({ id, label, hint, optional, children }: { id: string; label: string; hint?: string; optional?: boolean; children: ReactNode }) {
  const { t } = useSite();
  return (
    <div className="aegov-form-control">
      <label htmlFor={id}>
        {label}
        {optional && <span className="ms-1 font-normal text-aeblack-500">({t("common.optional")})</span>}
      </label>
      <div className="form-control-input">{children}</div>
      {hint && <p id={`${id}-hint`} className="mt-1 text-sm text-aeblack-600">{hint}</p>}
    </div>
  );
}

export function useSalary() {
  const { formatNumber } = useSite();
  return (s: Salary) => (s ? `${s.currency} ${s.min && s.max ? `${formatNumber(s.min)} - ${formatNumber(s.max)}` : formatNumber(s.min || s.max)}` : "");
}

export function MetaIcon({ name }: { name: "building" | "pin" | "clock" | "calendar" | "money" }) {
  const paths: Record<string, string> = {
    building: "M4 21V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16M16 9h2a2 2 0 0 1 2 2v10M8 7h4M8 11h4M8 15h4M3 21h18",
    pin: "M12 21s-7-6.2-7-11a7 7 0 1 1 14 0c0 4.8-7 11-7 11Zm0-8a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z",
    clock: "M12 7v5l3 2m6-2a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z",
    calendar: "M8 3v3m8-3v3M4 9h16M5 5h14a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Z",
    money: "M3 7h18v10H3zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z",
  };
  return (
    <svg className="h-4 w-4 shrink-0 text-primary-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={paths[name]} />
    </svg>
  );
}
