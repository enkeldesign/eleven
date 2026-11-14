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
//   - Candidate B, SPLIT (#76), iPhone portrait and landscape: PASS and KICK each hold a 44 x 44 pt
//     square; B is switched on through the bench with no step; PASS asks for the quick pass at the
//     press; KICK's flick, hold → flick and re-touch; the stick and KICK together; a KICK finger
//     held across a switch and back makes nothing; and at 180° a flick up kicks up-field
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

    // Candidate B, SPLIT (#76): switched on through the bench, live; PASS at the press, KICK's
    // flick, hold → flick and re-touch, two thumbs, a finger held across a switch, and 180°.
    // Candidate C, ONE FIRE (#77), shares the pass's helpers: `fire=1`. So does the layout matrix
    // (#31): `layout=1`.
    if (url.includes('split=1') || url.includes('fire=1') || url.includes('layout=1')) {
      const pointPx = Number(lastLog(/ELEVEN ui ready font=(\S+) pixelsPerPoint=([\d.]+)/)?.[2] ?? 1);
      const layoutLine = lastLog(layoutPattern)?.input ?? '';
      const zone = actionZonePattern.exec(layoutLine)?.slice(1, 5).map(Number) ?? null;
      const passAt = /pass=(-?\d+),(-?\d+),(\d+)/.exec(layoutLine)?.slice(1, 4).map(Number) ?? null;
      const markerAt = /kickMarker=(-?\d+),(-?\d+),(\d+)/.exec(layoutLine)?.slice(1, 4).map(Number) ?? null;
      const since = (from) => logs.slice(from).filter((l) => /^ELEVEN (press|release|action|controls|ui status|bench open|bench closed|view flipped)/.test(l)).map((l) => l.trim().replace('ELEVEN ', '').slice(0, 90)).join(' | ') || 'nothing';
      const burst = (...events) => Promise.all(events.map(([type, touchPoints]) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints })));
      const pt = (q, id) => ({ x: q.x, y: q.y, id });

      // Targets: PASS's disc holds a 44 x 44 pt square, and so does KICK's marker, inside a
      // right-hand area larger still.
      const square = (r) => (r * Math.SQRT2) / pointPx;
      check('PASS and KICK are bounded targets of at least 44 x 44 pt',
        !!passAt && !!zone && !!markerAt && square(passAt[2]) >= 44 && square(markerAt[2]) >= 44 && zone[2] / pointPx >= 88 && zone[3] / pointPx >= 88,
        `pass ${passAt?.join(',') ?? '?'} px (${passAt ? square(passAt[2]).toFixed(0) : '?'} pt square); kick marker ${markerAt?.join(',') ?? '?'}; area ${zone?.join(',') ?? '?'} px at ${pointPx} px/pt`);
      const pass = passAt ? at(passAt[0], passAt[1]) : at(n[6], n[7]);
      // KICK: its marker, beside PASS, unless a flick from there would end on one of the HUD's
      // actions. The test sends a flick in one go, and at a few frames a second the UI sees the
      // press where the flick ends: on THIS FELT WRONG, it opens the note sheet (a landscape
      // phone). Then the right-hand area's lower left, which is KICK too.
      const uiRects = ['feltWrong', 'save', 'bench'].map((k) => new RegExp(`${k}=(-?\\d+),(-?\\d+),(\\d+),(\\d+)`).exec(layoutLine)?.slice(1, 5).map(Number)).filter(Boolean);
      const onUi = (x, y) => uiRects.some(([rx, ry, rw, rh]) => x >= rx && x <= rx + rw && y >= ry && y <= ry + rh);
      const flicksClear = ([cx, cy]) => [[30, 60], [0, 60], [20, 40], [40, -10]].every(([dx, dy]) => !onUi(cx + dx * pointPx, cy - dy * pointPx));
      const offPass = ([cx, cy]) => !passAt || Math.hypot(cx - passAt[0], cy - passAt[1]) > passAt[2] + 4 * pointPx;
      const kickStarts = [
        ...(markerAt && markerAt[2] > 0 ? [['marker', markerAt[0], markerAt[1]]] : []),
        ...(zone ? [['lower left', zone[0] + 30 * pointPx, zone[1] + zone[3] - 30 * pointPx]] : []),
      ];
      const kickStart = kickStarts.find(([, cx, cy]) => offPass([cx, cy]) && flicksClear([cx, cy])) ?? kickStarts[0] ?? ["old ACTION disc's centre", n[6], n[7]];
      const kick = at(kickStart[1], kickStart[2]);
      const kickWhere = `KICK from its ${kickStart[0]} at ${kickStart[1].toFixed(0)},${kickStart[2].toFixed(0)}`;

      // The switch, through the bench: between frames, no step, the state untouched.
      const benchAt = benchPattern.exec(layoutLine);
      const benchCentre = benchAt ? at(Number(benchAt[1]) + Number(benchAt[3]) / 2, Number(benchAt[2]) + Number(benchAt[4]) / 2) : null;
      const segments = (text) => Object.fromEntries((text ?? '').split(';').filter(Boolean).map((t) => t.split('=')));
      const press = async (where, id, held = []) => {
        if (!where) return;
        const p = at(...where.split(',').map(Number));
        await burst(['touchStart', [...held, pt(p, id)]], ['touchEnd', [pt(p, id)]]);
        await sleep(300);
      };
      // The bench, opened on its Touch tab, where the A / B choice is: [close, views, candidates].
      const openBench = async (id, held = [], touchTab = true) => {
        const fromOpen = logs.length;
        if (benchCentre) await burst(['touchStart', [...held, pt(benchCentre, id)]], ['touchEnd', [pt(benchCentre, id)]]);
        const opened = (await waitForLog(/ELEVEN bench open .* tabs=(\S+) apply=\S+ revert=\S+ close=(\S+) models=\S+ views=(\S+)/, 15_000, fromOpen))?.match ?? null;
        if (!opened) return null;
        if (!touchTab) return [null, opened[2], opened[3], ''];
        const fromTab = logs.length;
        await press(segments(opened[1]).Touch, id, held);
        const candidates = (await waitForLog(/ELEVEN bench candidates (\S+)/, 10_000, fromTab))?.match ?? null;
        return [null, opened[2], opened[3], candidates?.[1] ?? ''];
      };
      const closeBench = async (opened, id, held = []) => {
        const fromClose = logs.length;
        if (opened) await press(opened[1], id, held);
        if (!(await waitForLog(/ELEVEN bench closed/, 5_000, fromClose))) await page.keyboard.press('Escape');
      };
      const switchPattern = (to) => new RegExp(`ELEVEN controls switched to=${to} tick=(\\d+) before=([0-9a-f]{16}) after=([0-9a-f]{16}) steps=(\\d+)`);

      // Between a candidate's checks, the last kick's wind-up and aftertouch window (a few tenths
      // of a simulated second) must have closed, or the next press where a kick is made shapes it.
      // The phone passes can step only about 13 times a wall-clock second, so wait on the
      // simulation, not the clock: two fresh trace lines, which come once a simulated second, are
      // 60 steps apart.
      const settle = async () => {
        const first = await waitForLog(tracePattern, 20_000, logs.length);
        if (first) await waitForLog(tracePattern, 20_000, first.index + 1);
      };

      // The layout matrix (#31): one viewport of the phone and tablet sizes ELEVEN is laid out
      // for. Every control inside the safe area, the play area clear of them, targets of 44 pt,
      // the footballer and the ball in the play area at a useful size, and a bench every part of
      // which is in reach.
      if (url.includes('layout=1')) {
        const boxOf = (k, line = layoutLine) => new RegExp(`${k}=(-?\\d+),(-?\\d+),(\\d+),(\\d+)`).exec(line)?.slice(1, 5).map(Number) ?? null;
        const inside = (a, b) => !!a && !!b && a[0] >= b[0] - 1 && a[1] >= b[1] - 1 && a[0] + a[2] <= b[0] + b[2] + 1 && a[1] + a[3] <= b[1] + b[3] + 1;
        const apart = (a, b) => !!a && !!b && (a[0] + a[2] <= b[0] + 1 || b[0] + b[2] <= a[0] + 1 || a[1] + a[3] <= b[1] + 1 || b[1] + b[3] <= a[1] + 1);
        const disc = (cx, cy, r) => [cx - r, cy - r, 2 * r, 2 * r];
        const pts = (b) => (b ? `${(b[2] / pointPx).toFixed(0)}x${(b[3] / pointPx).toFixed(0)}` : '?');
        const measure = (line) => {
          const m = layoutPattern.exec(line)?.slice(1).map(Number) ?? [];
          const kickRadius = Number(/kickRadius=(\d+)/.exec(line)?.[1] ?? 0);
          const passDisc = /pass=(-?\d+),(-?\d+),(\d+)/.exec(line)?.slice(1, 4).map(Number) ?? null;
          const marker = /kickMarker=(-?\d+),(-?\d+),(\d+)/.exec(line)?.slice(1, 4).map(Number) ?? null;
          return {
            screen: [0, 0, m[0], m[1]], safe: boxOf('safe', line), play: boxOf('play', line), action: boxOf('action', line), stickZone: boxOf('stickZone', line),
            felt: boxOf('feltWrong', line), save: boxOf('save', line), bench: boxOf('bench', line),
            stick: disc(m[8], m[9], m[10]), kick: disc(m[6], m[7], kickRadius),
            pass: passDisc ? disc(...passDisc) : null, marker: marker && marker[2] > 0 ? disc(...marker) : null,
          };
        };
        const controlsInSafe = (l) => Object.entries({ stick: l.stick, 'stick zone': l.stickZone, 'right-hand area': l.action, ACTION: l.kick, PASS: l.pass, 'felt wrong': l.felt, save: l.save, bench: l.bench })
          .filter(([, b]) => !inside(b, l.safe)).map(([k, b]) => `${k} ${b?.join(',') ?? 'missing'}`);
        const L = measure(layoutLine);

        const outside = controlsInSafe(L);
        check('every control is inside the safe area', !!L.safe && inside(L.safe, L.screen) && outside.length === 0,
          `safe ${L.safe?.join(',') ?? '?'} of screen ${L.screen.slice(2).join('x')} px; outside: ${outside.join(' | ') || 'none'}`);

        const over = Object.entries({ 'stick zone': L.stickZone, 'right-hand area': L.action, 'felt wrong': L.felt, save: L.save, bench: L.bench })
          .filter(([, b]) => !apart(L.play, b)).map(([k]) => k);
        check('the play area is clear of the controls and the HUD\'s actions', !!L.play && inside(L.play, L.safe) && over.length === 0 && L.play[2] / pointPx >= 150 && L.play[3] / pointPx >= 150,
          `play area ${L.play?.join(',') ?? '?'} px, ${pts(L.play)} pt; over it: ${over.join(', ') || 'nothing'}`);

        const small = Object.entries({ 'felt wrong': L.felt, save: L.save, bench: L.bench, stick: L.stick, ACTION: L.kick, PASS: L.pass && disc(0, 0, L.pass[2] / 2 / Math.SQRT2), 'KICK marker': L.marker && disc(0, 0, L.marker[2] / 2 / Math.SQRT2) })
          .filter(([, b]) => !b || b[2] / pointPx < 44 || b[3] / pointPx < 44).map(([k, b]) => `${k} ${pts(b)}`);
        check('bounded controls are at least 44 x 44 pt', small.length === 0 && L.action[2] / pointPx >= 88 && L.action[3] / pointPx >= 88,
          `stick ${pts(L.stick)}, ACTION ${pts(L.kick)}, PASS square ${L.pass ? (L.pass[2] / Math.SQRT2 / pointPx).toFixed(0) : '?'}, KICK marker square ${L.marker ? (L.marker[2] / Math.SQRT2 / pointPx).toFixed(0) : '?'}, felt wrong ${pts(L.felt)}, bench ${pts(L.bench)}, right-hand area ${pts(L.action)} pt; too small: ${small.join(' | ') || 'none'}`);

        // The footballer and the ball: in the play area, at a useful size, with room ahead.
        const viewPattern = /ELEVEN view tick=(\d+) .* ahead=([\d.]+)m across=([\d.]+)m ppm=([\d.]+) playerPx=([\d.]+) ballPx=([\d.]+) maxview=([\d.]+)m/;
        const seen = await waitForLog(/ELEVEN trace tick=(\d+) .* view=(\S+) /, 30_000, logs.length);
        const view = (await waitForLog(viewPattern, 30_000, seen ? seen.index : logs.length))?.match ?? null;
        const playerPt = view ? Number(view[5]) / pointPx : NaN, ballPt = view ? Number(view[6]) / pointPx : NaN;
        check('the footballer and the ball are in the play area at a useful size, with room ahead',
          !!seen && seen.match[2] === 'player:in,ball:in' && !!view && playerPt >= 12 && ballPt >= 4 && Number(view[2]) >= 7,
          `${seen ? `view=${seen.match[2]}` : 'no trace'}; ${view ? `footballer ${playerPt.toFixed(1)} pt, ball ${ballPt.toFixed(1)} pt, ${view[4]} px/m, ${view[2]} m ahead, ${view[3]} m across, widest view ${view[7]} m` : 'no view line'}`);
        await page.screenshot({ path: path.join(outDir, `${name}.png`), timeout: 60_000 }).catch(() => {});

        // The bench: the arrangement the screen calls for, with every switch, tab and action in
        // reach without scrolling, and room for the knobs.
        const css = page.viewportSize();
        const wantWide = css.height < 480 && css.width > css.height;
        const fromBench = logs.length;
        const openedL = await openBench(2, [], false);
        const reach = (await waitForLog(/ELEVEN bench reach arranged=(\w+) clipped=(\d+) names=(\S*) list=([\d.]+)x([\d.]+)/, 10_000, fromBench))?.match ?? null;
        const targets = (await waitForLog(/ELEVEN bench targets=(\d+) smallest=([\d.]+) smallestName=(\S+) overflow=(\S+)/, 5_000, fromBench))?.match ?? null;
        check('the bench is arranged for the screen, with every switch, tab and action in reach',
          !!reach && reach[1] === (wantWide ? 'wide' : 'stacked') && reach[2] === '0' && Number(reach[5]) >= 100 && !!targets && Number(targets[2]) >= 44 && targets[4] === 'none',
          `${reach ? reach[0].replace('ELEVEN bench reach ', '') : 'no reach line'}; ${targets ? targets[0].replace('ELEVEN bench ', '') : 'no targets line'}; wanted ${wantWide ? 'wide' : 'stacked'} at ${css.width}x${css.height} pt`);
        await page.screenshot({ path: path.join(outDir, `${name}-bench.png`), timeout: 60_000 }).catch(() => {});

        // Through the bench: the Touch tab, its first knob in view, and a live switch to B and back.
        const fromTouch = logs.length;
        const tabsL = (await waitForLog(/ELEVEN bench open .* tabs=(\S+) /, 1, fromBench))?.match ?? null;
        await press(segments(tabsL?.[1]).Touch, 2);
        const candidatesL = (await waitForLog(/ELEVEN bench candidates (\S+)/, 10_000, fromTouch))?.match ?? null;
        const knob = (await waitForLog(/ELEVEN bench control name=actionHoldSeconds value=\S+ less=(\d+),(\d+) more=(\d+),(\d+)/, 10_000, fromTouch))?.match ?? null;
        await press(segments(candidatesL?.[1]).B, 2);
        const toB = (await waitForLog(switchPattern('split'), 5_000, fromTouch))?.match ?? null;
        await press(segments(candidatesL?.[1]).A, 2);
        const toA = (await waitForLog(switchPattern('action-surface'), 5_000, fromTouch))?.match ?? null;
        const knobIn = !!knob && [[knob[1], knob[2]], [knob[3], knob[4]]].every(([x, y]) => Number(x) > 0 && Number(x) < L.screen[2] && Number(y) > 0 && Number(y) < L.screen[3]);
        check('the Touch tab, its knobs and the live switch work through the bench', !!candidatesL && knobIn && !!toB && toB[4] === '0' && !!toA && toA[4] === '0',
          `${candidatesL ? `candidates ${candidatesL[1]}` : 'no candidates line'}; ${knob ? `first knob − at ${knob[1]},${knob[2]}, + at ${knob[3]},${knob[4]}` : 'no knob line'}; ${toB ? 'to B' : 'no switch to B'}, ${toA ? 'back to A' : 'no switch back'}; since: ${since(fromTouch)}`);
        await closeBench(openedL, 2);

        // The screen turns: the layout follows at once, every control is inside the new safe
        // area, and the run goes on from where it was.
        const tickPattern = /ELEVEN trace tick=(\d+) /;
        const tickBefore = Number((await waitForLog(tickPattern, 20_000, logs.length))?.match[1] ?? NaN);
        const fromTurnL = logs.length;
        await page.setViewportSize({ width: css.height, height: css.width });
        let turned = null;
        for (let from = fromTurnL, until = Date.now() + 30_000; Date.now() < until;) {
          const line = await waitForLog(layoutPattern, Math.max(1, until - Date.now()), from);
          if (!line) break;
          from = line.index + 1;
          if (Number(line.match[1]) !== L.screen[2]) turned = line.match.input;
          if (turned && !(await waitForLog(layoutPattern, 1_500, from))) break;
        }
        const T = turned ? measure(turned) : null;
        const outsideT = T ? controlsInSafe(T) : ['no layout'];
        const tickAfter = Number((await waitForLog(tickPattern, 20_000, logs.length))?.match[1] ?? NaN);
        const viewAfter = (await waitForLog(/ELEVEN trace tick=(\d+) .* view=(\S+) /, 20_000, logs.length))?.match ?? null;
        check('turning the screen re-lays the controls and the run goes on', !!T && outsideT.length === 0 && apart(T.play, T.action) && apart(T.play, T.stickZone) && tickAfter > tickBefore && viewAfter?.[2] === 'player:in,ball:in',
          `${T ? `screen ${T.screen.slice(2).join('x')}, play ${T.play?.join(',')}` : 'no layout for the turned screen'}; outside the safe area: ${outsideT.join(' | ') || 'none'}; tick ${tickBefore} → ${tickAfter}; ${viewAfter ? `view=${viewAfter[2]}` : 'no trace'}`);
        await page.screenshot({ path: path.join(outDir, `${name}-turned.png`), timeout: 60_000 }).catch(() => {});

        notes.layout = `${css.width}x${css.height} pt at ${pointPx} px/pt: safe ${pts(L.safe)}, play area ${pts(L.play)}, stick ${pts(L.stick)}, ACTION ${pts(L.kick)}, right-hand area ${pts(L.action)}; `
          + (view ? `footballer ${playerPt.toFixed(1)} pt, ball ${ballPt.toFixed(1)} pt, ${(Number(view[4]) / pointPx).toFixed(1)} pt/m, ${view[2]} m ahead, ${view[3]} m across; ` : '')
          + `bench ${reach ? `${reach[1]}, knob list ${Number(reach[4]).toFixed(0)}x${Number(reach[5]).toFixed(0)} pt` : '?'}`;
        console.log(`::notice title=layout ${name}::${notes.layout}`);
        const errorsL = logs.filter((l) => /^pageerror|Exception|error CS/i.test(l));
        check('no page errors or exceptions', errorsL.length === 0, errorsL.length ? errorsL.slice(0, 5).join(' | ') : 'none');
        return;
      }

      // Candidate C, ONE FIRE (#77): a tap on FIRE is the quick pass at its release, a hold is a
      // kick along the footballer's heading, harder the longer it is held, and a re-touch shapes.
      if (url.includes('fire=1')) {
        const fire = at(n[6], n[7]);
        const fromC = logs.length;
        const openedC = await openBench(2);
        await press(segments(openedC?.[3]).C, 2);
        const toFire = (await waitForLog(switchPattern('one-fire'), 5_000, fromC))?.match ?? null;
        check('switching to C through the bench changes no state and makes no step', !!toFire && toFire[2] === toFire[3] && toFire[4] === '0',
          toFire ? toFire[0].replace('ELEVEN ', '') : `no switch line${openedC ? '' : ' (the bench did not open)'}; since: ${since(fromC)}`);
        await closeBench(openedC, 2);

        // A tap: the quick pass, at the release.
        await settle();
        const fromTap = logs.length;
        await burst(['touchStart', [pt(fire, 1)]], ['touchEnd', [pt(fire, 1)]]);
        const tapped = (await waitForLog(actionPattern, 20_000, fromTap))?.match ?? null;
        const tapResolved = await waitForLog(/ELEVEN event \d+ QuickPassResolved/, 20_000, fromTap);
        const tapPress = logs.slice(fromTap).find((l) => l.startsWith('ELEVEN press'));
        notes.fireTapLatencyMs = tapped ? Number(tapped[5]) : NaN;
        check('a tap on FIRE asks for the quick pass', !!tapped && tapped[1] === 'tap' && /target=fire/.test(tapPress ?? '') && !!tapResolved,
          `${tapPress ?? 'no press logged'}; ${tapped ? tapped[0].replace('ELEVEN action ', '') : 'no action line'}; ${tapResolved ? 'QuickPassResolved' : 'no QuickPassResolved event'}; since: ${since(fromTap)}`);

        // A hold, released: a kick, harder the longer the hold.
        const holdFire = async (ms, id = 1, held = []) => {
          await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [...held, pt(fire, id)] });
          await sleep(ms);
          // With another finger held, the lifted point is named, as the flicks above do.
          await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: held.length ? [pt(fire, id)] : [] });
        };
        await settle();
        const fromShort = logs.length;
        await holdFire(300);
        const short = (await waitForLog(actionPattern, 20_000, fromShort))?.match ?? null;
        await settle();
        const fromLong = logs.length;
        await holdFire(2_500);
        const long = (await waitForLog(actionPattern, 20_000, fromLong))?.match ?? null;
        // The samples' times follow the runner's frames, a few a second, so a short hold's length
        // isn't exact here: OneFireTests has the curve. A long hold is at full strength, and never
        // weaker than a short one.
        check('a hold released on FIRE asks for a kick, at full strength after a long hold',
          !!short && !!long && short[1] === 'holdkick' && long[1] === 'holdkick' && Number(long[2]) === 1 && Number(long[2]) >= Number(short[2]),
          `${short ? short[0].replace('ELEVEN action ', '') : `no action line; since: ${since(fromShort)}`}; then ${long ? long[0].replace('ELEVEN action ', '') : `no action line; since: ${since(fromLong)}`}`);

        // A re-touch while the hold's kick waits for its tick shapes it, and kicks nothing more.
        await settle();
        const fromShapeC = logs.length;
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [pt(fire, 1)] });
        await sleep(400);
        const again = { x: fire.x, y: fire.y + 10 * pointPx * sy };
        await burst(['touchEnd', []], ['touchStart', [pt(again, 2)]], ['touchMove', [pt({ x: again.x + 40 * pointPx * sx, y: again.y }, 2)]]);
        await sleep(400);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await sleep(300);
        const shapedC = logs.slice(fromShapeC).filter((l) => l.startsWith('ELEVEN press')).map((l) => l.trim().replace('ELEVEN press ', ''));
        const kicksC = logs.slice(fromShapeC).filter((l) => /ELEVEN action gesture=holdkick/.test(l)).length;
        check('a re-touch on FIRE during a kick shapes it and kicks nothing more', shapedC.some((l) => /target=shaping/.test(l)) && kicksC === 1,
          `${shapedC.join(' | ') || 'no presses'}; ${kicksC} kick(s)`);

        // Both thumbs, and the kick's line: the stick, steered at the ball, runs the footballer onto
        // it; a hold on FIRE then kicks it on along his heading, ahead of him.
        await settle();
        const fromRun = logs.length;
        const s0 = at(n[8], n[9]);
        const reach = n[10] * sx;
        const ballPattern = /ELEVEN trace tick=(\d+) player=(-?[\d.]+),(-?[\d.]+) ball=(-?[\d.]+),(-?[\d.]+),/;
        const aimAt = (m) => {
          const dx = Number(m[4]) - Number(m[2]), dy = Number(m[5]) - Number(m[3]);
          const length = Math.hypot(dx, dy) || 1;
          const on = toScreen({ x: dx / length, y: dy / length }, 0);
          return { x: s0.x + on.x * reach, y: s0.y - on.y * reach };
        };
        let s1 = aimAt((await waitForLog(ballPattern, 20_000, logs.length))?.match ?? [0, 0, 10, 0, 20, 1]);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [pt(s0, 1)] });
        for (let i = 1; i <= 5; i++) {
          await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [pt({ x: s0.x + ((s1.x - s0.x) * i) / 5, y: s0.y + ((s1.y - s0.y) * i) / 5 }, 1)] });
          await sleep(20);
        }
        // Steered at the ball on every trace line until he touches it: then he has it.
        let ran = null;
        for (let until = Date.now() + 40_000; Date.now() < until;) {
          if (await waitForLog(/ELEVEN event \d+ Touched 1/, 0, fromRun)) break;
          const line = await waitForLog(ballPattern, Math.max(1, until - Date.now()), logs.length);
          if (!line) break;
          ran = line.match;
          if (await waitForLog(/ELEVEN event \d+ Touched 1/, 0, fromRun)) break;
          s1 = aimAt(line.match);
          await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [pt(s1, 1)] });
        }
        const hasBall = !!(await waitForLog(/ELEVEN event \d+ Touched 1/, 0, fromRun));
        const fromKick = logs.length;
        await holdFire(600, 2, [pt(s1, 1)]);
        const both = (await waitForLog(actionPattern, 20_000, fromKick))?.match ?? null;
        const kicked = await waitForLog(/ELEVEN event \d+ Kicked/, 20_000, fromKick);
        let ahead = null;
        for (let from = kicked ? kicked.index : logs.length, until = Date.now() + 20_000; kicked && Date.now() < until;) {
          const line = await waitForLog(ballPattern, Math.max(1, until - Date.now()), from);
          if (!line) break;
          from = line.index + 1;
          ahead = line.match;
          if (Math.hypot(Number(ahead[4]) - Number(ahead[2]), Number(ahead[5]) - Number(ahead[3])) > 3) break;
        }
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        check('the stick and FIRE work at the same time, and the hold kicks the ball on along the heading',
          hasBall && !!both && both[1] === 'holdkick' && !!kicked && !!ahead && Math.hypot(Number(ahead[4]) - Number(ahead[2]), Number(ahead[5]) - Number(ahead[3])) > 3 && Number(ahead[4]) > Number(ahead[2]),
          `${hasBall ? 'ran onto the ball' : `never touched the ball${ran ? `: player ${ran[2]},${ran[3]} ball ${ran[4]},${ran[5]}` : ''}`}; ${both ? both[0].replace('ELEVEN action ', '') : 'no action line'}; ${kicked ? 'Kicked' : 'no Kicked event'}; ${ahead ? `then player ${ahead[2]},${ahead[3]} ball ${ahead[4]},${ahead[5]}` : 'no trace after'}; since: ${since(fromRun)}`);

        // A FIRE finger held while the controls switch to A and back to C, and the view flips to
        // 180°: lifted afterwards, long past the hold threshold, it makes nothing.
        await settle();
        const fromHeldC = logs.length;
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [pt(fire, 1)] });
        const holdingC = [pt(fire, 1)];
        const reopenedC = await openBench(2, holdingC);
        await press(segments(reopenedC?.[3]).A, 2, holdingC);
        await press(segments(reopenedC?.[3]).C, 2, holdingC);
        await press(segments(reopenedC?.[2])['180'], 2, holdingC);
        await closeBench(reopenedC, 2, holdingC);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await settle();
        const toAfromC = (await waitForLog(switchPattern('action-surface'), 1, fromHeldC))?.match ?? null;
        const backToC = (await waitForLog(switchPattern('one-fire'), 1, fromHeldC))?.match ?? null;
        const heldActionsC = logs.slice(fromHeldC).filter((l) => l.startsWith('ELEVEN action'));
        check('a FIRE finger held across a switch and back makes nothing', !!toAfromC && !!backToC && toAfromC[4] === '0' && backToC[4] === '0' && heldActionsC.length === 0,
          `${toAfromC ? 'to A' : 'no switch to A'}, ${backToC ? 'back to C' : 'no switch back'}; ${heldActionsC.length} action(s) after: ${heldActionsC.join(' | ') || 'none'}; since: ${since(fromHeldC)}`);

        // At 180° the gestures read the same: a hold is still the kick along the heading.
        const fromFlippedC = logs.length;
        await holdFire(500);
        const held180 = (await waitForLog(actionPattern, 20_000, fromFlippedC))?.match ?? null;
        check('at 180°: a hold released on FIRE is still the kick along the heading', !!held180 && held180[1] === 'holdkick',
          held180 ? held180[0].replace('ELEVEN action ', '') : `no action line; since: ${since(fromFlippedC)}`);

        const replayC = (await waitForLog(/ELEVEN recording from=(\d+) steps=(\d+) bytes=(\d+) replay=(\w+)/, 60_000, fromC))?.match ?? null;
        check('the recording across the switches replays to the live state', !!replayC && replayC[4] === 'match', replayC ? replayC[0].replace('ELEVEN ', '') : 'no recording check');
        const errorsC = logs.filter((l) => /^pageerror|Exception|error CS/i.test(l));
        check('no page errors or exceptions', errorsC.length === 0, errorsC.length ? errorsC.slice(0, 5).join(' | ') : 'none');
        return;
      }

      // ?controls=split starts on B: a landscape phone's bench has no room for its Touch tab's
      // knob list until #31, so the portrait pass switches through the bench.
      const startsOnB = url.includes('controls=split');
      const fromSwitch = logs.length;
      if (startsOnB) {
        const started = lastLog(/ELEVEN controls candidate=(\S+) (\S+)/);
        check('?controls=split starts on B', !!started && started[1] === 'split' && started[2].startsWith('split/'),
          started ? started[0].replace('ELEVEN ', '').slice(0, 120) : 'no controls line');
      } else {
        const opened = await openBench(2);
        await press(segments(opened?.[3]).B, 2);
        const toSplit = (await waitForLog(switchPattern('split'), 5_000, fromSwitch))?.match ?? null;
        check('switching to B through the bench changes no state and makes no step', !!toSplit && toSplit[2] === toSplit[3] && toSplit[4] === '0',
          toSplit ? toSplit[0].replace('ELEVEN ', '') : `no switch line${opened ? '' : ' (the bench did not open)'}; since: ${since(fromSwitch)}`);
        await closeBench(opened, 2);
      }

      // PASS: the quick pass at the press, with the press-to-command time.
      await sleep(500);
      const fromPass = logs.length;
      await burst(['touchStart', [pt(pass, 1)]], ['touchEnd', [pt(pass, 1)]]);
      const passed = (await waitForLog(actionPattern, 20_000, fromPass))?.match ?? null;
      const resolved = await waitForLog(/ELEVEN event \d+ QuickPassResolved/, 20_000, fromPass);
      const passPress = logs.slice(fromPass).find((l) => l.startsWith('ELEVEN press'));
      notes.passLatencyMs = passed ? Number(passed[5]) : NaN;
      check('a press on PASS asks for the quick pass', !!passed && passed[1] === 'pass' && /target=pass/.test(passPress ?? '') && !!resolved,
        `${passPress ?? 'no press logged'}; ${passed ? passed[0].replace('ELEVEN action ', '') : 'no action line'}; ${resolved ? 'QuickPassResolved' : 'no QuickPassResolved event'}; since: ${since(fromPass)}`);

      // KICK's immediate flick, up and right on screen: +x and −y at 0° (#72).
      const flickFrom = async (start, dx, dy, holdMs = 0, id = 1, held = []) => {
        const end = { x: start.x + dx * pointPx * sx, y: start.y - dy * pointPx * sy };
        const along = (f) => ({ x: start.x + (end.x - start.x) * f, y: start.y + (end.y - start.y) * f });
        if (holdMs > 0) {
          await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [...held, pt(start, id)] });
          await sleep(holdMs);
          await burst(...[0.34, 0.67, 1].map((f) => ['touchMove', [...held, pt(along(f), id)]]), ['touchEnd', [pt(end, id)]]);
        } else {
          await burst(['touchStart', [...held, pt(start, id)]], ...[0.34, 0.67, 1].map((f) => ['touchMove', [...held, pt(along(f), id)]]), ['touchEnd', [pt(end, id)]]);
        }
      };
      await settle();
      const fromFlick = logs.length;
      await flickFrom(kick, 30, 60);
      const flicked = (await waitForLog(actionPattern, 20_000, fromFlick))?.match ?? null;
      const flickWorld = toWorld({ x: 30, y: 60 }, 0);
      const flickPress = logs.slice(fromFlick).find((l) => l.startsWith('ELEVEN press'));
      check('an immediate flick on KICK is a manual kick along the gesture',
        !!flicked && flicked[1] === 'flick' && /target=kick/.test(flickPress ?? '') && Math.sign(Number(flicked[3])) === Math.sign(flickWorld.x) && Math.sign(Number(flicked[4])) === Math.sign(flickWorld.y),
        `${kickWhere}; ${flickPress ?? 'no press'}; ${flicked ? flicked[0].replace('ELEVEN action ', '') : `no action line; since: ${since(fromFlick)}`}`);

      // Hold → flick: stronger.
      await settle();
      const fromHold = logs.length;
      await flickFrom(kick, 30, 60, 1_000);
      const held = (await waitForLog(actionPattern, 20_000, fromHold))?.match ?? null;
      check('a hold then flick on KICK asks for a stronger kick', !!held && held[1] === 'holdflick' && !!flicked && Number(held[2]) > Number(flicked[2]),
        held ? `${held[0].replace('ELEVEN action ', '')}; the immediate flick asked ${flicked?.[2] ?? '?'}` : `no action line; since: ${since(fromHold)}`);

      // A re-touch on KICK while the flick's kick waits for its tick shapes it, and kicks nothing.
      await settle();
      const fromShape = logs.length;
      const second = { x: kick.x, y: kick.y + 10 * pointPx * sy };
      await burst(
        ['touchStart', [pt(kick, 1)]], ...[0.34, 0.67, 1].map((f) => ['touchMove', [pt({ x: kick.x + 30 * f * pointPx * sx, y: kick.y - 60 * f * pointPx * sy }, 1)]]),
        ['touchEnd', [pt({ x: kick.x + 30 * pointPx * sx, y: kick.y - 60 * pointPx * sy }, 1)]],
        ['touchStart', [pt(second, 2)]], ['touchMove', [pt({ x: second.x + 40 * pointPx * sx, y: second.y }, 2)]]);
      await sleep(400);
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      await sleep(300);
      const shapingPress = logs.slice(fromShape).filter((l) => l.startsWith('ELEVEN press')).map((l) => l.trim().replace('ELEVEN press ', ''));
      const kicks = logs.slice(fromShape).filter((l) => /ELEVEN action gesture=(flick|holdflick)/.test(l)).length;
      check('a re-touch on KICK during a kick shapes it and kicks nothing more', shapingPress.some((l) => /target=shaping/.test(l)) && kicks === 1,
        `${shapingPress.join(' | ') || 'no presses'}; ${kicks} kick(s)`);

      // Both thumbs: the stick held right while the right thumb flicks KICK.
      await settle();
      const fromBoth = logs.length;
      const s0 = at(n[8], n[9]);
      const reach = n[10] * sx;
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [pt(s0, 1)] });
      for (let i = 1; i <= 5; i++) {
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [pt({ x: s0.x + (reach * i) / 5, y: s0.y }, 1)] });
        await sleep(20);
      }
      const s1 = { x: s0.x + reach, y: s0.y };
      const beforeBoth = (await waitForLog(tracePattern, 10_000, logs.length))?.match ?? null;
      await flickFrom(kick, 30, 60, 0, 2, [pt(s1, 1)]);
      const both = (await waitForLog(actionPattern, 20_000, fromBoth))?.match ?? null;
      const afterBoth = (await waitForLog(tracePattern, 10_000, logs.length))?.match ?? null;
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      const moved = beforeBoth && afterBoth ? Math.hypot(Number(afterBoth[2]) - Number(beforeBoth[2]), Number(afterBoth[3]) - Number(beforeBoth[3])) : NaN;
      check('the stick and KICK work at the same time', !!both && both[1] === 'flick' && moved > 1,
        `${both ? both[0].replace('ELEVEN action ', '') : 'no action line'}; player moved ${Number.isFinite(moved) ? moved.toFixed(2) : 'unknown'} m; since: ${since(fromBoth)}`);

      // A KICK finger held while the controls switch to A and back (through the bench, so not
      // when the pass starts on B), and the view flips to 180°: lifted afterwards, it makes nothing.
      await sleep(1_500);
      const fromHeld = logs.length;
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [pt(kick, 1)] });
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [pt({ x: kick.x + 20 * pointPx * sx, y: kick.y - 40 * pointPx * sy }, 1)] });
      const holding = [pt({ x: kick.x + 20 * pointPx * sx, y: kick.y - 40 * pointPx * sy }, 1)];
      const reopened = await openBench(2, holding, !startsOnB);
      if (!startsOnB) {
        await press(segments(reopened?.[3]).A, 2, holding);
        await press(segments(reopened?.[3]).B, 2, holding);
      }
      await press(segments(reopened?.[2])['180'], 2, holding);
      await closeBench(reopened, 2, holding);
      await burst(['touchMove', [pt({ x: kick.x + 30 * pointPx * sx, y: kick.y - 60 * pointPx * sy }, 1)]], ['touchEnd', []]);
      await sleep(1_000);
      const toA = (await waitForLog(switchPattern('action-surface'), 1, fromHeld))?.match ?? null;
      const backToB = (await waitForLog(switchPattern('split'), 1, fromHeld))?.match ?? null;
      const flipped = (await waitForLog(/ELEVEN view flipped to=180 tick=\d+ before=([0-9a-f]{16}) after=([0-9a-f]{16}) steps=(\d+)/, 1, fromHeld))?.match ?? null;
      const heldActions = logs.slice(fromHeld).filter((l) => l.startsWith('ELEVEN action'));
      if (startsOnB) {
        check('a KICK finger held across the 180° flip makes nothing', !!flipped && flipped[3] === '0' && heldActions.length === 0,
          `${flipped ? 'flipped' : 'no flip'}; ${heldActions.length} action(s) after: ${heldActions.join(' | ') || 'none'}; since: ${since(fromHeld)}`);
      } else {
        check('a KICK finger held across a switch and back makes nothing', !!toA && !!backToB && toA[4] === '0' && backToB[4] === '0' && heldActions.length === 0,
          `${toA ? 'to A' : 'no switch to A'}, ${backToB ? 'back to B' : 'no switch back'}; ${heldActions.length} action(s) after: ${heldActions.join(' | ') || 'none'}; since: ${since(fromHeld)}`);
      }

      // At 180°: a flick up the screen on KICK kicks up-field, toward −x.
      await sleep(1_500);
      const fromFlipped = logs.length;
      await flickFrom(kick, 0, 60);
      const up180 = (await waitForLog(actionPattern, 20_000, fromFlipped))?.match ?? null;
      // A flick or a hold → flick: on a slow runner the samples' times can spread past the hold
      // threshold. The view's mapping is what this checks.
      check('at 180°: a flick up the screen on KICK kicks up-field', !!up180 && /^(flick|holdflick)$/.test(up180[1]) && Number(up180[3]) < 0 && Math.abs(Number(up180[4])) < Math.abs(Number(up180[3])) * 0.3,
        up180 ? up180[0].replace('ELEVEN action ', '') : `no action line; since: ${since(fromFlipped)}`);

      const replay = (await waitForLog(/ELEVEN recording from=(\d+) steps=(\d+) bytes=(\d+) replay=(\w+)/, 60_000, fromSwitch))?.match ?? null;
      check('the recording across the switches replays to the live state', !!replay && replay[4] === 'match', replay ? replay[0].replace('ELEVEN ', '') : 'no recording check');

      // The screen turns with a finger held on KICK (#76): the host cancels gestures in progress,
      // so the finger, moved and lifted on the new screen, kicks nothing. Last, in the portrait
      // pass: every place above was read from the layout before the turn.
      if (!startsOnB) {
        await settle();
        const fromTurn = logs.length;
        const size = page.viewportSize();
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [pt(kick, 1)] });
        await sleep(300);
        const turnPress = logs.slice(fromTurn).find((l) => l.startsWith('ELEVEN press'));
        await page.setViewportSize({ width: size.height, height: size.width });
        const cancelled = (await waitForLog(/ELEVEN gestures cancelled reason=resize screen=(\d+)x(\d+)/, 20_000, fromTurn))?.match ?? null;
        // A flick's worth of travel on the turned screen, then the lift.
        const turnedTo = { x: size.height * 0.5, y: size.width * 0.5 };
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [pt(turnedTo, 1)] }).catch(() => {});
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] }).catch(() => {});
        await settle();
        const turnActions = logs.slice(fromTurn).filter((l) => l.startsWith('ELEVEN action'));
        check('a KICK finger held while the screen turns makes nothing', /target=kick/.test(turnPress ?? '') && !!cancelled && turnActions.length === 0,
          `${turnPress ?? 'no press'}; ${cancelled ? cancelled[0].replace('ELEVEN ', '') : 'no cancel line'}; ${turnActions.length} action(s) after: ${turnActions.join(' | ') || 'none'}`);
      }

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
    // Released before the screenshot: a large viewport's screenshot can take seconds, and a held
    // stick kept running the player through it, past the goal line on the phone (#75's first run).
    await sleep(2500);
    if (useTouch) await touch('touchEnd');
    else await page.mouse.up();
    await page.screenshot({ path: path.join(outDir, `${name}-playing.png`) });
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
      let cut = null;
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

          // The cut with the ball (#75): a simulated second on along the reversal's line, the stick
          // turned a quarter turn on screen. The footballer touches the ball as the cut starts.
          if (out) {
            await waitForLog(ballPattern, 10_000, logs.length);
            const fromCut = logs.length;
            const side = { x: origin.x - (back.y - origin.y), y: origin.y + (back.x - origin.x) };
            if (useTouch) await touch('touchMove', side);
            else await page.mouse.move(side.x, side.y);
            cut = await waitForLog(/ELEVEN event (\d+) Cut 1/, 10_000, fromCut);
          }
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
      notes.cut = cut ? `cut touch at tick ${cut.match[1]}` : 'none';
      check('cutting with the ball touches it as the cut starts', !!cut,
        turned?.out ? notes.cut : 'no reversal to cut from');
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

    // Named presets (#46), on the desktop, where there is a keyboard to type a name with: the
    // running candidate saved under a name, TECHNICAL REFERENCE loaded and applied, then the
    // preset recalled and applied, hashed as in .NET. Deleted after the reload, which it survives.
    const presetsPattern = /ELEVEN bench presets count=(\d+) unread=(\d+) name=(\S+) save=(\S+) items=(\S*)/;
    const openPresets = async () => {
      const from = logs.length;
      const layoutNow = benchPattern.exec(lastLog(layoutPattern)?.input ?? '');
      if (layoutNow) await tap(centre(...layoutNow.slice(1, 5)));
      const open = await waitForLog(/ELEVEN bench open knobs=(\d+) group=(\S+) tabs=(\S+) apply=(\S+) revert=(\S+) close=(\S+)/, 8_000, from);
      const tabsNow = Object.fromEntries((open?.match[3] ?? '').split(';').filter(Boolean).map((t) => t.split('=')));
      if (tabsNow.Presets && open.match[2] !== 'Presets') await tap(at(...point(tabsNow.Presets)));
      return { open: open?.match ?? null, from };
    };
    // The latest presets line once the list has settled, with each preset's Load and Delete places.
    const presetsLine = async (from) => {
      await waitForLog(presetsPattern, 8_000, from);
      await sleep(800);
      const lines = logs.slice(from).map((l) => presetsPattern.exec(l)).filter(Boolean);
      const line = lines[lines.length - 1] ?? null;
      const items = Object.fromEntries((line?.[5] ?? '').split(';').filter(Boolean).map((i) => i.split(':')).map(([n, load, del]) => [n, { load, del }]));
      return { line, items };
    };
    if (name === 'desktop') {
      const first = await openPresets();
      let shelf = await presetsLine(first.from);
      // Save: a name the build refuses, then one it takes.
      let fromSave = logs.length;
      if (shelf.line) await tap(at(...point(shelf.line[3])));
      await page.keyboard.type('bad/name');
      if (shelf.line) await tap(at(...point(shelf.line[4])));
      const refused = await waitForLog(/ELEVEN bench preset refused (.*)/, 5_000, fromSave);
      for (let i = 0; i < 'bad/name'.length; i++) await page.keyboard.press('Backspace');
      fromSave = logs.length;
      await page.keyboard.type('Smoke 1');
      if (shelf.line) await tap(at(...point(shelf.line[4])));
      const saved = await waitForLog(/ELEVEN bench preset saved name=(\S+) replaced=(\w+) count=(\d+) config=([0-9a-f]{16})/, 5_000, fromSave);
      check('the running candidate is saved under a name, and a name that can\'t be used is refused',
        !!shelf.line && shelf.line[1] === '0' && !!refused && /letters, digits/.test(refused.match[1]) && !!saved && saved.match[1] === 'Smoke_1' && saved.match[2] === 'no' && saved.match[3] === '1' && saved.match[4] === expected?.[1],
        `${shelf.line ? `${shelf.line[1]} preset(s) at first` : 'no presets line'}; ${refused ? `refused: ${refused.match[1].trim()}` : 'the bad name was not refused'}; ${saved ? saved.match[0].replace('ELEVEN bench ', '') : 'no saved line'}; .NET config ${expected?.[1] ?? '?'}`);

      // TECHNICAL REFERENCE: loaded as pending changes, then applied.
      shelf = await presetsLine(fromSave);
      let fromLoad = logs.length;
      if (shelf.items.TECHNICAL_REFERENCE) await tap(at(...point(shelf.items.TECHNICAL_REFERENCE.load)));
      const loadedReference = await waitForLog(/ELEVEN bench preset loaded name=(\S+) pending=(\d+) controls=(\S+) model=\S+ config=([0-9a-f]{16})/, 5_000, fromLoad);
      if (first.open) await tap(at(...point(first.open[4])));
      const appliedReference = await waitForLog(/ELEVEN bench applied changes=(\d+) preset=(\S+) config=([0-9a-f]{16}) start=([0-9a-f]{16})/, 5_000, fromLoad);
      check('TECHNICAL REFERENCE restores the reference values',
        !!loadedReference && loadedReference.match[1] === 'TECHNICAL_REFERENCE' && loadedReference.match[2] === '2' && !!appliedReference && appliedReference.match[1] === '0'
          && appliedReference.match[2] === 'technical-reference' && appliedReference.match[3] === knobsLine?.[3] && appliedReference.match[4] === knobsLine?.[4],
        `${loadedReference ? loadedReference.match[0].replace('ELEVEN bench ', '') : 'no loaded line'}; ${appliedReference ? appliedReference.match[0].replace('ELEVEN bench ', '') : 'no applied line'}; at boot config ${knobsLine?.[3]} start ${knobsLine?.[4]}`);

      // The preset recalled: its values pending, then applied, hashed as in .NET, and named.
      const second = await openPresets();
      shelf = await presetsLine(second.from);
      fromLoad = logs.length;
      if (shelf.items.Smoke_1) await tap(at(...point(shelf.items.Smoke_1.load)));
      const loadedPreset = await waitForLog(/ELEVEN bench preset loaded name=(\S+) pending=(\d+) controls=(\S+) model=\S+ config=([0-9a-f]{16})/, 5_000, fromLoad);
      if (second.open) await tap(at(...point(second.open[4])));
      const appliedPreset = await waitForLog(/ELEVEN bench applied changes=(\d+) preset=(\S+) config=([0-9a-f]{16}) start=([0-9a-f]{16})/, 5_000, fromLoad);
      check('a saved preset is recalled and applied, hashed as in .NET, and the run is named after it',
        !!loadedPreset && loadedPreset.match[1] === 'Smoke_1' && loadedPreset.match[2] === '2' && !!appliedPreset && appliedPreset.match[1] === '2'
          && appliedPreset.match[2] === 'Smoke_1' && appliedPreset.match[3] === expected?.[1] && appliedPreset.match[4] === expected?.[2],
        `${loadedPreset ? loadedPreset.match[0].replace('ELEVEN bench ', '') : 'no loaded line'}; ${appliedPreset ? appliedPreset.match[0].replace('ELEVEN bench ', '') : 'no applied line'}; .NET config ${expected?.[1]} start ${expected?.[2]}`);
      await waitForLog(/ELEVEN bench closed/, 3_000, fromLoad);
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

      // The preset saved before the reload is still on the device (#46), and can be deleted.
      const kept = await openPresets();
      const shelfAfter = await presetsLine(kept.from);
      const fromDelete = logs.length;
      if (shelfAfter.items.Smoke_1 && shelfAfter.items.Smoke_1.del !== 'none') await tap(at(...point(shelfAfter.items.Smoke_1.del)));
      const deleted = await waitForLog(/ELEVEN bench preset deleted name=(\S+) count=(\d+)/, 5_000, fromDelete);
      check('a saved preset is still there after a reload, and can be deleted',
        shelfAfter.line?.[1] === '1' && !!shelfAfter.items.Smoke_1 && !!deleted && deleted.match[1] === 'Smoke_1' && deleted.match[2] === '0',
        `${shelfAfter.line ? `${shelfAfter.line[1]} preset(s) after the reload: ${Object.keys(shelfAfter.items).join(', ')}` : 'no presets line'}; ${deleted ? deleted.match[0].replace('ELEVEN bench ', '') : 'no deleted line'}`);
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
  // Candidate B, SPLIT (#76): PASS and KICK on an iPhone, portrait and landscape.
  await runPass(browser, `${base}?trace=1&split=1`, 'split-phone', phone, true);
  await runPass(browser, `${base}?trace=1&split=1&controls=split`, 'split-phone-landscape', phoneLandscape, true);
  // Candidate C, ONE FIRE (#77), on an iPhone in portrait.
  await runPass(browser, `${base}?trace=1&fire=1`, 'fire-phone', phone, true);

  // The layout matrix (#31): phone and tablet sizes in both orientations, a compact phone, a large
  // tablet, and a phone with a notch's and a home indicator's safe-area insets. At 2 px per pt at
  // most: the layout is in points, and the runner draws a large canvas slowly.
  const sized = (width, height, scale = 2) => ({ ...phone, viewport: { width, height }, screen: { width, height }, deviceScaleFactor: scale });
  await runPass(browser, `${base}?trace=1&layout=1`, 'layout-phone-portrait', sized(390, 664), true);
  await runPass(browser, `${base}?trace=1&layout=1`, 'layout-phone-landscape', sized(750, 342), true);
  await runPass(browser, `${base}?trace=1&layout=1`, 'layout-phone-compact', sized(320, 568), true);
  await runPass(browser, `${base}?trace=1&layout=1&safe=47,0,34,0`, 'layout-phone-portrait-notch', sized(390, 844), true);
  await runPass(browser, `${base}?trace=1&layout=1&safe=0,47,21,47`, 'layout-phone-landscape-notch', sized(844, 390), true);
  await runPass(browser, `${base}?trace=1&layout=1`, 'layout-tablet-portrait', sized(810, 1080, 1), true);
  await runPass(browser, `${base}?trace=1&layout=1`, 'layout-tablet-landscape', sized(1080, 810, 1), true);
  await runPass(browser, `${base}?trace=1&layout=1`, 'layout-tablet-large-portrait', sized(1024, 1366, 1), true);
  await runPass(browser, `${base}?trace=1&layout=1`, 'layout-tablet-large-landscape', sized(1366, 1024, 1), true);

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
