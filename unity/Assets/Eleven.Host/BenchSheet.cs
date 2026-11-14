using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Eleven.Presentation;
using Eleven.Simulation;
using Eleven.Simulation.Harness;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>
    /// The feel test bench (#45) as a Unity sheet (ADR-0003): every knob from the supported metadata
    /// (<see cref="BenchCandidate.Knobs"/>), in group tabs, each a value control from the recipes
    /// (−, the value and +, with a full-width slider), so precise tuning never needs a drag. The
    /// controls hold their valid range, so a value outside it can't be made, let alone applied.
    ///
    /// Edits make a pending candidate, said in words; APPLY / RETRY hands it to the host whole, which
    /// starts a fresh run with a new immutable config. Nothing here touches the simulation. Play goes
    /// on under the sheet; the scrim keeps presses from reaching it.
    /// </summary>
    internal sealed class BenchSheet
    {
        private static readonly string[] Models = { "Free", "Attached" };

        // The Pitch view (#72): attacking up, and a second half's switched ends.
        private static readonly string[] Views = { "0°", "180°" };

        // The touch candidates (#28, #76, #77), in ControlCandidate's order.
        private static readonly string[] Controls = { "A · Action", "B · Pass + Kick", "C · One fire" };

        private static readonly string[] Groups =
        {
            TunableParameter.BallGroup, TunableParameter.KickGroup, TunableParameter.PassGroup, TunableParameter.ControlGroup, TunableParameter.AttachedGroup, TunableParameter.GoalGroup, TunableParameter.KeeperGroup,
            TunableParameter.PlayerGroup, ActionSurfaceSettings.Group,
        };

        private readonly UiRoot _ui;
        private readonly bool _trace;
        private readonly Func<BenchCandidate> _applied;
        private readonly Action<BenchCandidate> _apply;
        private readonly Action<ControlModel> _switchModel;
        private readonly Func<bool> _secondHalf;
        private readonly Action<bool> _switchView;
        private readonly Func<int> _activeControls;
        private readonly Action<int> _switchControls;
        private readonly System.Action _closed;
        private readonly Dictionary<string, double> _pending = new Dictionary<string, double>();
        private readonly Dictionary<string, UiKit.Value> _shown = new Dictionary<string, UiKit.Value>();
        private readonly Theme _theme = Theme.Ordinary;

        private readonly VisualElement _scrim;
        private readonly UiKit.Surface _sheet;
        private readonly Label _state;
        private readonly Label _pendingText;
        private readonly Label _problem;
        private readonly UiKit.Segmented _models;
        private readonly UiKit.Segmented _views;
        private readonly UiKit.Segmented _controls;
        private readonly VisualElement _controlsBlock;
        private readonly UiKit.Segmented _tabs;
        private readonly ScrollView _list;
        private readonly UiKit.Action _close;
        private readonly UiKit.Action _revert;
        private readonly UiKit.Action _applyAction;
        private int _group;
        private bool _reportOpen;
        private bool _reportControls;

        public BenchSheet(UiRoot ui, bool trace, Func<BenchCandidate> applied, Action<BenchCandidate> apply, Action<ControlModel> switchModel,
            Func<bool> secondHalf, Action<bool> switchView, Func<int> activeControls, Action<int> switchControls, System.Action closed)
        {
            _secondHalf = secondHalf;
            _switchView = switchView;
            _activeControls = activeControls;
            _switchControls = switchControls;
            _ui = ui;
            _trace = trace;
            _applied = applied;
            _apply = apply;
            _switchModel = switchModel;
            _closed = closed;

            _scrim = new VisualElement { name = "Bench scrim", pickingMode = PickingMode.Position };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = _scrim.style.top = _scrim.style.right = _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = _theme[Role.Scrim].ToColor();
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.alignItems = Align.Center;
            _scrim.style.paddingLeft = _scrim.style.paddingRight = _scrim.style.paddingTop = _scrim.style.paddingBottom = Spacing.M;
            _scrim.style.display = DisplayStyle.None;

            // The sheet takes the height it is given and scrolls its knobs, so its actions stay in reach.
            // It takes all of it, whichever group is shown: sized to its group, a short group (Pass)
            // shrank it, and centred on the screen it moved the tabs and actions from under the finger.
            _sheet = UiKit.MakeSheet(_theme, "Test bench");
            _sheet.Element.name = "Bench sheet";
            _sheet.Element.style.width = Length.Percent(100);
            _sheet.Element.style.maxWidth = 560;
            _sheet.Element.style.height = Length.Percent(100);
            _sheet.Element.style.maxHeight = Length.Percent(100);
            _sheet.Body.style.flexShrink = 1;
            _sheet.Content.style.flexGrow = 1;
            _sheet.Content.style.flexShrink = 1;
            _sheet.Content.style.minHeight = 0;

            _state = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _pendingText = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _pendingText.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pendingText.style.marginBottom = Spacing.S;
            _problem = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _problem.style.display = DisplayStyle.None;
            _problem.style.marginBottom = Spacing.S;
            _sheet.Content.Add(_state);
            _sheet.Content.Add(_pendingText);
            _sheet.Content.Add(_problem);

            // The live FREE ↔ ATTACHED switch (#60): it acts at once and the run goes on, unlike the
            // knobs below, which wait for APPLY / RETRY and a fresh run.
            Label modelsLabel = UiKit.Text(_theme, TypeScale.Body, "Ball control. Switches now; the run goes on.", Role.Text);
            modelsLabel.style.marginBottom = Spacing.Xs;
            _sheet.Content.Add(modelsLabel);
            _models = UiKit.MakeSegmented(ui, _theme, Models, 0, index => _switchModel(index == 1 ? ControlModel.Attached : ControlModel.Free));
            _models.Element.style.marginBottom = Spacing.M;
            _models.Element.style.flexShrink = 0;
            _sheet.Content.Add(_models.Element);

            // The half-time flip, to test (#72): 180° shows a second half's switched ends, still
            // attacking up. It acts at once and the run goes on; only the view and the stick's
            // mapping change.
            Label viewsLabel = UiKit.Text(_theme, TypeScale.Body, "Pitch view. 180° is a second half: ends switched, still attacking up.", Role.Text);
            viewsLabel.style.marginBottom = Spacing.Xs;
            _sheet.Content.Add(viewsLabel);
            _views = UiKit.MakeSegmented(ui, _theme, Views, 0, index => _switchView(index == 1));
            _views.Element.style.marginBottom = Spacing.M;
            _views.Element.style.flexShrink = 0;
            _sheet.Content.Add(_views.Element);

            // The touch candidates (#28, #76): a choice anyone can make without a gesture, at the top
            // of the Touch tab with both candidates' knobs. It acts at once and the run goes on; only
            // the touch adapter changes, and gestures in progress are cancelled. In the tab rather
            // than above the tabs, so a phone's knob list keeps its room.
            _controlsBlock = new VisualElement { name = "Touch controls" };
            Label controlsLabel = UiKit.Text(_theme, TypeScale.Body, "Touch controls. Switches now; the run goes on.", Role.Text);
            controlsLabel.style.marginBottom = Spacing.Xs;
            _controlsBlock.Add(controlsLabel);
            _controls = UiKit.MakeSegmented(ui, _theme, Controls, 0, index => _switchControls(index));
            _controls.Element.style.marginBottom = Spacing.M;
            _controls.Element.style.flexShrink = 0;
            _controlsBlock.Add(_controls.Element);
            _controls.Element.RegisterCallback<GeometryChangedEvent>(_ => Report());

            _tabs = UiKit.MakeSegmented(ui, _theme, Groups, 0, ShowGroup);
            _tabs.Element.style.marginBottom = Spacing.S;
            // The tabs wrap onto a second row on a phone. They must never shrink: squeezed to one
            // row's height, the second row overflows under the knob list, which takes its presses.
            _tabs.Element.style.flexShrink = 0;
            _sheet.Content.Add(_tabs.Element);

            _list = UiKit.MakeScroll();
            _list.style.flexGrow = 1;
            _list.style.flexShrink = 1;
            // A knob's control reached with Tab scrolls into view.
            _list.RegisterCallback<FocusInEvent>(e =>
            {
                if (e.target is VisualElement focused && _list.contentContainer.Contains(focused)) _list.ScrollTo(focused);
            });
            _list.style.minHeight = 0;
            _sheet.Content.Add(_list);

            _close = UiKit.MakeAction(ui, _theme, ActionRole.Navigation, "Close", Close);
            _revert = UiKit.MakeAction(ui, _theme, ActionRole.Utility, "Revert", Revert);
            _applyAction = UiKit.MakeAction(ui, _theme, ActionRole.Primary, "Apply / Retry", Apply);
            _sheet.Actions.style.flexWrap = Wrap.Wrap;
            _sheet.Actions.style.justifyContent = Justify.FlexEnd;
            _sheet.Actions.Add(_close.Element);
            _sheet.Actions.Add(_revert.Element);
            _sheet.Actions.Add(_applyAction.Element);
            UiKit.Gaps(_sheet.Actions);

            _scrim.Add(_sheet.Element);
            ui.Root.Add(_scrim);
            _sheet.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
        }

        public bool Open { get; private set; }

        /// <summary>Opens the sheet on the applied values and any edits still pending from before.</summary>
        public void Show()
        {
            if (Open) return;
            Open = true;
            _scrim.style.display = DisplayStyle.Flex;
            UiKit.Select(_theme, _models, _applied().Config.ControlModel == ControlModel.Attached ? 1 : 0);
            UiKit.Select(_theme, _views, _secondHalf() ? 1 : 0);
            UiKit.Select(_theme, _controls, _activeControls());
            ShowGroup(_group);
            _reportOpen = true;
            Describe();
        }

        public void Close()
        {
            if (!Open) return;
            Open = false;
            _scrim.style.display = DisplayStyle.None;
            Log("ELEVEN bench closed");
            _closed();
        }

        /// <summary>
        /// The sheet's controls in order, for Tab while it is open: the group tabs, each shown knob's
        /// − and +, then Close, Revert and APPLY / RETRY. Built from the controls themselves, not from
        /// every focusable element underneath, which would include the scroll list's hidden scroller
        /// (Codex on #49). Disabled controls are skipped.
        /// </summary>
        public List<VisualElement> FocusOrder()
        {
            var order = new List<VisualElement>();
            foreach (UiKit.Raised model in _models.Segments) order.Add(model.Element);
            foreach (UiKit.Raised view in _views.Segments) order.Add(view.Element);
            foreach (UiKit.Raised tab in _tabs.Segments) order.Add(tab.Element);
            if (TouchShown) foreach (UiKit.Raised control in _controls.Segments) order.Add(control.Element);
            foreach (BenchKnob knob in BenchCandidate.Knobs.Where(k => InGroup(k, _group)))
            {
                if (!_shown.TryGetValue(knob.Name, out UiKit.Value v)) continue;
                order.Add(v.Less.Element);
                order.Add(v.More.Element);
            }

            order.Add(_close.Element);
            order.Add(_revert.Element);
            order.Add(_applyAction.Element);
            return order.Where(e => e.focusable && e.enabledInHierarchy).ToList();
        }

        private void ShowGroup(int index)
        {
            _group = index;
            UiKit.Select(_theme, _tabs, index);
            _list.Clear();
            _shown.Clear();
            BenchCandidate applied = _applied();
            if (TouchShown)
            {
                UiKit.Select(_theme, _controls, _activeControls());
                _list.Add(_controlsBlock);
            }

            foreach (BenchKnob knob in BenchCandidate.Knobs.Where(k => InGroup(k, index)))
            {
                TunableParameter p = knob.Parameter;
                double value = Value(knob, applied);
                BenchKnob k = knob;
                UiKit.Value control = UiKit.MakeValue(_ui, _theme, knob.Label, p.Unit, Meta(knob, applied), p.Min, p.Max, knob.Step, knob.StepDecimals, value, v => Changed(k, v));
                control.Element.name = knob.Name;
                control.Element.RegisterCallback<GeometryChangedEvent>(_ => Report());
                _shown[knob.Name] = control;
                _list.Add(control.Element);
            }

            _reportControls = true;
            if (_trace) Log($"ELEVEN bench group name={Groups[index]}");
        }

        // The Touch tab holds every candidate's knobs, A's, B's then C's, under the A / B / C choice
        // (#76, #77).
        private bool TouchShown => Groups[_group] == ActionSurfaceSettings.Group;

        private static bool InGroup(BenchKnob knob, int group) =>
            knob.Group == Groups[group]
            || (Groups[group] == ActionSurfaceSettings.Group && (knob.Group == SplitSettings.Group || knob.Group == OneFireSettings.Group));

        private void Changed(BenchKnob knob, double value)
        {
            BenchCandidate applied = _applied();
            if (BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(applied.Get(knob.Name))) _pending.Remove(knob.Name);
            else _pending[knob.Name] = value;
            if (_shown.TryGetValue(knob.Name, out UiKit.Value control)) control.Meta.text = Meta(knob, applied);
            _problem.style.display = DisplayStyle.None;
            string limit = value >= knob.Parameter.Max ? "max" : value <= knob.Parameter.Min ? "min" : "none";
            Log(FormattableString.Invariant($"ELEVEN bench value name={knob.Name} value={Number(value)} limit={limit} pending={_pending.Count}"));
            Describe();
        }

        private void Revert()
        {
            _pending.Clear();
            _problem.style.display = DisplayStyle.None;
            ShowGroup(_group);
            Log("ELEVEN bench reverted pending=0");
            Describe();
        }

        // APPLY / RETRY: the pending edits on the applied values, refused whole with the reason if
        // anything is invalid, otherwise handed to the host for a fresh run. With no edits it is a
        // retry: the same values, a fresh run.
        private void Apply()
        {
            BenchCandidate applied = _applied();
            if (!applied.TryWith(_pending, out BenchCandidate candidate, out string problem) || candidate == null)
            {
                _problem.text = $"Can't apply: {problem}";
                _problem.style.display = DisplayStyle.Flex;
                Log($"ELEVEN bench refused {problem}");
                return;
            }

            _pending.Clear();
            _apply(candidate);
            Close();
        }

        // What is running and what is pending, in words: state never rests on colour.
        private void Describe()
        {
            BenchCandidate applied = _applied();
            int fromReference = applied.ChangesFrom(BenchCandidate.Reference);
            _state.text = fromReference == 0
                ? "Running the technical reference values."
                : $"Running unsaved values: {fromReference} changed from the technical reference.";
            _pendingText.text = _pending.Count == 0
                ? "No pending changes. APPLY / RETRY starts a fresh run with these values."
                : $"{_pending.Count} pending change{(_pending.Count == 1 ? "" : "s")}. APPLY / RETRY starts a fresh run with {(_pending.Count == 1 ? "it" : "them")}.";
            Log($"ELEVEN bench pending text={_pendingText.text}");
        }

        private double Value(BenchKnob knob, BenchCandidate applied) =>
            _pending.TryGetValue(knob.Name, out double v) ? v : applied.Get(knob.Name);

        // The applied value, whether this knob has a pending edit, and its ranges: in words.
        private string Meta(BenchKnob knob, BenchCandidate applied)
        {
            TunableParameter p = knob.Parameter;
            string current = _pending.ContainsKey(knob.Name)
                ? $"Changed, applied {Number(applied.Get(knob.Name))}"
                : "Applied";
            string start = p.StartMin.HasValue && p.StartMax.HasValue
                ? $"research start {Number(p.StartMin.Value)}–{Number(p.StartMax.Value)}"
                : "technical reference";
            return $"{current} · {start} · valid {Number(p.Min)}–{Number(p.Max)}";
        }

        // Once laid out: where the controls are, for the smoke test, and whether every target is at
        // least 44 × 44 pt with nothing wider than the screen.
        private void Report()
        {
            if (!Open || (!_reportOpen && !_reportControls)) return;
            float s = _ui.PixelsPerPoint;
            string P(VisualElement e) => FormattableString.Invariant($"{e.worldBound.center.x * s:F0},{e.worldBound.center.y * s:F0}");

            if (_reportOpen)
            {
                _reportOpen = false;
                string tabs = string.Join(";", Groups.Select((g, i) => $"{g}={P(_tabs.Segments[i].Element)}"));
                string models = string.Join(";", Models.Select((m, i) => $"{m}={P(_models.Segments[i].Element)}"));
                string views = string.Join(";", new[] { "0", "180" }.Select((v, i) => $"{v}={P(_views.Segments[i].Element)}"));
                Log($"ELEVEN bench open knobs={BenchCandidate.Knobs.Count} group={Groups[_group]} tabs={tabs} apply={P(_applyAction.Element)} revert={P(_revert.Element)} close={P(_close.Element)} models={models} views={views}");

                float screen = _ui.Root.layout.width;
                float smallest = float.MaxValue;
                string smallestName = "none";
                string overflow = "none";
                int targets = 0;
                _sheet.Element.Query<VisualElement>().ForEach(v =>
                {
                    Rect r = v.worldBound;
                    bool target = v.pickingMode == PickingMode.Position && (v.focusable || (v.name ?? "").EndsWith(" slider", StringComparison.Ordinal));
                    if (target && r.width > 0)
                    {
                        targets++;
                        float side = Mathf.Min(r.width, r.height);
                        if (side < smallest)
                        {
                            smallest = side;
                            smallestName = v.name;
                        }
                    }

                    if (overflow == "none" && r.width > 0 && (r.xMax > screen + 0.5f || r.xMin < -0.5f)) overflow = string.IsNullOrEmpty(v.name) ? v.GetType().Name : v.name;
                });
                Log(FormattableString.Invariant(
                    $"ELEVEN bench targets={targets} smallest={(targets > 0 ? smallest : 0):F1} smallestName={(smallestName ?? "none").Replace(' ', '_')} overflow={overflow.Replace(' ', '_')}"));

                // Where Tab goes while the sheet is open, and how many of those stops can't be seen.
                List<VisualElement> order = FocusOrder();
                int hidden = order.Count(Hidden);
                Log($"ELEVEN bench focus order={string.Join(",", order.Select(v => (v.name ?? "").Replace(' ', '_')))} count={order.Count} hidden={hidden}");
            }

            if (_reportControls && _trace)
            {
                _reportControls = false;
                if (TouchShown)
                {
                    Log($"ELEVEN bench candidates A={P(_controls.Segments[0].Element)};B={P(_controls.Segments[1].Element)};C={P(_controls.Segments[2].Element)}");
                }

                foreach (BenchKnob knob in BenchCandidate.Knobs.Where(k => InGroup(k, _group)))
                {
                    if (!_shown.TryGetValue(knob.Name, out UiKit.Value v)) continue;
                    Rect track = v.Track.worldBound;
                    Log(FormattableString.Invariant(
                        $"ELEVEN bench control name={knob.Name} value={Number(v.Current)} less={P(v.Less.Element)} more={P(v.More.Element)} track={track.xMin * s:F0},{track.xMax * s:F0},{track.center.y * s:F0}"));
                }
            }
        }

        // Whether an element can't be seen: it or an ancestor is not displayed or hidden, or it has no size.
        private static bool Hidden(VisualElement e)
        {
            for (VisualElement v = e; v != null; v = v.parent)
            {
                if (v.resolvedStyle.display == DisplayStyle.None || v.resolvedStyle.visibility == Visibility.Hidden) return true;
            }

            return !(e.worldBound.width > 0) || !(e.worldBound.height > 0);
        }

        private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static void Log(string message) => Debug.Log(message);
    }
}
