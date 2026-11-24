using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>
    /// Development and ageing's knobs (#194). Every value is a technical reference, not tuned
    /// feel. Research gives the shape: a footballer peaks in his mid-to-late twenties (Dendir
    /// 2016: about 25–27), grows fastest in his late teens, and loses his pace and stamina first,
    /// from about 30, his technique later and his reading of the game last.
    /// </summary>
    public sealed class DevelopmentRules
    {
        /// <summary>Growth is at its fastest to this age.</summary>
        public int YoungTo = 19;

        /// <summary>Growth falls to 0 at this age.</summary>
        public int PeakFrom = 24;

        /// <summary>Level points a season at the fastest, for an average club's footballer who never starts.</summary>
        public double GrowthYoung = 0.85;

        /// <summary>His club manager's coaching and development move growth by up to this share either way.</summary>
        public double CompetenceSpread = 0.3;

        /// <summary>A footballer who starts every match grows by this share faster than one who never does.</summary>
        public double StartsBonus = 0.5;

        /// <summary>From this age his physical attributes decline, by <see cref="PhysicalDecline"/> points each a season.</summary>
        public int DeclineFrom = 30;

        public double PhysicalDecline = 1.0;

        /// <summary>From this age his technical attributes decline, at half the physical rate.</summary>
        public int TechnicalFrom = 32;

        /// <summary>From this age his reading of the game declines, at a quarter of the physical rate.</summary>
        public int MentalFrom = 34;

        /// <summary>A keeper declines this many years later.</summary>
        public int KeeperLater = 3;

        /// <summary>From these ages he may retire: by <see cref="RetireChance"/> at the first, rising by as much each year.</summary>
        public int RetireFrom = 34, KeeperRetireFrom = 37;

        public double RetireChance = 0.25;

        /// <summary>A youth's age when he joins.</summary>
        public int YouthMin = 16, YouthMax = 18;

        /// <summary>A youth joins this far below his club's division's level, before his manager's development.</summary>
        public double YouthBelow = 3;

        public static DevelopmentRules Reference => new DevelopmentRules();
    }

    /// <summary>A footballer who retired at the last season's turn (#194), for its review.</summary>
    public sealed class Retired
    {
        public int Id, ClubId, Age;
        public string FirstName = "", LastName = "";
        public Position Position;
    }

    /// <summary>
    /// Development and ageing (#194): every matchday a footballer short of his peak grows toward
    /// his potential; at each season's turn every footballer ages, the older decline, the oldest
    /// may retire, and each club's youth fill its squad again. Deterministic: every draw on the
    /// development stream's counters (INV-6).
    /// </summary>
    public static class Development
    {
        // A club's picked XI: the first 11 of its picks (#179).
        private const int PickedEleven = 11;

        // What each draw is for: the key's tick, beside the season and matchday.
        private const long GrowDraw = 1, DeclineDraw = 2, RetireDraw = 3, YouthDraw = 4;

        private static readonly int[] Keeping =
        {
            PlayerAttributes.Index("keeperReflexes"), PlayerAttributes.Index("keeperHandling"), PlayerAttributes.Index("keeperPositioning"),
        };

        // The attributes that fade first, and those that fade last; the rest are technique.
        private static readonly HashSet<int> Physical = Indices("acceleration", "topSpeed", "agility", "balance", "stamina", "strength", "jumping", "keeperReflexes");
        private static readonly HashSet<int> Mental = Indices("positioning", "anticipation", "vision", "decisions", "composure", "keeperPositioning");

        private static HashSet<int> Indices(params string[] ids)
        {
            var set = new HashSet<int>();
            foreach (string id in ids) set.Add(PlayerAttributes.Index(id));
            return set;
        }

        private static bool IsKeeper(Player p) => p.Positions.Count > 0 && p.Positions[0] == Position.Goalkeeper;

        // The attributes his level counts: a keeper's keeping, an outfielder's all but it.
        private static int LevelCount(Player p) => IsKeeper(p) ? Keeping.Length : PlayerAttributes.Count - Keeping.Length;

        private static bool CountsToLevel(Player p, int attribute) => IsKeeper(p) == (Array.IndexOf(Keeping, attribute) >= 0);

        /// <summary>His level on the 1–20 scale (#179): his keeping for a keeper, else the mean of his outfield attributes.</summary>
        public static double Level(Player p)
        {
            double sum = 0;
            for (int a = 0; a < p.Attributes.Length; a++)
            {
                if (CountsToLevel(p, a)) sum += p.Attributes[a];
            }

            return sum / LevelCount(p);
        }

        /// <summary>His growth in a season, in level points: by his age, his club manager's competences and his share of his club's matches started.</summary>
        public static double SeasonGrowth(Player p, Manager? manager, double startShare, DevelopmentRules? rules = null)
        {
            rules ??= DevelopmentRules.Reference;
            if (p.Age >= rules.PeakFrom || Level(p) + 1.0 / LevelCount(p) > p.Potential + 1e-9) return 0;
            double age = p.Age <= rules.YoungTo ? 1 : (double)(rules.PeakFrom - p.Age) / (rules.PeakFrom - rules.YoungTo);
            double club = 1;
            if (manager != null)
            {
                double competence = (manager.Competences[ManagerCompetences.Ids.IndexOf("coaching")] + manager.Competences[ManagerCompetences.Ids.IndexOf("development")]) / 2.0;
                club += rules.CompetenceSpread * (competence - 10.5) / 9.5;
            }

            return rules.GrowthYoung * age * club * (1 + rules.StartsBonus * Math.Clamp(startShare, 0, 1));
        }

        /// <summary>
        /// One matchday's development: <paramref name="starters"/> each start counted, and every
        /// footballer short of his peak and potential grown by his season's growth over the
        /// season's <paramref name="matchdays"/>. His share of starts is of his club's matches
        /// with a result so far; <paramref name="clubGrowth"/> sets each club's pace (#196's training).
        /// </summary>
        public static void Matchday(World world, Season season, IReadOnlyCollection<int> starters, int matchdays, DevelopmentRules? rules = null, IReadOnlyDictionary<int, double>? clubGrowth = null)
        {
            rules ??= DevelopmentRules.Reference;
            var started = new HashSet<int>(starters);
            var played = new Dictionary<int, int>();
            foreach (Fixture f in season.Fixtures)
            {
                if (!season.Results.ContainsKey(f.Id)) continue;
                played[f.Home] = played.TryGetValue(f.Home, out int h) ? h + 1 : 1;
                played[f.Away] = played.TryGetValue(f.Away, out int a) ? a + 1 : 1;
            }

            var managers = new Dictionary<int, Manager>();
            foreach (Manager m in world.Managers) managers[m.ClubId] = m;
            // Each point's draw is keyed by the season and his points so far in it: its own, whatever the matchday.
            long tick = (long)season.Number * 8 + GrowDraw;
            foreach (Player p in world.Players)
            {
                if (started.Contains(p.Id)) p.Starts++;
                double share = played.TryGetValue(p.ClubId, out int n) && n > 0 ? (double)p.Starts / n : 0;
                double growth = SeasonGrowth(p, managers.TryGetValue(p.ClubId, out Manager? m) ? m : null, share, rules) * LevelCount(p) / Math.Max(1, matchdays);
                if (clubGrowth != null && clubGrowth.TryGetValue(p.ClubId, out double pace)) growth *= pace;
                if (growth <= 0) continue;
                p.Progress += growth;
                while (p.Progress >= 1)
                {
                    // A point only while it keeps him at or under his potential, on an attribute short of 20.
                    int a = Level(p) + 1.0 / LevelCount(p) > p.Potential + 1e-9 ? -1 : Pick(p, CounterRng.Uniform(world.Seed, tick, RandomStream.Development, p.Id, p.SeasonPoints));
                    if (a < 0)
                    {
                        p.Progress = 0;
                        break;
                    }

                    p.Attributes[a]++;
                    p.Progress -= 1;
                    p.SeasonPoints++;
                }
            }
        }

        // An attribute his level counts and short of 20, drawn by his first position's profile: an
        // outfielder's points go most where the generator gives his position most (#157); -1 for none.
        private static int Pick(Player p, double u)
        {
            Position position = p.Positions.Count > 0 ? p.Positions[0] : Position.CentralMidfield;
            double total = 0;
            for (int a = 0; a < p.Attributes.Length; a++) total += Weight(p, position, a);
            if (total <= 0) return -1;
            double at = u * total;
            int last = -1;
            for (int a = 0; a < p.Attributes.Length; a++)
            {
                double w = Weight(p, position, a);
                if (w <= 0) continue;
                last = a;
                if (at < w) return a;
                at -= w;
            }

            return last;
        }

        private static double Weight(Player p, Position position, int a) =>
            !CountsToLevel(p, a) || p.Attributes[a] >= PlayerAttributes.Max ? 0
            : IsKeeper(p) ? 1
            : Math.Max(0.25, 1 + WorldGenerator.Profile(position, WorldGenerator.GroupOf(a)) / 4);

        /// <summary>
        /// The season's turn, into season <paramref name="season"/>: the older declined by the age
        /// they played the season at, every footballer a year older, the oldest perhaps retired, and
        /// each club's squad filled again from its youth. Each footballer's season's points become
        /// last season's, and his starts start again.
        /// </summary>
        public static void Rollover(World world, int season, DevelopmentRules? rules = null)
        {
            rules ??= DevelopmentRules.Reference;
            world.LastRetired.Clear();
            long decline = (long)season * 8 + DeclineDraw, retire = (long)season * 8 + RetireDraw;
            foreach (Player p in world.Players)
            {
                int later = IsKeeper(p) ? rules.KeeperLater : 0;
                for (int a = 0; a < p.Attributes.Length; a++)
                {
                    bool physical = Physical.Contains(a), mental = Mental.Contains(a);
                    int from = (physical ? rules.DeclineFrom : mental ? rules.MentalFrom : rules.TechnicalFrom) + later;
                    if (p.Age < from) continue;
                    double rate = rules.PhysicalDecline * (physical ? 1 : mental ? 0.25 : 0.5);
                    int loss = (int)Math.Floor(rate);
                    if (CounterRng.Uniform(world.Seed, decline, RandomStream.Development, p.Id, a) < rate - loss) loss++;
                    loss = Math.Min(loss, p.Attributes[a] - PlayerAttributes.Min);
                    p.Attributes[a] -= loss;
                    p.SeasonPoints -= loss;
                }

                p.LastSeasonPoints = p.SeasonPoints;
                p.SeasonPoints = 0;
                p.Starts = 0;
                p.Age++;
            }

            // Retirement, oldest first in the world's order: at the first age a chance, rising each year.
            var gone = new HashSet<int>();
            foreach (Player p in world.Players)
            {
                int from = IsKeeper(p) ? rules.KeeperRetireFrom : rules.RetireFrom;
                if (p.Age < from) continue;
                double chance = rules.RetireChance * (p.Age - from + 1);
                if (CounterRng.Uniform(world.Seed, retire, RandomStream.Development, p.Id, 0) >= chance) continue;
                gone.Add(p.Id);
                world.LastRetired.Add(new Retired { Id = p.Id, ClubId = p.ClubId, Age = p.Age, FirstName = p.FirstName, LastName = p.LastName, Position = p.Positions.Count > 0 ? p.Positions[0] : Position.CentralMidfield });
            }

            world.LastRetired.Sort((x, y) => x.Age != y.Age ? y.Age.CompareTo(x.Age) : x.Id.CompareTo(y.Id));
            world.Players.RemoveAll(p => gone.Contains(p.Id));
            foreach (Club c in world.Clubs)
            {
                c.Squad.RemoveAll(gone.Contains);

                // A club's picks are its XI's 11, then its bench (#179). A retired XI pick would move
                // the bench's first into the XI, an outfielder in goal for a keeper: its picks go back
                // to the AI's. One on the bench, at the end, just leaves it (Codex on #195).
                bool inXi = false;
                for (int i = 0; i < Math.Min(PickedEleven, c.Picks.Count); i++) inXi |= gone.Contains(c.Picks[i]);
                if (inXi) c.Picks.Clear();
                else c.Picks.RemoveAll(gone.Contains);
            }

            Youth(world, season, rules);
        }

        // Each club's squad filled to its size from its youth: each in the position its squad lacks
        // most against the template, at its division's level less YouthBelow, by its manager's development.
        private static void Youth(World world, int season, DevelopmentRules rules)
        {
            long youth = (long)season * 8 + YouthDraw;
            foreach (Club c in world.Clubs)
            {
                Manager? m = world.Managers.Find(x => x.Id == c.ManagerId);
                double development = m == null ? 0 : (m.Competences[ManagerCompetences.Ids.IndexOf("development")] - 10.5) / 9.5;
                double level = WorldGenerator.DivisionLevel[Math.Clamp(c.Division, 1, WorldGenerator.DivisionLevel.Length) - 1] - rules.YouthBelow + development;
                while (world.Players.FindAll(x => x.ClubId == c.Id).Count < WorldGenerator.SquadSize)
                {
                    int id = world.NextPlayerId++;
                    int age = rules.YouthMin + (int)(CounterRng.Uniform(world.Seed, youth, RandomStream.Development, id, 0) * (rules.YouthMax - rules.YouthMin + 1));
                    Player y = WorldGenerator.Youth(world.Seed, id, c.Id, Lacking(world, c), level, Math.Min(age, rules.YouthMax));
                    y.Joined = season;
                    y.Wage = Finance.Wage(y, c.Division);
                    world.Players.Add(y);
                    c.Squad.Add(y.Id);
                }
            }
        }

        // The position a club's squad lacks most against the template: the first whose count is short.
        private static Position Lacking(World world, Club c)
        {
            var have = new Dictionary<Position, int>();
            foreach (Player p in world.Players)
            {
                if (p.ClubId != c.Id || p.Positions.Count == 0) continue;
                have[p.Positions[0]] = have.TryGetValue(p.Positions[0], out int n) ? n + 1 : 1;
            }

            var want = new Dictionary<Position, int>();
            foreach (Position p in WorldGenerator.SquadTemplate) want[p] = want.TryGetValue(p, out int n) ? n + 1 : 1;
            foreach (Position p in WorldGenerator.SquadTemplate)
            {
                if ((have.TryGetValue(p, out int h) ? h : 0) < want[p]) return p;
            }

            return Position.CentralMidfield;
        }
    }
}
