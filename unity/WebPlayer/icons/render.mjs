// Renders the web player's PNG icons from eleven.svg (Erik's ELEVEN icon) in Chromium.
//
//   node unity/WebPlayer/icons/render.mjs
//
// Needs Playwright with Chromium. Re-run and commit the PNGs whenever eleven.svg changes.
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const dir = path.dirname(fileURLToPath(import.meta.url));
const svg = fs.readFileSync(path.join(dir, 'eleven.svg'), 'utf8');
const sizes = { 'eleven-180.png': 180, 'eleven-192.png': 192, 'eleven-512.png': 512, 'eleven-32.png': 32 };

const browser = await chromium.launch();
try {
  for (const [name, size] of Object.entries(sizes)) {
    const page = await browser.newPage({ viewport: { width: size, height: size } });
    await page.setContent(`<style>html,body{margin:0}svg{display:block;width:${size}px;height:${size}px}</style>${svg}`);
    await page.screenshot({ path: path.join(dir, name), omitBackground: false });
    await page.close();
    console.log(`${name} ${size}x${size}`);
  }
} finally {
  await browser.close();
}
