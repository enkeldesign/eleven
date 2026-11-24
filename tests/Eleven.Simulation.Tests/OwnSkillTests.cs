using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #166: every outfielder's CPU skill is his own state, whoever controls him. A switch, a
    /// substitution and a quick match leave each footballer with his own; a state of schema 29
    /// migrates, each footballer taking his CPU's skill and the human's his side's, and an old
    /// recording replays as it played.
    /// </summary>
    public class OwnSkillTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private const int Seat = SquadTrials.SeatController;

        // Each outfielder at his own skill, decision intervals well apart, so whose plays whom shows.
        private static CpuSkill Own(int id) => CpuSkill.Pro.With("decisionInterval", 0.05 + 0.04 * (id % 23));

        private static bool IsKeeper(SimState s, int id) => s.Keepers.Any(k => k.PlayerId == id);

        // Five a side, live, the human on home's first outfielder, every outfielder at his own skill.
        private static SimState Live()
        {
            SimState s = Squads.WithSkills(Squads.Match(C, 0x166, 5), Own);
            s.Restart.Phase = RestartPhase.Live;
            s.Restart.PhaseTicks = 0;
            return s;
        }

        private static void Switch(Simulator sim, int to)
        {
            var events = new List<SimEvent>();
            sim.Step(new[] { new Command(Seat, sim.Tick, Vec2.Zero, switchTo: to) }, events);
            Assert.Contains(events, e => e.Kind == SimEventKind.Switched && e.Subject == to);
        }

        [Fact]
        public void A_switch_back_and_forth_leaves_every_footballer_his_own_skill()
        {
            var sim = new Simulator(C, Live());
            foreach (int to in new[] { 2, 3, 1, 4, 1 })
            {
                Switch(sim, to);
                SimState s = sim.PeekState();
                foreach (PlayerState p in s.Players.Where(p => !IsKeeper(s, p.Id)))
                {
                    Assert.Equal(Own(p.Id), s.SkillOf(p.Id));
                    Assert.Equal(Own(p.Id), p.Skill);
                }
            }
        }

        [Fact]
        public void The_footballer_the_human_leaves_decides_at_his_own_interval()
        {
            var sim = new Simulator(C, Live());
            Switch(sim, 2);
            long freed = sim.PeekState().Cpus.Single(c => c.PlayerId == 1).NextDecisionTick;
            var commands = new List<Command>();
            for (int i = 0; i < 120; i++)
            {
                long tick = sim.Tick;
                commands.Clear();
                commands.Add(new Command(Seat, tick, Vec2.Zero));
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                sim.Step(commands, new List<SimEvent>());
                long next = sim.PeekState().Cpus.Single(c => c.PlayerId == 1).NextDecisionTick;
                if (next == freed) continue;

                // His first decision since: his own interval, not the one of the teammate whose CPU he got.
                Assert.Equal(tick + Simulator.DecisionTicksFor(Own(1), C), next);
                Assert.NotEqual(Simulator.DecisionTicksFor(Own(1), C), Simulator.DecisionTicksFor(Own(2), C));
                return;
            }

            Assert.Fail("The footballer left never decided.");
        }

        [Fact]
        public void A_substitute_for_a_cpus_footballer_plays_on_his_own_skill()
        {
            SimState s = Squads.WithSkills(Squads.Match(C, 5, 10), Own);
            for (int b = 0; b < s.Bench.Length; b++) s.Bench[b].Skill = Own(s.Bench[b].Id);
            int mate = Squads.HomeOutfielder(3), sub = Squads.HomeBench(10, 0);
            var sim = new Simulator(C, s);
            var events = new List<SimEvent>();
            sim.Step(new[] { new Command(Seat, sim.Tick, Vec2.Zero, subOff: mate, subOn: sub) }, events);
            Assert.Contains(events, e => e.Kind == SimEventKind.Substituted);
            Assert.Contains(sim.PeekState().Cpus, c => c.PlayerId == sub);
            Assert.Equal(Own(sub), sim.PeekState().SkillOf(sub));
            Assert.NotEqual(Own(mate), Own(sub));
        }

        [Fact]
        public void In_a_quick_match_the_human_and_each_team_mate_play_on_their_own_skills()
        {
            World w = QuickMatch.World;
            int home = w.Clubs[1].Id, away = w.Clubs[30].Id;
            SimState s = QuickMatch.Match(C, w, home, away, 10, 0x166);
            Dictionary<int, int> lineup = CareerSquads.Lineup(w, home, away, 10);
            int seen = 0;
            foreach (PlayerState p in s.Players.Where(p => !IsKeeper(s, p.Id)))
            {
                CpuSkill own = RatingMap.Skill(w.Players.Single(x => x.Id == lineup[p.Id]));
                Assert.Equal(own, s.SkillOf(p.Id));
                seen++;
            }

            Assert.Equal(20, seen);
            int[] home10 = Enumerable.Range(0, 10).Select(Squads.HomeOutfielder).ToArray();
            Assert.True(home10.Select(id => s.SkillOf(id)).Distinct().Count() > 1, "The human's side plays on one skill.");
        }

        [Fact]
        public void A_side_a_cpu_can_play_needs_every_outfielders_skill_and_one_no_cpu_can_play_needs_none()
        {
            SimState s = Live();
            s.Players[Array.FindIndex(s.Players, p => p.Id == 1)].Skill = default;
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(C, s));

            SimState alone = OneVOne.Match(C, 4);
            alone.Players[Array.FindIndex(alone.Players, p => p.Id == OneVOne.HomePlayer)].Skill = default;
            new Simulator(C, alone);
        }

        private static string FixturePath(string name) =>
            Path.Combine(BallCurvesTests.RepositoryRoot(), "tests", "Eleven.Simulation.Tests", "Fixtures", name);

        private static CommandRecording Old() => CommandRecording.Decode(File.ReadAllBytes(FixturePath("migration-29.elrc")));

        [Fact]
        public void A_recording_made_at_schema_29_migrates_and_replays_as_it_played()
        {
            CommandRecording old = Old();
            Assert.Equal(29, Snapshot.Read(old.StartSnapshot).SchemaVersion);
            Assert.Equal(File.ReadAllText(FixturePath("migration-29.txt")), MigrationFixture.Play(old, C));
            Assert.Contains("switches 8", File.ReadAllText(FixturePath("migration-29.txt")));
        }

        [Fact]
        public void Migrated_each_footballer_takes_his_cpus_skill_and_the_humans_his_sides()
        {
            SimState s = Snapshot.Read(Old().StartSnapshot).State;
            int human = s.Players.Single(p => p.OwnerControllerId == Seat).Id;
            Assert.DoesNotContain(s.Cpus, c => c.PlayerId == human);
            foreach (PlayerState p in s.Players)
            {
                if (IsKeeper(s, p.Id)) Assert.Equal(default(CpuSkill), p.Skill);
                else Assert.Equal(MigrationFixture.SkillOf(p.Id), s.SkillOf(p.Id));
            }

            Assert.Equal(MigrationFixture.HomeSkill, s.SkillOf(human));
        }

        [Fact]
        public void A_snapshot_of_any_other_old_schema_is_still_refused()
        {
            byte[] snapshot = Old().StartSnapshot;
            foreach (int schema in new[] { 28, 1, SimulationInfo.StateSchemaVersion + 1 })
            {
                byte[] other = (byte[])snapshot.Clone();
                BitConverter.GetBytes(schema).CopyTo(other, 4);
                Assert.Throws<FormatException>(() => Snapshot.Read(other));
            }
        }
    }
}
