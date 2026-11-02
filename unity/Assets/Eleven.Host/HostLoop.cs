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
        private readonly List<string> _resultLines = new List<string>(HudLayout.MaxResultLines);
        private readonly FramePacing _pacing = new FramePacing();

        // The last ten seconds, for THIS FELT WRONG (#44). It only reads the simulator.
        private readonly FeelRecorder _feel = new FeelRecorder();

        private SimConfig _config;
        private Simulator _simulator;
        private CommandRecording _recording;
        private PitchView _view;
        private TouchAdapter _input;
        private UiRoot _ui;
        private Specimen _specimen;
        private UiKit.Action _resetAction;
        private UiKit.Action _runAction;
        private HudLayout _layout;
        private string _buildId;
        private double _loadSeconds;
        private bool _trace;
        private bool _paused;
        private double _lastTime;
        private double _accumulator;
        private Vector2 _previousPlayer;
        private Vector3 _previousBall;
        private GUIStyle _textStyle;
        private GUIStyle _buildStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _resultStyle;
        private bool _resultFitChecked;
        private Vector2Int _styledFor;
        private int _framesSinceTrace;
        private double _lastTraceTime;

        private void Awake()
        {
            Application.runInBackground = false;
            _buildId = Application.version;
            string url = Application.absoluteURL ?? "";
            _trace = url.Contains("trace");

            _config = SpikeScenario.Config();
            _simulator = new Simulator(_config, SpikeScenario.InitialState(_config));
            _recording = CommandRecording.StartFrom(_simulator);
            _feel.Start(_simulator);
            AllowPageKeyboard();
            FeltWrongInstall(HostPalette.Css(HostPalette.HudText), HostPalette.Css(HostPalette.HudPanel),
                HostPalette.Css(HostPalette.HudText), HostPalette.Css(HostPalette.FocusRing));
            _view = new PitchView(_config, transform);
            _input = new TouchAdapter(SpikeScenario.ControllerId);
            // ?noui: measurement only (#48), the frame without the UI.
            // ?textscale=1.5: enlarged text, until settings exist to choose it (#48).
            int textScaleAt = url.IndexOf("textscale=", StringComparison.Ordinal);
            if (textScaleAt >= 0 && float.TryParse(url.Substring(textScaleAt + 10).Split('&')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float textScale))
            {
                UiRoot.TextScale = Mathf.Clamp(textScale, 0.5f, 3f);
            }

            if (!url.Contains("noui")) BuildUi();

            // ?specimen (or ?specimen=dark): the visual system's specimen over the match (#48).
            if (_ui != null && url.Contains("specimen")) _specimen = new Specimen(_ui, url.Contains("specimen=dark"));
            if (_trace) _input.PressLogged += press => Log($"ELEVEN press {press}");
            RememberPrevious();
            UpdateLayout(force: true);
            _lastTime = Time.realtimeSinceStartupAsDouble;
            _loadSeconds = PageSeconds();

            Log(FormattableString.Invariant($"ELEVEN host ready build={_buildId} tickRate={_config.TickRate} config={_config.Hash:x16} loadMs={_loadSeconds * 1000:F0}"));

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
            _input.Sample(_layout);


            // Before this frame's steps, so the window ends where the press was.
            string feltWrong = TakeFeltWrong();
            if (feltWrong != null) CaptureFeltWrong(feltWrong);

            double now = Time.realtimeSinceStartupAsDouble;
            double frame = now - _lastTime;
            _lastTime = now;

            if (_paused || frame > MaxFrameSeconds) _accumulator = 0;
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

            _specimen?.Tick(_simulator.Tick);

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
                Log(FormattableString.Invariant($"ELEVEN trace tick={s.Tick} player={s.Players[0].Position.X:F2},{s.Players[0].Position.Y:F2} ball={s.Ball.Position.X:F2},{s.Ball.Position.Y:F2},{s.Ball.Position.Z:F2} fps={fps:F1} hash={_simulator.StateHash():x16} view={_view.Visibility(_layout)} dropped={_pacing.Dropped} frames={_pacing.Frames} interval={_pacing.IntervalSeconds * 1000:F1}"));
            }

            if (_simulator.Tick % ReplayCheckEveryTicks == 0) CheckReplay();
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
            _simulator = new Simulator(_config, SpikeScenario.InitialState(_config));
            _recording = CommandRecording.StartFrom(_simulator);
            _feel.Start(_simulator);
            _input.Clear();
            RememberPrevious();
            Log("ELEVEN reset");
        }

        /// <summary>
        /// THIS FELT WRONG (#44): the last ten seconds, the snapshot that starts them, this build, the
        /// config and the note, kept on the device for SAVE CAPTURE. Only reads the simulation. The
        /// CI smoke test replays the kept bytes with .NET, as a player would export them (#50).
        /// </summary>
        private void CaptureFeltWrong(string note)
        {
            FeelCapture capture = _feel.Capture(_simulator, _buildId, "technical-reference", "none", note);
            byte[] bytes = capture.Encode();
            double seconds = capture.StepCount * _config.TickSeconds;
            string name = FormattableString.Invariant($"eleven-felt-wrong-{SafeName(_buildId)}-t{_simulator.Tick}.elfc");
            string summary = FormattableString.Invariant($"the last {seconds:F0} s");
            KeepCapture(bytes, name, summary);

            string line = FormattableString.Invariant(
                $"ELEVEN capture ticks={capture.StartTick}..{capture.StartTick + capture.StepCount} steps={capture.StepCount} bytes={bytes.Length} name={name}");
            Log(line);
        }

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
            _resultLines.Clear();
            _resultLines.Add(FormattableString.Invariant($"Scenario, {milliseconds:F0} ms"));
            _resultLines.Add($"digest {digest:x16}");
            _resultLines.Add($"final  {final:x16}");

            _resultFitChecked = false;
            UpdateLayout(force: false);
            Log(FormattableString.Invariant($"ELEVEN scenario name={SpikeScenario.Name} digest={digest:x16} final={final:x16} hashes={run.TickHashes.Count} events={run.Events.Count} config={run.Config.Hash:x16} ms={milliseconds:F0}"));
        }

        /// <summary>
        /// The UI around the match (#48), in UI Toolkit from code. Presses on it reach the UI, not the
        /// stick or the kick.
        /// </summary>
        private void BuildUi()
        {
            double started = Time.realtimeSinceStartupAsDouble;
            string step = "panel";
            try
            {
                _ui = new UiRoot(transform, Theme.Ordinary);
                step = "scale";
                _ui.SetPixelsPerPoint(PixelsPerPoint());
                step = "actions";
                _resetAction = UiKit.MakeAction(_ui, _ui.Theme, ActionRole.Utility, "Reset", ResetMatch);
                _runAction = UiKit.MakeAction(_ui, _ui.Theme, ActionRole.Utility, "Run scenario", RunScenario);
                _ui.Root.Add(_resetAction.Element);
                _ui.Root.Add(_runAction.Element);
                _input.OverUi = _ui.IsOverUi;
                if (_trace)
                {
                    // Whether pointer input reaches UI Toolkit at all, and where (#48 spike).
                    _ui.Root.RegisterCallback<PointerDownEvent>(e => Log(FormattableString.Invariant($"ELEVEN ui pointer down at={e.position.x:F0},{e.position.y:F0} target={(e.target as VisualElement)?.name}")), TrickleDown.TrickleDown);
                    _ui.Root.RegisterCallback<PointerUpEvent>(e => Log(FormattableString.Invariant($"ELEVEN ui pointer up at={e.position.x:F0},{e.position.y:F0}")), TrickleDown.TrickleDown);
                    _ui.Root.RegisterCallback<ClickEvent>(e => Log($"ELEVEN ui click target={(e.target as VisualElement)?.name}"), TrickleDown.TrickleDown);
                }
                // The label's laid-out size once it has one: zero means the font draws nothing.
                Label label = _runAction.Label;
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

        private void UpdateLayout(bool force)
        {
            if (!force && _layout.ScreenWidth == Screen.width && _layout.ScreenHeight == Screen.height
                && _layout.ResultLines == _resultLines.Count) return;

            _layout = HudLayout.For(Screen.width, Screen.height, Screen.safeArea, _resultLines.Count);
            Rect felt = _layout.FeltWrongButton;
            FeltWrongPlace(felt.x, felt.y, felt.width, felt.height, _layout.ScreenWidth, _layout.ScreenHeight);
            if (_ui != null)
            {
                _ui.SetPixelsPerPoint(PixelsPerPoint());
                _ui.Place(_resetAction.Element, _layout.ResetButton);
                _ui.Place(_runAction.Element, _layout.RunButton);
            }
            Log($"ELEVEN layout {_layout.Describe()}");
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            Panel(_layout.BuildLabel);
            GUI.Label(_layout.BuildLabel, BuildLine(), _buildStyle);


            if (_resultLines.Count > 0) DrawResult();

            // The stick's vector is measured from where the thumb landed; its base is drawn as near
            // there as fits in the stick zone, with the knob offset the same way.
            Vector2 origin = _input.StickActive ? _input.StickOrigin : _layout.StickRest;
            Vector2 stickCentre = _layout.KeepInStickZone(origin);
            Vector2 knob = stickCentre + (_input.StickActive ? _input.StickKnob - _input.StickOrigin : Vector2.zero);
            Disc(stickCentre, _layout.StickRadius, HostPalette.ControlIdle);
            Disc(knob, _layout.StickRadius * 0.45f, HostPalette.ControlActive);

            Disc(_layout.KickCentre, _layout.KickRadius, _input.KickHeld ? HostPalette.ControlActive : HostPalette.ControlIdle);
            var kickLabel = new Rect(_layout.KickCentre.x - _layout.KickRadius, _layout.KickCentre.y - _layout.KickRadius,
                _layout.KickRadius * 2, _layout.KickRadius * 2);
            GUI.Label(kickLabel, "KICK", _buttonStyle);
        }

        private void DrawResult()
        {
            Rect panel = _layout.Result;
            Panel(panel);
            float pad = _layout.Padding;
            float widest = 0;
            float columnWidth = panel.width - 2 * pad;
            for (int i = 0; i < _resultLines.Count; i++)
            {
                var line = new Rect(panel.x + pad, panel.y + pad * 0.5f + i * _layout.ResultLineHeight, columnWidth, _layout.ResultLineHeight);
                GUI.Label(line, _resultLines[i], _resultStyle);
                widest = Mathf.Max(widest, _resultStyle.CalcSize(new GUIContent(_resultLines[i])).x);
            }

            if (_resultFitChecked) return;
            _resultFitChecked = true;
            float needed = _resultLines.Count * _layout.ResultLineHeight + pad;
            bool fits = widest <= columnWidth && needed <= panel.height + 0.01f && panel.yMax <= _layout.ScreenHeight
                && !panel.Overlaps(_layout.PlayArea);
            Log(FormattableString.Invariant($"ELEVEN hud result fits={(fits ? "yes" : "NO")} widest={widest:F0} panel={panel.width:F0}x{panel.height:F0} lines={_resultLines.Count} screen={_layout.ScreenWidth}x{_layout.ScreenHeight}"));
        }

        private void EnsureStyles()
        {
            var screen = new Vector2Int(_layout.ScreenWidth, _layout.ScreenHeight);
            if (_textStyle != null && _styledFor == screen) return;

            int fontSize = _layout.FontSize;
            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(8, 8, 4, 4),
            };
            _textStyle.normal.textColor = HostPalette.HudText;

            _buildStyle = new GUIStyle(_textStyle) { fontSize = _layout.BuildFontSize, wordWrap = false, clipping = TextClipping.Clip };
            _buttonStyle = new GUIStyle(_textStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _resultStyle = new GUIStyle(_textStyle) { fontSize = _layout.ResultFontSize, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
            _resultFitChecked = false;
            _styledFor = screen;
        }

        private static void Panel(Rect rect)
        {
            GUI.color = HostPalette.HudPanel;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
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

        /// <summary>
        /// The build id, then the two numbers the device session reads off the screen (#5): how
        /// long the page took to load, and dropped frames over the last minute of play.
        /// </summary>
        private string BuildLine() => FormattableString.Invariant(
            $"ELEVEN {_buildId} · load {_loadSeconds:F1} s · {_pacing.Dropped} dropped in {_pacing.CoveredSeconds:F0} s");

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern double ElevenPageMilliseconds();

        [DllImport("__Internal")]
        private static extern void ElevenFeltWrongInstall(string text, string panel, string border, string focus);

        [DllImport("__Internal")]
        private static extern void ElevenFeltWrongPlace(float x, float y, float width, float height, int screenWidth, int screenHeight);

        [DllImport("__Internal")]
        private static extern string ElevenTakeFeltWrong();

        [DllImport("__Internal")]
        private static extern void ElevenKeepCapture(byte[] bytes, int length, string name, string summary);

        [DllImport("__Internal")]
        private static extern float ElevenCanvasCssWidth();

        // From navigation start, so a cold load includes the download and the engine's start-up.
        private static double PageSeconds() => ElevenPageMilliseconds() / 1000;

        // Screen pixels per CSS pixel, so a UI point is a CSS pixel. Measured against Screen.width,
        // the size Unity renders at, not the canvas element's backing size, which lags at start-up.
        private static float PixelsPerPoint()
        {
            float css = ElevenCanvasCssWidth();
            return css > 0 ? Screen.width / css : 1f;
        }

        // The player captures every key on the page by default, so page controls could never be
        // focused or pressed from a keyboard. The host reads no keys (touch, with the mouse as a
        // finger), so the page keeps them.
        private static void AllowPageKeyboard() => WebGLInput.captureAllKeyboardInput = false;

        private static void FeltWrongInstall(string text, string panel, string border, string focus) =>
            ElevenFeltWrongInstall(text, panel, border, focus);

        private static void FeltWrongPlace(float x, float y, float width, float height, int screenWidth, int screenHeight) =>
            ElevenFeltWrongPlace(x, y, width, height, screenWidth, screenHeight);

        private static string TakeFeltWrong() => ElevenTakeFeltWrong();

        private static void KeepCapture(byte[] bytes, string name, string summary) => ElevenKeepCapture(bytes, bytes.Length, name, summary);
#else
        private static double PageSeconds() => Time.realtimeSinceStartupAsDouble;

        private static float PixelsPerPoint() => Screen.dpi > 0 ? Screen.dpi / 96f : 1f;

        private static void AllowPageKeyboard() { }

        // Outside the web build there is no page: no button, and captures go to persistent data.
        private static void FeltWrongInstall(string text, string panel, string border, string focus) { }

        private static void FeltWrongPlace(float x, float y, float width, float height, int screenWidth, int screenHeight) { }

        private static string TakeFeltWrong() => null;

        private static void KeepCapture(byte[] bytes, string name, string summary) =>
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.persistentDataPath, name), bytes);
#endif

        private static Vector2 ToVector(Vec2 v) => new Vector2((float)v.X, (float)v.Y);

        private static Vector3 ToVector(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        private static void Log(string message) => Debug.Log(message);
    }
}
