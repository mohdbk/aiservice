import { DownloadSimple, Trash, UserCircle } from "@phosphor-icons/react";
import { FormEvent, useState } from "react";
import { api, ApiError } from "../api";
import { useAuth } from "../auth";
import { IconBox } from "../components/Bits";
import { Alert, Skeleton } from "../components/States";
import { usePageTitle, useSite } from "../site";
import { useAsync } from "../useAsync";

/** The candidate's account: a copy of their data, and asking for the account to be deleted. */
export default function Account() {
  const { t, errorText } = useSite();
  const { user } = useAuth();
  usePageTitle(t("account.title"));
  const status = useAsync((signal) => api<{ deletion_pending: boolean; in_talent_pool: boolean }>("/api/account", { signal }), []);
  const [pool, setPool] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [reason, setReason] = useState("");
  const [confirm, setConfirm] = useState(false);
  const [requested, setRequested] = useState(false);

  const download = async () => {
    setError(null);
    setBusy(true);
    try {
      const res = await fetch("/api/account/data", { credentials: "same-origin" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new ApiError(body?.error?.code || "server", res.status);
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "my-data.json";
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const remove = async (e: FormEvent) => {
    e.preventDefault();
    if (!confirm) return;
    setError(null);
    setBusy(true);
    try {
      await api("/api/account/delete", { body: { reason } });
      setRequested(true);
      status.reload();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const pending = requested || status.data?.deletion_pending;
  const inPool = pool ?? status.data?.in_talent_pool ?? false;
  const togglePool = async (join: boolean) => {
    setError(null);
    try {
      const r = await api<{ in_talent_pool: boolean }>("/api/account/talent-pool", { body: { join } });
      setPool(r.in_talent_pool);
    } catch (err) {
      setError(err);
    }
  };
  return (
    <div className="container mx-auto max-w-3xl px-4 py-10">
      <div className="mb-6 flex items-center gap-4">
        <IconBox icon={UserCircle} />
        <div className="min-w-0">
          <h1 className="font-heading text-3xl font-bold text-aeblack-950">{t("account.title")}</h1>
          <p className="truncate text-aeblack-600"><bdi>{user?.email}</bdi></p>
        </div>
      </div>
      {error ? <div className="mb-6"><Alert tone="error" title={errorText(error)} /></div> : null}

      <section className="mc-surface rounded-2xl border border-aeblack-100 bg-white p-6" aria-labelledby="data-title">
        <h2 id="data-title" className="font-heading text-lg font-semibold text-aeblack-900">{t("account.data_title")}</h2>
        <p className="mt-1 text-aeblack-600">{t("account.data_body")}</p>
        <button type="button" className="aegov-btn btn-outline mt-4" onClick={download} disabled={busy}>
          <DownloadSimple size={24} aria-hidden="true" />{t("account.download")}
        </button>
      </section>

      <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6" aria-labelledby="pool-title">
        <h2 id="pool-title" className="font-heading text-lg font-semibold text-aeblack-900">{t("account.pool_title")}</h2>
        <p className="mt-1 text-aeblack-600">{t("account.pool_body")}</p>
        {status.data && (
          <div className="aegov-check-item mt-4">
            <input id="talent-pool" type="checkbox" checked={inPool} onChange={(e) => togglePool(e.target.checked)} />
            <label htmlFor="talent-pool">{t("account.pool_join")}</label>
          </div>
        )}
      </section>

      <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6" aria-labelledby="delete-title">
        <h2 id="delete-title" className="font-heading text-lg font-semibold text-aeblack-900">{t("account.delete_title")}</h2>
        {!status.data && !status.error ? <Skeleton className="mt-4 h-20" /> : pending ? (
          <div className="mt-4"><Alert tone="info" title={t("account.delete_pending")}>{t("account.delete_pending_body")}</Alert></div>
        ) : (
          <form onSubmit={remove} className="mt-2 space-y-4" noValidate>
            <p className="text-aeblack-600">{t("account.delete_body")}</p>
            <div className="aegov-form-control">
              <label htmlFor="reason">{t("account.reason")} <span className="text-sm font-normal text-aeblack-500">({t("common.optional")})</span></label>
              <div className="form-control-input"><textarea id="reason" rows={3} maxLength={2000} value={reason} onChange={(e) => setReason(e.target.value)} /></div>
            </div>
            <div className="aegov-check-item">
              <input id="confirm-delete" type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} />
              <label htmlFor="confirm-delete">{t("account.confirm")}</label>
            </div>
            <button type="submit" className="aegov-btn btn-outline" disabled={!confirm || busy}>
              <Trash size={24} aria-hidden="true" />{t("account.delete")}
            </button>
          </form>
        )}
      </section>
    </div>
  );
}
