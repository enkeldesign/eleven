// Smoke test for the Unity Web player in headless Chromium, run by CI after the GameCI build.
//
//   node smoke.mjs <player dir with index.html> <dir with .NET hash logs> <output dir>
//
// It boots the player, reads the host's log lines from the browser console, and drives the
// on-screen controls with the mouse through Unity's Input System:
//   - boot: the host reports ready, with its build id
//   - autorun: the issue 1 scenario's digests in WebGL, compared with the .NET hash logs
//   - Maths and Run scenario buttons: each tap is acknowledged in the log
//   - stick: dragging it moves the dot (from the host's trace lines)
//   - rendering: the screenshot contains the pitch, line, dot and ball colours
// Results go to stdout as GitHub annotations and to <output dir>/smoke.json with screenshots.
// A digest mismatch is a warning, not a failure: cross-runtime determinism is #5's question.

import { chromium } from 'playwright';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';

const [playerDir, hashlogDir, outDir] = process.argv.slice(2);
if (!playerDir || !hashlogDir || !outDir) {
  console.error('usage: node smoke.mjs <player dir> <hash log dir> <output dir>');
  process.exit(2);
}
fs.mkdirSync(outDir, { recursive: true });

// Colours from HostPalette.cs.
const colours = {
  pitch: [0x2e, 0x6b, 0x3a],
  line: [0xf2, 0xf2, 0xf2],
  dot: [0xe0, 0x4a, 0x3f],
  ball: [0xf7, 0xe2, 0x6b],
};

const results = { checks: [], notes: {} };
const annotate = (level, title, message) =>
  console.log(`::${level} title=${title}::${String(message).replace(/%/g, '%25').replace(/\r?\n/g, '%0A')}`);
const check = (name, ok, detail) => {
  results.checks.push({ name, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'} ${name}: ${detail}`);
};

function expectedDigests() {
  const digests = {};
  for (const file of fs.readdirSync(hashlogDir)) {
    const text = fs.readFileSync(path.join(hashlogDir, file), 'utf8');
    const maths = /^# maths: (\w+)$/m.exec(text);
    const digest = /^# digest: ([0-9a-f]{16})$/m.exec(text);
    if (maths && digest) digests[maths[1]] = digest[1];
  }
  return digests;
}

function serve(root) {
  const types = {
    '.html': 'text/html',
    '.js': 'application/javascript',
    '.wasm': 'application/wasm',
    '.css': 'text/css',
    '.json': 'application/json',
    '.png': 'image/png',
    '.ico': 'image/x-icon',
  };
  const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    const file = path.join(root, decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
    if (!file.startsWith(path.resolve(root)) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
      res.writeHead(404);
      res.end();
      return;
    }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    fs.createReadStream(file).pipe(res);
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)));
}

const logs = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function waitForLog(pattern, timeoutMs, from = 0) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    for (let i = from; i < logs.length; i++) {
      const m = pattern.exec(logs[i]);
      if (m) return { match: m, index: i };
    }
    if (Date.now() > deadline) return null;
    await sleep(100);
  }
}
const lastLog = (pattern) => {
  for (let i = logs.length - 1; i >= 0; i--) {
    const m = pattern.exec(logs[i]);
    if (m) return m;
  }
  return null;
};

const server = await serve(path.resolve(playerDir));
const port = server.address().port;
const browser = await chromium.launch({
  args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--ignore-gpu-blocklist'],
});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
page.on('console', (m) => logs.push(m.text()));
page.on('pageerror', (e) => logs.push(`pageerror ${e.message}`));

