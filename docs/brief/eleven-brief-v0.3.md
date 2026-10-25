# ELEVEN brief v0.3

25 Oct 2026 · Erik

ELEVEN is a retro-style football game, initially targeting iPhone and iPad. This version supersedes v0.2: the platform becomes a hypothesis to test, Unity 6 is the leading candidate, decision rights are settled, and the brief adds a collaboration model and ELEVEN's place in the Enkel Design family. The next step is the engine spike, then Slice 1.

## Product vision

The target reaction from an experienced SWOS player: “Finally. I found the SWOS of mobile games — and it is even better than SWOS.” iPhone and iPad come first, and that is where the football must feel exceptional; nothing about the product is confined to them.

- **Football first.** Moving, receiving, passing, shooting, tackling, positioning and scoring must be compelling with no career layer at all.
- **References, not templates.** SWOS ’96/’97 for immediacy, readability, skill ceiling and career cohesion. Super Soccer Champs ’22 for how football works on a touchscreen. We prototype alternatives rather than clone either.
- **Small controls, deep play.** Depth comes from ball behaviour, timing, positioning, player attributes, decisions, tactics and opposition. Easy to understand, very hard to master.
- **Readable, not realistic.** Top-down, no minimap for now. Abstract enough to avoid uncanny realism, detailed enough to read a header, volley, bicycle kick, tackle or save at a glance.
- **Fair, intelligent opponents.** The CPU plays the same game through the same kind of perception limits. Its strength comes from decisions, not bonuses, and teams and managers feel recognisably different.
- **Perceptible attributes.** A fast winger and a slow centre-back feel different within seconds.
- **Compact, consequential management.** Playing and managing are one game. A few systems with strong consequences beat many shallow screens. Delegation is possible, but assistants are as good as their skill.
- **A complete offline game.** Football, career, saves and progression never need a network.
- **Clearly Enkel Design, clearly football.** Family resemblance with other Enkel Design products, with its own football identity.
- **Short matches.** Testing starts at two 3-minute halves; length is a tuning value.

## Product and platform principles

Seven principles that shape every technical decision. They describe what we protect, not what we build now.

1. **Touch-first, not mobile-bound.** Touch is the first input method, not an architectural dependency. Nothing assumes a particular platform, screen shape or input device.
2. **On-screen touch only.** No accelerometer, tilt or other device sensors in the control concept.
3. **Input-independent simulation.** Touch, keyboard, gamepad and remote input are adapters that produce owned commands. The simulation knows nothing about touchscreens, browsers, iPhones, keyboards, gamepads, Steam or Switch. A new input device is a new adapter, not a rewrite.
4. **Design for foreseeable extensions.** We don't build future features early, but we avoid closing obvious paths: PC and Steam, consoles, other screen sizes and orientations, keyboard and gamepad, local and online multiplayer, native distribution, cloud sync. Decision rule: when two solutions are otherwise comparable, prefer the one that keeps a foreseeable option open at low cost. We never pay large costs today for hypothetical features.
5. **Multiplayer preserved, not built.** No networking in v1 and no transport (Bluetooth or otherwise) baked in. Deterministic state, snapshots, controller ownership and explicit commands keep local, online, prediction and rollback approaches open. Offline single-player never depends on multiplayer infrastructure.
6. **Offline-first.** ELEVEN is a complete single-player game. Core football, career, saves and progression never depend on a network service; online services may only add to it. This holds on every platform. For the web build, the eventual acceptance test is: install → disconnect completely → cold-launch from the home screen → play normally.
7. **Accessible by design, with a realistic gameplay scope.** Menus, settings, text, colour use, readable information, remapping, motion options and feedback are designed accessibly from the start. Unlike TURN's Drive by Ear, the match itself is not required to be playable non-visually, and the football won't be compromised for a requirement we haven't made.

## Architectural invariants

These hold from the first commit, whichever engine wins. Each is enforced mechanically where possible. Superseding one takes an ADR that sets out the evidence and migration consequences, approved by Erik.

