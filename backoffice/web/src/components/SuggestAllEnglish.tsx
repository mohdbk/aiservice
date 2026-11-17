import { Translate } from "@phosphor-icons/react";
import { useState } from "react";
import { api, useLang, useToast } from "../core";
import { Button } from "./ui";

/** Queues English drafts for every job, screening question, test question and test that has no English yet. */
export function SuggestAllEnglish() {
  const { t } = useLang();
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const run = async () => {
    setBusy(true);
    try {
      const r = await api<{ total: number; jobs: number; screening: number; questions: number; tests: number }>("/suggest-english/missing", { body: {} });
      toast(r.total === 0 ? t("english_all.none") : t("english_all.queued", r), r.total === 0 ? "success" : "info");
    } catch {
      toast(t("english_all.failed"), "error");
    }
    setBusy(false);
  };
  return <Button variant="outline" busy={busy} onClick={run} icon={<Translate size={20} aria-hidden="true" />}>{t("english_all.button")}</Button>;
}
