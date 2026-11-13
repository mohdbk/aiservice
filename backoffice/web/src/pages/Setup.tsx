import { FloppyDisk, Plus } from "@phosphor-icons/react";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { DataTable, useList } from "../components/DataTable";
import { Badge, Button, Card, ErrorState, FlagLabel, Loading, Modal, PageHeader, SectionTabs, SelectInput, TextArea, TextInput, Toggle, useSaving } from "../components/ui";
import { api, useAsync, useAuth, useLang, usePageTitle, useToast } from "../core";

// ---------------- departments ----------------
type Dept = { id: number; name: string; name_en: string; is_active: boolean };

export function Departments() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("setup.departments"));
  const toast = useToast();
  const list = useList<Dept>("/departments", []);
  const [f, setF] = useState<Partial<Dept> | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!f) return;
    const body = { name: f.name || "", name_en: f.name_en || "", is_active: f.is_active ?? true, description: null, skills: null };
    if (await run(() => api(f.id ? `/departments/${f.id}` : "/departments", { method: f.id ? "PUT" : "POST", body })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("setup.departments")} actions={<Button onClick={() => setF({ is_active: true })} icon={<Plus size={20} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <DataTable list={list} rowKey={(d) => d.id} columns={[
        { key: "name", label: t("common.name"), sort: "name", render: (d) => <button type="button" className="text-start font-semibold text-primary-700" onClick={() => setF(d)}>{en && d.name_en ? d.name_en : d.name}</button> },
        { key: "active", label: t("common.status"), sort: "active", render: (d) => <Badge tone={d.is_active ? "green" : "gray"}>{d.is_active ? t("common.active") : t("common.inactive")}</Badge> },
      ]} />
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? t("common.edit") : t("common.add")} footer={<>
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button><Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <TextInput required label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={f.name || ""} onChange={(e) => setF({ ...f, name: e.target.value })} />
          <TextInput label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} dir="ltr" value={f.name_en || ""} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          <Toggle label={t("common.active")} checked={f.is_active ?? true} onChange={(v) => setF({ ...f, is_active: v })} />
        </Modal>
      )}
    </>
  );
}

// ---------------- locations ----------------
type Loc = { id: number; name: string; name_en: string; is_active: boolean; sort_order: number; jobs: number };

/** The places jobs are based in, with a name in each language; jobs pick one from this list. */
export function Locations() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("setup.locations"));
  const toast = useToast();
  const list = useList<Loc>("/locations", []);
  const [f, setF] = useState<Partial<Loc> | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!f) return;
    const body = { name: f.name || "", name_en: f.name_en || "", is_active: f.is_active ?? true, sort_order: f.sort_order ?? 0 };
    if (await run(() => api(f.id ? `/locations/${f.id}` : "/locations", { method: f.id ? "PUT" : "POST", body })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("setup.locations")} subtitle={t("setup.locations_hint")}
        actions={<Button onClick={() => setF({ is_active: true, sort_order: 0 })} icon={<Plus size={20} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <DataTable list={list} rowKey={(d) => d.id} columns={[
        { key: "name", label: t("common.name"), sort: "name", render: (d) => <button type="button" className="text-start font-semibold text-primary-700" onClick={() => setF(d)}>{en && d.name_en ? d.name_en : d.name}</button> },
        { key: "other", label: en ? t("setup.name_ar") : t("setup.name_en"), render: (d) => <span dir={en ? "rtl" : "ltr"}>{(en ? d.name : d.name_en) || "-"}</span> },
        { key: "jobs", label: t("nav.jobs"), render: (d) => <span className="tabular-nums">{d.jobs}</span> },
        { key: "active", label: t("common.status"), sort: "active", render: (d) => <Badge tone={d.is_active ? "green" : "gray"}>{d.is_active ? t("common.active") : t("common.inactive")}</Badge> },
      ]} />
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? t("common.edit") : t("common.add")} footer={<>
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button><Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <TextInput required label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={f.name || ""} onChange={(e) => setF({ ...f, name: e.target.value })} />
          <TextInput label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} dir="ltr" value={f.name_en || ""} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          <TextInput label={t("roles.sort_order")} type="number" value={f.sort_order ?? 0} onChange={(e) => setF({ ...f, sort_order: Number(e.target.value) || 0 })} />
          <Toggle label={t("common.active")} checked={f.is_active ?? true} onChange={(v) => setF({ ...f, is_active: v })} />
        </Modal>
      )}
    </>
  );
}