| # | Invariant | Enforced by |
| --- | --- | --- |
| 1 | **One authoritative football simulation.** Everything else constructs, sequences, observes, measures or presents it. The only permitted approximation is for matches nobody plays, and it must stay calibrated against this simulation. | Dependency rules in CI; no football logic outside the simulation package |
| 2 | **Engine-free simulation.** The simulation depends on no engine, rendering, physics, input-device or platform API. Under Unity: no GameObjects, Transform, Rigidbody or PhysX. | The simulation builds and tests outside the engine; in Unity, an assembly that forbids engine references |
| 3 | **Plain-data state, snapshot/restore first-class.** Anything that can affect the next tick lives in authoritative state or immutable configured data — never in rendering, UI, animation or closures. | Round-trip test: snapshot mid-match, restore, continue, compare per-tick hashes with an uninterrupted run |
| 4 | **Same-build, cross-platform determinism.** The same state, config, seeds and commands give the same result on every shipped runtime and headless. Cross-version replay is not a goal. | Per-tick state hashes compared between the CI runtime and every shipped runtime, in CI where possible and on real devices otherwise |
| 5 | **Config is separate from state, hashed and versioned.** Simulation behaviour is code version plus config hash. | Every snapshot and trace records both |
| 6 | **Independent, counter-based randomness.** Each consumer draws from its own stream. | Test that an extra draw in one stream leaves the others unchanged |
| 7 | **States migrate; input traces are version-specific and may expire.** | Schema version in every snapshot; migration tests for stored scenarios |
| 8 | **Set pieces are restart → simulation → termination rule, run by a sequencer.** Modes never contain football mechanics. | Restarts and termination rules are functions over state; review rule |
| 9 | **Perception boundary.** AI receives an Observation, never human commands or unrestricted internals. | Observation type has no command or raw-state fields; AI code can't reach simulation internals |
| 10 | **Input adapters produce owned commands.** Every command carries a controller id, is recorded before assistance, and assistance runs inside the simulation. Nothing assumes exactly one human or one input device. | Commands keyed by controller; replay test with assistance enabled |
| 11 | **Headless by default.** The full simulation runs without rendering or an engine. | CI plays headless matches on every build |
| 12 | **Gameplay timing lives in the simulation.** Kick wind-up, turns and tackle recovery are state; animation only picks frames. | State carries action phases; review rule |
| 13 | **AI predicts with the simulation's own functions.** Trajectories and time-to-reach come from shared code, never hard-coded physics. | Shared prediction module; review rule |
| 14 | **Consumers read a stable match-result contract.** Career and other consumers never depend on internal events. | Contract type; dependency rule |
| 15 | **No network on the single-player path.** | Single-player tests run with networking disabled; offline acceptance test per platform |

```
 ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐    ┌────────────────────────┐
 ╎ Input adapters        ╎──► │ Commands               │◄──── AI decisions ────┐
 ╎ Touch now; keyboard,  ╎    │ Owned by a controller, │                       │
 ╎ gamepad, network later╎    │ human or AI            │                       │
 └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘    └───────────┬────────────┘                       │
                                          │ commands in                        │
 ┌───────────────────────┐    ┌───────────▼────────────┐    ┌──────────────────┴─────┐
 │ Construct             │──► │ Simulation             │──► │ Observe                │
 │ Restarts, scenarios,  │    │ Authoritative          │    │ Perception →           │
 │ snapshots             │    │ plain-data state       │    │ Observation for AI     │
 └───────────────────────┘    │ Hashed config,         │    │ and HUD cues           │
 ┌───────────────────────┐    │ fixed step             │    └────────────────────────┘
 │ Sequence              │◄──►│ Deterministic,         │    ┌────────────────────────┐
 │ Modes: match,         │    │ engine-free            │──► │ Measure                │
 │ shootout, practice    │    │                        │    │ Metrics for AI eval,   │
 └───────────────────────┘    └───────────┬────────────┘    │ balance, calibration   │
                                          │ reads state     └────────────────────────┘
                         ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌▼╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐
                         ╎ Present                         ╎
                         ╎ Rendering, animation, audio, UI ╎
                         ╎ Reads state, never writes       ╎
                         └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘

 Solid: engine-free, runs headless outside any engine.   Dashed: engine host layer (Unity 6 if chosen).
```

Swapping the engine replaces only the dashed boxes; the simulation and its tests carry over unchanged.

## Decisions made

These are settled for now and become ADRs in the repository. The platform is deliberately absent: it is now a hypothesis (see Engine).

