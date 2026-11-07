using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Eleven.Presentation;
using Eleven.Simulation;
using Eleven.Simulation.Harness;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>
    /// The thin Unity host. It owns the fixed-step loop, feeds the simulation owned commands from
    /// the touch adapter, and draws interpolated state. The simulation itself never sees Unity
    /// time, physics or input (INV-2).
    ///
    /// Loop: real time accumulates and is spent in whole simulation ticks; whatever is left over
    /// becomes the interpolation factor between the previous and current tick. A frame longer
    /// than <see cref="MaxFrameSeconds"/> (the tab was in the background, the app was paused or
    /// the device stalled) is dropped rather than caught up, so the simulation never advances
    /// for time nobody saw. At most <see cref="MaxStepsPerFrame"/> ticks run per frame; any
    /// backlog beyond that is dropped too.
    /// </summary>
    public sealed class HostLoop : MonoBehaviour
    {
        private const double MaxFrameSeconds = 0.25;
        private const int MaxStepsPerFrame = 8;
        private const int TraceEveryTicks = 60;
        private const int ReplayCheckEveryTicks = 300;

        private readonly List<Command> _commands = new List<Command>(1);
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly FramePacing _pacing = new FramePacing();

        // The last ten seconds, for THIS FELT WRONG (#44). It only reads the simulator.
        private readonly FeelRecorder _feel = new FeelRecorder();

        // The feel test bench's applied values (#45). The config and the practice start come from
        // them; APPLY / RETRY replaces them whole and starts a fresh run.
        private BenchCandidate _bench = BenchCandidate.Reference;

        private SimConfig _config;
        private Simulator _simulator;
        private CommandRecording _recording;
        private PitchView _view;
        private TouchAdapter _input;
        private UiRoot _ui;
        private string _uiMode = "toolkit";
        private Specimen _specimen;
        private Hud _hud;
        private int _resultLines;

        // THIS FELT WRONG's capture, taken at the press and kept once the note sheet closes (#53).
        private FeelCapture _pending;
        private Rect _saveTold;
        private bool _saveActiveTold;
        private HudLayout _layout;
        private string _buildId;
        private double _loadSeconds;
        private bool _trace;
        private bool _paused;
        private double _lastTime;
        private double _accumulator;
        private Vector2 _previousPlayer;
        private Vector3 _previousBall;
        private GUIStyle _buttonStyle;
        private Vector2Int _styledFor;
        private int _framesSinceTrace;
        private double _lastTraceTime;
        private bool _modalWasOpen;

        // What the last ACTION gesture became, shown on the disc for a moment (#28).
        private string _lastGesture = "";
        private double _lastGestureUntil;

        private void Awake()
        {
            Application.runInBackground = false;
            _buildId = Application.version;
            string url = Application.absoluteURL ?? "";
            _trace = url.Contains("trace");

            _config = _bench.Config;
            _simulator = new Simulator(_config, _bench.PracticeStart());
            _recording = CommandRecording.StartFrom(_simulator);
            _feel.Start(_simulator);
            // The player keeps every key (WebGLInput.captureAllKeyboardInput stays at its default):
            // keyboard focus and activation are Unity's UI's (#53). A text field raises the phone's
            // keyboard.
            EnableMobileKeyboard();
            // ?pitch=stripes or ?pitch=plain: the comparators for the mown pattern (#58).
            PitchView.Mowing mowing = url.Contains("pitch=stripes") ? PitchView.Mowing.Stripes
                : url.Contains("pitch=plain") ? PitchView.Mowing.Plain : PitchView.Mowing.Crosscut;
            _view = new PitchView(_config, transform, mowing);
            _input = new TouchAdapter(SpikeScenario.ControllerId, _bench.ActionSurface);
            _input.Delivered += GestureDelivered;
            // ?noui: measurement only (#48), the frame without the UI.
            // ?textscale=1.5: enlarged text, until settings exist to choose it (#48).
            int textScaleAt = url.IndexOf("textscale=", StringComparison.Ordinal);
            if (textScaleAt >= 0 && float.TryParse(url.Substring(textScaleAt + 10).Split('&')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float textScale))
            {
                UiRoot.TextScale = Mathf.Clamp(textScale, 0.5f, 3f);
            }

            UiRoot.ReducedMotion = PrefersReducedMotion();

            // Frame cost (#48): ?ui=empty builds the UI Toolkit panel with nothing in it.
            _uiMode = url.Contains("ui=empty") ? "empty" : "toolkit";
            if (!url.Contains("noui")) BuildUi();

            // A capture kept on an earlier visit stays exportable: SAVE CAPTURE offers the newest one.
            if (_hud != null && HasKeptCapture()) _hud.ShowSave();

            // ?specimen (or ?specimen=dark): the visual system's specimen over the match (#48).
            if (_ui != null && url.Contains("specimen")) _specimen = new Specimen(_ui, url.Contains("specimen=dark"));
            if (_trace)
            {
                _input.PressLogged += press => Log($"ELEVEN press {press}");
                _input.ReleaseLogged += release => Log($"ELEVEN release {release}");
            }
            RememberPrevious();
            UpdateLayout(force: true);
            _lastTime = Time.realtimeSinceStartupAsDouble;
            _loadSeconds = PageSeconds();

            Log(FormattableString.Invariant($"ELEVEN host ready build={_buildId} tickRate={_config.TickRate} config={_config.Hash:x16} loadMs={_loadSeconds * 1000:F0}"));
            Log(FormattableString.Invariant($"ELEVEN bench knobs={BenchCandidate.Knobs.Count} preset={_bench.PresetName} config={_config.Hash:x16} start={_simulator.StateHash():x16}"));
            Log(FormattableString.Invariant($"ELEVEN pitch mowing={_view.Grass.ToString().ToLowerInvariant()} band={PitchView.MownBand}m"));

            if (url.Contains("autorun")) RunScenario();
        }

        private void OnDestroy() => _input?.Dispose();

        private void OnApplicationPause(bool paused)
        {
            _paused = paused;
            _accumulator = 0;
            _pacing.Clear();
            _lastTime = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            _framesSinceTrace++;
            UpdateLayout(force: false);

            // A sheet opening cancels a gesture in progress, so none ends as a phantom command when
            // it closes (#28).
            bool modal = _hud != null && _hud.ModalOpen;
            if (modal && !_modalWasOpen) _input.Clear();
            _modalWasOpen = modal;

            double now = Time.realtimeSinceStartupAsDouble;
            double frame = now - _lastTime;
            _lastTime = now;

            // Play waits while the note sheet is open, as it did under the browser's prompt (#44).
            bool waiting = _hud != null && _hud.SheetOpen;
            if (_paused || waiting || frame > MaxFrameSeconds) _accumulator = 0;
            else
            {
                _accumulator += frame;
                _pacing.Record(now, frame);
            }

            double tick = _config.TickSeconds;
            int steps = 0;
            while (_accumulator >= tick && steps < MaxStepsPerFrame)
            {
                StepOnce();
                _accumulator -= tick;
                steps++;
            }

            if (_accumulator >= tick) _accumulator = 0;

            // A press on ACTION shapes the ball while the controlled player's kick winds up or its
            // aftertouch window is open: the simulation's window, not a second duration (#28).
            PlayerState controlled = _simulator.PeekState().Players[0];
            _input.ShapingOpen = controlled.WindUpTicks > 0 || controlled.AftertouchTicks > 0;

            _specimen?.Tick(_simulator.Tick);
            if (_hud != null)
            {
                _hud.Tick(_buildId, _loadSeconds, _pacing.Dropped, _pacing.CoveredSeconds);
                TellPageWhereSaveIs();
            }

            float alpha = (float)(_accumulator / tick);
            SimState state = _simulator.PeekState();
            _view.Render(
                Vector2.Lerp(_previousPlayer, ToVector(state.Players[0].Position), alpha),
                Vector3.Lerp(_previousBall, ToVector(state.Ball.Position), alpha),
                _layout);
        }

        private void StepOnce()
        {
            RememberPrevious();
            _commands.Clear();
            _commands.Add(_input.CommandFor(_simulator.Tick));
            _events.Clear();
            _recording.Record(_commands); // as passed, before the simulation applies any assistance (INV-10)
            _simulator.Step(_commands, _events);
            _feel.Record(_simulator, _commands, _events);

            if (!_trace) return;

            foreach (SimEvent e in _events) Log($"ELEVEN event {e}");
            if (_simulator.Tick % TraceEveryTicks == 0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                double fps = _framesSinceTrace / Math.Max(1e-6, now - _lastTraceTime);
                _framesSinceTrace = 0;
                _lastTraceTime = now;
                SimState s = _simulator.PeekState();
                Log(FormattableString.Invariant($"ELEVEN trace tick={s.Tick} player={s.Players[0].Position.X:F2},{s.Players[0].Position.Y:F2} ball={s.Ball.Position.X:F2},{s.Ball.Position.Y:F2},{s.Ball.Position.Z:F2} fps={fps:F1} hash={_simulator.StateHash():x16} view={_view.Visibility(_layout)} dropped={_pacing.Dropped} frames={_pacing.Frames} interval={_pacing.IntervalSeconds * 1000:F1} hud={_hud?.BuildSize ?? "none"} model={ModelName(_config.ControlModel)}"));
            }

            if (_simulator.Tick % ReplayCheckEveryTicks == 0) CheckReplay();
        }

        /// <summary>
        /// A gesture's command went to a tick (#28): shown on the ACTION disc for a moment, with the
        /// time from the finger lifting to the command, and traced for the smoke test.
        /// </summary>
        private void GestureDelivered(ActionReport report)
        {
            double latency = (TouchAdapter.Now - report.ReleaseTime) * 1000;
            _lastGesture = report.Gesture == ActionGesture.Tap
                ? FormattableString.Invariant($"PASS\n{latency:F0} ms")
                : FormattableString.Invariant($"KICK {report.Strength * 100:F0}%\n{latency:F0} ms");
            _lastGestureUntil = Time.realtimeSinceStartupAsDouble + 1.2;
            if (!_trace) return;
            Log(FormattableString.Invariant(
                $"ELEVEN action gesture={report.Gesture.ToString().ToLowerInvariant()} strength={report.Strength:F3} direction={report.Direction.X:F2},{report.Direction.Y:F2} latency={latency:F1}ms tick={_simulator.Tick}"));
        }

        /// <summary>Trace mode: replays the session's recording headlessly and compares it with the live state.</summary>
        private void CheckReplay()
        {
            Simulator replay = _recording.Replay(_config);
            bool match = replay.Tick == _simulator.Tick && replay.StateHash() == _simulator.StateHash();
            Log($"ELEVEN recording from={_recording.StartTick} steps={_recording.StepCount} bytes={_recording.Encode().Length} replay={(match ? "match" : "MISMATCH")}");
        }

        private void RememberPrevious()
        {
            SimState state = _simulator.PeekState();
            _previousPlayer = ToVector(state.Players[0].Position);
            _previousBall = ToVector(state.Ball.Position);
        }

        private void ResetMatch()
        {
            _simulator = new Simulator(_config, _bench.PracticeStart());
            _recording = CommandRecording.StartFrom(_simulator);
            _feel.Start(_simulator);
            _input.Clear();
            RememberPrevious();
            Log("ELEVEN reset");
        }

        /// <summary>
        /// THIS FELT WRONG (#44, #53): the capture of the last ten seconds is taken at the press, so
        /// the window ends where the player pressed; then the note sheet opens, and play waits.
        /// Only reads the simulation.
        /// </summary>
        private void FeltWrongPressed()
        {
            if (_pending != null) return;
            _pending = _feel.Capture(_simulator, _buildId, _bench.PresetName, _input.Settings.Describe(), "");
            _input.Clear();
            Log($"ELEVEN capture pressed tick={_simulator.Tick}");
            _hud.OpenNote();
        }

        /// <summary>
        /// The note sheet closed: the capture, with the note if there is one, this build and the
        /// config, is kept on the device for SAVE CAPTURE and announced. The CI smoke test replays the
        /// kept bytes with .NET, as a player would export them (#50).
        /// </summary>
        private void KeepFeltWrong(string note)
        {
            FeelCapture capture = _pending;
            _pending = null;
            if (capture == null) return;
            capture.Note = note ?? "";
            byte[] bytes = capture.Encode();
            double seconds = capture.StepCount * _config.TickSeconds;
            long end = capture.StartTick + capture.StepCount;
            string name = FormattableString.Invariant($"eleven-felt-wrong-{SafeName(_buildId)}-t{end}.elfc");
            string summary = FormattableString.Invariant($"the last {seconds:F0} s");
            bool kept = KeepCapture(bytes, name, summary);
            Log(FormattableString.Invariant(
                $"ELEVEN capture ticks={capture.StartTick}..{end} steps={capture.StepCount} bytes={bytes.Length} name={name} note={capture.Note.Length}"));
            _hud.ShowStatus(kept
                ? $"Captured {summary}, kept on this device."
                : $"Captured {summary}, not kept on this device (storage unavailable): save it now.");
            _hud.ShowSave();
        }

        /// <summary>
        /// APPLY / RETRY (#45): the bench's candidate, already whole and valid, becomes the applied
        /// values, a new immutable config and a fresh run from the standard practice start. Nothing
        /// here touches the running simulation: it is replaced.
        /// </summary>
        private void ApplyBench(BenchCandidate candidate)
        {
            double started = Time.realtimeSinceStartupAsDouble;
            int changes = candidate.ChangesFrom(_bench);
            _bench = candidate;
            _config = candidate.Config;
            _view.Use(_config);
            _input.Use(candidate.ActionSurface);
            ResetMatch();
            _accumulator = 0;
            double milliseconds = (Time.realtimeSinceStartupAsDouble - started) * 1000;

            Log(FormattableString.Invariant(
                $"ELEVEN bench applied changes={changes} preset={_bench.PresetName} config={_config.Hash:x16} start={_simulator.StateHash():x16} hostMs={milliseconds:F1}"));
            _hud.ShowStatus(FormattableString.Invariant($"Applied {changes} change{(changes == 1 ? "" : "s")}, fresh run in {Math.Max(1, milliseconds):F0} ms."));
        }

        /// <summary>
        /// The live FREE ↔ ATTACHED switch (#60), between steps. The run goes on from the same state
        /// under the new model: nothing in the state is touched, and gestures in progress, or a kick
        /// queued for the next tick, carry over to the new simulator. Only the model changes in the
        /// applied values; pending bench edits stay pending. The run's recording and the felt-wrong
        /// recorder start a new segment here, since each holds one config.
        /// </summary>
        private void SwitchModel(ControlModel model)
        {
            if (_config.ControlModel == model) return;
            ulong before = _simulator.StateHash();
            _bench = _bench.WithControlModel(model);
            _config = _bench.Config;
            _view.Use(_config);
            _simulator = ControlSwitch.To(_simulator, model);
            _recording = CommandRecording.StartFrom(_simulator);
            _feel.Start(_simulator);

            Log(FormattableString.Invariant(
                $"ELEVEN model switched to={ModelName(model)} tick={_simulator.Tick} before={before:x16} after={_simulator.StateHash():x16} config={_config.Hash:x16}"));
            _hud.ShowStatus(model == ControlModel.Attached
                ? "Ball control: ATTACHED from now. The run goes on."
                : "Ball control: FREE from now. The run goes on.");
        }

        private static string ModelName(ControlModel model) => model switch
        {
            ControlModel.Free => "free",
            ControlModel.Attached => "attached",
            _ => "off",
        };

        private static string SafeName(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-') chars[i] = '-';
            }

            return new string(chars);
        }

        /// <summary>Runs the whole issue 1 scenario inside this build and shows its hashes.</summary>
        private void RunScenario()
        {
            double started = Time.realtimeSinceStartupAsDouble;
            ScenarioRun run = ScenarioRunner.Run(SpikeScenario.Config());
            double milliseconds = (Time.realtimeSinceStartupAsDouble - started) * 1000;

            ulong final = run.TickHashes[run.TickHashes.Count - 1];
            ulong digest = run.SequenceDigest();
            _resultLines = HudLayout.MaxResultLines;
            _hud?.ShowResult(milliseconds, digest, final);
            UpdateLayout(force: false);
            Log(FormattableString.Invariant($"ELEVEN scenario name={SpikeScenario.Name} digest={digest:x16} final={final:x16} hashes={run.TickHashes.Count} events={run.Events.Count} config={run.Config.Hash:x16} ms={milliseconds:F0}"));
        }

        /// <summary>
        /// The UI around the match (#48), in UI Toolkit from code. Presses on it reach the UI, not the
        /// stick or the ACTION surface.
        /// </summary>
        private void BuildUi()
        {
            double started = Time.realtimeSinceStartupAsDouble;
            string step = "panel";
            try
            {
                // Reset and Run sit over the pitch: the pitch context gives them chalk borders.
                _ui = new UiRoot(transform, Theme.Pitch);
                step = "scale";
                _ui.SetPixelsPerPoint(PixelsPerPoint());
                step = "actions";
                if (_uiMode == "empty")
                {
                    Log("ELEVEN ui ready font=none pixelsPerPoint=0 ms=0 panel=empty");
                    return;
                }

                _hud = new Hud(_ui, _trace, Relayout, ResetMatch, RunScenario, FeltWrongPressed, SaveCapture, KeepFeltWrong, VisibleRange, () => _bench, ApplyBench, SwitchModel);
                _input.OverUi = _ui.IsOverUi;
                if (_trace)
                {
                    // Whether pointer input reaches UI Toolkit at all, and where (#48 spike).
                    _ui.Root.RegisterCallback<PointerDownEvent>(e => Log(FormattableString.Invariant($"ELEVEN ui pointer down at={e.position.x:F0},{e.position.y:F0} target={(e.target as VisualElement)?.name}")), TrickleDown.TrickleDown);
                    _ui.Root.RegisterCallback<PointerUpEvent>(e => Log(FormattableString.Invariant($"ELEVEN ui pointer up at={e.position.x:F0},{e.position.y:F0}")), TrickleDown.TrickleDown);
                    _ui.Root.RegisterCallback<ClickEvent>(e => Log($"ELEVEN ui click target={(e.target as VisualElement)?.name}"), TrickleDown.TrickleDown);
                }
                // The label's laid-out size once it has one: zero means the font draws nothing.
                Label label = _hud.Run.Label;
                EventCallback<GeometryChangedEvent> measured = null;
                measured = _ =>
                {
                    label.UnregisterCallback(measured);
                    Log(FormattableString.Invariant($"ELEVEN ui text \"{label.text}\" width={label.layout.width:F1} height={label.layout.height:F1}"));
                };
                label.RegisterCallback(measured);
                double milliseconds = (Time.realtimeSinceStartupAsDouble - started) * 1000;
                string font = _ui.Font != null ? _ui.Font.name.Replace(' ', '_') : "none";
                Log(FormattableString.Invariant(
                    $"ELEVEN ui ready font={font} pixelsPerPoint={_ui.PixelsPerPoint:F2} ms={milliseconds:F1} panel={(_ui.Root.panel != null ? "yes" : "no")}"));
            }
            catch (Exception e)
            {
                Log($"ELEVEN ui failed at {step}: {e.GetType().Name}: {e.Message}");
            }
        }

        // The page starts SAVE CAPTURE's share in its own release handler, where the browser's
        // user-gesture requirement holds (ADR-0003), so it is told where the action is and whether a
        // press there saves, whenever either changes.
        private void TellPageWhereSaveIs()
        {
            Rect save = _layout.SaveCaptureButton;
            bool active = _hud.SaveActive;
            if (save == _saveTold && active == _saveActiveTold) return;
            _saveTold = save;
            _saveActiveTold = active;
            PlaceSaveCapture(save.x, save.y, save.width, save.height, _layout.ScreenWidth, _layout.ScreenHeight, active ? 1 : 0);
        }

        // The HUD measured an element needing another size: lay out again if anything moves.
        private void Relayout()
        {
            if (!Layout().SamePlaces(_layout)) UpdateLayout(true);
        }

        private HudLayout Layout() => HudLayout.For(Screen.width, Screen.height, Screen.safeArea, _resultLines, _hud?.Needs);

        private void UpdateLayout(bool force)
        {
            if (!force && _layout.ScreenWidth == Screen.width && _layout.ScreenHeight == Screen.height
                && _layout.ResultLines == _resultLines) return;

            _layout = Layout();
            _input.Place(_layout, PixelsPerPoint());
            if (_ui != null && _hud != null)
            {
                _ui.SetPixelsPerPoint(PixelsPerPoint());
                _hud.Place(_layout);
            }
            Log($"ELEVEN layout {_layout.Describe()}");
        }

        // The match controls (#28's) are the only thing still drawn with IMGUI.
        private void OnGUI()
        {
            // Not over a sheet: the note sheet or the test bench covers the match.
            if (Event.current.type != EventType.Repaint || (_hud != null && _hud.ModalOpen)) return;
            EnsureStyles();

            // The stick's vector is measured from where the thumb landed; its base is drawn as near
            // there as fits in the stick zone, with the knob offset the same way.
            Vector2 origin = _input.StickActive ? _input.StickOrigin : _layout.StickRest;
            Vector2 stickCentre = _layout.KeepInStickZone(origin);
            Vector2 knob = stickCentre + (_input.StickActive ? _input.StickKnob - _input.StickOrigin : Vector2.zero);
            Disc(stickCentre, _layout.StickRadius, HostPalette.ControlIdle);
            Disc(knob, _layout.StickRadius * 0.45f, HostPalette.ControlActive);

            // ACTION (#28): the whole right-hand area takes the gestures; the disc marks where the
            // thumb rests and says what the surface is doing.
            bool active = _input.ActionHeld || _input.Shaping;
            Disc(_layout.KickCentre, _layout.KickRadius, active ? HostPalette.ControlActive : HostPalette.ControlIdle);
            var actionLabel = new Rect(_layout.KickCentre.x - _layout.KickRadius, _layout.KickCentre.y - _layout.KickRadius,
                _layout.KickRadius * 2, _layout.KickRadius * 2);
            double charge = _input.Charge;
            string label = _input.Shaping ? "SHAPE"
                : _input.ActionHeld && charge > 0 ? FormattableString.Invariant($"HOLD\n{charge * 100:F0}%")
                : Time.realtimeSinceStartupAsDouble < _lastGestureUntil ? _lastGesture
                : "ACTION";
            GUI.Label(actionLabel, label, _buttonStyle);
        }

        private void EnsureStyles()
        {
            var screen = new Vector2Int(_layout.ScreenWidth, _layout.ScreenHeight);
            if (_buttonStyle != null && _styledFor == screen) return;

            _buttonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _layout.FontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _buttonStyle.normal.textColor = HostPalette.HudText;
            _styledFor = screen;
        }

        private void Disc(Vector2 centre, float radius, Color colour)
        {
            GUI.color = HostPalette.ControlOutline;
            float outline = radius + Mathf.Max(2f, radius * 0.06f);
            GUI.DrawTexture(new Rect(centre.x - outline, centre.y - outline, outline * 2, outline * 2), _view.Disc);
            GUI.color = colour;
            GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), _view.Disc);
            GUI.color = Color.white;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern double ElevenPageMilliseconds();

        [DllImport("__Internal")]
        private static extern int ElevenKeepCapture(byte[] bytes, int length, string name, string summary);

        [DllImport("__Internal")]
        private static extern int ElevenHasKeptCapture();

        [DllImport("__Internal")]
        private static extern void ElevenSaveCapture();

        [DllImport("__Internal")]
        private static extern void ElevenPlaceSaveCapture(float x, float y, float width, float height, int screenWidth, int screenHeight, int active);

        [DllImport("__Internal")]
        private static extern float ElevenCanvasCssWidth();

        [DllImport("__Internal")]
        private static extern float ElevenVisibleTop();

        [DllImport("__Internal")]
        private static extern float ElevenVisibleBottom();

        // The part of the canvas the page shows, top and bottom in points (#55).
        private static Vector2 VisibleRange() => new Vector2(ElevenVisibleTop(), ElevenVisibleBottom());

        [DllImport("__Internal")]
        private static extern int ElevenPrefersReducedMotion();

        private static bool PrefersReducedMotion() => ElevenPrefersReducedMotion() != 0;

        // From navigation start, so a cold load includes the download and the engine's start-up.
        private static double PageSeconds() => ElevenPageMilliseconds() / 1000;

        // Screen pixels per CSS pixel, so a UI point is a CSS pixel. Measured against Screen.width,
        // the size Unity renders at, not the canvas element's backing size, which lags at start-up.
        private static float PixelsPerPoint()
        {
            float css = ElevenCanvasCssWidth();
            return css > 0 ? Screen.width / css : 1f;
        }

        private static void EnableMobileKeyboard() => WebGLInput.mobileKeyboardSupport = true;

        // Storage and share/download are browser services with no UI (#53): the page keeps the
        // capture, and SAVE CAPTURE hands it to the share sheet or a download.
        private static bool KeepCapture(byte[] bytes, string name, string summary) => ElevenKeepCapture(bytes, bytes.Length, name, summary) != 0;

        private static bool HasKeptCapture() => ElevenHasKeptCapture() != 0;

        private static void SaveCapture() => ElevenSaveCapture();

        private static void PlaceSaveCapture(float x, float y, float width, float height, int screenWidth, int screenHeight, int active) =>
            ElevenPlaceSaveCapture(x, y, width, height, screenWidth, screenHeight, active);
#else
        private static double PageSeconds() => Time.realtimeSinceStartupAsDouble;

        private static float PixelsPerPoint() => Screen.dpi > 0 ? Screen.dpi / 96f : 1f;

        // Outside the web build the whole screen shows.
        private static Vector2 VisibleRange() => new Vector2(0, Screen.height / PixelsPerPoint());

        private static bool PrefersReducedMotion() => false;

        private static void EnableMobileKeyboard() { }

        // Outside the web build there is no page: captures go to persistent data, which is the save.
        private static bool KeepCapture(byte[] bytes, string name, string summary)
        {
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.persistentDataPath, name), bytes);
            return true;
        }

        private static bool HasKeptCapture() => false;

        private static void SaveCapture() { }

        private static void PlaceSaveCapture(float x, float y, float width, float height, int screenWidth, int screenHeight, int active) { }
#endif

        private static Vector2 ToVector(Vec2 v) => new Vector2((float)v.X, (float)v.Y);

        private static Vector3 ToVector(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        private static void Log(string message) => Debug.Log(message);
    }
}
