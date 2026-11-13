import { CheckCircle, Copy, LinkSimple, WarningCircle } from "@phosphor-icons/react";
import { useState } from "react";
import { api, ApiError, useLang, useToast } from "../core";
import { Badge, Button, ErrorState, Loading, Modal, Note } from "./ui";

type LinkInfo = {
  link: string; code: string; status: string; deadline_at: string; candidate: string; email: string; open: boolean;
  emails: { subject: string; status: string; created_at: string; sent_at: string | null; attempts: number; error: string }[];
};

/** "Copy Link": the candidate's assessment link, to send another way when the invitation email did not arrive. */
export function AssessmentLinkButton({ id, size = "sm" }: { id: number; size?: "sm" | "md" }) {
  const { t, date } = useLang();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [info, setInfo] = useState<LinkInfo | null>(null);
  const [error, setError] = useState<unknown>(null);

  const show = async () => {
    setOpen(true); setInfo(null); setError(null);
    try { setInfo(await api<LinkInfo>(`/invitations/${id}/link`)); } catch (e) { setError(e); }
  };
  const copy = async () => {
    if (!info) return;
    try { await navigator.clipboard.writeText(info.link); toast(t("asmlink.copied")); }
    catch { (document.getElementById(`asm-link-${id}`) as HTMLInputElement | null)?.select(); toast(t("asmlink.select"), "info"); }
  };
  const tone = (s: string) => (s === "Sent" ? "green" : s === "Failed" ? "red" : "amber");

  return (
    <>
      <Button variant="outline" size={size} onClick={show} icon={<LinkSimple size={20} aria-hidden="true" />}>{t("asmlink.button")}</Button>
      {open && (
        <Modal open onClose={() => setOpen(false)} title={t("asmlink.title")} footer={<>
          <Button variant="outline" onClick={() => setOpen(false)}>{t("common.close")}</Button>
          <Button disabled={!info} onClick={copy} icon={<Copy size={20} aria-hidden="true" />}>{t("asmlink.copy")}</Button></>}>
          {error ? <ErrorState error={error instanceof ApiError ? error : new ApiError("server", 500)} /> : !info ? <Loading /> : (
            <div className="space-y-4">
              <p className="text-sm text-aeblack-700">{t("asmlink.intro", { name: info.candidate })}</p>
              <input id={`asm-link-${id}`} readOnly dir="ltr" value={info.link} onFocus={(e) => e.currentTarget.select()}
                className="w-full rounded-lg border border-aeblack-200 bg-whitely-50 px-3 py-2 font-mono text-sm" aria-label={t("asmlink.title")} />
              <p className="text-xs text-aeblack-500">{t("asmlink.sign_in", { email: info.email })}</p>
              {!info.open && <Note tone="warning">{t("asmlink.closed", { status: t(`inv.status.${info.status}`), deadline: date(info.deadline_at, true) })}</Note>}
              <div>
                <p className="mb-2 text-sm font-semibold">{t("asmlink.emails")}</p>
                {info.emails.length === 0 ? <p className="flex items-center gap-2 text-sm text-aeblack-600"><WarningCircle size={20} aria-hidden="true" />{t("asmlink.no_email")}</p> : (
                  <ul className="divide-y divide-aeblack-100 text-sm">
                    {info.emails.map((m, i) => (
                      <li key={i} className="flex flex-wrap items-center justify-between gap-2 py-2">
                        <span className="min-w-0 truncate">{m.subject}</span>
                        <span className="flex items-center gap-2 text-xs text-aeblack-500">
                          {m.status === "Sent" && <CheckCircle size={16} className="text-aegreen-600" aria-hidden="true" />}
                          {date(m.sent_at || m.created_at, true)}
                          <Badge tone={tone(m.status)}>{t(`asmlink.mail.${m.status}`)}</Badge>
                        </span>
                        {m.status !== "Sent" && m.error && <span className="w-full text-xs text-aered-700" dir="ltr">{m.error}</span>}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          )}
        </Modal>
      )}
    </>
  );
}