| Decision | Notes |
| --- | --- |
| Name: ELEVEN | No name or identity that confines it to mobile. |
| Repository first | ELEVEN is its own repository, alongside webb and ai. The engine spike runs inside it, so evidence, CI, ADRs and useful code survive whichever direction wins. No throwaway pre-repo prototype. |
| No production environment yet | Deployment to enkel.design/eleven is a later concern. |
| Decision rights | Erik owns product decisions and player experience. Opus owns implementation and routine architecture within the invariants. Invariants are superseded only by an ADR with evidence and migration consequences, approved by Erik. SOL shapes product, design, acceptance criteria and evaluation. Codex reviews implementation against the issue, invariants and tests; it is a reviewer, not a competing architect. |
| Erik never routinely operates the engine editor | Erik's loop is a playable link on his iPhone or iPad. Editor state is reproducible from code and configuration wherever practical, and GitHub is the source of truth. |
| Determinism target: same build, every shipped runtime, headless | The maths model is chosen by the spike, not prescribed. |
| No networking in v1 | Local and online multiplayer stay architecturally credible (principle 5). |
| Top-down camera, no minimap | Current direction; revisited through play. |
| Starting match length: two 3-minute halves | A tuning value. |
| Football metrics layer built early | One instrument for AI evaluation, attributes, balance, exploit detection, quick-sim calibration and later career statistics. |
| Agents build knobs; Erik turns them | No agent tunes feel on its own judgement. Chosen values get characterisation tests. |
| Docs: short AGENTS.md, thin CLAUDE.md, ADR folder | Architecture docs follow once there is architecture to describe. |

## Engine: the Unity 6 hypothesis

Unity 6 survives scrutiny on paper, so it is the right hypothesis to test. Its real risk is the iteration loop — build times, CI licensing and Editor dependence — not determinism or performance. The spike therefore tests the loop first.

The primary hypothesis is Unity 6 with C#, URP, the Input System and Platform Toolkit where useful. Unreal is too heavy for this game.

**The simulation language is one of the most expensive choices to reverse.** The engine boundary is deliberately designed so the host remains replaceable, for instance by Godot if its C# web export ships. An engine swap would still cost real work in presentation, tooling, assets and build infrastructure; the architecture contains that cost rather than eliminating it. Choosing Unity really means choosing C# for the simulation.

**Assessment**

| Concern | Assessment | Risk |
| --- | --- | --- |
| Fast iteration for Erik | Web builds compile C# through IL2CPP to WebAssembly: minutes per build rather than seconds, and larger downloads than a TypeScript bundle. Push-to-playable-link time is the first thing the spike measures. | High — the main risk |
| CI | GameCI on GitHub Actions supports Unity Personal, using a licence file created once in Unity Hub plus account credentials. Unity has broken CI activation before by removing manual Personal activation. iOS builds need macOS runners, which cost more. | Medium |
| Agentic development | C# suits agents well. Scenes, prefabs and assets are YAML that agents edit poorly, and agents probably can't run the Editor in their sandboxes, so engine-side work is verified through CI builds. Mitigation: scenes built from code, editor scripts run in batch mode, few assets. | Medium |
| Deterministic simulation | Unity makes no cross-platform float guarantee. Community tests found float arithmetic consistent across Windows, Android, WebGL, Mac and iOS, but System.Math trig not, and a Mono versus IL2CPP float inconsistency on Apple Silicon has been reported. Expect own trig at minimum and fixed point at worst; both are cheap for 23 bodies. Unity physics is ruled out. | Medium, solvable |
| Web/PWA on iPhone and iPad | iOS Safari 15+ is a supported browser. Managed C# threads aren't available on the web, which is fine for a single-threaded simulation, and web audio is basic. Memory and load time on iPhone still need measuring. | Medium |
| Offline-first | Native builds are offline by nature. Unity 6 ships a PWA web template with a manifest and service worker; a cold offline launch on iOS still has to be proven. | Low native, medium web |
| Headless and AI evaluation | Better than any engine option: the engine-free simulation runs in plain .NET without Unity, in CI and in agents' environments. | Low |
| Long-term portability | The strongest option: iOS, Android, web, desktop and Steam, with consoles via Unity Pro plus platform-holder approval — a later cost. Unity's licensing has changed abruptly before; the engine-free simulation contains that risk. | Low |
| Future multiplayer | Our own deterministic model works over any transport; nothing Unity-specific is needed. | Low |
| Performance | Trivial natively for 23 bodies, and WebAssembly is ample. AI cost is ours, not Unity's. | Low |
| Maintainability without Erik in the Editor | Achievable with editor-as-code discipline. It needs two one-time tasks from Erik: creating the CI licence file and enrolling in the Apple Developer Program. | Medium |

