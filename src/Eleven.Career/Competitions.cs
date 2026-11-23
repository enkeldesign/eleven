#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Eleven.Simulation;
using Eleven.Simulation.Randomness;

namespace Eleven.Career
{
    /// <summary>A competition's id: the three divisions and the cup (#162).</summary>
    public enum CompetitionId : byte
    {
        Division1 = 1,
        Division2 = 2,
        Division3 = 3,
        Cup = 10,
    }

    /// <summary>
    /// The competitions' rules as data (#162, #80): three 12-club divisions meeting twice, 3/1/0
    /// points and 2 up, 2 down; a 36-club cup of single-leg ties settled by extra time and
    /// penalties, its final at a neutral ground.
    /// </summary>
    public sealed class CompetitionRules
    {
        public int LeagueClubs { get; set; } = 12;

        /// <summary>How often each pair of clubs meets: twice, once at each ground.</summary>
        public int Meetings { get; set; } = 2;

        public int PointsForWin { get; set; } = 3;

        public int PointsForDraw { get; set; } = 1;

        public int PointsForLoss { get; set; }

        public int Promoted { get; set; } = 2;

        public int Relegated { get; set; } = 2;

        public int CupEntrants { get; set; } = 36;

        /// <summary>A drawn cup tie goes to extra time and then penalties: the simulation's knockout.</summary>
        public bool CupExtraTimeAndPenalties { get; set; } = true;

        public bool CupNeutralFinal { get; set; } = true;

        /// <summary>Days from one league round to the next (#177).</summary>
        public int LeagueGapDays { get; set; } = 7;

        /// <summary>Days from the league round before a cup round to it (#177): midweek.</summary>
        public int CupGapDays { get; set; } = 3;

        /// <summary>A league season's rounds: each club meets each other <see cref="Meetings"/> times.</summary>
        public int LeagueRounds => (LeagueClubs - 1) * Meetings;

        /// <summary>The cup's rounds, by name.</summary>
        public static readonly ReadOnlyCollection<string> CupRounds = Array.AsReadOnly(new[]
        {
            "Preliminary round", "Round of 32", "Round of 16", "Quarter-finals", "Semi-finals", "Final",
        });

        public static CompetitionRules Reference => new CompetitionRules();
    }

    /// <summary>One match of a competition: who plays whom, where, and when in it.</summary>
    public sealed class Fixture
    {
        public int Id;
        public int Season;
        public CompetitionId Competition;

        /// <summary>The round, from 1.</summary>
        public int Round;

        /// <summary>The match's place in its round, from 0.</summary>
        public int Index;

        public int Home;
        public int Away;

        /// <summary>At a neutral ground: neither side is at home (the cup final).</summary>
        public bool Neutral;

        /// <summary>
        /// A stable id from the season, the competition, the round (below 64) and the place
        /// (below 64): the same match always has the same id, and no two matches share one.
        /// </summary>
        public static int MakeId(int season, CompetitionId competition, int round, int index) =>
            ((season * 16 + (int)competition) * 64 + round) * 64 + index;

        internal static Fixture Make(int season, CompetitionId competition, int round, int index, int home, int away, bool neutral = false) => new Fixture
        {
            Id = MakeId(season, competition, round, index),
            Season = season,
            Competition = competition,
            Round = round,
            Index = index,
            Home = home,
            Away = away,
            Neutral = neutral,
        };
    }

    /// <summary>
    /// A league's fixtures for a season (#162): a double round robin by the circle method. Every
    /// club plays once a round and meets every other once at home and once away; the second half
    /// is the first with the grounds swapped; and no club plays three in a row at home or away.
    /// The seed, the season and the division shuffle which club takes which place.
    /// </summary>
    public static class LeagueFixtures
    {
        public static List<Fixture> Generate(IReadOnlyList<int> clubs, ulong seed, int season, CompetitionId division)
        {
            int n = clubs.Count;
            if (n < 2 || n % 2 != 0) throw new ArgumentException($"A league needs an even number of clubs, not {n}.", nameof(clubs));
            int[] places = Shuffled(clubs, seed, season, (int)division);
            List<(int Home, int Away)[]> half = FirstHalf(n);
            var fixtures = new List<Fixture>(n * (n - 1));
            for (int leg = 0; leg < 2; leg++)
            {
                for (int r = 0; r < half.Count; r++)
                {
                    int round = leg * half.Count + r + 1;
                    for (int i = 0; i < half[r].Length; i++)
                    {
                        (int home, int away) = half[r][i];
                        if (leg == 1) (home, away) = (away, home);
                        fixtures.Add(Fixture.Make(season, division, round, i, places[home], places[away]));
                    }
                }
            }

            return fixtures;
        }

        // The circle method's first half, in places 0..n-1: place n-1 stays put and meets place r
        // in round r, at home in odd rounds; the others pair r+k with r-k, r+k at home for odd k.
        // That gives each place at most one pair of matches at the same ground in a half. Its
        // rounds are then turned, from the first turn that keeps every place from three at one
        // ground across the swap into the second half.
        private static List<(int, int)[]> FirstHalf(int n)
        {
            var rounds = new List<(int, int)[]>(n - 1);
            for (int r = 0; r < n - 1; r++)
            {
                var round = new (int, int)[n / 2];
                round[0] = r % 2 == 1 ? (n - 1, r) : (r, n - 1);
                for (int k = 1; k < n / 2; k++)
                {
                    int a = (r + k) % (n - 1), b = ((r - k) % (n - 1) + (n - 1)) % (n - 1);
                    round[k] = k % 2 == 1 ? (a, b) : (b, a);
                }

                rounds.Add(round);
            }

            for (int turn = 0; turn < n - 1; turn++)
            {
                var turned = new List<(int, int)[]>(n - 1);
                for (int r = 0; r < n - 1; r++) turned.Add(rounds[(r + turn) % (n - 1)]);
                if (NeverThreeAtOneGround(turned, n)) return turned;
            }

            // A league this small has no such order (four clubs); its schedule is still complete and balanced.
            return rounds;
        }