let exitCode = 0;
try {
  // Boot.
  const started = Date.now();
  await page.goto(`http://127.0.0.1:${port}/index.html?trace=1&autorun=1`);
  const ready = await waitForLog(/ELEVEN host ready build=(\S+) maths=(\w+)/, 240_000);
  const bootSeconds = (Date.now() - started) / 1000;
  results.notes.bootSeconds = bootSeconds;
  check('boot', !!ready, ready ? `host ready in ${bootSeconds.toFixed(1)} s, build ${ready.match[1]}` : 'no "ELEVEN host ready" within 240 s');
  if (!ready) throw new Error('player did not boot');
  results.notes.build = ready.match[1];

  // Autorun: the scenario inside the build, both backends, against .NET.
  const expected = expectedDigests();
  for (const maths of ['SystemMath', 'Portable']) {
    const line = await waitForLog(new RegExp(`ELEVEN scenario name=\\S+ maths=${maths} digest=([0-9a-f]{16}) final=([0-9a-f]{16}) hashes=(\\d+) events=(\\d+) config=\\w+ ms=(\\d+)`), 60_000);
    check(`scenario ${maths} runs in the build`, !!line, line ? `${line.match[3]} hashes in ${line.match[5]} ms` : 'no scenario line');
    if (!line) continue;
    const digest = line.match[1];
    results.notes[`digest${maths}`] = digest;
    const same = expected[maths] === digest;
    results.notes[`matchesDotnet${maths}`] = same;
    const message = `${maths}: WebGL (Chromium) ${digest}, .NET ${expected[maths] ?? 'missing'} — ${same ? 'identical' : 'DIFFERENT'}`;
    annotate(same ? 'notice' : 'warning', 'Scenario digest vs .NET', message);
  }

  // Controls, positioned from the host's own layout line. The host logs a new line whenever the
  // screen size changes, and the canvas can still be resizing just after boot, so wait for the
  // size to match the canvas and use the latest line.
  const layoutPattern = /ELEVEN layout screen=(\d+)x(\d+) run=(\d+),(\d+) maths=(\d+),(\d+) reset=(\d+),(\d+) kick=(\d+),(\d+) stick=(\d+),(\d+) stickRadius=(\d+)/;
  const canvas = page.locator('#unity-canvas');
  let layout = null;
  let backing = null;
  for (let i = 0; i < 50; i++) {
    backing = await canvas.evaluate((c) => [c.width, c.height]);
    layout = lastLog(layoutPattern);
    if (layout && Number(layout[1]) === backing[0] && Number(layout[2]) === backing[1]) break;
    await sleep(200);
  }
  const layouts = logs.filter((l) => layoutPattern.test(l)).map((l) => l.replace('ELEVEN layout ', ''));
  results.notes.layouts = layouts;
  check('layout matches the canvas', !!layout && Number(layout[1]) === backing[0] && Number(layout[2]) === backing[1],
    `canvas ${backing?.join('x')}; layouts logged: ${layouts.join(' / ') || 'none'}`);
  if (!layout) throw new Error('no layout');
  const n = layout.slice(1).map(Number);
  const box = await canvas.boundingBox();
  const sx = box.width / n[0];
  const sy = box.height / n[1];
  const at = (x, y) => ({ x: box.x + x * sx, y: box.y + y * sy });
  const tap = async ({ x, y }) => {
    await page.mouse.move(x, y);
    await page.mouse.down();
    await sleep(150); // held across several frames, as a finger would be
    await page.mouse.up();
    await sleep(150);
  };

  for (const maths of ['SystemMath', 'Portable']) {
    let from = logs.length;
    await tap(at(n[4], n[5]));
    const switched = await waitForLog(new RegExp(`ELEVEN maths=${maths} `), 5_000, from);
    check(`Maths button switches to ${maths}`, !!switched, switched ? switched.match[0].trim() : 'no acknowledgement');

    from = logs.length;
    await tap(at(n[2], n[3]));
    const ran = await waitForLog(new RegExp(`ELEVEN scenario name=\\S+ maths=${maths} digest=([0-9a-f]{16})`), 15_000, from);
    check(`Run scenario button with ${maths}`, !!ran, ran ? `digest ${ran.match[1]}` : 'no scenario line after tap');
  }

  // Stick: drag right from the stick's rest position and hold; the dot should move along +x.
  const before = lastLog(/ELEVEN trace tick=(\d+) dot=(-?[\d.]+),(-?[\d.]+)/);
  const origin = at(n[10], n[11]);
  await page.mouse.move(origin.x, origin.y);
  await page.mouse.down();
  for (let i = 1; i <= 10; i++) {
    await page.mouse.move(origin.x + (n[12] * sx * i) / 10, origin.y, { steps: 1 });
    await sleep(20);
  }
  await sleep(2500);
  await page.screenshot({ path: path.join(outDir, 'playing.png') });
  await page.mouse.up();
  await sleep(1200);
  const after = lastLog(/ELEVEN trace tick=(\d+) dot=(-?[\d.]+),(-?[\d.]+)/);
  const moved = before && after ? Number(after[2]) - Number(before[2]) : NaN;
  results.notes.stickMovedMetres = moved;
  check('stick moves the dot', moved > 3, before && after ? `dot x ${before[2]} → ${after[2]} (ticks ${before[1]} → ${after[1]})` : 'no trace lines');

  // Kick button: a tap must be taken without errors (whether it connects depends on position).
  const fromKick = logs.length;
  await tap(at(n[8], n[9]));
  await sleep(500);
  const kickErrors = logs.slice(fromKick).filter((l) => /error|exception/i.test(l));
  check('kick button tap', kickErrors.length === 0, kickErrors.length ? kickErrors.join(' | ') : 'no errors');

  // Rendering: count pixels close to each palette colour in a canvas screenshot.
  const png = await canvas.screenshot({ path: path.join(outDir, 'canvas.png') });
  const counts = await page.evaluate(async ({ data, colours }) => {
    const image = new Image();
    image.src = `data:image/png;base64,${data}`;
    await image.decode();
    const c = document.createElement('canvas');
    c.width = image.width;
    c.height = image.height;
    const g = c.getContext('2d');
    g.drawImage(image, 0, 0);
    const px = g.getImageData(0, 0, c.width, c.height).data;
    const out = Object.fromEntries(Object.keys(colours).map((k) => [k, 0]));
    for (let i = 0; i < px.length; i += 4) {
      for (const [k, [r, gg, b]] of Object.entries(colours)) {
        if (Math.abs(px[i] - r) + Math.abs(px[i + 1] - gg) + Math.abs(px[i + 2] - b) < 24) out[k]++;
      }
    }
    return out;
  }, { data: png.toString('base64'), colours });
  results.notes.pixels = counts;
  check('pitch, line, dot and ball are drawn', Object.values(counts).every((v) => v > 20), JSON.stringify(counts));

  const errors = logs.filter((l) => /^pageerror|Exception|error CS/i.test(l));
  check('no page errors or exceptions', errors.length === 0, errors.length ? errors.slice(0, 5).join(' | ') : 'none');
} catch (e) {
  check('smoke test ran to completion', false, e.message);
} finally {
  fs.writeFileSync(path.join(outDir, 'console.log'), logs.join('\n'));
  fs.writeFileSync(path.join(outDir, 'smoke.json'), JSON.stringify(results, null, 2));
  await browser.close();
  server.close();
}

const failed = results.checks.filter((c) => !c.ok);
if (failed.length) {
  // Context for diagnosis without the uploaded console log.
  const presses = logs.filter((l) => l.startsWith('ELEVEN press')).slice(-8);
  const traces = logs.filter((l) => l.startsWith('ELEVEN trace')).slice(-3);
  annotate('notice', 'Smoke context', `layouts: ${(results.notes.layouts || []).join(' / ')}\npresses: ${presses.join(' | ') || 'none'}\ntraces: ${traces.join(' | ')}`);
}
for (const c of failed) annotate('error', `Smoke: ${c.name}`, c.detail);
annotate(
  'notice',
  'WebGL smoke test',
  `${results.checks.length - failed.length}/${results.checks.length} checks passed; boot ${results.notes.bootSeconds?.toFixed?.(1)} s; ` +
    `build ${results.notes.build}; stick moved ${Number(results.notes.stickMovedMetres).toFixed(1)} m; pixels ${JSON.stringify(results.notes.pixels)}`,
);
if (failed.length) exitCode = 1;
process.exit(exitCode);
