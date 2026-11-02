using System;
using System.Globalization;
using Eleven.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>
    /// The visual system's specimen (#48), at <c>?specimen</c> (or <c>?specimen=dark</c>): a page
    /// built only from the production recipes in <see cref="UiKit"/>, in the ordinary or the dark
    /// mapping. It has no styles of its own beyond page layout, so it cannot drift from the
    /// components it shows. Erik reviewed it on device (2026-11-02): dark buttons with white text in every context.
    /// </summary>
    internal sealed class Specimen
    {
        private readonly UiRoot _ui;
        private bool _dark;
        private VisualElement _page;
        private UiKit.Data _tick;
        private UiKit.Data _frame;
        private int _frames;

        public Specimen(UiRoot ui, bool dark)
        {
            _ui = ui;
            _dark = dark;
            Build(Current());
        }

        private Theme Current() => _dark ? Theme.Dark : Theme.Ordinary;

        /// <summary>Called once a frame: the HUD data changes, the layout must not move.</summary>
        public void Tick(long tick)
        {
            _frames++;
            if (_tick == null) return;
            _tick.Value.text = tick.ToString("000000", CultureInfo.InvariantCulture);
            _frame.Value.text = (_frames % 1000).ToString("000", CultureInfo.InvariantCulture);
        }

        private void Build(Theme theme)
        {
            _page?.RemoveFromHierarchy();
            _page = new VisualElement { name = "specimen" };
            _page.style.position = Position.Absolute;
            _page.style.left = _page.style.top = _page.style.right = _page.style.bottom = 0;
            _page.style.backgroundColor = theme[Role.SurfacePage].ToColor();

            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            scroll.style.flexGrow = 1;
            VisualElement column = scroll.contentContainer;
            column.style.paddingLeft = column.style.paddingRight = Spacing.L;
            column.style.paddingTop = column.style.paddingBottom = Spacing.Xl;
            _page.Add(scroll);

            column.Add(UiKit.Text(theme, TypeScale.Display, "ELEVEN", Role.TextHeading));
            Label caption = UiKit.Text(theme, TypeScale.Small,
                $"Visual system specimen, {theme.Name} mapping. Production components only.", Role.TextMuted);
            caption.style.marginBottom = Spacing.L;
            column.Add(caption);

            UiKit.Segmented context = UiKit.MakeSegmented(_ui, theme, new[] { "Ordinary", "Dark" }, _dark ? 1 : 0, i =>
            {
                _dark = i == 1;
                Build(Current());
            });
            context.Element.style.marginBottom = Spacing.L;
            column.Add(context.Element);

            // Cards wrap into columns where the screen is wide enough (tablets), one column on phones.
            VisualElement grid = UiKit.Row(Spacing.L);
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            column.Add(grid);

            grid.Add(Card(theme, "Type", s =>
            {
                s.Add(UiKit.Text(theme, TypeScale.Display, "Display 36", Role.TextHeading));
                s.Add(UiKit.Text(theme, TypeScale.Heading, "Heading 26", Role.TextHeading));
                s.Add(UiKit.Text(theme, TypeScale.Title, "Title 19", Role.TextHeading));
                s.Add(UiKit.Text(theme, TypeScale.Body, "Body 15: calmer prose for explanations and settings.", Role.Text));
                s.Add(UiKit.Text(theme, TypeScale.Small, "Small 13: metadata and hints, muted.", Role.TextMuted));
                s.Add(UiKit.Text(theme, TypeScale.Label, "Label 13 bold uppercase", Role.Text));
                s.Add(UiKit.Text(theme, TypeScale.Micro, "Micro 11", Role.TextMuted));
            }));

            grid.Add(Card(theme, "Actions", s =>
            {
                VisualElement roles = Wrapping();
                roles.Add(UiKit.MakeAction(_ui, theme, ActionRole.Primary, "Kick off", () => { }).Element);
                roles.Add(UiKit.MakeAction(_ui, theme, ActionRole.Supporting, "Tactics", () => { }).Element);
                roles.Add(UiKit.MakeAction(_ui, theme, ActionRole.Utility, "Reset", () => { }).Element);
                roles.Add(UiKit.MakeAction(_ui, theme, ActionRole.Navigation, "Back", () => { }).Element);
                roles.Add(UiKit.MakeAction(_ui, theme, ActionRole.Destructive, "Delete", () => { }).Element);
                UiKit.Gaps(roles);
                s.Add(roles);
                s.Add(Caption(theme, "Primary, supporting, utility, navigation, destructive. One primary per view."));

                VisualElement states = Wrapping();
                UiKit.Action pressed = UiKit.MakeAction(_ui, theme, ActionRole.Utility, "Pressed", () => { });
                UiKit.SetPressed(pressed, true);
                UiKit.Action focused = UiKit.MakeAction(_ui, theme, ActionRole.Utility, "Focused", () => { });
                focused.Focus.style.display = DisplayStyle.Flex;
                UiKit.Action disabled = UiKit.MakeAction(_ui, theme, ActionRole.Primary, "Disabled", () => { });
                UiKit.SetEnabled(disabled, false);
                UiKit.Action busy = UiKit.MakeAction(_ui, theme, ActionRole.Primary, "Apply", () => { });
                UiKit.SetBusy(busy, true, "Applying");
                states.Add(pressed.Element);
                states.Add(focused.Element);
                states.Add(disabled.Element);
                states.Add(busy.Element);
                UiKit.Gaps(states);
                s.Add(states);
                s.Add(Caption(theme, "Pressed sits in its shadow; focus is a two-colour ring; disabled loses its elevation; busy says so."));
            }));

            grid.Add(Card(theme, "Selection and values", s =>
            {
                s.Add(UiKit.MakeSegmented(_ui, theme, new[] { "Low", "Medium", "High" }, 1, _ => { }).Element);
                s.Add(Caption(theme, "Selected: fill, a mark and bold text."));
                s.Add(UiKit.MakeValue(_ui, theme, "Kick speed", "m/s", "Current 18 · research start 15–32 · valid 0–60", 0, 60, 0.5, 1, 18, _ => { }).Element);
            }));

            grid.Add(Card(theme, "Over the pitch", s =>
            {
                // The pitch context: the same buttons with chalk borders on the pitch's own colour.
                Theme pitch = Theme.Pitch;
                var field = new VisualElement { pickingMode = PickingMode.Ignore };
                field.style.backgroundColor = Domain.Pitch.ToColor();
                UiKit.Radius(field, Shapes.Control.Radius);
                field.style.paddingLeft = field.style.paddingRight = field.style.paddingTop = field.style.paddingBottom = Spacing.M;
                VisualElement row = Wrapping();
                row.Add(UiKit.MakeAction(_ui, pitch, ActionRole.Utility, "Reset", () => { }).Element);
                row.Add(UiKit.MakeAction(_ui, pitch, ActionRole.Primary, "Felt wrong", () => { }).Element);
                UiKit.Gaps(row);
                field.Add(row);
                s.Add(field);
                s.Add(Caption(theme, "Dark fills are about 1:1 against the pitch; chalk borders carry the boundary."));
            }));

            grid.Add(Card(theme, "HUD data", s =>
            {
                _tick = UiKit.MakeData(theme, "TICK", "000000");
                _frame = UiKit.MakeData(theme, "FRAME", "000");
                s.Add(_tick.Element);
                s.Add(_frame.Element);
                s.Add(Caption(theme, "Tabular figures in fixed slots: changing values never move the layout."));
            }));

            grid.Add(Card(theme, "Palette", s =>
            {
                VisualElement swatches = Wrapping();
                foreach ((string name, Rgba colour) in Primitives.All)
                {
                    var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                    swatch.style.width = 96;
                    swatch.style.marginBottom = Spacing.S;
                    var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                    chip.style.height = Spacing.Xxl;
                    chip.style.backgroundColor = colour.ToColor();
                    UiKit.Radius(chip, Shapes.Control.Radius);
                    UiKit.BorderColour(chip, theme[Role.Outline]);
                    UiKit.Border(chip, Shapes.Control.Border);
                    swatch.Add(chip);
                    swatch.Add(UiKit.Text(theme, TypeScale.Micro, name, Role.Text));
                    swatch.Add(UiKit.Text(theme, TypeScale.Micro, colour.ToHex(), Role.TextMuted));
                    swatches.Add(swatch);
                }

                UiKit.Gaps(swatches);
                s.Add(swatches);
            }));

            UiKit.Gaps(grid);
            _ui.Root.Add(_page);
            _page.RegisterCallback<GeometryChangedEvent>(Report);
            Log($"ELEVEN specimen context={theme.Name}");
        }

        // After layout: the smallest interactive target and anything wider than the page.
        private void Report(GeometryChangedEvent e)
        {
            _page.UnregisterCallback<GeometryChangedEvent>(Report);
            float pageWidth = _page.layout.width;
            float smallest = float.MaxValue;
            string smallestName = "none";
            string overflow = "none";
            int targets = 0;
            _page.Query<VisualElement>().ForEach(v =>
            {
                Rect r = v.worldBound;
                if (v.focusable && v.pickingMode == PickingMode.Position)
                {
                    targets++;
                    float side = Mathf.Min(r.width, r.height);
                    if (side < smallest)
                    {
                        smallest = side;
                        smallestName = v.name;
                    }
                }

                if (overflow == "none" && r.width > 0 && r.xMax > _page.worldBound.xMax + 0.5f) overflow = string.IsNullOrEmpty(v.name) ? v.GetType().Name : v.name;
            });
            Log(FormattableString.Invariant(
                $"ELEVEN specimen targets={targets} smallest={(targets > 0 ? smallest : 0):F1} smallestName={smallestName.Replace(' ', '_')} pageWidth={pageWidth:F0} overflow={overflow.Replace(' ', '_')} reducedMotion={(UiRoot.ReducedMotion ? "yes" : "no")}"));
        }

        private static VisualElement Card(Theme theme, string title, Action<VisualElement> fill)
        {
            UiKit.Surface card = UiKit.MakeCard(theme, title);
            card.Element.style.flexGrow = 1;
            card.Element.style.flexBasis = 300;
            card.Element.style.minWidth = 0;
            card.Element.style.marginBottom = Spacing.L;
            card.Actions.style.display = DisplayStyle.None;
            fill(card.Content);
            return card.Element;
        }

        private static VisualElement Wrapping()
        {
            VisualElement row = UiKit.Row(Spacing.S);
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = Spacing.Xs;
            return row;
        }

        private static Label Caption(Theme theme, string text)
        {
            Label caption = UiKit.Text(theme, TypeScale.Small, text, Role.TextMuted);
            caption.style.marginBottom = Spacing.M;
            return caption;
        }

        private static void Log(string message) => Debug.Log(message);
    }
}