**Alternatives**

| Option | Verdict |
| --- | --- |
| TypeScript web stack, as in TURN | Fastest loop, proven by TURN. Basic Number arithmetic is well specified, but transcendental Math functions can differ between engines, so this path would also need deterministic maths for them. Native iOS and Steam need wrappers, and consoles are effectively closed. The control to compare against if Unity fails on the loop. |
| Godot 4 with C# | C# projects can't export to the web as of April 2026, which breaks Erik's web loop. With GDScript the web works, but the simulation language becomes GDScript. Consoles go through third parties. |
| Defold (Lua) | Free and source-available, targeting web, iOS, desktop, Switch and PlayStation (with approval). Its reputation is small web builds and command-line builds — exactly where Unity is weakest. Costs: Lua for a large simulation, and no JIT on iOS or the web. The fallback if Unity fails on the loop or CI. |
| Unreal | Too heavy for this game. |

**The simulation boundary under Unity holds.** A pure C# simulation with Unity as host is right, and I'd tighten it in five ways:

- Compile the same source twice: as a Unity assembly that forbids engine references, and as a plain .NET project for tests. Target the C# version Unity supports so both compile identically.
- The host drives the fixed step with its own accumulator. No FixedUpdate timing, Time.deltaTime or Unity physics inside the simulation.
- Animator only reads action phases from state; it never decides timing.
- Keep the host thin: input adapters, mapping snapshots to sprites, audio triggered by events, and UI. Anything with football meaning stays in the simulation.
- For determinism, the comparison that matters is .NET in CI against IL2CPP on an iPhone and in WebAssembly.

## The engine spike

The smallest test that could falsify Unity is about a week of agent work plus one device session with Erik, run inside the ELEVEN repository. It stops early as soon as a kill criterion fires.

**What gets built**

- **A minimal engine-free simulation in C#.** A fixed step, a ball with height, spin and bounce, one dot moved by owned commands, a goal-line check, counter-based randomness, a per-tick state hash, snapshot/restore and a scripted 60-second scenario. It isn't football; it exercises the maths football will need.
- **A .NET test harness.** Run-twice determinism, snapshot round-trip and a hash log, all without Unity.
- **A thin Unity 6 host.** One bootstrap scene built from code, simple shapes, an on-screen stick and kick button through the Input System, the build id on screen, and a button that runs the scenario and shows its final hash.
- **The pipeline.** .NET tests on every push; a Unity web build through GameCI deployed to static hosting, with the link posted on the PR; a native iOS build to TestFlight from a macOS runner.

**Order: cheapest kill first**

1. The loop: CI licence, web build and a playable link on Erik's phone.
2. The device session: load time, touch latency against TURN LAB on the same device, frame pacing, and the PWA offline test.
3. The determinism matrix: scenario hashes from .NET in CI, the web build in Safari and Chrome, and the native iOS build. Plain floats with own trig first; fixed point only if they diverge.
4. Native iOS through TestFlight.
5. The agent test: Opus makes and verifies a visible change, such as a new on-screen control, through code and CI alone.

**Kill criteria** (thresholds are proposals for Erik to confirm)

| # | Unity fails if | Measured by |
| --- | --- | --- |
| K1 | Push to playable web link takes over 20 minutes median, or needs any manual Editor step | CI timings across at least five pushes |
| K2 | CI can't build reliably with the licence we have | Build success rate during the spike |
| K3 | The web build on iPhone or iPad fails to run reliably, takes over 10 s to load on Wi-Fi, or is more than one frame (17 ms) slower from touch to screen than TURN LAB | Erik's device session; 240 fps slow-motion video on the iPhone |
| K4 | Hashes diverge across runtimes and neither own trig nor fixed point fixes it at acceptable cost | The determinism matrix |
| K5 | Opus can't make and verify a visible change without the Editor | The agent test |

The PWA offline test is recorded but isn't a kill on its own. If the web build can't cold-launch offline on iOS, offline-first is met by native builds and the web build becomes a development and alpha channel.

**Outputs**

