import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import { createServer, request } from "node:http";

import { createApp } from "../src/server.js";
import { createToken, readToken } from "../src/session.js";
import { createFrappe } from "../src/frappe.js";

const cfg = { port: 0, mock: true, sessionSecret: "x".repeat(40), sessionHours: 8, cookieSecure: true, trustProxy: false,
  webDist: new URL("./fixtures/web", import.meta.url).pathname, maxBodyMb: 2, frappeTimeoutMs: 5000 };
let server, base, mock;

before(async () => {
  const { createMockFrappe } = await import("../src/mock.js");
  mock = createMockFrappe();
  server = createServer(createApp(cfg, { frappe: mock }));
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
  r = await post("/api/applications", { job: "HR-OPN-2026-0003", phone: "+971 50 123 4567", declaration: true, lang: "en", cv }, { cookie });
  assert.deepEqual((await r.json()).error, { code: "screening_required", params: { questions: ["sq1", "sq2"] } });
  const job3 = await (await fetch(base + "/api/jobs/HR-OPN-2026-0003?lang=ar")).json();
  assert.equal(job3.screening[0].text, "هل لديك رخصة قيادة إماراتية سارية؟");
  assert.equal(JSON.stringify(job3).includes("expected"), false);
  r = await post("/api/applications", { job: "HR-OPN-2026-0003", phone: "+971 50 123 4567", country: "United Arab Emirates", declaration: true, lang: "en",
    screening: { sq1: "Yes", sq2: "4", sq3: ["Arabic"] }, cv }, { cookie });
  assert.equal(r.status, 200);
  assert.ok((await r.json()).application);
  r = await post("/api/applications", { job: "HR-OPN-2026-0003", phone: "+971 50 123 4567", declaration: true, screening: { sq1: "Yes", sq2: "4" }, cv }, { cookie });
  assert.equal((await r.json()).error.code, "already_applied");
  r = await post("/api/applications", { job: "HR-OPN-2026-0002", phone: "+971501234567", declaration: true, cv: { ...cv, name: "cv.exe" } }, { cookie });
  assert.equal((await r.json()).error.code, "cv_type");
  r = await post("/api/applications/HR-APP-2026-00012/corrections", { note: "I also hold a PMP certificate." }, { cookie });
  assert.equal(r.status, 200);
  r = await post("/api/logout", {}, { cookie });
  assert.match(r.headers.get("set-cookie"), /Max-Age=0/);
});

test("sign up needs email activation; the link signs in once", async () => {
  const r = await post("/api/signup", { full_name: "Sara Ali", email: "sara@example.com", password: "password123", lang: "en", next: "/en/jobs/HR-OPN-2026-0001/apply" });
  assert.equal(r.status, 200);
  const body = await r.json();
  assert.equal(body.verification_sent, true);
  assert.equal(r.headers.get("set-cookie"), null);
  const blocked = await post("/api/login", { email: "sara@example.com", password: "password123" });
  assert.equal((await blocked.json()).error.code, "email_not_verified");
  const token = mock._outbox.findLast((m) => m.to === "sara@example.com" && m.purpose === "verify").token;
  const v = await post("/api/verify-email", { token });
  assert.equal(v.status, 200);
  const vb = await v.json();
  assert.equal(vb.next, "/en/jobs/HR-OPN-2026-0001/apply");
  assert.match(v.headers.get("set-cookie"), /mc_session=/);
  const again = await post("/api/verify-email", { token });
  assert.equal((await again.json()).error.code, "token_invalid");
});

test("forgot password never reveals whether an account exists", async () => {
  const a = await (await post("/api/forgot-password", { email: "layla@example.com", lang: "en" })).json();
  const b = await (await post("/api/forgot-password", { email: "nobody@example.com", lang: "en" })).json();
  assert.deepEqual(a, b);
});