// ---------------- countries ----------------
type CountryRow = { id: number; code: string; name_ar: string; name_en: string; is_active: boolean; sort_order: number; applications: number };

/** The countries for country of residence and nationality on the application form, with a name per language. */
export function Countries() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("setup.countries"));
  const toast = useToast();
  const list = useList<CountryRow>("/countries", ["active"]);
  const [f, setF] = useState<Partial<CountryRow> | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!f) return;
    const body = { code: f.code || "", name_ar: f.name_ar || "", name_en: f.name_en || "", is_active: f.is_active ?? true, sort_order: f.sort_order ?? 0 };
    if (await run(() => api(f.id ? `/countries/${f.id}` : "/countries", { method: f.id ? "PUT" : "POST", body })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("setup.countries")} subtitle={t("setup.countries_hint")}
        actions={<Button onClick={() => setF({ is_active: true, sort_order: 0 })} icon={<Plus size={20} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <DataTable list={list} rowKey={(d) => d.id} searchLabel={t("common.search")}
        filters={[{ key: "active", label: t("common.status"), options: [{ value: "true", label: t("common.active") }, { value: "false", label: t("common.inactive") }] }]}
        columns={[
          { key: "name", label: t("common.name"), sort: en ? "name_en" : "name_ar", render: (d) => <button type="button" className="text-start font-semibold text-primary-700" onClick={() => setF(d)}>{en ? d.name_en : d.name_ar}</button> },
          { key: "other", label: en ? t("setup.name_ar") : t("setup.name_en"), render: (d) => <span dir={en ? "rtl" : "ltr"}>{en ? d.name_ar : d.name_en}</span> },
          { key: "code", label: t("setup.country_code"), sort: "code", render: (d) => <span dir="ltr" className="font-mono">{d.code}</span> },
          { key: "order", label: t("roles.sort_order"), sort: "order", render: (d) => <span className="tabular-nums">{d.sort_order}</span> },
          { key: "apps", label: t("setup.country_applications"), render: (d) => <span className="tabular-nums">{d.applications}</span> },
          { key: "active", label: t("common.status"), render: (d) => <Badge tone={d.is_active ? "green" : "gray"}>{d.is_active ? t("common.active") : t("common.inactive")}</Badge> },
        ]} />
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? t("common.edit") : t("common.add")} footer={<>
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button><Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <TextInput required label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={f.name_ar || ""} onChange={(e) => setF({ ...f, name_ar: e.target.value })} />
          <TextInput required label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} hint={f.id ? t("setup.country_rename_hint") : undefined} dir="ltr" value={f.name_en || ""} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          <TextInput required label={t("setup.country_code")} hint={t("setup.country_code_hint")} dir="ltr" maxLength={2} value={f.code || ""} onChange={(e) => setF({ ...f, code: e.target.value.toUpperCase().replace(/[^A-Z]/g, "") })} />
          <TextInput label={t("roles.sort_order")} hint={t("setup.country_order_hint")} type="number" value={f.sort_order ?? 0} onChange={(e) => setF({ ...f, sort_order: Number(e.target.value) || 0 })} />
          <Toggle label={t("common.active")} hint={t("setup.country_active_hint")} checked={f.is_active ?? true} onChange={(v) => setF({ ...f, is_active: v })} />
        </Modal>
      )}
    </>
  );
}

// ---------------- interview rounds ----------------
type Round = { id: number; name: string; name_en: string; description: string; is_active: boolean; skills: string[] };

