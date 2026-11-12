// Client for the recruitment API's portal endpoints (/api/portal/{method}) on the internal network, using the portal's
// service key. Older installations can still point FRAPPE_URL at moet_careers.portal_api on Frappe.
export class FrappeError extends Error {
  constructor(code, status = 502, params = {}) {
    super(code);
    this.code = code;
    this.status = status;
    this.params = params;
  }
}

export function createFrappe(cfg, fetchImpl = globalThis.fetch) {
  const base = cfg.apiUrl ? `${cfg.apiUrl}/api/portal/` : `${cfg.frappeUrl}/api/method/moet_careers.portal_api.`;
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
    resendVerification: (email, lang, next_path) => call("resend_verification", { json: { email, lang, next_path } }),
    verifyEmail: (token) => call("verify_email", { json: { token } }),
    requestPasswordReset: (email, lang) => call("request_password_reset", { json: { email, lang } }),
    resetPassword: (token, password) => call("reset_password", { json: { token, password } }),
    // epoch: session version from sign-in; a password reset changes it and signs out old sessions
    applications: (u, lang) => call("applications", { query: { email: u.email, epoch: u.epoch || 0, lang } }),
    application: (u, name, lang) => call("application", { query: { email: u.email, epoch: u.epoch || 0, name, lang } }),
    addCorrection: (u, name, note) => call("add_correction", { json: { email: u.email, epoch: u.epoch || 0, name, note } }),
    apply: (fields, file) => {
      const form = new FormData();
      for (const [k, v] of Object.entries(fields)) form.append(k, String(v ?? ""));
      form.append("cv", new Blob([file.data], { type: file.type || "application/octet-stream" }), file.name);
      return call("apply", { form, timeoutMs: 120_000 });
    },
    assessments: (u, lang) => call("assessments", { query: { email: u.email, epoch: u.epoch || 0, lang } }),
    assessment: (u, name, lang) => call("assessment", { query: { email: u.email, epoch: u.epoch || 0, name, lang } }),
    startAssessment: (u, name, lang) => call("start_assessment", { json: { email: u.email, epoch: u.epoch || 0, name, lang } }),
    assessmentState: (u, name, lang) => call("assessment_state", { query: { email: u.email, epoch: u.epoch || 0, name, lang } }),
    saveAnswer: (u, name, key, answer, section) =>
      call("save_assessment_answer", { json: { email: u.email, epoch: u.epoch || 0, name, key, section, answer: JSON.stringify(answer ?? null) } }),
    uploadAnswer: (u, name, key, section, file) => {
      const form = new FormData();
      for (const [k, v] of Object.entries({ email: u.email, epoch: u.epoch || 0, name, key, section })) form.append(k, String(v));
      form.append("file", new Blob([file.data], { type: file.type || "application/octet-stream" }), file.name);
      return call("upload_assessment_file", { form, timeoutMs: 120_000 });
    },
    nextSection: (u, name, section, lang) => call("next_assessment_section", { json: { email: u.email, epoch: u.epoch || 0, name, section, lang } }),
    assessmentEvent: (u, name, kind) => call("assessment_event", { json: { email: u.email, epoch: u.epoch || 0, name, kind } }),
    // Personal data: a copy, and a request to delete the account (reviewed by the Ministry).
    myData: (u) => call("my_data", { query: { email: u.email, epoch: u.epoch || 0 } }),
    dataRequests: (u) => call("data_requests", { query: { email: u.email, epoch: u.epoch || 0 } }),
    setTalentPool: (u, join) => call("talent_pool", { json: { email: u.email, epoch: u.epoch || 0, join: join ? "1" : "0" } }),
    respondOffer: (u, name, accept, reason) => call("respond_offer", { json: { email: u.email, epoch: u.epoch || 0, name, accept: accept ? "1" : "0", reason } }),
    bookInterview: (u, name, slot) => call("book_interview", { json: { email: u.email, epoch: u.epoch || 0, name, slot } }),
    requestDeletion: (u, reason) => call("request_deletion", { json: { email: u.email, epoch: u.epoch || 0, reason } }),
    logo: (dark) => call("logo", { query: { dark: dark ? 1 : 0 }, raw: true }),
  };
}
