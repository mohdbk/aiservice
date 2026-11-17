import { UsersThree } from "@phosphor-icons/react";
import { useState } from "react";
import { api, ApiError, useAsync, useLang, useToast } from "../core";
import { Badge, Button, Empty, ErrorState, Loading, Modal } from "./ui";

type Account = { id: number; full_name: string; email: string; joined_at: string; applications: number };
type Group = { reason: "name" | "phone"; accounts: Account[] };

/**
 * Candidate accounts that look like the same person (same name, or the same phone on their applications). Merging moves
 * one account's applications, alerts and requests into the account kept, and switches the duplicate off.
 */
export function Duplicates({ onMerged }: { onMerged: () => void }) {
  const { t } = useLang();
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button variant="outline" onClick={() => setOpen(true)} icon={<UsersThree size={20} aria-hidden="true" />}>{t("dup.button")}</Button>
      {open && <DuplicatesDialog onClose={() => setOpen(false)} onMerged={onMerged} />}
    </>
  );
}

function DuplicatesDialog({ onClose, onMerged }: { onClose: () => void; onMerged: () => void }) {
  const { t, date } = useLang();
  const toast = useToast();
  const data = useAsync(() => api<{ groups: Group[] }>("/candidates/duplicates"), []);
  const [confirm, setConfirm] = useState<{ keep: Account; from: Account } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const merge = async () => {
    if (!confirm) return;
    setBusy(true); setError(null);
    try {
      const r = await api<{ moved: number }>(`/candidates/${confirm.from.id}/merge`, { body: { keep_id: confirm.keep.id } });
      toast(t("dup.merged", { n: r.moved }));
      setConfirm(null);
      data.reload();
      onMerged();
    } catch (e) { setError(e as ApiError); }
    setBusy(false);
  };
  return (
    <Modal open onClose={onClose} title={confirm ? t("dup.confirm_title") : t("dup.title")} footer={confirm ? <>
      <Button variant="outline" onClick={() => setConfirm(null)}>{t("common.back")}</Button>
      <Button busy={busy} onClick={merge}>{t("dup.merge")}</Button>
    </> : <Button variant="outline" onClick={onClose}>{t("common.close")}</Button>}>
      {error && <ErrorState error={error} />}
      {confirm ? (
        <div className="space-y-3 text-sm">
          <p>{t("dup.confirm", { from: confirm.from.email, keep: confirm.keep.email })}</p>
          <p className="text-aeblack-600">{t("dup.confirm_hint")}</p>
        </div>
      ) : data.loading ? <Loading /> : data.error ? <ErrorState error={data.error} retry={data.reload} /> : !data.data?.groups.length ? <Empty>{t("dup.none")}</Empty> : (
        <ul className="space-y-4">
          {data.data.groups.map((g, n) => (
            <li key={n} className="rounded-xl border border-aeblack-100 p-3">
              <Badge tone="amber">{t(`dup.reason.${g.reason}`)}</Badge>
              <ul className="mt-2 divide-y divide-aeblack-100">
                {g.accounts.map((a) => (
                  <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                    <div className="min-w-0">
                      <p className="font-semibold" dir="auto">{a.full_name}</p>
                      <p className="truncate text-xs text-aeblack-500" dir="ltr">{a.email}</p>
                      <p className="text-xs text-aeblack-500">{t("dup.meta", { n: a.applications, date: date(a.joined_at) })}</p>
                    </div>
                    <div className="flex flex-wrap gap-1">
                      {g.accounts.filter((o) => o.id !== a.id).map((o) => (
                        <Button key={o.id} variant="link" size="sm" onClick={() => setConfirm({ keep: a, from: o })}>{t("dup.keep_this", { email: o.email })}</Button>
                      ))}
                    </div>
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
    </Modal>
  );
}
