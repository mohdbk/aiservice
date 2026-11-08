import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import { createServer, request } from "node:http";

import { createApp } from "../src/server.js";
import { createToken, readToken } from "../src/session.js";
import { createFrappe } from "../src/frappe.js";

const cfg = { port: 0, mock: true, sessionSecret: "x".repeat(40), sessionHours: 8, cookieSecure: true, trustProxy: false,
  webDist: new URL("./fixtures/dist", import.meta.url).pathname, maxBodyMb: 2, frappeTimeoutMs: 5000 };
let server, base;

before(async () => {
  server = createServer(createApp(cfg));
  await new Promise((r) => server.listen(0, r));
  base = `http://127.0.0.1:${server.address().port}`;
});
after(() => server.close());

const post = (path, body, headers = {}) => fetch(base + path, { method: "POST", headers: { "Content-Type": "application/json", "X-Portal": "1", ...headers }, body: JSON.stringify(body), redirect: "manual" });
const cookieOf = (res) => (res.headers.get("set-cookie") || "").split(";")[0];

test("root redirects to the default language", async () => {
  const r = await fetch(base + "/", { redirect: "manual" });
  assert.equal(r.status, 302);
  assert.equal(r.headers.get("location"), "/ar");
});

test("SPA routes serve index.html with security headers", async () => {
  const r = await fetch(base + "/en/jobs/HR-OPN-2026-0001");
  assert.equal(r.status, 200);
  assert.match(await r.text(), /id="root"/);
  assert.match(r.headers.get("content-security-policy"), /frame-ancestors 'none'/);
  assert.equal(r.headers.get("x-content-type-options"), "nosniff");
  const a = await fetch(base + "/assets/app.js");
  assert.match(a.headers.get("cache-control"), /immutable/);
  for (const raw of ["/assets/../../../../etc/passwd", "/assets/%2e%2e/%2e%2e/%2e%2e/etc/passwd", "/..%2f..%2fpackage.json"]) {
    const body = await new Promise((resolve) => {
      const u = new URL(base);
      const req = request({ host: u.hostname, port: u.port, path: raw }, (r) => { let d = ""; r.on("data", (c) => (d += c)); r.on("end", () => resolve(d)); });
      req.end();
    });
    assert.doesNotMatch(body, /root:|"name"/, raw);
  }
});

test("config and jobs per language", async () => {
  const c = await (await fetch(base + "/api/config?lang=ar")).json();
  assert.equal(c.dir, "rtl");
  assert.equal(c.text["nav.jobs"], "الوظائف");
  const j = await (await fetch(base + "/api/jobs?lang=en&q=data")).json();
  assert.equal(j.total, 1);
  const untranslated = await (await fetch(base + "/api/jobs/HR-OPN-2026-0003?lang=ar")).json();
  assert.equal(untranslated.content_lang, "en");
  const bad = await fetch(base + "/api/jobs?lang=../x");
  assert.equal(bad.status, 400);
  const missing = await fetch(base + "/api/jobs?lang=fr");
  assert.equal(missing.status, 404);
  assert.equal((await missing.json()).error.code, "unknown_language");
});

