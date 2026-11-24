using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A manager's memory of opponents (#191): each meeting his club plays remembered from its
    /// result, and what the meetings he remembers with an opponent do to his plans for the next.
    /// Only head-to-heads, and only the result contract's facts (#80's perception boundary).
    /// </summary>
    public static class ManagerMemory
    {
        /// <summary>
        /// Both managers of fixture <paramref name="fixture"/> remember it from
        /// <paramref name="result"/>, whose home side is the fixture's: each from his own side, the
        /// latest <see cref="MemoryRules.Depth"/> of each opponent kept.
        /// </summary>
        public static void Remember(World world, Season season, Fixture fixture, MatchResult result, MemoryRules? rules = null)
        {
            MemoryRules mr = rules ?? MemoryRules.Reference;
            // Played in full, the result has its metrics (#128); a quick-sim's are none.
            bool full = result.HomeMetrics.Passes + result.AwayMetrics.Passes > 0;
            int lateHome = 0, lateAway = 0;
            foreach (ResultGoal g in result.Goals)
            {
                if (g.Half < 2 || (g.Half == 2 && g.ClockSeconds < result.HalfSeconds / 2)) continue;
                if (g.Team == result.HomeTeam) lateHome++;
                else lateAway++;
            }

            int matchday = season.Matchday + 1;
            Add(world, fixture.Home, new Meeting
            {
                Opponent = fixture.Away, Season = season.Number, Matchday = matchday,
                GoalsFor = result.HomeGoals, GoalsAgainst = result.AwayGoals, LateFor = lateHome, LateAgainst = lateAway,
                OpponentFormation = CareerManagers.FormationOf(world, fixture.Away),
                Full = full, Ppda = full ? result.HomeMetrics.Ppda : 0, OpponentPpda = full ? result.AwayMetrics.Ppda : 0, Possession = full ? result.HomeMetrics.Possession : 0,
            }, mr);
            Add(world, fixture.Away, new Meeting
            {
                Opponent = fixture.Home, Season = season.Number, Matchday = matchday,
                GoalsFor = result.AwayGoals, GoalsAgainst = result.HomeGoals, LateFor = lateAway, LateAgainst = lateHome,
                OpponentFormation = CareerManagers.FormationOf(world, fixture.Home),
                Full = full, Ppda = full ? result.AwayMetrics.Ppda : 0, OpponentPpda = full ? result.HomeMetrics.Ppda : 0, Possession = full ? result.AwayMetrics.Possession : 0,
            }, mr);
        }

        // The club's manager remembers the meeting, keeping the latest few with that opponent.
        private static void Add(World world, int club, Meeting meeting, MemoryRules rules)
        {
            Club? c = world.Clubs.Find(x => x.Id == club);
            Manager? m = c == null ? null : world.Managers.Find(x => x.Id == c.ManagerId);
            if (m == null) return;
            m.Memory.Add(meeting);
            int kept = m.Memory.FindAll(x => x.Opponent == meeting.Opponent).Count;
            for (; kept > rules.Depth; kept--) m.Memory.RemoveAt(m.Memory.FindIndex(x => x.Opponent == meeting.Opponent));
        }

        /// <summary>His meetings with <paramref name="opponent"/>, oldest first.</summary>
        public static List<Meeting> With(Manager manager, int opponent) => manager.Memory.FindAll(x => x.Opponent == opponent);

        /// <summary>
        /// What his meetings with <paramref name="opponent"/> do to his plans: his line's shift in
        /// metres, his step-up's, and how much earlier he protects, as a share of the match. All 0
        /// with no memory of them.
        /// </summary>
        public static (double Line, double StepUp, double ProtectEarlier) Shift(Manager manager, int opponent, MemoryRules? rules = null)
        {
            MemoryRules mr = rules ?? MemoryRules.Reference;
            List<Meeting> met = With(manager, opponent);
            if (met.Count == 0) return (0, 0, 0);

            // His goal difference over the meetings: beaten, deeper and protecting earlier; beating them, the mirror.
            int balance = 0, lateFor = 0, lateAgainst = 0;
            foreach (Meeting x in met)
            {
                balance += x.GoalsFor - x.GoalsAgainst;
                lateFor += x.LateFor;
                lateAgainst += x.LateAgainst;
            }

            double line = Math.Clamp(balance * mr.LineShiftPerGoal, -mr.LineShiftMax, mr.LineShiftMax);
            double earlier = Math.Clamp(-balance * mr.ProtectShiftPerGoal, -mr.ProtectShiftMax, mr.ProtectShiftMax);
            // Caught late: earlier still.
            if (lateAgainst > lateFor) earlier += mr.LateShift;

            // Out-pressed in the meetings played in full: his step-up eases toward the low block.
            double step = 0, theirs = 0, his = 0;
            int full = 0;
            foreach (Meeting x in met)
            {
                if (!x.Full) continue;
                theirs += x.OpponentPpda;
                his += x.Ppda;
                full++;
            }

            if (full > 0 && theirs / full < his / full - mr.PressMargin) step = -mr.StepUpEase;
            return (line, step, earlier);
        }
    }
}
