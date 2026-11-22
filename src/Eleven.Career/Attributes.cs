#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Eleven.Career
{
    /// <summary>
    /// A footballer's football attributes (#80, #157), each 1–20. The list is data: a player
    /// stores one value per entry, in this order, so an attribute is added here without changing
    /// any other code. Ids are stable; display names are not.
    /// </summary>
    public static class PlayerAttributes
    {
        public const int Min = 1, Max = 20;

        /// <summary>The attributes, in their stored order: #80's 26 football characteristics.</summary>
        public static readonly ReadOnlyCollection<string> Ids = Array.AsReadOnly(new[]
        {
            "acceleration", "topSpeed", "agility", "balance", "stamina", "strength",
            "firstTouch", "dribbling", "shortPassing", "longPassing", "crossing",
            "shootingPower", "finishing", "curve", "heading", "jumping",
            "tackling", "marking", "positioning", "anticipation", "vision", "decisions", "composure",
            "keeperReflexes", "keeperHandling", "keeperPositioning",
        });

        private static readonly Dictionary<string, int> s_index = MakeIndex();

        public static int Count => Ids.Count;

        /// <summary>An attribute's place in a player's values; throws for an unknown id.</summary>
        public static int Index(string id) =>
            s_index.TryGetValue(id, out int i) ? i : throw new ArgumentException($"'{id}' is not a player attribute.", nameof(id));

        private static Dictionary<string, int> MakeIndex()
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < Ids.Count; i++) index.Add(Ids[i], i);
            return index;
        }
    }

    /// <summary>Where a footballer plays (#157): coarse positions, enough for squads and selection.</summary>
    public enum Position : byte
    {
        Goalkeeper = 0,
        CentreBack = 1,
        FullBack = 2,
        DefensiveMidfield = 3,
        CentralMidfield = 4,
        WideMidfield = 5,
        AttackingMidfield = 6,
        Striker = 7,
    }

    /// <summary>
    /// A kit colour (#157): a semantic token, resolved to a colour by the design system's kit tier
    /// (<c>Eleven.Presentation.Kit</c>), never a hard-coded value in the world (AGENTS.md). Stable
    /// numbers: they are stored.
    /// </summary>
    public enum KitColour : byte
    {
        Red = 0,
        Blue = 1,
        White = 2,
        Black = 3,
        Yellow = 4,
        Green = 5,
        SkyBlue = 6,
        Orange = 7,
        Purple = 8,
        Maroon = 9,
        Navy = 10,
        Grey = 11,
    }

    /// <summary>A footballer's stronger foot.</summary>
    public enum Foot : byte
    {
        Right = 0,
        Left = 1,
        Both = 2,
    }

    /// <summary>A manager's competences (#80), each 1–20, in their stored order.</summary>
    public static class ManagerCompetences
    {
        public static readonly ReadOnlyCollection<string> Ids = Array.AsReadOnly(new[]
        {
            "tactics", "coaching", "development", "scouting", "recruitment", "motivation", "adaptability",
        });

        public static int Count => Ids.Count;
    }
}