test("login requires the CSRF header and a same-origin Origin", async () => {
  let r = await fetch(base + "/api/login", { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
  assert.equal(r.status, 403);
  r = await post("/api/login", { email: "layla@example.com", password: "password123" }, { Origin: "https://evil.example" });
  assert.equal(r.status, 403);
});

test("sign in, see applications, apply, correct; cookies are secure", async () => {
  let r = await post("/api/login", { email: "layla@example.com", password: "wrong" });
  assert.equal(r.status, 401);
  assert.equal((await r.json()).error.code, "bad_credentials");
  r = await post("/api/login", { email: "Layla@Example.com", password: "password123" });
  assert.equal(r.status, 200);
  const sc = r.headers.get("set-cookie");
  assert.match(sc, /HttpOnly/);
  assert.match(sc, /Secure/);
  assert.match(sc, /SameSite=Lax/);
  const cookie = cookieOf(r);
  const me = await (await fetch(base + "/api/me", { headers: { cookie } })).json();
  assert.equal(me.user.email, "layla@example.com");
  const apps = await (await fetch(base + "/api/applications?lang=ar", { headers: { cookie } })).json();
  assert.equal(apps.applications.length, 2);
  const detail = await (await fetch(base + "/api/applications/HR-APP-2026-00012?lang=en", { headers: { cookie } })).json();
  assert.equal(detail.details_state, "ready");
  const cv = { name: "cv.pdf", type: "application/pdf", data: Buffer.from("%PDF-1.7 test").toString("base64") };
  r = await post("/api/applications", { job: "HR-OPN-2026-0003", phone: "+971 50 123 4567", country: "United Arab Emirates", declaration: true, lang: "en", cv }, { cookie });
  assert.equal(r.status, 200);
  assert.ok((await r.json()).application);
  r = await post("/api/applications", { job: "HR-OPN-2026-0003", phone: "+971 50 123 4567", declaration: true, cv }, { cookie });
  assert.equal((await r.json()).error.code, "already_applied");
  r = await post("/api/applications", { job: "HR-OPN-2026-0002", phone: "+971501234567", declaration: true, cv: { ...cv, name: "cv.exe" } }, { cookie });
  assert.equal((await r.json()).error.code, "cv_type");
  r = await post("/api/applications/HR-APP-2026-00012/corrections", { note: "I also hold a PMP certificate." }, { cookie });
  assert.equal(r.status, 200);
  r = await post("/api/logout", {}, { cookie });
  assert.match(r.headers.get("set-cookie"), /Max-Age=0/);
});

test("other applicants' applications and signed-out access are refused", async () => {
  const r0 = await post("/api/signup", { full_name: "Omar Saleh", email: "omar@example.com", password: "password123", lang: "en" });
  assert.equal(r0.status, 200);
  const cookie = cookieOf(r0);
  const r = await fetch(base + "/api/applications/HR-APP-2026-00012?lang=en", { headers: { cookie } });
  assert.equal(r.status, 404);
  const anon = await fetch(base + "/api/applications?lang=en");
  assert.equal(anon.status, 401);
  const forged = await fetch(base + "/api/applications?lang=en", { headers: { cookie: "mc_session=eyJlIjoibGF5bGFAZXhhbXBsZS5jb20iLCJ4Ijo5OTk5OTk5OTk5OTk5fQ.forged" } });
  assert.equal(forged.status, 401);
});

test("oversized bodies are rejected", async () => {
  const big = { job: "x", cv: { name: "a.pdf", data: "A".repeat(3 * 1024 * 1024) } };
  const r0 = await post("/api/login", { email: "layla@example.com", password: "password123" });
  const r = await post("/api/applications", big, { cookie: cookieOf(r0) });
  assert.equal(r.status, 413);
});

test("session tokens expire and resist tampering", () => {
  const t = createToken({ email: "a@b.c", full_name: "A" }, "s".repeat(40), 1, 0);
  assert.equal(readToken(t, "s".repeat(40), 1000).email, "a@b.c");
  assert.equal(readToken(t, "s".repeat(40), 2 * 3600_000), null);
  assert.equal(readToken(t, "t".repeat(40), 1000), null);
  assert.equal(readToken(t.replace(/^./, "x"), "s".repeat(40), 1000), null);
});

test("Frappe client sends the API key and site header and maps error codes", async () => {
  const seen = [];
  const fakeFetch = async (url, init) => {
    seen.push({ url, init });
    return new Response(JSON.stringify({ message: { error: { code: "job_not_found", params: {} } } }), { status: 404 });
  };
  const f = createFrappe({ frappeUrl: "http://frappe:8000", frappeSite: "hr.local", apiKey: "k", apiSecret: "s", frappeTimeoutMs: 1000 }, fakeFetch);
  await assert.rejects(f.job("en", "X"), (e) => e.code === "job_not_found" && e.status === 404);
  assert.equal(seen[0].init.headers.Authorization, "token k:s");
  assert.equal(seen[0].init.headers["X-Frappe-Site-Name"], "hr.local");
  assert.match(seen[0].url, /moet_careers\.portal_api\.job\?lang=en&name=X/);
});
