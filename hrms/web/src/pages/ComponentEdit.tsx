import {
  ArrowDown, ArrowUp, CalendarBlank, CheckSquare, Clock, DotsSixVertical, Hash, ListBullets, ListChecks, Minus, Plus, Rows, TextAa, TextAlignLeft, TextH, Trash,
} from "@phosphor-icons/react";
import { ReactNode, useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { A2Surface, A2Surfaces, applyMessages, writeData } from "../agent/a2ui";
import { ActionMenu, Button, Card, ErrorState, FlagLabel, Loading, Note, PageHeader, SectionTabs, SelectInput, TextArea, TextInput, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";
import { ACTIONS } from "./Components";
import { useLookups } from "./JobEdit";

type L = { ar: string; en: string };
type Opt = { value: string; label: L };
type FieldNode = { type: "field"; name: string; input: string; label: L; required?: boolean; default?: string; source?: string; options?: Opt[] };
type Node = { type: "heading" | "text"; text: L } | { type: "divider" } | FieldNode | { type: "row"; children: FieldNode[] };
type Definition = { title: L; submit: L; nodes: Node[] };
type Form = {
  id?: number; key: string; name: string; name_en: string; purpose: string; kind: "Ask" | "Approval"; action_name: string | null;
  is_active: boolean; row_version?: string; definition: Definition;
};

const INPUTS = ["text", "longtext", "number", "date", "time", "choice", "multichoice", "checkbox"] as const;
const SOURCES = ["rounds", "staff", "assessments", "departments", "statuses", "employment_types"];
const ICONS: Record<string, ReactNode> = {
  text: <TextAa size={20} />, longtext: <TextAlignLeft size={20} />, number: <Hash size={20} />, date: <CalendarBlank size={20} />, time: <Clock size={20} />,
  choice: <ListBullets size={20} />, multichoice: <ListChecks size={20} />, checkbox: <CheckSquare size={20} />,
};
const blank = (): L => ({ ar: "", en: "" });
const EMPTY: Form = {
  key: "", name: "", name_en: "", purpose: "", kind: "Ask", action_name: null, is_active: true,
  definition: { title: blank(), submit: blank(), nodes: [] },
};

function move<T>(list: T[], from: number, to: number) {
  if (to < 0 || to >= list.length || from === to) return list;
  const copy = [...list];
  const [x] = copy.splice(from, 1);
  copy.splice(to, 0, x);
  return copy;
}

/** Builds the same A2UI messages the agent sends, so the preview shows exactly what staff will see in the chat. */
function toA2ui(f: Form, lang: "ar" | "en", lists: Record<string, Opt[]>, t: (k: string) => string): any[] {
  const pick = (l?: L) => (l?.[lang] || l?.[lang === "ar" ? "en" : "ar"] || "");
  const components: any[] = [];
  const data: Record<string, any> = {};
  let n = 0;
  const add = (c: any) => {
    const id = c.id ?? `c${++n}`;
    components.push({ ...c, id });
    return id;
  };
  const field = (x: FieldNode) => {
    const path = `/form/${x.name || "field"}`;
    const label = pick(x.label) || x.name;
    const options = (x.source ? lists[x.source] ?? [] : x.options ?? []).map((o) => ({ value: o.value, label: pick(o.label) || o.value }));
    data[x.name || "field"] = x.input === "multichoice" ? (x.default ? x.default.split(",").map((s) => s.trim()) : []) : x.input === "checkbox" ? x.default === "true" : x.default ?? "";
    const value = { "@path": path };
    switch (x.input) {
      case "longtext": return add({ component: "TextField", label, value, textFieldType: "longText", required: !!x.required });
      case "number": return add({ component: "TextField", label, value, textFieldType: "number", required: !!x.required });
      case "date": return add({ component: "DateTimeInput", label, value, enableDate: true, enableTime: false, required: !!x.required });
      case "time": return add({ component: "DateTimeInput", label, value, enableDate: false, enableTime: true, required: !!x.required });
      case "choice": return add({ component: "ChoicePicker", label, value, options, maxAllowedSelections: 1, required: !!x.required });
      case "multichoice": return add({ component: "ChoicePicker", label, value, options, maxAllowedSelections: null, required: !!x.required });
      case "checkbox": return add({ component: "CheckBox", label, value });
      default: return add({ component: "TextField", label, value, textFieldType: "shortText", required: !!x.required });
    }
  };
  const parts: string[] = [];
  const say = (k: string) => t(`comp.preview.${k}.${lang}`);
  if (f.kind === "Approval") {
    parts.push(add({ component: "Text", text: say("approval"), variant: "h5" }));
    parts.push(add({ component: "Text", text: say("label"), variant: "body" }));
  } else if (pick(f.definition.title)) parts.push(add({ component: "Text", text: pick(f.definition.title), variant: "h5" }));
  for (const node of f.definition.nodes) {
    if (node.type === "heading") parts.push(add({ component: "Text", text: pick(node.text), variant: "h5" }));
    else if (node.type === "text") parts.push(add({ component: "Text", text: pick(node.text), variant: "caption" }));
    else if (node.type === "divider") parts.push(add({ component: "Divider", axis: "horizontal" }));
    else if (node.type === "field") parts.push(field(node));
    else if (node.type === "row") parts.push(add({ component: "Row", children: node.children.map(field), justify: "start", align: "center" }));
  }
  const required = f.definition.nodes.flatMap((x) => (x.type === "row" ? x.children : x.type === "field" ? [x] : []))
    .filter((x) => x.required && x.input !== "checkbox").map((x) => ({ "@call": "required", args: { value: { "@path": `/form/${x.name}` } } }));
  const button = (text: string, name: string, variant: string, checks?: any[]) =>
    add({ component: "Button", child: add({ component: "Text", text }), variant, checks, action: { event: { name, context: {} } } });
  const buttons = f.kind === "Approval"
    ? [button(say("approve"), "preview", "primary", required), button(say("cancel"), "preview", "borderless")]
    : [button(pick(f.definition.submit) || say("submit"), "preview", "primary", required)];
  parts.push(add({ id: "actions", component: "Row", children: buttons }));
  add({ id: "root", component: "Card", child: add({ component: "Column", children: parts }) });
  return [
    { version: "1.0", createSurface: { surfaceId: "preview", catalogId: "basic", dataModel: { form: data, state: "open" } } },
    { version: "1.0", updateComponents: { surfaceId: "preview", components } },
  ];
}

function BiInputs({ label, value, onChange, long }: { label: string; value: L; onChange: (v: L) => void; long?: boolean }) {
  const { t } = useLang();
  const Input = long ? TextArea : TextInput;
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {(["ar", "en"] as const).map((l) => (
        <Input key={l} label={<FlagLabel lang={l}>{label} ({t(`comp.lang.${l}`)})</FlagLabel>}
          dir={l === "ar" ? "rtl" : "ltr"} lang={l} value={value[l]} {...(long ? { rows: 2 } : {})}
          onChange={(e: React.ChangeEvent<HTMLInputElement & HTMLTextAreaElement>) => onChange({ ...value, [l]: e.target.value })} />
      ))}
    </div>
  );
}

function FieldEditor({ field, onChange, editable }: { field: FieldNode; onChange: (f: FieldNode) => void; editable: string[] | null }) {
  const { t } = useLang();
  const choice = field.input === "choice" || field.input === "multichoice";
  const options = field.options ?? [];
  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-3">
        {editable
          ? <SelectInput required label={t("comp.field_name")} value={field.name} onChange={(e) => onChange({ ...field, name: e.target.value })}
              options={[{ value: "", label: t("common.choose") }, ...editable.map((x) => ({ value: x, label: x }))]} />
          : <TextInput required label={t("comp.field_name")} dir="ltr" value={field.name} hint={t("comp.field_name_hint")}
              onChange={(e) => onChange({ ...field, name: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, "_") })} />}
        <SelectInput required label={t("comp.input")} value={field.input} onChange={(e) => onChange({ ...field, input: e.target.value })}
          options={INPUTS.map((x) => ({ value: x, label: t(`comp.input.${x}`) }))} />
        <TextInput label={t("comp.default")} dir="auto" value={field.default ?? ""} onChange={(e) => onChange({ ...field, default: e.target.value })} />
      </div>
      <BiInputs label={t("comp.label")} value={field.label} onChange={(label) => onChange({ ...field, label })} />
      {field.input !== "checkbox" && <Toggle label={t("comp.required")} checked={!!field.required} onChange={(v) => onChange({ ...field, required: v })} />}
      {choice && (
        <div className="space-y-2 rounded-xl bg-whitely-100 p-3">
          <SelectInput label={t("comp.options_from")} value={field.source ?? ""} onChange={(e) => onChange({ ...field, source: e.target.value || undefined })}
            options={[{ value: "", label: t("comp.source.custom") }, ...SOURCES.map((x) => ({ value: x, label: t(`comp.source.${x}`) }))]} />
          {!field.source && <>
            {options.map((o, i) => (
              <div key={i} className="grid items-end gap-2 sm:grid-cols-[8rem_1fr_1fr_auto]">
                <TextInput required label={t("comp.option_value")} dir="ltr" value={o.value} onChange={(e) => onChange({ ...field, options: options.map((x, k) => (k === i ? { ...x, value: e.target.value } : x)) })} />
                <TextInput label={<FlagLabel lang="ar">{t("comp.option_label")}</FlagLabel>} dir="rtl" value={o.label.ar}
                  onChange={(e) => onChange({ ...field, options: options.map((x, k) => (k === i ? { ...x, label: { ...x.label, ar: e.target.value } } : x)) })} />
                <TextInput label={<FlagLabel lang="en">{t("comp.option_label")}</FlagLabel>} dir="ltr" value={o.label.en}
                  onChange={(e) => onChange({ ...field, options: options.map((x, k) => (k === i ? { ...x, label: { ...x.label, en: e.target.value } } : x)) })} />
                <Button variant="link" className="pb-3" aria-label={t("common.remove")} onClick={() => onChange({ ...field, options: options.filter((_, k) => k !== i) })}><Trash size={20} /></Button>
              </div>
            ))}
            <Button variant="outline" size="sm" icon={<Plus size={20} aria-hidden="true" />}
              onClick={() => onChange({ ...field, options: [...options, { value: `option_${options.length + 1}`, label: blank() }] })}>{t("q.add_option")}</Button>
          </>}
        </div>
      )}
    </div>
  );
}

