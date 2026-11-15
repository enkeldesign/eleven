// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The 1v1 world (#82): one outfielder and one keeper a side, two sides, two goals. Home's
    /// outfielder is the human's; every other footballer has an AI controller of his own. The
    /// positions are a technical start, not a kick-off: restarts come with their own sequencer.
    /// </summary>
    public static class OneVOne
    {
        public const int HomeTeam = 1, AwayTeam = 2;

        /// <summary>Player ids, ascending: home's outfielder and keeper, then away's.</summary>
        public const int HomePlayer = 1, HomeKeeper = 2, AwayPlayer = 3, AwayKeeper = 4;

        /// <summary>Each footballer's own controller (INV-10): no two share one.</summary>
        public const int HomeController = 1, HomeKeeperController = 2, AwayController = 3, AwayKeeperController = 4;

        public const int BallId = 100;

        /// <summary>m. How far off his line a keeper starts, as in practice (#69).</summary>
        public const double KeeperStandOff = 1.5;

        /// <summary>m. How far from the centre spot away's outfielder starts: Law 8's 9.15 m.</summary>
        public const double OpponentDistance = 9.15;

        /// <summary>
        /// The start: the ball on the centre spot, home's outfielder a stride behind it, away's
        /// <see cref="OpponentDistance"/> off, each facing the goal he attacks, and each keeper
        /// <see cref="KeeperStandOff"/> off the line he guards, facing the play. Home attacks
        /// <paramref name="homeAttackSign"/>; the whole start turns with it.
        ///
        /// Away's outfielder is a CPU footballer (#83) of <paramref name="awayCpu"/>'s skill, the
        /// reference tier unless given. Home's is the human's, unless <paramref name="homeCpu"/>
        /// makes him one too, for a match no one plays.
        /// </summary>
        public static SimState Start(SimConfig config, ulong seed, int homeAttackSign = 1, PlayerCapabilities? home = null, PlayerCapabilities? away = null,
            KeeperCapabilities? homeKeeper = null, KeeperCapabilities? awayKeeper = null, CpuSkill? awayCpu = null, CpuSkill? homeCpu = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (homeAttackSign != 1 && homeAttackSign != -1) throw new ArgumentOutOfRangeException(nameof(homeAttackSign), homeAttackSign, "A side attacks +1 or −1.");
            double s = homeAttackSign;
            double homeFacing = homeAttackSign > 0 ? 0 : Math.PI, awayFacing = homeAttackSign > 0 ? Math.PI : 0;
            double keeperX = config.GoalLineX - KeeperStandOff;
            return new SimState
            {
                Seed = seed,
                Ball = new BallState { Id = BallId, Position = new Vec3(0, 0, config.BallRadius) },
                Teams = new[]
                {
                    new TeamState { Id = HomeTeam, AttackSign = homeAttackSign },
                    new TeamState { Id = AwayTeam, AttackSign = -homeAttackSign },
                },
                Players = new[]
                {
                    new PlayerState { Id = HomePlayer, OwnerControllerId = HomeController, TeamId = HomeTeam, Capabilities = home ?? PlayerCapabilities.Reference, Position = new Vec2(-s * 0.6, 0), Heading = homeFacing },
                    new PlayerState { Id = HomeKeeper, OwnerControllerId = HomeKeeperController, TeamId = HomeTeam, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(-s * keeperX, 0), Heading = homeFacing },
                    new PlayerState { Id = AwayPlayer, OwnerControllerId = AwayController, TeamId = AwayTeam, Capabilities = away ?? PlayerCapabilities.Reference, Position = new Vec2(s * OpponentDistance, 0), Heading = awayFacing },
                    new PlayerState { Id = AwayKeeper, OwnerControllerId = AwayKeeperController, TeamId = AwayTeam, Capabilities = PlayerCapabilities.Reference, Position = new Vec2(s * keeperX, 0), Heading = awayFacing },
                },
                Cpus = homeCpu.HasValue
                    ? new[] { new CpuState { PlayerId = HomePlayer, Skill = homeCpu.Value }, new CpuState { PlayerId = AwayPlayer, Skill = awayCpu ?? CpuSkill.Pro } }
                    : new[] { new CpuState { PlayerId = AwayPlayer, Skill = awayCpu ?? CpuSkill.Pro } },
                Keepers = new[]
                {
                    new KeeperState { PlayerId = HomeKeeper, Capabilities = homeKeeper ?? KeeperCapabilities.Reference },
                    new KeeperState { PlayerId = AwayKeeper, Capabilities = awayKeeper ?? KeeperCapabilities.Reference },
                },
            };
        }

        /// <summary>
        /// A match's start (#85): the same world with the referee on, and a kick-off to
        /// <paramref name="kickOffTeam"/> set up and ready. Both outfielders are CPU footballers
        /// when <paramref name="bothCpu"/>, for a match no one plays.
        /// </summary>
        public static SimState KickOff(SimConfig config, ulong seed, int kickOffTeam = HomeTeam, int homeAttackSign = 1, bool bothCpu = false,
            CpuSkill? awayCpu = null, CpuSkill? homeCpu = null)
        {
            SimState s = Start(config, seed, homeAttackSign, awayCpu: awayCpu, homeCpu: bothCpu ? homeCpu ?? CpuSkill.Pro : (CpuSkill?)null);
            int sign = kickOffTeam == HomeTeam ? homeAttackSign : -homeAttackSign;
            int taker = kickOffTeam == HomeTeam ? 0 : 2, other = kickOffTeam == HomeTeam ? 2 : 0;
            s.Players[taker].Position = new Vec2(-sign * Simulator.TakerBehind, 0);
            s.Players[taker].Heading = sign > 0 ? 0 : Math.PI;
            s.Players[other].Position = new Vec2(sign * OpponentDistance, 0);
            s.Players[other].Heading = sign > 0 ? Math.PI : 0;
            s.Restart = new RestartState
            {
                On = true, Phase = RestartPhase.Ready, Kind = RestartKind.KickOff, AwardedTeam = kickOffTeam,
                PhaseTicks = config.RestartReadyTicks, TakerId = s.Players[taker].Id,
            };
            return s;
        }
    }
}
