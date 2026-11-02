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
//   - autorun (desktop): the issue 1 scenario's digest in WebGL must equal the .NET hash log's
//     (ADR-0002: cross-runtime determinism is a gate)
//   - the HUD (#48, #53): placed actions hold their labels, the build line fits its panel and keeps
//     its size as its values change, and no page controls remain over the canvas
//   - Run scenario: the tap is acknowledged, and the result panel fits its text
//   - stick: dragging it moves the player (from the host's trace lines), and the camera keeps the
//     player and ball inside the area the HUD leaves free
//   - pacing: the trace lines carry the dropped-frame counter
//   - recording: the host's command recording replays to the live state
//   - THIS FELT WRONG (#44, #53): a Unity action of at least 44 x 44 pt, placed from the HUD's
//     layout. A press takes the capture of the last 10 s there and then, and opens a Unity sheet for
//     an optional note, while play waits; the capture is kept in localStorage, announced, and SAVE
//     CAPTURE (a Unity action too) offers it. Desktop types the note and saves; the keyboard reaches
//     THIS FELT WRONG with a visible focus ring. The capture as kept in localStorage is written to
//     <output dir>/capture-<pass>.elfc, and on desktop SAVE CAPTURE's download to
//     capture-<pass>-saved.elfc (byte-identical), for CI to replay with .NET
//   - rendering: the screenshot contains the pitch, line, player and ball colours
//   - icons (desktop): the home-screen icon and every manifest icon are ELEVEN's and load
// A third pass checks the service worker: a second deploy is served on reload, and with the
// server stopped the player still boots from the cache.
// Results go to stdout as GitHub annotations and to <output dir> with screenshots and console logs.

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
  player: [0x99, 0xcc, 0x44],
  ball: [0xf7, 0xe2, 0x6b],
};

const layoutPattern = /ELEVEN layout screen=(\d+)x(\d+) run=(\d+),(\d+) reset=(\d+),(\d+) kick=(\d+),(\d+) stick=(\d+),(\d+) stickRadius=(\d+)/;
// Where the capture actions are (#53), from the same layout line: x, y, width and height in pixels.
const capturePattern = /feltWrong=(-?\d+),(-?\d+),(\d+),(\d+) save=(-?\d+),(-?\d+),(\d+),(\d+)/;
const keptPattern = /ELEVEN capture ticks=(\d+)\.\.(\d+) steps=(\d+) bytes=(\d+) name=(\S+) note=(\d+)/;
const tracePattern = /ELEVEN trace tick=(\d+) player=(-?[\d.]+),(-?[\d.]+)/;

