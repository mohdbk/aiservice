import { FloppyDisk } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { Badge, Button, Card, ErrorState, FlagLabel, Loading, Note, PageHeader, Stat, TextArea, TextInput, Tick, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type Settings = {
  is_enabled: boolean; model_base_url: string; model_name: string; has_api_key: boolean; can_store_key: boolean; temperature: number | null;
  max_steps: number; extra_instructions: string; disabled_tools: string[]; tools: string[]; proposal_minutes: number; daily_run_limit: number;
  history_turns: number; examples_ar: string; examples_en: string; usage: { runs_today: number; people_today: number; approved_today: number };
};

const GROUPS: [string, (tool: string) => boolean][] = [
  ["read", (x) => /^(list_|get_|search_)/.test(x)],
  ["show", (x) => /^(show_|compare_|ask_)/.test(x)],
  ["propose", (x) => x.startsWith("propose_")],
];

/** Setup: everything about how the recruitment assistant behaves. */
export default function AgentSettings() {
  const { t } = useLang();
  usePageTitle(t("nav.agent_settings"));
  const toast = useToast();
  const loaded = useAsync(() => api<Settings>("/agent-settings"), []);
  const [s, setS] = useState<Settings | null>(null);
  const [apiKey, setApiKey] = useState("");
  const [clearKey, setClearKey] = useState(false);
  const { busy, error, run } = useSaving();
  useEffect(() => { if (loaded.data) setS(loaded.data); }, [loaded.data]);
  if (loaded.loading || !s) return loaded.error ? <ErrorState error={loaded.error} retry={loaded.reload} /> : <Loading />;
  const set = <K extends keyof Settings>(k: K, v: Settings[K]) => setS({ ...s, [k]: v });
  const num = (v: string, fallback: number) => (v.trim() === "" || Number.isNaN(Number(v)) ? fallback : Number(v));

  const save = async () => {
    const body = {
      is_enabled: s.is_enabled, model_base_url: s.model_base_url, model_name: s.model_name, api_key: apiKey || null, clear_api_key: clearKey,
      temperature: s.temperature, max_steps: s.max_steps, extra_instructions: s.extra_instructions, disabled_tools: s.disabled_tools,
      proposal_minutes: s.proposal_minutes, daily_run_limit: s.daily_run_limit, history_turns: s.history_turns, examples_ar: s.examples_ar, examples_en: s.examples_en,
    };
    if (await run(() => api("/agent-settings", { method: "PUT", body })) !== undefined) {
      toast(t("common.saved"));
      setApiKey("");
      setClearKey(false);
      loaded.reload();
    }
  };
  const toggleTool = (tool: string, on: boolean) =>
    set("disabled_tools", on ? s.disabled_tools.filter((x) => x !== tool) : [...s.disabled_tools, tool]);

  return (
    <>
      <PageHeader title={t("nav.agent_settings")} subtitle={t("ags.intro")}
        actions={<Button busy={busy} onClick={save} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>} />
      {error && <div className="mb-4"><ErrorState error={error} /></div>}
      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        {(["runs_today", "people_today", "approved_today"] as const).map((k) => (
          <Stat key={k} label={t(`ags.usage.${k}`)} value={s.usage[k]} />
        ))}
      </div>
      <div className="grid gap-6 lg:grid-cols-2">
        <Card title={t("ags.general")}>
          <div className="space-y-4">
            <Toggle label={t("ags.enabled")} hint={t("ags.enabled_hint")} checked={s.is_enabled} onChange={(v) => set("is_enabled", v)} />
            <TextArea label={t("ags.instructions")} hint={t("ags.instructions_hint")} rows={5} dir="auto" value={s.extra_instructions}
              onChange={(e) => set("extra_instructions", e.target.value)} />
            <TextArea label={<FlagLabel lang="ar">{t("ags.examples_ar")}</FlagLabel>} hint={t("ags.examples_hint")}
              rows={3} dir="rtl" lang="ar" value={s.examples_ar} onChange={(e) => set("examples_ar", e.target.value)} />
            <TextArea label={<FlagLabel lang="en">{t("ags.examples_en")}</FlagLabel>} hint={t("ags.examples_hint")}
              rows={3} dir="ltr" lang="en" value={s.examples_en} onChange={(e) => set("examples_en", e.target.value)} />
          </div>
        </Card>
        <Card title={t("ags.model")}>
          <div className="space-y-4">
            <Note>{t("ags.model_hint")}</Note>
            <TextInput label={t("ags.base_url")} type="url" placeholder="https://llm.internal/v1" value={s.model_base_url} onChange={(e) => set("model_base_url", e.target.value)} />
            <TextInput label={t("ags.model_name")} dir="ltr" value={s.model_name} onChange={(e) => set("model_name", e.target.value)} />
            <div>
              <TextInput label={<span className="inline-flex items-center gap-2">{t("ags.api_key")}
                {s.has_api_key ? <Badge tone="green">{t("ags.key_stored")}</Badge> : <Badge>{t("ags.key_none")}</Badge>}</span>}
                type="password" autoComplete="new-password" value={apiKey} disabled={!s.can_store_key || clearKey}
                placeholder={s.has_api_key ? t("ags.key_keep") : ""} hint={s.can_store_key ? t("ags.key_hint") : t("ags.key_unavailable")}
                onChange={(e) => setApiKey(e.target.value)} />
              {s.has_api_key && <div className="mt-2"><Tick label={t("ags.key_clear")} checked={clearKey} onChange={setClearKey} /></div>}
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <TextInput label={t("ags.temperature")} hint={t("ags.temperature_hint")} type="number" min={0} max={2} step={0.1}
                value={s.temperature ?? ""} onChange={(e) => set("temperature", e.target.value === "" ? null : Number(e.target.value))} />
              <TextInput label={t("ags.max_steps")} hint={t("ags.max_steps_hint")} type="number" min={1} max={20}
                value={s.max_steps} onChange={(e) => set("max_steps", num(e.target.value, 8))} />
            </div>
          </div>
        </Card>
        <Card title={t("ags.limits")}>
          <div className="grid gap-3 sm:grid-cols-3">
            <TextInput label={t("ags.proposal_minutes")} hint={t("ags.proposal_minutes_hint")} type="number" min={5} max={1440}
              value={s.proposal_minutes} onChange={(e) => set("proposal_minutes", num(e.target.value, 30))} />
            <TextInput label={t("ags.daily_limit")} hint={t("ags.daily_limit_hint")} type="number" min={1} max={5000}
              value={s.daily_run_limit} onChange={(e) => set("daily_run_limit", num(e.target.value, 200))} />
            <TextInput label={t("ags.history")} hint={t("ags.history_hint")} type="number" min={2} max={100}
              value={s.history_turns} onChange={(e) => set("history_turns", num(e.target.value, 30))} />
          </div>
        </Card>
        <Card title={t("ags.tools")}>
          <p className="mb-4 text-sm text-aeblack-600">{t("ags.tools_hint")}</p>
          <div className="space-y-5">
            {GROUPS.map(([group, match]) => (
              <fieldset key={group}>
                <legend className="mb-2 text-sm font-bold text-aeblack-800">{t(`ags.group.${group}`)}</legend>
                <ul className="grid gap-2 sm:grid-cols-2">
                  {s.tools.filter(match).map((tool) => (
                    <li key={tool}>
                      <Tick label={<>{t(`asst.tool.${tool}`)} <code dir="ltr" className="text-xs text-aeblack-400">{tool}</code></>}
                        checked={!s.disabled_tools.includes(tool)} onChange={(on) => toggleTool(tool, on)} />
                    </li>
                  ))}
                </ul>
              </fieldset>
            ))}
          </div>
        </Card>
      </div>
    </>
  );
}
