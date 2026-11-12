// Smoke check of the live demo: pages load through the backoffice, the API answers behind it, and screenshots of the
// sign-in screen in Arabic and English (desktop and phone) show the logo on the left at its proper proportions.
import { chromium } from "playwright";
const BASE = process.env.BASE_URL;
const ok = (c, m) => { if (!c) { console.error("FAIL " + m); process.exitCode = 1; } else console.log("ok   " + m); };

const health = await fetch(`${BASE}/healthz`);
ok(health.ok, "backoffice /healthz");
const me = await fetch(`${BASE}/api/backoffice/me`);
ok(me.status === 401, `API reached through the backoffice (/api/backoffice/me -> ${me.status})`);
const page = await fetch(`${BASE}/ar/applicants`);
ok(page.ok && (await page.text()).includes('id="root"'), "client routes return the app");
ok((page.headers.get("content-security-policy") || "").includes("default-src 'self'"), "security headers on pages");
const run = await fetch(`${BASE}/api/backoffice/agent/run`, { method: "POST", headers: { "X-Backoffice": "1", "Content-Type": "application/json" }, body: "{}" });
ok(run.status === 401, `assistant needs a signed-in staff member (${run.status})`);

const browser = await chromium.launch();
for (const [name, viewport] of [["desktop", { width: 1440, height: 900 }], ["phone", { width: 390, height: 844 }]]) {
  for (const lang of ["ar", "en"]) {
    const p = await browser.newPage({ viewport, deviceScaleFactor: 2 });
    await p.goto(`${BASE}/${lang}/sign-in`, { waitUntil: "networkidle" });
    const logo = p.locator("header img").first();
    const box = await logo.boundingBox();
    const natural = await logo.evaluate((img) => img.naturalWidth / img.naturalHeight);
    ok(box && box.x < viewport.width / 2, `${lang} ${name}: logo on the left (x=${box && Math.round(box.x)})`);
    ok(box && Math.abs(box.width / box.height - natural) < 0.05, `${lang} ${name}: logo proportions kept (${box && (box.width / box.height).toFixed(2)} vs ${natural.toFixed(2)})`);
    await p.screenshot({ path: `shots/signin-${lang}-${name}.png` });
    await p.close();
  }
}
await browser.close();
