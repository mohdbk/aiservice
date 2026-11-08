import { ReactNode } from "react";

export function Spinner({ label }: { label?: string }) {
  return (
    <span className="inline-flex items-center gap-3" role="status">
      <span className="inline-block h-5 w-5 animate-spin rounded-full border-2 border-primary-600 border-t-transparent motion-reduce:animate-none" aria-hidden="true" />
      {label ? <span>{label}</span> : <span className="sr-only">Loading</span>}
    </span>
  );
}

export function FullPageLoading() {
  return (
    <div className="flex min-h-[60vh] items-center justify-center">
      <Spinner />
    </div>
  );
}

export function FullPageError({ onRetry }: { onRetry: () => void }) {
  // Wording is not loaded yet, so this one screen is bilingual.
  return (
    <div className="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center gap-4 px-4 text-center">
      <p className="text-lg font-semibold" lang="ar" dir="rtl">الخدمة غير متاحة مؤقتا. يرجى المحاولة بعد قليل.</p>
      <p className="text-aeblack-600" lang="en" dir="ltr">The service is temporarily unavailable. Please try again shortly.</p>
      <button type="button" className="aegov-btn btn-outline btn-sm" onClick={onRetry}>إعادة المحاولة / Try again</button>
    </div>
  );
}

export function Alert({ tone = "info", title, children, id }: { tone?: "info" | "success" | "warning" | "error"; title?: string; children?: ReactNode; id?: string }) {
  return (
    <div id={id} className={`aegov-alert alert-${tone}`} role={tone === "error" ? "alert" : "status"} tabIndex={-1}>
      <div className="alert-content">
        {title && <p className="alert-title">{title}</p>}
        {children && <div>{children}</div>}
      </div>
    </div>
  );
}

export function EmptyState({ title, body, action }: { title: string; body?: string; action?: ReactNode }) {
  return (
    <div className="mc-surface rounded-2xl border border-dashed border-aeblack-200 bg-white px-6 py-12 text-center">
      <svg className="mx-auto mb-4 h-12 w-12 text-primary-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
        <path d="M3 7h18v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z" />
        <path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
      </svg>
      <h2 className="font-heading text-xl font-semibold text-aeblack-900">{title}</h2>
      {body && <p className="mx-auto mt-2 max-w-md text-aeblack-600">{body}</p>}
      {action && <div className="mt-6">{action}</div>}
    </div>
  );
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`mc-skeleton ${className}`} aria-hidden="true" />;
}
