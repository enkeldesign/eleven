using System;

namespace Eleven.Simulation
{
    /// <summary>
    /// What a keeper perceives (#69, INV-9): his own body, phase and capabilities, and the ball as
    /// seen now. No command, no other controller's input, no wind-up intent, no seed and no
    /// reference to state: his brain decides on this alone.
    /// </summary>
    public readonly struct KeeperObservation
    {
        public KeeperObservation(long tick, Vec2 position, Vec2 velocity, double heading, KeeperPhase phase, int phaseTicks,
            KeeperCapabilities capabilities, double runSpeed, Vec3 ballPosition, Vec3 ballVelocity, Vec3 ballSpin, bool ballHeld, bool holding)
        {
            Tick = tick;
            Position = position;
            Velocity = velocity;
            Heading = heading;
            Phase = phase;
            PhaseTicks = phaseTicks;
            Capabilities = capabilities;
            RunSpeed = runSpeed;
            BallPosition = ballPosition;
            BallVelocity = ballVelocity;
            BallSpin = ballSpin;
            BallHeld = ballHeld;
            Holding = holding;
        }

        public long Tick { get; }

        /// <summary>m, his position on the pitch.</summary>
        public Vec2 Position { get; }

        /// <summary>m/s.</summary>
        public Vec2 Velocity { get; }

        /// <summary>rad.</summary>
        public double Heading { get; }

        public KeeperPhase Phase { get; }

        /// <summary>Steps left in his phase.</summary>
        public int PhaseTicks { get; }

        public KeeperCapabilities Capabilities { get; }

        /// <summary>m/s, his top speed on his feet.</summary>
        public double RunSpeed { get; }

        public Vec3 BallPosition { get; }

        public Vec3 BallVelocity { get; }

        public Vec3 BallSpin { get; }

        /// <summary>Whether anyone holds the ball.</summary>
        public bool BallHeld { get; }

        /// <summary>Whether he holds it.</summary>
        public bool Holding { get; }
    }

    /// <summary>A keeper's brain's decision (#69): where he moves on his feet, and the point he dives for, if he does.</summary>
    public readonly struct KeeperDecision
    {
        public KeeperDecision(Vec2 move, Vec3? dive = null)
        {
            Move = move;
            Dive = dive;
        }

        /// <summary>Length at most 1, as a stick's: a fraction of his top speed.</summary>
        public Vec2 Move { get; }

        public Vec3? Dive { get; }
    }

    /// <summary>The perception boundary (INV-9): the only way an AI sees the simulation.</summary>
    public static class Perception
    {
        /// <summary>What the keeper at <paramref name="keeper"/> in <see cref="SimState.Keepers"/> perceives in <paramref name="state"/>.</summary>
        public static KeeperObservation OfKeeper(SimState state, int keeper)
        {
            KeeperState k = state.Keepers[keeper];
            PlayerState body = Array.Find(state.Players, p => p.Id == k.PlayerId);
            BallState ball = state.Ball;
            return new KeeperObservation(state.Tick, body.Position, body.Velocity, body.Heading, k.Phase, k.PhaseTicks, k.Capabilities,
                body.Capabilities.MaxSpeed, ball.Position, ball.Velocity, ball.Spin, ball.HeldBy != 0, ball.HeldBy == k.PlayerId);
        }
    }

    /// <summary>
    /// Runs each keeper's brain (#69): before a step, it appends each keeper's command for that
    /// step, his brain's decision on what he perceives, owned by his AI controller. The commands are
    /// recorded like any other, so a replay never runs a brain.
    /// </summary>
    public static class KeeperControllers
    {
        public static void Append(Simulator simulator, System.Collections.Generic.List<Command> commands)
        {
            SimState state = simulator.PeekState();
            for (int i = 0; i < state.Keepers.Length; i++)
            {
                KeeperObservation seen = Perception.OfKeeper(state, i);
                KeeperDecision decision = AI.KeeperBrain.Decide(seen, simulator.Config);
                int controller = Array.Find(state.Players, p => p.Id == state.Keepers[i].PlayerId).OwnerControllerId;
                commands.Add(new Command(controller, state.Tick, decision.Move, dive: decision.Dive));
            }
        }
    }
}
