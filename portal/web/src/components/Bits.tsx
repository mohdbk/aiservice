// Small shared pieces. Icons are Phosphor (the UAE Design System icon set), regular weight.
import { Buildings, CalendarBlank, Check, Clock, Icon, MapPin, Money } from "@phosphor-icons/react";
import { ReactNode } from "react";
import { Salary, Status } from "../api";
import { useSite } from "../site";

export function StatusBadge({ status, large }: { status: Status; large?: boolean }) {
  const { t } = useSite();
  return <span className={`aegov-badge badge-${status.tone}${large ? " badge-lg" : ""}`}>{t(`status.${status.code}`)}</span>;
}

// Icon in a rounded square, as the design system asks (primary on light).
export function IconBox({ icon: I, size = 24, className = "" }: { icon: Icon; size?: number; className?: string }) {
  return (
    <span className={`inline-flex shrink-0 items-center justify-center rounded-lg bg-primary-50 p-2 text-primary-600 ${className}`} aria-hidden="true">
      <I size={size} weight="regular" />
    </span>
  );
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
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full border-2 text-sm font-bold ${
                state === "upcoming" ? "border-aeblack-200 bg-white text-aeblack-500" : state === "current" ? "border-primary-600 bg-primary-600 text-whitely-50" : "border-primary-600 bg-primary-50 text-primary-700"}`}>
                {state === "done" ? <Check size={20} weight="bold" aria-hidden="true" /> : n}
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

export function RequiredMark() {
  return <span className="text-aered-600" aria-hidden="true"> *</span>;
}

/** A labelled field. Required fields carry a red asterisk (screen readers hear "required" from the input itself). */
export function Field({ id, label, hint, optional, required, prefix: P, children }: { id: string; label: string; hint?: string; optional?: boolean; required?: boolean; prefix?: Icon; children: ReactNode }) {
  const { t } = useSite();
  return (
    <div className="aegov-form-control">
      <label htmlFor={id}>
        {label}
        {required && <RequiredMark />}
        {optional && <span className="ms-1 font-normal text-aeblack-500">({t("common.optional")})</span>}
      </label>
      <div className="form-control-input">
        {P && <span className="control-prefix" aria-hidden="true"><P size={24} weight="regular" /></span>}
        {children}
      </div>
      {hint && <p id={`${id}-hint`} className="mt-1 text-sm text-aeblack-600">{hint}</p>}
    </div>
  );
}

export function useSalary() {
  const { formatNumber } = useSite();
  return (s: Salary) => (s ? `${s.currency} ${s.min && s.max ? `${formatNumber(s.min)} - ${formatNumber(s.max)}` : formatNumber(s.min || s.max)}` : "");
}

const META: Record<string, Icon> = { building: Buildings, pin: MapPin, clock: Clock, calendar: CalendarBlank, money: Money };

// Mini (20 px) icon for metadata next to small text; the design system allows 20 px sparingly.
export function MetaIcon({ name, size = 20 }: { name: "building" | "pin" | "clock" | "calendar" | "money"; size?: number }) {
  const I = META[name];
  return <I size={size} weight="regular" className="shrink-0 text-primary-600" aria-hidden="true" />;
}
export const META_ICONS = META;
