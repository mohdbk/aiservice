import { ReactNode, useEffect, useRef } from "react";
import { Navigate, Route, Routes, useLocation, useParams } from "react-router-dom";
import { api, ApiError, SiteConfig } from "./api";
import { useAuth } from "./auth";
import Layout from "./components/Layout";
import { FullPageError, FullPageLoading } from "./components/States";
import { SiteProvider, useSite } from "./site";
import { useAsync } from "./useAsync";
import Application from "./pages/Application";
import Applications from "./pages/Applications";
import Apply from "./pages/Apply";
import JobPage from "./pages/Job";
import Jobs from "./pages/Jobs";
import NotFound from "./pages/NotFound";
import SignIn from "./pages/SignIn";
import SignUp from "./pages/SignUp";
import AssessmentIntro from "./pages/AssessmentIntro";
import Assessments from "./pages/Assessments";
import TakeAssessment from "./pages/TakeAssessment";
import Account from "./pages/Account";
import { CheckEmail, ForgotPassword, ResetPassword, VerifyEmail } from "./pages/AccountPages";

const LANG_RE = /^[a-z]{2,3}(-[A-Za-z]{2,4})?$/;

function RootRedirect() {
  const { data, error } = useAsync((signal) => api<{ default_language: string }>("/api/config", { signal }), []);
  if (error) return <Navigate to="/ar" replace />;
  if (!data) return <FullPageLoading />;
  return <Navigate to={`/${data.default_language || "ar"}`} replace />;
}

function LangShell() {
  const { lang = "" } = useParams();
  const location = useLocation();
  const valid = LANG_RE.test(lang);
  const { data, error, reload } = useAsync(
    (signal) => (valid ? api<SiteConfig>(`/api/config?lang=${encodeURIComponent(lang)}`, { signal }) : Promise.reject(new ApiError("unknown_language", 404))),
    [lang],
  );
  if (error instanceof ApiError && error.code === "unknown_language") {
    // /fr/... when French is not enabled: go to the default language, same page.
    const fallback = (error.params.default as string) || "ar";
    const rest = valid ? location.pathname.slice(lang.length + 1) : location.pathname;
    return <Navigate to={`/${fallback}${rest}${location.search}`} replace />;
  }
  if (error) return <FullPageError onRetry={reload} />;
  if (!data) return <FullPageLoading />;
  return (
    <SiteProvider config={data}>
      <Layout>
        <ScrollAndFocus />
        <Routes>
          <Route index element={<Jobs />} />
          <Route path="jobs/:id" element={<JobPage />} />
          <Route path="jobs/:id/apply" element={<RequireAuth><Apply /></RequireAuth>} />
          <Route path="sign-in" element={<SignIn />} />
          <Route path="create-account" element={<SignUp />} />
          <Route path="check-email" element={<CheckEmail />} />
          <Route path="verify-email" element={<VerifyEmail />} />
          <Route path="forgot-password" element={<ForgotPassword />} />
          <Route path="reset-password" element={<ResetPassword />} />
          <Route path="applications" element={<RequireAuth><Applications /></RequireAuth>} />
          <Route path="applications/:id" element={<RequireAuth><Application /></RequireAuth>} />
          <Route path="assessments" element={<RequireAuth><Assessments /></RequireAuth>} />
          <Route path="account" element={<RequireAuth><Account /></RequireAuth>} />
          <Route path="assessments/:id" element={<RequireAuth><AssessmentIntro /></RequireAuth>} />
          <Route path="assessments/:id/take" element={<RequireAuth><TakeAssessment /></RequireAuth>} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </Layout>
    </SiteProvider>
  );
}

// On navigation: scroll to top and move focus to the main heading for screen readers.
function ScrollAndFocus() {
  const { pathname } = useLocation();
  const first = useRef(true);
  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }
    window.scrollTo(0, 0);
    const h1 = document.querySelector<HTMLElement>("main h1");
    if (h1) {
      h1.setAttribute("tabindex", "-1");
      h1.focus({ preventScroll: true });
    }
  }, [pathname]);
  return null;
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { user, ready } = useAuth();
  const { path } = useSite();
  const location = useLocation();
  if (!ready) return <FullPageLoading />;
  if (!user) return <Navigate to={`${path("/sign-in")}?next=${encodeURIComponent(location.pathname + location.search)}`} replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<RootRedirect />} />
      <Route path="/:lang/*" element={<LangShell />} />
    </Routes>
  );
}
