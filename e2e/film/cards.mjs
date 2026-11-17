// Draws the film's text in the app's own fonts (Inter and Noto Kufi Arabic, bundled with the portal): a transparent
// caption overlay per frame, the title card, a card per chapter and the end card, all 1920 x 1080.
// Usage (after film.mjs): REPO=../.. node cards.mjs
import { chromium } from "playwright";
import fs from "node:fs";
import path from "node:path";

const REPO = process.env.REPO || "../..";
const OUT = "film";
const manifest = JSON.parse(fs.readFileSync(path.join(OUT, "manifest.json"), "utf8"));
const fontDir = (pkg) => path.resolve(REPO, "portal/web/node_modules/@fontsource-variable", pkg, "files");
const font = (pkg, re) => `file://${path.join(fontDir(pkg), fs.readdirSync(fontDir(pkg)).find((f) => re.test(f)))}`;
const INTER = font("inter", /-latin-wght-normal\.woff2$/);
const KUFI = font("noto-kufi-arabic", /-arabic-wght-normal\.woff2$/);
const LOGO = `data:image/png;base64,${fs.readFileSync(path.resolve(REPO, "portal/web/public/logo-en.png")).toString("base64")}`;

const css = `
@font-face { font-family: Inter; src: url(${INTER}); font-weight: 100 900; }
@font-face { font-family: Kufi; src: url(${KUFI}); font-weight: 100 900; }
html, body { margin: 0; width: 1920px; height: 1080px; }
body { font-family: Inter, sans-serif; -webkit-font-smoothing: antialiased; }
.ar { font-family: Kufi, sans-serif; direction: rtl; }
`;

const caption = (m) => `<body style="background: transparent">
  <div style="position:absolute; left:50%; bottom:46px; transform:translateX(-50%); max-width:1560px; min-width:900px; box-sizing:border-box;
      padding:22px 40px 24px; border-radius:22px; background:rgba(16,20,26,.86); box-shadow:0 12px 40px rgba(0,0,0,.35); text-align:center">
    <div style="font-size:19px; letter-spacing:.14em; text-transform:uppercase; color:#d9bf85; font-weight:700; margin-bottom:8px">
      ${m.chapter} <span style="opacity:.5">&nbsp;·&nbsp;</span> <span class="ar" style="letter-spacing:0; text-transform:none">${m.chapter_ar}</span></div>
    <div style="font-size:38px; line-height:1.25; color:#fff; font-weight:650">${m.caption}</div>
    <div class="ar" style="font-size:31px; line-height:1.5; color:#efe3c6; font-weight:500; margin-top:6px">${m.caption_ar}</div>
  </div></body>`;

const card = ({ kicker = "", title, titleAr, sub = "", subAr = "", n = "" }) => `<body style="background:
    radial-gradient(1100px 600px at 85% -10%, rgba(182,138,53,.28), transparent 60%), radial-gradient(900px 500px at -10% 110%, rgba(182,138,53,.18), transparent 60%), #13171d;
    color:#fff; display:flex; flex-direction:column; justify-content:center; padding:0 160px; box-sizing:border-box">
  <img src="${LOGO}" style="position:absolute; top:70px; left:160px; height:72px; background:#fff; border-radius:14px; padding:10px 16px">
  ${n ? `<div style="font-size:150px; font-weight:800; color:#b68a35; line-height:1; margin-bottom:24px">${n}</div>` : ""}
  ${kicker ? `<div style="font-size:24px; letter-spacing:.16em; text-transform:uppercase; color:#d9bf85; font-weight:700; margin-bottom:22px">${kicker}</div>` : ""}
  <div style="font-size:84px; font-weight:750; line-height:1.08">${title}</div>
  <div class="ar" style="font-size:64px; font-weight:700; line-height:1.5; color:#efe3c6; margin-top:18px; text-align:right">${titleAr}</div>
  ${sub ? `<div style="font-size:30px; color:#c9ced6; margin-top:36px; max-width:1300px; line-height:1.45">${sub}</div>` : ""}
  ${subAr ? `<div class="ar" style="font-size:28px; color:#c9ced6; margin-top:10px; line-height:1.7; text-align:right">${subAr}</div>` : ""}
</body>`;

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
const draw = async (html, file, transparent) => {
  await page.setContent(`<!doctype html><meta charset="utf-8"><style>${css}</style>${html}`, { waitUntil: "load" });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(150);
  await page.screenshot({ path: path.join(OUT, file), omitBackground: transparent, ...(transparent ? {} : { type: "jpeg", quality: 95 }) });
};

for (const m of manifest) await draw(caption(m), m.file.replace(".jpg", ".cap.png"), true);
await draw(card({ kicker: "Ministry of Economy & Tourism · Recruitment", title: "From Job Opening to Interview", titleAr: "من الإعلان عن الوظيفة إلى المقابلة",
  sub: "One candidate's journey: requirements, applying, the Big Five questionnaire, scoring and booking an interview.",
  subAr: "رحلة مرشحة واحدة: المتطلبات، والتقديم، واستبيان السمات الخمس، والتقييم، وحجز المقابلة." }), "card_title.jpg", false);
const chapters = [...new Map(manifest.map((m) => [m.chapter, m])).values()];
for (const [i, m] of chapters.entries()) await draw(card({ n: String(i + 1), title: m.chapter, titleAr: m.chapter_ar }), `card_${i + 1}.jpg`, false);
await draw(card({ kicker: "Recommendations only · people decide", title: "Faster, fairer, explainable hiring", titleAr: "توظيف أسرع وأكثر عدلا وقابلية للتفسير",
  sub: "Every score shows its evidence. HR approves the requirements and makes every decision.",
  subAr: "كل تقييم يظهر دليله. يعتمد فريق الموارد البشرية المتطلبات ويتخذ كل القرارات." }), "card_end.jpg", false);
fs.writeFileSync(path.join(OUT, "chapters.json"), JSON.stringify(chapters.map((m, i) => ({ n: i + 1, chapter: m.chapter, first: m.file }))));
await browser.close();
console.log(`captions for ${manifest.length} frames and ${chapters.length + 2} cards`);
