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
        public static FormationLine[] Lines(Formation f) => (FormationLine[])Table(f).Clone();

        /// <summary>
        /// The lines of <paramref name="n"/> outfielders in formation <paramref name="f"/>, from the back.
        /// From <see cref="ClosesUpFrom"/> to ten, the formation's, a side short of men closing up from
        /// the front: each man missing comes out of the furthest-forward line of more than one. Below,
        /// and above ten, the small sides' shape of #96, whatever the formation.
        /// </summary>
        public static FormationLine[] Lines(Formation f, int n)
        {
            if (n < ClosesUpFrom || n > Outfielders) return Derived(n);
            FormationLine[] lines = Lines(f);
            for (int missing = Outfielders - n; missing > 0; missing--)
            {
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (lines[i].Count <= 1) continue;
                    lines[i] = new FormationLine(lines[i].Count - 1, lines[i].Depth, lines[i].HalfWidth);
                    break;
                }
            }

            return lines;
        }

        /// <summary>The fewest outfielders who still play a formation; fewer play the small sides' shape.</summary>
        public const int ClosesUpFrom = 8;

        // Each formation's lines for ten, from the back (m, the side's own frame, ball at the centre
        // spot). Technical references, not tuned feel: the 4-4-2 is #106's shape exactly; the others
        // keep its back line at −24 m and its forwards at +4 m, a back five a little deeper, a
        // 4-2-3-1's two holding midfielders and three behind the striker between, and a front three wide.
        private static FormationLine[] Table(Formation f) => f switch
        {
            Formation.FourFourTwo => new[] { L(4, BackLine, WideRow), L(4, MiddleLine, WideRow), L(2, ForwardLine, ForwardsApart) },
            Formation.FourThreeThree => new[] { L(4, BackLine, WideRow), L(3, MiddleLine, ShapeWidth), L(3, ForwardLine, FrontThree) },
            Formation.FourTwoThreeOne => new[] { L(4, BackLine, WideRow), L(2, -15, 8), L(3, -4, FrontThree), L(1, ForwardLine, 0) },
            Formation.ThreeFiveTwo => new[] { L(3, BackLine, ShapeWidth), L(5, MiddleLine, WideFive), L(2, ForwardLine, ForwardsApart) },
            Formation.FiveThreeTwo => new[] { L(5, -25, WideFive), L(3, -11, ShapeWidth), L(2, ForwardLine, ForwardsApart) },
            Formation.ThreeFourThree => new[] { L(3, BackLine, ShapeWidth), L(4, MiddleLine, WideRow), L(3, ForwardLine, FrontThree) },
            _ => throw new ArgumentOutOfRangeException(nameof(f), f, "Unknown formation."),
        };

        private static FormationLine L(int count, double depth, double halfWidth) => new FormationLine(count, depth, halfWidth);

        // m. A front three's wide men, and a row of five's wing-backs.
        private const double FrontThree = 20, WideFive = 24;

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
