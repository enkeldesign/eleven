using System;
using System.Collections.Generic;
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
        public static TeamTactic[] Plans(Manager manager, int team)
        {
            Formation f = manager.PreferredFormation;
            TeamTactic balanced = TeamTactic.Reference(team, f);
            if (manager.Pressing != 0 || manager.Depth != 0)
            {
                // His line from −6 m to +6 m by his depth. Below the middle of his pressing his press
                // draws back to #126's low block's; above it his shape steps up toward the high press's.
                double t = (Math.Clamp(manager.Pressing, 1, 20) - 1) / 19.0, d = (Math.Clamp(manager.Depth, 1, 20) - 1) / 19.0;
                balanced.Line = 12 * (d - 0.5);
                balanced.Press = t < 0.5 ? TacticTrials.LowBlock.Press + (TeamTactic.PressMax - TacticTrials.LowBlock.Press) * 2 * t : TeamTactic.PressMax;
                balanced.StepUp = t < 0.5 ? 6 : 6 + (TacticTrials.HighPress.StepUp - 6) * (2 * t - 1);
            }

            // His temperament, 0 cautious to 1 bold: the bold chase all the way to the high press, the cautious half way; the cautious protect all the way to the low block.
            double bold = (Math.Clamp(manager.Temperament, 1, 20) - 1) / 19.0;
            TeamTactic chase = Toward(balanced, TacticTrials.HighPress, 0.5 + 0.5 * bold), protect = Toward(balanced, TacticTrials.LowBlock, 1 - 0.5 * bold);
            return new[] { balanced, chase, protect };
        }

        // The plan moved a share of the way to another's instructions, in its own formation and side.
        private static TeamTactic Toward(TeamTactic from, TeamTactic to, double share) => new TeamTactic
        {
            TeamId = from.TeamId,
            Formation = from.Formation,
            StepUp = Math.Clamp(from.StepUp + (to.StepUp - from.StepUp) * share, 0, TeamTactic.StepUpMax),
            Line = Math.Clamp(from.Line + (to.Line - from.Line) * share, -TeamTactic.LineMax, TeamTactic.LineMax),
            Width = Math.Clamp(from.Width + (to.Width - from.Width) * share, TeamTactic.WidthMin, TeamTactic.WidthMax),
            Press = Math.Clamp(from.Press + (to.Press - from.Press) * share, TeamTactic.PressMin, TeamTactic.PressMax),
        };

        /// <summary>
        /// His three plans for meeting <paramref name="opponent"/> (#191): his own, shifted by what
        /// he remembers of them (<see cref="ManagerMemory.Shift"/>). No memory of them, his own.
        /// </summary>
        public static TeamTactic[] Plans(Manager manager, int team, int opponent, MemoryRules? rules = null)
        {
            TeamTactic[] plans = Plans(manager, team);
            (double line, double step, _) = ManagerMemory.Shift(manager, opponent, rules);
            if (line == 0 && step == 0) return plans;
            for (int i = 0; i < plans.Length; i++)
            {
                plans[i].Line = Math.Clamp(plans[i].Line + line, -TeamTactic.LineMax, TeamTactic.LineMax);
                plans[i].StepUp = Math.Clamp(plans[i].StepUp + step, 0, TeamTactic.StepUpMax);
            }

            return plans;
        }

        /// <summary>The plans of the club's manager for meeting <paramref name="opponent"/> (0: anyone), or the reference plans if it has none.</summary>
        public static TeamTactic[] Plans(World world, int club, int team, int opponent = 0)
        {
            Club? c = world.Clubs.Find(x => x.Id == club);
            Manager? m = c == null ? null : world.Managers.Find(x => x.Id == c.ManagerId);
            return m == null ? new[] { TeamTactic.Reference(team), TeamTactic.Reference(team), TeamTactic.Reference(team) } : Plans(m, team, opponent);
        }

        /// <summary>His profile in a match (#182): managing tactics, by his temperament and competences.</summary>
        public static ManagerProfile Profile(Manager manager, SimConfig config)
        {
            // #127's presets are the anchors: cautious chases and protects from 0.67, bold chases
            // from 0.5 and protects from 0.83. The able read the moment nearer; the adaptable change earlier.
            double bold = Share(manager.Temperament), tactics = Share(Competence(manager, "tactics")), adapt = Share(Competence(manager, "adaptability"));
            return new ManagerProfile(
                tactics: true,
                chaseFrom: (1 - bold) * 0.67 + bold * 0.5,
                chaseMargin: 1,
                protectFrom: (1 - bold) * 0.67 + bold * 0.83,
                spread: (1 - tactics) * 0.1 + tactics * 0.02,
                subAfter: (1 - adapt) * 0.75 + adapt * 0.6,
                subFatigue: (1 - adapt) * 0.25 + adapt * 0.15);
        }

        private static double Share(int value) => (Math.Clamp(value, 1, 20) - 1) / 19.0;

        private static int Competence(Manager m, string id) => m.Competences[ManagerCompetences.Ids.IndexOf(id)];

        /// <summary>Both sides' managers' profiles for a career match, by team id: a club without a manager has the config's.</summary>
        /// <summary>His profile for meeting <paramref name="opponent"/> (#191): his own, protecting earlier by what he remembers of them.</summary>
        public static ManagerProfile Profile(Manager manager, SimConfig config, int opponent, MemoryRules? rules = null)
        {
            ManagerProfile p = Profile(manager, config);
            (_, _, double earlier) = ManagerMemory.Shift(manager, opponent, rules);
            if (earlier == 0) return p;
            return new ManagerProfile(p.Tactics, p.ChaseFrom, p.ChaseMargin, Math.Clamp(p.ProtectFrom - earlier, 0, 1), p.Spread, p.SubAfter, p.SubFatigue);
        }

        public static Dictionary<int, ManagerProfile> Profiles(World world, int homeClub, int awayClub, SimConfig config)
        {
            ManagerProfile Of(int club, int opponent)
            {
                Club? c = world.Clubs.Find(x => x.Id == club);
                Manager? m = c == null ? null : world.Managers.Find(x => x.Id == c.ManagerId);
                return m == null ? ManagerProfile.From(config) : Profile(m, config, opponent);
            }

            return new Dictionary<int, ManagerProfile> { [Squads.HomeTeam] = Of(homeClub, awayClub), [Squads.AwayTeam] = Of(awayClub, homeClub) };
        }

        /// <summary>The club's formation: its manager's preferred one, or the 4-4-2.</summary>
        public static Formation FormationOf(World world, int club)
        {
            Club? c = world.Clubs.Find(x => x.Id == club);
            Manager? m = c == null ? null : world.Managers.Find(x => x.Id == c.ManagerId);
            return m?.PreferredFormation ?? Formation.FourFourTwo;
        }
    }
}
