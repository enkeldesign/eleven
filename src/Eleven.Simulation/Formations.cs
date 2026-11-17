using System;

namespace Eleven.Simulation
{
    /// <summary>
    /// A side's formation (#125, M5): where each place in its shape has its home. Named for ten
    /// outfielders, from the back. Plain data in the side's <see cref="TeamState"/> (INV-3); the
    /// CPU reads it through its observation (INV-9). docs/simulation/formations.md.
    /// </summary>
    public enum Formation : byte
    {
        /// <summary>The technical reference since #106, and the default.</summary>
        FourFourTwo = 0,
        FourThreeThree = 1,
        FourTwoThreeOne = 2,
        ThreeFiveTwo = 3,
        FiveThreeTwo = 4,
        ThreeFourThree = 5,
    }

    /// <summary>
    /// One line of a formation (#125): how many play in it, its depth and its half-width, in the
    /// side's own frame with the ball at the centre spot (m; the side attacks +x).
    /// </summary>
    public readonly struct FormationLine
    {
        public FormationLine(int count, double depth, double halfWidth)
        {
            Count = count;
            Depth = depth;
            HalfWidth = halfWidth;
        }

        public int Count { get; }

        public double Depth { get; }

        /// <summary>m. How far either side of the middle its outermost two stand; 0 for one.</summary>
        public double HalfWidth { get; }
    }

    /// <summary>The library of formations (#125) and each place's home in them.</summary>
    public static class Formations
    {
        /// <summary>Every formation, in the order of <see cref="Formation"/>.</summary>
        public static readonly Formation[] All =
        {
            Formation.FourFourTwo, Formation.FourThreeThree, Formation.FourTwoThreeOne,
            Formation.ThreeFiveTwo, Formation.FiveThreeTwo, Formation.ThreeFourThree,
        };

        /// <summary>Outfielders a formation is named for.</summary>
        public const int Outfielders = 10;

        public static bool IsDefined(Formation f) => (byte)f <= (byte)Formation.ThreeFourThree;

        /// <summary>Its usual name: "4-4-2".</summary>
        public static string Name(Formation f) => f switch
        {
            Formation.FourFourTwo => "4-4-2",
            Formation.FourThreeThree => "4-3-3",
            Formation.FourTwoThreeOne => "4-2-3-1",
            Formation.ThreeFiveTwo => "3-5-2",
            Formation.FiveThreeTwo => "5-3-2",
            Formation.ThreeFourThree => "3-4-3",
            _ => throw new ArgumentOutOfRangeException(nameof(f), f, "Unknown formation."),
        };

        /// <summary>Parses a formation's name ("4-3-3") or its enum name.</summary>
        public static Formation Parse(string name)
        {
            foreach (Formation f in All)
            {
                if (Name(f) == name || f.ToString() == name) return f;
            }

            throw new ArgumentException($"Unknown formation '{name}'.", nameof(name));
        }

        /// <summary>Its lines from the back, for ten outfielders.</summary>
        public static FormationLine[] Lines(Formation f) => Derived(Outfielders);

        /// <summary>
        /// The lines of <paramref name="n"/> outfielders in formation <paramref name="f"/>, from the back.
        /// </summary>
        public static FormationLine[] Lines(Formation f, int n) => Derived(n);

        /// <summary>
        /// The home of place <paramref name="slot"/> (1 at the back) of <paramref name="n"/> in formation
        /// <paramref name="f"/>, in the side's own frame with the ball at the centre spot.
        /// </summary>
        public static Vec2 Home(Formation f, int n, int slot)
        {
            FormationLine[] lines = Lines(f, n);
            int at = slot - 1;
            foreach (FormationLine line in lines)
            {
                if (at < line.Count)
                {
                    double y = line.Count <= 1 ? 0 : (-1 + 2.0 * at / (line.Count - 1)) * line.HalfWidth;
                    return new Vec2(line.Depth, y);
                }

                at -= line.Count;
            }

            FormationLine last = lines[lines.Length - 1];
            return new Vec2(last.Depth, 0);
        }

        // The shape before formations (#96, #106): three lines, half the rest at the back, two up
        // front from eight, one below; a row of four or more wider.
        internal const double BackLine = -24, MiddleLine = -10, ForwardLine = 4, ShapeWidth = 14, WideRow = 22, ForwardsApart = 10;
        internal const int TwoForwardsFrom = 8;

        internal static FormationLine[] Derived(int n)
        {
            int forward = n >= TwoForwardsFrom ? 2 : 1;
            int back = n >= TwoForwardsFrom ? (n - 2) / 2 : n / 2;
            int middle = n - forward - back;
            return new[]
            {
                new FormationLine(back, BackLine, back <= 1 ? 0 : back >= 4 ? WideRow : ShapeWidth),
                new FormationLine(middle, MiddleLine, middle <= 1 ? 0 : middle >= 4 ? WideRow : ShapeWidth),
                new FormationLine(forward, ForwardLine, forward <= 1 ? 0 : ForwardsApart),
            };
        }
    }
}