export function Rounds() {
  const { t, lang } = useLang();
  const en = lang === "en";
  usePageTitle(t("setup.rounds"));
  const toast = useToast();
  const list = useList<Round>("/rounds", []);
  const [f, setF] = useState<(Partial<Round> & { skillsText?: string }) | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!f) return;
    const body = { name: f.name || "", name_en: f.name_en || "", is_active: f.is_active ?? true, description: f.description || "", skills: (f.skillsText || "").split("\n").map((s) => s.trim()).filter(Boolean) };
    if (await run(() => api(f.id ? `/rounds/${f.id}` : "/rounds", { method: f.id ? "PUT" : "POST", body })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("setup.rounds")} actions={<Button onClick={() => setF({ is_active: true, skillsText: "" })} icon={<Plus size={20} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <DataTable list={list} rowKey={(r) => r.id} columns={[
        { key: "name", label: t("common.name"), sort: "name", render: (r) => <button type="button" className="font-semibold text-primary-700" onClick={() => setF({ ...r, skillsText: r.skills.join("\n") })}>{en && r.name_en ? r.name_en : r.name}</button> },
        { key: "skills", label: t("setup.skills"), render: (r) => <span className="text-sm">{r.skills.join(" · ")}</span> },
        { key: "active", label: t("common.status"), sort: "active", render: (r) => <Badge tone={r.is_active ? "green" : "gray"}>{r.is_active ? t("common.active") : t("common.inactive")}</Badge> },
      ]} />
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? t("common.edit") : t("common.add")} footer={<>
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button><Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <TextInput required label={<FlagLabel lang="ar">{t("setup.name_ar")}</FlagLabel>} dir="rtl" value={f.name || ""} onChange={(e) => setF({ ...f, name: e.target.value })} />
          <TextInput label={<FlagLabel lang="en">{t("setup.name_en")}</FlagLabel>} dir="ltr" value={f.name_en || ""} onChange={(e) => setF({ ...f, name_en: e.target.value })} />
          <TextArea label={t("setup.description")} value={f.description || ""} onChange={(e) => setF({ ...f, description: e.target.value })} />
          <TextArea label={t("setup.skills")} hint={t("setup.skills_hint")} rows={5} value={f.skillsText || ""} onChange={(e) => setF({ ...f, skillsText: e.target.value })} />
          <Toggle label={t("common.active")} checked={f.is_active ?? true} onChange={(v) => setF({ ...f, is_active: v })} />
        </Modal>
      )}
    </>
  );
}

// ---------------- portal settings ----------------
type SettingsData = {
  main_site_url: string; portal_url: string; default_language_code: string; allow_signup: boolean; require_email_verification: boolean; max_cv_mb: number;
  auto_score_applications: boolean; languages: { code: string; native_name: string; is_enabled: boolean }[];
  time_zone: string; time_zone_setting: string; time_zones: { id: string; offset: string }[];
};

export function Settings() {
  const { t } = useLang();
  const { me, setMe } = useAuth();
  usePageTitle(t("setup.settings"));
  const toast = useToast();
  const loaded = useAsync(() => api<SettingsData>("/settings"), []);
  const [s, setS] = useState<SettingsData | null>(null);
  const { busy, error, run } = useSaving();
  useEffect(() => { if (loaded.data) setS(loaded.data); }, [loaded.data]);
  if (loaded.loading || !s) return loaded.error ? <ErrorState error={loaded.error} retry={loaded.reload} /> : <Loading />;
  const set = <K extends keyof SettingsData>(k: K, v: SettingsData[K]) => setS({ ...s, [k]: v });
  const save = async () => {
    const { languages, time_zones, time_zone, time_zone_setting, ...rest } = s;
    void languages; void time_zones; void time_zone;
    if (await run(() => api("/settings", { method: "PUT", body: { ...rest, time_zone: time_zone_setting } })) !== undefined) {
      toast(t("common.saved"));
      // Times on every page follow the new zone at once.
      setMe(me ? { ...me, time_zone: time_zone_setting || "Asia/Dubai" } : null);
      loaded.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("setup.settings")} actions={<Button busy={busy} onClick={save} icon={<FloppyDisk size={20} aria-hidden="true" />}>{t("common.save")}</Button>} />
      {error && <div className="mb-4"><ErrorState error={error} /></div>}
      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <div className="space-y-4">
            <TextInput label={t("setup.main_site_url")} type="url" value={s.main_site_url} onChange={(e) => set("main_site_url", e.target.value)} />
            <TextInput label={t("setup.portal_url")} type="url" value={s.portal_url} onChange={(e) => set("portal_url", e.target.value)} />
            <SelectInput required label={t("setup.default_language")} value={s.default_language_code} onChange={(e) => set("default_language_code", e.target.value)}
              options={s.languages.filter((l) => l.is_enabled).map((l) => ({ value: l.code, label: l.native_name }))} />
            <TextInput required label={t("setup.max_cv_mb")} type="number" min={1} max={50} value={s.max_cv_mb} onChange={(e) => set("max_cv_mb", Number(e.target.value) || 5)} />
            <SelectInput required label={t("setup.time_zone")} hint={t("setup.time_zone_hint", { zone: s.time_zone })} value={s.time_zone_setting}
              onChange={(e) => set("time_zone_setting", e.target.value)}
              options={[{ value: "", label: t("setup.time_zone_default") }, ...s.time_zones.map((z) => ({ value: z.id, label: `${z.id.replace(/_/g, " ")} (${z.offset})` }))]} />
          </div>
        </Card>
        <Card>
          <div className="space-y-5">
            <Toggle label={t("setup.allow_signup")} checked={s.allow_signup} onChange={(v) => set("allow_signup", v)} />
            <Toggle label={t("setup.require_verification")} checked={s.require_email_verification} onChange={(v) => set("require_email_verification", v)} />
            <Toggle label={t("setup.auto_score")} checked={s.auto_score_applications} onChange={(v) => set("auto_score_applications", v)} />
          </div>
        </Card>
      </div>
    </>
  );
}

