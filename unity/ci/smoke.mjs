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
//   - the goal (#68): the pitch draws the simulation's goal, Law 1's mouth with 12 cm posts
//   - the keeper (#69): practice puts him in goal, where his brain keeps him on his feet
//   - autorun (desktop): the issue 1 scenario's digest in WebGL must equal the .NET hash log's
//     (ADR-0002: cross-runtime determinism is a gate)
//   - the HUD (#48, #53): placed actions hold their labels, the build line fits its panel and keeps
//     its size as its values change, and no page controls remain over the canvas
//   - Run scenario: the tap is acknowledged, and the result panel fits its text
//   - stick: dragging it up the screen runs the player up-field (from the host's trace lines), and
//     the camera keeps the player and ball inside the area the HUD leaves free
//   - the vertical pitch (#72): the attacked end is up the screen and the goal line runs across it.
//     View passes at iPhone portrait and landscape and iPad check that at 0°, flip to 180° through
//     the bench (the tick and state hash unchanged), and check it again, with stick and flick up
//     the screen going up-field each time
//   - pacing: the trace lines carry the dropped-frame counter
//   - recording: the host's command recording replays to the live state
//   - THIS FELT WRONG (#44, #53): a Unity action of at least 44 x 44 pt, placed from the HUD's
//     layout. A press takes the capture of the last 10 s there and then, and opens a Unity sheet for
//     an optional note, while play waits; the capture is kept in localStorage, announced, and SAVE
//     CAPTURE (a Unity action too) offers it. Desktop types the note and saves; the keyboard reaches
//     THIS FELT WRONG with a visible focus ring. The capture as kept in localStorage is written to
//     <output dir>/capture-<pass>.elfc, and on desktop SAVE CAPTURE's download to
//     capture-<pass>-saved.elfc (byte-identical), for CI to replay with .NET
//   - FEEL / TEST BENCH (#45): a Unity action opens a sheet of every knob the host describes, in
//     group tabs, with targets of at least 44 x 44 pt and nothing wider than the screen; opening and
//     closing it leaves the run alone; no value goes past its valid range; edits make a pending
//     candidate, shown in words; APPLY / RETRY starts a fresh run whose config and practice start
//     hash as .NET's <hash log dir>/bench-candidate.txt says; the time from the press to the fresh
//     run is reported; a capture under the candidate goes to capture-<pass>-bench.elfc
//   - rendering: the screenshot contains the pitch's two mown shades, line, player and ball colours
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
  // The pitch's mown shade (#58): the pattern that makes movement readable.
  mown: [0x27, 0x5c, 0x32],
  line: [0xf2, 0xf2, 0xf2],
  player: [0x99, 0xcc, 0x44],
  ball: [0xf7, 0xe2, 0x6b],
};

