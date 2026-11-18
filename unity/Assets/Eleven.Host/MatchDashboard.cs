using System;
using System.Collections.Generic;
using Eleven.Presentation;
using Eleven.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>
    /// The match dashboard (#128): the human manager's view of the match, shown while it is paused.
    /// The score and the clock, each side's metrics side by side (the recorder's, #128 step 1), each
    /// side's plan and, for the human's side, SWITCH PLAN between its three stored plans (#126).
    /// It reads the match and changes nothing: a plan chosen here goes on the human's next command,
    /// recorded with it (INV-10). Glanceable on a phone: one panel over the stopped play, the two
    /// sides in two columns, scrolled where it doesn't fit.
    /// </summary>
    internal sealed class MatchDashboard
    {
        /// <summary>What the dashboard shows, read from the match by the host.</summary>
        public struct View
        {
            public string Score, Clock, Half;
            public SideMetrics You, Cpu;

            /// <summary>The plans' names, or none at a size without plans.</summary>
            public string[] Plans;

            /// <summary>Each side's plan, 0 to 2, and the one it waits to take, 1 to 3 (0 none).</summary>
            public int YourPlan, YourPending, CpuPlan;

            /// <summary>The plan the human asked for, not yet sent: 1 to 3, 0 none.</summary>
            public int Asked;

            /// <summary>s of the match clock before the human's side may switch again; 0 now.</summary>
            public int CooldownSeconds;

            /// <summary>Whether the side is still settling into its plan.</summary>
            public bool Settling;

            /// <summary>Whether the match is over: nothing to switch.</summary>
            public bool Over;

            /// <summary>When a switch asked for now is taken: "at once" or "at the next stoppage".</summary>
            public string TakenWhen;
        }

        private static readonly string[] Rows = { "Possession", "Shots", "On target", "xG", "Passes", "Corners", "PPDA" };

        private readonly UiRoot _ui;
        private readonly Theme _theme;
        private readonly Action<int> _switchPlan;
        private readonly UiKit.Surface _sheet;
        private readonly ScrollView _scroll;
        private readonly Label _line;
        private readonly Label[] _you = new Label[Rows.Length], _cpu = new Label[Rows.Length];
        private readonly Label _yourPlan, _cpuPlan, _switchCaption, _planNote;
        private readonly VisualElement _switchRow;
        private UiKit.Segmented _plans;
        private string[] _planNames = new string[0];
        private readonly Action<string> _placed;
        private bool _reportPlaces;

        public MatchDashboard(UiRoot ui, Action<int> switchPlan, Action<string> placed = null)
        {
            _ui = ui;
            _placed = placed;
            _theme = Theme.Ordinary;
            _switchPlan = switchPlan;
            _sheet = UiKit.MakeSheet(_theme, "Match");
            _sheet.Element.name = "Match dashboard";
            _sheet.Element.style.position = Position.Absolute;
            _sheet.Element.style.display = DisplayStyle.None;
            _sheet.Body.style.flexShrink = 1;
            _sheet.Body.style.minHeight = 0;
            _sheet.Actions.style.display = DisplayStyle.None;
            _sheet.Content.style.flexShrink = 1;
            _sheet.Content.style.minHeight = 0;

            _scroll = UiKit.MakeScroll();
            _scroll.name = "Match dashboard list";
            _scroll.style.flexShrink = 1;
            _scroll.style.minHeight = 0;
            _sheet.Content.Add(_scroll);

            // The score, the clock and the half, as the match panel has them.
            _line = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _line.style.marginBottom = Spacing.M;
            _scroll.Add(_line);

            // The plans first, the manager's decision: each side's, then SWITCH PLAN for the human's.
            _yourPlan = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _cpuPlan = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _cpuPlan.style.marginTop = Spacing.Xs;
            _switchCaption = UiKit.Text(_theme, TypeScale.Micro, "Switch plan", Role.TextMuted);
            _switchCaption.style.marginTop = Spacing.M;
            _switchCaption.style.marginBottom = Spacing.Xs;
            _switchRow = new VisualElement { name = "Switch plan", pickingMode = PickingMode.Ignore };
            _planNote = UiKit.Text(_theme, TypeScale.Body, "", Role.TextMuted);
            _planNote.style.marginTop = Spacing.Xs;
            _scroll.Add(_yourPlan);
            _scroll.Add(_cpuPlan);
            _scroll.Add(_switchCaption);
            _scroll.Add(_switchRow);
            _scroll.Add(_planNote);

            // Then the metrics: a name, then the two sides in tabular columns, the human's first.
            VisualElement table = new VisualElement { name = "Match metrics", pickingMode = PickingMode.Ignore };
            table.style.marginTop = Spacing.L;
            table.Add(TableRow(UiKit.Text(_theme, TypeScale.Micro, "", Role.TextMuted), Heading("You"), Heading("CPU")));
            for (int i = 0; i < Rows.Length; i++)
            {
                _you[i] = Figure();
                _cpu[i] = Figure();
                table.Add(TableRow(UiKit.Text(_theme, TypeScale.Body, Rows[i], Role.TextMuted), _you[i], _cpu[i]));
            }

            _scroll.Add(table);

            // A plan's segment reached with Tab scrolls into view.
            _scroll.RegisterCallback<FocusInEvent>(e =>
            {
                if (e.target is VisualElement focused && _scroll.contentContainer.Contains(focused)) _scroll.ScrollTo(focused);
            });

            // Once shown and laid out, where it and its plans' segments are, in HUD pixels (the smoke's).
            _sheet.Element.RegisterCallback<GeometryChangedEvent>(_ => ReportPlaces());
            ui.Root.Add(_sheet.Element);
        }

        private void ReportPlaces()
        {
            if (!_reportPlaces || !Shown || _placed == null) return;
            Rect panel = _sheet.Element.worldBound;
            if (panel.width <= 0 || panel.height <= 0) return;
            _reportPlaces = false;
            float s = _ui.PixelsPerPoint;
            string Box(Rect r) => FormattableString.Invariant($"{r.x * s:F0},{r.y * s:F0},{r.width * s:F0},{r.height * s:F0}");
            var plans = new List<string>();
            if (_plans != null && _switchRow.resolvedStyle.display == DisplayStyle.Flex)
            {
                for (int i = 0; i < _plans.Segments.Count; i++) plans.Add($"{_planNames[i].Replace(' ', '_')}:{Box(_plans.Segments[i].Element.worldBound)}");
            }

            _placed($"panel={Box(panel)} viewport={Box(_scroll.contentViewport.worldBound)} plans={(plans.Count > 0 ? string.Join("|", plans) : "none")}");
        }

        /// <summary>The panel, for its place in the HUD and the smoke's checks.</summary>
        public VisualElement Element => _sheet.Element;

        public bool Shown { get; private set; }

        /// <summary>The plans' segments, in order: focusable while shown and switchable.</summary>
        public IEnumerable<VisualElement> FocusOrder()
        {
            if (!Shown || _plans == null) yield break;
            foreach (UiKit.Raised r in _plans.Segments)
            {
                if (r.Element.enabledSelf) yield return r.Element;
            }
        }

        public void Show(bool shown)
        {
            if (Shown == shown) return;
            Shown = shown;
            _sheet.Element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown) return;
            _scroll.scrollOffset = Vector2.zero;
            _reportPlaces = true;
            ReportPlaces();
        }

        /// <summary>Over the play area, inset by the HUD's padding: as wide as it reads well, as tall as the area.</summary>
        public void Place(Rect playArea, float padding)
        {
            float s = Mathf.Max(0.01f, _ui.PixelsPerPoint);
            float width = Mathf.Min(playArea.width - 2 * padding, 480 * s);
            _sheet.Element.style.left = (playArea.x + (playArea.width - width) / 2) / s;
            _sheet.Element.style.top = (playArea.y + padding) / s;
            _sheet.Element.style.width = width / s;
            _sheet.Element.style.maxHeight = Mathf.Max(Interaction.MinTarget, playArea.height - 2 * padding) / s;
        }

        public void Update(View v)
        {
            Set(_line, $"{v.Score} · {v.Clock} · {v.Half}");
            string[] you = Figures(v.You), cpu = Figures(v.Cpu);
            for (int i = 0; i < Rows.Length; i++)
            {
                Set(_you[i], you[i]);
                Set(_cpu[i], cpu[i]);
            }

            bool plans = v.Plans != null && v.Plans.Length > 0;
            if (plans && !SameNames(v.Plans)) BuildPlans(v.Plans);
            _switchCaption.style.display = _switchRow.style.display = plans ? DisplayStyle.Flex : DisplayStyle.None;
            _cpuPlan.style.display = plans ? DisplayStyle.Flex : DisplayStyle.None;
            if (!plans)
            {
                Set(_yourPlan, "No stored plans in a 1v1 or 2v2 match.");
                Set(_planNote, "");
                return;
            }

            string Name(int i) => i >= 0 && i < v.Plans.Length ? v.Plans[i] : "";
            Set(_yourPlan, $"Your plan: {Name(v.YourPlan)}{(v.Settling ? " (settling)" : "")}");
            Set(_cpuPlan, $"CPU plan: {Name(v.CpuPlan)}");

            // The segment marked is the plan the side plays, or the one it will take.
            int marked = v.Asked > 0 ? v.Asked - 1 : v.YourPending > 0 ? v.YourPending - 1 : v.YourPlan;
            if (_plans.Selected != marked) UiKit.Select(_theme, _plans, marked);
            bool open = !v.Over && v.CooldownSeconds == 0 && v.YourPending == 0 && v.Asked == 0;
            for (int i = 0; i < _plans.Segments.Count; i++) _plans.Segments[i].Element.SetEnabled(open && i != v.YourPlan);

            Set(_planNote,
                v.Over ? "The match is over."
                : v.Asked > 0 ? $"{Name(v.Asked - 1)}: asked. Taken {v.TakenWhen} once play resumes."
                : v.YourPending > 0 ? $"{Name(v.YourPending - 1)}: taken at the next stoppage."
                : v.CooldownSeconds > 0 ? FormattableString.Invariant($"Next change in {v.CooldownSeconds} s of play.")
                : $"A change is taken {v.TakenWhen}.");
        }

        /// <summary>The dashboard's figures, in the rows' order.</summary>
        public static string[] Figures(in SideMetrics m) => new[]
        {
            FormattableString.Invariant($"{Math.Round(100 * m.Possession):0} %"),
            FormattableString.Invariant($"{m.Shots}"),
            FormattableString.Invariant($"{m.OnTarget}"),
            FormattableString.Invariant($"{m.Xg:0.00}"),
            FormattableString.Invariant($"{m.PassesCompleted}/{m.Passes}"),
            FormattableString.Invariant($"{m.Corners}"),
            FormattableString.Invariant($"{m.Ppda:0.0}"),
        };

        /// <summary>A line for the trace: what is shown, row by row.</summary>
        public string Report(in View v)
        {
            string[] you = Figures(v.You), cpu = Figures(v.Cpu);
            var parts = new List<string>();
            for (int i = 0; i < Rows.Length; i++) parts.Add($"{Rows[i].Replace(' ', '_').ToLowerInvariant()}={you[i].Replace(" ", "")}-{cpu[i].Replace(" ", "")}");
            return string.Join(" ", parts);
        }

        private bool SameNames(string[] names)
        {
            if (names.Length != _planNames.Length) return false;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != _planNames[i]) return false;
            }

            return true;
        }

        private void BuildPlans(string[] names)
        {
            _planNames = (string[])names.Clone();
            _switchRow.Clear();
            _plans = UiKit.MakeSegmented(_ui, _theme, names, 0, i => _switchPlan(i + 1));
            _switchRow.Add(_plans.Element);
        }

        private Label Heading(string text)
        {
            Label l = UiKit.Text(_theme, TypeScale.Micro, text, Role.TextMuted);
            l.style.unityTextAlign = TextAnchor.MiddleRight;
            return l;
        }

        private Label Figure()
        {
            Label l = UiKit.Text(_theme, TypeScale.Data, "", Role.Text);
            l.style.unityTextAlign = TextAnchor.MiddleRight;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            return l;
        }

        // A row of the table: the name takes what is left, each side's figure a fixed column.
        private static VisualElement TableRow(Label name, Label you, Label cpu)
        {
            VisualElement row = UiKit.Row(Spacing.S);
            row.style.alignItems = Align.Center;
            row.style.minHeight = 24;
            name.style.flexGrow = 1;
            name.style.flexShrink = 1;
            float column = 7 * TypeScale.Data.Size * UiRoot.TextScale * 0.6f;
            you.style.width = cpu.style.width = column;
            you.style.flexShrink = cpu.style.flexShrink = 0;
            row.Add(name);
            row.Add(you);
            row.Add(cpu);
            UiKit.Gaps(row);
            return row;
        }

        private static void Set(Label label, string text)
        {
            if (label.text != text) label.text = text;
        }
    }
}
