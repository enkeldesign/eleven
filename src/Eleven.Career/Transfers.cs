#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Career
{
    /// <summary>
    /// The transfer market's knobs (#203), all technical references, not tuned feel. FIFA's two
    /// registration periods, a long one between seasons and a short one mid-season; a fee that
    /// rises with ability and division, peaks in the mid-twenties and falls with a contract's
    /// years left (Carmichael and Thomas, 1993; Dobson and Gerrard, 1999; Frick, 2007).
    /// </summary>
    public sealed class TransferRules
    {
        /// <summary>A footballer's value at level 10, at his peak, in the second division, with three years left.</summary>
        public double ValueBase = 320;

        /// <summary>His value's growth a level: exp(ValueSlope × (level − 10)), as #198's wage.</summary>
        public double ValueSlope = 0.25;

        /// <summary>His value by his club's division: 1, 2, 3.</summary>
        public double[] DivisionValue = { 1.6, 1.0, 0.6 };

        /// <summary>By age: under 21 more a level of room to his potential, at most; then from 28, 30 and 32.</summary>
        public double YoungRoom = 0.15, YoungMost = 1.6, From28 = 0.75, From30 = 0.5, From32 = 0.25;

        /// <summary>By the contract's years left: one, two; three or more is 1.</summary>
        public double OneYearLeft = 0.5, TwoYearsLeft = 0.8;

        /// <summary>A club that wants him asks this much more than his value.</summary>
        public double WantedMarkup = 1.25;

        /// <summary>A buyer spends at most this share of its balance on one fee.</summary>
        public double SpendShare = 0.5;

        /// <summary>A buyer's new footballer must be this many levels above the one he replaces.</summary>
        public double UpgradeMargin = 1;

        /// <summary>
        /// A buyer weighs price against quality: of those it can afford, it takes the best level
        /// less this many levels a k of fee. 0 takes the best whatever the price.
        /// </summary>
        public double LevelsPerK = 0.001;

        /// <summary>A club buys at most this many a window, and an over-budget one lists this many.</summary>
        public int BuysPerWindow = 2, Listed = 2;

        /// <summary>A seller keeps at least this many in its squad.</summary>
        public int SquadMinimum = 20;

        /// <summary>The mid-season window opens after the league round this far through the season, 0.5 halfway.</summary>
        public double MidSeason = 0.5;

        public static TransferRules Reference => new TransferRules();
    }

    /// <summary>A transfer window (#203). Numeric values are stable and encoded as one byte.</summary>
    public enum TransferWindow : byte
    {
        PreSeason = 0,
        MidSeason = 1,
    }

    /// <summary>One transfer (#203): who, from and to which club, for what fee, in which window.</summary>
    public sealed class Transfer
    {
        public int PlayerId, From, To, Season;
        public long Fee;

        /// <summary>His value when he moved, at his old club, for the measurement of fees against values.</summary>
        public long Value;

        public TransferWindow Window;
        public string FirstName = "", LastName = "";
    }

    /// <summary>
    /// The transfer market (#203): a footballer's value, and a window's buying and selling by
    /// the AI's clubs, each fee from the buyer's balance to the seller's on the ledger's transfers
    /// line, the footballer moving with a new contract at the buyer's wage.
    /// </summary>
    public static class Transfers
    {
        // A club's shape (4-4-2): its places, each the positions that fill it and how many.
        private static readonly (Position[] Positions, int Count)[] Shape =
        {
            (new[] { Position.Goalkeeper }, 1),
            (new[] { Position.CentreBack }, 2),
            (new[] { Position.FullBack }, 2),
            (new[] { Position.DefensiveMidfield, Position.CentralMidfield, Position.AttackingMidfield }, 2),
            (new[] { Position.WideMidfield }, 2),
            (new[] { Position.Striker }, 2),
        };

        /// <summary>A footballer's value in k, by his level, age, contract and his club's division.</summary>
        public static long Value(Player p, int division, TransferRules? rules = null)
        {
            rules ??= TransferRules.Reference;
            double level = Development.Level(p);
            double age = p.Age < 21 ? Math.Min(1 + rules.YoungRoom * Math.Max(0, p.Potential - level), rules.YoungMost)
                : p.Age >= 32 ? rules.From32 : p.Age >= 30 ? rules.From30 : p.Age >= 28 ? rules.From28 : 1;
            double contract = p.ContractYears <= 1 ? rules.OneYearLeft : p.ContractYears == 2 ? rules.TwoYearsLeft : 1;
            double value = rules.ValueBase * SimMath.Exp(rules.ValueSlope * (level - 10)) * Of(rules.DivisionValue, division) * age * contract;
            return Math.Max(1, (long)Math.Round(value));
        }

        /// <summary>The fee his club asks: his value, more if it wants him (#201).</summary>
        public static long Asking(World world, Club seller, Player p, TransferRules? rules = null)
        {
            rules ??= TransferRules.Reference;
            long value = Value(p, seller.Division, rules);
            return Contracts.Wanted(world, seller, p, seller.Division) ? (long)Math.Round(value * rules.WantedMarkup) : value;
        }

        /// <summary>A club's XI by level: its best keeper and its best at each place of its shape, 4-4-2, short places filled by its best left.</summary>
        public static List<Player> Eleven(World world, Club club)
        {
            List<Player> squad = world.Players.FindAll(p => p.ClubId == club.Id && p.Positions.Count > 0);
            squad.Sort(Best);
            var xi = new List<Player>();
            int keepers = 0;
            foreach ((Position[] positions, int count) in Shape)
            {
                int taken = 0;
                foreach (Player p in squad)
                {
                    if (taken == count) break;
                    if (Array.IndexOf(positions, p.Positions[0]) < 0 || xi.Contains(p)) continue;
                    xi.Add(p);
                    taken++;
                    if (p.Positions[0] == Position.Goalkeeper) keepers++;
                }
            }

            foreach (Player p in squad)
            {
                if (xi.Count >= 11) break;
                if (xi.Contains(p) || (p.Positions[0] == Position.Goalkeeper && keepers > 0)) continue;
                xi.Add(p);
            }

            return xi;
        }

        /// <summary>Whether the matchday just played is the mid-season window's: the league round <see cref="TransferRules.MidSeason"/> through.</summary>
        public static bool MidSeasonAfter(Season season, CompetitionRules competition, TransferRules? rules = null)
        {
            rules ??= TransferRules.Reference;
            List<Matchday> calendar = SeasonCalendar.Of(competition);
            int played = season.Matchday - 1;
            if (played < 0 || played >= calendar.Count) return false;
            return calendar[played].Kind == MatchdayKind.League && calendar[played].Round == (int)Math.Round(competition.LeagueRounds * rules.MidSeason);
        }

        /// <summary>
        /// A window in <paramref name="season"/>: each club but those <paramref name="excluded"/>
        /// (the human's, until M6.12c), by division and reputation, buying for its XI's weakest
        /// place; the over-budget listing their highest-paid outside their XI. Each club's
        /// division is <paramref name="division"/>'s, or its own.
        /// </summary>
        public static List<Transfer> Window(World world, int season, TransferWindow window, IReadOnlyDictionary<int, int>? division = null, ICollection<int>? excluded = null, TransferRules? rules = null)
        {
            rules ??= TransferRules.Reference;
            int Div(Club c) => division != null && division.TryGetValue(c.Id, out int d) ? d : c.Division;
            bool Out(int club) => excluded != null && excluded.Contains(club);
            var made = new List<Transfer>();
            world.LastTransfers = made;
            var clubs = new Dictionary<int, Club>();
            foreach (Club c in world.Clubs) clubs[c.Id] = c;
            int Count(Club c, Position? at = null) => world.Players.FindAll(p => p.ClubId == c.Id && (at == null || (p.Positions.Count > 0 && p.Positions[0] == at))).Count;

            // The over-budget list their highest-paid outside their XI.
            var listed = new HashSet<int>();
            foreach (Club c in world.Clubs)
            {
                if (Out(c.Id) || Finance.WageBill(world, c) <= Finance.WageBudget(world, c, division: Div(c))) continue;
                List<Player> xi = Eleven(world, c);
                List<Player> outside = world.Players.FindAll(p => p.ClubId == c.Id && !xi.Contains(p));
                outside.Sort((a, b) => a.Wage != b.Wage ? b.Wage.CompareTo(a.Wage) : a.Id.CompareTo(b.Id));
                for (int i = 0; i < Math.Min(rules.Listed, outside.Count); i++) listed.Add(outside[i].Id);
            }

            // The buyers: by division, then reputation.
            var order = new List<Club>(world.Clubs);
            order.Sort((a, b) => Div(a) != Div(b) ? Div(a).CompareTo(Div(b)) : a.Reputation != b.Reputation ? b.Reputation.CompareTo(a.Reputation) : a.Id.CompareTo(b.Id));
            foreach (Club buyer in order)
            {
                if (Out(buyer.Id)) continue;
                int d = Div(buyer);
                for (int k = 0; k < rules.BuysPerWindow; k++)
                {
                    List<Player> xi = Eleven(world, buyer);
                    if (xi.Count == 0) break;
                    Player weakest = xi[0];
                    foreach (Player p in xi)
                    {
                        if (Development.Level(p) < Development.Level(weakest) || (Development.Level(p) == Development.Level(weakest) && p.Id < weakest.Id)) weakest = p;
                    }

                    Position at = weakest.Positions[0];
                    double need = Development.Level(weakest) + rules.UpgradeMargin;

                    // Room: a squad short of 23, or one unwanted footballer of the place in his contract's last season, let go.
                    Player? leaving = null;
                    if (Count(buyer) >= WorldGenerator.SquadSize)
                    {
                        foreach (Player p in world.Players)
                        {
                            if (p.ClubId != buyer.Id || p.ContractYears != 1 || p.Positions.Count == 0 || p.Positions[0] != at || xi.Contains(p) || Contracts.Wanted(world, buyer, p, d)) continue;
                            if (leaving == null || Development.Level(p) < Development.Level(leaving) || (Development.Level(p) == Development.Level(leaving) && p.Id < leaving.Id)) leaving = p;
                        }

                        if (leaving == null) break;
                    }

                    long bill = Finance.WageBill(world, buyer) - (leaving?.Wage ?? 0), budget = Finance.WageBudget(world, buyer, division: d);
                    double purse = buyer.Balance * rules.SpendShare;
                    Player? best = null;
                    long bestFee = 0;
                    foreach (Player p in world.Players)
                    {
                        if (p.ClubId == 0 || p.ClubId == buyer.Id || Out(p.ClubId) || p.Positions.Count == 0 || p.Positions[0] != at) continue;
                        if (Development.Level(p) < need || !clubs.TryGetValue(p.ClubId, out Club? seller)) continue;
                        bool isListed = listed.Contains(p.Id);
                        if (!isListed && Div(seller) < d) continue;
                        if (Count(seller) - 1 < rules.SquadMinimum || Count(seller, at) - 1 < (at == Position.Goalkeeper ? 2 : 1)) continue;
                        long fee = isListed ? Value(p, Div(seller), rules) : AskingAt(world, seller, p, Div(seller), rules);
                        if (fee > purse || bill + Finance.Wage(p, d) > budget) continue;
                        double score = Development.Level(p) - rules.LevelsPerK * fee, bestScore = best == null ? 0 : Development.Level(best) - rules.LevelsPerK * bestFee;
                        if (best == null || score > bestScore || (score == bestScore && (fee < bestFee || (fee == bestFee && p.Id < best.Id))))
                        {
                            best = p;
                            bestFee = fee;
                        }
                    }

                    if (best == null) break;
                    Club from = clubs[best.ClubId];
                    if (leaving != null)
                    {
                        leaving.ClubId = 0;
                        leaving.FreeSince = season;
                        leaving.ContractYears = 0;
                        Development.Leave(buyer, new HashSet<int> { leaving.Id });
                        world.LastMoves.Add(new ContractMove { PlayerId = leaving.Id, From = buyer.Id, To = 0, FirstName = leaving.FirstName, LastName = leaving.LastName });
                    }

                    long value = Value(best, Div(from), rules);
                    Finance.Book(buyer, LedgerLine.Transfers, -bestFee);
                    Finance.Book(from, LedgerLine.Transfers, bestFee);
                    Development.Leave(from, new HashSet<int> { best.Id });
                    best.ClubId = buyer.Id;
                    best.Wage = Finance.Wage(best, d);
                    best.ContractYears = Contracts.RenewYears(best.Age);
                    buyer.Squad.Add(best.Id);
                    listed.Remove(best.Id);
                    made.Add(new Transfer { PlayerId = best.Id, From = from.Id, To = buyer.Id, Season = season, Fee = bestFee, Value = value, Window = window, FirstName = best.FirstName, LastName = best.LastName });
                }
            }

            return made;
        }

        // The fee asked at the seller's division for the window.
        private static long AskingAt(World world, Club seller, Player p, int division, TransferRules rules)
        {
            long value = Value(p, division, rules);
            return Contracts.Wanted(world, seller, p, division) ? (long)Math.Round(value * rules.WantedMarkup) : value;
        }

        private static int Best(Player a, Player b)
        {
            double la = Development.Level(a), lb = Development.Level(b);
            return la != lb ? lb.CompareTo(la) : a.Id.CompareTo(b.Id);
        }

        private static double Of(double[] row, int division) => row[Math.Clamp(division, 1, row.Length) - 1];
    }
}
