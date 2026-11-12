import { ArrowCounterClockwise, FloppyDisk } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Badge, Button, Card, ErrorState, FlagLabel, Loading, Modal, Note, PageHeader, Row, RowList, TextArea, TextInput, useSaving } from "../components/ui";
import { api, useAsync, useAuth, useLang, usePageTitle, useToast } from "../core";

type Cutoffs = { low: number; moderate: number; high: number; very_high: number };
type TypeNotes = {
  type: string; name_ar: string; name_en: string; strengths_ar: string[]; strengths_en: string[]; risks_ar: string[]; risks_en: string[]; customised: boolean;
};
type Settings = { band_cutoffs: Cutoffs; default_cutoffs: Cutoffs; types: TypeNotes[] };
type Draft = { type: string; name_ar: string; name_en: string; strengths_ar: string; strengths_en: string; risks_ar: string; risks_en: string; customised: boolean };

const KEYS: (keyof Cutoffs)[] = ["low", "moderate", "high", "very_high"];

/** HR's personality settings: where the workplace Big Five bands start, and the notes shown for each of the 16 types. */
export default function PersonalitySettings() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("nav.personality_settings"));
  const { me } = useAuth();
  const canEdit = !!me?.can_administer;
  const toast = useToast();
  const data = useAsync(() => api<Settings>("/personality-settings"), []);
  const [c, setC] = useState<Cutoffs | null>(null);
  const [d, setD] = useState<Draft | null>(null);
  const bands = useSaving();
  const note = useSaving();
  useEffect(() => { if (data.data) setC(data.data.band_cutoffs); }, [data.data]);

  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const s = data.data!;
  const cut = c || s.band_cutoffs;
  // The score range each band covers, from the cut-offs (out of 25).
  const ranges = [
    ["VeryLow", 5, cut.low - 1], ["Low", cut.low, cut.moderate - 1], ["Moderate", cut.moderate, cut.high - 1],
    ["High", cut.high, cut.very_high - 1], ["VeryHigh", cut.very_high, 25],
  ] as const;

  const saveBands = async () => {
    if (await bands.run(() => api("/personality-settings/bands", { method: "PUT", body: cut })) !== undefined) {
      toast(t("common.saved"));
      data.reload();
    }
  };
  const saveNote = async () => {
    if (!d) return;
    if (await note.run(() => api(`/personality-settings/types/${d.type}`, { method: "PUT", body: d })) !== undefined) {
      toast(t("common.saved"));
      setD(null);
      data.reload();
    }
  };
  const reset = async () => {
    if (!d || !window.confirm(t("perset.reset_confirm"))) return;
    if (await note.run(() => api(`/personality-settings/types/${d.type}`, { method: "DELETE" })) !== undefined) {
      toast(t("common.saved"));
      setD(null);
      data.reload();
    }
  };
  const open = (n: TypeNotes) => setD({
    type: n.type, name_ar: n.name_ar, name_en: n.name_en, strengths_ar: n.strengths_ar.join("\n"), strengths_en: n.strengths_en.join("\n"),
    risks_ar: n.risks_ar.join("\n"), risks_en: n.risks_en.join("\n"), customised: n.customised,
  });

  return (
    <>
      <PageHeader title={t("nav.personality_settings")} subtitle={t("perset.subtitle")} />
      <div className="space-y-6">
        <Card title={t("perset.bands")} actions={canEdit && <>
          <Button variant="outline" size="sm" onClick={() => setC(s.default_cutoffs)} icon={<ArrowCounterClockwise size={20} aria-hidden="true" />}>{t("perset.defaults")}</Button>
          <Button size="sm" busy={bands.busy} onClick={saveBands} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>
        </>}>
          <div className="mb-4"><Note>{t("perset.bands_hint")}</Note></div>
          {bands.error && <div className="mb-4"><ErrorState error={bands.error} /></div>}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {KEYS.map((k) => (
              <TextInput key={k} label={t(`perset.from.${k}`)} type="number" min={6} max={25} value={cut[k]} disabled={!canEdit}
                onChange={(e) => setC({ ...cut, [k]: Number(e.target.value) || 0 })} />
            ))}
          </div>
          <ul className="mt-4 flex flex-wrap gap-2">
            {ranges.map(([b, from, to]) => (
              <li key={b}><Badge tone={b === "VeryHigh" || b === "High" ? "green" : b === "Moderate" ? "gray" : "amber"}>
                {t(`pers.band.${b}`)} <span dir="ltr">{from > to ? "-" : from === to ? from : `${from} to ${to}`}</span>
              </Badge></li>
            ))}
          </ul>
        </Card>

        <Card title={t("perset.types")}>
          <div className="mb-4"><Note>{t("perset.types_hint")}</Note></div>
          <RowList>
            {s.types.map((n) => (
              <Row key={n.type} onOpen={canEdit ? () => open(n) : undefined}
                title={<><span dir="ltr" className="me-2 font-bold tracking-wide">{n.type}</span>{en ? n.name_en : n.name_ar}</>}
                sub={(en ? n.strengths_en : n.strengths_ar).join(" · ")}
                aside={n.customised && <Badge tone="gold">{t("perset.customised")}</Badge>} />
            ))}
          </RowList>
        </Card>
      </div>

      {d && (
        <Modal open onClose={() => setD(null)} title={d.type} footer={<>
          {d.customised && <Button variant="outline" onClick={reset} icon={<ArrowCounterClockwise size={24} aria-hidden="true" />}>{t("perset.reset")}</Button>}
          <Button variant="outline" onClick={() => setD(null)}>{t("common.cancel")}</Button>
          <Button busy={note.busy} onClick={saveNote}>{t("common.save")}</Button></>}>
          {note.error && <ErrorState error={note.error} />}
          <TextInput label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={d.name_ar} onChange={(e) => setD({ ...d, name_ar: e.target.value })} />
          <TextInput label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} dir="ltr" value={d.name_en} onChange={(e) => setD({ ...d, name_en: e.target.value })} />
          <TextArea label={<FlagLabel lang="ar">{t("pers.strengths")}</FlagLabel>} hint={t("perset.lines_hint")} dir="rtl" rows={4} value={d.strengths_ar} onChange={(e) => setD({ ...d, strengths_ar: e.target.value })} />
          <TextArea label={<FlagLabel lang="en">{t("pers.strengths")}</FlagLabel>} dir="ltr" rows={4} value={d.strengths_en} onChange={(e) => setD({ ...d, strengths_en: e.target.value })} />
          <TextArea label={<FlagLabel lang="ar">{t("pers.risks")}</FlagLabel>} dir="rtl" rows={3} value={d.risks_ar} onChange={(e) => setD({ ...d, risks_ar: e.target.value })} />
          <TextArea label={<FlagLabel lang="en">{t("pers.risks")}</FlagLabel>} dir="ltr" rows={3} value={d.risks_en} onChange={(e) => setD({ ...d, risks_en: e.target.value })} />
        </Modal>
      )}
    </>
  );
}
