// Client for moet_careers.portal_api on the internal Frappe server. Uses the service user's API key.
export class FrappeError extends Error {
  constructor(code, status = 502, params = {}) {
    super(code);
    this.code = code;
    this.status = status;
    this.params = params;
  }
}

export function createFrappe(cfg, fetchImpl = globalThis.fetch) {
  const base = `${cfg.frappeUrl}/api/method/moet_careers.portal_api.`;
  const headers = () => {
    const h = { Accept: "application/json", Authorization: `token ${cfg.apiKey}:${cfg.apiSecret}` };
    if (cfg.frappeSite) h["X-Frappe-Site-Name"] = cfg.frappeSite;
    return h;
  };

  async function call(method, { query, json, form, timeoutMs, raw } = {}) {
    const qs = query ? "?" + new URLSearchParams(Object.entries(query).filter(([, v]) => v !== undefined && v !== null && v !== "")) : "";
    const init = { method: json || form ? "POST" : "GET", headers: headers(), signal: AbortSignal.timeout(timeoutMs || cfg.frappeTimeoutMs) };
    if (json) {
      init.headers["Content-Type"] = "application/json";
      init.body = JSON.stringify(json);
    } else if (form) {
      init.body = form;
    }
    let res;
    try {
      res = await fetchImpl(base + method + qs, init);
    } catch (e) {
      throw new FrappeError("server", 502, {}, e);
    }
    if (raw) {
      if (!res.ok) throw new FrappeError(res.status === 404 ? "not_found" : "server", res.status === 404 ? 404 : 502);
      return res;
    }
    let body = {};
    try {
      body = await res.json();
    } catch {
      /* non-JSON error page */
    }
    const msg = body && body.message;
    if (msg && msg.error && msg.error.code) throw new FrappeError(msg.error.code, res.status >= 400 ? res.status : 400, msg.error.params || {});
    if (!res.ok) {
      console.error(`frappe ${method} failed: HTTP ${res.status} ${body.exc_type || ""}`.trim());
      throw new FrappeError("server", 502);
    }
    return msg;
  }

  return {
    config: (lang) => call("config", { query: { lang } }),
    jobs: (q) => call("jobs", { query: q }),
    job: (lang, name) => call("job", { query: { lang, name } }),
    signup: (data) => call("signup", { json: data }),
    authenticate: (email, password) => call("authenticate", { json: { email, password } }),
    applications: (email, lang) => call("applications", { query: { email, lang } }),
    application: (email, name, lang) => call("application", { query: { email, name, lang } }),
    addCorrection: (email, name, note) => call("add_correction", { json: { email, name, note } }),
    apply: (fields, file) => {
      const form = new FormData();
      for (const [k, v] of Object.entries(fields)) form.append(k, String(v ?? ""));
      form.append("cv", new Blob([file.data], { type: file.type || "application/octet-stream" }), file.name);
      return call("apply", { form, timeoutMs: 120_000 });
    },
    logo: (dark) => call("logo", { query: { dark: dark ? 1 : 0 }, raw: true }),
  };
}