- ADR-0001, engine and platform: the measurements, Opus's recommendation and Erik's approval.
- ADR-0002, the simulation maths model.
- If Unity fails, the criterion that fired points to the alternative: K1 or K2 to the TypeScript web stack or Defold, K3 to native-only testing or the web stack.
- Spike code that isn't kept is deleted once ADR-0001 is accepted, so the repo never carries a dead second host.

## Hypotheses to prototype

Each is answered by building and playing. Feel hypotheses end in Erik's judgement; the technical ones end in a measurement.

| Hypothesis | How we test it | What decides it |
| --- | --- | --- |
| SSC-style touch control is the right starting point, not necessarily the answer | Two or three control candidates in Slice 1, switchable live | Which scheme Erik keeps choosing; how often intended actions fail |
| Free ball versus ball attached to the dribbler | Both models in Slice 1 behind a switch | Dribbling and tackling feel; which one Erik keeps choosing |
| Portrait versus landscape | Both layouts in Slice 1 | Readability, thumb occlusion, preference |
| 60 versus 120 Hz simulation | Latency measurement in Slice 1 | Input-to-screen delay and perceived responsiveness |
| Six-minute matches feel right | Tuning value from Slice 1 onwards | Session length Erik settles on |
| Abstract visuals read instantly | Action sprites from 2v2 onwards | Headers, volleys, bicycle kicks, tackles and saves recognised at a glance |
| HUD cues depend on the controlled player's awareness | Prototype at 5v5, through the same perception layer as the AI. Guardrail: awareness may add or sharpen cues, never hide what is plainly visible | Adds depth without hurting readability |
| No minimap is fair enough | Watch for losses caused by off-screen threats the AI could perceive | If it bites, limit AI awareness through the perception layer |
| Saved tactical setups, switchable mid-match at a cost | Prototype at 11v11 with team tactics | Switching matters and isn't spammed |
| Delegation with imperfect assistants | Career prototype, e.g. a co-trainer picking the XI by their skill | Saves time and produces interesting mistakes |
| Headless throughput is enough for evaluation | Ticks per second once 11v11 AI exists | Evaluation budget; input to the quick-sim decision |

## Deferred decisions

Deliberately left open until the named evidence exists. Deciding them earlier would be guessing.

| Decision | Reopen when |
| --- | --- |
| How unplayed matches get results: a calibrated statistical model, a coarser simulation or a hybrid | Headless throughput is measured |
| Opponent modelling of the human and manager intelligence | The base AI plays good football and the metrics layer exists |
| Career systems beyond the match contract | A playable 11v11 match exists |
| Whether AI awareness is limited by facing and distance | The perception layer exists and play shows a fairness problem |
| Compare and promote tooling beyond named presets | There are presets worth comparing |
| Keyboard and gamepad adapters | A platform or tester needs them |
| Local and online multiplayer, and their transport | A product decision to build them; after v1 for online |
| PC, Steam, consoles and the paid engine tier consoles require | A port is actually pursued |
| Cloud sync and platform services | After offline single-player is complete |
| Production hosting at enkel.design/eleven | External testing needs a stable address |
| A shared Enkel Design token file | A third product needs it, or drift causes real problems |
| Reinforcement learning, self-play, imitation, specialised agents | A demonstrated need |

## Collaboration model

Keep the four persistent roles for authority and add **lenses**: short, written review perspectives applied only when an issue calls for one. A lens has no authority, and its value comes from independence, not from the label.

**Why lenses, not hats.** An agent that switches hats inside its own work mostly repeats its own blind spots with a new title. A genuinely different perspective comes from a different model or a fresh context reviewing work it didn't produce. So the rule is about independence: an agent may run a checklist on its own work, but a review lens is applied by someone other than the author.

**Which perspectives earn a lens**

| Perspective | Verdict | Applied by | Triggered by |
| --- | --- | --- | --- |
| Accessibility | Adopt now | SOL or a fresh agent | Issues touching UI, text, colour, input, remapping or motion |
| Adversarial architecture review | Adopt now | Codex or a fresh agent, never the ADR's author | ADRs that add or supersede an invariant, and ADR-0001 |
| Exploit hunting | Adopt when opponents exist | Opus builds the bots; SOL defines the strategies to try | AI, tactics and balance issues |
| Art direction and Enkel Design consistency | Adopt when visual work starts | SOL; Erik decides | Visual language, tokens, sprites, match presentation |
| Copy and editorial | Later | Any agent except the writer | Menus, tutorials and career text, once they exist |
| Interaction and game design | Duplicate | — | Already SOL's role |
| Gameplay testing | Duplicate | — | Erik's role; agents can't feel. Their part is exploit hunting and automated evaluation. |
| QA and regression | Duplicate | — | Automated tests plus Codex review |
| Project and release coordination | Not now | — | A release checklist in CI is enough until external testers arrive |
| Performance | Not a lens | — | A CI budget test; profiling when it fails |

