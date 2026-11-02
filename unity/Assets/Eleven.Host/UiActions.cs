using System;
using Eleven.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>What an action does: its emphasis, from the one forward action down.</summary>
    internal enum ActionRole
    {
        Primary,
        Supporting,
        Utility,
        Navigation,
        Destructive,
    }

    /// <summary>
    /// The action recipe (#48): a strong outline, rounded geometry and a hard offset shadow with no
    /// blur. The layers are separate elements, so states compose instead of erasing one another:
    /// the shadow (elevation), the body (fill, outline, label) and the two-colour focus indicator.
    /// Pressed moves the body into its shadow; disabled drops the elevation and says why in text.
    /// </summary>
    internal static class UiActions
    {
        public sealed class Action
        {
            public VisualElement Element;
            public VisualElement Shadow;
            public VisualElement Body;
            public Label Label;
            public VisualElement Focus;
            public string Name;
        }

        public static Action Make(UiRoot ui, ActionRole role, string name, System.Action onClick)
        {
            Theme theme = ui.Theme;
            Shape shape = role == ActionRole.Primary ? Shapes.PrimaryAction : Shapes.Control;
            var a = new Action { Name = name, Element = new VisualElement { name = name, focusable = true } };
            a.Element.pickingMode = PickingMode.Position;

            a.Focus = Stack(Interaction.FocusGap + Interaction.FocusRing, shape.Radius + Interaction.FocusGap + Interaction.FocusRing);
            a.Focus.style.borderTopColor = a.Focus.style.borderBottomColor = a.Focus.style.borderLeftColor = a.Focus.style.borderRightColor = theme[Role.FocusRing].ToColor();
            Border(a.Focus, Interaction.FocusRing);
            // Inside the ring's border: a band of the gap colour between the ring and the component.
            var gap = Stack(0, shape.Radius + Interaction.FocusGap);
            gap.style.borderTopColor = gap.style.borderBottomColor = gap.style.borderLeftColor = gap.style.borderRightColor = theme[Role.FocusGap].ToColor();
            Border(gap, Interaction.FocusGap);
            a.Focus.Add(gap);
            a.Focus.style.display = DisplayStyle.None;

            a.Shadow = Stack(0, shape.Radius);
            a.Shadow.style.translate = new Translate(shape.Shadow, shape.Shadow);
            a.Shadow.style.backgroundColor = theme[Role.Shadow].ToColor();

            a.Body = Stack(0, shape.Radius);
            a.Body.style.backgroundColor = Fill(theme, role).ToColor();
            a.Body.style.borderTopColor = a.Body.style.borderBottomColor = a.Body.style.borderLeftColor = a.Body.style.borderRightColor = theme[Role.Outline].ToColor();
            Border(a.Body, shape.Border);
            a.Body.style.justifyContent = Justify.Center;
            a.Body.style.alignItems = Align.Center;

            a.Label = new Label(name.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            ApplyType(a.Label, TypeScale.Label);
            a.Label.style.color = (IsFilled(role) ? theme[Role.OnAction] : theme[Role.Text]).ToColor();
            a.Label.style.unityTextAlign = TextAnchor.MiddleCenter;
            a.Label.style.whiteSpace = WhiteSpace.Normal;
            a.Body.Add(a.Label);

            a.Element.Add(a.Focus);
            a.Element.Add(a.Shadow);
            a.Element.Add(a.Body);

            a.Element.RegisterCallback<PointerDownEvent>(_ => Press(a, shape, true));
            a.Element.RegisterCallback<PointerUpEvent>(_ => Press(a, shape, false));
            a.Element.RegisterCallback<PointerLeaveEvent>(_ => Press(a, shape, false));
            a.Element.RegisterCallback<ClickEvent>(_ => onClick());
            a.Element.RegisterCallback<NavigationSubmitEvent>(_ => onClick());
            // The ring shows for keyboard focus, not after every tap.
            a.Element.RegisterCallback<FocusInEvent>(_ => a.Focus.style.display = ui.KeyboardFocus ? DisplayStyle.Flex : DisplayStyle.None);
            a.Element.RegisterCallback<NavigationMoveEvent>(_ => a.Focus.style.display = DisplayStyle.Flex);
            a.Element.RegisterCallback<FocusOutEvent>(_ => a.Focus.style.display = DisplayStyle.None);
            return a;
        }

        /// <summary>Size, weight, tracking and case for a type role.</summary>
        public static void ApplyType(TextElement text, TypeStyle type)
        {
            text.style.fontSize = type.Size;
            text.style.unityFontStyleAndWeight = type.Weight == Weight.Regular ? FontStyle.Normal : FontStyle.Bold;
            text.style.letterSpacing = (float)(type.Tracking * type.Size);
            text.style.marginLeft = text.style.marginRight = text.style.marginTop = text.style.marginBottom = 0;
            text.style.paddingLeft = text.style.paddingRight = Spacing.Xs;
            text.style.paddingTop = text.style.paddingBottom = 0;
        }

        private static void Press(Action a, Shape shape, bool down)
        {
            // Into the shadow: the body moves by the shadow's offset, so the shadow disappears under it.
            a.Body.style.translate = down ? new Translate(shape.Shadow, shape.Shadow) : new Translate(0, 0);
        }

        private static bool IsFilled(ActionRole role) => role == ActionRole.Primary || role == ActionRole.Supporting || role == ActionRole.Destructive;

        private static Rgba Fill(Theme theme, ActionRole role) => role switch
        {
            ActionRole.Primary => theme[Role.ActionPrimary],
            ActionRole.Supporting => theme[Role.ActionSupporting],
            ActionRole.Destructive => theme[Role.StateDanger],
            _ => theme[Role.SurfaceRaised],
        };

        // A child filling its parent, grown by `outset` on every side, with this corner radius.
        private static VisualElement Stack(float outset, float radius)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = -outset;
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = radius;
            return e;
        }

        private static void Border(VisualElement e, float width)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
        }
    }
}
