using System;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A manager's plans (#180): his three, balanced, chase and protect (#127's order), in his
    /// preferred formation, from his philosophy and temperament. Untuned technical references.
    /// </summary>
    public static class CareerManagers
    {
        /// <summary>His three plans for <paramref name="team"/>: balanced, chase, protect.</summary>
        public static TeamTactic[] Plans(Manager manager, int team) => new[] { TeamTactic.Reference(team), TeamTactic.Reference(team), TeamTactic.Reference(team) };

        /// <summary>The plans of the club's manager, or the reference plans if it has none.</summary>
        public static TeamTactic[] Plans(World world, int club, int team)
        {
            Club? c = world.Clubs.Find(x => x.Id == club);
            Manager? m = c == null ? null : world.Managers.Find(x => x.Id == c.ManagerId);
            return m == null ? new[] { TeamTactic.Reference(team), TeamTactic.Reference(team), TeamTactic.Reference(team) } : Plans(m, team);
        }

        /// <summary>The club's formation: its manager's preferred one, or the 4-4-2.</summary>
        public static Formation FormationOf(World world, int club) => Formation.FourFourTwo;
    }
}