**How it runs**

- **Roles belong to agents; lenses are adopted per task.** Persistent responsibilities stay with Erik, Opus, SOL and Codex. The issue names any lenses it needs.
- **The repo holds the record.** Each lens is a one-page file in docs/lenses/: purpose, trigger, questions and blocking criteria. Findings go in a PR review comment headed with the lens name, each marked blocking or non-blocking. Decisions move into ADRs, follow-up work into issues, and recurring findings into the lens file or AGENTS.md. No separate role logs.
- **Lenses advise; owners decide.** Erik decides product, Opus implementation, and SOL acceptance criteria. Conflicting findings go to the owner once; no lens reviews another lens.
- **Blocking is narrow.** A finding blocks only if it breaks an invariant, an acceptance criterion, a test, or the lens's written blocking criteria. Everything else is non-blocking.
- **One pass per lens per PR.** A re-check covers blocking findings only.
- **Default is zero lenses.** Every PR gets Codex review; an issue adds at most two lenses unless Erik asks for more.
- **Lenses earn their place.** Add one when the same kind of problem slips through twice; retire one that goes unused.

The smallest useful model is therefore the four roles, Codex on every PR, and two lens files on day one — accessibility and adversarial architecture review — with exploit hunting and art direction added when their work begins.

## Enkel Design family

Share the grammar, not the code. ELEVEN should read as clearly Enkel Design through shared principles and interaction conventions, and as clearly football through its own theme, motifs and match presentation.

| Layer | Shared or ELEVEN's own | How |
| --- | --- | --- |
| Principles: clarity, accessibility, honest feedback | Shared | A written Enkel Design grammar |
| Interaction grammar: how buttons, toggles, sliders, lists, dialogs, navigation, confirmation, focus and errors behave | Shared | The same grammar; ELEVEN implements it natively |
| Design DNA: rounded, bold, high-contrast outlined controls; type-scale ratios; spacing rhythm; stroke weights; motion principles; contrast minimums | Shared as values | DNA values in the grammar, copied into ELEVEN's base tokens with a version reference |
| Theme: palette, typography treatment, football motifs, atmosphere, sound | ELEVEN's own | ELEVEN theme tokens |
| Components: menus, settings, squad screens, tactics board, scoreboards | ELEVEN's own, built on the grammar | Football-specific variants where the context genuinely differs |
| Match presentation: pitch, sprites, HUD, camera | ELEVEN's own | A game presentation system where readability comes first |

**Conceptually shared, technically separate.** TURN is web and ELEVEN will probably be Unity, so shared components would be impractical even if they were wise. There is no cross-project component library and no dependency on TURN.

- **Tokens in three tiers.** Enkel base (the DNA values) → ELEVEN theme → component. Components read only theme and component tokens, so changing ELEVEN's look never touches the base, and re-aligning with Enkel Design touches only the base.
- **The token layer fits Unity naturally.** UI Toolkit uses CSS-like stylesheets with variables, so the same values map across from the web.
- **The grammar needs writing down.** Today it seems to live implicitly in TURN. A short written grammar, extracted from TURN and kept outside both projects, is the one shared artifact worth creating now.
- **Extract shared tooling only on evidence.** A shared token file or tooling waits until a third product needs it or drift causes real problems.

## Development and test workflow

Erik's loop is a requirement: agents → GitHub → automated tests and build → playable link → Erik plays on his iPhone or iPad. The TURN pipeline is copied into the repo and adapted, not shared: issue → implementation → automated verification → review → device/play test → iteration → release.

**Principles**

- Test first for rules. Prototype first for feel. Eval first for intelligence.
- Agents build the instruments; humans judge the feel.
- Anything judged manually is still reproducible, measurable and preservable.

**Two lanes to Erik's device**

- **ELEVEN LAB (web).** Every merge to main, and optionally every PR, produces a playable web link, like TURN LAB today. This is the daily feel loop.
- **Native iOS (TestFlight).** Built regularly, at least once per slice milestone, to verify real-device latency, performance and anything the browser hides.