const layoutPattern = /ELEVEN layout screen=(\d+)x(\d+) run=(\d+),(\d+) reset=(\d+),(\d+) kick=(\d+),(\d+) stick=(\d+),(\d+) stickRadius=(\d+)/;
// Where the capture actions are (#53), from the same layout line: x, y, width and height in pixels.
const capturePattern = /feltWrong=(-?\d+),(-?\d+),(\d+),(\d+) save=(-?\d+),(-?\d+),(\d+),(\d+)/;
// The TEST BENCH action (#45), from the same layout line.
const benchPattern = /bench=(-?\d+),(-?\d+),(\d+),(\d+)/;
// Candidate A's ACTION surface (#28), from the same layout line, and what a gesture on it became.
const actionZonePattern = /action=(-?\d+),(-?\d+),(\d+),(\d+)/;
const actionPattern = /ELEVEN action gesture=(\w+) strength=([\d.]+) direction=(-?[\d.]+),(-?[\d.]+) latency=(-?[\d.]+)ms/;
const keptPattern = /ELEVEN capture ticks=(\d+)\.\.(\d+) steps=(\d+) bytes=(\d+) name=(\S+) note=(\d+)/;
const tracePattern = /ELEVEN trace tick=(\d+) player=(-?[\d.]+),(-?[\d.]+)/;
// The vertical pitch (#72): where the host shows the player, the attacked end, the practice goal
// and its posts, in HUD pixels with y down, and how far the play shows up-field and across.
const viewPattern = /ELEVEN view tick=(\d+) orientation=(\d+) up=(\S+) player=(-?\d+),(-?\d+) attacked=(-?\d+),(-?\d+) goal=(-?\d+),(-?\d+) posts=(-?\d+),(-?\d+);(-?\d+),(-?\d+) ahead=([\d.]+)m across=([\d.]+)m/;
// Screen vectors here are +x right and +y up the screen, as PitchOrientation's are.
const toWorld = (v, degrees) => (degrees === 180 ? { x: -v.y, y: v.x } : { x: v.y, y: -v.x });
const toScreen = (v, degrees) => (degrees === 180 ? { x: v.y, y: -v.x } : { x: -v.y, y: v.x });

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
      check('placed actions fit their labels', latest.size >= 5 && misfits.length === 0,
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

    // The goal is drawn from the simulation's geometry (#68): Law 1's mouth, 12 cm posts on the line.
    const pitch = await waitForLog(/ELEVEN pitch mowing=\S+ band=\S+ goal=(\S+) posts=(\S+) at=(\S+)/, 5_000);
    check('the pitch draws the simulation\'s goal', !!pitch && pitch.match[1] === '7.32x2.44' && pitch.match[2] === '0.12' && pitch.match[3] === '52.44,3.72',
      pitch ? pitch.match[0].replace('ELEVEN pitch ', '') : 'no pitch line');

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

    // The vertical pitch (#72): the attacked end is up the screen, the practice goal above the
    // player at 0° and below at 180°, and its posts on one screen row with the goal's centre
    // between them.
    const checkView = (degrees, v, label) => {
      if (!v) {
        check(`${label}: the attacked end is up the screen`, false, 'no view line');
        return;
      }
      const [player, attacked, goal, left, right] = [[4, 5], [6, 7], [8, 9], [10, 11], [12, 13]].map(([i, j]) => ({ x: Number(v[i]), y: Number(v[j]) }));
      const goalWay = degrees === 0 ? goal.y < player.y : goal.y > player.y;
      check(`${label}: the attacked end is up the screen`, Number(v[2]) === degrees && attacked.y < player.y && goalWay,
        `orientation ${v[2]} (${v[3]} up); player ${player.x},${player.y}; attacked end ${attacked.x},${attacked.y}; practice goal ${goal.x},${goal.y}`);
      check(`${label}: the goal line runs across the screen`,
        Math.abs(left.y - right.y) <= 2 && Math.abs(goal.y - left.y) <= 2 && goal.x > Math.min(left.x, right.x) && goal.x < Math.max(left.x, right.x),
        `posts ${left.x},${left.y} and ${right.x},${right.y}; goal centre ${goal.x},${goal.y}`);
      notes[`view${degrees}`] = `${v[14]} m up-field, ${v[15]} m across`;
    };
    const viewNow = async () => (await waitForLog(viewPattern, 30_000, logs.length))?.match ?? null;

    // View passes (#72): the vertical pitch at this size, at 0°, then flipped to 180° through the
    // bench, as a match's half-time would: stick and flick up the screen go up-field either way.
    if (url.includes('view=1')) {
      const up = (degrees) => (degrees === 0 ? 1 : -1);
      // Low in the ACTION zone: a flick up from its centre ends over THIS FELT WRONG on a phone,
      // and a lift over a UI action can activate it (#57), opening the note sheet.
      const zone = actionZonePattern.exec(lastLog(layoutPattern)?.input ?? '')?.slice(1, 5).map(Number) ?? null;
      const actionAt = zone ? at(zone[0] + zone[2] / 2, zone[1] + zone[3] * 0.85) : at(n[6], n[7]);
      const since = (from) => logs.slice(from).filter((l) => /^ELEVEN (press|release|action|ui status|ui sheet|bench open|bench closed|view flipped)/.test(l)).map((l) => l.trim().replace('ELEVEN ', '').slice(0, 90)).join(' | ') || 'nothing';
      const pointPx = Number(lastLog(/ELEVEN ui ready font=(\S+) pixelsPerPoint=([\d.]+)/)?.[2] ?? 1);
      const burst = (...events) => Promise.all(events.map(([type, touchPoints]) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints })));

      const stickUp = async (degrees) => {
        const label = `at ${degrees}°`;
        // Trace lines come once a simulated second, and on a slow runner the simulation steps
        // only on frames short enough to count: wait long, and fall back on the latest line.
        const before = (await waitForLog(tracePattern, 30_000, logs.length))?.match ?? lastLog(tracePattern);
        const origin = at(n[8], n[9]);
        const reach = n[10] * sx;
        if (useTouch) await touch('touchStart', origin);
        else {
          await page.mouse.move(origin.x, origin.y);
          await page.mouse.down();
        }
        for (let i = 1; i <= 10; i++) {
          const p = { x: origin.x, y: origin.y - (reach * i) / 10 };
          if (useTouch) await touch('touchMove', p);
          else await page.mouse.move(p.x, p.y);
          await sleep(20);
        }
        // Held until a trace line shows a run well up-field, at most 30 s: long enough on a slow
        // runner, and short enough on a fast one to stay on the pitch.
        let after = null;
        const until = Date.now() + 30_000;
        while (Date.now() < until) {
          const line = (await waitForLog(tracePattern, Math.max(1, until - Date.now()), logs.length))?.match ?? null;
          if (!line) break;
          after = line;
          if (before && Math.abs(Number(line[2]) - Number(before[2])) > 4) break;
        }
        await page.screenshot({ path: path.join(outDir, `${name}-${degrees}.png`) });
        if (useTouch) await touch('touchEnd');
        else await page.mouse.up();
        const dx = before && after ? Number(after[2]) - Number(before[2]) : NaN;
        const dy = before && after ? Number(after[3]) - Number(before[3]) : NaN;
        check(`${label}: stick up the screen runs up-field`, dx * up(degrees) > 2 && Math.abs(dy) < Math.abs(dx) * 0.2,
          before && after ? `player ${before[2]},${before[3]} → ${after[2]},${after[3]}` : 'no trace lines');
        const inView = after?.input.match(/view=player:(\w+),ball:(\w+)/);
        check(`${label}: player and ball stay clear of the HUD`, !!inView && inView[1] === 'in' && inView[2] === 'in', inView ? inView[0] : 'no view report');
      };

      const flickUp = async (degrees) => {
        await sleep(1_500);
        const from = logs.length;
        const to = { x: actionAt.x, y: actionAt.y - 60 * pointPx * sy };
        const along = (f) => ({ x: actionAt.x, y: actionAt.y + (to.y - actionAt.y) * f });
        if (useTouch) {
          await burst(['touchStart', [{ ...actionAt, id: 1 }]], ...[0.34, 0.67, 1].map((f) => ['touchMove', [{ ...along(f), id: 1 }]]), ['touchEnd', []]);
        } else {
          await page.mouse.move(actionAt.x, actionAt.y);
          await page.mouse.down();
          for (const f of [0.34, 0.67, 1]) await page.mouse.move(along(f).x, along(f).y);
          await page.mouse.up();
        }
        const flicked = (await waitForLog(actionPattern, 5_000, from))?.match ?? null;
        const x = Number(flicked?.[3]), y = Number(flicked?.[4]);
        check(`at ${degrees}°: a flick up the screen kicks up-field`, !!flicked && flicked[1] === 'flick' && x * up(degrees) > 0 && Math.abs(y) < Math.abs(x) * 0.3,
          flicked ? flicked[0].replace('ELEVEN action ', '') : `no action line after the flick; since: ${since(from)}`);
      };

      checkView(0, await viewNow(), 'at 0°');
      await stickUp(0);
      await flickUp(0);

      // The flip, through the bench's Pitch view: between frames, with no step and no command.
      const benchAt = benchPattern.exec(lastLog(layoutPattern)?.input ?? '');
      const fromOpen = logs.length;
      if (benchAt) await tap(at(Number(benchAt[1]) + Number(benchAt[3]) / 2, Number(benchAt[2]) + Number(benchAt[4]) / 2));
      const opened = (await waitForLog(/ELEVEN bench open .* close=(\S+) models=\S+ views=(\S+)/, 15_000, fromOpen))?.match ?? null;
      const views = Object.fromEntries((opened?.[2] ?? '').split(';').filter(Boolean).map((t) => t.split('=')));
      const fromFlip = logs.length;
      if (views['180']) await tap(at(...views['180'].split(',').map(Number)));
      const flipped = (await waitForLog(/ELEVEN view flipped to=180 tick=(\d+) before=([0-9a-f]{16}) after=([0-9a-f]{16}) steps=(\d+)/, 5_000, fromFlip))?.match ?? null;
      check('the half-time flip changes no state and makes no step', !!flipped && flipped[2] === flipped[3] && flipped[4] === '0',
        flipped ? flipped[0].replace('ELEVEN ', '') : `no flip line${opened ? '' : ' (the bench did not open)'}; since: ${since(fromOpen)}`);
      if (opened) await tap(at(...opened[1].split(',').map(Number)));
      // Escape closes any sheet still open, so a failure above doesn't hide the checks below.
      if (!(await waitForLog(/ELEVEN bench closed/, 5_000, fromFlip))) await page.keyboard.press('Escape');

      checkView(180, await viewNow(), 'at 180°');
      await stickUp(180);
      await flickUp(180);
      const replay = (await waitForLog(/ELEVEN recording from=(\d+) steps=(\d+) bytes=(\d+) replay=(\w+)/, 60_000, fromFlip))?.match ?? null;
      check('the recording across the flip replays to the live state', !!replay && replay[4] === 'match', replay ? replay[0].replace('ELEVEN ', '') : 'no recording check');
      const errors = logs.filter((l) => /^pageerror|Exception|error CS/i.test(l));
      check('no page errors or exceptions', errors.length === 0, errors.length ? errors.slice(0, 5).join(' | ') : 'none');
      return;
    }

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

    // Stick: drag up the screen from the stick's rest position and hold; the player should run
    // up-field, along +x toward the goal at the top (#72). Trace lines come once a simulated
    // second, so wait for a fresh one on each side of the drag.
    const before = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
    const origin = at(n[8], n[9]);
    const reach = n[10] * sx;
    if (useTouch) await touch('touchStart', origin);
    else {
      await page.mouse.move(origin.x, origin.y);
      await page.mouse.down();
    }
    for (let i = 1; i <= 10; i++) {
      const p = { x: origin.x, y: origin.y - (reach * i) / 10 };
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
    check('stick up the screen runs the player up-field', notes.stickMovedMetres > 3, before && after ? `player x ${before[2]} → ${after[2]} (ticks ${before[1]} → ${after[1]})` : 'no trace lines');
    checkView(0, await viewNow(), 'the vertical pitch');

    // The keeper (#69): practice puts him in goal, and his brain keeps him there, on his feet.
    const keeper = after?.input.match(/keeper=(-?[\d.]+),(-?[\d.]+),(\w+)/);
    check('the keeper stands in goal', !!keeper && Number(keeper[1]) > 49.5 && Number(keeper[1]) < 52.5 && Math.abs(Number(keeper[2])) < 3.66 && keeper[3] === 'position',
      keeper ? keeper[0] : 'no keeper in the trace line');

    // Running with the ball (#59, FREE): the stick steers the player onto the resting ball, and
    // touches carry it on. Like a player, it re-aims at the ball from each trace line (once a
    // simulated second) until the first touch, then holds its line.
    {
      const ballPattern = /ELEVEN trace tick=(\d+) player=(-?[\d.]+),(-?[\d.]+) ball=(-?[\d.]+),(-?[\d.]+)/;
      const touchPattern = /ELEVEN event (\d+) Touched 1/;
      const fromDribble = logs.length;
      const start = ballPattern.exec(lastLog(ballPattern)?.input ?? '') ?? null;
      let carried = null;
      let touched = null;
      let turned = null;
      let closest = Infinity;
      let last = start;
      if (start) {
        const aimAt = (m) => {
          const [px, py, bx, by] = m.slice(2, 6).map(Number);
          closest = Math.min(closest, Math.hypot(bx - px, by - py));
          const length = Math.hypot(bx - px, by - py) || 1;
          const s = toScreen({ x: (bx - px) / length, y: (by - py) / length }, 0);
          return { x: origin.x + s.x * reach, y: origin.y - s.y * reach };
        };
        // Pressed and dragged out in steps, as the stick check above does: on desktop, a press
        // followed at once by one long move left the player standing (ce241fa).
        const held = aimAt(start);
        let stickAt = held;
        if (useTouch) await touch('touchStart', origin);
        else {
          await page.mouse.move(origin.x, origin.y);
          await page.mouse.down();
        }
        for (let i = 1; i <= 10; i++) {
          const p = { x: origin.x + ((held.x - origin.x) * i) / 10, y: origin.y + ((held.y - origin.y) * i) / 10 };
          if (useTouch) await touch('touchMove', p);
          else await page.mouse.move(p.x, p.y);
          await sleep(20);
        }
        const until = Date.now() + 30_000;
        while (!touched && Date.now() < until) {
          const seen = logs.length;
          touched = await waitForLog(touchPattern, 0, fromDribble);
          if (touched) break;
          const fresh = (await waitForLog(ballPattern, Math.max(1, until - Date.now()), seen))?.match ?? null;
          touched = await waitForLog(touchPattern, 0, fromDribble);
          if (!fresh || touched) continue;
          last = fresh;
          const steer = aimAt(fresh);
          stickAt = steer;
          if (useTouch) await touch('touchMove', steer);
          else await page.mouse.move(steer.x, steer.y);
        }
        const from = touched ? (await waitForLog(ballPattern, 10_000, logs.length))?.match ?? null : null;
        const to = from ? (await waitForLog(ballPattern, 10_000, logs.length))?.match ?? null : null;

        // The turn with the ball (#66): the stick reversed in one move while carrying the ball. The
        // footballer traps it beside where they plant, then plays it out as they come round.
        if (to) {
          const fromTurn = logs.length;
          const back = { x: 2 * origin.x - stickAt.x, y: 2 * origin.y - stickAt.y };
          if (useTouch) await touch('touchMove', back);
          else await page.mouse.move(back.x, back.y);
          const trap = await waitForLog(/ELEVEN event (\d+) Turned 1 1/, 10_000, fromTurn);
          const out = trap ? await waitForLog(/ELEVEN event (\d+) Turned 1 2/, 10_000, fromTurn) : null;
          turned = { trap, out };
        }
        if (useTouch) await touch('touchEnd');
        else await page.mouse.up();
        if (from && to) {
          const moved = Math.hypot(Number(to[4]) - Number(from[4]), Number(to[5]) - Number(from[5]));
          const apart = Math.hypot(Number(to[4]) - Number(to[2]), Number(to[5]) - Number(to[3]));
          carried = { moved, apart };
        }
      }
      const touches = logs.slice(fromDribble).filter((l) => /ELEVEN event \d+ Touched 1/.test(l)).length;
      notes.dribble = carried ? `${touches} touches, ball carried ${carried.moved.toFixed(1)} m, ${carried.apart.toFixed(2)} m from the player` : 'none';
      const presses = logs.slice(fromDribble).filter((l) => l.startsWith('ELEVEN press')).map((l) => l.replace('ELEVEN press ', ''));
      const approach = Number.isFinite(closest) && last
        ? `; closest ${closest.toFixed(2)} m; last trace tick ${last[1]} player ${last[2]},${last[3]} ball ${last[4]},${last[5]}; presses: ${presses.join(' | ') || 'none'}`
        : '';
      check('running onto the ball carries it with touches', !!carried && touches >= 2 && carried.moved > 3 && carried.apart < 2,
        start ? (carried ? notes.dribble : `${touches} touches; ${touched ? 'no trace lines after the first touch' : 'no Touched event'}${approach}`) : 'no trace line with the ball');
      const trapTick = Number(turned?.trap?.match[1] ?? NaN), outTick = Number(turned?.out?.match[1] ?? NaN);
      notes.turn = turned?.trap ? `trap at tick ${trapTick}, out at ${turned.out ? outTick : 'none'}` : 'none';
      check('reversing with the ball traps it, then plays it out', !!turned?.trap && !!turned?.out && outTick > trapTick,
        carried ? notes.turn : 'no dribble to reverse');
    }

    // Candidate A, ACTION SURFACE (#28): the right thumb's surface, large and at least 44 x 44 pt.
    const fromKick = logs.length;
    const pointPx = Number(ui?.[2] ?? 1);
    const zone = actionZonePattern.exec(lastLog(layoutPattern)?.input ?? '')?.slice(1, 5).map(Number) ?? null;
    check('the ACTION surface is a large right-thumb surface of at least 44 x 44 pt',
      !!zone && zone[2] / pointPx >= 44 && zone[3] / pointPx >= 44 && zone[0] + zone[2] / 2 > n[0] / 2,
      zone ? `${zone.join(',')} px, ${(zone[2] / pointPx).toFixed(0)} x ${(zone[3] / pointPx).toFixed(0)} pt` : 'no action= in the layout line');
    const actionCentre = zone ? at(zone[0] + zone[2] / 2, zone[1] + zone[3] / 2) : at(n[6], n[7]);

    // Gestures on ACTION are timed from their own events, so in the touch pass each gesture's events
    // go out in one burst: awaited one by one, every DevTools touch event costs a frame, and at this
    // runner's 3-7 fps that makes any tap a quarter-second hold. A real tap is far shorter than the
    // hold threshold; the burst keeps it so.
    const burst = (...events) => Promise.all(events.map(([type, touchPoints]) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints })));
    const one = (p, id = 1) => [{ x: p.x, y: p.y, id }];

    // A tap asks the footballer for the quick pass, delivered at once: no waiting for the hold.
    if (useTouch) {
      await burst(['touchStart', one(actionCentre)], ['touchEnd', []]);
      await sleep(150);
    } else await tap(actionCentre);
    const tapped = await waitForLog(actionPattern, 5_000, fromKick);
    const tapPress = logs.slice(fromKick).find((l) => l.startsWith('ELEVEN press'));
    const resolved = await waitForLog(/ELEVEN event \d+ QuickPassResolved/, 5_000, fromKick);
    notes.tapLatencyMs = tapped ? Number(tapped.match[5]) : NaN;
    check('a tap on ACTION asks for the quick pass', !!tapped && tapped.match[1] === 'tap' && /target=action/.test(tapPress ?? '') && !!resolved,
      `${tapPress ?? 'no press logged'}; ${tapped ? tapped.match[0].replace('ELEVEN action ', '') : 'no action line'}; ${resolved ? resolved.match[0].replace('ELEVEN event ', '') : 'no QuickPassResolved event'}`);

    // An immediate flick is a manual kick along the gesture: right and up the screen is, on the
    // vertical pitch, +x and −y (#72). Wait for the quick pass's wind-up and window to pass first,
    // so ACTION starts a gesture.
    await sleep(1_500);
    const fromFlick = logs.length;
    const flickTo = { x: actionCentre.x + 60 * pointPx * sx, y: actionCentre.y - 30 * pointPx * sy };
    const flickAt = (f) => ({ x: actionCentre.x + (flickTo.x - actionCentre.x) * f, y: actionCentre.y + (flickTo.y - actionCentre.y) * f });
    if (useTouch) {
      await burst(['touchStart', one(actionCentre)], ...[0.34, 0.67, 1].map((f) => ['touchMove', one(flickAt(f))]), ['touchEnd', []]);
    } else {
      await page.mouse.move(actionCentre.x, actionCentre.y);
      await page.mouse.down();
      for (const f of [0.34, 0.67, 1]) await page.mouse.move(flickAt(f).x, flickAt(f).y);
      await page.mouse.up();
    }
    const flicked = await waitForLog(actionPattern, 5_000, fromFlick);
    const flickWorld = toWorld({ x: 60, y: 30 }, 0);
    check('an immediate flick on ACTION is a manual kick along the gesture',
      !!flicked && flicked.match[1] === 'flick' && Math.sign(Number(flicked.match[3])) === Math.sign(flickWorld.x) && Math.sign(Number(flicked.match[4])) === Math.sign(flickWorld.y),
      flicked ? flicked.match[0].replace('ELEVEN action ', '') : 'no action line after the flick');

    // Both thumbs at once: the stick held right while the right thumb taps ACTION. The tap still
    // asks for the pass and the stick still moves the player. Touch only: a mouse is one pointer.
    if (useTouch) {
      await sleep(1_500);
      const fromBoth = logs.length;
      const s0 = at(n[8], n[9]);
      const s1 = { x: s0.x + reach, y: s0.y };
      const points = (...p) => p.map(([q, id]) => ({ x: q.x, y: q.y, id }));
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: points([s0, 1]) });
      for (let i = 1; i <= 5; i++) {
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: points([{ x: s0.x + (reach * i) / 5, y: s0.y }, 1]) });
        await sleep(20);
      }
      const beforeBoth = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
      // DevTools keeps a finger left out of a touchMove down; a touchEnd listing one finger lifts
      // that finger alone. So: the right thumb lands beside the held left thumb, then lifts alone.
      await burst(['touchStart', points([s1, 1], [actionCentre, 2])], ['touchEnd', points([actionCentre, 2])]);
      const both = await waitForLog(actionPattern, 5_000, fromBoth);
      const afterBoth = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      const presses = logs.slice(fromBoth).filter((l) => /^ELEVEN (press|release) /.test(l)).map((l) => l.trim().replace('ELEVEN ', ''));
      const targets = presses.map((l) => /target=(\w+)/.exec(l)?.[1]).filter(Boolean);
      const moved = beforeBoth && afterBoth ? Math.hypot(Number(afterBoth[2]) - Number(beforeBoth[2]), Number(afterBoth[3]) - Number(beforeBoth[3])) : NaN;
      check('the stick and ACTION work at the same time', !!both && both.match[1] === 'tap' && targets.includes('stick') && targets.includes('action') && moved > 1,
        `${presses.join(' | ') || 'no presses'}; ${both ? both.match[0].replace('ELEVEN action ', '') : 'no action line'}; player moved ${Number.isFinite(moved) ? moved.toFixed(2) : 'unknown'} m while both were down`);
    }

    // A finger keeps its owner from press to lift (Codex on #57): Unity reports a held finger
    // pressed again when it moves, and that repeat must not hand the stick's finger to the UI it
    // passes over, or its lift never reaches the stick and the player runs on. Here the stick's
    // finger lands on its rest, then its first move (where Unity repeats the press) goes straight
    // onto Run scenario, and it lifts there: the player stops. Run scenario because the lift may
    // also activate the action it is over (UI Toolkit's own input; noted, not asserted), and a
    // scenario run leaves play alone, where THIS FELT WRONG's note sheet would pause it.
    if (useTouch) {
      await sleep(500);
      const fromOver = logs.length;
      const rest = at(n[8], n[9]);
      const over = at(n[2], n[3]);
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: one(rest) });
      await sleep(1_000);
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: one(over) });
      await waitForLog(tracePattern, 5_000, logs.length);
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      // Two fresh traces after the lift: the player has stopped between them.
      const first = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
      const second = first ? (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null : null;
      // Then a fresh drag right: the stick is free again, so it moves the player.
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: one(rest) });
      for (let i = 1; i <= 5; i++) {
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: one({ x: rest.x + (reach * i) / 5, y: rest.y }) });
      }
      const dragFrom = (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null;
      const dragTo = dragFrom ? (await waitForLog(tracePattern, 5_000, logs.length))?.match ?? null : null;
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      const lines = logs.slice(fromOver).filter((l) => /^ELEVEN (press|release|capture pressed|scenario) /.test(l)).map((l) => l.trim().replace('ELEVEN ', '').slice(0, 80));
      const still = !!first && !!second && first[2] === second[2] && first[3] === second[3];
      const dragged = dragFrom && dragTo ? Math.hypot(Number(dragTo[2]) - Number(dragFrom[2]), Number(dragTo[3]) - Number(dragFrom[3])) : NaN;
      const freed = dragged > 1;
      notes.liftOverActionActivates = lines.some((l) => l.startsWith('scenario'));
      check('a stick finger that passes over the UI still lets go of the stick', still && freed,
        `${lines.join(' | ') || 'no presses'}; player ${first ? `${first[2]},${first[3]}` : '?'} → ${second ? `${second[2]},${second[3]}` : '?'} after the lift; a fresh drag moved it ${Number.isFinite(dragged) ? dragged.toFixed(2) : '?'} m; the lift ${notes.liftOverActionActivates ? 'activated' : 'did not activate'} Run scenario`);
    }

    const kickErrors = logs.slice(fromKick).filter((l) => /error|exception/i.test(l));
    check('ACTION gestures are taken without errors', kickErrors.length === 0, kickErrors.length ? kickErrors.join(' | ') : 'no errors');

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
    // On a phone the on-screen keyboard covers the lower part of the screen (#55): the sheet sits
    // in the top half, clear of it.
    if (useTouch) {
      const box = sheet ? /box=(\d+),(\d+),(\d+),(\d+)/.exec(sheet.match.input) : null;
      const bottom = box ? Number(box[2]) + Number(box[4]) : NaN;
      check('the note sheet sits clear of the on-screen keyboard', !!box && bottom <= n[1] * 0.5,
        box ? `sheet ${box.slice(1, 5).join(',')} px, bottom at ${(100 * bottom / n[1]).toFixed(0)} % of the screen` : 'no sheet box in the log');
    }
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

    // FEEL / TEST BENCH (#45) as a Unity sheet (ADR-0003): a Unity action opens it; it lists every
    // knob in group tabs; opening and closing it leaves the run alone; no value goes past its valid
    // range; edits make a pending candidate, shown in words; APPLY / RETRY starts a fresh run whose
    // config and practice start hash as .NET says that candidate's do. The host says where its
    // controls are, as it does for the HUD.
    const point = (text) => text.split(',').map(Number);
    const benchFit = lastLog(/ELEVEN ui fit name=Test_bench .* size=([\d.]+)x([\d.]+) (\w+)/);
    const benchAt = benchPattern.exec(lastLog(layoutPattern)?.input ?? '');
    check('test bench is a Unity action of at least 44 x 44 pt that holds its label',
      !!benchAt && !!benchFit && Number(benchFit[1]) >= 44 && Number(benchFit[2]) >= 44 && benchFit[3] === 'ok',
      `${benchAt ? `placed at ${benchAt.slice(1, 5).join(',')} px` : 'not in the layout line'}; ${benchFit ? benchFit.input.trim().replace('ELEVEN ui fit ', '') : 'no fit report'}`);
    const knobsLine = lastLog(/ELEVEN bench knobs=(\d+) preset=(\S+) config=([0-9a-f]{16}) start=([0-9a-f]{16})/);
    const controlPattern = (knob) => new RegExp(`ELEVEN bench control name=${knob} value=(\\S+) less=(\\S+) more=(\\S+) track=(\\S+)`);
    const valuePattern = (knob) => new RegExp(`ELEVEN bench value name=${knob} value=(\\S+) limit=(\\w+) pending=(\\d+)`);
    const fromOpen = logs.length;
    const tickBeforeOpen = Number(lastLog(tracePattern)?.[1] ?? -1);
    if (benchAt) await tap(centre(...benchAt.slice(1, 5)));
    const opened = await waitForLog(/ELEVEN bench open knobs=(\d+) group=(\S+) tabs=(\S+) apply=(\S+) revert=(\S+) close=(\S+)/, 8_000, fromOpen);
    check('the bench lists every knob the host describes', !!opened && !!knobsLine && opened.match[1] === knobsLine[1] && Number(opened.match[1]) > 0,
      opened ? `${opened.match[1]} knobs in tabs ${opened.match[3]}, host describes ${knobsLine?.[1] ?? 'none'}` : 'no "bench open" line');
    const targets = await waitForLog(/ELEVEN bench targets=(\d+) smallest=([\d.]+) smallestName=(\S+) overflow=(\S+)/, 5_000, fromOpen);
    check('bench controls are at least 44 x 44 pt, and nothing is wider than the screen',
      !!targets && Number(targets.match[2]) >= 44 && targets.match[4] === 'none',
      targets ? targets.match[0].replace('ELEVEN bench ', '') : 'no target report');
    // Tab while the sheet is open (Codex on #49): only controls that can be seen, ending at the actions.
    const focusOrder = await waitForLog(/ELEVEN bench focus order=(\S*) count=(\d+) hidden=(\d+)/, 3_000, fromOpen);
    check('Tab in the bench stops only at controls that can be seen, ending at its actions',
      !!focusOrder && focusOrder.match[3] === '0' && /Close,Revert,Apply_\/_Retry$/.test(focusOrder.match[1]),
      focusOrder ? `${focusOrder.match[2]} stops, ${focusOrder.match[3]} hidden; ends ${focusOrder.match[1].split(',').slice(-4).join(',')}` : 'no focus order line');
    const tabs = Object.fromEntries((opened?.match[3] ?? '').split(';').filter(Boolean).map((t) => t.split('=')));
    // Every tab opens its group, also where the tabs wrap onto a second row (a phone).
    const reached = [];
    for (const [group, where] of Object.entries(tabs)) {
      const from = logs.length;
      await tap(at(...point(where)));
      if (await waitForLog(new RegExp(`ELEVEN bench group name=${group}\\b`), 5_000, from)) reached.push(group);
    }
    check('every bench tab opens its group', Object.keys(tabs).length > 0 && reached.length === Object.keys(tabs).length,
      `${reached.length} of ${Object.keys(tabs).length} tabs opened their group: ${reached.join(', ') || 'none'} (tabs ${opened?.match[3] ?? 'none'})`);
    // Back to the first tab, where the sheet opened, for the checks that follow.
    const [firstGroup, firstAt] = Object.entries(tabs)[0] ?? [];
    if (firstGroup) {
      const from = logs.length;
      await tap(at(...point(firstAt)));
      await waitForLog(new RegExp(`ELEVEN bench group name=${firstGroup}\\b`), 5_000, from);
    }
    // The live FREE ↔ ATTACHED switch (#60), at the top of the sheet: it switches at once, the
    // state is the same either side of each switch, and the run goes on from it. That last part
    // is checked once the bench closes: on CI's phone pass the run crawls while the sheet is open.
    const models = Object.fromEntries((opened?.match.input.match(/ models=(\S+)/)?.[1] ?? '').split(';').filter(Boolean).map((t) => t.split('=')));
    const switched = [];
    for (const model of ['Attached', 'Free']) {
      const from = logs.length;
      if (models[model]) await tap(at(...point(models[model])));
      const line = await waitForLog(new RegExp(`ELEVEN model switched to=${model.toLowerCase()} tick=(\\d+) before=([0-9a-f]{16}) after=([0-9a-f]{16})`), 5_000, from);
      switched.push({ model, line: line?.match ?? null });
    }

    const tabTo = async (group, knob) => {
      const from = logs.length;
      if (tabs[group]) await tap(at(...point(tabs[group])));
      return (await waitForLog(controlPattern(knob), 5_000, from))?.match ?? null;
    };
    const nudge = async (where, knob) => {
      const from = logs.length;
      await tap(at(...point(where)));
      return (await waitForLog(valuePattern(knob), 3_000, from))?.match ?? null;
    };

    // No value goes past its valid range: Gravity's slider pressed at its far end, then +, stays at
    // the top of the range. Revert puts every pending edit back.
    const gravity = (await waitForLog(controlPattern('gravity'), 3_000, fromOpen))?.match ?? null;
    let top = null;
    let past = null;
    if (gravity) {
      const [x0, x1, y] = point(gravity[4]);
      top = await nudge(`${x1 - 1},${y}`, 'gravity');
      past = await nudge(gravity[3], 'gravity');
    }
    check('no value goes past its valid range', !!top && !!past && top[2] === 'max' && past[2] === 'max' && past[1] === top[1],
      top ? `slider at its end: ${top[1]} (${top[2]}); then +: ${past?.[1]} (${past?.[2]})` : gravity ? 'no value line' : 'no gravity control line');
    const fromRevert = logs.length;
    if (opened) await tap(at(...point(opened.match[5])));
    const reverted = await waitForLog(/ELEVEN bench reverted pending=(\d+)/, 3_000, fromRevert);

    // Two edits make a pending candidate, shown in words: Kick speed one step up, Max speed five.
    const kick = await tabTo('Kick', 'kickSpeed');
    const kickUp = kick ? await nudge(kick[3], 'kickSpeed') : null;
    const speed = await tabTo('Player', 'maxSpeed');
    let speedUp = null;
    for (let i = 0; speed && i < 5; i++) speedUp = await nudge(speed[3], 'maxSpeed');
    const pending = lastLog(/ELEVEN bench pending text=(.*)/);
    check('edits make a pending candidate, shown in words',
      !!reverted && reverted.match[1] === '0' && kickUp?.[1] === '18.5' && speedUp?.[1] === '7.5' && /^2 pending changes/.test(pending?.[1] ?? ''),
      `revert: ${reverted ? reverted.match[0].replace('ELEVEN bench ', '') : 'none'}; kick speed ${kickUp?.[1] ?? 'none'}, max speed ${speedUp?.[1] ?? 'none'}; "${pending?.[1]?.trim() ?? 'no pending text'}"`);

    // Closing leaves the run alone: no reset, no apply and no game input, and the run went on.
    const fromClose = logs.length;
    if (opened) await tap(at(...point(opened.match[6])));
    await waitForLog(/ELEVEN bench closed/, 3_000, fromClose);
    // CI's software renderer runs the phone pass near 5 fps, where frames over 250 ms are dropped
    // rather than caught up, so give play time to cover a trace interval past the opening.
    const traceAfter = (from) => waitFor(logs, { exec: (l) => { const m = tracePattern.exec(l); return m && Number(m[1]) > tickBeforeOpen ? m : null; } }, 20_000, from);
    await traceAfter(fromOpen);
    const whileOpen = logs.slice(fromOpen).filter((l) => /^ELEVEN (reset|bench applied)/.test(l) || (/^ELEVEN press /.test(l) && !/target=ui/.test(l)));
    const tickAfterClose = Number(lastLog(tracePattern)?.[1] ?? -1);
    check('opening and closing the bench leaves the run alone', !!opened && whileOpen.length === 0 && tickAfterClose > tickBeforeOpen,
      `${whileOpen.length ? whileOpen.slice(0, 3).join(' | ') : 'no reset, apply or game input'}; trace tick ${tickBeforeOpen} → ${tickAfterClose}`);
    // The run went on from the switch: a trace at or past the last switch's tick, waited for,
    // since the latest trace when the sheet closes may predate the switches.
    const switchTicks = switched.map((w) => Number(w.line?.[1] ?? NaN));
    const resumed = Number.isFinite(switchTicks[1])
      ? await waitFor(logs, { exec: (l) => { const m = tracePattern.exec(l); return m && Number(m[1]) >= switchTicks[1] ? m : null; } }, 20_000, fromOpen)
      : null;
    check('the ball control model switches live, keeping the run and its state',
      switched.every((w) => w.line && w.line[2] === w.line[3]) && switchTicks[1] >= switchTicks[0] && whileOpen.length === 0 && !!resumed,
      Object.keys(models).length
        ? `${switched.map((w) => (w.line ? `${w.model} at tick ${w.line[1]}, state ${w.line[2]} → ${w.line[3]}` : `${w.model}: no switch line`)).join('; ')}; next trace tick ${resumed ? resumed.match[1] : 'none within 20 s'}`
        : `no model control in the bench (open line: ${opened?.match.input ?? 'none'})`);

    // APPLY / RETRY: the pending candidate starts a fresh run, hashed as .NET hashes it.
    const fromReopen = logs.length;
    if (benchAt) await tap(centre(...benchAt.slice(1, 5)));
    const reopened = await waitForLog(/ELEVEN bench open knobs=(\d+) group=(\S+) tabs=(\S+) apply=(\S+) revert=(\S+) close=(\S+)/, 8_000, fromReopen);
    const fromApply = logs.length;
    const tickBeforeApply = Number(lastLog(tracePattern)?.[1] ?? -1);
    if (reopened) await tap(at(...point(reopened.match[4])));
    const applied = await waitForLog(/ELEVEN bench applied changes=(\d+) preset=(\S+) config=([0-9a-f]{16}) start=([0-9a-f]{16}) hostMs=([\d.]+)/, 5_000, fromApply);
    const expectedFile = path.join(hashlogDir, 'bench-candidate.txt');
    const expected = fs.existsSync(expectedFile) ? /config ([0-9a-f]{16})\s+practice start ([0-9a-f]{16})/.exec(fs.readFileSync(expectedFile, 'utf8')) : null;
    check('APPLY / RETRY starts a fresh run with the candidate, hashed as in .NET',
      !!applied && applied.match[1] === '2' && applied.match[2] === 'unsaved' && !!expected
        && applied.match[3] === expected[1] && applied.match[4] === expected[2] && applied.match[3] !== knobsLine?.[3],
      applied ? `${applied.match[0].replace('ELEVEN ', '')}; .NET: ${expected ? `config ${expected[1]} start ${expected[2]}` : 'no bench-candidate.txt'}` : 'no "bench applied" line');
    if (applied) notes.benchApplyMs = `host ${applied.match[5]} ms`;
    const afterApply = applied ? (await waitForLog(tracePattern, 20_000, applied.index + 1))?.match ?? null : null;
    const closedOnApply = !!(await waitForLog(/ELEVEN bench closed/, 3_000, fromApply));
    const announcedApply = await waitForLog(/ELEVEN ui status (Applied .*)/, 3_000, fromApply);
    check('the fresh run starts over, the sheet closes and the result is announced',
      !!afterApply && Number(afterApply[1]) < tickBeforeApply && closedOnApply && /^Applied 2 changes, fresh run in \d+ ms/.test(announcedApply?.match[1] ?? ''),
      `trace tick ${tickBeforeApply} → ${afterApply?.[1]}; sheet ${closedOnApply ? 'closed' : 'still open'}; ${announcedApply?.match[1]?.trim() ?? 'no status'}`);

    // A capture under the candidate records it, and replays exactly under .NET in CI too.
    const fromBenchCapture = logs.length;
    if (placed) await tap(centre(...placed.slice(1, 5)));
    const benchSheet = await waitForLog(/ELEVEN ui sheet open keep=(\d+),(\d+) skip=(\d+),(\d+)/, 8_000, fromBenchCapture);
    if (benchSheet) await tap(at(Number(benchSheet.match[3]), Number(benchSheet.match[4])));
    const benchCaptured = await waitForLog(keptPattern, 10_000, fromBenchCapture);
    const benchBytes = benchCaptured ? await keptBytes(benchCaptured.match[5]) : null;
    if (benchBytes) fs.writeFileSync(path.join(outDir, `capture-${name}-bench.elfc`), benchBytes);
    check('felt-wrong captures under the candidate, which the capture names', !!benchBytes && benchBytes.includes(Buffer.from('unsaved', 'utf8')),
      benchCaptured ? `${benchCaptured.match[0].replace('ELEVEN ', '')}; preset "unsaved" ${benchBytes?.includes(Buffer.from('unsaved', 'utf8')) ? 'recorded' : 'NOT recorded'}` : 'no capture line');

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
    check('pitch, line, player and ball are drawn', ['pitch', 'line', 'player', 'ball'].every((k) => notes.pixels[k] > 20), JSON.stringify(notes.pixels));
    // The mown pattern (#58) covers about half the grass. Edges blended near the pitch's colour
    // count as the mown shade too (about 0.5 % of the grass without any pattern), so the check is a
    // share of the grass, not a count.
    const grass = notes.pixels.pitch + notes.pixels.mown;
    const mownShare = grass > 0 ? notes.pixels.mown / grass : 0;
    check('the pitch is mown in two shades', mownShare >= 0.2 && mownShare <= 0.8,
      `${(mownShare * 100).toFixed(1)} % of the grass is the mown shade (${notes.pixels.mown} of ${grass} pixels)`);

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
  // The mown pattern's frame cost (#58): the same idle pass on the plain pitch, right after the
  // cross-cut one, on the same runner. The software renderer exaggerates fill cost; a phone's GPU doesn't.
  await runPass(browser, idle('noui=1&pitch=plain'), 'phone-noui-plain-pitch', phone, true);
  await runPass(browser, idle('ui=empty'), 'phone-panel-empty', phone, true);
  await runPass(browser, `${base}?trace=1`, 'phone', phone, true);
  await runPass(browser, idle('noui=1'), 'phone-noui-after', phone, true);
  // The HUD on a 320 pt phone with text at 200 % (WCAG 1.4.4): placed actions must grow with their labels.
  await runPass(browser, `${base}?trace=1&idle=1&textscale=2`, 'narrow-large-text-hud', { ...phone, viewport: { width: 320, height: 640 } }, true);
  // The vertical pitch (#72) at 0° and 180°: iPhone portrait and landscape, and iPad portrait.
  const { defaultBrowserType: _landscape, ...phoneLandscape } = devices['iPhone 13 landscape'];
  const { defaultBrowserType: _tablet, ...tabletPortrait } = devices['iPad (gen 7)'];
  await runPass(browser, `${base}?trace=1&view=1`, 'view-phone', phone, true);
  await runPass(browser, `${base}?trace=1&view=1`, 'view-phone-landscape', phoneLandscape, true);
  // The iPad at device scale 1: the same layout in proportion, a quarter of the pixels. At its
  // own scale 2 the software renderer ran it at 3 fps, too slow for the simulation to step.
  await runPass(browser, `${base}?trace=1&view=1`, 'view-tablet', { ...tabletPortrait, deviceScaleFactor: 1 }, true);

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
  .map(([pass, n]) => n.idleOnly ? `${pass}: idle fps ${n.idleFps ?? 'none'}` : `${pass}: boot ${n.bootSeconds?.toFixed?.(1)} s, load ${n.loadSeconds?.toFixed?.(1)} s, dropped ${n.dropped}, stick ${Number(n.stickMovedMetres).toFixed(1)} m, layout ${(n.layouts || []).slice(-1)[0]?.split(' ')[0]}, bench apply ${n.benchApplyMs ?? 'none'}, ui ${n.ui ?? 'none'}, idle fps ${n.idleFps ?? 'none'}, median fps ${n.fps ?? 'none'}, pixels ${JSON.stringify(n.pixels)}`)
  .join('; ');
annotate('notice', 'WebGL smoke test', `${results.checks.length - failed.length}/${results.checks.length} checks passed; build ${results.notes.desktop?.build}; scenario digest ${results.notes.desktop?.digest ?? 'none'}; ${summary}`);
process.exit(failed.length ? 1 : 0);
