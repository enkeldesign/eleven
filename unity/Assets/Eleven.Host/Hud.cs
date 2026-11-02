using System;
using System.Collections.Generic;
using System.Globalization;
using Eleven.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>
    /// The UI around the match (#48, #53), in UI Toolkit from the visual system's recipes: the build
    /// line, Reset and Run scenario, the scenario result, THIS FELT WRONG with its note sheet, SAVE
    /// CAPTURE and the status line. The match controls (stick and kick) are #28's.
    ///
    /// Every placed element measures what it needs after layout (<see cref="HudNeeds"/>), and asks
    /// for the HUD to be laid out again when that changes. What an element needs doesn't depend on
    /// where it is placed, so this settles within two passes.
    /// </summary>
    internal sealed class Hud
    {
        private const float StatusSeconds = 6f;

        // The build line's values change every frame where frames drop; a few updates a second read
        // the same and spare the text from being laid out again each frame.
        private const float BuildEverySeconds = 0.5f;

        private readonly UiRoot _ui;
        private readonly bool _trace;
        private readonly System.Action _relayout;
        private readonly Action<string> _noteDone;
        private readonly List<UiKit.Action> _actions = new List<UiKit.Action>();

        private readonly UiKit.Surface _build;
        private readonly UiKit.Data _buildId;
        private readonly UiKit.Data _load;
        private readonly UiKit.Data _dropped;
        private readonly UiKit.Surface _result;
        private readonly UiKit.Data _scenario;
        private readonly UiKit.Data _digest;
        private readonly UiKit.Data _final;
        private readonly UiKit.Surface _status;
        private readonly VisualElement _scrim;
        private readonly UiKit.Surface _sheet;
        private readonly UiKit.TextInput _note;
        private readonly UiKit.Action _keep;
        private readonly UiKit.Action _skip;

        private HudLayout _layout;
        private float _statusUntil = -1;
        private bool _saveShown;
        private bool _sheetPlaced;
        private int _openedFrame = -1;
        private int _closedFrame = -10;
        private bool _reportBuild;
        private bool _reportResult;
        private float _buildShownAt = -1;

        public Hud(UiRoot ui, bool trace, System.Action relayout, System.Action reset, System.Action run,
            System.Action feltWrong, System.Action saveCapture, Action<string> noteDone)
        {
            _ui = ui;
            _trace = trace;
            _relayout = relayout;
            _noteDone = noteDone;
            Theme pitch = ui.Theme;

            // The build id, then the two numbers the device session reads off the screen (#5): how
            // long the page took to load, and dropped frames over the last minute of play. Tabular
            // slots sized for their widest values, so the panel never changes size as they change.
            _build = UiKit.MakeHudPanel(pitch);
            _build.Element.name = "Build";
            VisualElement buildRow = UiKit.Row(Spacing.M);
            buildRow.style.alignItems = Align.Center;
            _buildId = UiKit.MakeData(pitch, "ELEVEN", Application.version);
            _load = UiKit.MakeData(pitch, "LOAD", "99.9 s");
            _dropped = UiKit.MakeData(pitch, "DROPPED", "9999 in 60 s");
            buildRow.Add(_buildId.Element);
            buildRow.Add(_load.Element);
            buildRow.Add(_dropped.Element);
            UiKit.Gaps(buildRow);
            buildRow.style.flexWrap = Wrap.Wrap;
            _build.Content.Add(buildRow);
            ui.Root.Add(_build.Element);
            Measure(_build, n => n.Build, (n, v) => n.Build = v);

            Reset = Action(ActionRole.Utility, "Reset", reset, null, (n, v) => n.Reset = v, n => n.Reset);
            Run = Action(ActionRole.Utility, "Run scenario", run, null, (n, v) => n.Run = v, n => n.Run);

            // The scenario result: its time and hashes, names in one column and values in tabular slots.
            _result = UiKit.MakeHudPanel(pitch);
            _result.Element.name = "Result";
            // The hashes are always 16 figures, so they need no reserved slot to stay still.
            _scenario = ResultRow(pitch, "SCENARIO", "99999 ms");
            _digest = ResultRow(pitch, "DIGEST", "0");
            _final = ResultRow(pitch, "FINAL", "0");
            _result.Element.style.display = DisplayStyle.None;
            ui.Root.Add(_result.Element);
            Measure(_result, n => n.Result, (n, v) => n.Result = v);

            // THIS FELT WRONG is the one forward action over the match; a key that closed the note
            // sheet must not press it again behind the sheet. SAVE CAPTURE supports it, and shows once
            // there is a capture to save (its place is kept meanwhile).
            FeltWrong = Action(ActionRole.Primary, "Felt wrong", () =>
            {
                if (!SheetOpen && Time.frameCount - _closedFrame > 2) feltWrong();
            }, "Felt\nwrong", (n, v) => n.FeltWrong = v, n => n.FeltWrong);
            SaveCapture = Action(ActionRole.Supporting, "Save capture", saveCapture, "Save\ncapture", (n, v) => n.SaveCapture = v, n => n.SaveCapture);
            SaveCapture.Element.style.visibility = Visibility.Hidden;
            SaveCapture.Element.focusable = false;

            _status = UiKit.MakeStatus(pitch);
            _status.Element.name = "Status";
            ui.Root.Add(_status.Element);
            Measure(_status, n => n.Status, (n, v) => n.Status = v);

            // The note sheet (#53): over a scrim that takes every press, so nothing reaches play or
            // the HUD while it is open. The capture is already taken; the note is optional, and either
            // action keeps the capture. Its page-like content takes the ordinary mapping.
            Theme sheet = Theme.Ordinary;
            _scrim = new VisualElement { name = "Scrim", pickingMode = PickingMode.Position };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = _scrim.style.top = _scrim.style.right = _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = sheet[Role.Scrim].ToColor();
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.alignItems = Align.Center;
            _scrim.style.paddingLeft = _scrim.style.paddingRight = Spacing.L;
            _scrim.style.display = DisplayStyle.None;
            _sheet = UiKit.MakeSheet(sheet, "What felt wrong?");
            _sheet.Element.name = "Note sheet";
            _sheet.Element.style.width = Length.Percent(100);
            _sheet.Element.style.maxWidth = 480;
            Label caption = UiKit.Text(sheet, TypeScale.Body, "Optional. The last 10 seconds are already captured.", Role.TextMuted);
            caption.style.marginBottom = Spacing.M;
            _sheet.Content.Add(caption);
            _note = UiKit.MakeTextInput(sheet, "Note", 280);
            _sheet.Content.Add(_note.Element);
            _skip = UiKit.MakeAction(ui, sheet, ActionRole.Navigation, "No note", () => CloseNote(""));
            _keep = UiKit.MakeAction(ui, sheet, ActionRole.Primary, "Keep note", () => CloseNote(_note.Field.value));
            _sheet.Actions.style.flexWrap = Wrap.Wrap;
            _sheet.Actions.style.justifyContent = Justify.FlexEnd;
            _sheet.Actions.Add(_skip.Element);
            _sheet.Actions.Add(_keep.Element);
            UiKit.Gaps(_sheet.Actions);
            _scrim.Add(_sheet.Element);
            ui.Root.Add(_scrim);

            // Tab and Shift+Tab move focus through the HUD's controls in their order, and through the
            // sheet's alone while it is open. The HUD does this itself: it doesn't rely on the panel
            // turning Tab into navigation, which the smoke test found it doesn't here (#53).
            VisualElement tree = ui.Root.panel?.visualTree ?? ui.Root;
            tree.RegisterCallback<KeyDownEvent>(e =>
            {
                if (_trace) Log($"ELEVEN ui key code={e.keyCode} char={(int)e.character}");
                if (e.keyCode != KeyCode.Tab && e.character != '\t') return;
                // The key moves focus; its character, which may come as a separate event, goes nowhere.
                if (e.keyCode == KeyCode.Tab) MoveFocus(e.shiftKey ? -1 : 1);
                e.StopPropagation();
                ui.Root.focusController?.IgnoreEvent(e);
            }, TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (_trace) Log($"ELEVEN ui navigate direction={e.direction}");
                if (e.direction != NavigationMoveEvent.Direction.Next && e.direction != NavigationMoveEvent.Direction.Previous) return;
                e.StopPropagation();
                ui.Root.focusController?.IgnoreEvent(e);
            }, TrickleDown.TrickleDown);

            // Enter keeps the note and Escape keeps the capture without one, wherever focus is while
            // the sheet is open; not the key that opened it.
            tree.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!SheetOpen || Time.frameCount == _openedFrame) return;
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    CloseNote(_note.Field.value);
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    CloseNote("");
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationCancelEvent>(e =>
            {
                if (!SheetOpen || Time.frameCount == _openedFrame) return;
                CloseNote("");
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            _sheet.Body.RegisterCallback<GeometryChangedEvent>(_ => SheetPlaced());

            if (_trace)
            {
                // Where keyboard focus goes, and whether its ring shows (#53).
                ui.Root.RegisterCallback<FocusInEvent>(e =>
                {
                    var target = e.target as VisualElement;
                    if (target == _note.Field)
                    {
                        Log($"ELEVEN ui focus name=Note ring={(_note.Focus.style.display == DisplayStyle.Flex ? "shown" : "hidden")}");
                        return;
                    }

                    foreach (UiKit.Action a in _actions)
                    {
                        if (a.Element != target) continue;
                        Log($"ELEVEN ui focus name={a.Name.Replace(' ', '_')} ring={(a.Focus.style.display == DisplayStyle.Flex ? "shown" : "hidden")}");
                    }
                });
            }
        }

        public HudNeeds Needs { get; } = new HudNeeds();

        public UiKit.Action Reset { get; }

        public UiKit.Action Run { get; }

        public UiKit.Action FeltWrong { get; }

        public UiKit.Action SaveCapture { get; }

        /// <summary>Whether the note sheet is open: play waits, as it did under the browser's prompt (#44).</summary>
        public bool SheetOpen { get; private set; }

        /// <summary>Whether a press on SAVE CAPTURE would save now: shown, and not behind the sheet.</summary>
        public bool SaveActive => _saveShown && !SheetOpen;

        /// <summary>The build panel's laid-out size in points, for the trace line (it must not jitter).</summary>
        public string BuildSize => FormattableString.Invariant($"{_build.Element.layout.width:F0}x{_build.Element.layout.height:F0}");

        /// <summary>Puts every element where the layout says.</summary>
        public void Place(in HudLayout layout)
        {
            _layout = layout;
            float row = layout.BuildLabel.width;
            _ui.PlaceAuto(_build.Element, layout.BuildLabel.position, row);
            _ui.Place(Reset.Element, layout.ResetButton);
            _ui.Place(Run.Element, layout.RunButton);
            _ui.PlaceAuto(_result.Element, layout.Result.position, row);
            _ui.Place(FeltWrong.Element, layout.FeltWrongButton);
            _ui.Place(SaveCapture.Element, layout.SaveCaptureButton);
            _ui.PlaceAuto(_status.Element, layout.StatusArea.position, layout.PlayArea.width - 2 * layout.Padding);
            _reportBuild = _reportResult = true;
        }

        /// <summary>Called once a frame: the build line's values, and the status line's time.</summary>
        public void Tick(string build, double loadSeconds, int dropped, double coveredSeconds)
        {
            float now = Time.realtimeSinceStartup;
            if (_buildShownAt < 0 || now - _buildShownAt >= BuildEverySeconds)
            {
                _buildShownAt = now;
                Set(_buildId.Value, build);
                Set(_load.Value, FormattableString.Invariant($"{loadSeconds:F1} s"));
                Set(_dropped.Value, FormattableString.Invariant($"{dropped} in {coveredSeconds:F0} s"));
            }

            if (_statusUntil >= 0 && Time.realtimeSinceStartup >= _statusUntil)
            {
                _statusUntil = -1;
                _status.Element.style.display = DisplayStyle.None;
            }

            // Once a panel has settled where the layout put it, at the size it measured, it reports
            // whether its values fit their slots (for the CI smoke test).
            if (_reportBuild && Settled(_build, Needs.Build, _layout.BuildLabel.position))
            {
                _reportBuild = false;
                Report(_build, "build");
            }

            if (_reportResult && Settled(_result, Needs.Result, _layout.Result.position))
            {
                _reportResult = false;
                Report(_result, "result");
            }
        }

        public void ShowResult(double milliseconds, ulong digest, ulong final)
        {
            Set(_scenario.Value, FormattableString.Invariant($"{milliseconds:F0} ms"));
            Set(_digest.Value, digest.ToString("x16", CultureInfo.InvariantCulture));
            Set(_final.Value, final.ToString("x16", CultureInfo.InvariantCulture));
            _result.Element.style.display = DisplayStyle.Flex;
            _reportResult = true;
        }

        /// <summary>The status line: states what just happened for a few seconds, without taking input.</summary>
        public void ShowStatus(string text)
        {
            _status.Title.text = text;
            _status.Element.style.display = DisplayStyle.Flex;
            _statusUntil = Time.realtimeSinceStartup + StatusSeconds;
            Log($"ELEVEN ui status {text}");
        }

        /// <summary>SAVE CAPTURE shows once there is a capture to save.</summary>
        public void ShowSave()
        {
            if (_saveShown) return;
            _saveShown = true;
            SaveCapture.Element.style.visibility = Visibility.Visible;
            SaveCapture.Element.focusable = !SheetOpen;
            Log("ELEVEN ui save visible=yes");
        }

        /// <summary>Opens the note sheet; the HUD behind it takes neither presses nor focus.</summary>
        public void OpenNote()
        {
            if (SheetOpen) return;
            SheetOpen = true;
            _openedFrame = Time.frameCount;
            _sheetPlaced = false;
            _note.Field.value = "";
            foreach (UiKit.Action a in _actions) a.Element.focusable = false;
            _scrim.style.display = DisplayStyle.Flex;
        }

        private void CloseNote(string note)
        {
            if (!SheetOpen) return;
            SheetOpen = false;
            _closedFrame = Time.frameCount;
            _note.Field.Blur();
            _scrim.style.display = DisplayStyle.None;
            foreach (UiKit.Action a in _actions) a.Element.focusable = a != SaveCapture || _saveShown;
            Log("ELEVEN ui sheet closed");
            _noteDone(note);
        }

        // Once the sheet has its layout: focus the note, so typing goes straight in, and say where
        // its actions are for the smoke test.
        private void SheetPlaced()
        {
            if (!SheetOpen || _sheetPlaced) return;
            _sheetPlaced = true;
            _note.Field.Focus();
            float s = _ui.PixelsPerPoint;
            Vector2 keep = _keep.Element.worldBound.center * s;
            Vector2 skip = _skip.Element.worldBound.center * s;
            Log(FormattableString.Invariant($"ELEVEN ui sheet open keep={keep.x:F0},{keep.y:F0} skip={skip.x:F0},{skip.y:F0}"));
        }

        // The next control in order that can take focus, from the focused one.
        private void MoveFocus(int step)
        {
            var order = new List<VisualElement>();
            if (SheetOpen)
            {
                order.Add(_note.Field);
                order.Add(_skip.Element);
                order.Add(_keep.Element);
            }
            else
            {
                foreach (UiKit.Action a in _actions)
                {
                    if (a.Element.focusable && a.Element.resolvedStyle.visibility == Visibility.Visible) order.Add(a.Element);
                }
            }

            if (order.Count == 0) return;
            Focusable focused = _ui.Root.focusController?.focusedElement;
            int at = -1;
            for (int i = 0; i < order.Count; i++)
            {
                if (focused is VisualElement v && (v == order[i] || order[i].Contains(v))) at = i;
            }

            int next = at < 0 ? (step > 0 ? 0 : order.Count - 1) : (at + step + order.Count) % order.Count;
            order[next].Focus();
        }

        private UiKit.Action Action(ActionRole role, string name, System.Action onClick, string label,
            Action<HudNeeds, Vector2> setNeed, Func<HudNeeds, Vector2> getNeed)
        {
            UiKit.Action a = UiKit.MakeAction(_ui, _ui.Theme, role, name, onClick, label);
            _ui.Root.Add(a.Element);
            _actions.Add(a);
            Fit(a, setNeed, getNeed);
            return a;
        }

        private UiKit.Data ResultRow(Theme theme, string name, string widest)
        {
            UiKit.Data d = UiKit.MakeData(theme, name, widest);
            d.Name.style.minWidth = 9 * TypeScale.Micro.Size * UiRoot.TextScale * 0.6f;
            _result.Content.Add(d.Element);
            return d;
        }

        // A placed action takes the size its label needs (#48, Codex's accessibility finding): after
        // each layout it measures its label, and if it needs another size the HUD lays itself out
        // again. It also reports whether the label fits, for the CI smoke test.
        private void Fit(UiKit.Action action, Action<HudNeeds, Vector2> setNeed, Func<HudNeeds, Vector2> getNeed)
        {
            action.Body.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                // What the text needs, not its laid-out size: layout squeezes a label to its parent,
                // so an overflowing label never looks taller. Its width on one line, and its height
                // at the width the body gives it.
                Rect body = action.Body.contentRect;
                Rect outer = action.Element.layout;
                Label label = action.Label;
                Vector2 line = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                Vector2 needed = label.MeasureTextSize(label.text, body.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                string verdict = needed.y > body.height + 0.5f ? "overflow" : line.x > body.width + 0.5f ? "wraps" : "ok";
                Log(FormattableString.Invariant(
                    $"ELEVEN ui fit name={action.Name.Replace(' ', '_')} line={line.x:F0}x{line.y:F0} needs={needed.y:F0} body={body.width:F0}x{body.height:F0} size={outer.width:F0}x{outer.height:F0} {verdict}"));
                if (body.width <= 0 || float.IsNaN(line.x) || float.IsNaN(needed.y)) return;

                // In screen pixels: the action's width with its label on one line, and its height
                // at the width it has now. The insets are its border and padding, whatever its size.
                float s = _ui.PixelsPerPoint;
                var needs = new Vector2(
                    Mathf.Ceil((line.x + outer.width - body.width + 1) * s),
                    Mathf.Ceil((needed.y + outer.height - body.height) * s));
                Vector2 was = getNeed(Needs);
                if (Mathf.Abs(needs.x - was.x) <= 1 && Mathf.Abs(needs.y - was.y) <= 1) return;
                setNeed(Needs, needs);
                _relayout();
            });
        }

        // A panel is as big as its content; the HUD lays out around the size it measures.
        private void Measure(UiKit.Surface panel, Func<HudNeeds, Vector2> getNeed, Action<HudNeeds, Vector2> setNeed)
        {
            panel.Element.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (panel.Element.resolvedStyle.display == DisplayStyle.None) return;
                Rect r = panel.Element.layout;
                if (float.IsNaN(r.width) || r.width <= 0) return;
                float s = _ui.PixelsPerPoint;
                var needs = new Vector2(Mathf.Ceil(r.width * s), Mathf.Ceil(r.height * s));
                Vector2 was = getNeed(Needs);
                if (Mathf.Abs(needs.x - was.x) <= 1 && Mathf.Abs(needs.y - was.y) <= 1) return;
                setNeed(Needs, needs);
                _relayout();
            });
        }

        private bool Settled(UiKit.Surface panel, Vector2 need, Vector2 placed)
        {
            if (panel.Element.resolvedStyle.display == DisplayStyle.None) return false;
            Rect r = panel.Element.layout;
            float s = _ui.PixelsPerPoint;
            return !float.IsNaN(r.width) && r.width > 0
                && Mathf.Abs(r.width * s - need.x) <= 1.5f && Mathf.Abs(r.height * s - need.y) <= 1.5f
                && Mathf.Abs(r.x * s - placed.x) <= 1.5f && Mathf.Abs(r.y * s - placed.y) <= 1.5f;
        }

        private void Report(UiKit.Surface panel, string name)
        {
            float s = _ui.PixelsPerPoint;
            Rect bound = panel.Element.worldBound;
            var pixels = new Rect(bound.x * s, bound.y * s, bound.width * s, bound.height * s);
            float widest = 0;
            bool fits = pixels.xMax <= _layout.ScreenWidth + 1 && pixels.yMax <= _layout.ScreenHeight + 1 && !pixels.Overlaps(_layout.PlayArea);
            panel.Element.Query<Label>().ForEach(l =>
            {
                if (l.style.minWidth.keyword != StyleKeyword.Undefined || l.style.minWidth.value.value <= 0) return;
                float needed = l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                widest = Mathf.Max(widest, needed);
                if (needed > l.layout.width + 0.5f) fits = false;
            });
            Log(FormattableString.Invariant(
                $"ELEVEN hud {name} fits={(fits ? "yes" : "NO")} widest={widest:F0} panel={bound.width:F0}x{bound.height:F0} screen={_layout.ScreenWidth}x{_layout.ScreenHeight}"));
        }

        private static void Set(Label label, string text)
        {
            if (label.text != text) label.text = text;
        }

        private static void Log(string message) => Debug.Log(message);
    }
}
