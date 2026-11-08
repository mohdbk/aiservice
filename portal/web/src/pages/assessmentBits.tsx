import { Brain, BookOpen, Briefcase, Code, Exam, Icon, Scales, Translate, UserList } from "@phosphor-icons/react";
import { AsmStatus } from "../api";
import { useSite } from "../site";

const TYPE_ICON: Record<string, Icon> = {
  "Knowledge Test": BookOpen, "Language Check": Translate, "Situational Judgement": Scales, "Case Study": Briefcase,
  "Technical Task": Code, Aptitude: Brain, Personality: UserList,
};
export const typeIcon = (type: string): Icon => TYPE_ICON[type] || Exam;
export const typeKey = (type: string) => `asm.type.${(TYPE_ICON[type] ? type : "Other").toLowerCase().replace(/\s+/g, "_")}`;

const TONE: Record<AsmStatus, string> = {
  to_do: "warning", in_progress: "info", submitted: "info", completed: "success", expired: "error", cancelled: "error",
};

export function AsmStatusBadge({ status }: { status: AsmStatus }) {
  const { t } = useSite();
  return <span className={`aegov-badge badge-${TONE[status] || "info"}`}>{t(`asm_status.${status}`)}</span>;
}

export function formatClock(seconds: number) {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;
  const pad = (n: number) => String(n).padStart(2, "0");
  return h ? `${h}:${pad(m)}:${pad(r)}` : `${pad(m)}:${pad(r)}`;
}

export function useDeadline() {
  const { lang } = useSite();
  return (s: string) => {
    if (!s) return "";
    const d = new Date(s.replace(" ", "T"));
    if (isNaN(d.getTime())) return s;
    try {
      return new Intl.DateTimeFormat(`${lang}-u-nu-latn`, { dateStyle: "long", timeStyle: "short" }).format(d);
    } catch {
      return d.toLocaleString("en");
    }
  };
}
