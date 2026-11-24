using System;
using System.Collections.Generic;
using Eleven.Simulation;

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
        public double GrowthYoung = 1.2;

        /// <summary>His club manager's coaching and development move growth by up to this share either way.</summary>
        public double CompetenceSpread = 0.3;

        /// <summary>A footballer who starts every match grows by this share faster than one who never does.</summary>
        public double StartsBonus = 0.5;

        /// <summary>From this age his physical attributes decline, by <see cref="PhysicalDecline"/> points each a season.</summary>
        public int DeclineFrom = 30;

        public double PhysicalDecline = 0.8;

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
        /// <summary>His level on the 1–20 scale (#179): his keeping for a keeper, else the mean of his outfield attributes.</summary>
        public static double Level(Player p) => 0;

        /// <summary>His growth in a season, in level points: by his age, his club manager's competences and his share of his club's matches started.</summary>
        public static double SeasonGrowth(Player p, Manager? manager, double startShare, DevelopmentRules? rules = null) => 0;

        /// <summary>
        /// One matchday's development: <paramref name="starters"/> each start counted, and every
        /// footballer short of his peak and potential grown by his season's growth over the
        /// season's <paramref name="matchdays"/>.
        /// </summary>
        public static void Matchday(World world, Season season, IReadOnlyCollection<int> starters, int matchdays, DevelopmentRules? rules = null)
        {
        }

        /// <summary>
        /// The season's turn, into season <paramref name="season"/>: every footballer a year
        /// older, the older declined, the oldest perhaps retired, and each club's squad filled
        /// again from its youth.
        /// </summary>
        public static void Rollover(World world, int season, DevelopmentRules? rules = null)
        {
        }
    }
}
