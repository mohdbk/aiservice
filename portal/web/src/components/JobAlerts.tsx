import { BellRinging, Plus, Trash } from "@phosphor-icons/react";
import { FormEvent, useState } from "react";
import { api } from "../api";
import { useSite } from "../site";
import { useAsync } from "../useAsync";
import { Alert } from "./States";

type AlertRow = { id: number; keywords: string; department: string; location: string; created: string };
type Data = { alerts: AlertRow[]; max: number; departments: { id: number; name: string }[]; locations: { id: number; name: string }[] };

/** Job alerts: saved searches the candidate gets an email for when new matching jobs are published. */
export function JobAlerts() {
  const { t, lang, errorText } = useSite();
  const data = useAsync((signal) => api<Data>(`/api/account/alerts?lang=${lang}`, { signal }), [lang]);
  const [keywords, setKeywords] = useState("");
  const [department, setDepartment] = useState("");
  const [location, setLocation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  const add = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(null); setSaved(false);
    try {
      await api("/api/account/alerts", { body: { keywords: keywords.trim(), department: Number(department) || 0, location: Number(location) || 0, lang } });
      setKeywords(""); setDepartment(""); setLocation(""); setSaved(true);
      data.reload();
    } catch (err) { setError(err); } finally { setBusy(false); }
  };
  const remove = async (id: number) => {
    setError(null);
    try { await api(`/api/account/alerts/${id}`, { method: "DELETE" }); data.reload(); } catch (err) { setError(err); }
  };
  const d = data.data;
  const full = !!d && d.alerts.length >= d.max;

  return (
    <section className="mc-surface mt-6 rounded-2xl border border-aeblack-100 bg-white p-6" aria-labelledby="alerts-title">
      <h2 id="alerts-title" className="flex items-center gap-2 font-heading text-lg font-semibold text-aeblack-900">
        <BellRinging size={24} className="text-primary-600" aria-hidden="true" />{t("alerts.title")}
      </h2>
      <p className="mt-1 text-aeblack-600">{t("alerts.body")}</p>
      {error ? <div className="mt-4"><Alert tone="error" title={errorText(error)} /></div> : null}
      {saved && <div className="mt-4" role="status"><Alert tone="success" title={t("alerts.saved")} /></div>}
      {d && d.alerts.length > 0 && (
        <ul className="mt-4 divide-y divide-aeblack-100 rounded-xl border border-aeblack-100">
          {d.alerts.map((a) => (
            <li key={a.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <p className="font-medium text-aeblack-900"><bdi>{a.keywords || t("alerts.any_job")}</bdi></p>
                <p className="text-sm text-aeblack-600">{[a.department, a.location].filter(Boolean).join(" · ") || t("alerts.everywhere")}</p>
              </div>
              <button type="button" onClick={() => remove(a.id)} className="aegov-btn btn-outline btn-sm shrink-0" aria-label={t("alerts.remove")}>
                <Trash size={20} aria-hidden="true" /><span className="max-sm:sr-only">{t("alerts.remove")}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {d && !full && (
        <form onSubmit={add} className="mt-4 grid gap-4 md:grid-cols-3">
          <div className="aegov-form-control md:col-span-3">
            <label htmlFor="alert-keywords">{t("alerts.keywords")}</label>
            <div className="form-control-input"><input id="alert-keywords" value={keywords} onChange={(e) => setKeywords(e.target.value)} maxLength={300} placeholder={t("alerts.keywords_example")} /></div>
          </div>
          <div className="aegov-form-control">
            <label htmlFor="alert-department">{t("alerts.department")}</label>
            <div className="form-control-input">
              <select id="alert-department" value={department} onChange={(e) => setDepartment(e.target.value)}>
                <option value="">{t("alerts.any")}</option>
                {d.departments.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
            </div>
          </div>
          <div className="aegov-form-control">
            <label htmlFor="alert-location">{t("alerts.location")}</label>
            <div className="form-control-input">
              <select id="alert-location" value={location} onChange={(e) => setLocation(e.target.value)}>
                <option value="">{t("alerts.any")}</option>
                {d.locations.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
            </div>
          </div>
          <div className="flex items-end">
            <button type="submit" className="aegov-btn btn-sm w-full md:w-auto" disabled={busy || (!keywords.trim() && !department && !location)}>
              <Plus size={20} aria-hidden="true" />{t("alerts.add")}
            </button>
          </div>
        </form>
      )}
      {full && <p className="mt-4 text-sm text-aeblack-600">{t("alerts.full", { n: d!.max })}</p>}
    </section>
  );
}
