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
        private static readonly string[] Plays = { "Practice", "1v1", "2v2", "5v5", "11v11" };
        private readonly Func<int> _play;
        private readonly Action<int> _switchPlay;
        private UiKit.Segmented _plays;

        // How automatic switching is (#136), in SwitchMode's order.
        private static readonly string[] Switchings = { "Auto", "On the ball", "Manual" };
        private UiKit.Segmented _switchings;
        private readonly VisualElement _switchingBlock = new VisualElement { name = "Switching" };
        private readonly Func<int> _switching;
        private readonly Action<int> _switchSwitching;
        private VisualElement _playBlock;

        private static readonly string[] Groups =
        {
            TunableParameter.BallGroup, TunableParameter.KickGroup, TunableParameter.PassGroup, TunableParameter.ControlGroup, TunableParameter.AttachedGroup, TunableParameter.GoalGroup, TunableParameter.KeeperGroup,
            TunableParameter.PlayerGroup, ActionSurfaceSettings.Group, PresetsTab,
        };

        // The last tab (#46): named presets, not a group of knobs.
        private const string PresetsTab = "Presets";

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
        private readonly Func<string> _loadPresets;
        // Writes the shelf to the device; false when the device refused it.
        private readonly Func<string, bool> _storePresets;
        private readonly Func<string> _build;
        private readonly UiKit.TextInput _presetName;
        private readonly UiKit.Action _presetSave;
        private readonly Label _presetStatus;
        private readonly List<VisualElement> _presetStops = new List<VisualElement>();
        private readonly List<(string Name, VisualElement Load, VisualElement Delete)> _presetPlaces = new List<(string, VisualElement, VisualElement)>();
        private PresetShelf _shelf = new PresetShelf();
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
        private readonly Label _modelsLabel;
        private readonly Label _viewsLabel;
        // The wide arrangement's columns (#31): the live switches over the actions, then the tabs.
        private readonly VisualElement _switchColumn;
        private readonly ScrollView _switchScroll;
        private readonly ScrollView _tabScroll;
        private string _arranged;
        private readonly VisualElement _liveBlock = new VisualElement { name = "Bench live" };
        private readonly UiKit.Action _close;
        private readonly UiKit.Action _revert;
        private readonly UiKit.Action _applyAction;
        private int _group;
        private bool _reportOpen;
        private bool _reportControls;

        public BenchSheet(UiRoot ui, bool trace, Func<BenchCandidate> applied, Action<BenchCandidate> apply, Action<ControlModel> switchModel,
            Func<bool> secondHalf, Action<bool> switchView, Func<int> activeControls, Action<int> switchControls,
            Func<string> loadPresets, Func<string, bool> storePresets, Func<string> build, System.Action closed,
            Func<int> play = null, Action<int> switchPlay = null, Func<int> switching = null, Action<int> switchSwitching = null)
        {
            _play = play ?? (() => 0);
            _switchPlay = switchPlay ?? (_ => { });
            _switching = switching ?? (() => 0);
            _switchSwitching = switchSwitching ?? (_ => { });
            _loadPresets = loadPresets;
            _storePresets = storePresets;
            _build = build;
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
            // Only the knob list gives way when the sheet is short of room. Left to shrink, the
            // title and the actions' row were squeezed in proportion, and the actions hung out of
            // the sheet's bottom (#31).
            _sheet.Actions.style.flexShrink = 0;
            if (_sheet.Title != null) _sheet.Title.style.flexShrink = 0;

            _state = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _pendingText = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _pendingText.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pendingText.style.marginBottom = Spacing.S;
            _problem = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _problem.style.display = DisplayStyle.None;
            _problem.style.marginBottom = Spacing.S;
            _state.style.flexShrink = _pendingText.style.flexShrink = _problem.style.flexShrink = 0;

            // The live FREE ↔ ATTACHED switch (#60): it acts at once and the run goes on, unlike the
            // knobs below, which wait for APPLY / RETRY and a fresh run.
            _modelsLabel = UiKit.Text(_theme, TypeScale.Body, ModelsText, Role.Text);
            _modelsLabel.style.marginBottom = Spacing.Xs;
            _modelsLabel.style.flexShrink = 0;
            _models = UiKit.MakeSegmented(ui, _theme, Models, 0, index => _switchModel(index == 1 ? ControlModel.Attached : ControlModel.Free));
            _models.Element.style.marginBottom = Spacing.M;
            _models.Element.style.flexShrink = 0;

            // The half-time flip, to test (#72): 180° shows a second half's switched ends, still
            // attacking up. It acts at once and the run goes on; only the view and the stick's
            // mapping change.
            _viewsLabel = UiKit.Text(_theme, TypeScale.Body, ViewsText, Role.Text);
            _viewsLabel.style.marginBottom = Spacing.Xs;
            _viewsLabel.style.flexShrink = 0;
            _views = UiKit.MakeSegmented(ui, _theme, Views, 0, index => _switchView(index == 1));
            _views.Element.style.marginBottom = Spacing.M;
            _views.Element.style.flexShrink = 0;

            // The touch candidates (#28, #76): a choice anyone can make without a gesture, at the top
            // of the Touch tab with both candidates' knobs. It acts at once and the run goes on; only
            // the touch adapter changes, and gestures in progress are cancelled. In the tab rather
            // than above the tabs, so a phone's knob list keeps its room.
            _controlsBlock = new VisualElement { name = "Touch controls" };

            // What is played (#87): practice, or a 1v1 match against the CPU from its kick-off. It
            // acts at once, as the other live switches do. At the top of the Presets tab, the
            // session's tab, so the Touch tab's choices keep their places on a phone.
            _playBlock = new VisualElement { name = "Play" };
            Label playLabel = UiKit.Text(_theme, TypeScale.Body, "Play. Switches now: a match starts at its kick-off.", Role.Text);
            playLabel.style.marginBottom = Spacing.Xs;
            _playBlock.Add(playLabel);
            _plays = UiKit.MakeSegmented(ui, _theme, Plays, 0, index => _switchPlay(index));
            _plays.Element.style.marginBottom = Spacing.M;
            _plays.Element.style.flexShrink = 0;
            _playBlock.Add(_plays.Element);
            _plays.Element.RegisterCallback<GeometryChangedEvent>(_ => ReportPlay());
            if (_trace)
            {
                // Where keyboard focus goes inside the sheet (#144), as the HUD reports its own (#53).
                _sheet.Element.RegisterCallback<FocusInEvent>(e =>
                {
                    if (e.target is VisualElement v && FocusOrder().Contains(v)) Log($"ELEVEN bench focus name={(v.name ?? "").Replace(' ', '_')}");
                });
            }

            // Switching players (#136): how automatic it is. It acts at once and the match goes on.
            // At the end of the Presets tab, so nothing above the presets moves.
            Label switchingLabel = UiKit.Text(_theme, TypeScale.Body, "Switching players. Switches now. On the ball: only to your pass's receiver, and on SWITCH. Manual: only on SWITCH.", Role.Text);
            switchingLabel.style.marginTop = Spacing.M;
            switchingLabel.style.marginBottom = Spacing.Xs;
            _switchingBlock.Add(switchingLabel);
            _switchings = UiKit.MakeSegmented(ui, _theme, Switchings, 0, index => _switchSwitching(index));
            _switchings.Element.style.marginBottom = Spacing.M;
            _switchings.Element.style.flexShrink = 0;
            _switchingBlock.Add(_switchings.Element);
            _switchings.Element.RegisterCallback<GeometryChangedEvent>(_ => Report());

            Label controlsLabel = UiKit.Text(_theme, TypeScale.Body, "Touch controls. Switches now; the run goes on.", Role.Text);
            controlsLabel.style.marginBottom = Spacing.Xs;
            _controlsBlock.Add(controlsLabel);
            _controls = UiKit.MakeSegmented(ui, _theme, Controls, 0, index => _switchControls(index));
            _controls.Element.style.marginBottom = Spacing.M;
            _controls.Element.style.flexShrink = 0;
            _controlsBlock.Add(_controls.Element);
            _controls.Element.RegisterCallback<GeometryChangedEvent>(_ => Report());

            // Named presets (#46): a name to save the current values under, and what happened last.
            _presetName = UiKit.MakeTextInput(_theme, "Preset name", BenchPreset.MaxNameLength);
            _presetName.Element.style.flexGrow = 1;
            _presetName.Element.style.flexShrink = 1;
            _presetName.Element.style.marginRight = Spacing.S;
            _presetSave = UiKit.MakeAction(ui, _theme, ActionRole.Supporting, "Save preset", SavePreset, "Save");
            _presetStatus = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _presetStatus.style.unityFontStyleAndWeight = FontStyle.Bold;
            _presetStatus.style.marginBottom = Spacing.S;
            _presetSave.Element.RegisterCallback<GeometryChangedEvent>(_ => Report());

            _tabs = UiKit.MakeSegmented(ui, _theme, Groups, 0, ShowGroup);
            _tabs.Element.style.marginBottom = Spacing.S;
            // The tabs wrap onto a second row on a phone. They must never shrink: squeezed to one
            // row's height, the second row overflows under the knob list, which takes its presses.
            _tabs.Element.style.flexShrink = 0;

            _list = UiKit.MakeScroll();
            _list.style.flexGrow = 1;
            _list.style.flexShrink = 1;
            // A knob's control reached with Tab scrolls into view.
            _list.RegisterCallback<FocusInEvent>(e =>
            {
                if (e.target is VisualElement focused && _list.contentContainer.Contains(focused)) _list.ScrollTo(focused);
            });

            // A scrolled Presets tab reports its places again, for the smoke test.
            _list.verticalScroller.valueChanged += _ =>
            {
                if (!_trace || !PresetsShown) return;
                _reportControls = true;
                Report();
            };
            _list.style.minHeight = 0;
            _list.style.minWidth = 0;

            // The wide arrangement (#31), for a screen too short to stack all of this: a landscape
            // phone. Three columns side by side, each scrolling on its own if it must.
            _switchScroll = UiKit.MakeScroll();
            _switchScroll.style.flexGrow = 1;
            _switchScroll.style.flexShrink = 1;
            _switchScroll.style.minHeight = 0;
            _switchColumn = new VisualElement { name = "Bench switches" };
            _switchColumn.style.width = Length.Percent(30);
            _switchColumn.style.flexShrink = 0;
            _switchColumn.style.marginRight = Spacing.M;
            _tabScroll = UiKit.MakeScroll();
            _tabScroll.name = "Bench tabs";
            _tabScroll.style.width = Length.Percent(27);
            _tabScroll.style.flexShrink = 0;
            _tabScroll.style.marginRight = Spacing.M;

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
            Arrange();
            // The screen turned or was resized with the sheet open: the other arrangement, at once.
            _scrim.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (Open) Arrange();
            });
        }

        private const string ModelsText = "Ball control. Switches now; the run goes on.";
        private const string ViewsText = "Pitch view. 180° is a second half: ends switched, still attacking up.";

        /// <summary>A screen shorter than this many points, and wider than it is tall, gets the wide arrangement (#31).</summary>
        public const float WideBelowHeight = 480;

        /// <summary>A screen shorter than this many points, and not wide, gets the compact arrangement (#31): every phone in portrait.</summary>
        public const float CompactBelowHeight = 720;

        /// <summary>Under this many points high, the compact arrangement's live switches scroll with the knobs too (#31): a compact phone.</summary>
        public const float SwitchesScrollBelowHeight = 600;

        // Whether the live switches scroll with the knobs: the compact arrangement on a screen too short for them to stay put.
        private bool _switchesScroll;

        /// <summary>The arrangement a screen of this size, in points, calls for: stacked, wide or compact.</summary>
        public static string ArrangementFor(float width, float height) =>
            height > 0 && height < WideBelowHeight && width > height ? "wide"
            : height > 0 && height < CompactBelowHeight ? "compact"
            : "stacked";

        /// <summary>
        /// Lays the sheet out for the screen it has (#31).
        ///
        /// Stacked, where there is height for it: the state, the live switches, the tabs, the knob
        /// list and the actions, one over the other, with the list taking what is left.
        ///
        /// Wide, on a screen too short for that (a landscape phone, where the stacked rows left the
        /// knob list no room at all): three columns. The live switches over the actions; the tabs;
        /// and the knob list at the sheet's full height. The title goes and the labels shorten, so
        /// every switch, every tab and every action is in reach without scrolling.
        /// </summary>
        private void Arrange()
        {
            Rect screen = _ui.Root.layout;
            string arranged = ArrangementFor(screen.width, screen.height);
            bool switchesScroll = arranged == "compact" && screen.height < SwitchesScrollBelowHeight;
            if (_arranged == arranged && _switchesScroll == switchesScroll) return;
            _arranged = arranged;
            _switchesScroll = switchesScroll;
            bool wide = arranged == "wide", compact = arranged == "compact";

            _sheet.Content.Clear();
            _liveBlock.Clear();
            _modelsLabel.text = wide || compact ? "Ball control" : ModelsText;
            _viewsLabel.text = wide || compact ? "Pitch view (180°: second half)" : ViewsText;
            if (wide)
            {
                _switchScroll.Add(_modelsLabel);
                _switchScroll.Add(_models.Element);
                _switchScroll.Add(_viewsLabel);
                _switchScroll.Add(_views.Element);
                _switchScroll.Add(_state);
                _switchScroll.Add(_pendingText);
                _switchScroll.Add(_problem);
                _switchColumn.Add(_switchScroll);
                _switchColumn.Add(_sheet.Actions);
                _sheet.Actions.style.marginTop = Spacing.S;
                _sheet.Actions.style.justifyContent = Justify.FlexStart;
                _tabScroll.Add(_tabs.Element);
                _sheet.Content.style.flexDirection = FlexDirection.Row;
                _sheet.Content.Add(_switchColumn);
                _sheet.Content.Add(_tabScroll);
                _sheet.Content.Add(_list);
                _sheet.Element.style.maxWidth = StyleKeyword.None;
            }
            else
            {
                _sheet.Content.style.flexDirection = FlexDirection.Column;
                // Compact: the title goes, and what is running and pending scrolls with the knobs,
                // after them, so the list has room. The live switches, the tabs and the actions stay put.
                // On a compact phone the live switches scroll after the knobs as well.
                VisualElement status = compact ? _liveBlock : _sheet.Content;
                VisualElement switches = _switchesScroll ? _liveBlock : _sheet.Content;
                status.Add(_state);
                status.Add(_pendingText);
                status.Add(_problem);
                switches.Add(_modelsLabel);
                switches.Add(_models.Element);
                switches.Add(_viewsLabel);
                switches.Add(_views.Element);
                _sheet.Content.Add(_tabs.Element);
                _sheet.Content.Add(_list);
                _sheet.Body.Add(_sheet.Actions);
                _sheet.Actions.style.marginTop = compact ? Spacing.S : Spacing.L;
                _sheet.Actions.style.justifyContent = Justify.FlexEnd;
                _sheet.Element.style.maxWidth = 560;
            }

            if (_sheet.Title != null) _sheet.Title.style.display = wide || compact ? DisplayStyle.None : DisplayStyle.Flex;

            // Wrapped onto two rows in a narrow column, the actions need a gap between the rows.
            foreach (UiKit.Action action in new[] { _close, _revert, _applyAction }) action.Element.style.marginBottom = wide || compact ? Spacing.Xs : 0;

            // The list holds the live block in the compact arrangement.
            if (Open) ShowGroup(_group);
            _reportOpen = Open;
            _reportControls = Open;
            if (_trace) Log($"ELEVEN bench arranged={arranged} screen={screen.width:F0}x{screen.height:F0}");
        }

        public bool Open { get; private set; }

        /// <summary>The screen's unsafe edges, in points (#31): the sheet keeps clear of them.</summary>
        public void Inset(float top, float right, float bottom, float left)
        {
            _scrim.style.paddingTop = Spacing.M + top;
            _scrim.style.paddingRight = Spacing.M + right;
            _scrim.style.paddingBottom = Spacing.M + bottom;
            _scrim.style.paddingLeft = Spacing.M + left;
        }

        /// <summary>Opens the sheet on the applied values and any edits still pending from before.</summary>
        public void Show()
        {
            if (Open) return;
            Open = true;
            _scrim.style.display = DisplayStyle.Flex;
            _shelf = PresetShelf.Read(_loadPresets());
            Arrange();
            UiKit.Select(_theme, _models, _applied().Config.ControlModel == ControlModel.Attached ? 1 : 0);
            UiKit.Select(_theme, _views, _secondHalf() ? 1 : 0);
            UiKit.Select(_theme, _controls, _activeControls());
            UiKit.Select(_theme, _plays, _play());
            UiKit.Select(_theme, _switchings, _switching());
            _presetStatus.text = "";
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
            if (PresetsShown)
            {
                // The play switch (#144), the presets, then the switching mode (#136), as on screen.
                foreach (UiKit.Raised play in _plays.Segments) order.Add(play.Element);
                order.AddRange(_presetStops);
                foreach (UiKit.Raised switching in _switchings.Segments) order.Add(switching.Element);
            }
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

            if (PresetsShown) ShowPresets(applied);
            if (_arranged == "compact") _list.Add(_liveBlock);
            _reportControls = true;
            if (_trace) Log($"ELEVEN bench group name={Groups[index]}");
        }

        private bool PresetsShown => Groups[_group] == PresetsTab;

        // Where the play switch's choices are, for the smoke test (#87), once laid out.
        private void ReportPlay()
        {
            if (!_trace || !Open || !PresetsShown) return;
            float s = _ui.PixelsPerPoint;
            string P(VisualElement e) => FormattableString.Invariant($"{e.worldBound.center.x * s:F0},{e.worldBound.center.y * s:F0}");
            Log($"ELEVEN bench play practice={P(_plays.Segments[0].Element)} match={P(_plays.Segments[1].Element)} match2={P(_plays.Segments[2].Element)} match5={P(_plays.Segments[3].Element)} match11={P(_plays.Segments[4].Element)}");
        }

        // The Presets tab (#46): a name and Save, TECHNICAL REFERENCE, then each saved preset with
        // Load and Delete, then any stored preset this build can't read, with the reason.
        private void ShowPresets(BenchCandidate applied)
        {
            UiKit.Select(_theme, _plays, _play());
            UiKit.Select(_theme, _switchings, _switching());
            _list.Add(_playBlock);
            _presetStops.Clear();
            _presetPlaces.Clear();
            Label intro = UiKit.Text(_theme, TypeScale.Body,
                "Save the values shown, pending changes included, under a short name. Load sets a preset's values as pending changes. A preset is not approved feel.", Role.Text);
            intro.style.marginBottom = Spacing.S;
            _list.Add(intro);

            VisualElement save = UiKit.Row(Spacing.S);
            save.style.marginBottom = Spacing.Xs;
            save.Add(_presetName.Element);
            save.Add(_presetSave.Element);
            _list.Add(save);
            _list.Add(_presetStatus);
            _presetStops.Add(_presetName.Field);
            _presetStops.Add(_presetSave.Element);

            PresetRow(BenchPreset.TechnicalReference, applied, deletable: false);
            foreach (BenchPreset suggested in BenchPreset.Suggested) PresetRow(suggested, applied, deletable: false);
            foreach (BenchPreset preset in _shelf.Presets) PresetRow(preset, applied, deletable: true);
            foreach (string problem in _shelf.Problems)
            {
                Label unread = UiKit.Text(_theme, TypeScale.Body, $"Can't be loaded. {problem}", Role.Text);
                unread.style.marginTop = Spacing.S;
                _list.Add(unread);
            }

            _list.Add(_switchingBlock);
        }

        // Whether a preset is what is running: its values, its ball control model and its touch candidate.
        private bool Running(BenchPreset preset, BenchCandidate applied) =>
            preset.Candidate.ChangesFrom(applied) == 0 && preset.Model == applied.Config.ControlModel && (int)preset.Controls == _activeControls();

        private void PresetRow(BenchPreset preset, BenchCandidate applied, bool deletable)
        {
            int fromReference = preset.Candidate.ChangesFrom(BenchCandidate.Reference);
            string about = !deletable ? "The untuned reference values."
                : $"{fromReference} changed from the reference · {Controls[(int)preset.Controls]} · {(preset.Model == ControlModel.Attached ? "Attached" : "Free")} · build {(preset.Build.Length > 0 ? preset.Build : "unknown")}";
            if (Running(preset, applied)) about += " · running";

            var row = new VisualElement { name = $"Preset {preset.Name}" };
            row.style.marginTop = Spacing.S;
            Label name = UiKit.Text(_theme, TypeScale.Label, preset.Name, Role.TextHeading);
            Label meta = UiKit.Text(_theme, TypeScale.Body, about, Role.Text);
            meta.style.marginBottom = Spacing.Xs;
            row.Add(name);
            row.Add(meta);
            VisualElement actions = UiKit.Row(Spacing.S);
            BenchPreset p = preset;
            UiKit.Action load = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, $"Load {preset.Name}", () => LoadPreset(p), "Load");
            actions.Add(load.Element);
            _presetStops.Add(load.Element);
            UiKit.Action delete = null;
            if (deletable)
            {
                delete = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, $"Delete {preset.Name}", () => DeletePreset(p.Name), "Delete");
                actions.Add(delete.Element);
                _presetStops.Add(delete.Element);
            }

            UiKit.Gaps(actions);
            row.Add(actions);
            _list.Add(row);

            // Where its actions are, for the smoke test, once they are laid out.
            // Placed when the line is written, so a scrolled list reports where they are now.
            _presetPlaces.Add((p.Name.Replace(' ', '_'), load.Element, delete?.Element));
            load.Element.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                _reportControls = true;
                Report();
            });
        }

        // Load: the preset's touch candidate and ball control model become the active ones at once,
        // as their live switches would, and its values the pending changes, whole. Nothing runs
        // under the values until APPLY / RETRY.
        private void LoadPreset(BenchPreset preset)
        {
            _switchModel(preset.Model);
            UiKit.Select(_theme, _models, preset.Model == ControlModel.Attached ? 1 : 0);
            BenchCandidate applied = _applied();
            _pending.Clear();
            foreach (BenchKnob k in BenchCandidate.Knobs)
            {
                double value = preset.Candidate.Get(k.Name);
                if (BitConverter.DoubleToInt64Bits(value) != BitConverter.DoubleToInt64Bits(applied.Get(k.Name))) _pending[k.Name] = value;
            }

            _switchControls((int)preset.Controls);
            UiKit.Select(_theme, _controls, _activeControls());
            _problem.style.display = DisplayStyle.None;
            _presetStatus.text = _pending.Count == 0
                ? $"\"{preset.Name}\" is what is running."
                : $"\"{preset.Name}\" loaded as {_pending.Count} pending change{(_pending.Count == 1 ? "" : "s")}. APPLY / RETRY starts a fresh run with {(_pending.Count == 1 ? "it" : "them")}.";
            Log(FormattableString.Invariant($"ELEVEN bench preset loaded name={preset.Name.Replace(' ', '_')} pending={_pending.Count} controls={TouchCandidates.NameOf(preset.Controls)} model={BenchPreset.ModelName(preset.Model)} config={preset.ConfigHash:x16}"));
            Describe();
        }

        // Save: the values shown, pending changes included, under the name typed. A name already
        // saved is replaced. Refused, with the reason, for a name or values that can't be used.
        private void SavePreset()
        {
            BenchCandidate applied = _applied();
            string problem;
            BenchPreset preset = null;
            bool made = applied.TryWith(_pending, out BenchCandidate candidate, out problem) && candidate != null
                && BenchPreset.TryMake(_presetName.Field.value, candidate, (ControlCandidate)_activeControls(), _build(), out preset, out problem);
            if (!made || preset == null)
            {
                _presetStatus.text = $"Not saved. {problem}";
                Log($"ELEVEN bench preset refused {problem}");
                // Back to the name, selected, so typing replaces it.
                _presetName.Field.Focus();
                _presetName.Field.SelectAll();
                return;
            }

            // Kept on the device, or not saved at all: the shelf is what storage holds (Codex on #90).
            string before = _shelf.Write();
            bool replaced = _shelf.Save(preset);
            if (!_storePresets(_shelf.Write()))
            {
                _shelf = PresetShelf.Read(before);
                _presetStatus.text = $"Not saved. This device wouldn't keep \"{preset.Name}\".";
                Log($"ELEVEN bench preset refused storage");
                return;
            }

            _presetName.Field.value = "";
            ShowGroup(_group);
            _presetStatus.text = replaced ? $"Replaced \"{preset.Name}\"." : $"Saved \"{preset.Name}\".";
            Log(FormattableString.Invariant($"ELEVEN bench preset saved name={preset.Name.Replace(' ', '_')} replaced={(replaced ? "yes" : "no")} count={_shelf.Presets.Count} config={preset.ConfigHash:x16}"));
            Describe();
        }

        private void DeletePreset(string name)
        {
            string before = _shelf.Write();
            if (!_shelf.Delete(name)) return;
            if (!_storePresets(_shelf.Write()))
            {
                _shelf = PresetShelf.Read(before);
                _presetStatus.text = $"Not deleted. This device wouldn't change its presets.";
                Log($"ELEVEN bench preset refused storage");
                return;
            }

            ShowGroup(_group);
            _presetStatus.text = $"Deleted \"{name}\".";
            Log(FormattableString.Invariant($"ELEVEN bench preset deleted name={name.Replace(' ', '_')} count={_shelf.Presets.Count}"));
            Describe();
        }

        /// <summary>The name of the saved preset these values are, the technical reference's, or "unsaved": what a capture records (#46).</summary>
        public static string PresetNameOf(BenchCandidate candidate, ControlCandidate controls, string stored)
        {
            // A saved preset first: one may hold the reference values under another ball control
            // model or touch candidate. The technical reference is its values, FREE and Candidate A.
            BenchPreset saved = PresetShelf.Read(stored).Matching(candidate, controls) ?? BenchPreset.SuggestedMatching(candidate, controls);
            if (saved != null) return saved.Name;
            return IsTechnicalReference(candidate, controls) ? BenchCandidate.ReferencePreset : BenchCandidate.UnsavedPreset;
        }

        private static bool IsTechnicalReference(BenchCandidate candidate, ControlCandidate controls) =>
            candidate.ChangesFrom(BenchCandidate.Reference) == 0 && candidate.Config.ControlModel == BenchPreset.TechnicalReference.Model && controls == BenchPreset.TechnicalReference.Controls;

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
            var controls = (ControlCandidate)_activeControls();
            BenchPreset running = _shelf.Matching(applied, controls) ?? BenchPreset.SuggestedMatching(applied, controls);
            _state.text = running != null ? $"Running preset \"{running.Name}\": {fromReference} changed from the technical reference."
                : IsTechnicalReference(applied, controls) ? "Running the technical reference values."
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
            string Box(VisualElement e) => FormattableString.Invariant($"{e.worldBound.x * s:F0},{e.worldBound.y * s:F0},{e.worldBound.width * s:F0},{e.worldBound.height * s:F0}");

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

                // Whether every switch, tab and action is in reach without scrolling (#31), and the
                // room the knob list has, in points.
                var clipped = new List<string>();
                void Reach(VisualElement e, VisualElement within)
                {
                    Rect r = e.worldBound, b = within.worldBound;
                    bool shown = r.width > 0 && r.xMin >= b.xMin - 0.5f && r.xMax <= b.xMax + 0.5f && r.yMin >= b.yMin - 0.5f && r.yMax <= b.yMax + 0.5f;
                    if (!shown) clipped.Add((e.name ?? "").Replace(' ', '_'));
                }

                bool wide = _arranged == "wide";
                VisualElement switchesIn = wide ? _switchScroll.contentViewport : _sheet.Body;
                VisualElement tabsIn = wide ? _tabScroll.contentViewport : _sheet.Body;
                if (!_switchesScroll)
                {
                    foreach (UiKit.Raised model in _models.Segments) Reach(model.Element, switchesIn);
                    foreach (UiKit.Raised view in _views.Segments) Reach(view.Element, switchesIn);
                }

                foreach (UiKit.Raised tab in _tabs.Segments) Reach(tab.Element, tabsIn);
                foreach (UiKit.Action action in new[] { _close, _revert, _applyAction }) Reach(action.Element, _sheet.Body);
                Rect list = _list.contentViewport.worldBound;
                Log(FormattableString.Invariant(
                    $"ELEVEN bench reach arranged={_arranged} clipped={clipped.Count} names={string.Join(",", clipped)} list={list.width:F0}x{list.height:F0}"));

                // Where Tab goes while the sheet is open, and how many of those stops can't be seen.
                List<VisualElement> order = FocusOrder();
                int hidden = order.Count(Hidden);
                Log($"ELEVEN bench focus order={string.Join(",", order.Select(v => (v.name ?? "").Replace(' ', '_')))} count={order.Count} hidden={hidden}");
            }

            if (_reportControls && _trace)
            {
                _reportControls = false;
                if (PresetsShown)
                {
                    // The Presets tab's own focus order (#144): the play switch, the presets, the switching mode.
                    List<VisualElement> presetsOrder = FocusOrder();
                    Log($"ELEVEN bench focus order={string.Join(",", presetsOrder.Select(v => (v.name ?? "").Replace(' ', '_')))} count={presetsOrder.Count} hidden={presetsOrder.Count(Hidden)}");
                    Log(FormattableString.Invariant(
                        $"ELEVEN bench presets count={_shelf.Presets.Count} unread={_shelf.Problems.Count()} name={P(_presetName.Element)} save={P(_presetSave.Element)} items={string.Join(";", _presetPlaces.Select(i => $"{i.Name}:{P(i.Load)}:{(i.Delete != null ? P(i.Delete) : "none")}"))} list={Box(_list.contentViewport)} switching=auto:{P(_switchings.Segments[0].Element)};ball:{P(_switchings.Segments[1].Element)};manual:{P(_switchings.Segments[2].Element)}"));
                }

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
