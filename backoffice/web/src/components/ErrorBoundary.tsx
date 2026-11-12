import { Component, ErrorInfo, ReactNode } from "react";

/**
 * Keeps one broken screen from blanking the whole backoffice: shows a short message with a retry button and logs the
 * error. Wording is bilingual because this can render outside the language context.
 */
export class ErrorBoundary extends Component<{ children: ReactNode; compact?: boolean }, { error: Error | null }> {
  state = { error: null as Error | null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("[backoffice] screen error", error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div role="alert" className={`rounded-xl border border-aered-200 bg-aered-50 text-aered-800 ${this.props.compact ? "m-3 p-3 text-sm" : "m-4 p-6"}`}>
        <p className="font-semibold" lang="ar" dir="rtl">حدث خطأ في هذه الشاشة.</p>
        <p className="font-semibold" lang="en" dir="ltr">Something went wrong on this screen.</p>
        <button type="button" className="aegov-btn btn-outline btn-sm mt-3" onClick={() => this.setState({ error: null })}>
          <span lang="ar">إعادة المحاولة</span> / <span lang="en">Try Again</span>
        </button>
      </div>
    );
  }
}