**Working without the Editor**

- **The simulation needs no engine.** It builds and tests in plain .NET, so most early work is verified in minutes in CI and in agents' own environments.
- **Engine state is code.** Scenes are built at runtime or by editor scripts run in batch mode. Project settings, input actions and build configuration are committed as text. No undocumented manual Editor state; if a change truly needs the Editor's interface, the PR says so and why.
- **Agents see the result through the web build.** Automated screenshots of it let agents and reviewers check visual changes without the Editor.
- **Erik's one-time tasks.** Creating the CI licence file in Unity Hub, and enrolling in the Apple Developer Program for TestFlight.

**Rules of the loop**

- **Acceptance criteria come first.** They are written into the issue before implementation. Codex reviews against the issue, invariants and tests, marking each finding blocking or non-blocking.
- **The repo is the shared memory.** Decisions live in ADRs and issues; a decision that exists only in a chat hasn't been made.
- **Carry over TURN's hard-won rules.** Before AGENTS.md is written, go through turn/AGENTS.md for rules added after incidents or repeated mistakes, and keep those that apply.

**Tests**

- **Rules:** written before the code; they must first fail on an assertion, not an error.
- **Simulation:** properties that always hold — no NaN, speed caps, goal-line and post integrity — fuzzed headlessly with random command streams.
- **Determinism and snapshots:** run-twice and snapshot round-trip on every build; the cross-runtime hash comparison wherever CI can reach the runtime, and on real devices otherwise.
- **Feel:** characterisation curves for a standard pass, driven shot, lob and sprint make any feel change visible. Baseline updates are separate commits with a reason; feel-related ones need Erik's sign-off.
- **AI:** statistical evaluations on fixed seed sets with confidence intervals, in a fast CI suite and a larger nightly suite.
- **Performance:** a per-tick budget test, so evaluation stays affordable.
- **Offline:** single-player tests run with networking disabled.

**Feedback from play**

- Every build shows its build id.
- A one-tap “this felt wrong” capture saves the last ten seconds as a replay, a snapshot, the build id, the config hash and a note.
- Every played match is recorded: seed, version, config and inputs.

**External testing, later:** the private LAB build for Erik → shareable web builds for small alpha groups → TestFlight → platform-native distribution.

## Slice 1: find the football

Slice 1 tests whether SWOS/SSC-style football can feel exceptional on glass. It starts only once ADR-0001 has settled the engine, and it grows from the spike's simulation skeleton rather than starting over.

**Playable**

- Pitch, goal and keeper
- Ball with height and spin
- One controlled player
- Two or three on-screen touch control candidates, switchable live
- Free-ball and attached control models behind a switch
- Portrait and landscape layouts, as a prototype question
- Live tuning panel, named presets, save and capture

**Underneath**

- The engine-free simulation: plain-data state, hashed config, counter-based randomness
- A touch input adapter producing owned commands
- Headless execution and the determinism and snapshot checks in CI
- Build and version id on screen
- The one-tap “this felt wrong” capture

**Not in Slice 1:** outfield opponents, team AI, career, menus, audio polish, other input devices.

**Growth:** 1v1 → 2v2 → 5v5 → 11v11, each step when the previous one earns it.

**Exit criteria**

- [ ] Erik keeps playing after he has finished testing.
- [ ] The determinism, snapshot and headless checks pass in CI.
- [ ] A preferred control scheme, ball model and orientation are chosen, or the reason they can't be yet is written down.

## Initial issue sequence

Issues 0–7 take the repo to an engine decision; issues 8–15 make up Slice 1. Acceptance criteria for each are drafted by SOL before Opus starts.