// ---------------- portal wording ----------------
type TextRow = { key: string; english: string; shipped: string; text: string; review_status: string; approved: boolean };

export function PortalText() {
  const { t, lang: panel } = useLang();
  usePageTitle(t("setup.portal_text"));
  const toast = useToast();
  const [lang, setLang] = useState(panel === "en" ? "en" : "ar");
  const list = useList<TextRow>(`/portal-text/${lang}`, ["changed"]);
  const [edit, setEdit] = useState<TextRow | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!edit) return;
    if (await run(() => api("/portal-text", { method: "PUT", body: { lang, key: edit.key, text: edit.text, approved: true } })) !== undefined) {
      toast(t("common.saved"));
      setEdit(null);
      list.reload();
    }
  };
  return (
    <>
      <PageHeader title={t("setup.portal_text")} subtitle={t("setup.portal_text_hint")} />
      <SectionTabs value={lang} onChange={setLang} tabs={[
        { value: "ar", label: <FlagLabel lang="ar"><span lang="ar">العربية</span></FlagLabel> },
        { value: "en", label: <FlagLabel lang="en"><span lang="en">English</span></FlagLabel> },
      ]} />
      <DataTable list={list} rowKey={(r) => r.key}
        filters={[{ key: "changed", label: t("setup.your_text"), options: [{ value: "1", label: t("common.yes") }] }]}
        columns={[
          { key: "key", label: t("setup.key"), sort: "key", render: (r) => <code className="text-xs" dir="ltr">{r.key}</code> },
          { key: "std", label: t("setup.standard"), render: (r) => <span className="text-sm" dir={lang === "ar" ? "rtl" : "ltr"}>{r.shipped || r.english}</span> },
          { key: "mine", label: t("setup.your_text"), render: (r) => (
            <button type="button" className="text-start text-sm font-semibold text-primary-700" dir={lang === "ar" ? "rtl" : "ltr"} onClick={() => setEdit(r)}>
              {r.text || t("common.edit")}
            </button>
          ) },
        ]} />
      {edit && (
        <Modal open onClose={() => setEdit(null)} title={edit.key} footer={<>
          <Button variant="outline" onClick={() => setEdit(null)}>{t("common.cancel")}</Button><Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <p className="rounded-lg bg-whitely-100 p-3 text-sm" dir={lang === "ar" ? "rtl" : "ltr"}>{edit.shipped || edit.english}</p>
          <TextArea label={t("setup.your_text")} hint={t("setup.portal_text_hint")} rows={4} dir={lang === "ar" ? "rtl" : "ltr"} value={edit.text} onChange={(e) => setEdit({ ...edit, text: e.target.value })} />
        </Modal>
      )}
    </>
  );
}

// ---------------- staff accounts ----------------
type StaffRow = { id: number; email: string; full_name: string; role: string; is_enabled: boolean; last_sign_in_at: string | null; two_step?: boolean };