const results = { checks: [], notes: {} };
const annotate = (level, title, message) =>
  console.log(`::${level} title=${title}::${String(message).replace(/%/g, '%25').replace(/\r?\n/g, '%0A')}`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// The scenario's digest from the .NET hash log (one log per scenario).
function expectedDigest() {
  const digests = fs.readdirSync(hashlogDir)
    .map((file) => /^# digest: ([0-9a-f]{16})$/m.exec(fs.readFileSync(path.join(hashlogDir, file), 'utf8'))?.[1])
    .filter(Boolean);
  return digests.length === 1 ? digests[0] : null;
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

// Median frame rate over the trace lines among these logs.
function medianFps(lines) {
  const fps = lines.map((l) => /ELEVEN trace .* fps=([\d.]+)/.exec(l)?.[1]).filter(Boolean).map(Number).sort((a, b) => a - b);
  return fps.length ? fps[Math.floor(fps.length / 2)].toFixed(1) : 'none';
}

// The visual system's specimen (#48): built from the production components, it must keep every
// interactive target at 44 x 44 pt or more and nothing wider than the page, and it is screenshotted
// whole (tall viewports, so no scrolling) for review.
async function runSpecimenPass(browser, url, name, contextOptions) {
  const logs = [];
  const check = (what, ok, detail) => {
    results.checks.push({ name: `${name}: ${what}`, ok, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'} ${name}: ${what}: ${detail}`);
  };
  const context = await browser.newContext(contextOptions);
  const page = await context.newPage();
  page.on('console', (m) => logs.push(m.text()));
  page.on('pageerror', (e) => logs.push(`pageerror ${e.message}`));
  try {
    await page.goto(url);
    const report = await waitFor(logs, /ELEVEN specimen targets=(\d+) smallest=([\d.]+) smallestName=(\S+) pageWidth=(\d+) overflow=(\S+) reducedMotion=(\w+)/, 240_000);
    check('specimen lays out', !!report, report ? report.match[0].replace('ELEVEN specimen ', '') : 'no specimen report');
    if (report) {
      const m = report.match;
      check('every interactive target is at least 44 x 44 pt', Number(m[1]) > 0 && Number(m[2]) >= 43.9, `${m[1]} targets, smallest ${m[2]} pt (${m[3]})`);
      check('nothing is wider than the page', m[5] === 'none', `page ${m[4]} pt wide, overflow: ${m[5]}`);
      const wantsReduced = contextOptions.reducedMotion === 'reduce';
      check('follows the reduced-motion preference', m[6] === (wantsReduced ? 'yes' : 'no'), `preference ${wantsReduced ? 'reduce' : 'none'}, UI reducedMotion=${m[6]}`);
    }
    await sleep(500);
    // The viewport, not the canvas element: no wait for the element to hold still.
    await page.screenshot({ path: path.join(outDir, `specimen-${name}.png`), timeout: 60_000 });
    const errors = logs.filter((l) => /^pageerror|Exception|error CS/i.test(l));
    check('no page errors or exceptions', errors.length === 0, errors.length ? errors.slice(0, 5).join(' | ') : 'none');
  } catch (e) {
    check('ran to completion', false, e.message);
  } finally {
    fs.writeFileSync(path.join(outDir, `specimen-${name}-console.log`), logs.join('\n'));
    await context.close();
  }
}

async function runPass(browser, url, name, contextOptions, useTouch) {
  const logs = [];
  const check = (what, ok, detail) => {
    results.checks.push({ name: `${name}: ${what}`, ok, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'} ${name}: ${what}: ${detail}`);
  };
  const notes = (results.notes[name] = {});
  const waitForLog = (pattern, timeoutMs, from = 0) => waitFor(logs, pattern, timeoutMs, from);
  const keptBytes = async (captureName) => {
    const data = await page.evaluate((n) => (JSON.parse(localStorage.getItem('eleven.captures') || '[]').find((c) => c.name === n) || {}).data || null, captureName).catch(() => null);
    return data ? Buffer.from(data, 'base64') : null;
  };
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
    const ready = await waitForLog(/ELEVEN host ready build=(\S+)/, 240_000);
    notes.bootSeconds = (Date.now() - started) / 1000;
    check('boot', !!ready, ready ? `host ready in ${notes.bootSeconds.toFixed(1)} s, build ${ready.match[1]}` : 'no "ELEVEN host ready" within 240 s');
    if (!ready) throw new Error('player did not boot');
    notes.build = ready.match[1];

    // Placed HUD actions hold their labels on one line, also with enlarged text (#48). The HUD may
    // lay itself out again once it has measured its labels, so the latest report counts.
    if (!url.includes('noui') && !url.includes('ui=')) {
      await waitForLog(/ELEVEN ui fit name=Run_scenario /, 5_000);
      await page.waitForTimeout(1_500);
      // Unity's console lines end in a newline: trim it, or the verdict at the end never matches.
      const fits = logs.filter((l) => l.startsWith('ELEVEN ui fit')).map((l) => l.trimEnd());
      const latest = new Map(fits.map((l) => [/name=(\S+)/.exec(l)[1], l]));
      const misfits = [...latest.values()].filter((l) => !l.endsWith(' ok'));
      check('placed actions fit their labels', latest.size >= 4 && misfits.length === 0,
        latest.size ? [...latest.values()].map((l) => l.replace('ELEVEN ui fit ', '')).join(' | ') : 'no fit report');
      // The build line is HUD data in a panel (#53): its values fit their slots, also enlarged.
      const build = lastLog(/ELEVEN hud build fits=(\w+) .*/);
      check('the build line fits its panel', !!build && build[1] === 'yes', build ? build[0].trim().replace('ELEVEN hud build ', '') : 'no build report');
    }

    // Idle frame rate over six seconds right after boot, before any input: with ?noui the same
    // window measures the frame without the UI, so the UI's frame cost shows as the difference (#48).
    const idleFrom = logs.length;
    await sleep(6_000);
    notes.idleFps = medianFps(logs.slice(idleFrom));
    if (url.includes('idle=1')) {
      notes.idleOnly = true;
      return;
    }
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

    // Autorun: the scenario inside the build against .NET. A different digest fails the run.
    if (url.includes('autorun')) {
      const expected = expectedDigest();
      const line = await waitForLog(/ELEVEN scenario name=\S+ digest=([0-9a-f]{16}) final=([0-9a-f]{16}) hashes=(\d+) events=(\d+) config=\w+ ms=(\d+)/, 60_000);
      check('scenario runs in the build', !!line, line ? `${line.match[3]} hashes in ${line.match[5]} ms` : 'no scenario line');
      if (line) {
        notes.digest = line.match[1];
        check('scenario digest equals .NET\'s', expected !== null && notes.digest === expected,
          `WebGL (Chromium) ${notes.digest}, .NET ${expected ?? 'missing or ambiguous'}`);
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

    // The UI around the match is UI Toolkit, built from code (#48): its text must render.
    const ui = lastLog(/ELEVEN ui ready font=(\S+) pixelsPerPoint=([\d.]+) ms=([\d.]+)/);
    const text = lastLog(/ELEVEN ui text "([^"]+)" width=([\d.]+) height=([\d.]+)/);
    check('the UI Toolkit panel lays out text', !!ui && !!text && Number(text[2]) > 0,
      ui ? `font ${ui[1]}, ${ui[2]} px per pt, built in ${ui[3]} ms; ${text ? `"${text[1]}" ${text[2]} x ${text[3]} pt` : 'no text layout line'}` : 'no "ELEVEN ui ready" line');
    if (ui) notes.ui = `${ui[1]}, ${ui[2]} px/pt, ${ui[3]} ms`;

    const fromRun = logs.length;
    await tap(at(n[2], n[3]));
    const ran = await waitForLog(/ELEVEN scenario name=\S+ digest=([0-9a-f]{16})/, 20_000, fromRun);
    check('Run scenario button', !!ran, ran ? `digest ${ran.match[1]}` : 'no scenario line after tap');
    const runPress = logs.slice(fromRun).find((l) => l.startsWith('ELEVEN press'));
    check('a press on the UI belongs to the UI, not to play', !!runPress && /target=ui/.test(runPress), runPress ?? 'no press logged');
    const fit = await waitForLog(/ELEVEN hud result fits=(\w+) .*/, 8_000, fromRun);
    check('result panel fits its text', !!fit && fit.match[1] === 'yes', fit ? fit.match[0].replace('ELEVEN hud result ', '') : 'no fit report');

    // Stick: drag right from the stick's rest position and hold; the player should move along +x.
    // Trace lines come once a simulated second, so wait for a fresh one on each side of the drag.
    const before = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
    const origin = at(n[8], n[9]);
    const reach = n[10] * sx;
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
    const view = after?.input.match(/view=player:(\w+),ball:(\w+)/);
    check('player and ball stay clear of the HUD', !!view && view[1] === 'in' && view[2] === 'in',
      view ? view[0] : 'no view report in the trace line');
    const pacing = after?.input.match(/dropped=(\d+) frames=(\d+) interval=([\d.]+)/);
    notes.dropped = pacing ? `${pacing[1]}/${pacing[2]}` : 'none';
    check('counts dropped frames', !!pacing && Number(pacing[2]) > 0,
      pacing ? `${pacing[1]} dropped of ${pacing[2]} frames, interval ${pacing[3]} ms` : 'no pacing in the trace line');
    check('stick moves the player', notes.stickMovedMetres > 3, before && after ? `player x ${before[2]} → ${after[2]} (ticks ${before[1]} → ${after[1]})` : 'no trace lines');

    // Kick button: a tap must be taken without errors (whether it connects depends on position).
    const fromKick = logs.length;
    await tap(at(n[6], n[7]));
    await sleep(500);
    const kickErrors = logs.slice(fromKick).filter((l) => /error|exception/i.test(l));
    check('kick button tap', kickErrors.length === 0, kickErrors.length ? kickErrors.join(' | ') : 'no errors');

    // The live session's command recording replays headlessly in the build to the same state.
    const replay = await waitForLog(/ELEVEN recording from=(\d+) steps=(\d+) bytes=(\d+) replay=(\w+)/, 15_000, fromKick);
    check('recording replays to the live state', !!replay && replay.match[4] === 'match', replay ? replay.match[0].replace('ELEVEN ', '') : 'no recording check');

    // THIS FELT WRONG (#44) as a Unity action (#53): placed from the HUD's layout, at least 44 x 44
    // pt, holding its label. No page controls are left over the canvas.
    const pageControls = await page.evaluate(() => document.querySelectorAll('body > button, [role=status]').length);
    check('no page controls over the canvas', pageControls === 0, `${pageControls} page button(s) or status element(s)`);
    const placed = capturePattern.exec(lastLog(layoutPattern)?.input ?? '');
    const centre = (x, y, w, h) => at(Number(x) + Number(w) / 2, Number(y) + Number(h) / 2);
    const feltFit = lastLog(/ELEVEN ui fit name=Felt_wrong .* size=([\d.]+)x([\d.]+) (\w+)/);
    check('felt-wrong is a Unity action of at least 44 x 44 pt that holds its label',
      !!placed && !!feltFit && Number(feltFit[1]) >= 44 && Number(feltFit[2]) >= 44 && feltFit[3] === 'ok',
      `${placed ? `placed at ${placed.slice(1, 5).join(',')} px` : 'not in the layout line'}; ${feltFit ? feltFit.input.trim().replace('ELEVEN ui fit ', '') : 'no fit report'}`);

    // A press takes the capture there and then: the window ends where the player pressed. The note
    // sheet opens over the match, and play waits until it closes.
    const fromCapture = logs.length;
    if (placed) await tap(centre(...placed.slice(1, 5)));
    const pressed = await waitForLog(/ELEVEN capture pressed tick=(\d+)/, 10_000, fromCapture);
    const sheet = await waitForLog(/ELEVEN ui sheet open keep=(\d+),(\d+) skip=(\d+),(\d+)/, 5_000, fromCapture);
    check('a press opens the note sheet', !!pressed && !!sheet, sheet ? sheet.match[0].replace('ELEVEN ', '') : pressed ? 'no sheet' : 'no press');
    await sleep(1_500);
    const during = logs.slice(fromCapture).map((l) => tracePattern.exec(l)).filter(Boolean).map((m) => Number(m[1]));
    check('play waits while the note sheet is open', !!pressed && during.every((t) => t <= Number(pressed.match[1])),
      pressed ? `pressed at tick ${pressed.match[1]}; trace ticks since: ${during.join(', ') || 'none'}` : 'no press');

    // Desktop types the note and keeps it with Enter; the phone keeps the capture without one.
    const note = `smoke ${name}`;
    if (name === 'desktop') {
      await page.keyboard.type(note);
      await page.keyboard.press('Enter');
    } else if (sheet) {
      await tap(at(Number(sheet.match[3]), Number(sheet.match[4])));
    }
    const captured = await waitForLog(keptPattern, 10_000, fromCapture);
    check('the capture ends where the player pressed', !!captured && !!pressed && captured.match[2] === pressed.match[1],
      captured ? `ticks ${captured.match[1]}..${captured.match[2]}, pressed at ${pressed?.match[1]}` : 'no capture line');
    // The capture as a player keeps it (#50): its bytes in localStorage, which SAVE CAPTURE exports.
    const keptCapture = captured ? await keptBytes(captured.match[5]) : null;
    check('felt-wrong captures the last seconds and keeps them whole',
      !!captured && Number(captured.match[3]) > 0 && !!keptCapture && keptCapture.length === Number(captured.match[4]),
      captured ? `ticks ${captured.match[1]}..${captured.match[2]}, ${captured.match[4]} bytes captured, ${keptCapture ? keptCapture.length : 'none'} kept, ${captured.match[5]}` : 'no capture line');
    if (keptCapture) {
      fs.writeFileSync(path.join(outDir, `capture-${name}.elfc`), keptCapture);
      notes.capture = `${captured.match[3]} steps, ${keptCapture.length} bytes`;
    }
    if (name === 'desktop') {
      check('the note typed in the sheet is in the capture', !!keptCapture && keptCapture.includes(Buffer.from(note, 'utf8')),
        captured ? `note of ${captured.match[6]} characters; "${note}" ${keptCapture?.includes(Buffer.from(note, 'utf8')) ? 'found' : 'NOT found'} in the kept bytes` : 'no capture');
    }

    // Capturing doesn't stop play once the sheet closes: the simulation runs on past the capture.
    // CI's software renderer runs the phone pass near 5 fps, where frames over 250 ms are dropped
    // rather than caught up, so give play time to cover a trace interval.
    const afterCapture = (await waitForLog(tracePattern, 20_000, logs.length))?.match ?? null;
    check('play goes on after a capture', !!captured && !!afterCapture && Number(afterCapture[1]) > Number(captured.match[2]),
      afterCapture ? `trace tick ${afterCapture[1]} after capture end ${captured?.match[2]}` : 'no trace line after the capture');
    const kept = await page.evaluate(() => JSON.parse(localStorage.getItem('eleven.captures') || '[]').length).catch(() => 0);
    check('the capture is kept on the device', kept >= 1, `${kept} capture(s) in localStorage`);
    const announced = await waitForLog(/ELEVEN ui status (.*)/, 3_000, fromCapture);
    check('the capture is announced', !!announced && /Captured/.test(announced.match[1]), announced ? announced.match[1] : 'no status line');
    const offered = await waitForLog(/ELEVEN ui save visible=yes/, 3_000);
    if (name === 'desktop') {
      const [download] = await Promise.all([
        page.waitForEvent('download', { timeout: 5_000 }).catch(() => null),
        placed ? tap(centre(...placed.slice(5, 9))) : Promise.resolve(),
      ]);
      // The file a player would attach: saved for CI to replay, and identical to the kept capture.
      let same = false;
      let detail = offered ? 'no download' : 'SAVE CAPTURE not shown';
      if (download) {
        const file = path.join(outDir, `capture-${name}-saved.elfc`);
        await download.saveAs(file);
        const saved = fs.readFileSync(file);
        same = !!keptCapture && saved.equals(keptCapture);
        detail = `${download.suggestedFilename()}, ${saved.length} bytes, ${same ? 'identical to the kept capture' : 'DIFFERS from the kept capture'}`;
      }
      check('SAVE CAPTURE downloads the kept capture, byte for byte', !!download && /\.elfc$/.test(download.suggestedFilename()) && same, detail);
    } else {
      check('SAVE CAPTURE is offered', !!offered, offered ? 'shown' : 'not shown');
    }

    // The keyboard reaches the Unity controls (#53): the player captures keys again, Tab moves focus
    // to THIS FELT WRONG with the two-colour focus ring, Enter presses it, and Escape keeps the
    // capture without a note.
    if (name === 'desktop') {
      let focus = null;
      for (let i = 0; i < 10 && !focus; i++) {
        const fromTab = logs.length;
        await page.keyboard.press('Tab');
        const moved = await waitForLog(/ELEVEN ui focus name=(\S+) ring=(\w+)/, 1_000, fromTab);
        if (moved && moved.match[1] === 'Felt_wrong') focus = moved.match;
      }
      check('Tab reaches felt-wrong, with a visible focus ring', !!focus && focus[2] === 'shown',
        focus ? focus[0].replace('ELEVEN ui ', '') : `focus never reached Felt_wrong: ${logs.filter((l) => l.startsWith('ELEVEN ui focus')).slice(-4).join(' | ') || 'no focus lines'}`);
      const fromKey = logs.length;
      if (focus) await page.keyboard.press('Enter');
      const keyedSheet = await waitForLog(/ELEVEN ui sheet open/, 5_000, fromKey);
      if (keyedSheet) await page.keyboard.press('Escape');
      const keyed = await waitForLog(keptPattern, 5_000, fromKey);
      check('Enter on felt-wrong captures, and Escape keeps it without a note', !!keyedSheet && !!keyed && keyed.match[6] === '0',
        keyed ? keyed.match[0].replace('ELEVEN ', '') : keyedSheet ? 'sheet opened, no capture after Escape' : 'no sheet after Enter');
    }

    // Tabular HUD data doesn't jitter (#53): the build panel keeps its size while the dropped-frame
    // count changes through play.
    const huds = logs.map((l) => /ELEVEN trace .* dropped=(\d+) .* hud=(\d+x\d+)/.exec(l)).filter(Boolean);
    const sizes = [...new Set(huds.slice(1).map((m) => m[2]))];
    const droppedSeen = [...new Set(huds.map((m) => m[1]))];
    check('the build line keeps its size as its values change', huds.length >= 3 && sizes.length === 1,
      huds.length ? `panel ${sizes.join(' / ')} pt over ${huds.length} trace lines, dropped ${droppedSeen[0]}..${droppedSeen[droppedSeen.length - 1]}` : 'no hud size in the trace lines');

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
    check('pitch, line, player and ball are drawn', Object.values(notes.pixels).every((v) => v > 20), JSON.stringify(notes.pixels));

    // The Run scenario action is UI Toolkit (#48): around its centre, its dark neutral fill and its
    // white label must be on screen, so text really renders in the WebGL build, not only measures.
    // Buttons are dark with white text (Erik, 2026-11-02).
    const label = await page.evaluate(async ({ data, cx, cy, ui }) => {
      const image = new Image();
      image.src = `data:image/png;base64,${data}`;
      await image.decode();
      const c = document.createElement('canvas');
      c.width = image.width;
      c.height = image.height;
      const g = c.getContext('2d');
      g.drawImage(image, 0, 0);
      const half = { x: Math.round(24 * ui), y: Math.round(5 * ui) };
      const px = g.getImageData(cx - half.x, cy - half.y, half.x * 2, half.y * 2).data;
      const near = (i, [r, gg, b]) => Math.abs(px[i] - r) + Math.abs(px[i + 1] - gg) + Math.abs(px[i + 2] - b) < 40;
      const out = { fill: 0, text: 0, total: px.length / 4 };
      for (let i = 0; i < px.length; i += 4) {
        if (near(i, [0x3a, 0x42, 0x50])) out.fill++;
        if (near(i, [0xfc, 0xfd, 0xf8])) out.text++;
      }
      return out;
    }, { data: png.toString('base64'), cx: n[2], cy: n[3], ui: Number(ui?.[2] ?? 1) });
    // White labels cover more of the sample than ink ones did: a tenth of fill is plenty.
    check('the UI action shows its fill and its label', label.fill > label.total * 0.1 && label.text > label.total * 0.03,
      `around Run scenario's centre: ${label.fill} slate fill, ${label.text} white text of ${label.total} pixels`);

    const errors = logs.filter((l) => /^pageerror|Exception|error CS/i.test(l));
    check('no page errors or exceptions', errors.length === 0, errors.length ? errors.slice(0, 5).join(' | ') : 'none');

    // A kept capture stays exportable after the page reloads: "kept on this device" has to mean it.
    if (name === 'desktop') {
      const newest = await page.evaluate(() => JSON.parse(localStorage.getItem('eleven.captures') || '[]').pop()?.name ?? '');
      const fromReload = logs.length;
      await page.reload();
      const again = await waitForLog(/ELEVEN host ready build=(\S+)/, 240_000, fromReload);
      check('reload boots', !!again, again ? 'host ready after reload' : 'no "ELEVEN host ready" after reload');
      // SAVE CAPTURE is a Unity action (#53): offered once the host finds the kept capture. As after
      // the first boot, the canvas can still be resizing, so wait for the layout to match it and for
      // SAVE CAPTURE's own fit report, then press it where the latest layout line puts it.
      const offered = !!(await waitForLog(/ELEVEN ui save visible=yes/, 10_000, fromReload));
      let relaid = null;
      for (let i = 0; i < 50; i++) {
        const size = await canvas.evaluate((c) => [c.width, c.height]);
        relaid = logs.slice(fromReload).filter((l) => layoutPattern.test(l)).pop() ?? null;
        const m = relaid ? layoutPattern.exec(relaid) : null;
        if (m && Number(m[1]) === size[0] && Number(m[2]) === size[1]) break;
        await sleep(200);
      }
      await waitForLog(/ELEVEN ui fit name=Save_capture /, 5_000, fromReload);
      await sleep(1_500);
      relaid = logs.slice(fromReload).filter((l) => layoutPattern.test(l)).pop() ?? null;
      const placedAgain = capturePattern.exec(relaid ?? '');
      const boxAgain = await canvas.boundingBox();
      const atAgain = (x, y) => ({ x: boxAgain.x + x * (boxAgain.width / n[0]), y: boxAgain.y + y * (boxAgain.height / n[1]) });
      const fromSave = logs.length;
      const [download] = offered && placedAgain
        ? await Promise.all([
          page.waitForEvent('download', { timeout: 5_000 }).catch(() => null),
          tap(atAgain(Number(placedAgain[5]) + Number(placedAgain[7]) / 2, Number(placedAgain[6]) + Number(placedAgain[8]) / 2)),
        ])
        : [null];
      const saves = logs.slice(fromReload).filter((l) => l.startsWith('ELEVEN page save')).map((l) => l.replace('ELEVEN page save ', ''));
      const downs = logs.slice(fromSave).filter((l) => l.startsWith('ELEVEN ui pointer down')).map((l) => l.replace('ELEVEN ui pointer down ', ''));
      check('after a reload SAVE CAPTURE still exports the newest kept capture', offered && !!download && download.suggestedFilename() === newest,
        `${!offered ? 'SAVE CAPTURE not offered after reload' : download ? `${download.suggestedFilename()} (newest kept: ${newest})` : 'no download'}; page: ${saves.join(' | ') || 'no save lines'}; ui: ${downs.join(' | ') || 'no pointer down'}`);
    }
  } catch (e) {
    check('ran to completion', false, e.message);
  } finally {
    fs.writeFileSync(path.join(outDir, `${name}-console.log`), logs.join('\n'));
    notes.fps = medianFps(logs);
    if (results.checks.some((c) => c.name.startsWith(name) && !c.ok)) {
      // Context for diagnosis without the uploaded console log.
      const presses = logs.filter((l) => l.startsWith('ELEVEN press')).slice(-8);
      const traces = logs.filter((l) => l.startsWith('ELEVEN trace')).slice(-3);
      annotate('notice', `Smoke context (${name})`, `layouts: ${(notes.layouts || []).join(' / ')}\npresses: ${presses.join(' | ') || 'none'}\ntraces: ${traces.join(' | ')}`);
      // The most repeated other console lines (a per-frame warning shows here), and every UI line.
      const counts = new Map();
      for (const l of logs) {
        if (/^ELEVEN (trace|press|event)/.test(l)) continue;
        const key = l.slice(0, 220);
        counts.set(key, (counts.get(key) || 0) + 1);
      }
      const repeated = [...counts.entries()].sort((a, b) => b[1] - a[1]).slice(0, 6).map(([l, c]) => `${c}x ${l}`);
      const uiLines = logs.filter((l) => /ELEVEN ui|UIElements|UI Toolkit|PanelSettings|Theme Style|TextSettings|font/i.test(l)).slice(0, 8).map((l) => l.slice(0, 300));
      annotate('notice', `Smoke console (${name})`, `${logs.length} lines; most repeated:\n${repeated.join('\n')}\nUI lines:\n${uiLines.join('\n') || 'none'}`);
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
  // The UI's frame cost (#48): idle passes without the UI before and after each full pass, so
  // neither side gets the cold or the warm browser.
  const desktop = { viewport: { width: 1280, height: 800 } };
  const { defaultBrowserType, ...phone } = devices['iPhone 13'];
  // Idle-only passes for the frame cost: no UI, and an empty UI Toolkit panel.
  const idle = (mode) => `${base}?trace=1&idle=1&${mode}`;
  await runPass(browser, idle('noui=1'), 'desktop-noui-before', desktop, false);
  await runPass(browser, idle('ui=empty'), 'desktop-panel-empty', desktop, false);
  await runPass(browser, `${base}?trace=1&autorun=1`, 'desktop', desktop, false);
  await runPass(browser, idle('noui=1'), 'desktop-noui-after', desktop, false);
  await runPass(browser, idle('noui=1'), 'phone-noui-before', phone, true);
  await runPass(browser, idle('ui=empty'), 'phone-panel-empty', phone, true);
  await runPass(browser, `${base}?trace=1`, 'phone', phone, true);
  await runPass(browser, idle('noui=1'), 'phone-noui-after', phone, true);
  // The HUD on a 320 pt phone with text at 200 % (WCAG 1.4.4): placed actions must grow with their labels.
  await runPass(browser, `${base}?trace=1&idle=1&textscale=2`, 'narrow-large-text-hud', { ...phone, viewport: { width: 320, height: 640 } }, true);

  // The specimen: phone and tablet, both mappings, and a 320 pt phone with text at 150 %.
  const tall = (o, height) => ({ ...o, viewport: { ...o.viewport, height } });
  const { defaultBrowserType: _ipad, ...tablet } = devices['iPad (gen 7)'];
  await runSpecimenPass(browser, `${base}?specimen=ordinary`, 'phone-ordinary', tall(phone, 2400));
  await runSpecimenPass(browser, `${base}?specimen=dark`, 'phone-dark', tall(phone, 2400));
  await runSpecimenPass(browser, `${base}?specimen=ordinary`, 'tablet-ordinary', tall(tablet, 1800));
  await runSpecimenPass(browser, `${base}?specimen=dark`, 'tablet-dark', { ...tall(tablet, 1800), reducedMotion: 'reduce' });
  await runSpecimenPass(browser, `${base}?specimen=ordinary&textscale=2`, 'narrow-large-text', { ...tall(phone, 3600), viewport: { width: 320, height: 3600 } });
  await runServiceWorkerPass(browser, path.resolve(playerDir));
} finally {
  await browser.close();
  server.close();
}

fs.writeFileSync(path.join(outDir, 'smoke.json'), JSON.stringify(results, null, 2));
const failed = results.checks.filter((c) => !c.ok);
for (const c of failed) annotate('error', `Smoke: ${c.name}`, c.detail);
const summary = Object.entries(results.notes)
  .map(([pass, n]) => n.idleOnly ? `${pass}: idle fps ${n.idleFps ?? 'none'}` : `${pass}: boot ${n.bootSeconds?.toFixed?.(1)} s, load ${n.loadSeconds?.toFixed?.(1)} s, dropped ${n.dropped}, stick ${Number(n.stickMovedMetres).toFixed(1)} m, layout ${(n.layouts || []).slice(-1)[0]?.split(' ')[0]}, ui ${n.ui ?? 'none'}, idle fps ${n.idleFps ?? 'none'}, median fps ${n.fps ?? 'none'}, pixels ${JSON.stringify(n.pixels)}`)
  .join('; ');
annotate('notice', 'WebGL smoke test', `${results.checks.length - failed.length}/${results.checks.length} checks passed; build ${results.notes.desktop?.build}; scenario digest ${results.notes.desktop?.digest ?? 'none'}; ${summary}`);
process.exit(failed.length ? 1 : 0);
