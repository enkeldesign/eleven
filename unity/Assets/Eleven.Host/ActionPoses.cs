using System;
using System.Collections.Generic;
using Eleven.Presentation;
using Eleven.Simulation;

namespace Eleven.Host
{
    /// <summary>
    /// Which action each footballer is drawn in (#101), from the simulation's state and the events
    /// of its steps: a wind-up or a kick just struck, a header or a volley just met, a touch just
    /// taken, a slide or a fall, a keeper's dive or the ball in his gloves, the carrier shielding,
    /// a run or a stand. It reads and never writes (INV-2): nothing here reaches a command.
    /// Engine-free, so the tests run it.
    /// </summary>
    public sealed class ActionPoses
    {
        /// <summary>s. How long a kick, a header, a volley or a touch is drawn after its step.</summary>
        public const double Hold = 0.25;

        /// <summary>m. A stride: the run's two frames change each time he covers this.</summary>
        public const double Stride = 0.9;

        /// <summary>m/s. Slower than this he stands.</summary>
        public const double Moving = 0.5;

        private readonly Dictionary<int, (Pose Pose, long Tick)> _struck = new Dictionary<int, (Pose, long)>();
        private readonly Dictionary<int, double> _covered = new Dictionary<int, double>();

        /// <summary>Forgets everything: a new match or a reset.</summary>
        public void Clear()
        {
            _struck.Clear();
            _covered.Clear();
        }

        /// <summary>After each step: what its events say each footballer did, and how far each ran.</summary>
        public void Observe(SimState state, IReadOnlyList<SimEvent> events, SimConfig config)
        {
            foreach (SimEvent e in events)
            {
                Pose? pose = e.Kind switch
                {
                    SimEventKind.Kicked => Pose.Kick,
                    SimEventKind.Headed => Pose.Header,
                    SimEventKind.Volleyed => Pose.Volley,
                    SimEventKind.Touched => Pose.Receive,
                    SimEventKind.Attached => Pose.Receive,
                    _ => (Pose?)null,
                };
                if (!pose.HasValue) continue;

                // A header or a volley is also a kick, on the same step: the air contact is drawn.
                if (pose == Pose.Kick && _struck.TryGetValue(e.Subject, out (Pose Pose, long Tick) had) && had.Tick == e.Tick
                    && (had.Pose == Pose.Header || had.Pose == Pose.Volley)) continue;
                _struck[e.Subject] = (pose.Value, e.Tick);
            }

            foreach (PlayerState p in state.Players)
            {
                _covered.TryGetValue(p.Id, out double run);
                _covered[p.Id] = run + Math.Sqrt(p.Velocity.LengthSquared) * config.TickSeconds;
            }
        }

        /// <summary>The pose of the footballer at <paramref name="index"/> now.</summary>
        /// <param name="still">Reduced motion: no frame animation beyond a change of state, so a run is one frame.</param>
        public Pose Of(SimState state, int index, SimConfig config, bool still = false)
        {
            PlayerState p = state.Players[index];
            int keeper = Array.FindIndex(state.Keepers, k => k.PlayerId == p.Id);
            if (keeper >= 0)
            {
                if (state.Keepers[keeper].Phase == KeeperPhase.Dive) return Pose.KeeperDive;
                if (state.Ball.HeldBy == p.Id) return Pose.KeeperHold;
            }

            if (p.Challenge == ChallengePhase.Slide) return Pose.Slide;
            if (p.Challenge == ChallengePhase.Grounded) return Pose.Grounded;

            int hold = (int)Math.Round(Hold * config.TickRate);
            bool struck = _struck.TryGetValue(p.Id, out (Pose Pose, long Tick) last) && state.Tick - last.Tick < hold;
            if (struck && (last.Pose == Pose.Header || last.Pose == Pose.Volley)) return last.Pose;
            if (p.WindUpTicks > 0 || (struck && last.Pose == Pose.Kick)) return Pose.Kick;
            if (Shielding(state, p, config)) return Pose.Shield;
            if (struck && last.Pose == Pose.Receive) return Pose.Receive;

            double speed = Math.Sqrt(p.Velocity.LengthSquared);
            if (speed < Moving) return Pose.Stand;
            if (still) return Pose.RunA;
            _covered.TryGetValue(p.Id, out double covered);
            return (long)(covered / Stride) % 2 == 0 ? Pose.RunA : Pose.RunB;
        }

        /// <summary>
        /// The human's side's keeper's place in <see cref="SimState.Keepers"/> (#110): the first
        /// side's, by who he plays for; the first keeper in a world without sides; -1 with none.
        /// Never by the array's order, which a substitution changes.
        /// </summary>
        public static int HomeKeeper(SimState state)
        {
            return state.Keepers.Length == 0 ? -1 : 0;
        }

        /// <summary>
        /// rad, world. The way the pose is drawn facing: his heading; a slide along its line; a dive
        /// gloves first, which the sprite draws to its left.
        /// </summary>
        public static double Facing(SimState state, int index, Pose pose)
        {
            PlayerState p = state.Players[index];
            if (pose == Pose.Slide && p.ChallengeLine.LengthSquared > 0) return Math.Atan2(p.ChallengeLine.Y, p.ChallengeLine.X);
            if (pose == Pose.KeeperDive)
            {
                int k = Array.FindIndex(state.Keepers, x => x.PlayerId == p.Id);
                Vec2 dive = k >= 0 ? state.Keepers[k].DiveVelocity : Vec2.Zero;
                if (dive.LengthSquared > 0) return Math.Atan2(dive.Y, dive.X) - Math.PI / 2;
            }

            return p.Heading;
        }

        // The carrier slowed with an opponent close: the simulation's own test (#97), read from the
        // state. The carrier is the simulation's, under FREE as under ATTACHED.
        private static bool Shielding(SimState state, PlayerState p, SimConfig config)
        {
            if (config.ShieldSpeed <= 0 || !Simulator.Carries(state, config, p)) return false;
            double slow = config.ShieldSpeed * p.Capabilities.MaxSpeed;
            if (p.Velocity.LengthSquared >= slow * slow) return false;
            foreach (PlayerState other in state.Players)
            {
                if (other.TeamId == p.TeamId) continue;
                if ((other.Position - p.Position).LengthSquared <= config.ShieldRange * config.ShieldRange) return true;
            }

            return false;
        }
    }
}
