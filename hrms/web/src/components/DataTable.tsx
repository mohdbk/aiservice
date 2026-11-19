import { CaretDown, CaretUp, CaretUpDown, BookmarkSimple, FunnelSimple, Trash, MagnifyingGlass, Rows, SquareSplitVertical, X } from "@phosphor-icons/react";
import { Menu } from "@base-ui/react";
import { FormEvent, ReactNode, useEffect, useState } from "react";
import { useLocation, useSearchParams } from "react-router-dom";
import { api, ApiError, useAsync, useLang } from "../core";
import { Button, Empty, ErrorState, Loading, Modal, SelectInput, TextInput } from "./ui";

export type Page<T> = { items: T[]; total: number; page: number; size: number; pages: number; sort: string; dir: "asc" | "desc" };

export type Column<T> = { key: string; label: ReactNode; sort?: string; render: (row: T) => ReactNode; className?: string };

export type FilterDef = { key: string; label: string; options: { value: string; label: string }[] };

const LIST_KEYS = ["page", "size", "sort", "dir", "q"];

/**
 * A server-paged list whose state (page, size, sort, direction, search and filters) lives in the address, so a
 * view can be bookmarked, shared and survives a reload.
 */
export function useList<T>(path: string, filterKeys: string[], fixed: Record<string, string> = {}) {
  const [params, setParams] = useSearchParams();
  const keys = [...LIST_KEYS, ...filterKeys];
  const query = new URLSearchParams();
  for (const k of keys) {
    const v = params.get(k);
    if (v) query.set(k, v);
  }
  for (const [k, v] of Object.entries(fixed)) if (v) query.set(k, v);
  const qs = query.toString();
  const result = useAsync(() => api<Page<T>>(`${path}${path.includes("?") ? "&" : "?"}${qs}`), [path, qs]);
  const set = (changes: Record<string, string | null>) => {
    const p = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) p.set(k, v); else p.delete(k);
    }
    if (!("page" in changes)) p.delete("page");
    setParams(p, { replace: true });
  };
  const replace = (q: string) => setParams(new URLSearchParams(q), { replace: true });
  return { ...result, params, get: (k: string) => params.get(k) || "", set, replace };
}

type ListState<T> = ReturnType<typeof useList<T>>;

type View = { name: string; q: string };

/** Named filter and sort combinations for one list, kept in this browser, so a common view is one click away. */
function SavedViews({ current, apply }: { current: string; apply: (q: string) => void }) {
  const { t } = useLang();
  const where = useLocation().pathname.replace(/^\/(ar|en)/, "");
  const key = `mh-views:${where}`;
  const read = (): View[] => { try { return JSON.parse(localStorage.getItem(key) || "[]"); } catch { return []; } };
  const [views, setViews] = useState<View[]>(read);
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState("");
  const store = (v: View[]) => { setViews(v); try { localStorage.setItem(key, JSON.stringify(v)); } catch { /* private window */ } };
  const clean = (q: string) => { const p = new URLSearchParams(q); p.delete("page"); p.sort(); return p.toString(); };
  const now = clean(current);
  const save = () => { const n = name.trim(); if (!n) return; store([...views.filter((v) => v.name !== n), { name: n, q: now }].slice(-12)); setNaming(false); setName(""); };
  return (
    <>
      <Menu.Root>
        <Menu.Trigger className="aegov-btn btn-outline"><BookmarkSimple size={20} aria-hidden="true" />{t("views.title")}{views.length > 0 && <span className="text-aeblack-500" dir="ltr">({views.length})</span>}</Menu.Trigger>
        <Menu.Portal>
          <Menu.Positioner sideOffset={6} align="start" className="z-[80]">
            <Menu.Popup className="mh-popup min-w-56">
              {views.map((v) => (
                <div key={v.name} className="flex items-center">
                  <Menu.Item className={`mh-item flex-1 ${clean(v.q) === now ? "font-bold text-primary-800" : ""}`} onClick={() => apply(v.q)}>{v.name}</Menu.Item>
                  <Menu.Item className="mh-item px-2 text-aeblack-500" aria-label={t("views.delete", { name: v.name })} onClick={() => store(views.filter((x) => x.name !== v.name))}><Trash size={16} aria-hidden="true" /></Menu.Item>
                </div>
              ))}
              {views.length === 0 && <p className="px-3 py-2 text-xs text-aeblack-500">{t("views.none")}</p>}
              <Menu.Item className="mh-item border-t border-aeblack-100 font-semibold text-primary-700" disabled={!now} onClick={() => setNaming(true)}>{t("views.save")}</Menu.Item>
            </Menu.Popup>
          </Menu.Positioner>
        </Menu.Portal>
      </Menu.Root>
      <Modal open={naming} onClose={() => setNaming(false)} title={t("views.save")} footer={<>
        <Button variant="outline" onClick={() => setNaming(false)}>{t("common.cancel")}</Button>
        <Button disabled={!name.trim()} onClick={save}>{t("common.save")}</Button>
      </>}>
        <TextInput label={t("views.name")} hint={t("views.hint")} value={name} maxLength={60} autoFocus onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); save(); } }} />
      </Modal>
    </>
  );
}