        private static bool NeverThreeAtOneGround(List<(int, int)[]> half, int n)
        {
            for (int place = 0; place < n; place++)
            {
                int run = 0;
                bool last = false;
                for (int leg = 0; leg < 2; leg++)
                {
                    foreach ((int, int)[] round in half)
                    {
                        bool home = false;
                        foreach ((int h, int _) in round) home |= h == place;
                        if (leg == 1) home = !home;
                        run = run > 0 && home == last ? run + 1 : 1;
                        last = home;
                        if (run >= 3) return false;
                    }
                }
            }

            return true;
        }

        // The clubs in schedule places, by a Fisher-Yates shuffle on the competition stream.
        internal static int[] Shuffled(IReadOnlyList<int> clubs, ulong seed, long tick, int entity)
        {
            var places = new int[clubs.Count];
            for (int i = 0; i < places.Length; i++) places[i] = clubs[i];
            for (int i = places.Length - 1; i > 0; i--)
            {
                int j = (int)(CounterRng.Uniform(seed, tick, RandomStream.Competition, entity, i) * (i + 1));
                if (j > i) j = i;
                (places[i], places[j]) = (places[j], places[i]);
            }

            return places;
        }
    }

    /// <summary>
    /// The cup (#162): its first round, each round's draw, and a tie's winner. With 36 entrants
    /// the eight lowest-ranked play a preliminary round of four ties for the round of 32; then
    /// the round of 16, the quarter-finals, the semi-finals and the final.
    /// </summary>
    public static class Cup
    {
        /// <summary>
        /// The clubs of the preliminary round and those with a bye, from the entrants ranked
        /// strongest first: as many of the lowest-ranked as it takes to leave a power of two.
        /// </summary>
        public static (List<int> Preliminary, List<int> Byes) FirstRound(IReadOnlyList<int> ranked)
        {
            int field = 1;
            while (field * 2 <= ranked.Count) field *= 2;
            int playing = 2 * (ranked.Count - field);
            var byes = new List<int>();
            var preliminary = new List<int>();
            for (int i = 0; i < ranked.Count; i++) (i < ranked.Count - playing ? byes : preliminary).Add(ranked[i]);
            return (preliminary, byes);
        }

        /// <summary>
        /// A round's draw: the clubs paired at random from the round's own seed, the first drawn
        /// at home. Two clubs are the final, at a neutral ground. Each round, season and seed
        /// draws for itself, so drawing one round never moves another.
        /// </summary>
        public static List<Fixture> Draw(IReadOnlyList<int> clubs, ulong seed, int season, int round)
        {
            if (clubs.Count < 2 || clubs.Count % 2 != 0) throw new ArgumentException($"A cup round needs an even number of clubs, not {clubs.Count}.", nameof(clubs));
            int[] drawn = LeagueFixtures.Shuffled(clubs, seed, (long)season * 64 + round, (int)CompetitionId.Cup);
            bool final = clubs.Count == 2;
            var ties = new List<Fixture>(clubs.Count / 2);
            for (int i = 0; i < drawn.Length / 2; i++) ties.Add(Fixture.Make(season, CompetitionId.Cup, round, i, drawn[2 * i], drawn[2 * i + 1], final));
            return ties;
        }

        /// <summary>
        /// The tie's winner, by goals and then by the shootout (INV-14's result). A cup tie
        /// always has one, so a drawn result is refused.
        /// </summary>
        public static int Winner(Fixture tie, MatchResult result)
        {
            int side = result.Winner;
            if (side == result.HomeTeam) return tie.Home;
            if (side == result.AwayTeam) return tie.Away;
            throw new InvalidOperationException($"Cup tie {tie.Id} ended level: a cup tie goes to extra time and penalties.");
        }
    }

    /// <summary>What a matchday plays: a league round in every division, or a cup round.</summary>
    public enum MatchdayKind : byte
    {
        League,
        Cup,
    }

    /// <summary>One matchday of the season: its kind and that competition's round, from 1.</summary>
    public struct Matchday
    {
        public MatchdayKind Kind;
        public int Round;

        /// <summary>Its day of the season (#177), from 0: league rounds a week apart, a cup round midweek.</summary>
        public int Day;
    }

    /// <summary>
    /// The season's matchdays in order (#162): the league's rounds, with the cup's spread between
    /// them, cup round c after league round ⌊c × rounds / cup rounds⌋, and the final after the
    /// last league round. With 22 and 6: after rounds 3, 7, 11, 14 and 18, and the final last.
    /// </summary>
    public static class SeasonCalendar
    {
        public static List<Matchday> Of(CompetitionRules rules)
        {
            int league = rules.LeagueRounds, cup = CompetitionRules.CupRounds.Count;
            var days = new List<Matchday>(league + cup);
            int next = 1;
            for (int r = 1; r <= league; r++)
            {
                days.Add(new Matchday { Kind = MatchdayKind.League, Round = r });
                while (next <= cup && (next == cup ? r == league : r == next * league / cup))
                {
                    days.Add(new Matchday { Kind = MatchdayKind.Cup, Round = next });
                    next++;
                }
            }

            return days;
        }
    }
}
