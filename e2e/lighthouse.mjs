// Lighthouse on the public pages: the careers portal (job list, a job, sign-in) and the backoffice sign-in, in Arabic and
// English. Accessibility and best practices must stay at or above the floors; performance and SEO are reported.
// Usage: PORTAL_URL=http://127.0.0.1:8080 BASE_URL=http://127.0.0.1:4173 CHROME_PATH=... node lighthouse.mjs
import { execFileSync } from "node:child_process";
import fs from "node:fs";

const PORTAL = process.env.PORTAL_URL || "http://127.0.0.1:8080";
const BASE = process.env.BASE_URL || "http://127.0.0.1:4173";
const FLOORS = { accessibility: 0.9, "best-practices": 0.8 };
const pages = [
  ["Portal jobs (ar)", `${PORTAL}/ar`], ["Portal jobs (en)", `${PORTAL}/en`], ["Portal sign-in (en)", `${PORTAL}/en/sign-in`],
  ["Backoffice sign-in (ar)", `${BASE}/ar/sign-in`], ["Backoffice sign-in (en)", `${BASE}/en/sign-in`],
];
fs.mkdirSync("lighthouse", { recursive: true });
const rows = [];
let failed = false;
for (const [name, url] of pages) {
  const out = `lighthouse/${name.replace(/[^a-z0-9]+/gi, "_").toLowerCase()}.json`;
  try {
    execFileSync("npx", ["-y", "lighthouse@12.2.1", url, "--quiet", "--output=json", `--output-path=${out}`,
      "--only-categories=performance,accessibility,best-practices,seo", "--preset=desktop",
      "--chrome-flags=--headless=new --no-sandbox --disable-gpu"], { stdio: ["ignore", "ignore", "inherit"], timeout: 180_000 });
  } catch (e) {
    console.error(`FAIL  ${name}: lighthouse did not run (${e.message.split("\n")[0]})`);
    failed = true;
    continue;
  }
  const r = JSON.parse(fs.readFileSync(out, "utf8"));
  const score = (k) => r.categories[k]?.score ?? 0;
  const row = { name, performance: score("performance"), accessibility: score("accessibility"), "best-practices": score("best-practices"), seo: score("seo") };
  rows.push(row);
  for (const [k, floor] of Object.entries(FLOORS)) {
    if (row[k] < floor) {
      failed = true;
      const misses = Object.values(r.audits).filter((a) => a.score !== null && a.score < 1 && r.categories[k].auditRefs.some((x) => x.id === a.id && x.weight > 0))
        .map((a) => `${a.id}: ${a.title}`).slice(0, 8);
      console.error(`FAIL  ${name}: ${k} ${Math.round(row[k] * 100)} < ${floor * 100}\n      ${misses.join("\n      ")}`);
    }
  }
}
const pct = (v) => String(Math.round(v * 100)).padStart(3);
const table = ["| Page | Performance | Accessibility | Best practices | SEO |", "|---|---|---|---|---|",
  ...rows.map((r) => `| ${r.name} | ${pct(r.performance)} | ${pct(r.accessibility)} | ${pct(r["best-practices"])} | ${pct(r.seo)} |`)].join("\n");
console.log(table);
if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `## Lighthouse\n\n${table}\n`);
process.exit(failed ? 1 : 0);
