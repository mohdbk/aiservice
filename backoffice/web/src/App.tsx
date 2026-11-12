import { Navigate, Route, Routes, useLocation, useParams } from "react-router-dom";
import { Shell } from "./components/Shell";
import { Loading } from "./components/ui";
import { useAuth } from "./core";
import AgentSettings from "./pages/AgentSettings";
import Applicant from "./pages/Applicant";
import Applicants from "./pages/Applicants";
import Assistant from "./pages/Assistant";
import ComponentEdit from "./pages/ComponentEdit";
import Components from "./pages/Components";
import Dashboard from "./pages/Dashboard";
import Interview from "./pages/Interview";
import Interviews from "./pages/Interviews";
import Invitations from "./pages/Invitations";
import JobEdit from "./pages/JobEdit";
import Jobs from "./pages/Jobs";
import Marking from "./pages/Marking";
import Offers from "./pages/Offers";
import QuestionEdit from "./pages/QuestionEdit";
import { EmailLog, EmailServer, EmailTemplates } from "./pages/Email";
import DocumentTemplates from "./pages/DocumentTemplates";
import Questions from "./pages/Questions";
import RoleProfiles from "./pages/RoleProfiles";
import Screening from "./pages/Screening";
import { Departments, Password, PortalText, Rounds, Settings, Staff } from "./pages/Setup";
import SignIn from "./pages/SignIn";
import TestEdit from "./pages/TestEdit";
import Tests from "./pages/Tests";

function Guarded() {
  const { me } = useAuth();
  const { lang } = useParams();
  const loc = useLocation();
  if (lang !== "ar" && lang !== "en") return <Navigate to={`/ar${loc.pathname}`} replace />;
  if (me === undefined) return <div className="p-8"><Loading /></div>;
  if (me === null) return <Navigate to={`/${lang}/sign-in?next=${encodeURIComponent(loc.pathname + loc.search)}`} replace />;
  return (
    <Shell>
      {/* Keyed by language: switching language reloads the page's data (lists come back in that language). */}
      <Routes key={lang}>
        <Route index element={<Dashboard />} />
        <Route path="jobs" element={<Jobs />} />
        <Route path="jobs/new" element={<JobEdit />} />
        <Route path="jobs/:id" element={<JobEdit />} />
        <Route path="applicants" element={<Applicants />} />
        <Route path="applicants/:id" element={<Applicant />} />
        <Route path="interviews" element={<Interviews />} />
        <Route path="interviews/:id" element={<Interview />} />
        <Route path="offers" element={<Offers />} />
        <Route path="screening" element={<Screening />} />
        <Route path="tests" element={<Tests />} />
        <Route path="tests/new" element={<TestEdit />} />
        <Route path="tests/:id" element={<TestEdit />} />
        <Route path="questions" element={<Questions />} />
        <Route path="questions/new" element={<QuestionEdit />} />
        <Route path="questions/:id" element={<QuestionEdit />} />
        <Route path="invitations" element={<Invitations />} />
        <Route path="role-profiles" element={<RoleProfiles />} />
        <Route path="marking/:id" element={<Marking />} />
        <Route path="assistant" element={<Assistant />} />
        <Route path="password" element={<Password />} />
        <Route path="setup/departments" element={<Departments />} />
        <Route path="setup/rounds" element={<Rounds />} />
        <Route path="setup/settings" element={<Settings />} />
        <Route path="setup/portal-text" element={<PortalText />} />
        <Route path="setup/staff" element={<Staff />} />
        <Route path="setup/assistant" element={<AgentSettings />} />
        <Route path="setup/components" element={<Components />} />
        <Route path="setup/document-templates" element={<DocumentTemplates />} />
        <Route path="setup/offer-templates" element={<Navigate to={`/${lang}/setup/document-templates`} replace />} />
        <Route path="setup/email-templates" element={<EmailTemplates />} />
        <Route path="setup/email-server" element={<EmailServer />} />
        <Route path="setup/email-log" element={<EmailLog />} />
        <Route path="setup/components/:id" element={<ComponentEdit />} />
        <Route path="*" element={<Navigate to={`/${lang}`} replace />} />
      </Routes>
    </Shell>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/ar" replace />} />
      <Route path="/:lang/sign-in" element={<SignIn />} />
      <Route path="/:lang/*" element={<Guarded />} />
    </Routes>
  );
}
