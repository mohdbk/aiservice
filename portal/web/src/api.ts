// JSON client for the portal backend (same origin). Errors carry a code the UI turns into text.
export class ApiError extends Error {
  code: string;
  status: number;
  params: Record<string, unknown>;
  constructor(code: string, status: number, params: Record<string, unknown> = {}) {
    super(code);
    this.code = code;
    this.status = status;
    this.params = params;
  }
}

export async function api<T>(path: string, opts: { method?: string; body?: unknown; signal?: AbortSignal } = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  const init: RequestInit = { method: opts.method || (opts.body !== undefined ? "POST" : "GET"), headers, credentials: "same-origin", signal: opts.signal };
  if (init.method !== "GET") {
    headers["Content-Type"] = "application/json";
    headers["X-Portal"] = "1";
    init.body = JSON.stringify(opts.body ?? {});
  }
  let res: Response;
  try {
    res = await fetch(path, init);
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("connection", 0);
  }
  let data: any = {};
  try {
    data = await res.json();
  } catch {
    /* empty body */
  }
  if (!res.ok) throw new ApiError(data?.error?.code || "server", res.status, data?.error?.params || {});
  return data as T;
}

export const qs = (params: Record<string, string | number | undefined>) =>
  new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined && v !== "").map(([k, v]) => [k, String(v)])).toString();

export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const r = new FileReader();
    r.onload = () => resolve(String(r.result).split(",")[1] || "");
    r.onerror = () => reject(new ApiError("cv_empty", 0));
    r.readAsDataURL(file);
  });
}

// ---------- types ----------
export type Language = { code: string; label: string; dir: "rtl" | "ltr" };
export type SiteConfig = {
  languages: Language[];
  default_language: string;
  lang: string;
  dir: "rtl" | "ltr";
  text: Record<string, string>;
  main_site_url: string;
  logo: string;
  logo_dark: string;
  allow_signup: boolean;
  max_cv_mb: number;
  countries: string[];
};
export type Salary = { currency: string; min: number; max: number; per: string } | null;
export type Job = {
  id: string;
  title: string;
  content_lang: string;
  content_dir: "rtl" | "ltr";
  department: string;
  location: string;
  employment_type: string;
  posted_on: string;
  closes_on: string;
  salary: Salary;
  summary: string;
  accepting: boolean;
  description_html?: string;
  screening?: ScreeningQuestion[];
};
export type ScreeningQuestion = { key: string; type: string; required: boolean; text: string; options: { value: string; label: string }[] };
export type JobList = { jobs: Job[]; total: number; page: number; pages: number; filters: { departments: string[]; locations: string[] } };
export type User = { email: string; full_name: string };
export type Status = { code: string; tone: "info" | "warning" | "success" | "error" };
export type ApplicationSummary = { id: string; job: string; title: string; submitted: string; reference: string; status: Status };
export type Details = {
  years: number | null;
  work: { position: string; employer: string; start: string; end: string }[];
  education: { degree: string; institution: string; end: string }[];
  skills: string[];
  languages: string[];
  certificates: string[];
};
export type ApplicationDetail = ApplicationSummary & { details_state: "ready" | "preparing" | "unavailable"; details: Details | null };

// ---------- assessments ----------
export type AsmStatus = "to_do" | "in_progress" | "submitted" | "completed" | "expired" | "cancelled";
export type AssessmentSummary = {
  /** Languages the candidate can take it in, and the one chosen (or the original). */
  languages?: string[];
  language?: string;
  id: string;
  title: string;
  type: string;
  status: AsmStatus;
  deadline: string;
  attempts_allowed: number;
  attempts_used: number;
  can_start: boolean;
  reason: string;
  minutes: number;
  extra_time_percent: number;
  sections: { title: string; minutes: number }[];
  instructions: string;
  score: number | null;
  result: string;
  job_title: string;
};
export type AsmQuestion = { key: string; type: string; text: string; points: number; max_words: number; options: { key: string; text: string }[] };
export type AsmState = {
  /** The language the questions are in (the candidate's choice). */
  language?: string;
  finished?: boolean;
  attempt: number;
  section_index: number;
  section_count: number;
  sections: { title: string; minutes: number; questions: number }[];
  section: { title: string; instructions: string; remaining_seconds: number | null; questions: AsmQuestion[] };
  answers: Record<string, unknown>;
};
