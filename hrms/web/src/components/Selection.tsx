import { Link } from "react-router-dom";
import { api, useAsync, useLang } from "../core";
import { DataTable, useList } from "./DataTable";
import { Badge, Card, Note, Score, SelectInput, TextInput } from "./ui";

export type Weights = { cv: number; tests: number; interview: number; personality: number };
export const DEFAULT_WEIGHTS: Weights = { cv: 0, tests: 40, interview: 40, personality: 20 };
export const PARTS: (keyof Weights)[] = ["tests", "interview", "personality", "cv"];

type Part = { key: keyof Weights; weight: number; score: number | null };
export type SelectionView = {
  total: number | null; complete: boolean; parts: Part[];
  fit: { profile_id: number; name_ar: string; name_en: string; percent: number; level: string } | null;
};

const fitTone = (l?: string | null) => (l === "Strong" ? "green" : l === "Moderate" ? "amber" : l ? "red" : "gray");

/** The job's selection settings: the role profile personality results are matched against, and the weight of each part. */
export function SelectionSettings({ roleProfileId, weights, onChange }: {
  roleProfileId: number | null; weights: Weights; onChange: (roleProfileId: number | null, weights: Weights) => void;
}) {
  const { t } = useLang();
  const total = PARTS.reduce((s, k) => s + (Number(weights[k]) || 0), 0);
  return (
    <Card title={t("sel.settings")}>
      <div className="mb-4"><Note>{t("sel.settings_hint")}</Note></div>
      <div className="grid gap-6 lg:grid-cols-2">
        <RoleProfileSelect value={roleProfileId} onChange={(rp) => onChange(rp, weights)} />
        <div>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {PARTS.map((k) => (
              <TextInput key={k} label={t(`sel.part.${k}`)} type="number" min={0} max={100} value={weights[k]}
                onChange={(e) => onChange(roleProfileId, { ...weights, [k]: Math.max(0, Math.min(100, Number(e.target.value) || 0)) })} />
            ))}
          </div>
          <p className={`mt-2 text-sm ${total === 100 ? "text-aeblack-500" : "font-semibold text-aered-700"}`}>
            {t("sel.weights_total", { total })}
          </p>
        </div>
      </div>
    </Card>
  );
}

/** The combined score on the applicant's page: the total and each part with its weight. */
export function SelectionCard({ s }: { s: SelectionView | null | undefined }) {
  const { t, lang } = useLang();
  if (!s) return null;
  return (
    <Card title={t("sel.title")} actions={!s.complete && <Badge tone="amber">{t("sel.incomplete")}</Badge>}>
      <div className="flex flex-wrap items-center gap-6">
        <div>
          <p className="text-sm text-aeblack-500">{t("sel.total")}</p>
          <p className="text-4xl"><Score value={s.total} /></p>
        </div>
        <ul className="grid flex-1 grid-cols-2 gap-3 sm:grid-cols-4">
          {PARTS.map((k) => {
            const p = s.parts.find((x) => x.key === k);
            if (!p) return null;
            return (
              <li key={k} className={`rounded-xl border border-aeblack-100 p-3 ${p.weight === 0 ? "border-dashed bg-aeblack-50" : ""}`}>
                <p className="text-xs text-aeblack-500">{t(`sel.part.${k}`)} <span dir="ltr">({p.weight}%)</span></p>
                <p className="mt-1 text-xl"><Score value={p.score} /></p>
              </li>
            );
          })}
        </ul>
      </div>
      {s.fit && (
        <p className="mt-4 flex flex-wrap items-center gap-2 text-sm text-aeblack-700">
          {t("sel.fit_with", { profile: lang === "en" ? s.fit.name_en : s.fit.name_ar })}
          <Badge tone={fitTone(s.fit.level)}>{t(`pers.fit.${s.fit.level}`)}</Badge>
        </p>
      )}
      <p className="mt-3 text-xs text-aeblack-500">{t("sel.how")}</p>
    </Card>
  );
}

type RankRow = {
  rank: number; id: number; code: string; full_name: string; status: string; total: number | null; complete: boolean; cv: number | null;
  tests: number | null; interview: number | null; personality: number | null; role_profile: string | null; fit_level: string | null;
};

/** The job's applicants ranked by combined score. */
export function JobRanking({ jobId }: { jobId: number | string }) {
  const { t, to } = useLang();
  const list = useList<RankRow>(`/jobs/${jobId}/ranking`, ["all"]);
  const w = (list.data as unknown as { weights?: Weights } | undefined)?.weights;
  const head = (k: keyof Weights) => <>{t(`sel.part.${k}`)}{w && <span className="ms-1 font-normal text-aeblack-500" dir="ltr">{w[k]}%</span>}</>;
  return (
    <>
      <div className="mb-4"><Note>{t("sel.ranking_hint")}</Note></div>
      <DataTable list={list} rowKey={(r) => r.id} searchLabel={t("apps.search_hint")}
        filters={[{ key: "all", label: t("sel.show"), options: [{ value: "true", label: t("sel.show_all") }] }]}
        columns={[
          { key: "rank", label: "#", sort: "rank", render: (r) => <span className="tabular-nums" dir="ltr">{r.rank}</span> },
          { key: "name", label: t("common.name"), sort: "name", render: (r) => (
            <Link className="font-semibold text-primary-700" to={to(`/applicants/${r.id}`)}>{r.full_name}</Link>
          ) },
          { key: "total", label: t("sel.total"), sort: "total", render: (r) => (
            <span className="inline-flex items-center gap-2"><Score value={r.total} />{r.total !== null && !r.complete && <Badge tone="amber">{t("sel.partial")}</Badge>}</span>
          ) },
          { key: "tests", label: head("tests"), sort: "tests", render: (r) => <Score value={r.tests} /> },
          { key: "interview", label: head("interview"), sort: "interview", render: (r) => <Score value={r.interview} /> },
          { key: "personality", label: head("personality"), sort: "personality", render: (r) => (
            <span className="inline-flex items-center gap-2"><Score value={r.personality} />{r.fit_level && <Badge tone={fitTone(r.fit_level)}>{t(`pers.fit.${r.fit_level}`)}</Badge>}</span>
          ) },
          ...(w && w.cv > 0 ? [{ key: "cv", label: head("cv"), sort: "cv", render: (r: RankRow) => <Score value={r.cv} /> }] : []),
          { key: "status", label: t("common.status"), sort: "status", render: (r) => <Badge>{t(`app.status.${r.status}`)}</Badge> },
        ]} />
    </>
  );
}

/** The job's role profile (the job family whose personality fit is part of the selection score). */
export function RoleProfileSelect({ value, onChange }: { value: number | null; onChange: (id: number | null) => void }) {
  const { t, lang } = useLang();
  const profiles = useAsync(() => api<{ items: { id: number; name_ar: string; name_en: string; personality_model: string; is_active: boolean }[] }>("/role-profiles"), []);
  // One entry per job family: the Big Five profile (its 16-type sibling with the same name is used for that questionnaire).
  const families = (profiles.data?.items || []).filter((p) => p.is_active && p.personality_model !== "WorkplaceType16");
  const typeOnly = (profiles.data?.items || []).filter((p) => p.is_active && p.personality_model === "WorkplaceType16" && !families.some((f) => f.name_en === p.name_en));
  return (
    <SelectInput label={t("sel.role_profile")} hint={t("sel.role_profile_hint")} value={value ?? ""}
      onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
      options={[{ value: "", label: t("common.none") }, ...[...families, ...typeOnly].map((p) => ({ value: p.id, label: lang === "en" ? p.name_en : p.name_ar }))]} />
  );
}
