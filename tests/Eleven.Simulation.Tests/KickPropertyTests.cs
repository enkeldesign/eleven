using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Maths;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Fuzzed properties of the manual kick (#37): seeded random configs (every kick and aftertouch
    /// tunable across its whole valid range), one to three players with random capabilities near the
    /// ball, random kick intents (zero, short and over-long directions; strength and loft beyond 0–1),
    /// random aftertouch, and junk commands.
    ///
    /// As in <see cref="MovementPropertyTests"/>, each property has a companion test that feeds its
    /// check a deliberately broken run and shows the check catches it.
    /// </summary>
    public class KickPropertyTests
    {
        private const int Cases = 200;
        private const int StepsPerCase = 240;
        private const int Unowned = 99;

        private sealed class Case
        {
            public Case(int seed, SimConfig config, SimState initial)
            {
                Seed = seed;
                Config = config;
                Initial = initial;
            }

            public int Seed { get; }
            public SimConfig Config { get; }
            public SimState Initial { get; }
            public override string ToString() => $"seed {Seed}";
        }

        /// <summary>One step: what went in, the players and ball before and after, and what it emitted.</summary>
        private sealed class Observed
        {
            public Observed(long tick, Command[] commands, SimState before, SimState after, SimEvent[] events)
            {
                Tick = tick;
                Commands = commands;
                Before = before;
                After = after;
                Events = events;
            }

            public long Tick { get; }
            public Command[] Commands { get; }
            public SimState Before { get; }
            public SimState After { get; }
            public SimEvent[] Events { get; }

            public bool Contact(int playerId) =>
                Events.Any(e => (e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KickMissed) && e.Subject == playerId);

            public bool Kicked(int playerId) => Events.Any(e => e.Kind == SimEventKind.Kicked && e.Subject == playerId);
        }

        private sealed class Run
        {
            public Run(Case c, List<Observed> steps)
            {
                Case = c;
                Steps = steps;
            }

            public Case Case { get; }
            public List<Observed> Steps { get; }
        }

        // ---- The fuzz ---------------------------------------------------------------------------

        private static double Draw(Random random, TunableParameter p) => p.Min + random.NextDouble() * (p.Max - p.Min);

        private static TunableParameter Tunable(string name) => SimConfig.Tunables.Single(p => p.Name == name);

        private static SimConfig FuzzConfig(Random random)
        {
            var overrides = new List<KeyValuePair<string, double>>();
            foreach (string name in new[] { "kickSpeed", "kickLoft", "kickSideSpin", "aftertouchBend", "aftertouchLift", "kickWindUpSeconds", "aftertouchSeconds" })
            {
                overrides.Add(new KeyValuePair<string, double>(name, Draw(random, Tunable(name))));
            }

            // Some configs with no wind-up, or no window at all.
            if (random.Next(5) == 0) overrides.Add(new KeyValuePair<string, double>("kickWindUpSeconds", 0));
            if (random.Next(10) == 0) overrides.Add(new KeyValuePair<string, double>("aftertouchSeconds", 0));
            return new SimConfig().With(overrides);
        }

        private static IEnumerable<Case> Cases_()
        {
            for (int seed = 1; seed <= Cases; seed++)
            {
                var random = new Random(seed);
                SimConfig config = FuzzConfig(random);
                var ball = new Vec2(random.NextDouble() * 80 - 40, random.NextDouble() * 50 - 25);
                int count = 1 + seed % 3;
                var players = new PlayerState[count];
                for (int i = 0; i < count; i++)
                {
                    var capabilities = new PlayerCapabilities();
                    foreach (TunableParameter p in PlayerCapabilities.Parameters) capabilities = capabilities.With(p.Name, Draw(random, p));
                    double around = random.NextDouble() * 2 * Math.PI, distance = random.NextDouble() * 1.5;
                    players[i] = new PlayerState
                    {
                        Id = i + 1,
                        OwnerControllerId = 11 + i,
                        Capabilities = capabilities,
                        Position = new Vec2(ball.X + distance * Math.Cos(around), ball.Y + distance * Math.Sin(around)),
                        Heading = random.NextDouble() * 2 * Math.PI - Math.PI,
                    };
                }

                double roll = random.NextDouble() < 0.5 ? 0 : random.NextDouble() * 3;
                yield return new Case(seed, config, new SimState
                {
                    Seed = (ulong)seed,
                    Ball = new BallState
                    {
                        Id = 100,
                        Position = new Vec3(ball.X, ball.Y, config.BallRadius),
                        Velocity = new Vec3(roll * Math.Cos(seed), roll * Math.Sin(seed), 0),
                    },
                    Players = players,
                });
            }
        }

        private static Vec2 Direction(double angle, double length) => new Vec2(length * Math.Cos(angle), length * Math.Sin(angle));

        private static Vec2 AnyVector(Random random, double maxLength) => Direction(random.NextDouble() * 2 * Math.PI, random.NextDouble() * maxLength);

        private static KickIntent AnyIntent(Random random)
        {
            Vec2 direction = random.Next(3) switch
            {
                0 => Vec2.Zero,
                1 => AnyVector(random, 1e-3),
                _ => AnyVector(random, 5),
            };
            return new KickIntent(direction, random.NextDouble() * 2 - 0.5, random.NextDouble() * 2 - 0.5);
        }

        // Live run: players either chase the ball or hold a random stick, kick now and then, and
        // hold random aftertouch. Commands may depend on the state, so they are recorded as passed.
        private static Run Live(Case c)
        {
            var random = new Random(c.Seed * 7919);
            var sim = new Simulator(c.Config, c.Initial);
            int n = c.Initial.Players.Length;
            var chase = new bool[n];
            var sticks = new Vec2[n];
            var aftertouch = new Vec2[n];
            var steps = new List<Observed>(StepsPerCase);
            for (int t = 0; t < StepsPerCase; t++)
            {
                SimState state = sim.PeekState();
                var commands = new List<Command>();
                for (int i = 0; i < n; i++)
                {
                    PlayerState p = state.Players[i];
                    if (random.Next(15) == 0)
                    {
                        chase[i] = random.Next(3) != 0;
                        sticks[i] = AnyVector(random, 1.5);
                    }

                    if (random.Next(8) == 0) aftertouch[i] = AnyVector(random, 1.5);
                    if (random.Next(12) == 0) continue; // no command: neutral

                    Vec2 move = sticks[i];
                    if (chase[i])
                    {
                        Vec2 offset = state.Ball.Position.Horizontal - p.Position;
                        double d = Math.Sqrt(offset.LengthSquared);
                        move = d > 0.3 ? offset * (1 / d) : Vec2.Zero;
                    }

                    KickIntent? kick = random.Next(5) == 0 ? AnyIntent(random) : (KickIntent?)null;
                    commands.Add(new Command(p.OwnerControllerId, t, move, kick, aftertouch[i]));
                    if (random.Next(80) == 0) commands.Add(new Command(p.OwnerControllerId, t, move, AnyIntent(random))); // duplicate: both rejected
                }

                int victim = state.Players[random.Next(n)].OwnerControllerId;
                if (random.Next(8) == 0) commands.Add(new Command(Unowned, t, AnyVector(random, 1), AnyIntent(random), AnyVector(random, 1)));
                if (random.Next(15) == 0) commands.Add(new Command(victim, t - 1, Vec2.Zero, AnyIntent(random))); // stale
                if (random.Next(15) == 0) commands.Add(new Command(victim, t + 1, Vec2.Zero, AnyIntent(random))); // early
                if (random.Next(20) == 0) commands.Add(new Command(victim, t, Vec2.Zero, new KickIntent(new Vec2(1, 0), double.NaN, 0.5))); // non-finite
                if (random.Next(20) == 0) commands.Add(new Command(victim, t, Vec2.Zero, null, new Vec2(0, double.PositiveInfinity))); // non-finite

                steps.Add(Step(sim, commands.ToArray()));
            }

            return new Run(c, steps);
        }

        // Replays recorded commands, with the aftertouch of chosen steps replaced.
        private static Run Replay(Run run, Func<int, Observed, bool> replaceAftertouch, int seed)
        {
            var random = new Random(seed);
            var sim = new Simulator(run.Case.Config, run.Case.Initial);
            var steps = new List<Observed>(run.Steps.Count);
            for (int s = 0; s < run.Steps.Count; s++)
            {
                Command[] commands = run.Steps[s].Commands;
                if (replaceAftertouch(s, run.Steps[s]))
                {
                    // Non-finite aftertouch stays: replacing it would turn a rejected command into a valid one.
                    commands = commands.Select(c => Finite(c.Aftertouch) ? new Command(c.ControllerId, c.Tick, c.Move, c.Kick, AnyVector(random, 1.5)) : c).ToArray();
                }

                steps.Add(Step(sim, commands));
            }

            return new Run(run.Case, steps);
        }

        private static Observed Step(Simulator sim, Command[] commands)
        {
            SimState before = sim.CopyState();
            var events = new List<SimEvent>();
            sim.Step(commands, events);
            return new Observed(before.Tick, commands, before, sim.CopyState(), events.ToArray());
        }

        private static readonly List<Run> Fuzzed = Cases_().Select(Live).ToList();

        // ---- Helpers ------------------------------------------------------------------------------

        private static bool Finite(Vec2 v) => double.IsFinite(v.X) && double.IsFinite(v.Y);

        // The one command a step applies for a controller: valid, at its tick, and not duplicated.
        private static Command? Accepted(Observed o, int controller)
        {
            Command[] valid = o.Commands.Where(c => c.ControllerId == controller && c.Tick == o.Tick && Finite(c.Move) && Finite(c.Aftertouch)
                && (!c.Kick.HasValue || (Finite(c.Kick.Value.Direction) && double.IsFinite(c.Kick.Value.Strength) && double.IsFinite(c.Kick.Value.Loft)))).ToArray();
            return valid.Length == 1 ? valid[0] : (Command?)null;
        }

        private static double Clamp01(double x) => Math.Min(1, Math.Max(0, x));

        private static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        private static double Wrap(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }

        private delegate string? Check(SimConfig config, Observed o);

        private static string? FirstViolation(Check check, Func<SimConfig, Observed, Observed>? mutate = null)
        {
            foreach (Run run in Fuzzed)
            {
                for (int s = 0; s < run.Steps.Count; s++)
                {
                    Observed o = mutate == null ? run.Steps[s] : mutate(run.Case.Config, run.Steps[s]);
                    string? violation = check(run.Case.Config, o);
                    if (violation != null) return $"{run.Case}, step {s}: {violation}";
                }
            }

            return null;
        }

        private static void Holds(Check check) => Assert.Null(FirstViolation(check));

        private static void CatchesMutant(Check check, Func<SimConfig, Observed, Observed> mutate) => Assert.NotNull(FirstViolation(check, mutate));

        // A copy of a step with its after-state changed.
        private static Observed WithAfter(Observed o, Action<SimState> change)
        {
            SimState after = o.After.Clone();
            change(after);
            return new Observed(o.Tick, o.Commands, o.Before, after, o.Events);
        }

        // ---- The fuzz covers what it claims ---------------------------------------------------------

        [Fact]
        public void The_fuzz_covers_kicks_misses_wind_ups_windows_and_hand_overs()
        {
            var steps = Fuzzed.SelectMany(r => r.Steps.Select(o => (r.Case.Config, o))).ToList();
            Assert.True(steps.Count(x => x.o.Events.Any(e => e.Kind == SimEventKind.Kicked)) > 300);
            Assert.True(steps.Count(x => x.o.Events.Any(e => e.Kind == SimEventKind.KickMissed)) > 300);
            Assert.Contains(Fuzzed, r => r.Case.Config.KickWindUpTicks == 0 && r.Steps.Any(o => o.Events.Any(e => e.Kind == SimEventKind.Kicked)));
            Assert.Contains(Fuzzed, r => r.Case.Config.AftertouchTicks == 0);
            Assert.Contains(Fuzzed, r => r.Case.Config.KickWindUpTicks > 10 && r.Steps.Any(o => o.Events.Any(e => e.Kind == SimEventKind.Kicked)));

            // Aftertouch input inside an open window, and a window closed early by someone else's contact.
            Assert.Contains(steps, x => x.o.Before.Players.Any(p => p.AftertouchTicks > 0
                && Accepted(x.o, p.OwnerControllerId) is Command c && c.Aftertouch.LengthSquared > 0.25));
            Assert.Contains(steps, x => x.o.Before.Players.Zip(x.o.After.Players, (b, a) => (b, a))
                .Any(p => p.b.AftertouchTicks > 1 && p.a.AftertouchTicks == 0 && !x.o.Kicked(p.a.Id)));

            // Intents along the heading, and strength and loft beyond 0–1.
            Assert.Contains(steps, x => x.o.Commands.Any(c => c.Kick.HasValue && c.Kick.Value.Direction.LengthSquared == 0));
            Assert.Contains(steps, x => x.o.Commands.Any(c => c.Kick.HasValue && (c.Kick.Value.Strength > 1 || c.Kick.Value.Loft < 0)));

            var rejections = new HashSet<CommandRejection>(steps.SelectMany(x => x.o.Events).Select(e => e.Rejection));
            foreach (CommandRejection r in new[] { CommandRejection.Unowned, CommandRejection.Stale, CommandRejection.Early, CommandRejection.Duplicate, CommandRejection.Invalid })
            {
                Assert.Contains(r, rejections);
            }
        }

        // ---- Manual intent is kept --------------------------------------------------------------------

        // The line expected from an accepted intent: its own direction, or the heading if it has none.
        private static double Line(KickIntent intent, PlayerState after) =>
            intent.Direction.X == 0 && intent.Direction.Y == 0 ? after.Heading : SimMath.Atan2(intent.Direction.Y, intent.Direction.X);

        // An intent accepted while the player is ready starts a kick, and the kick stores the intent as
        // given: its own line (or the heading), and strength and loft only clamped.
        private static string? IntentStoredAsGiven(SimConfig config, Observed o)
        {
            int windUp = config.KickWindUpTicks;
            for (int i = 0; i < o.After.Players.Length; i++)
            {
                PlayerState before = o.Before.Players[i], after = o.After.Players[i];
                Command? command = Accepted(o, after.OwnerControllerId);
                bool ready = before.WindUpTicks == 0 && before.KickCooldown <= 1;
                bool intended = command.HasValue && command.Value.Kick.HasValue && ready;
                bool started = windUp > 0 ? before.WindUpTicks == 0 && after.WindUpTicks == windUp : o.Contact(after.Id);
                if (intended != started) return $"player {after.Id}: intent {(intended ? "accepted" : "absent")} but kick {(started ? "started" : "not started")}";
                if (!started) continue;

                KickIntent intent = command!.Value.Kick!.Value;
                double line = Line(intent, after);
                if (windUp > 0)
                {
                    if (!Same(after.WindUpDirection, line) || !Same(after.WindUpStrength, Clamp01(intent.Strength)) || !Same(after.WindUpLoft, Clamp01(intent.Loft)))
                    {
                        return $"player {after.Id}: stored {after.WindUpDirection}/{after.WindUpStrength}/{after.WindUpLoft}, intent {line}/{intent.Strength}/{intent.Loft}";
                    }
                }
                else if (o.Kicked(after.Id) && !Same(after.AftertouchDirection, line))
                {
                    return $"player {after.Id}: kicked along {after.AftertouchDirection}, intended {line}";
                }
            }

            return null;
        }

        // A wind-up's line never changes, and a connecting kick shapes along that same line.
        private static string? LineHeldThroughTheWindUp(SimConfig config, Observed o)
        {
            for (int i = 0; i < o.After.Players.Length; i++)
            {
                PlayerState before = o.Before.Players[i], after = o.After.Players[i];
                if (before.WindUpTicks > 1 && !Same(before.WindUpDirection, after.WindUpDirection))
                {
                    return $"player {after.Id}: line moved from {before.WindUpDirection} to {after.WindUpDirection} during the wind-up";
                }

                if (before.WindUpTicks == 1 && o.Kicked(after.Id) && !Same(before.WindUpDirection, after.AftertouchDirection))
                {
                    return $"player {after.Id}: wound up along {before.WindUpDirection}, kicked along {after.AftertouchDirection}";
                }
            }

            return null;
        }

        // The ball leaves within ± the kicker's execution error of the intended line.
        private static string? LaunchWithinErrorOfTheLine(SimConfig config, Observed o)
        {
            foreach (SimEvent e in o.Events.Where(e => e.Kind == SimEventKind.Kicked))
            {
                // With several contacts in one step the last one decides the ball.
                if (o.Events.Any(f => f.Kind == SimEventKind.Kicked && f.Subject > e.Subject)) continue;
                PlayerState kicker = o.After.Players.Single(p => p.Id == e.Subject);
                Vec2 v = o.After.Ball.Velocity.Horizontal;
                if (v.LengthSquared == 0) continue;
                double off = Math.Abs(Wrap(Math.Atan2(v.Y, v.X) - kicker.AftertouchDirection));
                if (off > kicker.Capabilities.KickError + 1e-12) return $"player {kicker.Id}: launched {off} rad off the line, error {kicker.Capabilities.KickError}";
            }

            return null;
        }

        [Fact]
        public void A_kick_stores_its_intent_as_given() => Holds(IntentStoredAsGiven);

        [Fact]
        public void The_intent_check_catches_a_line_retargeted_at_the_intent() =>
            CatchesMutant(IntentStoredAsGiven, (config, o) => WithAfter(o, s =>
            {
                for (int i = 0; i < s.Players.Length; i++) s.Players[i].WindUpDirection += 1e-9;
            }));

        [Fact]
        public void The_line_is_held_through_the_wind_up() => Holds(LineHeldThroughTheWindUp);

        [Fact]
        public void The_line_check_catches_a_line_retargeted_during_the_wind_up() =>
            CatchesMutant(LineHeldThroughTheWindUp, (config, o) => WithAfter(o, s =>
            {
                for (int i = 0; i < s.Players.Length; i++)
                {
                    if (s.Players[i].WindUpTicks > 0) s.Players[i].WindUpDirection = Wrap(s.Players[i].WindUpDirection + 0.2);
                }
            }));

        [Fact]
        public void The_ball_leaves_within_the_kickers_error_of_the_intended_line() => Holds(LaunchWithinErrorOfTheLine);

        // The mutant turns every launch towards the nearest other player: assistance a manual kick must never get.
        [Fact]
        public void The_launch_check_catches_a_kick_turned_towards_a_teammate() =>
            CatchesMutant(LaunchWithinErrorOfTheLine, (config, o) =>
            {
                SimEvent kicked = o.Events.FirstOrDefault(e => e.Kind == SimEventKind.Kicked);
                if (kicked.Kind != SimEventKind.Kicked || o.After.Players.Length < 2) return o;
                return WithAfter(o, s =>
                {
                    PlayerState kicker = s.Players.Single(p => p.Id == kicked.Subject);
                    PlayerState mate = s.Players.Where(p => p.Id != kicker.Id)
                        .OrderBy(p => (p.Position - s.Ball.Position.Horizontal).LengthSquared).First();
                    Vec2 to = mate.Position - s.Ball.Position.Horizontal;
                    double speed = Math.Sqrt(s.Ball.Velocity.Horizontal.LengthSquared) * (1 / Math.Sqrt(to.LengthSquared));
                    s.Ball.Velocity = new Vec3(to.X * speed, to.Y * speed, s.Ball.Velocity.Z);
                });
            });

        // ---- Timing lives in the simulation ---------------------------------------------------------

        // The wind-up counts down one step at a time, and the kick connects or misses exactly when it runs out.
        private static string? ContactWhenTheWindUpEnds(SimConfig config, Observed o)
        {
            int windUp = config.KickWindUpTicks;
            for (int i = 0; i < o.After.Players.Length; i++)
            {
                PlayerState before = o.Before.Players[i], after = o.After.Players[i];
                if (windUp == 0)
                {
                    if (after.WindUpTicks != 0) return $"player {after.Id}: winding up with no wind-up configured";
                    continue;
                }

                bool expected = before.WindUpTicks == 1;
                if (o.Contact(after.Id) != expected) return $"player {after.Id}: contact {o.Contact(after.Id)} with {before.WindUpTicks} wind-up steps left";
                bool counted = before.WindUpTicks > 0 ? after.WindUpTicks == before.WindUpTicks - 1 : after.WindUpTicks == 0 || after.WindUpTicks == windUp;
                if (!counted) return $"player {after.Id}: wind-up {before.WindUpTicks} -> {after.WindUpTicks}";
            }

            return null;
        }

        // A contact opens the kicker's window and closes everyone else's; otherwise windows count down.
        private static string? WindowOpensAtContactAndCountsDown(SimConfig config, Observed o)
        {
            SimEvent[] kicks = o.Events.Where(e => e.Kind == SimEventKind.Kicked).ToArray();
            int? last = kicks.Length > 0 ? kicks.Max(e => e.Subject) : (int?)null;
            for (int i = 0; i < o.After.Players.Length; i++)
            {
                PlayerState before = o.Before.Players[i], after = o.After.Players[i];
                int expected = last.HasValue ? (after.Id == last.Value ? config.AftertouchTicks : 0) : Math.Max(before.AftertouchTicks - 1, 0);
                if (after.AftertouchTicks != expected) return $"player {after.Id}: window {before.AftertouchTicks} -> {after.AftertouchTicks}, expected {expected}";
            }

            return null;
        }

        [Fact]
        public void A_kick_connects_exactly_when_its_wind_up_runs_out() => Holds(ContactWhenTheWindUpEnds);

        [Fact]
        public void The_wind_up_check_catches_a_wind_up_that_stalls() =>
            CatchesMutant(ContactWhenTheWindUpEnds, (config, o) => WithAfter(o, s =>
            {
                for (int i = 0; i < s.Players.Length; i++)
                {
                    if (o.Before.Players[i].WindUpTicks > 1) s.Players[i].WindUpTicks = o.Before.Players[i].WindUpTicks;
                }
            }));

        [Fact]
        public void The_aftertouch_window_opens_at_contact_and_counts_down() => Holds(WindowOpensAtContactAndCountsDown);

        [Fact]
        public void The_window_check_catches_a_window_that_stays_open() =>
            CatchesMutant(WindowOpensAtContactAndCountsDown, (config, o) => WithAfter(o, s =>
            {
                for (int i = 0; i < s.Players.Length; i++)
                {
                    if (o.Before.Players[i].AftertouchTicks > 0 && s.Players[i].AftertouchTicks > 0) s.Players[i].AftertouchTicks++;
                }
            }));

        // ---- Aftertouch expiry ----------------------------------------------------------------------

        // Replaces every command's aftertouch with random input on each step where no window is open,
        // plus (for the mutant) each window's last open step, and reports the first divergence.
        private static string? ExpiryViolation(bool alsoLastWindowStep)
        {
            foreach (Run run in Fuzzed)
            {
                bool Replace(int s, Observed o) =>
                    o.Before.Players.All(p => p.AftertouchTicks == 0) || (alsoLastWindowStep && o.Before.Players.Any(p => p.AftertouchTicks == 1));

                Run changed = Replay(run, Replace, run.Case.Seed + 50_000);
                for (int s = 0; s < run.Steps.Count; s++)
                {
                    SimState a = run.Steps[s].After, b = changed.Steps[s].After;
                    if (a.ComputeHash(new Encoding.CanonicalWriter()) != b.ComputeHash(new Encoding.CanonicalWriter()))
                    {
                        return $"{run.Case}, step {s}: aftertouch outside every window changed the state";
                    }
                }
            }

            return null;
        }

        [Fact]
        public void Outside_the_window_aftertouch_input_never_changes_anything() => Assert.Null(ExpiryViolation(alsoLastWindowStep: false));

        // The boundary: input on a window's last step still counts, so changing it as well is caught.
        [Fact]
        public void The_expiry_check_catches_input_changed_on_the_windows_last_step() => Assert.NotNull(ExpiryViolation(alsoLastWindowStep: true));

        // ---- Bounded and finite ---------------------------------------------------------------------

        private static string? StaysBounded(SimConfig config, Observed o)
        {
            foreach (PlayerState p in o.After.Players)
            {
                if (p.WindUpTicks < 0 || p.WindUpTicks > config.KickWindUpTicks) return $"player {p.Id}: wind-up {p.WindUpTicks}";
                if (p.AftertouchTicks < 0 || p.AftertouchTicks > config.AftertouchTicks) return $"player {p.Id}: window {p.AftertouchTicks}";
                if (p.KickCooldown < 0 || p.KickCooldown > config.KickCooldownTicks) return $"player {p.Id}: cooldown {p.KickCooldown}";
                if (!(p.WindUpStrength >= 0 && p.WindUpStrength <= 1 && p.WindUpLoft >= 0 && p.WindUpLoft <= 1)) return $"player {p.Id}: strength {p.WindUpStrength}, loft {p.WindUpLoft}";
                if (!double.IsFinite(p.WindUpDirection) || !double.IsFinite(p.AftertouchDirection)) return $"player {p.Id}: non-finite line";
            }

            BallState b = o.After.Ball;
            bool finite = double.IsFinite(b.Position.X) && double.IsFinite(b.Position.Y) && double.IsFinite(b.Position.Z)
                && double.IsFinite(b.Velocity.X) && double.IsFinite(b.Velocity.Y) && double.IsFinite(b.Velocity.Z)
                && double.IsFinite(b.Spin.X) && double.IsFinite(b.Spin.Y) && double.IsFinite(b.Spin.Z);
            return finite ? null : $"non-finite ball {b.Position} {b.Velocity} {b.Spin}";
        }

        [Fact]
        public void Kick_state_stays_bounded_and_finite() => Holds(StaysBounded);

        [Fact]
        public void The_bounds_check_catches_an_unclamped_strength() =>
            CatchesMutant(StaysBounded, (config, o) => WithAfter(o, s => s.Players[0].WindUpStrength = 1.5));
    }
}
