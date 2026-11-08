// Stateless signed session cookie: base64url(JSON) + "." + HMAC-SHA256. Holds only the applicant's email and name.
import { createHmac, timingSafeEqual } from "node:crypto";

export const COOKIE = "mc_session";

const b64 = (s) => Buffer.from(s).toString("base64url");
const sign = (data, secret) => createHmac("sha256", secret).update(data).digest("base64url");

export function createToken(user, secret, hours, now = Date.now()) {
  const data = b64(JSON.stringify({ e: user.email, n: user.full_name || "", x: now + hours * 3600_000 }));
  return `${data}.${sign(data, secret)}`;
}

export function readToken(token, secret, now = Date.now()) {
  if (!token || typeof token !== "string" || token.length > 2048) return null;
  const [data, mac] = token.split(".");
  if (!data || !mac) return null;
  const expected = Buffer.from(sign(data, secret));
  const given = Buffer.from(mac);
  if (expected.length !== given.length || !timingSafeEqual(expected, given)) return null;
  try {
    const p = JSON.parse(Buffer.from(data, "base64url").toString());
    if (!p.e || typeof p.x !== "number" || p.x < now) return null;
    return { email: p.e, full_name: p.n || "" };
  } catch {
    return null;
  }
}

export function parseCookies(header = "") {
  const out = {};
  for (const part of header.split(";")) {
    const i = part.indexOf("=");
    if (i > 0) out[part.slice(0, i).trim()] = decodeURIComponent(part.slice(i + 1).trim());
  }
  return out;
}

export function cookieHeader(value, { secure, maxAgeSec }) {
  const attrs = [`${COOKIE}=${encodeURIComponent(value)}`, "Path=/", "HttpOnly", "SameSite=Lax", `Max-Age=${maxAgeSec}`];
  if (secure) attrs.push("Secure");
  return attrs.join("; ");
}
