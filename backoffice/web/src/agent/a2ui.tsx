import { ReactNode } from "react";
import { Button } from "../components/ui";

/**
 * A2UI v1.0 renderer (https://a2ui.org) for the basic catalog, drawn with UAE Design System (aegov) styles.
 * Surfaces are flat component lists keyed by id, drawn from "root"; values can bind to the surface's data model.
 */

export type A2Component = { id: string; component: string; [prop: string]: any };
export type A2Surface = { id: string; components: Record<string, A2Component>; data: Record<string, any> };
export type A2Surfaces = Record<string, A2Surface>;
export type A2Action = { name: string; surfaceId: string; sourceComponentId: string; timestamp: string; context: Record<string, any> };

const at = (data: any, path: string) =>
  path.split("/").filter(Boolean).reduce((v, k) => (v == null ? undefined : v[k]), data);

function setAt(data: Record<string, any>, path: string | undefined, value: any): Record<string, any> {
  const keys = (path || "").split("/").filter(Boolean);
  if (keys.length === 0) return value ?? {};
  const copy = structuredClone(data);
  let o = copy;
  keys.slice(0, -1).forEach((k) => (o = o[k] ??= {}));
  if (value === null) delete o[keys[keys.length - 1]];
  else o[keys[keys.length - 1]] = value;
  return copy;
}

/** Resolves {"@path": "/x"} bindings (and nested objects) against the data model. */
export function resolve(v: any, data: any): any {
  if (v && typeof v === "object" && !Array.isArray(v)) {
    if (typeof v["@path"] === "string") return at(data, v["@path"]);
    return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, resolve(x, data)]));
  }
  return v;
}

/** Applies A2UI server-to-client messages to the current surfaces. Unknown messages are ignored. */
export function applyMessages(surfaces: A2Surfaces, messages: any[]): { surfaces: A2Surfaces; touched: string[] } {
  const next = { ...surfaces };
  const touched: string[] = [];
  for (const m of messages) {
    if (!m || m.version !== "1.0") continue;
    if (m.createSurface) {
      const c = m.createSurface;
      next[c.surfaceId] = { id: c.surfaceId, components: {}, data: c.dataModel ?? {} };
      for (const x of c.components ?? []) next[c.surfaceId].components[x.id] = x;
      touched.push(c.surfaceId);
    } else if (m.updateComponents) {
      const u = m.updateComponents;
      const s = next[u.surfaceId] ?? { id: u.surfaceId, components: {}, data: {} };
      next[u.surfaceId] = { ...s, components: { ...s.components, ...Object.fromEntries((u.components ?? []).map((x: A2Component) => [x.id, x])) } };
      touched.push(u.surfaceId);
    } else if (m.updateDataModel) {
      const u = m.updateDataModel;
      const s = next[u.surfaceId];
      if (s) next[u.surfaceId] = { ...s, data: setAt(s.data, u.path, u.value) };
    } else if (m.deleteSurface) {
      delete next[m.deleteSurface.surfaceId];
    }
  }
  return { surfaces: next, touched: [...new Set(touched)] };
}

const TEXT: Record<string, string> = {
  h1: "text-2xl font-bold", h2: "text-xl font-bold", h3: "text-lg font-bold", h4: "text-base font-bold", h5: "text-sm font-bold",
  caption: "text-xs text-aeblack-500 whitespace-pre-line", body: "text-sm whitespace-pre-line",
};
const JUSTIFY: Record<string, string> = { start: "justify-start", center: "justify-center", end: "justify-end", spaceBetween: "justify-between", spaceAround: "justify-around" };

export function A2Surface({ surface, busy, onAction }: { surface: A2Surface; busy?: boolean; onAction: (a: A2Action) => void }) {
  const seen = new Set<string>();
  const draw = (id: string | undefined): ReactNode => {
    if (!id || seen.has(id)) return null; // guards against cycles
    const c = surface.components[id];
    if (!c) return null;
    seen.add(id);
    const v = (p: string) => resolve(c[p], surface.data);
    const kids = (): ReactNode[] => (Array.isArray(c.children) ? c.children.map((k: string) => <div key={k} className="min-w-0 flex-1">{draw(k)}</div>) : []);
    switch (c.component) {
      case "Text": {
        const variant = (v("variant") as string) || "body";
        const text = String(v("text") ?? "");
        const Tag = variant.startsWith("h") ? (`h${Math.min(6, Number(variant.slice(1)) + 1)}` as "h2") : "p";
        return <Tag dir="auto" className={TEXT[variant] ?? TEXT.body}>{text}</Tag>;
      }
      case "Row":
        return <div className={`flex flex-wrap items-center gap-3 ${JUSTIFY[c.justify] ?? ""}`}>{kids()}</div>;
      case "Column":
      case "List":
        return <div className="flex flex-col gap-3">{Array.isArray(c.children) ? c.children.map((k: string) => <div key={k}>{draw(k)}</div>) : null}</div>;
      case "Card":
        return <div className="rounded-xl border border-aeblack-100 bg-white p-4 shadow-sm">{draw(c.child)}</div>;
      case "Divider":
        return <hr className="border-aeblack-100" />;
      case "Image":
        return <img src={String(v("url") ?? "")} alt={String(v("description") ?? "")} className="max-h-60 rounded-lg" />;
      case "Button": {
        const ev = c.action?.event;
        const label = surface.components[c.child];
        return (
          <Button size="sm" variant={c.variant === "borderless" ? "link" : "primary"} busy={busy && c.variant !== "borderless"}
            disabled={!ev || busy}
            onClick={() => ev && onAction({
              name: ev.name, surfaceId: surface.id, sourceComponentId: c.id, timestamp: new Date().toISOString(), context: resolve(ev.context ?? {}, surface.data),
            })}>
            {label?.component === "Text" ? String(resolve(label.text, surface.data) ?? "") : draw(c.child)}
          </Button>
        );
      }
      default:
        return null;
    }
  };
  return <div className="mt-3">{draw("root")}</div>;
}
