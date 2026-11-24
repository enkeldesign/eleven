#nullable enable
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>
    /// The fictional world from a seed (#157): <see cref="Divisions"/> divisions of
    /// <see cref="ClubsPerDivision"/> clubs, <see cref="SquadSize"/> footballers a club, one
    /// manager each. Deterministic: counter-based draws on the world's own stream (INV-6), keyed
    /// by what is drawn, for whom and which draw. Every name is invented from syllable tables:
    /// the world is fictional (#80). Its values are technical references, not tuned feel.
    /// </summary>
    public static class WorldGenerator
    {
        public const int Divisions = 3, ClubsPerDivision = 12, SquadSize = 23, KeepersPerSquad = 3;

        /// <summary>The mean attribute level of a division's clubs, top division first (1–20).</summary>
        public static readonly double[] DivisionLevel = { 13, 11, 9 };

        /// <summary>How far a club's level spreads either side of its division's.</summary>
        public const double ClubSpread = 1.5;

        /// <summary>A squad's positions, in squad order: 23 footballers, 3 of them keepers.</summary>
        public static readonly Position[] SquadTemplate =
        {
            Position.Goalkeeper, Position.Goalkeeper, Position.Goalkeeper,
            Position.CentreBack, Position.CentreBack, Position.CentreBack, Position.CentreBack,
            Position.FullBack, Position.FullBack, Position.FullBack, Position.FullBack,
            Position.DefensiveMidfield, Position.DefensiveMidfield,
            Position.CentralMidfield, Position.CentralMidfield, Position.CentralMidfield,
            Position.WideMidfield, Position.WideMidfield, Position.WideMidfield,
            Position.AttackingMidfield,
            Position.Striker, Position.Striker, Position.Striker,
        };

        // What each draw is for: the tick of the counter-based key.
        private const long ClubDraw = 1, PlayerDraw = 2, ManagerDraw = 3, NameDraw = 4;

        private static readonly string[] PlaceStarts =
        {
            "Bal", "Cor", "Dra", "Fen", "Gal", "Hes", "Isk", "Jor", "Kel", "Lun", "Mar", "Nes",
            "Orl", "Pry", "Quel", "Ros", "Sta", "Tav", "Ulm", "Vel", "Wyr", "Yst", "Zor", "Brin",
        };

        private static readonly string[] PlaceEnds =
        {
            "ane", "berg", "coln", "dor", "fell", "grove", "hurst", "mouth", "ness", "ova",
            "quay", "rith", "sund", "thorpe", "vale", "wick", "mere", "stead",
        };

        private static readonly string[] ClubKinds = { "United", "Athletic", "Rovers", "Wanderers", "Town", "Albion", "City", "Harriers" };

        private static readonly string[] FirstNames =
        {
            "Aldo", "Bram", "Cass", "Dario", "Edvin", "Falk", "Goran", "Hale", "Ilan", "Jory", "Kito", "Lenn",
            "Mats", "Nilo", "Orin", "Pell", "Rafe", "Sten", "Tavi", "Ulric", "Vesa", "Wim", "Yari", "Zeno",
        };

        private static readonly string[] SurnameStarts = { "Ab", "Bel", "Cran", "Dal", "Eck", "Fro", "Gund", "Holm", "Ist", "Kar", "Lod", "Mol", "Nyr", "Ost", "Pal", "Rud", "Sev", "Tor", "Ulv", "Vand", "Wes" };

        private static readonly string[] SurnameEnds = { "ard", "berg", "dahl", "enko", "feld", "gren", "ini", "kov", "lund", "mann", "oni", "quist", "son", "ström", "vik", "wall" };

        // The kit tokens a club's shirt and shorts are drawn from (the design system holds the colours).
        private static readonly KitColour[] Colours =
        {
            KitColour.Red, KitColour.Blue, KitColour.White, KitColour.Black, KitColour.Yellow, KitColour.Green,
            KitColour.SkyBlue, KitColour.Orange, KitColour.Purple, KitColour.Maroon, KitColour.Navy, KitColour.Grey,
        };

        private static readonly string[] Groups = BuildGroups();

        /// <summary>The world for <paramref name="seed"/>.</summary>
        public static World Generate(ulong seed)
        {
            var world = new World { Seed = seed };
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var shorts = new HashSet<string>(System.StringComparer.Ordinal);
            int nextPlayer = 1;
            for (int d = 0; d < Divisions; d++)
            {
                for (int k = 0; k < ClubsPerDivision; k++)
                {
                    int id = d * ClubsPerDivision + k + 1;
                    double level = DivisionLevel[d] + (Draw(seed, ClubDraw, id, 0) * 2 - 1) * ClubSpread;
                    Club club = MakeClub(seed, id, d + 1, level, names, shorts);
                    world.Clubs.Add(club);
                    for (int s = 0; s < SquadTemplate.Length; s++)
                    {
                        Player p = MakePlayer(seed, nextPlayer++, club.Id, SquadTemplate[s], level);
                        club.Squad.Add(p.Id);
                        world.Players.Add(p);
                    }

                    Manager m = MakeManager(seed, id, club.Id, level);
                    club.ManagerId = m.Id;
                    world.Managers.Add(m);
                }
            }

            world.NextPlayerId = nextPlayer;

            // Money (#198): each club's opening balance, each footballer's wage.
            foreach (Club c in world.Clubs)
            {
                c.Balance = Finance.StartBalance(c);
                foreach (Player p in world.Players)
                {
                    if (p.ClubId == c.Id) p.Wage = Finance.Wage(p, c.Division);
                }
            }

            // The board (#200): each club's objectives from its wage rank.
            Board.Objectives(world, CompetitionRules.Reference);
            return world;
        }

        private static double Draw(ulong seed, long what, int entity, int draw) => CounterRng.Uniform(seed, what, RandomStream.World, entity, draw);

        private static int Pick(ulong seed, long what, int entity, int draw, int count)
        {
            int i = (int)(Draw(seed, what, entity, draw) * count);
            return i >= count ? count - 1 : i;
        }

        private static int Clamp(double value, int min, int max)
        {
            int v = value >= 0 ? (int)(value + 0.5) : -(int)(-value + 0.5);
            return v < min ? min : v > max ? max : v;
        }

        private static Club MakeClub(ulong seed, int id, int division, double level, HashSet<string> names, HashSet<string> shorts)
        {
            string place = "", name = "";
            for (int attempt = 0; ; attempt++)
            {
                place = PlaceStarts[Pick(seed, NameDraw, id, 3 * attempt, PlaceStarts.Length)] + PlaceEnds[Pick(seed, NameDraw, id, 3 * attempt + 1, PlaceEnds.Length)];
                name = place + " " + ClubKinds[Pick(seed, NameDraw, id, 3 * attempt + 2, ClubKinds.Length)];
                if (names.Add(name)) break;
            }

            string upper = place.ToUpperInvariant();
            string shortName = upper.Substring(0, 3);
            for (int i = 3; !shorts.Add(shortName); i++)
            {
                // Another letter of the place, then a digit, until it is unique.
                shortName = i < upper.Length ? upper.Substring(0, 2) + upper[i] : upper.Substring(0, 2) + (char)('0' + (i - upper.Length) % 10);
            }

            int primary = Pick(seed, ClubDraw, id, 1, Colours.Length);
            int secondary = (primary + 1 + Pick(seed, ClubDraw, id, 2, Colours.Length - 1)) % Colours.Length;
            return new Club
            {
                Id = id,
                Name = name,
                ShortName = shortName,
                Shirt = Colours[primary],
                Shorts = Colours[secondary],
                Reputation = Clamp(level + (Draw(seed, ClubDraw, id, 3) * 2 - 1), 1, 20),
                Division = division,
            };
        }

        // Each attribute's group, by its place in PlayerAttributes.
        private static string[] BuildGroups()
        {
            var g = new string[PlayerAttributes.Count];
            void Set(string group, params string[] ids)
            {
                foreach (string id in ids) g[PlayerAttributes.Index(id)] = group;
            }

            Set("pace", "acceleration", "topSpeed", "agility");
            Set("body", "balance", "stamina", "strength");
            Set("ball", "firstTouch", "dribbling");
            Set("pass", "shortPassing", "longPassing", "crossing", "vision");
            Set("shot", "shootingPower", "finishing", "curve");
            Set("air", "heading", "jumping");
            Set("defend", "tackling", "marking", "positioning");
            Set("mind", "anticipation", "decisions", "composure");
            Set("keep", "keeperReflexes", "keeperHandling", "keeperPositioning");
            return g;
        }

        /// <summary>An attribute's group: pace, body, ball, pass, shot, air, defend, mind or keep.</summary>
        internal static string GroupOf(int attribute) => Groups[attribute];

        // How a position shifts a group from the footballer's level: its profile.
        internal static double Profile(Position position, string group) => (position, group) switch
        {
            (Position.Goalkeeper, "keep") => 4,
            (Position.Goalkeeper, "air") => 1,
            (Position.Goalkeeper, "mind") => 0,
            (Position.Goalkeeper, "body") => -1,
            (Position.Goalkeeper, "pace") => -2,
            (Position.Goalkeeper, "pass") => -3,
            (Position.Goalkeeper, _) => -4,
            (_, "keep") => -9,
            (Position.CentreBack, "defend") => 3,
            (Position.CentreBack, "air") => 3,
            (Position.CentreBack, "body") => 1,
            (Position.CentreBack, "shot") => -3,
            (Position.CentreBack, "ball") => -2,
            (Position.FullBack, "pace") => 2,
            (Position.FullBack, "defend") => 1,
            (Position.FullBack, "shot") => -2,
            (Position.DefensiveMidfield, "defend") => 2,
            (Position.DefensiveMidfield, "mind") => 1,
            (Position.DefensiveMidfield, "shot") => -1,
            (Position.CentralMidfield, "pass") => 2,
            (Position.CentralMidfield, "mind") => 1,
            (Position.WideMidfield, "pace") => 2,
            (Position.WideMidfield, "ball") => 2,
            (Position.WideMidfield, "defend") => -2,
            (Position.AttackingMidfield, "ball") => 2,
            (Position.AttackingMidfield, "pass") => 1,
            (Position.AttackingMidfield, "shot") => 1,
            (Position.AttackingMidfield, "defend") => -3,
            (Position.Striker, "shot") => 3,
            (Position.Striker, "air") => 1,
            (Position.Striker, "defend") => -3,
            _ => 0,
        };

        /// <summary>
        /// A youth for a club (#194): footballer <paramref name="id"/>, aged <paramref name="age"/>,
        /// drawn like the generator's own at <paramref name="level"/>, his potential above it.
        /// </summary>
        public static Player Youth(ulong seed, int id, int clubId, Position position, double level, int age) => MakePlayer(seed, id, clubId, position, level, age);

        private static Player MakePlayer(ulong seed, int id, int clubId, Position position, double clubLevel, int age = 0)
        {
            // His level: the club's, spread by the sum of three draws (about ±3, most near 0).
            double spread = (Draw(seed, PlayerDraw, id, 0) + Draw(seed, PlayerDraw, id, 1) + Draw(seed, PlayerDraw, id, 2) - 1.5) * 2;
            double level = clubLevel + spread;
            var p = new Player
            {
                Id = id,
                ClubId = clubId,
                FirstName = FirstNames[Pick(seed, NameDraw, 1000 + id, 0, FirstNames.Length)],
                LastName = SurnameStarts[Pick(seed, NameDraw, 1000 + id, 1, SurnameStarts.Length)] + SurnameEnds[Pick(seed, NameDraw, 1000 + id, 2, SurnameEnds.Length)],
                Age = age > 0 ? age : 17 + Pick(seed, PlayerDraw, id, 3, position == Position.Goalkeeper ? 20 : 17),
            };
            p.Positions.Add(position);
            Position? second = Neighbour(position);
            if (second.HasValue && Draw(seed, PlayerDraw, id, 4) < 0.4) p.Positions.Add(second.Value);
            double foot = Draw(seed, PlayerDraw, id, 5);
            p.Foot = foot < 0.7 ? Foot.Right : foot < 0.95 ? Foot.Left : Foot.Both;

            for (int a = 0; a < PlayerAttributes.Count; a++)
            {
                double noise = (Draw(seed, PlayerDraw, id, 10 + a) * 2 - 1) * 2;
                p.Attributes[a] = Clamp(level + Profile(position, Groups[a]) + noise, PlayerAttributes.Min, PlayerAttributes.Max);
            }

            // Potential: above his level by more the younger he is.
            double room = p.Age < 21 ? 4 : p.Age < 24 ? 2.5 : p.Age < 28 ? 1 : 0;
            p.Potential = Clamp(level + 1 + room * Draw(seed, PlayerDraw, id, 6), PlayerAttributes.Min, PlayerAttributes.Max);
            return p;
        }

        // A position a footballer can also play, if any.
        private static Position? Neighbour(Position position) => position switch
        {
            Position.CentreBack => Position.FullBack,
            Position.FullBack => Position.WideMidfield,
            Position.DefensiveMidfield => Position.CentralMidfield,
            Position.CentralMidfield => Position.AttackingMidfield,
            Position.WideMidfield => Position.AttackingMidfield,
            Position.AttackingMidfield => Position.Striker,
            Position.Striker => Position.AttackingMidfield,
            _ => null,
        };

        private static readonly Formation[] FormationChoices =
        {
            Formation.FourFourTwo, Formation.FourThreeThree, Formation.FourTwoThreeOne,
            Formation.ThreeFiveTwo, Formation.FiveThreeTwo, Formation.ThreeFourThree,
        };

        private static Manager MakeManager(ulong seed, int id, int clubId, double clubLevel)
        {
            var m = new Manager
            {
                Id = id,
                ClubId = clubId,
                FirstName = FirstNames[Pick(seed, NameDraw, 5000 + id, 0, FirstNames.Length)],
                LastName = SurnameStarts[Pick(seed, NameDraw, 5000 + id, 1, SurnameStarts.Length)] + SurnameEnds[Pick(seed, NameDraw, 5000 + id, 2, SurnameEnds.Length)],
                Age = 35 + Pick(seed, ManagerDraw, id, 0, 30),
                PreferredFormation = FormationChoices[Pick(seed, ManagerDraw, id, 1, FormationChoices.Length)],
                Temperament = 1 + Pick(seed, ManagerDraw, id, 2, 20),
                Reputation = Clamp(clubLevel + (Draw(seed, ManagerDraw, id, 3) * 2 - 1) * 2, 1, 20),
                // #180: his philosophy, on counters of its own so that no other draw moves.
                Pressing = 1 + Pick(seed, ManagerDraw, id, 20, 20),
                Depth = 1 + Pick(seed, ManagerDraw, id, 21, 20),
            };
            for (int c = 0; c < ManagerCompetences.Count; c++)
            {
                m.Competences[c] = Clamp(clubLevel + (Draw(seed, ManagerDraw, id, 10 + c) * 2 - 1) * 3, 1, 20);
            }

            return m;
        }
    }
}
