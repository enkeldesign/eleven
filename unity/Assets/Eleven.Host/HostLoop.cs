using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Harness;
using Eleven.Simulation.Maths;
using UnityEngine;

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

        private readonly List<Command> _commands = new List<Command>(1);
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly Dictionary<MathBackend, string> _scenarioResults = new Dictionary<MathBackend, string>();

        private SimConfig _config;
        private Simulator _simulator;
        private PitchView _view;
        private TouchAdapter _input;
        private HudLayout _layout;
        private string _buildId;
        private bool _trace;
        private bool _paused;
        private double _lastTime;
        private double _accumulator;
        private Vector2 _previousDot;
        private Vector3 _previousBall;
        private GUIStyle _textStyle;
        private GUIStyle _buttonStyle;
        private int _styledForHeight;
        private int _framesSinceTrace;
        private double _lastTraceTime;

        private void Awake()
        {
            Application.runInBackground = false;
            _buildId = Application.version;
            string url = Application.absoluteURL ?? "";
            _trace = url.Contains("trace");

            _config = SpikeScenario.Config(MathBackend.Portable);
            _simulator = new Simulator(_config, SpikeScenario.InitialState(_config));
            _view = new PitchView(_config, transform);
            _input = new TouchAdapter(SpikeScenario.ControllerId);
            if (_trace) _input.PressLogged += press => Log($"ELEVEN press {press}");
            RememberPrevious();
            UpdateLayout(force: true);
            _lastTime = Time.realtimeSinceStartupAsDouble;

            Log($"ELEVEN host ready build={_buildId} maths={_config.MathBackend} tickRate={_config.TickRate} config={_config.Hash:x16}");

            if (url.Contains("autorun"))
            {
                RunScenario(MathBackend.SystemMath);
                RunScenario(MathBackend.Portable);
            }
        }

        private void OnDestroy() => _input?.Dispose();

        private void OnApplicationPause(bool paused)
        {
            _paused = paused;
            _accumulator = 0;
            _lastTime = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            _framesSinceTrace++;
            UpdateLayout(force: false);
            _input.Sample(_layout);

            if (_input.ResetTapped) ResetMatch();
            if (_input.MathsTapped) SwitchMaths();
            if (_input.RunTapped) RunScenario(_config.MathBackend);

            double now = Time.realtimeSinceStartupAsDouble;
            double frame = now - _lastTime;
            _lastTime = now;

            if (_paused || frame > MaxFrameSeconds) _accumulator = 0;
            else _accumulator += frame;

            double tick = _config.TickSeconds;
            int steps = 0;
            while (_accumulator >= tick && steps < MaxStepsPerFrame)
            {
                StepOnce();
                _accumulator -= tick;
                steps++;
            }

            if (_accumulator >= tick) _accumulator = 0;

            float alpha = (float)(_accumulator / tick);
            SimState state = _simulator.PeekState();
            _view.Render(
                Vector2.Lerp(_previousDot, ToVector(state.Dots[0].Position), alpha),
                Vector3.Lerp(_previousBall, ToVector(state.Ball.Position), alpha));
        }

        private void StepOnce()
        {
            RememberPrevious();
            _commands.Clear();
            _commands.Add(_input.CommandFor(_simulator.Tick));
            _events.Clear();
            _simulator.Step(_commands, _events);

            if (!_trace) return;

            foreach (SimEvent e in _events) Log($"ELEVEN event {e}");
            if (_simulator.Tick % TraceEveryTicks == 0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                double fps = _framesSinceTrace / Math.Max(1e-6, now - _lastTraceTime);
                _framesSinceTrace = 0;
                _lastTraceTime = now;
                SimState s = _simulator.PeekState();
                Log(FormattableString.Invariant($"ELEVEN trace tick={s.Tick} dot={s.Dots[0].Position.X:F2},{s.Dots[0].Position.Y:F2} ball={s.Ball.Position.X:F2},{s.Ball.Position.Y:F2},{s.Ball.Position.Z:F2} fps={fps:F1} hash={_simulator.StateHash():x16}"));
            }
        }

        private void RememberPrevious()
        {
            SimState state = _simulator.PeekState();
            _previousDot = ToVector(state.Dots[0].Position);
            _previousBall = ToVector(state.Ball.Position);
        }

        private void ResetMatch()
        {
            _simulator = new Simulator(_config, SpikeScenario.InitialState(_config));
            _input.Clear();
            RememberPrevious();
            Log($"ELEVEN reset maths={_config.MathBackend}");
        }

        /// <summary>Continues the live match from its current state under the other maths backend.</summary>
        private void SwitchMaths()
        {
            MathBackend next = _config.MathBackend == MathBackend.Portable ? MathBackend.SystemMath : MathBackend.Portable;
            _config = SpikeScenario.Config(next);
            _simulator = new Simulator(_config, _simulator.CopyState());
            Log($"ELEVEN maths={next} config={_config.Hash:x16}");
        }

        /// <summary>Runs the whole issue 1 scenario inside this build and shows its hashes.</summary>
        private void RunScenario(MathBackend backend)
        {
            double started = Time.realtimeSinceStartupAsDouble;
            ScenarioRun run = ScenarioRunner.Run(SpikeScenario.Config(backend));
            double milliseconds = (Time.realtimeSinceStartupAsDouble - started) * 1000;

            ulong final = run.TickHashes[run.TickHashes.Count - 1];
            ulong digest = run.SequenceDigest();
            _scenarioResults[backend] = FormattableString.Invariant(
                $"{backend}: digest {digest:x16}  final {final:x16}  ({run.TickHashes.Count} hashes, {milliseconds:F0} ms)");
            Log(FormattableString.Invariant($"ELEVEN scenario name={SpikeScenario.Name} maths={backend} digest={digest:x16} final={final:x16} hashes={run.TickHashes.Count} events={run.Events.Count} config={run.Config.Hash:x16} ms={milliseconds:F0}"));
        }

        private void UpdateLayout(bool force)
        {
            if (!force && _layout.ScreenWidth == Screen.width && _layout.ScreenHeight == Screen.height) return;

            _layout = HudLayout.For(Screen.width, Screen.height, Screen.safeArea);
            Log($"ELEVEN layout {_layout.Describe()}");
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            Panel(_layout.BuildLabel);
            GUI.Label(_layout.BuildLabel, $"ELEVEN build {_buildId}", _textStyle);

            Button(_layout.ResetButton, "Reset");
            Button(_layout.RunButton, "Run scenario");
            Button(_layout.MathsButton, $"Maths: {_config.MathBackend}");

            if (_scenarioResults.Count > 0)
            {
                Panel(_layout.Result);
                string text = string.Join("\n", _scenarioResults.Values);
                GUI.Label(_layout.Result, text, _textStyle);
            }

            Vector2 stickCentre = _input.StickActive ? _input.StickOrigin : _layout.StickRest;
            Vector2 knob = _input.StickActive ? _input.StickKnob : _layout.StickRest;
            Disc(stickCentre, _layout.StickRadius, HostPalette.ControlIdle);
            Disc(knob, _layout.StickRadius * 0.45f, HostPalette.ControlActive);

            Disc(_layout.KickCentre, _layout.KickRadius, _input.KickHeld ? HostPalette.ControlActive : HostPalette.ControlIdle);
            var kickLabel = new Rect(_layout.KickCentre.x - _layout.KickRadius, _layout.KickCentre.y - _layout.KickRadius,
                _layout.KickRadius * 2, _layout.KickRadius * 2);
            GUI.Label(kickLabel, "KICK", _buttonStyle);
        }

        private void EnsureStyles()
        {
            if (_textStyle != null && _styledForHeight == _layout.ScreenHeight) return;

            int fontSize = Mathf.Max(12, Mathf.RoundToInt(_layout.Unit * 0.36f));
            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(8, 8, 4, 4),
            };
            _textStyle.normal.textColor = HostPalette.HudText;

            _buttonStyle = new GUIStyle(_textStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _styledForHeight = _layout.ScreenHeight;
        }

        private void Button(Rect rect, string label)
        {
            Panel(rect);
            GUI.color = HostPalette.ControlIdle;
            GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, label, _buttonStyle);
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

        private static Vector2 ToVector(Vec2 v) => new Vector2((float)v.X, (float)v.Y);

        private static Vector3 ToVector(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        private static void Log(string message) => Debug.Log(message);
    }
}
