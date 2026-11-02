using System;
using System.Collections.Generic;
using System.Globalization;
using Eleven.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>What an action does: its emphasis, from the one forward action down.</summary>
    internal enum ActionRole
    {
        /// <summary>The one forward action in a view.</summary>
        Primary,

        /// <summary>Opens a sheet or supports the primary.</summary>
        Supporting,

        /// <summary>Everyday tools: Reset, Run scenario.</summary>
        Utility,

        /// <summary>Back, close, dismiss.</summary>
        Navigation,

        /// <summary>Deletes or discards.</summary>
        Destructive,
    }

    /// <summary>
    /// The component recipes of ELEVEN's visual system (#48, ADR-0003). Each takes the
    /// <see cref="Theme"/> of its context, so the same recipe renders the ordinary and the dark
    /// mapping, and reads semantic roles only.
    ///
    /// Raised components are separate layers, so states compose instead of erasing one another:
    /// the hard shadow (elevation), the body (fill, outline, content) and the two-colour focus
    /// indicator. Pressed moves the body into its shadow; disabled drops the elevation and dashes
    /// nothing, because UI Toolkit has no dashed borders, so it also says why in text.
    /// </summary>
    internal static class UiKit
    {
        // ---- Raised: the shared frame of actions, segments, cards and sheets ----

        public class Raised
        {
            public VisualElement Element;
            public VisualElement Shadow;
            public VisualElement Body;
            public VisualElement Focus;
            public Shape Shape;
        }

        public static T MakeRaised<T>(Theme theme, Shape shape, Rgba fill, bool focusable) where T : Raised, new()
        {
            var r = new T { Shape = shape, Element = new VisualElement { focusable = focusable } };
            if (focusable)
            {
                r.Focus = Stack(Interaction.FocusGap + Interaction.FocusRing, shape.Radius + Interaction.FocusGap + Interaction.FocusRing);
                BorderColour(r.Focus, theme[Role.FocusRing]);
                Border(r.Focus, Interaction.FocusRing);
                var gap = Stack(0, shape.Radius + Interaction.FocusGap);
                BorderColour(gap, theme[Role.FocusGap]);
                Border(gap, Interaction.FocusGap);
                r.Focus.Add(gap);
                r.Focus.style.display = DisplayStyle.None;
                r.Element.Add(r.Focus);
            }

            r.Shadow = Stack(0, shape.Radius);
            r.Shadow.style.translate = new Translate(shape.Shadow, shape.Shadow);
            r.Shadow.style.backgroundColor = theme[Role.Shadow].ToColor();
            r.Element.Add(r.Shadow);

            r.Body = new VisualElement { pickingMode = PickingMode.Ignore };
            r.Body.style.flexGrow = 1;
            Radius(r.Body, shape.Radius);
            r.Body.style.backgroundColor = fill.ToColor();
            BorderColour(r.Body, theme[Role.Outline]);
            Border(r.Body, shape.Border);
            r.Element.Add(r.Body);
            return r;
        }

        /// <summary>Wires press-into-shadow and keyboard-only focus rings to a raised component.</summary>
        public static void Interactive(UiRoot ui, Raised r, System.Action activate)
        {
            r.Element.pickingMode = PickingMode.Position;
            r.Element.RegisterCallback<PointerDownEvent>(_ => SetPressed(r, true));
            r.Element.RegisterCallback<PointerUpEvent>(_ => SetPressed(r, false));
            r.Element.RegisterCallback<PointerLeaveEvent>(_ => SetPressed(r, false));
            r.Element.RegisterCallback<ClickEvent>(_ => activate());
            r.Element.RegisterCallback<NavigationSubmitEvent>(_ => activate());
            if (r.Focus == null) return;
            r.Element.RegisterCallback<FocusInEvent>(_ => r.Focus.style.display = ui.KeyboardFocus ? DisplayStyle.Flex : DisplayStyle.None);
            r.Element.RegisterCallback<NavigationMoveEvent>(_ => r.Focus.style.display = DisplayStyle.Flex);
            r.Element.RegisterCallback<FocusOutEvent>(_ => r.Focus.style.display = DisplayStyle.None);
        }

        /// <summary>Pressed: the body moves by the shadow's offset, into the shadow.</summary>
        public static void SetPressed(Raised r, bool down)
        {
            int offset = UiRoot.ReducedMotion ? 0 : r.Shape.Shadow;
            r.Body.style.translate = down ? new Translate(offset, offset) : new Translate(0, 0);
            if (UiRoot.ReducedMotion) r.Shadow.style.visibility = down ? Visibility.Hidden : Visibility.Visible;
        }

        // ---- Actions ----

        public sealed class Action : Raised
        {
            public Label Label;
            public string Name;
            public ActionRole Role;
            public bool Enabled = true;
            public bool Busy;
        }

        /// <summary>
        /// An action: a strong outline, rounded geometry and a hard shadow. Its label renders in
        /// uppercase; <paramref name="name"/> stays in natural case.
        /// </summary>
        public static Action MakeAction(UiRoot ui, Theme theme, ActionRole role, string name, System.Action onClick)
        {
            Shape shape = role == ActionRole.Primary ? Shapes.PrimaryAction : Shapes.Control;
            Action a = MakeRaised<Action>(theme, shape, ActionFill(theme, role), focusable: true);
            a.Name = name;
            a.Role = role;
            a.Element.name = name;
            a.Element.style.minWidth = a.Element.style.minHeight = Interaction.MinTarget;
            a.Body.style.justifyContent = Justify.Center;
            a.Body.style.alignItems = Align.Center;
            a.Body.style.paddingLeft = a.Body.style.paddingRight = Spacing.S;

            a.Label = Text(theme, TypeScale.Label, name, ActionText(role));
            a.Label.style.unityTextAlign = TextAnchor.MiddleCenter;
            a.Body.Add(a.Label);

            Interactive(ui, a, () =>
            {
                if (a.Enabled && !a.Busy) onClick();
            });
            return a;
        }

        /// <summary>
        /// Disabled: no elevation (the body sits where pressed), so it reads as unavailable by shape,
        /// and it takes no input. The caller shows <paramref name="reason"/> where the user looks.
        /// </summary>
        public static void SetEnabled(Action a, bool enabled)
        {
            a.Enabled = enabled;
            a.Shadow.style.visibility = enabled ? Visibility.Visible : Visibility.Hidden;
            a.Body.style.translate = enabled ? new Translate(0, 0) : new Translate(a.Shape.Shadow, a.Shape.Shadow);
            a.Element.SetEnabled(enabled);
        }

        /// <summary>Busy: the label says what is happening, and further presses are ignored.</summary>
        public static void SetBusy(Action a, bool busy, string busyLabel)
        {
            a.Busy = busy;
            a.Label.text = Display(busy ? busyLabel + "…" : a.Name, TypeScale.Label);
        }

        // ---- Text ----

        /// <summary>Text in a type role and a colour role. Uppercase roles render uppercase.</summary>
        public static Label Text(Theme theme, TypeStyle type, string text, Role colour = Role.Text)
        {
            var label = new Label(Display(text, type)) { pickingMode = PickingMode.Ignore };
            ApplyType(label, type);
            label.style.color = theme[colour].ToColor();
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>Size, weight, tracking and case for a type role.</summary>
        public static void ApplyType(TextElement text, TypeStyle type)
        {
            text.style.fontSize = type.Size * UiRoot.TextScale;
            text.style.unityFontStyleAndWeight = type.Weight == Weight.Regular ? FontStyle.Normal : FontStyle.Bold;
            text.style.letterSpacing = (float)(type.Tracking * type.Size * UiRoot.TextScale);
            text.style.marginLeft = text.style.marginRight = text.style.marginTop = text.style.marginBottom = 0;
            text.style.paddingLeft = text.style.paddingRight = text.style.paddingTop = text.style.paddingBottom = 0;
        }

        public static string Display(string text, TypeStyle type) =>
            type.Uppercase ? text.ToUpper(CultureInfo.InvariantCulture) : text;

        // ---- Surfaces ----

        public sealed class Surface : Raised
        {
            public Label Title;
            public VisualElement Content;
            public VisualElement Actions;
        }

        /// <summary>A card: a framed surface with a title, content and an action row.</summary>
        public static Surface MakeCard(Theme theme, string title) => MakeSurface(theme, Shapes.Card, Role.SurfaceCard, title, TypeScale.Title);

        /// <summary>A sheet or dialog: the largest frame, for modal content with a clear dismiss path.</summary>
        public static Surface MakeSheet(Theme theme, string title) => MakeSurface(theme, Shapes.Sheet, Role.SurfaceSheet, title, TypeScale.Heading);

        private static Surface MakeSurface(Theme theme, Shape shape, Role fill, string title, TypeStyle titleType)
        {
            Surface s = MakeRaised<Surface>(theme, shape, theme[fill], focusable: false);
            s.Element.pickingMode = PickingMode.Position;
            s.Body.pickingMode = PickingMode.Position;
            s.Body.style.paddingLeft = s.Body.style.paddingRight = Spacing.L;
            s.Body.style.paddingTop = s.Body.style.paddingBottom = Spacing.L;
            if (!string.IsNullOrEmpty(title))
            {
                s.Title = Text(theme, titleType, title, Role.TextHeading);
                s.Title.style.marginBottom = Spacing.M;
                s.Body.Add(s.Title);
            }

            s.Content = new VisualElement { pickingMode = PickingMode.Ignore };
            s.Body.Add(s.Content);
            s.Actions = Row(Spacing.S);
            s.Actions.style.marginTop = Spacing.L;
            s.Body.Add(s.Actions);
            return s;
        }

        // ---- HUD data ----

        public sealed class Data
        {
            public VisualElement Element;
            public Label Name;
            public Label Value;
        }

        /// <summary>
        /// A stable label and a changing value in tabular figures, in a slot sized for
        /// <paramref name="widestValue"/>, so the layout never jitters as digits change.
        /// </summary>
        public static Data MakeData(Theme theme, string name, string widestValue)
        {
            var d = new Data { Element = Row(Spacing.Xs) };
            d.Name = Text(theme, TypeScale.Micro, name, Role.TextMuted);
            d.Value = Text(theme, TypeScale.Data, widestValue, Role.Text);
            d.Value.style.unityTextAlign = TextAnchor.MiddleRight;
            d.Value.style.whiteSpace = WhiteSpace.NoWrap;
            // The built-in font's figures share one advance; reserve the widest value's width.
            d.Value.style.minWidth = widestValue.Length * TypeScale.Data.Size * 0.6f;
            d.Element.style.alignItems = Align.Center;
            d.Element.Add(d.Name);
            d.Element.Add(d.Value);
            Gaps(d.Element);
            return d;
        }

        // ---- Selection ----

        public sealed class Segmented
        {
            public VisualElement Element;
            public readonly List<Raised> Segments = new List<Raised>();
            public readonly List<Label> Labels = new List<Label>();
            public readonly List<VisualElement> Marks = new List<VisualElement>();
            public int Selected;
        }

        /// <summary>
        /// A segmented choice in one coherent frame. The selected segment takes the selection fill,
        /// a drawn mark and bold text, so its state never rests on colour alone. Segments wrap under
        /// enlarged text rather than shrink below the touch minimum.
        /// </summary>
        public static Segmented MakeSegmented(UiRoot ui, Theme theme, string[] options, int selected, Action<int> onChange)
        {
            var seg = new Segmented { Element = Row(Spacing.Xs), Selected = selected };
            seg.Element.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                Raised r = MakeRaised<Raised>(theme, Shapes.Control, theme[Role.SurfaceRaised], focusable: true);
                r.Element.name = options[i];
                r.Element.style.minWidth = r.Element.style.minHeight = Interaction.MinTarget;
                r.Element.style.marginBottom = Spacing.Xs;
                r.Body.style.flexDirection = FlexDirection.Row;
                r.Body.style.alignItems = Align.Center;
                r.Body.style.justifyContent = Justify.Center;
                r.Body.style.paddingLeft = r.Body.style.paddingRight = Spacing.S;

                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.style.width = mark.style.height = Spacing.S;
                Radius(mark, Spacing.S / 2f);
                mark.style.backgroundColor = theme[Role.OnAction].ToColor();
                mark.style.marginRight = Spacing.Xs;
                Label label = Text(theme, TypeScale.Label, options[i], Role.Text);
                r.Body.Add(mark);
                r.Body.Add(label);
                seg.Segments.Add(r);
                seg.Labels.Add(label);
                seg.Marks.Add(mark);
                seg.Element.Add(r.Element);
                Interactive(ui, r, () =>
                {
                    Select(theme, seg, index);
                    onChange?.Invoke(index);
                });
            }

            Select(theme, seg, selected);
            Gaps(seg.Element);
            return seg;
        }

        public static void Select(Theme theme, Segmented seg, int index)
        {
            seg.Selected = index;
            for (int i = 0; i < seg.Segments.Count; i++)
            {
                bool on = i == index;
                seg.Segments[i].Body.style.backgroundColor = (on ? theme[Role.Selection] : theme[Role.SurfaceRaised]).ToColor();
                seg.Marks[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                seg.Labels[i].style.color = (on ? theme[Role.OnAction] : theme[Role.Text]).ToColor();
                seg.Labels[i].style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        // ---- Value control ----

        public sealed class Value
        {
            public VisualElement Element;
            public Label Name;
            public Label Meta;
            public Label Shown;
            public Action Less;
            public Action More;
            public VisualElement Track;
            public VisualElement Fill;
            public VisualElement Thumb;
            public double Min;
            public double Max;
            public double Step;
            public int Decimals;
            public string Unit;
            public double Current;
            public Action<double> Changed;
        }

        /// <summary>
        /// A labelled value: −, the value and + in one row, a full-width slider below. Precise
        /// adjustment never needs a drag, and the row fits 320 pt with 44 pt targets.
        /// </summary>
        public static Value MakeValue(UiRoot ui, Theme theme, string name, string unit, string meta, double min, double max, double step, int decimals, double value, Action<double> changed)
        {
            var v = new Value { Min = min, Max = max, Step = step, Decimals = decimals, Unit = unit, Current = value, Changed = changed };
            v.Element = new VisualElement();
            v.Element.style.marginBottom = Spacing.M;

            v.Name = Text(theme, TypeScale.Body, $"{name} ({unit})", Role.Text);
            v.Name.style.unityFontStyleAndWeight = FontStyle.Bold;
            v.Meta = Text(theme, TypeScale.Small, meta, Role.TextMuted);
            v.Element.Add(v.Name);
            v.Element.Add(v.Meta);

            VisualElement row = Row(Spacing.S);
            row.style.alignItems = Align.Center;
            row.style.marginTop = Spacing.Xs;
            v.Less = MakeAction(ui, theme, ActionRole.Utility, "−", () => Nudge(v, -1));
            v.More = MakeAction(ui, theme, ActionRole.Utility, "+", () => Nudge(v, 1));
            v.Shown = Text(theme, TypeScale.Data, "", Role.Text);
            v.Shown.style.minWidth = 6 * TypeScale.Data.Size * 0.6f;
            v.Shown.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(v.Less.Element);
            row.Add(v.Shown);
            row.Add(v.More.Element);
            Gaps(row);
            v.Element.Add(row);

            // The slider: its own full-width row, a track with a fill and a thumb of touch size.
            v.Track = new VisualElement { name = name + " slider" };
            v.Track.style.height = Interaction.MinTarget;
            v.Track.style.marginTop = Spacing.Xs;
            v.Track.style.justifyContent = Justify.Center;
            var rail = new VisualElement { pickingMode = PickingMode.Ignore };
            rail.style.height = Spacing.Xs;
            Radius(rail, Spacing.Xs / 2f);
            rail.style.backgroundColor = theme[Role.TextMuted].ToColor();
            v.Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Fill.style.position = Position.Absolute;
            v.Fill.style.left = 0;
            v.Fill.style.height = Spacing.Xs;
            v.Fill.style.top = (Interaction.MinTarget - Spacing.Xs) / 2f;
            v.Fill.style.backgroundColor = theme[Role.ActionPrimary].ToColor();
            v.Thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Thumb.style.position = Position.Absolute;
            v.Thumb.style.width = v.Thumb.style.height = Spacing.L + Spacing.Xs;
            v.Thumb.style.top = (Interaction.MinTarget - Spacing.L - Spacing.Xs) / 2f;
            Radius(v.Thumb, (Spacing.L + Spacing.Xs) / 2f);
            v.Thumb.style.backgroundColor = theme[Role.SurfaceRaised].ToColor();
            BorderColour(v.Thumb, theme[Role.Outline]);
            Border(v.Thumb, Shapes.Control.Border);
            v.Track.Add(rail);
            v.Track.Add(v.Fill);
            v.Track.Add(v.Thumb);
            v.Element.Add(v.Track);

            v.Track.RegisterCallback<PointerDownEvent>(e =>
            {
                v.Track.CapturePointer(e.pointerId);
                Drag(v, e.localPosition.x);
            });
            v.Track.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (v.Track.HasPointerCapture(e.pointerId)) Drag(v, e.localPosition.x);
            });
            v.Track.RegisterCallback<PointerUpEvent>(e => v.Track.ReleasePointer(e.pointerId));
            v.Track.RegisterCallback<GeometryChangedEvent>(_ => Show(v));
            Show(v);
            return v;
        }

        public static void SetValue(Value v, double value)
        {
            v.Current = Math.Max(v.Min, Math.Min(v.Max, value));
            Show(v);
            v.Changed?.Invoke(v.Current);
        }

        private static void Nudge(Value v, int direction)
        {
            double n = direction > 0 ? Math.Floor(v.Current / v.Step + 1e-9) + 1 : Math.Ceiling(v.Current / v.Step - 1e-9) - 1;
            SetValue(v, Math.Round(n * v.Step, Math.Max(0, v.Decimals)));
        }

        private static void Drag(Value v, float x)
        {
            float width = v.Track.layout.width;
            if (!(width > 0)) return;
            double t = Math.Max(0, Math.Min(1, x / width));
            double raw = v.Min + t * (v.Max - v.Min);
            SetValue(v, Math.Round(Math.Round(raw / v.Step) * v.Step, Math.Max(0, v.Decimals)));
        }

        private static void Show(Value v)
        {
            v.Shown.text = v.Current.ToString("0.######", CultureInfo.InvariantCulture);
            float width = v.Track.layout.width;
            if (!(width > 0)) return;
            float t = (float)((v.Current - v.Min) / (v.Max - v.Min));
            float thumb = Spacing.L + Spacing.Xs;
            v.Fill.style.width = t * width;
            v.Thumb.style.left = t * (width - thumb);
        }

        // ---- Status ----

        /// <summary>The status line: a small card at the top that states what just happened.</summary>
        public static Surface MakeStatus(Theme theme)
        {
            Surface s = MakeSurface(theme, Shapes.Control, Role.SurfaceRaised, null, TypeScale.Body);
            s.Body.style.paddingTop = s.Body.style.paddingBottom = Spacing.S;
            s.Body.style.paddingLeft = s.Body.style.paddingRight = Spacing.M;
            s.Actions.style.display = DisplayStyle.None;
            s.Title = Text(theme, TypeScale.Body, "", Role.Text);
            s.Content.Add(s.Title);
            s.Element.style.display = DisplayStyle.None;
            return s;
        }

        // ---- Layout helpers ----

        /// <summary>A row with a gap between its children; call <see cref="Gaps"/> once they are added.</summary>
        public static VisualElement Row(float gap)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore, userData = gap };
            row.style.flexDirection = FlexDirection.Row;
            return row;
        }

        /// <summary>
        /// Applies a row's gap to its children, once: UI Toolkit has no gap property, so each child
        /// but the last carries it as a right margin, and wrapping rows also as a bottom margin. Set
        /// once rather than on every geometry change, which would make layout feed itself.
        /// </summary>
        public static void Gaps(VisualElement row)
        {
            float gap = row.userData is float g ? g : 0;
            bool wraps = row.style.flexWrap.value == Wrap.Wrap;
            for (int i = 0; i < row.childCount; i++)
            {
                row[i].style.marginRight = wraps || i < row.childCount - 1 ? gap : 0;
                if (wraps) row[i].style.marginBottom = gap;
            }
        }

        public static VisualElement Stack(float outset, float radius)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = -outset;
            Radius(e, radius);
            return e;
        }

        public static void Radius(VisualElement e, float radius)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = radius;
        }

        public static void Border(VisualElement e, float width)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
        }

        public static void BorderColour(VisualElement e, Rgba colour)
        {
            Color c = colour.ToColor();
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
        }

        private static bool IsFilled(ActionRole role) => role == ActionRole.Primary || role == ActionRole.Supporting || role == ActionRole.Destructive;

        private static Role ActionText(ActionRole role) => IsFilled(role) ? Role.OnAction : Role.Text;

        private static Rgba ActionFill(Theme theme, ActionRole role) => role switch
        {
            ActionRole.Primary => theme[Role.ActionPrimary],
            ActionRole.Supporting => theme[Role.ActionSupporting],
            ActionRole.Destructive => theme[Role.StateDanger],
            _ => theme[Role.SurfaceRaised],
        };
    }
}
