import { Plus, Trash } from "@phosphor-icons/react";
import { useState } from "react";
import { MODELS, SUM_BANDS } from "../components/personality";
import { Badge, Button, Card, Empty, ErrorState, FlagLabel, Loading, Modal, PageHeader, Row, RowList, SelectInput, TextInput, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useAuth, useLang, usePageTitle, useToast } from "../core";

type Profile = {
  id?: number; name_ar: string; name_en: string; personality_model: string; weights: Record<string, number>; targets: Record<string, string>;
  types: string[]; strong_from: number; moderate_from: number; is_active: boolean; sort_order: number; row_version?: string;
};

const ROLE_MODELS = ["WorkplaceBigFive", "WorkplaceType16"];
const LETTERS = [["E", "I"], ["N", "S"], ["T", "F"], ["J", "P"]];
const ALL_TYPES = LETTERS[0].flatMap((a) => LETTERS[1].flatMap((b) => LETTERS[2].flatMap((c) => LETTERS[3].map((d) => a + b + c + d))));

const blank = (model: string): Profile => ({
  name_ar: "", name_en: "", personality_model: model, weights: {}, targets: {}, types: [], strong_from: 75, moderate_from: 55, is_active: true, sort_order: 0,
});

/** HR's role profiles: what each position looks for in a personality questionnaire, used to show a fit next to results. */
export default function RoleProfiles() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("nav.role_profiles"));
  const { me } = useAuth();
  const toast = useToast();
  const list = useAsync(() => api<{ items: Profile[] }>("/role-profiles"), []);
  const [f, setF] = useState<Profile | null>(null);
  const { busy, error, run } = useSaving();
  const canEdit = !!me?.can_administer;

  const save = async () => {
    if (!f) return;
    if (await run(() => api(f.id ? `/role-profiles/${f.id}` : "/role-profiles", { method: f.id ? "PUT" : "POST", body: f })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  const remove = async () => {
    if (!f?.id || !window.confirm(t("roles.delete_confirm"))) return;
    if (await run(() => api(`/role-profiles/${f.id}`, { method: "DELETE" })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };

  if (list.loading && !list.data) return <Loading />;
  if (list.error) return <ErrorState error={list.error} />;
  const items = list.data?.items || [];
  const traitName = (model: string, code: string) => { const tr = MODELS[model]?.find((x) => x.code === code); return tr ? (en ? tr.en : tr.ar) : code; };
  const weightTotal = f ? Object.values(f.weights).reduce((s, v) => s + (Number(v) || 0), 0) : 0;
  const typeModel = f?.personality_model === "WorkplaceType16";

  return (
    <>
      <PageHeader title={t("nav.role_profiles")} subtitle={t("roles.subtitle")}
        actions={canEdit && <Button onClick={() => setF(blank("WorkplaceBigFive"))} icon={<Plus size={24} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <div className="space-y-6">
        {ROLE_MODELS.map((model) => {
          // Big Five profiles cover both Big Five scorings.
          const rows = items.filter((r) => r.personality_model === model || (model === "WorkplaceBigFive" && r.personality_model === "BigFive"));
          return (
            <Card key={model} title={t(`roles.model.${model}`)}>
              {rows.length === 0 ? <Empty /> : (
                <RowList>
                  {rows.map((r) => (
                    <Row key={r.id} title={en ? r.name_en : r.name_ar} onOpen={canEdit ? () => setF({ ...r }) : undefined}
                      sub={model === "WorkplaceType16" ? <span dir="ltr" className="tracking-wide">{r.types.join(" · ")}</span>
                        : Object.entries(r.weights).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${traitName(model, k).split(" (")[0]} ${v}%`).join(" · ")}
                      aside={!r.is_active && <Badge>{t("common.inactive")}</Badge>} />
                  ))}
                </RowList>
              )}
            </Card>
          );
        })}
        <p className="text-xs text-aeblack-500">{t("roles.note")}</p>
      </div>

      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? t("common.edit") : t("common.add")} footer={<>
          {f.id && <Button variant="outline" onClick={remove} icon={<Trash size={24} aria-hidden="true" />}>{t("common.delete")}</Button>}
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button>
          <Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <TextInput label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={f.name_ar} onChange={(e) => setF({ ...f, name_ar: e.target.value })} />
          <TextInput label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} dir="ltr" value={f.name_en} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          <SelectInput label={t("pers.model")} value={f.personality_model} disabled={!!f.id}
            onChange={(e) => setF({ ...f, personality_model: e.target.value, weights: {}, targets: {}, types: [] })}
            options={ROLE_MODELS.map((v) => ({ value: v, label: t(`roles.model.${v}`) }))} />

          {typeModel ? (
            <fieldset>
              <legend className="mb-2 text-sm font-semibold">{t("roles.types")}</legend>
              <div className="grid grid-cols-4 gap-2" dir="ltr">
                {ALL_TYPES.map((ty) => {
                  const on = f.types.includes(ty);
                  return (
                    <button key={ty} type="button" aria-pressed={on} onClick={() => setF({ ...f, types: on ? f.types.filter((x) => x !== ty) : [...f.types, ty] })}
                      className={`rounded-lg border px-2 py-2 text-sm font-bold tracking-wider ${on ? "border-primary-600 bg-primary-50 text-primary-800" : "border-aeblack-200 text-aeblack-600"}`}>{ty}</button>
                  );
                })}
              </div>
              <p className="mt-2 text-xs text-aeblack-500">{t("roles.types_hint")}</p>
            </fieldset>
          ) : (
            <fieldset>
              <legend className="mb-2 text-sm font-semibold">{t("roles.weights")}</legend>
              <div className="space-y-2">
                {(MODELS[f.personality_model] || []).map((tr) => (
                  <div key={tr.code} className="grid grid-cols-[1fr_5.5rem_8rem] items-end gap-2">
                    <span className="pb-3 text-sm">{en ? tr.en : tr.ar}</span>
                    <TextInput label={t("roles.weight")} type="number" min={0} max={100} value={f.weights[tr.code] ?? ""}
                      onChange={(e) => setF({ ...f, weights: { ...f.weights, [tr.code]: Number(e.target.value) || 0 } })} />
                    <SelectInput label={t("roles.target")} value={f.targets[tr.code] ?? ""}
                      onChange={(e) => setF({ ...f, targets: { ...f.targets, [tr.code]: e.target.value } })}
                      options={[{ value: "", label: t("roles.no_target") }, ...SUM_BANDS.map((b) => ({ value: b, label: t(`pers.band.${b}`) }))]} />
                  </div>
                ))}
              </div>
              <p className={`mt-2 text-sm ${weightTotal === 100 ? "text-aegreen-700" : "text-aered-700"}`}>{t("roles.weight_total", { n: weightTotal })}</p>
              <div className="mt-3 grid grid-cols-2 gap-3">
                <TextInput label={t("roles.strong_from")} type="number" min={1} max={100} value={f.strong_from} onChange={(e) => setF({ ...f, strong_from: Number(e.target.value) || 0 })} />
                <TextInput label={t("roles.moderate_from")} type="number" min={1} max={99} value={f.moderate_from} onChange={(e) => setF({ ...f, moderate_from: Number(e.target.value) || 0 })} />
              </div>
              <p className="mt-2 text-xs text-aeblack-500">{t("roles.weights_hint")}</p>
            </fieldset>
          )}
          <TextInput label={t("roles.sort_order")} type="number" value={f.sort_order} onChange={(e) => setF({ ...f, sort_order: Number(e.target.value) || 0 })} />
          <Toggle label={t("common.active")} checked={f.is_active} onChange={(v) => setF({ ...f, is_active: v })} />
        </Modal>
      )}
    </>
  );
}
