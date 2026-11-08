// All settings come from the environment. See docs/ON_PREM.md, "External careers portal".
const env = process.env;
const bool = (v, d) => (v === undefined || v === "" ? d : !["0", "false", "no", "off"].includes(String(v).toLowerCase()));

export function loadConfig(overrides = {}) {
  const c = {
    port: Number(env.PORT || 8080),
    frappeUrl: (env.FRAPPE_URL || "").replace(/\/+$/, ""),
    frappeSite: env.FRAPPE_SITE || "",
    apiKey: env.CAREERS_PORTAL_API_KEY || "",
    apiSecret: env.CAREERS_PORTAL_API_SECRET || "",
    sessionSecret: env.SESSION_SECRET || "",
    sessionHours: Number(env.SESSION_HOURS || 8),
    cookieSecure: bool(env.COOKIE_SECURE, true),
    trustProxy: bool(env.TRUST_PROXY, false),
    // Header holding the client address when behind a trusted proxy: x-forwarded-for (first entry) or x-real-ip.
    clientIpHeader: (env.CLIENT_IP_HEADER || "x-forwarded-for").toLowerCase(),
    mock: bool(env.MOCK_FRAPPE, false),
    webDist: env.WEB_DIST || new URL("../../web/dist", import.meta.url).pathname,
    maxBodyMb: Number(env.MAX_BODY_MB || 30),
    frappeTimeoutMs: Number(env.FRAPPE_TIMEOUT_MS || 30000),
    ...overrides,
  };
  if (!c.mock) {
    const missing = ["frappeUrl", "apiKey", "apiSecret"].filter((k) => !c[k]);
    if (missing.length) throw new Error(`Missing settings: ${missing.join(", ")} (FRAPPE_URL, CAREERS_PORTAL_API_KEY, CAREERS_PORTAL_API_SECRET)`);
  }
  if (!c.sessionSecret || c.sessionSecret.length < 32) {
    if (!c.mock) throw new Error("SESSION_SECRET must be at least 32 characters");
    c.sessionSecret = "mock-session-secret-not-for-production-use-0000";
  }
  return c;
}