export default function ComponentEdit() {
  const { id } = useParams();
  const { t, to, lang } = useLang();
  const toast = useToast();
  const nav = useNavigate();
  const isNew = !id || id === "new";
  usePageTitle(isNew ? t("comp.new") : t("comp.edit"));
  const loaded = useAsync(() => (isNew ? Promise.resolve(EMPTY) : api<Form>(`/components/${id}`)), [id]);
  const agentInfo = useAsync(() => api<{ actions: Record<string, string[]> }>("/agent/components"), []);
  const lookups = useLookups();
  const [f, setF] = useState<Form | null>(null);
  const [open, setOpen] = useState<number | null>(null);
  const [drag, setDrag] = useState<number | null>(null);
  const [previewLang, setPreviewLang] = useState<"ar" | "en">(lang === "en" ? "en" : "ar");
  const [surfaces, setSurfaces] = useState<A2Surfaces>({});
  const save = useSaving();

  useEffect(() => {
    if (loaded.data) setF({ ...EMPTY, ...loaded.data, definition: { ...EMPTY.definition, ...loaded.data.definition } });
  }, [loaded.data]);

  const lists = useMemo<Record<string, Opt[]>>(() => {
    const l = lookups.data as any;
    const opt = (rows: any[] | undefined, ar: string, en = ar) => (rows ?? []).map((r) => ({ value: String(r.id), label: { ar: String(r[ar] ?? ""), en: String(r[en] || r[ar] || "") } }));
    const fixed = (keys: string[], prefix: string) => keys.map((k) => ({ value: k, label: { ar: t(`${prefix}${k}.ar`), en: t(`${prefix}${k}.en`) } }));
    return {
      rounds: opt(l?.rounds, "name"), staff: opt(l?.staff, "name"), assessments: opt(l?.assessments, "title"), departments: opt(l?.departments, "name", "name_en"),
      statuses: fixed(["Open", "Replied", "Hold", "Accepted", "Rejected"], "comp.status."),
      employment_types: fixed(["FullTime", "PartTime", "Contract", "Temporary", "Internship"], "comp.employment."),
    };
  }, [lookups.data, t]);

  useEffect(() => {
    if (f) setSurfaces(applyMessages({}, toA2ui(f, previewLang, lists, t)).surfaces);
  }, [f, previewLang, lists, t]);

  if (loaded.loading || !f) return <Loading />;
  if (loaded.error) return <ErrorState error={loaded.error} retry={loaded.reload} />;

  const set = <K extends keyof Form>(k: K, v: Form[K]) => setF((x) => (x ? { ...x, [k]: v } : x));
  const setDef = (d: Partial<Definition>) => setF((x) => (x ? { ...x, definition: { ...x.definition, ...d } } : x));
  const nodes = f.definition.nodes;
  const setNode = (i: number, n: Node) => setDef({ nodes: nodes.map((x, k) => (k === i ? n : x)) });
  const editable = f.kind === "Approval" && f.action_name ? agentInfo.data?.actions?.[f.action_name] ?? [] : null;
  const newField = (): FieldNode => ({ type: "field", name: editable?.[0] ?? "", input: "text", label: blank() });
  const addNode = (n: Node) => {
    setDef({ nodes: [...nodes, n] });
    setOpen(nodes.length);
  };

  const submit = async () => {
    const body = { ...f, action_name: f.kind === "Approval" ? f.action_name : null };
    const r = await save.run(() => api<{ id: number }>(isNew ? "/components" : `/components/${id}`, { method: isNew ? "POST" : "PUT", body }));
    if (r) {
      toast(t("common.saved"));
      if (isNew) nav(to(`/setup/components/${r.id}`), { replace: true });
      else loaded.reload();
    }
  };
  const remove = async () => {
    if (!window.confirm(t("comp.delete_confirm"))) return;
    if (await save.run(() => api(`/components/${id}`, { method: "DELETE" })) !== undefined) {
      toast(t("common.saved"));
      nav(to("/setup/components"));
    }
  };

  const summary = (n: Node): ReactNode => {
    const pick = (l: L) => l[lang as "ar" | "en"] || l.ar || l.en;
    switch (n.type) {
      case "heading": return <span className="inline-flex items-center gap-2"><TextH size={20} aria-hidden="true" />{pick(n.text) || t("comp.node.heading")}</span>;
      case "text": return <span className="inline-flex items-center gap-2"><TextAlignLeft size={20} aria-hidden="true" />{pick(n.text) || t("comp.node.text")}</span>;
      case "divider": return <span className="inline-flex items-center gap-2"><Minus size={20} aria-hidden="true" />{t("comp.node.divider")}</span>;
      case "field": return <span className="inline-flex items-center gap-2">{ICONS[n.input]}{pick(n.label) || n.name || t("comp.node.field")}<code dir="ltr" className="text-xs text-aeblack-500">{n.name}</code></span>;
      case "row": return <span className="inline-flex items-center gap-2"><Rows size={20} aria-hidden="true" />{t("comp.node.row")} ({n.children.length})</span>;
    }
  };

  return (
    <>
      <PageHeader back={<Link to={to("/setup/components")} className="mb-2 inline-block text-sm text-primary-700">{t("nav.components")}</Link>}
        title={isNew ? t("comp.new") : f.name || t("comp.edit")}
        actions={<>
          {!isNew && <Button variant="danger" onClick={remove} icon={<Trash size={20} aria-hidden="true" />}>{t("common.delete")}</Button>}
          <Button busy={save.busy} onClick={submit}>{t("common.save")}</Button>
        </>} />
      {save.error && <div className="mb-4"><ErrorState error={save.error} /></div>}
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_26rem]">
        <div className="space-y-6">
          <Card title={t("comp.details")}>
            <div className="space-y-4">
              <div className="grid gap-3 sm:grid-cols-2">
                <TextInput required label={<FlagLabel lang="ar">{t("comp.name")}</FlagLabel>} dir="rtl" value={f.name} onChange={(e) => set("name", e.target.value)} />
                <TextInput required label={<FlagLabel lang="en">{t("comp.name_en")}</FlagLabel>} dir="ltr" value={f.name_en} onChange={(e) => set("name_en", e.target.value)} />
              </div>
              <div className="grid gap-3 sm:grid-cols-3">
                <TextInput required label={t("comp.key")} dir="ltr" value={f.key} hint={t("comp.key_hint")}
                  onChange={(e) => set("key", e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, "_"))} />
                <SelectInput required label={t("comp.kind")} value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value as Form["kind"], action_name: e.target.value === "Approval" ? f.action_name ?? ACTIONS[0] : null })}
                  options={[{ value: "Ask", label: t("comp.kind.Ask") }, { value: "Approval", label: t("comp.kind.Approval") }]} />
                {f.kind === "Approval" && <SelectInput required label={t("comp.action")} value={f.action_name ?? ""} onChange={(e) => set("action_name", e.target.value)}
                  options={ACTIONS.map((a) => ({ value: a, label: t(`comp.action.${a}`) }))} />}
              </div>
              <Note>{t(f.kind === "Approval" ? "comp.kind_help.Approval" : "comp.kind_help.Ask")}</Note>
              <TextArea required label={t("comp.purpose")} hint={t("comp.purpose_hint")} rows={2} dir="auto" value={f.purpose} onChange={(e) => set("purpose", e.target.value)} />
              {f.kind === "Ask" && <>
                <BiInputs label={t("comp.form_title")} value={f.definition.title} onChange={(title) => setDef({ title })} />
                <BiInputs label={t("comp.submit_label")} value={f.definition.submit} onChange={(submit) => setDef({ submit })} />
              </>}
              <Toggle label={t("common.active")} checked={f.is_active} onChange={(v) => set("is_active", v)} />
            </div>
          </Card>

          <Card title={t("comp.layout")} actions={
            <ActionMenu label={t("comp.add")} icon={<Plus size={20} aria-hidden="true" />} items={[
              { label: t("comp.node.field"), onClick: () => addNode(newField()) },
              { label: t("comp.node.row"), onClick: () => addNode({ type: "row", children: [newField(), newField()] }) },
              { label: t("comp.node.heading"), onClick: () => addNode({ type: "heading", text: blank() }) },
              { label: t("comp.node.text"), onClick: () => addNode({ type: "text", text: blank() }) },
              { label: t("comp.node.divider"), onClick: () => addNode({ type: "divider" }) },
            ]} />}>
            {nodes.length === 0 && <Note>{t("comp.empty")}</Note>}
            <ol className="space-y-2">
              {nodes.map((n, i) => (
                <li key={i} draggable onDragStart={() => setDrag(i)} onDragOver={(e) => e.preventDefault()}
                  onDrop={() => { if (drag !== null) { setDef({ nodes: move(nodes, drag, i) }); setOpen(null); } setDrag(null); }}
                  className={`rounded-xl border bg-white ${open === i ? "border-primary-400" : "border-aeblack-100"} ${drag === i ? "opacity-50" : ""}`}>
                  <div className="flex items-center gap-2 p-3">
                    <DotsSixVertical size={20} className="cursor-grab text-aeblack-400" aria-hidden="true" />
                    <button type="button" className="min-w-0 flex-1 text-start font-semibold" aria-expanded={open === i} onClick={() => setOpen(open === i ? null : i)}>{summary(n)}</button>
                    <Button variant="link" aria-label={t("comp.move_up")} disabled={i === 0} onClick={() => setDef({ nodes: move(nodes, i, i - 1) })}><ArrowUp size={20} /></Button>
                    <Button variant="link" aria-label={t("comp.move_down")} disabled={i === nodes.length - 1} onClick={() => setDef({ nodes: move(nodes, i, i + 1) })}><ArrowDown size={20} /></Button>
                    <Button variant="link" aria-label={t("common.remove")} onClick={() => { setDef({ nodes: nodes.filter((_, k) => k !== i) }); setOpen(null); }}><Trash size={20} /></Button>
                  </div>
                  {open === i && (
                    <div className="border-t border-aeblack-100 p-4">
                      {(n.type === "heading" || n.type === "text") && <BiInputs label={t(`comp.node.${n.type}`)} long={n.type === "text"} value={n.text} onChange={(text) => setNode(i, { ...n, text })} />}
                      {n.type === "divider" && <p className="text-sm text-aeblack-500">{t("comp.divider_help")}</p>}
                      {n.type === "field" && <FieldEditor field={n} editable={editable} onChange={(x) => setNode(i, x)} />}
                      {n.type === "row" && (
                        <div className="space-y-4">
                          {n.children.map((c, k) => (
                            <div key={k} className="rounded-xl border border-aeblack-100 p-3">
                              <div className="mb-2 flex items-center justify-between">
                                <span className="text-sm font-semibold">{t("comp.column", { n: k + 1 })}</span>
                                <Button variant="link" aria-label={t("common.remove")} disabled={n.children.length === 1}
                                  onClick={() => setNode(i, { ...n, children: n.children.filter((_, j) => j !== k) })}><Trash size={20} /></Button>
                              </div>
                              <FieldEditor field={c} editable={editable} onChange={(x) => setNode(i, { ...n, children: n.children.map((y, j) => (j === k ? x : y)) })} />
                            </div>
                          ))}
                          {n.children.length < 4 && <Button variant="outline" size="sm" icon={<Plus size={20} aria-hidden="true" />}
                            onClick={() => setNode(i, { ...n, children: [...n.children, newField()] })}>{t("comp.add_column")}</Button>}
                        </div>
                      )}
                    </div>
                  )}
                </li>
              ))}
            </ol>
          </Card>
        </div>

        <div className="lg:sticky lg:top-24 lg:self-start">
          <Card title={t("comp.preview")}>
            <SectionTabs value={previewLang} onChange={(v) => setPreviewLang(v as "ar" | "en")} tabs={[
              { value: "ar", label: <FlagLabel lang="ar"><span lang="ar">العربية</span></FlagLabel> },
              { value: "en", label: <FlagLabel lang="en"><span lang="en">English</span></FlagLabel> },
            ]} />
            <div dir={previewLang === "ar" ? "rtl" : "ltr"} lang={previewLang} className="mh-native rounded-2xl bg-whitely-50 p-3">
              <div className="mh-chat-bot rounded-2xl px-4 py-3 text-sm">
                {surfaces.preview && <A2Surface surface={surfaces.preview} onAction={() => toast(t("comp.preview_only"), "info")} emptyText={t(`comp.preview.choose.${previewLang}`)}
                  onData={(sid, path, value) => setSurfaces((s) => writeData(s, sid, path, value))} />}
              </div>
            </div>
            <p className="mt-3 text-xs text-aeblack-500">{t("comp.preview_help")}</p>
          </Card>
        </div>
      </div>
    </>
  );
}
