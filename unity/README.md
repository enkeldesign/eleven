# Unity host (spike)

A thin Unity 6 host around the engine-free simulation, for the engine spike ([#3](https://github.com/enkeldesign/eleven/issues/3)). Nothing in it is made by hand in the Editor's interface: scene, settings and visuals come from code.

## Layout

| Path | What it is |
| --- | --- |
| `ProjectSettings/ProjectVersion.txt` | Unity version (6000.3 LTS). The only committed settings file |
| `Packages/manifest.json` | Packages. The simulation and its harness are local packages pointing at `../../src/Eleven.Simulation` and `../../src/Eleven.Simulation.Harness`, the same source files the .NET build compiles |
| `Assets/Eleven.Host/` | The host: fixed-step loop, touch adapter, view and HUD (`Eleven.Host.asmdef`) |
| `Assets/Eleven.Host/Editor/` | Batch-mode build script and project settings as code (`Eleven.Host.Editor.asmdef`) |
| `WebPlayer/ServiceWorker.js` | The player's service worker, written over the PWA template's by the build script |
| `WebPlayer/icons/`, `WebPlayer/manifest.webmanifest` | The ELEVEN icon (`eleven.svg`, Erik's source) and the PNGs rendered from it by `icons/render.mjs`, with the web app manifest that lists them. The build script installs them over the template's Unity icons |
| `ci/smoke.mjs` | Headless Chromium smoke test run by CI against the Web build |

The simulation's assembly definition (`src/Eleven.Simulation/Eleven.Simulation.asmdef`) sets `noEngineReferences`, so Unity refuses any engine reference in it (INV-2). Unity writes `.meta` files into `src/` when it imports those packages. They are git-ignored, because nothing refers to them by GUID.

## How it runs

- `HostBootstrap` creates the host after the (empty) scene loads. Camera, pitch, dot and ball are built at runtime from generated textures.
- `HostLoop` accumulates real time and spends it in whole simulation ticks, interpolating between the last two ticks for drawing. A frame longer than 0.25 s (backgrounded tab, pause, stall) is dropped, not caught up, so the simulation never advances for unseen time. At most 8 ticks run per frame.
- `TouchAdapter` reads touches (and the mouse, as one finger on desktop) through the Input System. It produces owned commands for controller 1: a floating stick in the left half and a kick button bottom right.
- **The UI around the match** (`Hud`) is UI Toolkit, from the visual system's recipes (`UiRoot`, `UiKit`; [docs/ui/visual-system.md](../docs/ui/visual-system.md)), built entirely in code. Presses on it never reach play. Only the match controls (stick and kick, #28's) are still drawn with IMGUI.
  - **The build line** is HUD data: the build id (CI run number and commit), the page's load time and dropped frames over the last minute of play (for the device session, #5). Its slots are sized for their widest values, so it never changes size.
  - **Reset**, and **Run scenario**, which runs the issue 1 scenario inside the build and shows its time, digest and final hash in a HUD panel.
  - **Every placed element measures what it needs** after layout, and `HudLayout` lays out around it, so enlarged text widens actions and grows panels rather than overflowing them.
- **THIS FELT WRONG** (#44, #53) is a Unity action just above the kick button, where the right thumb is.
  - **A press** takes the capture there and then, so its window ends where the player pressed. A Unity sheet then asks for an optional note, and play waits until it closes. **Keep note** (or Enter) keeps the note; **No note** (or Escape) keeps the capture without one.
  - **The capture** is the last ten seconds (`FeelRecorder`/`FeelCapture`, [determinism.md](../docs/simulation/determinism.md#feel-captures)). The page keeps it in `localStorage` (the last five), and the status line announces it.
  - **SAVE CAPTURE**, beside or above THIS FELT WRONG, shares or downloads it as `.elfc`, for `eleven-sim replay`. Nothing leaves the device unless it is saved. Storage and share/download are browser services with no UI (`Plugins/ElevenPage.jslib`). The host tells the page where SAVE CAPTURE is, and the page's own release handler starts the share for a press there, so the browser's user-gesture requirement holds whatever the frame timing.
- **The keyboard:** the player keeps every key (`WebGLInput.captureAllKeyboardInput` at its default). Tab moves Unity's focus, with the two-colour focus ring, and Enter activates. The note's text field raises the phone's keyboard (`WebGLInput.mobileKeyboardSupport`).
- URL options for testing:
  - `?autorun` runs the scenario at start.
  - `?trace` logs state every 60 ticks and every event.
  - `?specimen` (or `?specimen=dark`) shows the visual system's specimen over the match.
  - `?textscale=1.5` enlarges UI text.
  - `?ui=empty` builds an empty UI Toolkit panel, for the frame-cost passes.
  - `?noui` builds no UI, which the smoke test uses to measure the UI's frame cost. The smoke test replays each capture with .NET from the bytes kept in `localStorage` and, on desktop, SAVE CAPTURE's download (#50).

## Building

CI (`.github/workflows/unity.yml`) builds the Web player with GameCI on every PR and on `main`, then smoke-tests it in headless Chromium. The test drives the stick and buttons with the mouse, fails unless the in-build scenario digest equals the .NET hash log's (ADR-0002), and checks the rendered colours. Results appear as check annotations; the player and screenshots are uploaded as artifacts.

Locally, with Unity 6000.3 installed:

```
Unity -batchmode -quit -projectPath unity -buildTarget WebGL \
  -executeMethod Eleven.Host.Editor.BuildScript.BuildWeb -customBuildPath ../build/WebGL/WebGL
```

`ProjectSetup.Apply` sets every player setting the build depends on, including Input System–only input handling, before each build. Settings files Unity generates are not committed.

## One-time setup: Unity licence for CI

GameCI activates Unity in CI from three repository secrets. They only need setting once, or again if the licence or the Unity account changes.

1. Install Unity Hub on any computer and sign in with the Unity account the project uses. A Personal licence is enough.
2. In Unity Hub, open **Preferences → Licences**, then **Add → Get a free personal licence**. Hub writes a licence file, `Unity_lic.ulf`:
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`
3. In the GitHub repository, open **Settings → Secrets and variables → Actions → New repository secret** and add:
   - `UNITY_LICENSE`: the entire contents of `Unity_lic.ulf`
   - `UNITY_EMAIL`: the Unity account's email address
   - `UNITY_PASSWORD`: the Unity account's password
4. Push any commit. The **Unity host / Build Web player** check should pass. Its annotation reports the Unity version and build time.

A licence failure shows as the annotation "No build report: Unity stopped before the build script ran".

## Playable links (GitHub Pages)

The **Deploy playable link** job publishes every build:
- `main`: <https://enkeldesign.github.io/eleven/>
- each PR: `https://enkeldesign.github.io/eleven/pr/<number>/`

The link is posted as a PR comment, which is kept up to date on every push.

The whole site lives on the orphan `site` branch (one commit, force-pushed), because each Pages deployment replaces the entire site. Previews of closed PRs are removed on the next deploy.

Repository settings this needs, all set once:
- **Settings → Pages → Source:** GitHub Actions.
- **Settings → Environments → github-pages → Deployment branches and tags:** No restriction. PR runs deploy from `refs/pull/<n>/merge`, which no branch pattern matches.

Builds use Unity's PWA template, so a link can be added to a phone's home screen. Files are Gzip-compressed with Unity's JavaScript decompression fallback, because Pages can't send `Content-Encoding` headers for them.

The template's service worker answers from its cache first and never drops old caches. Unity's file names are the same in every build, so a phone kept running the first build it saw ([#15](https://github.com/enkeldesign/eleven/issues/15)). The build script replaces it with `WebPlayer/ServiceWorker.js`:

- It goes to the network first, revalidating, so a reload shows the latest deploy.
- It falls back to its cache only offline.
- Each build gets its own cache per link, and older ones are deleted when it takes over.
- A phone still on the template's worker picks up the new one on its first reload and shows the latest build from the second.
