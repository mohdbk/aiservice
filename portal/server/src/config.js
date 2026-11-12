// All settings come from the environment. See docs/ON_PREM.md, "External careers portal".
const env = process.env;
const bool = (v, d) => (v === undefined || v === "" ? d : !["0", "false", "no", "off"].includes(String(v).toLowerCase()));

export function loadConfig(overrides = {}) {
  const c = {
    port: Number(env.PORT || 8080),
    // The recruitment API (.NET). FRAPPE_URL is still read for older installations.
    apiUrl: (env.RECRUITMENT_API_URL || "").replace(/\/+$/, ""),
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
    const missing = [c.apiUrl || c.frappeUrl ? null : "RECRUITMENT_API_URL", c.apiKey ? null : "CAREERS_PORTAL_API_KEY", c.apiSecret ? null : "CAREERS_PORTAL_API_SECRET"].filter(Boolean);
    if (missing.length) throw new Error(`Missing settings: ${missing.join(", ")}`);
  }
  if (!c.sessionSecret || c.sessionSecret.length < 32) {
    if (!c.mock) throw new Error("SESSION_SECRET must be at least 32 characters");
    c.sessionSecret = "mock-session-secret-not-for-production-use-0000";
  }
  return c;
}
