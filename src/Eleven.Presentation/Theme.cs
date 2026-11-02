using System;
using System.Collections.Generic;

namespace Eleven.Presentation
{
    /// <summary>
    /// The semantic roles components read (#48). There is deliberately no generic "football" role:
    /// pitch, kit and gameplay colours belong to <see cref="Domain"/>.
    /// </summary>
    public enum Role
    {
        /// <summary>The page or screen behind everything.</summary>
        SurfacePage,

        /// <summary>Controls and raised surfaces.</summary>
        SurfaceRaised,

        /// <summary>Cards and panels.</summary>
        SurfaceCard,

        /// <summary>Sheets and dialogs.</summary>
        SurfaceSheet,

        /// <summary>Body text on any surface.</summary>
        Text,

        /// <summary>Headings and titles on any surface.</summary>
        TextHeading,

        /// <summary>Muted essential text: metadata, hints.</summary>
        TextMuted,

        /// <summary>Text and symbols on any coloured action or state fill.</summary>
        OnAction,

        /// <summary>Component boundaries.</summary>
        Outline,

        /// <summary>The hard offset shadow under raised components.</summary>
        Shadow,

        /// <summary>A control's inner edge: a light band along the inside of its top border.</summary>
        InnerEdge,

        /// <summary>
        /// The keyboard focus ring: the outer part of a two-colour indicator (WCAG technique C40). It
        /// is drawn on <see cref="FocusGap"/>, so the pair reads at 3:1 on any background, the pitch
        /// included.
        /// </summary>
        FocusRing,

        /// <summary>The inner part of the focus indicator, between the component and <see cref="FocusRing"/>.</summary>
        FocusGap,

        /// <summary>Dims what is behind a modal sheet.</summary>
        Scrim,

        ActionPrimary,
        ActionSupporting,
        ActionInformation,
        Selection,
        StateSuccess,
        StateWarning,
        StateDanger,
    }

    /// <summary>
    /// A mapping from every <see cref="Role"/> to a colour. Themes and contexts swap the mapping;
    /// components never recolour themselves.
    /// </summary>
    public sealed class Theme
    {
        private readonly Rgba[] _colours;

        private Theme(string name, Rgba[] colours)
        {
            Name = name;
            _colours = colours;
        }

        public string Name { get; }

        public Rgba this[Role role] => _colours[(int)role];

        /// <summary>The ordinary mapping: light surfaces, Ink text and outlines.</summary>
        public static readonly Theme Ordinary = Map("ordinary", new Dictionary<Role, Rgba>
        {
            [Role.SurfacePage] = Primitives.Chalk50,
            [Role.SurfaceRaised] = Primitives.Paper0,
            [Role.SurfaceCard] = Primitives.Paper0,
            [Role.SurfaceSheet] = Primitives.Chalk50,
            [Role.Text] = Primitives.Ink900,
            [Role.TextHeading] = Primitives.Ink900,
            [Role.TextMuted] = Primitives.Slate500,
            [Role.OnAction] = Primitives.Ink900,
            [Role.Outline] = Primitives.Ink900,
            [Role.Shadow] = Primitives.Ink900,
            [Role.InnerEdge] = Primitives.Paper0.WithAlpha(0.55),
            [Role.FocusRing] = Primitives.Ink900,
            [Role.FocusGap] = Primitives.Paper0,
            [Role.Scrim] = Primitives.Ink900.WithAlpha(0.5),
            [Role.ActionPrimary] = Primitives.Royal300,
            [Role.ActionSupporting] = Primitives.Violet200,
            [Role.ActionInformation] = Primitives.Sky300,
            [Role.Selection] = Primitives.Grass300,
            [Role.StateSuccess] = Primitives.Green400,
            [Role.StateWarning] = Primitives.Gold300,
            [Role.StateDanger] = Primitives.Red400,
        });

        /// <summary>The dark-context mapping: navy-black surfaces, Chalk text and light outlines. Coloured fills keep Ink text.</summary>
        public static readonly Theme Dark = Map("dark", new Dictionary<Role, Rgba>
        {
            [Role.SurfacePage] = Primitives.Night900,
            [Role.SurfaceRaised] = Primitives.Night800,
            [Role.SurfaceCard] = Primitives.Night800,
            [Role.SurfaceSheet] = Primitives.Night900,
            [Role.Text] = Primitives.Chalk50,
            [Role.TextHeading] = Primitives.Paper0,
            [Role.TextMuted] = Primitives.Slate300,
            [Role.OnAction] = Primitives.Ink900,
            [Role.Outline] = Primitives.Chalk50,
            [Role.Shadow] = Primitives.Ink900,
            [Role.InnerEdge] = Primitives.Paper0.WithAlpha(0.3),
            [Role.FocusRing] = Primitives.Chalk50,
            [Role.FocusGap] = Primitives.Night900,
            [Role.Scrim] = Primitives.Ink900.WithAlpha(0.7),
            [Role.ActionPrimary] = Primitives.Royal300,
            [Role.ActionSupporting] = Primitives.Violet200,
            [Role.ActionInformation] = Primitives.Sky300,
            [Role.Selection] = Primitives.Grass300,
            [Role.StateSuccess] = Primitives.Green400,
            [Role.StateWarning] = Primitives.Gold300,
            [Role.StateDanger] = Primitives.Red400,
        });

        public static IReadOnlyList<Theme> All { get; } = new[] { Ordinary, Dark };

        public static readonly Role[] Roles = (Role[])Enum.GetValues(typeof(Role));

        // Every role must be mapped: a missing one fails here, at start-up, not in a component.
        private static Theme Map(string name, Dictionary<Role, Rgba> roles)
        {
            var values = (Role[])Enum.GetValues(typeof(Role));
            var colours = new Rgba[values.Length];
            foreach (Role role in values)
            {
                if (!roles.TryGetValue(role, out Rgba colour)) throw new InvalidOperationException($"Theme {name} has no {role}.");
                colours[(int)role] = colour;
            }

            return new Theme(name, colours);
        }

        /// <summary>The surfaces text and controls sit on.</summary>
        public static readonly Role[] Surfaces = { Role.SurfacePage, Role.SurfaceRaised, Role.SurfaceCard, Role.SurfaceSheet };

        /// <summary>The text roles used on surfaces.</summary>
        public static readonly Role[] TextRoles = { Role.Text, Role.TextHeading, Role.TextMuted };

        /// <summary>The coloured fills that carry <see cref="Role.OnAction"/> text.</summary>
        public static readonly Role[] Fills =
        {
            Role.ActionPrimary, Role.ActionSupporting, Role.ActionInformation, Role.Selection,
            Role.StateSuccess, Role.StateWarning, Role.StateDanger,
        };
    }
}
