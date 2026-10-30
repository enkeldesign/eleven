// Smoke test for the Unity Web player in headless Chromium, run by CI after the GameCI build.
//
//   node smoke.mjs <player dir with index.html> <dir with .NET hash logs> <output dir>
//
// Two passes over the same build:
//   desktop - 1280x800 window, the mouse as one finger
//   phone   - iPhone-sized portrait viewport with touch events (the template's mobile path)
// Each pass boots the player, reads the host's log lines from the browser console and drives the
// on-screen controls through Unity's Input System:
//   - boot: the host reports ready, with its build id and the page's load time
//   - autorun (desktop): the issue 1 scenario's digests in WebGL, compared with the .NET hash logs
//   - Maths and Run scenario: each tap is acknowledged, and the result panel fits its text
//   - stick: dragging it moves the dot (from the host's trace lines), and the camera keeps the
//     dot and ball inside the area the HUD leaves free
//   - pacing: the trace lines carry the dropped-frame counter
//   - recording: the host's command recording replays to the live state
//   - rendering: the screenshot contains the pitch, line, dot and ball colours
//   - icons (desktop): the home-screen icon and every manifest icon are ELEVEN's and load
// A third pass checks the service worker: a second deploy is served on reload, and with the
// server stopped the player still boots from the cache.
// Results go to stdout as GitHub annotations and to <output dir> with screenshots and console logs.
// A digest mismatch is a warning, not a failure: cross-runtime determinism is #5's question.

import { chromium, devices } from 'playwright';
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

const layoutPattern = /ELEVEN layout screen=(\d+)x(\d+) run=(\d+),(\d+) maths=(\d+),(\d+) reset=(\d+),(\d+) kick=(\d+),(\d+) stick=(\d+),(\d+) stickRadius=(\d+)/;
const tracePattern = /ELEVEN trace tick=(\d+) dot=(-?[\d.]+),(-?[\d.]+)/;

const results = { checks: [], notes: {} };
const annotate = (level, title, message) =>
  console.log(`::${level} title=${title}::${String(message).replace(/%/g, '%25').replace(/\r?\n/g, '%0A')}`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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

async function waitFor(logs, pattern, timeoutMs, from = 0) {
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

// deploy.number above 1 stands for a later deploy of the same player: index.html carries a
// marker with the number and ServiceWorker.js changes, as it does between real builds.
function serve(root, deploy = { number: 1 }) {
  const types = {
    '.html': 'text/html',
    '.js': 'application/javascript',
    '.wasm': 'application/wasm',
    '.css': 'text/css',
    '.json': 'application/json',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
    '.webmanifest': 'application/manifest+json',
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
    const name = path.basename(file);
    if (deploy.number > 1 && (name === 'index.html' || name === 'ServiceWorker.js')) {
      const text = fs.readFileSync(file, 'utf8');
      res.end(name === 'index.html'
        ? text.replace('<head>', `<head><meta name="eleven-deploy" content="${deploy.number}">`)
        : `${text}\n// deploy ${deploy.number}\n`);
      return;
    }
    fs.createReadStream(file).pipe(res);
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)));
}

