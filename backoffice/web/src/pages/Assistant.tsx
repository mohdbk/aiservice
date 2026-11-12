import { AgentChat } from "../agent/AgentChat";
import { PageHeader } from "../components/ui";
import { useLang, usePageTitle } from "../core";

export default function Assistant() {
  const { t } = useLang();
  usePageTitle(t("asst.title"));
  return (
    <>
      <PageHeader title={t("asst.title")} subtitle={t("asst.intro")} />
      <AgentChat />
    </>
  );
}