test("password reset changes the password and signs out every session", async () => {
  await post("/api/signup", { full_name: "Huda K", email: "huda@example.com", password: "password123", lang: "en" });
  await post("/api/verify-email", { token: mock._outbox.findLast((m) => m.to === "huda@example.com").token });
  const s1 = cookieOf(await post("/api/login", { email: "huda@example.com", password: "password123" }));
  assert.equal((await fetch(base + "/api/applications?lang=en", { headers: { cookie: s1 } })).status, 200);
  await post("/api/forgot-password", { email: "huda@example.com", lang: "en" });
  const token = mock._outbox.findLast((m) => m.to === "huda@example.com" && m.purpose === "reset").token;
  const r = await post("/api/reset-password", { token, password: "newpassword456" });
  assert.equal(r.status, 200);
  assert.equal((await fetch(base + "/api/applications?lang=en", { headers: { cookie: s1 } })).status, 401);
  assert.equal((await post("/api/login", { email: "huda@example.com", password: "password123" })).status, 401);
  assert.equal((await post("/api/login", { email: "huda@example.com", password: "newpassword456" })).status, 200);
  assert.equal((await (await post("/api/reset-password", { token, password: "another12345" })).json()).error.code, "token_invalid");
});

test("other applicants' applications and signed-out access are refused", async () => {
  const r0 = await post("/api/signup", { full_name: "Omar Saleh", email: "omar@example.com", password: "password123", lang: "en" });
  assert.equal(r0.status, 200);
  await post("/api/verify-email", { token: mock._outbox.findLast((m) => m.to === "omar@example.com").token });
  const cookie = cookieOf(await post("/api/login", { email: "omar@example.com", password: "password123" }));
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
  await assert.rejects(f.applications({ email: "a@b.c", epoch: 3 }, "en"));
  assert.match(seen[1].url, /epoch=3/);
  assert.equal(seen[0].init.headers.Authorization, "token k:s");
  assert.equal(seen[0].init.headers["X-Frappe-Site-Name"], "hr.local");
  assert.match(seen[0].url, /moet_careers\.portal_api\.job\?lang=en&name=X/);
});

test("assessments: start, timed sections, answers, finish", async () => {
  const cookie = cookieOf(await post("/api/login", { email: "layla@example.com", password: "password123" }));
  const list = await (await fetch(base + "/api/assessments?lang=ar", { headers: { cookie } })).json();
  assert.equal(list.assessments.length, 2);
  assert.equal(list.assessments[0].title, "مهارات البيانات وإكسل");
  let st = await (await post("/api/assessments/AINV-00001/start", { lang: "en" }, { cookie })).json();
  assert.equal(st.section_index, 0);
  assert.equal(st.section_count, 2);
  assert.ok(st.section.remaining_seconds > 500);
  assert.equal(JSON.stringify(st).includes("correct"), false);
  let r = await post("/api/assessments/AINV-00001/answer", { key: "q1", answer: "a", section: 0 }, { cookie });
  assert.equal(r.status, 200);
  r = await post("/api/assessments/AINV-00001/answer", { key: "q4", answer: "text", section: 0 }, { cookie });
  assert.equal((await r.json()).error.code, "question_closed");
  st = await (await post("/api/assessments/AINV-00001/next", { section: 0, lang: "en" }, { cookie })).json();
  assert.equal(st.section_index, 1);
  r = await post("/api/assessments/AINV-00001/answer", { key: "q1", answer: "b", section: 0 }, { cookie });
  assert.equal((await r.json()).error.code, "section_closed");                   // a closed section cannot be changed
  r = await post("/api/assessments/AINV-00001/file", { key: "q5", section: 1, file: { name: "chart.pdf", data: Buffer.from("%PDF").toString("base64") } }, { cookie });
  assert.equal(r.status, 200);
  st = await (await post("/api/assessments/AINV-00001/next", { section: 1, lang: "en" }, { cookie })).json();
  assert.equal(st.finished, true);
  const again = await post("/api/assessments/AINV-00001/start", { lang: "en" }, { cookie });
  assert.equal((await again.json()).error.code, "no_attempts_left");
  const other = await fetch(base + "/api/assessments/AINV-00001?lang=en");
  assert.equal(other.status, 401);
});
