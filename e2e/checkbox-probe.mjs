// Probe: how the built portal CSS draws checkboxes and radios, checked and unchecked (computed styles and a screenshot).
import { chromium } from "playwright";
import fs from "node:fs";
const css = fs.readdirSync("portal/web/dist/assets").filter((f) => f.endsWith(".css")).map((f) => fs.readFileSync(`portal/web/dist/assets/${f}`, "utf8")).join("\n");
const html = `<!doctype html><html lang="en" dir="ltr"><head><style>${css}</style></head><body class="p-6 space-y-3">
<div class="aegov-check-item"><input id="a" type="checkbox"><label for="a">aegov-check-item</label></div>
<label class="flex items-center gap-3"><input id="b" type="checkbox" class="h-5 w-5 rounded accent-primary-600"><span>screening checkbox</span></label>
<label class="flex items-center gap-3"><input id="c" type="radio" name="r" class="h-5 w-5 rounded-full accent-primary-600"><span>screening radio</span></label>
</body></html>`;
const b = await chromium.launch();
const p = await b.newPage();
await p.setContent(html);
const read = () => p.evaluate(() => ["a", "b", "c"].map((id) => {
  const e = document.getElementById(id); const s = getComputedStyle(e);
  return `${id} checked=${e.checked} appearance=${s.appearance} bg=${s.backgroundColor} img=${s.backgroundImage.slice(0, 60)} border=${s.borderColor} size=${s.width}x${s.height} opacity=${s.opacity} display=${s.display} pos=${s.position}`;
}).join("\n"));
console.log("UNCHECKED\n" + await read());
await p.screenshot({ path: "probe-unchecked.png" });
for (const id of ["a", "b", "c"]) await p.click(`#${id}`, { force: true }).catch((e) => console.log("click", id, e.message.split("\n")[0]));
console.log("AFTER CLICK\n" + await read());
await p.click("label[for=a]");
console.log("AFTER LABEL CLICK a\n" + await read());
await p.screenshot({ path: "probe-checked.png" });
const rules = [...css.matchAll(/[^{}]*check[^{}]*\{[^}]*\}/g)].map((m) => m[0].trim()).filter((r) => /aegov-check|checkbox|:checked/.test(r)).slice(0, 25);
console.log("RULES\n" + rules.join("\n"));
await b.close();
