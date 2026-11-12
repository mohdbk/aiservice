import { CaretDown, CaretUp, CaretUpDown, FunnelSimple, MagnifyingGlass, X } from "@phosphor-icons/react";
import { FormEvent, ReactNode, useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api, ApiError, useAsync, useLang } from "../core";
import { Button, Empty, ErrorState, Loading, SelectInput } from "./ui";

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
  return { ...result, params, get: (k: string) => params.get(k) || "", set };
}

type ListState<T> = ReturnType<typeof useList<T>>;

export function DataTable<T>({ list, columns, rowKey, filters = [], searchLabel, empty, toolbar }: {
  list: ListState<T>; columns: Column<T>[]; rowKey: (row: T) => string | number; filters?: FilterDef[]; searchLabel?: string; empty?: ReactNode; toolbar?: ReactNode;
}) {
  const { t } = useLang();
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
            <label htmlFor="list-q" className="mb-1 block text-sm font-semibold">{searchLabel || t("common.search")}</label>
            <div className="form-control-input">
              <span className="control-prefix"><MagnifyingGlass size={20} aria-hidden="true" /></span>
              <input id="list-q" type="search" value={q} onChange={(e) => setQ(e.target.value)} />
            </div>
          </div>
          <Button type="submit" variant="outline" aria-label={t("common.search")} icon={<MagnifyingGlass size={20} aria-hidden="true" />}>
            <span className="hidden sm:inline">{t("common.search")}</span>
          </Button>
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
            {active && (
              <Button variant="link" onClick={() => { setQ(""); list.set(Object.fromEntries(["q", ...filters.map((f) => f.key)].map((k) => [k, null]))); }}
                icon={<X size={20} aria-hidden="true" />}>{t("list.clear")}</Button>
            )}
            {toolbar}
          </div>
        )}
      </form>
      {list.loading && !data ? <Loading /> : list.error ? <ErrorState error={list.error as ApiError} retry={list.reload} /> : !data || data.items.length === 0 ? <Empty>{empty}</Empty> : (
        <>
          {/* Wide screens: a table. */}
          <div className={`hidden overflow-x-auto rounded-2xl border border-aeblack-100 bg-white transition md:block ${list.loading ? "opacity-60" : ""}`}>
            <table className="mh-table">
              <caption className="sr-only">{t("list.total", { count: data.total })}</caption>
              <thead>
                <tr>
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
                  <tr key={rowKey(row)}>{columns.map((c) => <td key={c.key} className={c.className}>{c.render(row)}</td>)}</tr>
                ))}
              </tbody>
            </table>
          </div>
          {/* Phones: one card per row, the first column as its title and the others as labelled values. */}
          <ul className={`space-y-3 transition md:hidden ${list.loading ? "opacity-60" : ""}`} aria-label={t("list.total", { count: data.total })}>
            {data.items.map((row) => (
              <li key={rowKey(row)} className="mc-surface rounded-2xl border border-aeblack-100 bg-white p-4">
                {first && <div className="mb-3 text-base font-semibold [&_a]:font-semibold">{first.render(row)}</div>}
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