export function Staff() {
  const { t, date } = useLang();
  usePageTitle(t("setup.staff"));
  const toast = useToast();
  const list = useList<StaffRow>("/staff", ["role", "enabled"]);
  const [f, setF] = useState<(Partial<StaffRow> & { password?: string }) | null>(null);
  const { busy, error, run } = useSaving();
  const save = async () => {
    if (!f) return;
    const body = { email: f.email, full_name: f.full_name, role: f.role || "Recruiter", is_enabled: f.is_enabled ?? true, password: f.password || null };
    if (await run(() => api(f.id ? `/staff/${f.id}` : "/staff", { method: f.id ? "PUT" : "POST", body })) !== undefined) {
      toast(t("common.saved"));
      setF(null);
      list.reload();
    }
  };
  const roles = ["Recruiter", "RecruitmentAdmin", "Administrator"].map((r) => ({ value: r, label: t(`role.${r}`) }));
  return (
    <>
      <PageHeader title={t("setup.staff")} actions={<Button onClick={() => setF({ role: "Recruiter", is_enabled: true })} icon={<Plus size={20} aria-hidden="true" />}>{t("common.add")}</Button>} />
      <DataTable list={list} rowKey={(u) => u.id}
        filters={[
          { key: "role", label: t("setup.role"), options: roles },
          { key: "enabled", label: t("setup.enabled"), options: [{ value: "true", label: t("common.yes") }, { value: "false", label: t("common.no") }] },
        ]}
        columns={[
          { key: "name", label: t("common.name"), sort: "name", render: (u) => <button type="button" className="font-semibold text-primary-700" onClick={() => setF(u)}>{u.full_name}</button> },
          { key: "email", label: t("auth.email"), sort: "email", render: (u) => <span dir="ltr">{u.email}</span> },
          { key: "role", label: t("setup.role"), sort: "role", render: (u) => t(`role.${u.role}`) },
          { key: "enabled", label: t("setup.enabled"), render: (u) => <Badge tone={u.is_enabled ? "green" : "gray"}>{u.is_enabled ? t("common.yes") : t("common.no")}</Badge> },
          { key: "two_step", label: t("twostep.title"), render: (u) => <Badge tone={u.two_step ? "green" : "gray"}>{u.two_step ? t("twostep.on") : t("twostep.off")}</Badge> },
          { key: "last", label: t("setup.last_sign_in"), sort: "last_sign_in", render: (u) => date(u.last_sign_in_at, true) },
        ]} />
      {f && (
        <Modal open onClose={() => setF(null)} title={f.id ? t("common.edit") : t("common.add")} footer={<>
          <Button variant="outline" onClick={() => setF(null)}>{t("common.cancel")}</Button><Button busy={busy} onClick={save}>{t("common.save")}</Button></>}>
          {error && <ErrorState error={error} />}
          <TextInput required label={t("common.name")} value={f.full_name || ""} onChange={(e) => setF({ ...f, full_name: e.target.value })} />
          <TextInput required label={t("auth.email")} type="email" value={f.email || ""} onChange={(e) => setF({ ...f, email: e.target.value })} />
          <SelectInput required label={t("setup.role")} value={f.role || "Recruiter"} onChange={(e) => setF({ ...f, role: e.target.value })} options={roles} />
          <TextInput label={t("auth.password")} type="password" autoComplete="new-password" hint={t("setup.password_hint")} value={f.password || ""} onChange={(e) => setF({ ...f, password: e.target.value })} />
          <Toggle label={t("setup.enabled")} checked={f.is_enabled ?? true} onChange={(v) => setF({ ...f, is_enabled: v })} />
          {f.id && f.two_step && (
            <div className="rounded-lg border border-aeblack-100 p-3">
              <p className="text-sm font-semibold">{t("twostep.title")}: {t("twostep.on")}</p>
              <p className="mb-2 text-xs text-aeblack-500">{t("twostep.reset_hint")}</p>
              <Button size="sm" variant="outline" busy={busy} onClick={async () => {
                if (!window.confirm(t("twostep.reset_confirm"))) return;
                if (await run(() => api(`/staff/${f.id}/two-step/reset`, { body: {} })) !== undefined) { toast(t("common.saved")); setF(null); list.reload(); }
              }}>{t("twostep.reset")}</Button>
            </div>
          )}
        </Modal>
      )}
    </>
  );
}

// ---------------- own password ----------------
export function Password() {
  const { t, to } = useLang();
  usePageTitle(t("auth.change_password"));
  const { setMe } = useAuth();
  const nav = useNavigate();
  const toast = useToast();
  const [cur, setCur] = useState("");
  const [next, setNext] = useState("");
  const { busy, error, run } = useSaving();
  return (
    <>
      <PageHeader title={t("auth.change_password")} />
      <Card className="max-w-lg">
        <form className="space-y-4" onSubmit={async (e) => {
          e.preventDefault();
          if (await run(() => api("/password", { body: { current: cur, next } })) !== undefined) {
            toast(t("auth.password_changed"));
            setMe(null);
            nav(to("/sign-in"));
          }
        }}>
          {error && <ErrorState error={error} />}
          <TextInput label={t("auth.current_password")} type="password" autoComplete="current-password" required value={cur} onChange={(e) => setCur(e.target.value)} />
          <TextInput label={t("auth.new_password")} type="password" autoComplete="new-password" minLength={12} required hint={t("error.password_short")} value={next} onChange={(e) => setNext(e.target.value)} />
          <Button type="submit" busy={busy}>{t("common.save")}</Button>
        </form>
      </Card>
    </>
  );
}