async function runPass(browser, url, name, contextOptions, useTouch) {
  const logs = [];
  const check = (what, ok, detail) => {
    results.checks.push({ name: `${name}: ${what}`, ok, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'} ${name}: ${what}: ${detail}`);
  };
  const notes = (results.notes[name] = {});
  const waitForLog = (pattern, timeoutMs, from = 0) => waitFor(logs, pattern, timeoutMs, from);
  const lastLog = (pattern) => {
    for (let i = logs.length - 1; i >= 0; i--) {
      const m = pattern.exec(logs[i]);
      if (m) return m;
    }
    return null;
  };

  const context = await browser.newContext(contextOptions);
  const page = await context.newPage();
  page.on('console', (m) => logs.push(m.text()));
  page.on('pageerror', (e) => logs.push(`pageerror ${e.message}`));
  const cdp = useTouch ? await context.newCDPSession(page) : null;

  try {
    // Boot.
    const started = Date.now();
    await page.goto(url);
    const ready = await waitForLog(/ELEVEN host ready build=(\S+) maths=(\w+)/, 240_000);
    notes.bootSeconds = (Date.now() - started) / 1000;
    check('boot', !!ready, ready ? `host ready in ${notes.bootSeconds.toFixed(1)} s, build ${ready.match[1]}` : 'no "ELEVEN host ready" within 240 s');
    if (!ready) throw new Error('player did not boot');
    notes.build = ready.match[1];
    // The page's own load time, which the build shows on screen for the device session (#5).
    const loadMs = Number(/loadMs=(\d+)/.exec(ready.match.input)?.[1]);
    notes.loadSeconds = loadMs / 1000;
    check('reports its load time', loadMs > 0 && loadMs < 240_000, Number.isFinite(loadMs) ? `${loadMs} ms from navigation start` : 'no loadMs on the ready line');

    if (name === 'desktop') {
      const icons = await page.evaluate(async () => {
        const urls = [document.querySelector('link[rel="apple-touch-icon"]')?.href];
        const manifest = document.querySelector('link[rel="manifest"]')?.href;
        const list = manifest ? (await (await fetch(manifest)).json()).icons ?? [] : [];
        for (const icon of list) urls.push(new URL(icon.src, manifest).href);
        const results = [];
        for (const url of urls) {
          const ok = !!url && /\/icons\/eleven/.test(url) && (await fetch(url)).ok;
          results.push(`${url ? url.split('/').pop() : 'missing'}:${ok ? 'ok' : 'FAILED'}`);
        }
        return results;
      });
      check('ELEVEN icons load', icons.length >= 4 && icons.every((i) => i.endsWith(':ok')), icons.join(' '));
    }

    // Autorun: the scenario inside the build, both backends, against .NET.
    if (url.includes('autorun')) {
      const expected = expectedDigests();
      for (const maths of ['SystemMath', 'Portable']) {
        const line = await waitForLog(new RegExp(`ELEVEN scenario name=\\S+ maths=${maths} digest=([0-9a-f]{16}) final=([0-9a-f]{16}) hashes=(\\d+) events=(\\d+) config=\\w+ ms=(\\d+)`), 60_000);
        check(`scenario ${maths} runs in the build`, !!line, line ? `${line.match[3]} hashes in ${line.match[5]} ms` : 'no scenario line');
        if (!line) continue;
        const digest = line.match[1];
        notes[`digest${maths}`] = digest;
        const same = expected[maths] === digest;
        annotate(same ? 'notice' : 'warning', 'Scenario digest vs .NET',
          `${maths}: WebGL (Chromium) ${digest}, .NET ${expected[maths] ?? 'missing'} — ${same ? 'identical' : 'DIFFERENT'}`);
      }
    }

    // Controls, positioned from the host's own layout line. The host logs a new line whenever
    // the screen size changes, and the canvas can still be resizing just after boot, so wait for
    // the size to match the canvas and use the latest line.
    const canvas = page.locator('#unity-canvas');
    let layout = null;
    let backing = null;
    for (let i = 0; i < 50; i++) {
      backing = await canvas.evaluate((c) => [c.width, c.height]);
      layout = lastLog(layoutPattern);
      if (layout && Number(layout[1]) === backing[0] && Number(layout[2]) === backing[1]) break;
      await sleep(200);
    }
    notes.layouts = logs.filter((l) => layoutPattern.test(l)).map((l) => l.replace('ELEVEN layout ', ''));
    check('layout matches the canvas', !!layout && Number(layout[1]) === backing[0] && Number(layout[2]) === backing[1],
      `canvas ${backing?.join('x')}; layouts logged: ${notes.layouts.join(' / ') || 'none'}`);
    if (!layout) throw new Error('no layout');
    const n = layout.slice(1).map(Number);
    const box = await canvas.boundingBox();
    const sx = box.width / n[0];
    const sy = box.height / n[1];
    const at = (x, y) => ({ x: box.x + x * sx, y: box.y + y * sy });
    const touch = (type, p) =>
      cdp.send('Input.dispatchTouchEvent', { type, touchPoints: p ? [{ x: p.x, y: p.y, id: 1 }] : [] });
    // Taps are deliberately short: a tap must count even if it starts and ends between frames.
    const tap = async (p) => {
      if (useTouch) {
        await touch('touchStart', p);
        await touch('touchEnd');
      } else {
        await page.mouse.move(p.x, p.y);
        await page.mouse.down();
        await page.mouse.up();
      }
      await sleep(150);
    };

    for (const maths of ['SystemMath', 'Portable']) {
      let from = logs.length;
      await tap(at(n[4], n[5]));
      const switched = await waitForLog(new RegExp(`ELEVEN maths=${maths} `), 8_000, from);
      check(`Maths button switches to ${maths}`, !!switched, switched ? switched.match[0].trim() : 'no acknowledgement');

      from = logs.length;
      await tap(at(n[2], n[3]));
      const ran = await waitForLog(new RegExp(`ELEVEN scenario name=\\S+ maths=${maths} digest=([0-9a-f]{16})`), 20_000, from);
      check(`Run scenario button with ${maths}`, !!ran, ran ? `digest ${ran.match[1]}` : 'no scenario line after tap');
      const fit = await waitForLog(/ELEVEN hud result fits=(\w+) .*/, 8_000, from);
      check(`result panel fits its text (${maths})`, !!fit && fit.match[1] === 'yes', fit ? fit.match[0].replace('ELEVEN hud result ', '') : 'no fit report');
    }

    // Stick: drag right from the stick's rest position and hold; the dot should move along +x.
    // Trace lines come once a simulated second, so wait for a fresh one on each side of the drag.
    const before = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
    const origin = at(n[10], n[11]);
    const reach = n[12] * sx;
    if (useTouch) await touch('touchStart', origin);
    else {
      await page.mouse.move(origin.x, origin.y);
      await page.mouse.down();
    }
    for (let i = 1; i <= 10; i++) {
      const p = { x: origin.x + (reach * i) / 10, y: origin.y };
      if (useTouch) await touch('touchMove', p);
      else await page.mouse.move(p.x, p.y);
      await sleep(20);
    }
    await sleep(2500);
    await page.screenshot({ path: path.join(outDir, `${name}-playing.png`) });
    if (useTouch) await touch('touchEnd');
    else await page.mouse.up();
    const after = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
    notes.stickMovedMetres = before && after ? Number(after[2]) - Number(before[2]) : NaN;
    const view = after?.input.match(/view=dot:(\w+),ball:(\w+)/);
    check('dot and ball stay clear of the HUD', !!view && view[1] === 'in' && view[2] === 'in',
      view ? view[0] : 'no view report in the trace line');
    const pacing = after?.input.match(/dropped=(\d+) frames=(\d+) interval=([\d.]+)/);
    notes.dropped = pacing ? `${pacing[1]}/${pacing[2]}` : 'none';
    check('counts dropped frames', !!pacing && Number(pacing[2]) > 0,
      pacing ? `${pacing[1]} dropped of ${pacing[2]} frames, interval ${pacing[3]} ms` : 'no pacing in the trace line');
    check('stick moves the dot', notes.stickMovedMetres > 3, before && after ? `dot x ${before[2]} → ${after[2]} (ticks ${before[1]} → ${after[1]})` : 'no trace lines');

    // Kick button: a tap must be taken without errors (whether it connects depends on position).
    const fromKick = logs.length;
    await tap(at(n[8], n[9]));
    await sleep(500);
    const kickErrors = logs.slice(fromKick).filter((l) => /error|exception/i.test(l));
    check('kick button tap', kickErrors.length === 0, kickErrors.length ? kickErrors.join(' | ') : 'no errors');

    // The live session's command recording replays headlessly in the build to the same state.
    const replay = await waitForLog(/ELEVEN recording from=(\d+) steps=(\d+) bytes=(\d+) replay=(\w+)/, 15_000, fromKick);
    check('recording replays to the live state', !!replay && replay.match[4] === 'match', replay ? replay.match[0].replace('ELEVEN ', '') : 'no recording check');

    // Rendering: count pixels close to each palette colour in a canvas screenshot.
    const png = await canvas.screenshot({ path: path.join(outDir, `${name}-canvas.png`) });
    notes.pixels = await page.evaluate(async ({ data, colours }) => {
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
    check('pitch, line, dot and ball are drawn', Object.values(notes.pixels).every((v) => v > 20), JSON.stringify(notes.pixels));

    const errors = logs.filter((l) => /^pageerror|Exception|error CS/i.test(l));
    check('no page errors or exceptions', errors.length === 0, errors.length ? errors.slice(0, 5).join(' | ') : 'none');
  } catch (e) {
    check('ran to completion', false, e.message);
  } finally {
    fs.writeFileSync(path.join(outDir, `${name}-console.log`), logs.join('\n'));
    if (results.checks.some((c) => c.name.startsWith(name) && !c.ok)) {
      // Context for diagnosis without the uploaded console log.
      const presses = logs.filter((l) => l.startsWith('ELEVEN press')).slice(-8);
      const traces = logs.filter((l) => l.startsWith('ELEVEN trace')).slice(-3);
      annotate('notice', `Smoke context (${name})`, `layouts: ${(notes.layouts || []).join(' / ')}\npresses: ${presses.join(' | ') || 'none'}\ntraces: ${traces.join(' | ')}`);
    }
    await context.close();
  }
}

// The service worker, with a server of its own that can stand in for a new deploy or go away.
async function runServiceWorkerPass(browser, root) {
  const name = 'service worker';
  const logs = [];
  const check = (what, ok, detail) => {
    results.checks.push({ name: `${name}: ${what}`, ok, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'} ${name}: ${what}: ${detail}`);
  };
  const deploy = { number: 1 };
  const server = await serve(root, deploy);
  const context = await browser.newContext({ viewport: { width: 960, height: 600 } });
  const page = await context.newPage();
  page.on('console', (m) => logs.push(m.text()));
  page.on('pageerror', (e) => logs.push(`pageerror ${e.message}`));
  const ready = /ELEVEN host ready build=(\S+)/;
  const load = async (go) => {
    const from = logs.length;
    await go();
    return waitFor(logs, ready, 120_000, from);
  };

  try {
    const first = await load(() => page.goto(`http://127.0.0.1:${server.address().port}/index.html?trace=1`));
    if (!first) throw new Error('player did not boot');
    // Once the worker is active, a second visit is under its control; then a new deploy.
    const active = await page.evaluate(() => Promise.race([
      navigator.serviceWorker.ready.then(() => true),
      new Promise((resolve) => setTimeout(() => resolve(false), 60_000)),
    ]));
    if (!active) throw new Error('no active service worker within 60 s');
    await load(() => page.reload());
    const controlled = await page.evaluate(() => !!navigator.serviceWorker.controller);
    check('controls the page', controlled, controlled ? 'second visit controlled by the service worker' : 'no controller on the second visit');
    deploy.number = 2;
    const after = await load(() => page.reload());
    const marker = await page.evaluate(() => document.querySelector('meta[name="eleven-deploy"]')?.content ?? 'none');
    check('serves a new deploy on reload', !!after && marker === '2', `deploy marker after reload: ${marker}`);

    server.closeAllConnections();
    await new Promise((resolve) => server.close(resolve));
    const offline = await load(() => page.reload());
    check('boots offline from its cache', !!offline, offline ? `host ready, build ${offline.match[1]}` : 'no "ELEVEN host ready" with the server stopped');
  } catch (e) {
    check('ran to completion', false, e.message);
  } finally {
    fs.writeFileSync(path.join(outDir, 'service-worker-console.log'), logs.join('\n'));
    await context.close();
    if (server.listening) server.close();
  }
}

const server = await serve(path.resolve(playerDir));
const base = `http://127.0.0.1:${server.address().port}/index.html`;
const browser = await chromium.launch({
  args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--ignore-gpu-blocklist'],
});
try {
  await runPass(browser, `${base}?trace=1&autorun=1`, 'desktop', { viewport: { width: 1280, height: 800 } }, false);
  const { defaultBrowserType, ...phone } = devices['iPhone 13'];
  await runPass(browser, `${base}?trace=1`, 'phone', phone, true);
  await runServiceWorkerPass(browser, path.resolve(playerDir));
} finally {
  await browser.close();
  server.close();
}

fs.writeFileSync(path.join(outDir, 'smoke.json'), JSON.stringify(results, null, 2));
const failed = results.checks.filter((c) => !c.ok);
for (const c of failed) annotate('error', `Smoke: ${c.name}`, c.detail);
const summary = Object.entries(results.notes)
  .map(([pass, n]) => `${pass}: boot ${n.bootSeconds?.toFixed?.(1)} s, load ${n.loadSeconds?.toFixed?.(1)} s, dropped ${n.dropped}, stick ${Number(n.stickMovedMetres).toFixed(1)} m, layout ${(n.layouts || []).slice(-1)[0]?.split(' ')[0]}, pixels ${JSON.stringify(n.pixels)}`)
  .join('; ');
annotate('notice', 'WebGL smoke test', `${results.checks.length - failed.length}/${results.checks.length} checks passed; build ${results.notes.desktop?.build}; ${summary}`);
process.exit(failed.length ? 1 : 0);
