// MoET careers portal backend. Runs in the DMZ: serves the React app and a small JSON API, and is the only
// component that talks to the internal Frappe server. No runtime dependencies (Node 22 built-ins only).
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { pathToFileURL } from "node:url";

import { loadConfig } from "./config.js";
import { createFrappe, FrappeError } from "./frappe.js";
import { createMockFrappe } from "./mock.js";
import { LIMITS, RateLimiter } from "./ratelimit.js";
import { COOKIE, cookieHeader, createToken, parseCookies, readToken } from "./session.js";

const TYPES = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8", ".svg": "image/svg+xml", ".png": "image/png", ".jpg": "image/jpeg",
  ".ico": "image/x-icon", ".woff2": "font/woff2", ".webp": "image/webp", ".txt": "text/plain; charset=utf-8",
};
const CV_EXT = /\.(pdf|docx|jpe?g|png)$/i;

const CSP = [
  "default-src 'self'", "script-src 'self'", "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
  "font-src 'self' data: https://fonts.gstatic.com", "img-src 'self' data:", "connect-src 'self'",
  "frame-ancestors 'none'", "base-uri 'self'", "form-action 'self'", "object-src 'none'",
].join("; ");

class HttpError extends Error {
  constructor(status, code, params = {}) {
    super(code);
    this.status = status;
    this.code = code;
    this.params = params;
  }
}

