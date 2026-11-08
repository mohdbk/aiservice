// Render data/*.html to A4 PNGs at about 200 DPI (1588 px wide), like production's pdftoppm -r 200.
const { chromium } = require("playwright");
const fs = require("fs");
const path = require("path");

(async () => {
  const dir = path.join(__dirname, "data");
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 794, height: 1123 }, deviceScaleFactor: 2 });
  for (const f of fs.readdirSync(dir).filter((f) => f.endsWith(".html")).sort()) {
    await page.goto("file://" + path.join(dir, f));
    await page.waitForTimeout(150);
    await page.screenshot({ path: path.join(dir, f.replace(".html", ".png")), fullPage: true });
    console.log("rendered", f);
  }
  await browser.close();
})();
