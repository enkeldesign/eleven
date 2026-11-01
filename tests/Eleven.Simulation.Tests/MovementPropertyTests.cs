using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation.Maths;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Fuzzed properties of player movement (#36), over seeded random players whose capabilities
    /// span the whole valid range (<see cref="PlayerCapabilities.Parameters"/>), driven by random
    /// sticks: neutral, full, partial, over-long, plus unowned, stale, non-finite and duplicate
    /// commands.
    ///
    /// As in <see cref="BallPropertyTests"/>, each property has a companion test that feeds its
    /// check a deliberately broken run and shows the check catches it.
    /// </summary>
    public class MovementPropertyTests
    {
        private const int Cases = 300;
        private const int StepsPerCase = 300;
        private const int Unowned = 99;

        // Relative rounding allowed on top of the exact bounds. The bounds are exact in real
        // arithmetic; each step adds a few ulps at most.
        private const double Rounding = 1e-9;

        private sealed class Case
        {
            public Case(int seed, SimState initial)
            {
                Seed = seed;
                Initial = initial;
            }

            public int Seed { get; }
            public SimState Initial { get; }
            public override string ToString() => $"seed {Seed}";
        }

        /// <summary>One step: every player before and after, by index.</summary>
        private readonly struct Observed
        {
            public Observed(PlayerState[] before, PlayerState[] after)
            {
                Before = before;
                After = after;
            }

            public PlayerState[] Before { get; }
            public PlayerState[] After { get; }
        }

        // ---- The fuzz ---------------------------------------------------------------------------

        private static SimConfig Config { get; } = new SimConfig();

        private static double Draw(Random random, TunableParameter p) => p.Min + random.NextDouble() * (p.Max - p.Min);

        // One to three players with their own capabilities, starting anywhere from rest to full speed.
        // The ball is far out of reach, so every kick misses and only movement is under test.
        private static IEnumerable<Case> Cases_()
        {
            for (int seed = 1; seed <= Cases; seed++)
            {
                var random = new Random(seed);
                int count = 1 + seed % 3;
                var players = new PlayerState[count];
                for (int i = 0; i < count; i++)
                {
                    var capabilities = new PlayerCapabilities
                    {
                        MaxSpeed = Draw(random, PlayerCapabilities.Parameters[0]),
                        Acceleration = Draw(random, PlayerCapabilities.Parameters[1]),
                    };
                    double direction = random.NextDouble() * 2 * Math.PI;
                    double speed = random.NextDouble() < 0.3 ? 0 : random.NextDouble() * capabilities.MaxSpeed;
                    players[i] = new PlayerState
                    {
                        Id = i + 1,
                        OwnerControllerId = 11 + i,
                        Capabilities = capabilities,
                        Position = new Vec2(random.NextDouble() * 100 - 50, random.NextDouble() * 60 - 30),
                        Velocity = new Vec2(speed * Math.Cos(direction), speed * Math.Sin(direction)),
                        Heading = direction,
                    };
                }

                yield return new Case(seed, new SimState
                {
                    Seed = (ulong)seed,
                    Ball = new BallState { Id = 100, Position = new Vec3(1e4, 1e4, Config.BallRadius) },
                    Players = players,
                });
            }
        }

        private static Vec2 Stick(Random random) => random.Next(10) switch
        {
            0 or 1 => Vec2.Zero,
            2 or 3 or 4 or 5 => Direction(random.NextDouble() * 2 * Math.PI, 1),
            6 or 7 => Direction(random.NextDouble() * 2 * Math.PI, random.NextDouble()),
            8 => new Vec2(random.NextDouble() * 6 - 3, random.NextDouble() * 6 - 3),
            _ => Direction(random.NextDouble() * 2 * Math.PI, 1e-4),
        };

        private static Vec2 Direction(double angle, double length) => new Vec2(length * Math.Cos(angle), length * Math.Sin(angle));

        // A run's command stream. Sticks persist for a while, as a thumb's would, so players reach
        // full speed and turn at it. Valid commands for `keep` (a controller) come from their own
        // random stream, so a second run can change everything else, including rejected junk sent
        // as `keep`, and leave the owner's valid commands alone.
        private static List<Command[]> Commands(Case c, int otherSeed, int? keep = null)
        {
            var own = new Random(c.Seed * 7919);
            var others = new Random(otherSeed);
            PlayerState[] players = c.Initial.Players;
            var sticks = new Vec2[players.Length];
            var steps = new List<Command[]>();
            for (int t = 0; t < StepsPerCase; t++)
            {
                var commands = new List<Command>();
                for (int i = 0; i < players.Length; i++)
                {
                    int controller = players[i].OwnerControllerId;
                    Random random = controller == keep ? own : others;
                    if (random.Next(20) == 0) sticks[i] = Stick(random);
                    if (random.Next(10) == 0) continue; // no command: neutral
                    commands.Add(new Command(controller, t, sticks[i], random.Next(4) == 0));
                    if (random.Next(60) == 0) commands.Add(new Command(controller, t, Stick(random), false)); // duplicate: both rejected
                }

                if (others.Next(8) == 0) commands.Add(new Command(Unowned, t, Stick(others), true));
                if (others.Next(15) == 0) commands.Add(new Command(players[0].OwnerControllerId, t - 1, Stick(others), false)); // stale
                if (others.Next(15) == 0) commands.Add(new Command(players[players.Length - 1].OwnerControllerId, t + 1, Stick(others), false)); // early
                if (others.Next(15) == 0) commands.Add(new Command(players[0].OwnerControllerId, t, new Vec2(double.NaN, 0), true)); // non-finite
                steps.Add(commands.ToArray());
            }

            return steps;
        }

        private static List<Observed> Run(Case c, List<Command[]> commands)
        {
            var sim = new Simulator(Config, c.Initial);
            var events = new List<SimEvent>();
            var observed = new List<Observed>(commands.Count);
            foreach (Command[] step in commands)
            {
                var before = (PlayerState[])sim.PeekState().Players.Clone();
                sim.Step(step, events);
                observed.Add(new Observed(before, (PlayerState[])sim.PeekState().Players.Clone()));
            }

            return observed;
        }

        private static readonly List<(Case Case, List<Observed> Steps)> Fuzzed =
            Cases_().Select(c => (c, Run(c, Commands(c, c.Seed)))).ToList();

        // ---- The checks: each returns the first violation, or null -------------------------------

        private delegate string? Check(PlayerState before, PlayerState after);

        private static double Length(Vec2 v) => Math.Sqrt(v.LengthSquared);

        private static string? WithinTopSpeed(PlayerState before, PlayerState after)
        {
            double cap = after.Capabilities.MaxSpeed * (1 + Rounding);
            return Length(after.Velocity) > cap ? $"speed {Length(after.Velocity)} over top speed {after.Capabilities.MaxSpeed}" : null;
        }

        private static string? WithinAcceleration(PlayerState before, PlayerState after)
        {
            double allowed = after.Capabilities.Acceleration * Config.TickSeconds * (1 + Rounding);
            double change = Length(after.Velocity - before.Velocity);
            return change > allowed ? $"velocity changed by {change} in a tick, allowed {allowed}" : null;
        }

        private static string? CapabilitiesUnchanged(PlayerState before, PlayerState after) =>
            Same(before.Capabilities.MaxSpeed, after.Capabilities.MaxSpeed) && Same(before.Capabilities.Acceleration, after.Capabilities.Acceleration)
                ? null
                : $"capabilities {before.Capabilities.MaxSpeed}/{before.Capabilities.Acceleration} -> {after.Capabilities.MaxSpeed}/{after.Capabilities.Acceleration}";

        // Heading is the direction of travel, and is kept below a millimetre per second.
        private static string? HeadingFollowsTravel(PlayerState before, PlayerState after)
        {
            double expected = after.Velocity.LengthSquared > 1e-6 ? SimMath.Atan2(after.Velocity.Y, after.Velocity.X) : before.Heading;
            return Same(expected, after.Heading) ? null : $"heading {after.Heading}, expected {expected} for velocity {after.Velocity}";
        }

        private static string? StaysFinite(PlayerState before, PlayerState after) =>
            double.IsFinite(after.Position.X) && double.IsFinite(after.Position.Y)
            && double.IsFinite(after.Velocity.X) && double.IsFinite(after.Velocity.Y) && double.IsFinite(after.Heading)
                ? null
                : $"non-finite: {after.Position} {after.Velocity} {after.Heading}";

        private static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        private static string? FirstViolation(Check check, Func<PlayerState, PlayerState, PlayerState>? mutate = null)
        {
            foreach ((Case c, List<Observed> steps) in Fuzzed)
            {
                for (int s = 0; s < steps.Count; s++)
                {
                    for (int p = 0; p < steps[s].After.Length; p++)
                    {
                        PlayerState before = steps[s].Before[p];
                        PlayerState after = mutate == null ? steps[s].After[p] : mutate(before, steps[s].After[p]);
                        string? violation = check(before, after);
                        if (violation != null) return $"{c}, step {s}, player {after.Id}: {violation}";
                    }
                }
            }

            return null;
        }

        private static void Holds(Check check) => Assert.Null(FirstViolation(check));

        private static void CatchesMutant(Check check, Func<PlayerState, PlayerState, PlayerState> mutate) =>
            Assert.NotNull(FirstViolation(check, mutate));

        // ---- The fuzz covers what it claims ---------------------------------------------------------

        [Fact]
        public void The_fuzz_covers_top_speed_full_acceleration_stops_turns_and_rejections()
        {
            IEnumerable<(PlayerState Before, PlayerState After)> all =
                Fuzzed.SelectMany(f => f.Steps.SelectMany(s => s.Before.Zip(s.After, (b, a) => (b, a))));
            var steps = all.ToList();

            Assert.Contains(steps, s => Length(s.After.Velocity) > s.After.Capabilities.MaxSpeed * (1 - 1e-9));
            Assert.Contains(steps, s => Length(s.After.Velocity - s.Before.Velocity) > s.After.Capabilities.Acceleration * Config.TickSeconds * (1 - 1e-9));
            Assert.Contains(steps, s => s.Before.Velocity.LengthSquared > 0 && s.After.Velocity.LengthSquared == 0);
            Assert.Contains(steps, s => Math.Abs(s.After.Heading - s.Before.Heading) > 0.05 && s.After.Velocity.LengthSquared > 1);
            Assert.Contains(Fuzzed, f => f.Steps[0].After.Length == 3);

            var rejections = new HashSet<CommandRejection>();
            foreach ((Case c, _) in Fuzzed.Take(20))
            {
                var events = new List<SimEvent>();
                var sim = new Simulator(Config, c.Initial);
                foreach (Command[] step in Commands(c, c.Seed)) sim.Step(step, events);
                rejections.UnionWith(events.Select(e => e.Rejection));
            }

            foreach (CommandRejection r in new[] { CommandRejection.Unowned, CommandRejection.Stale, CommandRejection.Early, CommandRejection.Duplicate, CommandRejection.Invalid })
            {
                Assert.Contains(r, rejections);
            }
        }

        // ---- Properties, each with its mutant -----------------------------------------------------

        [Fact]
        public void No_player_exceeds_their_top_speed() => Holds(WithinTopSpeed);

        [Fact]
        public void The_top_speed_check_catches_a_player_running_too_fast() =>
            CatchesMutant(WithinTopSpeed, (b, a) => { a.Velocity = a.Velocity * 1.01; return a; });

        [Fact]
        public void No_velocity_changes_faster_than_the_players_acceleration() => Holds(WithinAcceleration);

        [Fact]
        public void The_acceleration_check_catches_a_change_that_is_too_quick() =>
            CatchesMutant(WithinAcceleration, (b, a) => { a.Velocity = b.Velocity + (a.Velocity - b.Velocity) * 1.5; return a; });

        [Fact]
        public void Capabilities_never_change() => Holds(CapabilitiesUnchanged);

        [Fact]
        public void The_capability_check_catches_a_change() =>
            CatchesMutant(CapabilitiesUnchanged, (b, a) => { a.Capabilities.Acceleration += 1e-9; return a; });

        [Fact]
        public void The_heading_follows_the_direction_of_travel() => Holds(HeadingFollowsTravel);

        [Fact]
        public void The_heading_check_catches_a_heading_off_the_direction_of_travel() =>
            CatchesMutant(HeadingFollowsTravel, (b, a) => { a.Heading += 1e-9; return a; });

        [Fact]
        public void Player_state_stays_finite() => Holds(StaysFinite);

        [Fact]
        public void The_finiteness_check_catches_a_non_finite_value() =>
            CatchesMutant(StaysFinite, (b, a) => { a.Position = new Vec2(a.Position.X, double.PositiveInfinity); return a; });

        // Owned control (INV-10): change every other controller's commands, unowned ones included, and
        // a player's own run is unchanged, bit for bit.
        private static string? OwnedControlViolation(Func<List<Observed>, List<Observed>>? mutate = null)
        {
            foreach ((Case c, List<Observed> steps) in Fuzzed.Where(f => f.Case.Initial.Players.Length > 1))
            {
                PlayerState[] players = c.Initial.Players;
                int index = c.Seed % players.Length;
                int owner = players[index].OwnerControllerId;
                List<Observed> first = Run(c, Commands(c, c.Seed, keep: owner));
                List<Observed> second = Run(c, Commands(c, c.Seed + 100_000, keep: owner));
                if (mutate != null) second = mutate(second);

                for (int s = 0; s < first.Count; s++)
                {
                    if (!SamePlayer(first[s].After[index], second[s].After[index]))
                    {
                        return $"{c}, step {s}: player {players[index].Id} moved differently when only other controllers' commands changed";
                    }
                }
            }

            return null;
        }

        private static bool SamePlayer(PlayerState a, PlayerState b) =>
            a.Id == b.Id && a.OwnerControllerId == b.OwnerControllerId && a.KickCooldown == b.KickCooldown
            && Same(a.Position.X, b.Position.X) && Same(a.Position.Y, b.Position.Y)
            && Same(a.Velocity.X, b.Velocity.X) && Same(a.Velocity.Y, b.Velocity.Y) && Same(a.Heading, b.Heading)
            && Same(a.Capabilities.MaxSpeed, b.Capabilities.MaxSpeed) && Same(a.Capabilities.Acceleration, b.Capabilities.Acceleration);

        [Fact]
        public void Only_a_players_owner_controls_them() => Assert.Null(OwnedControlViolation());

        [Fact]
        public void The_ownership_check_catches_a_player_moved_by_someone_else() =>
            Assert.NotNull(OwnedControlViolation(steps =>
            {
                Observed s = steps[steps.Count / 2];
                PlayerState[] after = (PlayerState[])s.After.Clone();
                for (int i = 0; i < after.Length; i++) after[i].Position = new Vec2(after[i].Position.X + 1e-12, after[i].Position.Y);
                steps[steps.Count / 2] = new Observed(s.Before, after);
                return steps;
            }));
    }
}