export function createApp(cfg = loadConfig(), deps = {}) {
  const frappe = deps.frappe || (cfg.mock ? createMockFrappe() : createFrappe(cfg));
  const limiter = deps.limiter || new RateLimiter();
  const configCache = new Map();

  const securityHeaders = (res) => {
    res.setHeader("Content-Security-Policy", CSP);
    res.setHeader("X-Content-Type-Options", "nosniff");
    res.setHeader("Referrer-Policy", "strict-origin-when-cross-origin");
    res.setHeader("X-Frame-Options", "DENY");
    res.setHeader("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
    res.setHeader("Cross-Origin-Opener-Policy", "same-origin");
    if (cfg.cookieSecure) res.setHeader("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
  };

  const send = (res, status, body, extra = {}) => {
    const data = JSON.stringify(body);
    res.writeHead(status, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", ...extra });
    res.end(data);
  };

  const clientIp = (req) => {
    if (cfg.trustProxy) {
      const fwd = String(req.headers[cfg.clientIpHeader || "x-forwarded-for"] || "").split(",")[0].trim();
      if (fwd) return fwd;
    }
    return req.socket.remoteAddress || "unknown";
  };

  const limit = (key, [count, windowSec]) => {
    if (!limiter.hit(key, count, windowSec)) throw new HttpError(429, "rate_limited");
  };

  async function readJson(req, maxBytes) {
    const type = String(req.headers["content-type"] || "");
    if (!type.startsWith("application/json")) throw new HttpError(415, "bad_request");
    let size = 0;
    const chunks = [];
    for await (const chunk of req) {
      size += chunk.length;
      if (size > maxBytes) throw new HttpError(413, "payload_too_large");
      chunks.push(chunk);
    }
    try {
      const v = JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
      if (!v || typeof v !== "object" || Array.isArray(v)) throw new Error();
      return v;
    } catch {
      throw new HttpError(400, "bad_request");
    }
  }

  // Mutating requests: custom header (cannot be sent cross-site without CORS) and, when present, a same-origin Origin.
  const checkCsrf = (req) => {
    if (req.headers["x-portal"] !== "1") throw new HttpError(403, "forbidden");
    const origin = req.headers.origin;
    if (origin) {
      const host = req.headers["x-forwarded-host"] && cfg.trustProxy ? req.headers["x-forwarded-host"] : req.headers.host;
      try {
        if (new URL(origin).host !== host) throw new Error();
      } catch {
        throw new HttpError(403, "forbidden");
      }
    }
  };

  const currentUser = (req) => readToken(parseCookies(req.headers.cookie)[COOKIE], cfg.sessionSecret);
  const requireUser = (req) => {
    const u = currentUser(req);
    if (!u) throw new HttpError(401, "signed_out");
    return u;
  };
  const sessionCookie = (user) => cookieHeader(createToken(user, cfg.sessionSecret, cfg.sessionHours),
    { secure: cfg.cookieSecure, maxAgeSec: Math.round(cfg.sessionHours * 3600) });
  const clearCookie = () => cookieHeader("", { secure: cfg.cookieSecure, maxAgeSec: 0 });

  const publicUser = (u) => ({ email: u.email, full_name: u.full_name || "" });
  const str = (v, max) => (typeof v === "string" ? v.slice(0, max) : "");
  const langParam = (url) => {
    const l = url.searchParams.get("lang") || "";
    if (!/^[a-z]{2,3}(-[A-Za-z]{2,4})?$/.test(l)) throw new HttpError(400, "unknown_language");
    return l;
  };

  async function siteConfig(lang) {
    const key = lang || "";
    const hit = configCache.get(key);
    if (hit && hit.until > Date.now()) return hit.value;
    const value = await frappe.config(lang || undefined);
    configCache.set(key, { value, until: Date.now() + 60_000 });
    return value;
  }

  // ---------------- API routes ----------------
  async function api(req, res, url) {
    const { pathname } = url;
    const m = req.method;
    const ip = clientIp(req);
    limit(`all:${ip}`, LIMITS.all);

    if (m === "GET" && pathname === "/api/config") {
      const lang = url.searchParams.get("lang") ? langParam(url) : "";
      return send(res, 200, await siteConfig(lang));
    }
    if (m === "GET" && pathname === "/api/jobs") {
      const q = { lang: langParam(url), q: str(url.searchParams.get("q"), 100), department: str(url.searchParams.get("department"), 140),
        location: str(url.searchParams.get("location"), 140), page: Math.max(1, Math.min(1000, Number(url.searchParams.get("page")) || 1)) };
      return send(res, 200, await frappe.jobs(q));
    }
    let mm = pathname.match(/^\/api\/jobs\/([A-Za-z0-9._-]{1,140})$/);
    if (m === "GET" && mm) return send(res, 200, await frappe.job(langParam(url), mm[1]));

    if (m === "GET" && pathname === "/api/logo") {
      const r = await frappe.logo(url.searchParams.get("dark") === "1");
      const type = r.headers.get("content-type") || "application/octet-stream";
      if (!/^image\/(png|jpeg|svg\+xml|webp|gif)/.test(type)) throw new HttpError(404, "not_found");
      const buf = Buffer.from(await r.arrayBuffer());
      res.writeHead(200, { "Content-Type": type, "Cache-Control": "public, max-age=3600", "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'" });
      return res.end(buf);
    }

    if (m === "GET" && pathname === "/api/me") {
      const cu = currentUser(req);
      return send(res, 200, { user: cu ? publicUser(cu) : null });
    }

    if (m === "POST" && pathname === "/api/login") {
      checkCsrf(req);
      const b = await readJson(req, 16_384);
      const email = str(b.email, 200).trim().toLowerCase();
      limit(`login_ip:${ip}`, LIMITS.login_ip);
      limit(`login_email:${email}`, LIMITS.login_email);
      const user = await frappe.authenticate(email, str(b.password, 256));
      return send(res, 200, { user: publicUser(user) }, { "Set-Cookie": sessionCookie(user) });
    }
    if (m === "POST" && pathname === "/api/signup") {
      checkCsrf(req);
      limit(`signup:${ip}`, LIMITS.signup);
      const b = await readJson(req, 16_384);
      const r = await frappe.signup({ full_name: str(b.full_name, 140), email: str(b.email, 200), password: str(b.password, 256),
        lang: str(b.lang, 10), next_path: str(b.next, 300) });
      if (r.verification_sent) return send(res, 200, { verification_sent: true, email: r.email });
      return send(res, 200, { user: publicUser(r) }, { "Set-Cookie": sessionCookie(r) });
    }
    // Account links. Resend and forgot always answer the same way (no account discovery).
    if (m === "POST" && (pathname === "/api/resend-verification" || pathname === "/api/forgot-password")) {
      checkCsrf(req);
      const b = await readJson(req, 16_384);
      const email = str(b.email, 200).trim().toLowerCase();
      limit(`account_ip:${ip}`, LIMITS.account_ip);
      limit(`account_email:${email}`, LIMITS.account_email);
      try {
        if (pathname === "/api/resend-verification") await frappe.resendVerification(email, str(b.lang, 10), str(b.next, 300));
        else await frappe.requestPasswordReset(email, str(b.lang, 10));
      } catch (e) {
        if (!(e instanceof FrappeError) || e.code === "server") throw e;
      }
      return send(res, 200, { sent: true });
    }
    if (m === "POST" && pathname === "/api/verify-email") {
      checkCsrf(req);
      limit(`verify:${ip}`, LIMITS.verify);
      const b = await readJson(req, 4096);
      const r = await frappe.verifyEmail(str(b.token, 200));
      return send(res, 200, { user: publicUser(r), next: r.next || "", lang: r.lang || "" }, { "Set-Cookie": sessionCookie(r) });
    }
    if (m === "POST" && pathname === "/api/reset-password") {
      checkCsrf(req);
      limit(`reset:${ip}`, LIMITS.reset);
      const b = await readJson(req, 4096);
      await frappe.resetPassword(str(b.token, 200), str(b.password, 256));
      return send(res, 200, { ok: true }, { "Set-Cookie": clearCookie() });
    }
    if (m === "POST" && pathname === "/api/logout") {
      checkCsrf(req);
      return send(res, 200, { ok: true }, { "Set-Cookie": clearCookie() });
    }

    if (m === "GET" && pathname === "/api/applications") {
      const u = requireUser(req);
      return send(res, 200, await frappe.applications(u, langParam(url)));
    }
    mm = pathname.match(/^\/api\/applications\/([A-Za-z0-9._-]{1,140})$/);
    if (m === "GET" && mm) {
      const u = requireUser(req);
      return send(res, 200, await frappe.application(u, mm[1], langParam(url)));
    }
    if (m === "POST" && pathname === "/api/applications") {
      checkCsrf(req);
      const u = requireUser(req);
      limit(`apply:${ip}`, LIMITS.apply);
      const b = await readJson(req, cfg.maxBodyMb * 1024 * 1024);
      const cv = b.cv && typeof b.cv === "object" ? b.cv : null;
      const name = str(cv?.name, 200).replace(/[\\/\r\n"]/g, "_");
      if (!cv || !name || typeof cv.data !== "string") throw new HttpError(400, "cv_required");
      if (!CV_EXT.test(name)) throw new HttpError(400, "cv_type");
      const data = Buffer.from(cv.data, "base64");
      if (!data.length) throw new HttpError(400, "cv_empty");
      const result = await frappe.apply({ email: u.email, epoch: u.epoch || 0, job: str(b.job, 140), phone: str(b.phone, 30), country: str(b.country, 140), nationality: str(b.nationality, 140),
        cover_note: str(b.cover_note, 4000), declaration: b.declaration ? "1" : "0", talent_pool: b.talent_pool === true ? "1" : "0", lang: str(b.lang, 10),
        screening: JSON.stringify(b.screening && typeof b.screening === "object" && !Array.isArray(b.screening) ? b.screening : {}).slice(0, 20000) },
      { name, data, type: str(cv.type, 100) });
      return send(res, 200, result);
    }
    // Assessments (candidate side). The server times every section; answers are saved one by one.
    if (m === "GET" && pathname === "/api/assessments") {
      const u = requireUser(req);
      return send(res, 200, await frappe.assessments(u, langParam(url)));
    }
    mm = pathname.match(/^\/api\/assessments\/([A-Za-z0-9._-]{1,140})(?:\/(state|start|answer|file|next|event))?$/);
    if (mm) {
      const u = requireUser(req);
      const [, id, action] = mm;
      if (m === "GET" && !action) return send(res, 200, await frappe.assessment(u, id, langParam(url)));
      if (m === "GET" && action === "state") return send(res, 200, await frappe.assessmentState(u, id, langParam(url)));
      if (m === "POST" && action) {
        checkCsrf(req);
        limit(`asm:${u.email}`, LIMITS.assessment_write);
        const b = await readJson(req, action === "file" ? 15 * 1024 * 1024 : 256 * 1024);
        const lang = str(b.lang, 10);
        const section = Number.isInteger(b.section) ? b.section : -1;
        if (action === "start") return send(res, 200, await frappe.startAssessment(u, id, lang));
        if (action === "answer") return send(res, 200, await frappe.saveAnswer(u, id, str(b.key, 20), b.answer ?? null, section));
        if (action === "next") return send(res, 200, await frappe.nextSection(u, id, section, lang));
        if (action === "event") return send(res, 200, await frappe.assessmentEvent(u, id, ["hidden", "paste"].includes(b.kind) ? b.kind : ""));
        if (action === "file") {
          const f = b.file && typeof b.file === "object" ? b.file : null;
          const name = str(f?.name, 200).replace(/[\\/\r\n"]/g, "_");
          if (!f || !name || typeof f.data !== "string") throw new HttpError(400, "file_required");
          const data = Buffer.from(f.data, "base64");
          if (!data.length) throw new HttpError(400, "file_required");
          if (data.length > 10 * 1024 * 1024) throw new HttpError(413, "file_too_large", { max_mb: 10 });
          return send(res, 200, await frappe.uploadAnswer(u, id, str(b.key, 20), section, { name, data, type: str(f.type, 100) }));
        }
      }
    }
    // The candidate's own data: a copy to download, and a request to delete the account.
    if (m === "GET" && pathname === "/api/account") {
      const u = requireUser(req);
      return send(res, 200, await frappe.dataRequests(u));
    }
    if (m === "GET" && pathname === "/api/account/data") {
      const u = requireUser(req);
      limit(`mydata:${u.email}`, LIMITS.correction);
      return send(res, 200, await frappe.myData(u), { "Content-Disposition": 'attachment; filename="my-data.json"' });
    }
    if (m === "POST" && pathname === "/api/account/talent-pool") {
      checkCsrf(req);
      const u = requireUser(req);
      const b = await readJson(req, 4096);
      return send(res, 200, await frappe.setTalentPool(u, b.join === true));
    }
    mm = pathname.match(/^\/api\/applications\/([A-Za-z0-9._-]{1,140})\/offer$/);
    if (m === "POST" && mm) {
      checkCsrf(req);
      const u = requireUser(req);
      limit(`offer:${ip}`, LIMITS.correction);
      const b = await readJson(req, 8192);
      return send(res, 200, await frappe.respondOffer(u, mm[1], b.accept === true, str(b.reason, 1000)));
    }
    mm = pathname.match(/^\/api\/applications\/([A-Za-z0-9._-]{1,140})\/book$/);
    if (m === "POST" && mm) {
      checkCsrf(req);
      const u = requireUser(req);
      limit(`book:${ip}`, LIMITS.correction);
      const b = await readJson(req, 4096);
      return send(res, 200, await frappe.bookInterview(u, mm[1], Number.isInteger(b.slot) ? b.slot : -1));
    }
    if (m === "POST" && pathname === "/api/account/delete") {
      checkCsrf(req);
      const u = requireUser(req);
      limit(`delete:${ip}`, LIMITS.correction);
      const b = await readJson(req, 16_384);
      return send(res, 200, await frappe.requestDeletion(u, str(b.reason, 2000)));
    }
    mm = pathname.match(/^\/api\/applications\/([A-Za-z0-9._-]{1,140})\/corrections$/);
    if (m === "POST" && mm) {
      checkCsrf(req);
      const u = requireUser(req);
      limit(`correction:${ip}`, LIMITS.correction);
      const b = await readJson(req, 16_384);
      return send(res, 200, await frappe.addCorrection(u, mm[1], str(b.note, 2000)));
    }
    throw new HttpError(404, "not_found");
  }

  // ---------------- static files (React build) ----------------
  async function serveFile(res, file, cache) {
    try {
      const s = await stat(file);
      if (!s.isFile()) return false;
      const body = await readFile(file);
      res.writeHead(200, { "Content-Type": TYPES[extname(file)] || "application/octet-stream", "Cache-Control": cache });
      res.end(body);
      return true;
    } catch {
      return false;
    }
  }

  async function web(req, res, url) {
    if (req.method !== "GET" && req.method !== "HEAD") return send(res, 405, { error: { code: "bad_request", params: {} } });
    let path;
    try {
      path = decodeURIComponent(url.pathname);
    } catch {
      return send(res, 400, { error: { code: "bad_request", params: {} } });
    }
    if (path === "/" || path === "") {
      let lang = "ar";
      try {
        lang = (await siteConfig("")).default_language || "ar";
      } catch {
        /* Frappe unreachable: fall back to Arabic */
      }
      res.writeHead(302, { Location: `/${lang}`, "Cache-Control": "no-store" });
      return res.end();
    }
    const safe = normalize(path).replace(/^(\.\.[/\\])+/, "");
    if (safe.includes("\0")) return send(res, 400, { error: { code: "bad_request", params: {} } });
    if (safe.startsWith("/assets/") && (await serveFile(res, join(cfg.webDist, safe), "public, max-age=31536000, immutable"))) return;
    if (/^\/[\w.-]+\.(ico|png|svg|txt|webmanifest)$/.test(safe) && (await serveFile(res, join(cfg.webDist, safe), "public, max-age=3600"))) return;
    if (safe.startsWith("/assets/")) return send(res, 404, { error: { code: "not_found", params: {} } });
    // Single page app: every other path is handled by the React router (/ar/..., /en/...).
    if (await serveFile(res, join(cfg.webDist, "index.html"), "no-cache")) return;
    res.writeHead(503, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Portal web build not found. Run the web build (portal/web).");
  }

  return async function handler(req, res) {
    securityHeaders(res);
    const url = new URL(req.url || "/", "http://portal.local");
    try {
      if (url.pathname === "/healthz") return send(res, 200, { ok: true });
      if (url.pathname.startsWith("/api/")) return await api(req, res, url);
      return await web(req, res, url);
    } catch (e) {
      if (e instanceof HttpError || e instanceof FrappeError) {
        const status = e.code === "server" ? 502 : e.status;
        const extra = e.code === "signed_out" && url.pathname !== "/api/login" ? { "Set-Cookie": clearCookie() } : {};
        if (e.status === 413) extra.Connection = "close";   // the rest of the oversized body is not read
        return send(res, status, { error: { code: e.code, params: e.params || {} } }, extra);
      }
      console.error("portal error", e && e.stack ? e.stack.split("\n").slice(0, 3).join(" | ") : e);
      if (!res.headersSent) send(res, 500, { error: { code: "server", params: {} } });
    }
  };
}

if (import.meta.url === pathToFileURL(process.argv[1] || "").href) {
  const cfg = loadConfig();
  const server = createServer(createApp(cfg));
  server.requestTimeout = 180_000;
  server.headersTimeout = 30_000;
  server.listen(cfg.port, () => console.log(`careers portal on :${cfg.port}${cfg.mock ? " (mock Frappe)" : ""}`));
  const stop = () => server.close(() => process.exit(0));
  process.on("SIGTERM", stop);
  process.on("SIGINT", stop);
}
