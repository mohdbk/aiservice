import { DeviceMobile, ShieldCheck } from "@phosphor-icons/react";
import qrcode from "qrcode-generator";
import { useState } from "react";
import { Badge, Button, Card, ErrorState, Loading, Note, PageHeader, TextInput, useSaving } from "../components/ui";
import { api, useAsync, useLang, usePageTitle, useToast } from "../core";

type State = { enabled: boolean; enabled_at: string | null };
type SetUp = { secret: string; uri: string };

/** The signed-in person's two-step sign-in: add an authenticator app (scan the code, confirm with a code), or turn it off. */
export default function TwoStep() {
  const { t, date } = useLang();
  usePageTitle(t("twostep.title"));
  const toast = useToast();
  const data = useAsync(() => api<State>("/two-step"), []);
  const [setup, setSetup] = useState<SetUp | null>(null);
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const saving = useSaving();
  if (data.loading && !data.data) return <Loading />;
  if (data.error) return <ErrorState error={data.error} retry={data.reload} />;
  const s = data.data!;

  const start = async () => {
    const r = await saving.run(() => api<SetUp>("/two-step/setup", { body: {} }));
    if (r) { setSetup(r); setCode(""); }
  };
  const enable = async () => {
    if (await saving.run(() => api("/two-step/enable", { body: { code } })) !== undefined) {
      toast(t("twostep.enabled"));
      setSetup(null); setCode(""); data.reload();
    }
  };
  const disable = async () => {
    if (await saving.run(() => api("/two-step/disable", { body: { password, code } })) !== undefined) {
      toast(t("twostep.disabled"));
      setPassword(""); setCode(""); data.reload();
    }
  };
  const qr = (() => {
    if (!setup) return "";
    const q = qrcode(0, "M");
    q.addData(setup.uri);
    q.make();
    return q.createSvgTag({ cellSize: 5, margin: 2, scalable: true });
  })();

  return (
    <>
      <PageHeader title={t("twostep.title")} subtitle={t("twostep.subtitle")} />
      <Card className="max-w-xl" title={<span className="inline-flex items-center gap-2"><ShieldCheck size={24} className="text-primary-600" aria-hidden="true" />{t("twostep.title")}</span>}
        actions={<Badge tone={s.enabled ? "green" : "gray"}>{s.enabled ? t("twostep.on") : t("twostep.off")}</Badge>}>
        {saving.error && <div className="mb-4"><ErrorState error={saving.error} /></div>}
        {s.enabled ? (
          <div className="space-y-4">
            <p className="text-sm text-aeblack-600">{t("twostep.on_since", { at: date(s.enabled_at, true) })}</p>
            <TextInput required label={t("auth.password")} type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
            <TextInput required label={t("twostep.code")} inputMode="numeric" autoComplete="one-time-code" dir="ltr" maxLength={7} value={code} onChange={(e) => setCode(e.target.value.replace(/[^0-9 ]/g, ""))} />
            <Button variant="outline" busy={saving.busy} disabled={!password || code.replace(/ /g, "").length !== 6} onClick={disable}>{t("twostep.turn_off")}</Button>
          </div>
        ) : !setup ? (
          <div className="space-y-4">
            <Note>{t("twostep.why")}</Note>
            <Button busy={saving.busy} onClick={start} icon={<DeviceMobile size={20} aria-hidden="true" />}>{t("twostep.turn_on")}</Button>
          </div>
        ) : (
          <div className="space-y-4">
            <ol className="list-decimal space-y-1 ps-5 text-sm text-aeblack-700">
              <li>{t("twostep.step1")}</li>
              <li>{t("twostep.step2")}</li>
              <li>{t("twostep.step3")}</li>
            </ol>
            <div className="mx-auto w-48 rounded-lg border border-aeblack-100 bg-white p-2" role="img" aria-label={t("twostep.qr")} dangerouslySetInnerHTML={{ __html: qr }} />
            <div className="text-sm">
              <p className="text-aeblack-500">{t("twostep.manual")}</p>
              <code dir="ltr" className="block break-all rounded bg-whitely-100 px-2 py-1 font-mono text-sm tracking-wider">{setup.secret.replace(/(.{4})/g, "$1 ").trim()}</code>
            </div>
            <TextInput required label={t("twostep.code")} inputMode="numeric" autoComplete="one-time-code" dir="ltr" maxLength={7} value={code} onChange={(e) => setCode(e.target.value.replace(/[^0-9 ]/g, ""))} />
            <div className="flex gap-2">
              <Button busy={saving.busy} disabled={code.replace(/ /g, "").length !== 6} onClick={enable}>{t("twostep.confirm")}</Button>
              <Button variant="outline" onClick={() => setSetup(null)}>{t("common.cancel")}</Button>
            </div>
          </div>
        )}
      </Card>
    </>
  );
}
