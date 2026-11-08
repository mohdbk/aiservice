// UAE Design System breadcrumb (aegov-breadcrumb). On phones only the "back" link is shown.
import { CaretLeft, CaretRight, House } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { useSite } from "../site";

type Crumb = { label: string; to?: string };

export function Breadcrumb({ items, back }: { items: Crumb[]; back?: string }) {
  const { t, path } = useSite();
  const parent = [...items].reverse().find((i) => i.to);
  return (
    <>
      <nav className="aegov-breadcrumb mb-6 hidden sm:block" aria-label="breadcrumb">
        <ol className="flex flex-wrap items-center gap-2 text-sm">
          <li className="flex items-center gap-2">
            <Link to={path()} className="inline-flex items-center gap-1"><House size={20} aria-hidden="true" /><span className="sr-only">{t("site.portal")}</span></Link>
          </li>
          {items.map((c, i) => (
            <li key={i} className="flex items-center gap-2">
              <CaretRight size={16} className="text-aeblack-400 rtl:-scale-x-100" aria-hidden="true" />
              {c.to && i < items.length - 1 ? <Link to={c.to}>{c.label}</Link> : <span aria-current="page" className="max-w-[40ch] truncate">{c.label}</span>}
            </li>
          ))}
        </ol>
      </nav>
      {parent?.to && (
        <nav className="mb-6 sm:hidden" aria-label="breadcrumb">
          <Link to={parent.to} className="inline-flex items-center gap-1 text-sm font-medium text-primary-700">
            <CaretLeft size={20} className="rtl:-scale-x-100" aria-hidden="true" />{back || parent.label}
          </Link>
        </nav>
      )}
    </>
  );
}
