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

        /// <summary>The keyboard focus ring, drawn past a gap of the surface it sits on.</summary>
        FocusRing,

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
        public static readonly Theme Ordinary = new Theme("ordinary", new Rgba[RoleCount]);

        /// <summary>The dark-context mapping: navy-black surfaces, Chalk text and light outlines. Coloured fills keep Ink text.</summary>
        public static readonly Theme Dark = new Theme("dark", new Rgba[RoleCount]);

        public static IReadOnlyList<Theme> All { get; } = new[] { Ordinary, Dark };

        public static readonly Role[] Roles = (Role[])Enum.GetValues(typeof(Role));

        private static int RoleCount => Enum.GetValues(typeof(Role)).Length;

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
