using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #109: small injuries as bounded event risks at fouls, never continuous random damage. A foul
    /// may injure the fouled footballer, in proportion to its severity, on its own stream: a minor
    /// injury slows him for the rest of the match, a serious one takes him off. Only at eleven a side.
    /// </summary>
    public class InjuryTests
    {
        // The challenge's own mechanics, shielding off (#97); no cards, so only injuries take anyone off.
        private static readonly SimConfig Base = SpikeScenario.Config().With("shieldSpeed", 0).With("cautionFrom", 2).With("sendOffFrom", 2);

        private const int Side = 10;
        private static readonly int Carrier = Squads.HomeOutfielder(0);

        // As CardTests: eleven a side in play, home attacking +x, the carrier at `at` with the ball,
        // an away defender a step from the end of a standing challenge from just behind him.
        private static SimState Fouling(SimConfig config, ulong seed, int perSide = Side)
        {
            SimState s = Squads.Match(config, seed, perSide);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.Kind = RestartKind.None;
            s.Restart.TakerId = 0;
            s.Restart.LastTouchedTeam = Squads.HomeTeam;
            for (int i = 0; i < s.Players.Length; i++)
            {
                int id = s.Players[i].Id;
                bool keeper = s.Keepers.Any(k => k.PlayerId == id);
                bool home = s.Players[i].TeamId == Squads.HomeTeam;
                s.Players[i].Position = keeper ? new Vec2(home ? -51 : 51, 0) : new Vec2(home ? -45 : -30, -30 + 2.5 * i);
                s.Players[i].Velocity = Vec2.Zero;
            }

            var at = new Vec2(-10, 0);
            int c = Index(s, Carrier), f = Index(s, Squads.AwayOutfielder(perSide, 0));
            s.Players[c].Position = at;
            s.Ball.Position = new Vec3(at.X + 0.7, at.Y, config.BallRadius);
            s.Ball.LastPlayedBy = Carrier;
            s.Players[f].Position = at + new Vec2(-0.3, 0);
            s.Players[f].Velocity = new Vec2(5, 0);
            s.Players[f].Challenge = ChallengePhase.Commit;
            s.Players[f].ChallengeTicks = 1;
            s.Players[f].ChallengeLine = new Vec2(1, 0);
            return s;
        }

        private static int Index(SimState s, int id) => Array.FindIndex(s.Players, p => p.Id == id);

        // The foul on 40 seeds: each outcome's state and events.
        private static List<(SimState State, List<SimEvent> Events)> Fouls(SimConfig config, int perSide = Side)
        {
            var all = new List<(SimState, List<SimEvent>)>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var sim = new Simulator(config, Fouling(config, seed, perSide));
                var events = new List<SimEvent>();
                sim.Step(Array.Empty<Command>(), events);
                Assert.Contains(events, e => e.Kind == SimEventKind.Foul);
                all.Add((sim.PeekState(), events));
            }

            return all;
        }

        [Fact]
        public void With_no_risk_no_foul_injures()
        {
            Assert.All(Fouls(Base.With("injuryRisk", 0)), f => Assert.DoesNotContain(f.Events, e => e.Kind == SimEventKind.Injured));
        }

        [Fact]
        public void A_foul_may_injure_the_fouled_footballer_and_a_minor_injury_keeps_him_on()
        {
            var fouls = Fouls(Base.With("injuryRisk", 1).With("injurySerious", 0));
            var hurt = fouls.Where(f => f.Events.Any(e => e.Kind == SimEventKind.Injured)).ToList();
            Assert.InRange(hurt.Count, 1, fouls.Count - 1);
            Assert.All(hurt, f =>
            {
                SimEvent e = Assert.Single(f.Events, x => x.Kind == SimEventKind.Injured);
                Assert.Equal((Carrier, (int)InjuryLevel.Minor), (e.Subject, e.Other));
                PlayerState p = f.State.Players[Index(f.State, Carrier)];
                Assert.Equal(InjuryLevel.Minor, p.Injury);
                Assert.False(p.Off);
            });
        }

        [Fact]
        public void A_serious_injury_takes_him_off()
        {
            var hurt = Fouls(Base.With("injuryRisk", 1).With("injurySerious", 1)).Where(f => f.Events.Any(e => e.Kind == SimEventKind.Injured)).ToList();
            Assert.NotEmpty(hurt);
            Assert.All(hurt, f =>
            {
                PlayerState p = f.State.Players[Index(f.State, Carrier)];
                Assert.Equal(InjuryLevel.Serious, p.Injury);
                Assert.True(p.Off);
                Assert.True(Math.Abs(p.Position.Y) > Base.TouchlineY + 5);
                Assert.Equal(0, p.Slot);
                Assert.False(p.SentOff);
                // The human's controller has taken a teammate.
                PlayerState now = f.State.Players.Single(x => x.OwnerControllerId == SquadTrials.SeatController);
                Assert.Equal(Squads.HomeTeam, now.TeamId);
                Assert.False(now.Off);
            });
        }

        [Fact]
        public void A_harder_foul_injures_more_often()
        {
            // The same foul at a walk and at full pace: the faster one is more severe.
            int Hurt(double pace)
            {
                int n = 0;
                for (ulong seed = 1; seed <= 60; seed++)
                {
                    SimConfig c = Base.With("injuryRisk", 0.5);
                    SimState s = Fouling(c, seed);
                    s.Players[Index(s, Squads.AwayOutfielder(Side, 0))].Velocity = new Vec2(pace, 0);
                    var sim = new Simulator(c, s);
                    var events = new List<SimEvent>();
                    sim.Step(Array.Empty<Command>(), events);
                    if (events.Any(e => e.Kind == SimEventKind.Injured)) n++;
                }

                return n;
            }

            Assert.True(Hurt(7) > Hurt(0.5), $"at full pace {Hurt(7)}, at a walk {Hurt(0.5)}");
        }

        [Fact]
        public void Small_sides_play_without_injuries()
        {
            Assert.All(Fouls(Base.With("injuryRisk", 1), perSide: 5), f => Assert.DoesNotContain(f.Events, e => e.Kind == SimEventKind.Injured));
        }

        [Fact]
        public void Hurt_and_playing_on_he_runs_at_injuredSpeed_of_his_top_speed()
        {
            double TopSpeed(InjuryLevel injury)
            {
                SimState s = Squads.Match(Base, 1, Side);
                s.Restart.Phase = RestartPhase.Live;
                s.Restart.Kind = RestartKind.None;
                s.Restart.TakerId = 0;
                int me = Index(s, Carrier);
                s.Players[me].Position = new Vec2(-40, 20);
                s.Players[me].Injury = injury;
                s.Ball.Position = new Vec3(30, -20, Base.BallRadius);
                var sim = new Simulator(Base, s);
                for (int t = 0; t < 3 * Base.TickRate; t++) sim.Step(new[] { new Command(SquadTrials.SeatController, sim.Tick, new Vec2(1, 0)) }, new List<SimEvent>());
                return Math.Sqrt(sim.PeekState().Players[me].Velocity.LengthSquared);
            }

            Assert.Equal(Base.InjuredSpeed, TopSpeed(InjuryLevel.Minor) / TopSpeed(InjuryLevel.None), 2);
        }

        [Fact]
        public void The_result_counts_each_sides_injuries()
        {
            SimState s = Squads.Match(Base, 1, Side);
            var recorder = new MatchRecorder();
            recorder.Observe(Base, s, new[]
            {
                new SimEvent(10, SimEventKind.Injured, Carrier, other: (int)InjuryLevel.Minor),
                new SimEvent(20, SimEventKind.Injured, Squads.AwayOutfielder(Side, 2), other: (int)InjuryLevel.Serious),
                new SimEvent(30, SimEventKind.Injured, Squads.AwayOutfielder(Side, 3), other: (int)InjuryLevel.Minor),
                new SimEvent(40, SimEventKind.FullTime, 0),
            });
            Assert.NotNull(recorder.Result);
            Assert.Equal((1, 2), (recorder.Result!.HomeInjuries, recorder.Result.AwayInjuries));
            Assert.Contains("injuries=1-2\n", recorder.Result.ToText());
        }

        [Fact]
        public void An_injury_survives_a_snapshot()
        {
            SimConfig c = Base.With("injuryRisk", 1).With("injurySerious", 0);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var sim = new Simulator(c, Fouling(c, seed));
                var events = new List<SimEvent>();
                sim.Step(Array.Empty<Command>(), events);
                if (!events.Any(e => e.Kind == SimEventKind.Injured)) continue;
                Simulator back = Snapshot.Restore(Snapshot.Capture(sim), c);
                Assert.Equal(InjuryLevel.Minor, back.PeekState().Players[Index(back.PeekState(), Carrier)].Injury);
                Assert.Equal(sim.StateHash(), back.StateHash());
                return;
            }

            Assert.Fail("no foul injured in 40 seeds");
        }
    }
}
