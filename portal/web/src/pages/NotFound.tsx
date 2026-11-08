import { Link } from "react-router-dom";
import { EmptyState } from "../components/States";
import { usePageTitle, useSite } from "../site";

export default function NotFound() {
  const { t, path } = useSite();
  usePageTitle(t("common.not_found_title"));
  return (
    <div className="container mx-auto max-w-2xl px-4 py-16">
      <h1 className="sr-only">{t("common.not_found_title")}</h1>
      <EmptyState title={t("common.not_found_title")} body={t("common.not_found_body")} action={<Link className="aegov-btn" to={path()}>{t("common.back_to_jobs")}</Link>} />
    </div>
  );
}