export type Selection = { selected: (string | number)[]; onChange: (keys: (string | number)[]) => void; label: (row: unknown) => string };

/** Row spacing in every list, kept per browser: comfortable (the default) or compact. */
function useDensity() {
  const read = () => { try { return localStorage.getItem("mh-density") === "compact"; } catch { return false; } };
  const [compact, setCompact] = useState(read);
  const toggle = () => setCompact((c) => { try { localStorage.setItem("mh-density", c ? "comfortable" : "compact"); } catch { /* private window */ } return !c; });
  return { compact, toggle };
}

export function DataTable<T>({ list, columns, rowKey, filters = [], searchLabel, empty, emptyHint, emptyAction, toolbar, selection, bulk, body }: {
  list: ListState<T>; columns: Column<T>[]; rowKey: (row: T) => string | number; filters?: FilterDef[]; searchLabel?: string; empty?: ReactNode;
  emptyHint?: ReactNode; emptyAction?: ReactNode; toolbar?: ReactNode;
  /** Rows can be ticked; the bar of actions for the ticked rows shows above the list. */
  selection?: Selection; bulk?: ReactNode;
  /** Shown in place of the table when there are rows (another view of the same list, such as a board). */
  body?: ReactNode;
}) {
  const { t } = useLang();
  const density = useDensity();
  const keysOnPage = (list.data?.items || []).map(rowKey);
  const picked = new Set(selection?.selected || []);
  const allPicked = keysOnPage.length > 0 && keysOnPage.every((k) => picked.has(k));
  const somePicked = keysOnPage.some((k) => picked.has(k));
  const pickAll = () => selection?.onChange(allPicked ? selection.selected.filter((k) => !keysOnPage.includes(k)) : [...new Set([...selection.selected, ...keysOnPage])]);
  const pick = (k: string | number) => selection?.onChange(picked.has(k) ? selection.selected.filter((x) => x !== k) : [...selection.selected, k]);
  const [q, setQ] = useState(list.get("q"));
  const [showFilters, setShowFilters] = useState(false);
  useEffect(() => setQ(list.get("q")), [list.get("q")]);
  const data = list.data;
  const active = filters.some((f) => list.get(f.key)) || !!list.get("q");
  const submit = (e: FormEvent) => {
    e.preventDefault();
    list.set({ q: q.trim() || null });
  };
  const sortBy = (key: string) => {
    const current = data?.sort === key ? data.dir : null;
    list.set({ sort: key, dir: current === "asc" ? "desc" : "asc" });
  };
  const filtersOn = filters.filter((f) => list.get(f.key)).length;
  const clearAll = () => { setQ(""); list.set(Object.fromEntries(["q", ...filters.map((f) => f.key)].map((k) => [k, null]))); };
  const header = (c: Column<T>) => {
    const sorted = c.sort && data?.sort === c.sort ? data.dir : null;
    return c.sort ? (
      <button type="button" onClick={() => sortBy(c.sort!)} className="inline-flex items-center gap-1 font-semibold hover:text-primary-700">
        {c.label}
        {sorted === "asc" ? <CaretUp size={16} aria-hidden="true" /> : sorted === "desc" ? <CaretDown size={16} aria-hidden="true" /> : <CaretUpDown size={16} className="text-aeblack-300" aria-hidden="true" />}
      </button>
    ) : c.label;
  };
  const [first, ...rest] = columns;
  const actions = rest.filter((c) => c.key === "act");
  const details = rest.filter((c) => c.key !== "act");
  return (
    <div>
      <form onSubmit={submit} role="search" className="mb-4 space-y-3">
        <div className="flex items-end gap-2">
          <div className="aegov-form-control min-w-0 flex-1">
            <label htmlFor="list-q" className={searchLabel ? "mb-1 block text-sm font-semibold" : "sr-only"}>{searchLabel || t("common.search")}</label>
            <div className="form-control-input">
              <span className="control-prefix"><MagnifyingGlass size={20} aria-hidden="true" /></span>
              <input id="list-q" type="search" value={q} placeholder={searchLabel ? undefined : t("list.search_placeholder")} onChange={(e) => setQ(e.target.value)} />
            </div>
          </div>
          <Button type="submit" variant="outline" aria-label={t("common.search")} icon={<MagnifyingGlass size={20} aria-hidden="true" />}>
            <span className="hidden sm:inline">{t("common.search")}</span>
          </Button>
          {filters.length > 0 && <div className="hidden sm:block"><SavedViews current={list.params.toString()} apply={list.replace} /></div>}
          {(filters.length > 0 || toolbar) && (
            <Button variant="outline" className="sm:hidden" aria-expanded={showFilters} onClick={() => setShowFilters(!showFilters)}
              icon={<FunnelSimple size={20} aria-hidden="true" />} aria-label={t("list.filters")}>
              {filtersOn > 0 && <span className="rounded-full bg-primary-600 px-1.5 text-xs text-white" dir="ltr">{filtersOn}</span>}
            </Button>
          )}
        </div>
        {(filters.length > 0 || active || toolbar) && (
          <div className={`${showFilters ? "grid" : "hidden"} grid-cols-1 gap-3 sm:flex sm:flex-wrap sm:items-end`}>
            {filters.map((f) => (
              <SelectInput key={f.key} wrapClass="sm:min-w-40" label={f.label} value={list.get(f.key)} onChange={(e) => list.set({ [f.key]: e.target.value || null })}
                options={[{ value: "", label: t("common.all") }, ...f.options]} />
            ))}
            {toolbar}
            {filters.length > 0 && <div className="sm:hidden"><SavedViews current={list.params.toString()} apply={list.replace} /></div>}
            {active && (
              <Button variant="link" onClick={clearAll}
                icon={<X size={20} aria-hidden="true" />}>{t("list.clear")}</Button>
            )}
          </div>
        )}
      </form>
      {list.loading && !data ? <Loading /> : list.error ? <ErrorState error={list.error as ApiError} retry={list.reload} /> : !data || data.items.length === 0 ? (active
        // Nothing matches the search or filters: say so and offer to clear them (not "nothing here yet").
        ? <Empty icon={<MagnifyingGlass size={26} />} hint={t("list.no_match_hint")}
            action={<Button variant="outline" onClick={clearAll} icon={<X size={20} aria-hidden="true" />}>{t("list.clear")}</Button>}>{t("list.no_match")}</Empty>
        : <Empty hint={emptyHint} action={emptyAction}>{empty}</Empty>) : body ? <>{body}</> : (
        <>
          {selection && selection.selected.length > 0 && (
            <div role="region" aria-label={t("bulk.region")} className="sticky top-16 z-[5] mb-3 sm:top-20 flex flex-wrap items-center gap-2 rounded-2xl border border-primary-200 bg-primary-50 px-4 py-2.5 shadow-sm">
              <span className="me-2 text-sm font-bold text-primary-900">{t("bulk.selected", { count: selection.selected.length })}</span>
              {bulk}
              <Button variant="link" size="sm" className="ms-auto" onClick={() => selection.onChange([])} icon={<X size={18} aria-hidden="true" />}>{t("bulk.clear")}</Button>
            </div>
          )}
          {/* Wide screens: a table, its header kept in view while the rows scroll. */}
          <div className={`mh-table-wrap hidden rounded-2xl border border-aeblack-100 bg-white transition md:block ${list.loading ? "opacity-60" : ""}`}>
            <table className={`mh-table ${density.compact ? "mh-table-compact" : ""}`}>
              <caption className="sr-only">{t("list.total", { count: data.total })}</caption>
              <thead>
                <tr>
                  {selection && (
                    <th scope="col" className="w-10">
                      <input type="checkbox" className="mh-check" checked={allPicked} aria-label={t("bulk.all")}
                        ref={(el) => { if (el) el.indeterminate = somePicked && !allPicked; }} onChange={pickAll} />
                    </th>
                  )}
                  {columns.map((c) => {
                    const sorted = c.sort && data.sort === c.sort ? data.dir : null;
                    return (
                      <th key={c.key} scope="col" aria-sort={sorted === "asc" ? "ascending" : sorted === "desc" ? "descending" : undefined} className={c.className}>{header(c)}</th>
                    );
                  })}
                </tr>
              </thead>
              <tbody>
                {data.items.map((row) => (
                  <tr key={rowKey(row)} className={picked.has(rowKey(row)) ? "mh-row-picked" : ""}>
                    {selection && (
                      <td className="w-10">
                        <input type="checkbox" className="mh-check" checked={picked.has(rowKey(row))} aria-label={t("bulk.one", { name: selection.label(row) })} onChange={() => pick(rowKey(row))} />
                      </td>
                    )}
                    {columns.map((c) => <td key={c.key} className={c.className}>{c.render(row)}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {/* Phones: one card per row, the first column as its title and the others as labelled values. */}
          <ul className={`space-y-3 transition md:hidden ${list.loading ? "opacity-60" : ""}`} aria-label={t("list.total", { count: data.total })}>
            {data.items.map((row) => (
              <li key={rowKey(row)} className={`mc-surface rounded-2xl border bg-white p-4 ${picked.has(rowKey(row)) ? "border-primary-300 ring-1 ring-primary-200" : "border-aeblack-100"}`}>
                {first && <div className="mb-3 flex items-start gap-3 text-base font-semibold [&_a]:font-semibold">
                  {selection && <input type="checkbox" className="mh-check mt-1" checked={picked.has(rowKey(row))} aria-label={t("bulk.one", { name: selection.label(row) })} onChange={() => pick(rowKey(row))} />}
                  <div className="min-w-0 flex-1">{first.render(row)}</div>
                </div>}
                <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
                  {details.map((c) => (
                    <div key={c.key} className="min-w-0">
                      <dt className="text-xs text-aeblack-500">{c.label}</dt>
                      <dd className="mt-0.5 break-words">{c.render(row)}</dd>
                    </div>
                  ))}
                </dl>
                {actions.map((c) => <div key={c.key} className="mt-3 border-t border-aeblack-100 pt-3">{c.render(row)}</div>)}
              </li>
            ))}
          </ul>
          <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm text-aeblack-600">
            <span>{t("list.showing", { from: (data.page - 1) * data.size + 1, to: Math.min(data.page * data.size, data.total), total: data.total })}</span>
            <div className="flex flex-wrap items-center gap-3">
              <button type="button" onClick={density.toggle} aria-pressed={density.compact} className="hidden items-center gap-1.5 rounded-lg px-2 py-1 hover:bg-whitely-100 md:inline-flex">
                {density.compact ? <Rows size={18} aria-hidden="true" /> : <SquareSplitVertical size={18} aria-hidden="true" />}
                {density.compact ? t("list.comfortable") : t("list.compact")}
              </button>
              <label className="inline-flex items-center gap-2">
                {t("list.per_page")}
                <select className="rounded-lg border border-aeblack-200 bg-white px-2 py-1" value={data.size} onChange={(e) => list.set({ size: e.target.value })}>
                  {[10, 25, 50, 100].map((n) => <option key={n} value={n}>{n}</option>)}
                </select>
              </label>
              {data.pages > 1 && (
                <nav className="flex items-center gap-2" aria-label={t("list.pages")}>
                  <Button variant="outline" size="sm" disabled={data.page <= 1} onClick={() => list.set({ page: String(data.page - 1) })}>{t("common.previous")}</Button>
                  <span>{t("common.page", { page: data.page, pages: data.pages })}</span>
                  <Button variant="outline" size="sm" disabled={data.page >= data.pages} onClick={() => list.set({ page: String(data.page + 1) })}>{t("common.next")}</Button>
                </nav>
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

export const opts = (t: (k: string) => string, prefix: string, values: string[]) => values.map((v) => ({ value: v, label: t(`${prefix}${v}`) }));
