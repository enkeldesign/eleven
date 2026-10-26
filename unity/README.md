# Unity host (spike)

A thin Unity 6 host around the engine-free simulation, for the engine spike ([#3](https://github.com/enkeldesign/eleven/issues/3)). Nothing in it is made by hand in the Editor's interface: scene, settings and visuals come from code.

## Layout

| Path | What it is |
| --- | --- |
| `ProjectSettings/ProjectVersion.txt` | Unity version (6000.3 LTS). The only committed settings file |
| `Packages/manifest.json` | Packages. The simulation and its harness are local packages pointing at `../../src/Eleven.Simulation` and `../../src/Eleven.Simulation.Harness`, the same source files the .NET build compiles |
| `Assets/Eleven.Host/` | The host: fixed-step loop, touch adapter, view and HUD (`Eleven.Host.asmdef`) |
| `Assets/Eleven.Host/Editor/` | Batch-mode build script and project settings as code (`Eleven.Host.Editor.asmdef`) |
| `ci/smoke.mjs` | Headless Chromium smoke test run by CI against the Web build |

The simulation's assembly definition (`src/Eleven.Simulation/Eleven.Simulation.asmdef`) sets `noEngineReferences`, so Unity refuses any engine reference in it (INV-2). Unity writes `.meta` files into `src/` when it imports those packages. They are git-ignored, because nothing refers to them by GUID.

## How it runs

- `HostBootstrap` creates the host after the (empty) scene loads. Camera, pitch, dot and ball are built at runtime from generated textures.
- `HostLoop` accumulates real time and spends it in whole simulation ticks, interpolating between the last two ticks for drawing. A frame longer than 0.25 s (backgrounded tab, pause, stall) is dropped, not caught up, so the simulation never advances for unseen time. At most 8 ticks run per frame.
- `TouchAdapter` reads touches (and the mouse, as one finger on desktop) through the Input System. It produces owned commands for controller 1: a floating stick in the left half and a kick button bottom right.
- On screen: the build id (CI run number and commit), **Reset**, **Run scenario** (runs the issue 1 scenario inside the build and shows its digest and final hash), and **Maths** (switches between the `Portable` and `SystemMath` backends).
- URL options for testing: `?autorun` runs the scenario with both backends at start; `?trace` logs state every 60 ticks and every event.

## Building

CI (`.github/workflows/unity.yml`) builds the Web player with GameCI on every PR and on `main`, then smoke-tests it in headless Chromium. The test drives the stick and buttons with the mouse, compares the in-build scenario digests with the .NET hash logs, and checks the rendered colours. Results appear as check annotations; the player and screenshots are uploaded as artifacts.

Locally, with Unity 6000.3 installed:

```
Unity -batchmode -quit -projectPath unity -buildTarget WebGL \
  -executeMethod Eleven.Host.Editor.BuildScript.BuildWeb -customBuildPath ../build/WebGL/WebGL
```

`ProjectSetup.Apply` sets every player setting the build depends on, including Input System–only input handling, before each build. Settings files Unity generates are not committed.
