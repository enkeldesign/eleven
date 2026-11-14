using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eleven.Simulation.AI;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The CPU outfielder (#83): he sees an observation, his brain asks for a plan, the simulation
    /// times it, and his body plays through the commands any footballer's does.
    /// </summary>
    public class CpuTests
    {
        private static readonly SimConfig C = SpikeScenario.Config();

        private const int Home = OneVOne.HomePlayer, Away = OneVOne.AwayPlayer;

        private static PlayerState P(Simulator sim, int id) => sim.PeekState().Players.First(p => p.Id == id);

        private static CpuState Cpu(Simulator sim, int id) => sim.PeekState().Cpus.First(c => c.PlayerId == id);

        // Steps with every brain running, and whatever else is given for the human.
        private static List<SimEvent> Run(Simulator sim, int ticks, Func<long, Command?>? human = null, List<Command[]>? recorded = null, List<ulong>? hashes = null, List<SimState>? states = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var commands = new List<Command>();
                Command? h = human?.Invoke(sim.Tick);
                if (h.HasValue) commands.Add(h.Value);
                KeeperControllers.Append(sim, commands);
                CpuControllers.Append(sim, commands);
                recorded?.Add(commands.ToArray());
                sim.Step(commands, events);
                hashes?.Add(sim.StateHash());
                states?.Add(sim.PeekState().Clone());
            }

            return events;
        }

        // The 1v1 start with the human's outfielder out of the way, far up his own touchline.
        private static SimState Alone(ulong seed = 5, CpuSkill? skill = null, int homeAttackSign = 1)
        {
            SimState s = OneVOne.Start(C, seed, homeAttackSign, awayCpu: skill ?? CpuSkill.Pro);
            s.Players[0].Position = new Vec2(-homeAttackSign * 40, homeAttackSign * 30);
            return s;
        }

        // ---- The boundary (INV-9) ---------------------------------------------------------------

        [Fact]
        public void The_observation_holds_no_state_no_command_and_no_seed()
        {
            var banned = new[] { typeof(SimState), typeof(Simulator), typeof(Command), typeof(PlayerState), typeof(BallState), typeof(CpuState), typeof(KeeperState), typeof(KickIntent), typeof(ulong) };
            foreach (Type t in new[] { typeof(OutfieldObservation), typeof(SeenPlayer) })
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    Type held = f.FieldType.IsArray ? f.FieldType.GetElementType()! : f.FieldType;
                    Assert.DoesNotContain(held, banned);
                }
            }

            // The brain takes the observation and the config, nothing else, and is in the namespace
            // the analyzer keeps the state out of (ELEVEN003).
            MethodInfo decide = typeof(OutfieldBrain).GetMethod(nameof(OutfieldBrain.Decide))!;
            Assert.Equal(new[] { typeof(OutfieldObservation), typeof(SimConfig) }, decide.GetParameters().Select(p => p.ParameterType));
            Assert.Equal("Eleven.Simulation.AI", typeof(OutfieldBrain).Namespace);
            Assert.True(typeof(OutfieldObservation).IsSealed);
            Assert.Empty(typeof(OutfieldObservation).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        }

        [Fact]
        public void He_places_what_he_sees_within_his_error_and_sees_no_one_beyond_his_range()
        {
            SimState s = OneVOne.Start(C, seed: 9, awayCpu: CpuSkill.Apprentice);
            s.Ball.Position = new Vec3(-20, 12, C.BallRadius);
            Vec2 me = s.Players[2].Position;
            OutfieldObservation seen = Perception.OfOutfielder(s, 0);

            // Away attacks −x, so his frame is the world turned.
            Assert.Equal(new Vec2(-me.X, -me.Y), seen.Position);
            Vec2 ball = new Vec2(-seen.BallPosition.X, -seen.BallPosition.Y);
            double distance = Math.Sqrt((s.Ball.Position.Horizontal - me).LengthSquared);
            double off = Math.Sqrt((ball - s.Ball.Position.Horizontal).LengthSquared);
            Assert.InRange(off, 1e-9, CpuSkill.Apprentice.PerceptionError * distance * Math.Sqrt(2));

            // The same state, the same observation; another tick, another error.
            Assert.Equal(seen.BallPosition, Perception.OfOutfielder(s, 0).BallPosition);
            s.Tick = 1;
            Assert.NotEqual(seen.BallPosition, Perception.OfOutfielder(s, 0).BallPosition);

            // Apprentice sees 30 m: home's outfielder, 9.75 m off, and not the keepers, 42 and 60 m off.
            Assert.Single(seen.Others);
            Assert.False(seen.Others[0].Teammate || seen.Others[0].Keeper);

            // With no error he places everything where it is, and a longer sight shows the keepers.
            s.Cpus[0].Skill = CpuSkill.Elite.With("perceptionError", 0);
            OutfieldObservation exact = Perception.OfOutfielder(s, 0);
            Assert.Equal(new Vec3(20, -12, C.BallRadius), exact.BallPosition);
            Assert.Equal(3, exact.Others.Count);
            Assert.Single(exact.Others, o => o.Teammate && o.Keeper);
            Assert.True(exact.BothGoals);
        }

        // ---- The timers are the simulation's (INV-12) -----------------------------------------------

        [Fact]
        public void A_plan_waits_out_his_reaction_and_he_decides_again_only_after_his_interval()
        {
            CpuSkill skill = CpuSkill.Pro;
            int interval = Simulator.DecisionTicksFor(skill, C), reaction = Simulator.ReactionTicksFor(skill, C);
            Assert.Equal((9, 9), (interval, reaction));

            var sim = new Simulator(C, Alone());
            var recorded = new List<Command[]>();
            Run(sim, 40, recorded: recorded);

            // He decides at tick 0, then every interval; each plan acts a reaction later.
            long[] decided = recorded.SelectMany(step => step).Where(c => c.ControllerId == OneVOne.AwayController && c.Plan.HasValue).Select(c => c.Tick).ToArray();
            Assert.Equal(new long[] { 0, 9, 18, 27, 36 }, decided);
            CpuState cpu = Cpu(sim, Away);
            Assert.Equal(45, cpu.NextDecisionTick);
            Assert.Equal((27L, 36L), (cpu.Plan.ObservedTick, cpu.Plan.ActsTick));
            Assert.True(cpu.HasNext);
            Assert.Equal((36L, 45L), (cpu.Next.ObservedTick, cpu.Next.ActsTick));

            // Until his first plan acts he stands: no stick before tick 9.
            Assert.All(recorded.Take(9).SelectMany(step => step).Where(c => c.ControllerId == OneVOne.AwayController), c => Assert.Equal(Vec2.Zero, c.Move));
            Assert.Contains(recorded.Skip(9).First(), c => c.ControllerId == OneVOne.AwayController && c.Move.LengthSquared > 0.5);
        }

        [Fact]
        public void A_plan_before_he_may_decide_or_from_a_footballer_who_is_no_cpu_is_refused()
        {
            var sim = new Simulator(C, Alone());
            var events = new List<SimEvent>();
            var plan = new CpuPlan(CpuMode.Carry, new Vec2(1, 0));
            sim.Step(new[] { new Command(OneVOne.AwayController, 0, Vec2.Zero, plan: plan) }, events);
            Assert.Empty(events);
            Assert.True(Cpu(sim, Away).HasNext);

            // Too soon: the whole command is rejected, and nothing of his changes.
            ulong before = sim.StateHash();
            CpuState was = Cpu(sim, Away);
            sim.Step(new[] { new Command(OneVOne.AwayController, 1, new Vec2(1, 0), plan: new CpuPlan(CpuMode.Shoot, new Vec2(0, 1))) }, events);
            Assert.Equal(CommandRejection.PlanRefused, Assert.Single(events).Rejection);
            Assert.Equal(was.Next.Mode, Cpu(sim, Away).Next.Mode);
            Assert.Equal(was.NextDecisionTick, Cpu(sim, Away).NextDecisionTick);
            Assert.Equal(Vec2.Zero, P(sim, Away).Velocity);
            Assert.NotEqual(before, sim.StateHash());

            // The human's controller has no CPU footballer; neither has a keeper's.
            events.Clear();
            sim.Step(new[] { new Command(OneVOne.HomeController, 2, Vec2.Zero, plan: plan), new Command(OneVOne.AwayKeeperController, 2, Vec2.Zero, plan: plan) }, events);
            Assert.Equal(new[] { CommandRejection.PlanRefused, CommandRejection.PlanRefused }, events.Select(e => e.Rejection));

            // A plan with a value that isn't finite is invalid, as any command's is.
            events.Clear();
            sim.Step(new[] { new Command(OneVOne.AwayController, 3, Vec2.Zero, plan: new CpuPlan(CpuMode.Carry, new Vec2(double.NaN, 0))) }, events);
            Assert.Equal(CommandRejection.Invalid, Assert.Single(events).Rejection);
        }

        [Fact]
        public void A_plan_moves_no_one_by_itself()
        {
            // The plan is kept and timed; only an ordinary stick moves him.
            var sim = new Simulator(C, Alone());
            sim.Step(new[] { new Command(OneVOne.AwayController, 0, Vec2.Zero, plan: new CpuPlan(CpuMode.Carry, new Vec2(-1, 0))) }, new List<SimEvent>());
            for (int i = 1; i < 30; i++) sim.Step(Array.Empty<Command>(), new List<SimEvent>());
            Assert.Equal(CpuMode.Carry, Cpu(sim, Away).Plan.Mode);
            Assert.Equal(new Vec2(OneVOne.OpponentDistance, 0), P(sim, Away).Position);
        }

        [Fact]
        public void The_cpu_state_round_trips_and_a_bad_one_is_refused()
        {
            var sim = new Simulator(C, Alone());
            Run(sim, 25);
            Simulator restored = Snapshot.Restore(Snapshot.Capture(sim), C);
            Assert.Equal(sim.StateHash(), restored.StateHash());
            CpuState a = Cpu(sim, Away), b = Cpu(restored, Away);
            Assert.Equal((a.NextDecisionTick, a.Plan.Mode, a.Plan.Move, a.Plan.ActsTick, a.HasNext, a.Next.ObservedTick, a.Skill.DecisionInterval),
                (b.NextDecisionTick, b.Plan.Mode, b.Plan.Move, b.Plan.ActsTick, b.HasNext, b.Next.ObservedTick, b.Skill.DecisionInterval));

            SimState With(Action<SimState> change)
            {
                SimState s = Alone();
                change(s);
                return s;
            }

            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Cpus[0].PlayerId = 99)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Cpus[0].PlayerId = OneVOne.AwayKeeper)));
            Assert.Throws<ArgumentException>(() => new Simulator(C, With(s => s.Cpus = new[] { s.Cpus[0], s.Cpus[0] })));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(C, With(s => s.Cpus[0].Skill.DecisionInterval = 0)));
            Assert.ThrowsAny<Exception>(() => new Simulator(C, With(s => s.Cpus[0].Skill.PerceptionError = double.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulator(C, With(s => s.Cpus[0].Plan.Move = new Vec2(3, 0))));
        }

        // ---- He plays -----------------------------------------------------------------------------

        [Fact]
        public void He_goes_to_a_loose_ball_carries_it_at_the_goal_he_attacks_and_shoots()
        {
            var sim = new Simulator(C, Alone());
            var recorded = new List<Command[]>();
            var states = new List<SimState>();
            List<SimEvent> events = Run(sim, 15 * 60, recorded: recorded, states: states);

            // Away attacks −x. He touches the ball, takes it toward −x and kicks it at that goal.
            Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Subject == Away);
            SimEvent firstTouch = events.First(e => e.Kind == SimEventKind.Touched && e.Subject == Away);
            Assert.InRange(firstTouch.Tick, 1, 3 * 60);
            Assert.Contains(events, e => e.Kind == SimEventKind.Kicked && e.Subject == Away);
            SimEvent kick = events.First(e => e.Kind == SimEventKind.Kicked && e.Subject == Away);
            SimState atKick = states[(int)kick.Tick];
            Assert.InRange(atKick.Ball.Position.X, -C.GoalLineX, -C.GoalLineX + 18);
            Assert.True(atKick.Ball.Velocity.X < -10, $"the shot goes {atKick.Ball.Velocity.X} m/s along x");
            Assert.True(events.Count(e => e.Kind == SimEventKind.Touched && e.Subject == Away && e.Tick < kick.Tick) >= 5);

            // His modes, in the order he first took them.
            CpuMode[] modes = recorded.SelectMany(step => step).Where(c => c.Plan.HasValue).Select(c => c.Plan!.Value.Mode).Distinct().ToArray();
            Assert.Equal(new[] { CpuMode.SeekBall, CpuMode.Approach, CpuMode.Carry, CpuMode.Shoot }, modes.Take(4));

            // It ends at the goal: scored, saved or wide.
            Assert.Contains(events, e => e.Kind == SimEventKind.Goal || e.Kind == SimEventKind.KeeperHeld || e.Kind == SimEventKind.KeeperParried || e.Kind == SimEventKind.GoalLineCrossed || e.Kind == SimEventKind.HitFrame);
        }

        [Fact]
        public void Out_of_possession_he_gets_between_the_ball_and_his_own_goal()
        {
            // The human stands on the ball, well inside away's half and off to one side; away starts up the pitch.
            SimState s = OneVOne.Start(C, seed: 5);
            s.Ball.Position = new Vec3(25, 12, C.BallRadius);
            s.Players[0].Position = new Vec2(24.5, 12);
            s.Players[2].Position = new Vec2(-5, -10);
            var sim = new Simulator(C, s);
            var recorded = new List<Command[]>();
            Run(sim, 6 * 60, recorded: recorded);

            // Away's own goal is at +x. He ends goal-side of the ball, near the line from it to his goal.
            Vec2 ball = sim.PeekState().Ball.Position.Horizontal, he = P(sim, Away).Position, goal = new Vec2(C.GoalLineX, 0);
            Assert.True(he.X > ball.X, $"he is at {he.X}, the ball at {ball.X}");
            Vec2 line = goal - ball, to = he - ball;
            double along = (to.X * line.X + to.Y * line.Y) / Math.Sqrt(line.LengthSquared);
            double across = Math.Abs(to.X * line.Y - to.Y * line.X) / Math.Sqrt(line.LengthSquared);
            // Within pressing distance he goes for the ball itself, from the goal side.
            Assert.InRange(along, 0, 6);
            Assert.InRange(across, 0, 1.5);
            Assert.Contains(recorded.SelectMany(step => step), c => c.Plan.HasValue && c.Plan.Value.Mode == CpuMode.Defend);
        }

        [Fact]
        public void While_a_keeper_holds_the_ball_he_takes_up_a_position_and_waits()
        {
            SimState s = Alone();
            s.Ball.HeldBy = OneVOne.HomeKeeper;
            s.Ball.Position = new Vec3(s.Players[1].Position.X, 0, 1);
            var sim = new Simulator(C, s);
            var recorded = new List<Command[]>();
            Run(sim, 8 * 60, recorded: recorded);
            Assert.All(recorded.SelectMany(step => step).Where(c => c.Plan.HasValue), c => Assert.Equal(CpuMode.Recover, c.Plan!.Value.Mode));

            // Their keeper's ball: he drops into his own half, which for away is +x, and stops there.
            Assert.InRange(P(sim, Away).Position.X, 11, 13);
            Assert.True(P(sim, Away).Velocity.LengthSquared < 0.05);
        }

        // ---- The same body and the same commands as a human's ---------------------------------------------

        [Fact]
        public void His_commands_sent_by_a_human_move_the_same_body_the_same_way()
        {
            var sim = new Simulator(C, Alone());
            var recorded = new List<Command[]>();
            var states = new List<SimState>();
            Run(sim, 10 * 60, recorded: recorded, states: states);

            // The same start with no CPU footballer at all, and his recorded stick and kicks sent as a
            // human's would be: without the plans.
            SimState plain = Alone();
            plain.Cpus = Array.Empty<CpuState>();
            var human = new Simulator(C, plain);
            for (int i = 0; i < recorded.Count; i++)
            {
                Command[] stripped = recorded[i].Select(c => new Command(c.ControllerId, c.Tick, c.Move, c.Kick, c.Aftertouch, c.QuickPass, c.Dive)).ToArray();
                human.Step(stripped, new List<SimEvent>());
                SimState a = states[i], b = human.PeekState();
                Assert.True(a.Ball.Position.Equals(b.Ball.Position) && a.Ball.Velocity.Equals(b.Ball.Velocity), $"the ball differs at step {i}");
                for (int p = 0; p < a.Players.Length; p++)
                {
                    Assert.True(a.Players[p].Position.Equals(b.Players[p].Position) && a.Players[p].Velocity.Equals(b.Players[p].Velocity), $"player {a.Players[p].Id} differs at step {i}");
                }
            }

            // And his commands carry nothing a human's adapter couldn't send, but the plan.
            Assert.All(recorded.SelectMany(step => step).Where(c => c.ControllerId == OneVOne.AwayController), c => Assert.True(!c.Dive.HasValue && c.Aftertouch.Equals(Vec2.Zero)));
        }

        [Fact]
        public void Difficulty_changes_his_skill_only_never_his_body()
        {
            foreach (string tier in CpuTrials.Tiers)
            {
                CpuSkill skill = CpuSkill.Named(tier)!.Value;
                SimState s = OneVOne.Start(C, seed: 5, awayCpu: skill, homeCpu: skill);
                new Simulator(C, s);
                Assert.All(s.Players, p => Assert.Equal(PlayerCapabilities.Reference.MaxSpeed, p.Capabilities.MaxSpeed));
                Assert.All(s.Players, p => Assert.Equal(PlayerCapabilities.Reference.KickPower, p.Capabilities.KickPower));
                Assert.All(s.Players, p => Assert.Equal(PlayerCapabilities.Reference.BallControl, p.Capabilities.BallControl));
            }

            // Weakest to strongest: slower to decide and react, a larger error, a shorter sight.
            CpuSkill[] tiers = CpuTrials.Tiers.Select(t => CpuSkill.Named(t)!.Value).ToArray();
            for (int i = 1; i < tiers.Length; i++)
            {
                Assert.True(tiers[i].DecisionInterval < tiers[i - 1].DecisionInterval && tiers[i].ReactionDelay < tiers[i - 1].ReactionDelay);
                Assert.True(tiers[i].PerceptionError < tiers[i - 1].PerceptionError && tiers[i].VisionRange > tiers[i - 1].VisionRange);
            }

            Assert.Null(CpuSkill.Named("legend"));
            Assert.Equal(CpuSkill.Pro.DecisionInterval, CpuSkill.Named("PRO")!.Value.DecisionInterval);

            // The skill's names are every value it has, and each tier is inside its ranges.
            Assert.Equal(new[] { "decisionInterval", "reactionDelay", "perceptionError", "visionRange" }, CpuSkill.Parameters.Select(p => p.Name));
            Assert.Equal(0.15, CpuSkill.Pro.Get("decisionInterval"));
        }

        [Fact]
        public void The_weakest_tier_gets_to_a_loose_ball_later_than_the_strongest()
        {
            long FirstTouch(CpuSkill skill)
            {
                SimState s = Alone(skill: skill);
                s.Ball.Position = new Vec3(0, 14, C.BallRadius);
                s.Ball.Velocity = new Vec3(0, 6, 0);
                List<SimEvent> events = Run(new Simulator(C, s), 8 * 60);
                Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Subject == Away);
                return events.First(e => e.Kind == SimEventKind.Touched && e.Subject == Away).Tick;
            }

            long apprentice = FirstTouch(CpuSkill.Apprentice), elite = FirstTouch(CpuSkill.Elite);
            Assert.True(elite < apprentice, $"elite at tick {elite}, apprentice at tick {apprentice}");
        }

        // ---- Deterministic, and the same football either way ----------------------------------------------

        // The human runs at the ball and shoots now and then.
        private static Command? Human(long tick) =>
            new Command(OneVOne.HomeController, tick, tick < 240 ? new Vec2(1, 0.05) : new Vec2(-0.3, 1), tick % 75 == 40 ? new KickIntent(new Vec2(1, 0.1), 0.9, 0.2) : (KickIntent?)null);

        [Fact]
        public void A_match_against_him_is_the_same_twice_from_a_snapshot_and_from_its_recorded_commands()
        {
            const int Ticks = 1200;
            var first = new List<ulong>();
            var recorded = new List<Command[]>();
            var sim = new Simulator(C, OneVOne.Start(C, seed: 0xC83));
            List<SimEvent> events = Run(sim, 400, Human, recorded, first);
            byte[] snapshot = Snapshot.Capture(sim);
            events.AddRange(Run(sim, Ticks - 400, Human, recorded, first));
            Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Subject == Away);
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);

            var second = new List<ulong>();
            List<SimEvent> again = Run(new Simulator(C, OneVOne.Start(C, seed: 0xC83)), Ticks, Human, hashes: second);
            Assert.Equal(first, second);
            Assert.Equal(events, again);

            var resumed = new List<ulong>();
            Run(Snapshot.Restore(snapshot, C), Ticks - 400, Human, hashes: resumed);
            Assert.Equal(first.Skip(400), resumed);

            // From the recording, through its encoding, with no brain running.
            var recording = CommandRecording.StartFrom(new Simulator(C, OneVOne.Start(C, seed: 0xC83)));
            foreach (Command[] step in recorded) recording.Record(step);
            var replayed = new List<ulong>();
            CommandRecording.Decode(recording.Encode()).Replay(C, tickHashes: replayed);

            // The replay's hashes start with the state it started from.
            replayed.RemoveAt(0);
            Assert.Equal(first, replayed);
        }

        [Fact]
        public void He_plays_the_same_football_attacking_either_way()
        {
            var a = new List<SimState>();
            var b = new List<SimState>();
            Run(new Simulator(C, Alone()), 8 * 60, states: a);
            Run(new Simulator(C, Alone(homeAttackSign: -1)), 8 * 60, states: b);
            for (int i = 0; i < a.Count; i++)
            {
                Vec3 x = a[i].Ball.Position, y = b[i].Ball.Position;
                Assert.True(Math.Abs(x.X + y.X) < 1e-6 && Math.Abs(x.Y + y.Y) < 1e-6 && Math.Abs(x.Z - y.Z) < 1e-6, $"step {i}: the ball at {x.X:R},{x.Y:R} against {y.X:R},{y.Y:R}");
                Vec2 p = a[i].Players[2].Position, q = b[i].Players[2].Position;
                Assert.True(Math.Abs(p.X + q.X) < 1e-6 && Math.Abs(p.Y + q.Y) < 1e-6, $"step {i}: he is at {p.X:R},{p.Y:R} against {q.X:R},{q.Y:R}");
            }
        }

        // ---- CPU against CPU ------------------------------------------------------------------------------

        [Fact]
        public void The_trials_match_the_recorded_technical_reference()
        {
            var csv = new System.IO.StringWriter();
            CpuTrials.WriteCsv(csv, C, CpuTrials.Run(C));
            string reference = System.IO.Path.Combine(BallCurvesTests.RepositoryRoot(), "docs", "simulation", "cpu-trials-reference.csv");
            Assert.Equal(System.IO.File.ReadAllText(reference), csv.ToString());
        }

        [Fact]
        public void Cpu_against_cpu_they_shoot_the_ball_changes_feet_and_no_one_is_stuck()
        {
            List<CpuTrial> trials = CpuTrials.Run(C);
            Assert.Equal(4 * CpuTrials.Seeds, trials.Count);
            foreach (string tier in CpuTrials.Tiers)
            {
                List<CpuTrial> of = trials.Where(t => t.Tier == tier).ToList();

                // Every trial ends at a goal, in a keeper's hands or out of play: none runs out of time.
                Assert.All(of, t => Assert.NotEqual("time", t.Outcome));
                Assert.All(of, t => Assert.True(t.HomeKicks + t.AwayKicks >= 1, $"{tier} {t.Seed:x}: no kick"));
                Assert.All(of, t => Assert.True(t.LongestIdleSeconds < 10, $"{tier} {t.Seed:x}: {t.LongestIdleSeconds} s with no one on the ball"));
                Assert.True(of.Sum(t => t.Recoveries) > 0, $"{tier}: the ball never changed feet");
                Assert.True(of.Select(t => t.Outcome).Distinct().Count() >= 2, $"{tier}: every trial ended {of[0].Outcome}");

                // Both sides get the ball, across the seeds.
                Assert.True(of.Any(t => t.HomeTouches > t.AwayTouches) && of.Any(t => t.AwayTouches > t.HomeTouches), $"{tier}: one side always had the ball");
            }

            // The same trials again are the same runs.
            Assert.Equal(trials.Select(t => t.FinalHash), CpuTrials.Run(C).Select(t => t.FinalHash));
        }
    }
}