| # | Issue | Done when |
| --- | --- | --- |
| 0 | Create the ELEVEN repo: AGENTS.md, CLAUDE.md, ADR template and ADR-0000 (this brief as baseline), issue template with acceptance criteria and lenses, .NET CI | CI is green on an empty simulation project |
| 1 | Spike: engine-free simulation skeleton and .NET harness | Run-twice determinism, snapshot round-trip and hash log pass in CI |
| 2 | Spike: thin Unity 6 host with a code-built scene, touch stick and kick button, build id, scenario hash button | It runs in a local or CI build with no manual Editor state |
| 3 | Spike: web build and playable link per PR through GameCI | K1 and K2 measured over five pushes |
| 4 | Spike: device session — load, latency against TURN LAB, frame pacing, PWA offline, determinism matrix | K3 and K4 measured and recorded |
| 5 | Spike: native iOS build to TestFlight from CI | Erik installs and runs it from TestFlight |
| 6 | Spike: agent test without the Editor | K5 measured |
| 7 | ADR-0001 engine and platform, ADR-0002 maths model | Erik approves; unused spike code deleted |
| 8 | Ball model: height, spin, bounce, friction, with characterisation curves | Curves recorded; properties fuzzed |
| 9 | Controlled player movement through owned commands and the touch adapter | Replay test passes with assistance on |
| 10 | Touch control candidates A, B and C, switchable live | Erik can switch mid-session |
| 11 | Ball control models: free and attached, switchable | Erik can switch mid-session |
| 12 | Goal, goal line and a basic keeper | Goal-line and post properties pass |
| 13 | Tuning panel, named presets and the “this felt wrong” capture | A capture replays exactly in CI |
| 14 | Portrait and landscape layouts | Both playable on iPhone and iPad |
| 15 | Slice 1 review | Exit criteria checked; the next slice's scope agreed |

## Where Opus disagrees or adds a condition

Resolved since v0.2: decision rights, and testing both ball models in Slice 1. Seven points remain.

1. **Hold console reach to your own decision rule.** Consoles are the one path the web stack really closes; Steam and native iOS work through wrappers. The rule says to preserve a foreseeable option at low cost. If the spike shows Unity's loop is materially slower than TURN's, that isn't low cost, and console reach alone shouldn't outweigh it. I still support testing Unity first.
2. **ADR-0001 is largely a language decision.** Frame it as “a C# simulation hosted in Unity” versus “a TypeScript simulation in the browser”. The simulation language is one of the most expensive choices to reverse; the host stays replaceable behind the boundary, at a real but contained cost.
3. **Both paths need deterministic maths.** On the web, basic Number arithmetic is well specified, but transcendental Math functions can differ between engines. Unity makes no official cross-runtime guarantee even for basic arithmetic, though community tests found it consistent. Expect own trig on either path and possibly fixed point under Unity; the spike measures how much is needed, and that cost belongs in the comparison.
4. **Don't fight Safari for offline.** If the Unity web build can't cold-launch offline on iOS, meet offline-first with native builds and keep the web build as a development and alpha channel.
5. **“Erik never operates the Editor” has two one-time exceptions.** Creating the CI licence file in Unity Hub and enrolling in the Apple Developer Program. Neither is routine, but both are needed before the spike can finish.
6. **Lenses, not hats.** Multiple perspectives help only when they are independent. A role switch inside one agent's own work is the pretend team you want to avoid, so the model above is built on independence and fewer lenses than the list proposed.
7. **The Enkel Design grammar needs a home.** No shared code, agreed. But the grammar should be written down once and kept outside both TURN and ELEVEN — in its own small repository or in ai. Where it lives is Erik's call.

## Sources

Engine facts checked on 25 Oct 2026:

- [Unity Manual: Web browser compatibility](https://docs.unity3d.com/Manual/webgl-browsercompatibility.html)
- [Unity Manual: Web templates, including PWA](https://docs.unity3d.com/Manual/web-templates-intro.html)
- [Unity Manual: Web technical limitations](https://docs.unity.com/en-us/engine/6000.7/manual/platform-specific/webgl/intro/technical-overview)
- [Unity Discussions: State of Determinism in Unity](https://discussions.unity.com/t/state-of-determinism-in-unity/867770)
- [Unity Issue Tracker: Apple Silicon floating-point inconsistencies between Mono and IL2CPP](https://issuetracker.unity3d.com/issues/apple-silicon-floating-point-inconsistencies-between-mono-and-il2cpp)
- [Unity Discussions: manual activation of Personal licences removed](https://discussions.unity.com/t/unity-no-longer-supports-manual-activation-of-personal-licenses/926760)
- [GameCI: activation on GitHub Actions](https://game.ci/docs/github/activation)
- [Unity: console and PC games](https://unity.com/solutions/console-and-pc-games)
- [Godot Forum: exporting C# projects to the web](https://forum.godotengine.org/t/is-there-an-update-on-exporting-c-projects-to-web/128821?page=2)
- [Wikipedia: Defold](https://en.wikipedia.org/wiki/Defold)
