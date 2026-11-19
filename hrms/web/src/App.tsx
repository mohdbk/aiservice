import { lazy, Suspense } from "react";
import { Navigate, Route, Routes, useLocation, useParams } from "react-router-dom";
import { Shell } from "./components/Shell";
import { Loading } from "./components/ui";
import { useAuth } from "./core";
import SignIn from "./pages/SignIn";

// Each page's code loads when it is first opened, so the first visit downloads only what it shows.
const AgentSettings = lazy(() => import("./pages/AgentSettings"));
const Applicant = lazy(() => import("./pages/Applicant"));
const Applicants = lazy(() => import("./pages/Applicants"));
const Assistant = lazy(() => import("./pages/Assistant"));
const ComponentEdit = lazy(() => import("./pages/ComponentEdit"));
const Components = lazy(() => import("./pages/Components"));
const Dashboard = lazy(() => import("./pages/Dashboard"));
const Interview = lazy(() => import("./pages/Interview"));
const Interviews = lazy(() => import("./pages/Interviews"));
const Invitations = lazy(() => import("./pages/Invitations"));
const JobEdit = lazy(() => import("./pages/JobEdit"));
const Jobs = lazy(() => import("./pages/Jobs"));
const Marking = lazy(() => import("./pages/Marking"));
const Offers = lazy(() => import("./pages/Offers"));
const QuestionEdit = lazy(() => import("./pages/QuestionEdit"));
const EmailLog = lazy(() => import("./pages/Email").then((m) => ({ default: m.EmailLog })));
const EmailServer = lazy(() => import("./pages/Email").then((m) => ({ default: m.EmailServer })));
const EmailTemplates = lazy(() => import("./pages/Email").then((m) => ({ default: m.EmailTemplates })));
const DocumentTemplates = lazy(() => import("./pages/DocumentTemplates"));
const Questions = lazy(() => import("./pages/Questions"));
const RoleProfiles = lazy(() => import("./pages/RoleProfiles"));
const Reports = lazy(() => import("./pages/Reports"));
const AssistantUsage = lazy(() => import("./pages/System").then((m) => ({ default: m.AssistantUsage })));
const AuditLog = lazy(() => import("./pages/System").then((m) => ({ default: m.AuditLog })));
const DataProtection = lazy(() => import("./pages/System").then((m) => ({ default: m.DataProtection })));
const HandOffSettings = lazy(() => import("./pages/System").then((m) => ({ default: m.HandOffSettings })));
const HealthPage = lazy(() => import("./pages/System").then((m) => ({ default: m.HealthPage })));
const TextMessages = lazy(() => import("./pages/System").then((m) => ({ default: m.TextMessages })));
const TalentPool = lazy(() => import("./pages/TalentPool"));
const Approvals = lazy(() => import("./pages/Approvals"));
const TwoStep = lazy(() => import("./pages/TwoStep"));
const Candidates = lazy(() => import("./pages/Candidates"));
const Requisitions = lazy(() => import("./pages/Requisitions"));
const PersonalitySettings = lazy(() => import("./pages/PersonalitySettings"));
const Screening = lazy(() => import("./pages/Screening"));
const Countries = lazy(() => import("./pages/Setup").then((m) => ({ default: m.Countries })));
const Departments = lazy(() => import("./pages/Setup").then((m) => ({ default: m.Departments })));
const Locations = lazy(() => import("./pages/Setup").then((m) => ({ default: m.Locations })));
const Password = lazy(() => import("./pages/Setup").then((m) => ({ default: m.Password })));
const PortalText = lazy(() => import("./pages/Setup").then((m) => ({ default: m.PortalText })));
const Rounds = lazy(() => import("./pages/Setup").then((m) => ({ default: m.Rounds })));
const Settings = lazy(() => import("./pages/Setup").then((m) => ({ default: m.Settings })));
const Staff = lazy(() => import("./pages/Setup").then((m) => ({ default: m.Staff })));
const TestEdit = lazy(() => import("./pages/TestEdit"));
const Tests = lazy(() => import("./pages/Tests"));

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
      <Suspense fallback={<Loading />}>
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
        <Route path="reports" element={<Reports />} />
        <Route path="personality-settings" element={<PersonalitySettings />} />
        <Route path="marking/:id" element={<Marking />} />
        <Route path="assistant" element={<Assistant />} />
        <Route path="password" element={<Password />} />
        <Route path="two-step" element={<TwoStep />} />
        <Route path="setup/departments" element={<Departments />} />
        <Route path="setup/locations" element={<Locations />} />
        <Route path="setup/countries" element={<Countries />} />
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
        <Route path="setup/assistant-usage" element={<AssistantUsage />} />
        <Route path="setup/audit" element={<AuditLog />} />
        <Route path="setup/health" element={<HealthPage />} />
        <Route path="setup/data-protection" element={<DataProtection />} />
        <Route path="setup/texts" element={<TextMessages />} />
        <Route path="setup/handoff" element={<HandOffSettings />} />
        <Route path="talent-pool" element={<TalentPool />} />
        <Route path="candidates" element={<Candidates />} />
        <Route path="approvals" element={<Approvals />} />
        <Route path="requisitions" element={<Requisitions />} />
        <Route path="setup/components/:id" element={<ComponentEdit />} />
        <Route path="*" element={<Navigate to={`/${lang}`} replace />} />
      </Routes>
      </Suspense>
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
