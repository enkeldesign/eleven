using System;
using Eleven.Simulation.Encoding;

namespace Eleven.Simulation
{
    /// <summary>
    /// The complete authoritative state (INV-3): everything that can affect the next tick that
    /// isn't in <see cref="SimConfig"/>. Plain data, ids instead of references, no behaviour
    /// beyond copying and encoding.
    /// </summary>
    public sealed class SimState
    {
        /// <summary>
        /// Steps completed so far. Step N applies the commands for tick N and turns state N into
        /// state N + 1 (see <see cref="SimulationInfo.TickConvention"/>).
        /// </summary>
        public long Tick;

        /// <summary>Seed of every random stream for this run.</summary>
        public ulong Seed;

        public BallState Ball;

        /// <summary>Controllable players, in ascending id order. Slice 1 has exactly one.</summary>
        public PlayerState[] Players = Array.Empty<PlayerState>();

        public SimState Clone()
        {
            var copy = (SimState)MemberwiseClone();
            copy.Players = (PlayerState[])Players.Clone();
            return copy;
        }

        /// <summary>
        /// Canonical encoding, in this order: Tick (int64), Seed (uint64), Ball, player count (int32),
        /// each player. Field order within the ball and players is as declared, a player's
        /// capabilities in their own declaration order. Fails on NaN or ±Infinity anywhere.
        /// </summary>
        public void Encode(CanonicalWriter w)
        {
            w.WriteInt64(Tick);
            w.WriteUInt64(Seed);

            w.WriteInt32(Ball.Id);
            WriteVec3(w, Ball.Position, "Ball.Position");
            WriteVec3(w, Ball.Velocity, "Ball.Velocity");
            WriteVec3(w, Ball.Spin, "Ball.Spin");
            w.WriteBool(Ball.BeyondGoalLine);

            w.WriteInt32(Players.Length);
            foreach (PlayerState player in Players)
            {
                w.WriteInt32(player.Id);
                w.WriteInt32(player.OwnerControllerId);
                w.WriteDouble(player.Capabilities.MaxSpeed, "Player.Capabilities.MaxSpeed");
                w.WriteDouble(player.Capabilities.Acceleration, "Player.Capabilities.Acceleration");
                WriteVec2(w, player.Position, "Player.Position");
                WriteVec2(w, player.Velocity, "Player.Velocity");
                w.WriteDouble(player.Heading, "Player.Heading");
                w.WriteInt32(player.KickCooldown);
            }
        }

        public static SimState Decode(CanonicalReader r)
        {
            var state = new SimState
            {
                Tick = r.ReadInt64(),
                Seed = r.ReadUInt64(),
            };

            state.Ball.Id = r.ReadInt32();
            state.Ball.Position = ReadVec3(r, "Ball.Position");
            state.Ball.Velocity = ReadVec3(r, "Ball.Velocity");
            state.Ball.Spin = ReadVec3(r, "Ball.Spin");
            state.Ball.BeyondGoalLine = r.ReadBool();

            int count = r.ReadInt32();
            if (count < 0 || count > 1024) throw new FormatException($"Implausible player count {count}.");
            state.Players = new PlayerState[count];
            for (int i = 0; i < count; i++)
            {
                state.Players[i] = new PlayerState
                {
                    Id = r.ReadInt32(),
                    OwnerControllerId = r.ReadInt32(),
                    Capabilities = new PlayerCapabilities
                    {
                        MaxSpeed = r.ReadDouble("Player.Capabilities.MaxSpeed"),
                        Acceleration = r.ReadDouble("Player.Capabilities.Acceleration"),
                    },
                    Position = ReadVec2(r, "Player.Position"),
                    Velocity = ReadVec2(r, "Player.Velocity"),
                    Heading = r.ReadDouble("Player.Heading"),
                    KickCooldown = r.ReadInt32(),
                };
            }

            return state;
        }

        /// <summary>FNV-1a 64 of the canonical encoding.</summary>
        public ulong ComputeHash(CanonicalWriter scratch)
        {
            scratch.Clear();
            Encode(scratch);
            return scratch.Hash();
        }

        private static void WriteVec3(CanonicalWriter w, Vec3 v, string field)
        {
            w.WriteDouble(v.X, field);
            w.WriteDouble(v.Y, field);
            w.WriteDouble(v.Z, field);
        }

        private static void WriteVec2(CanonicalWriter w, Vec2 v, string field)
        {
            w.WriteDouble(v.X, field);
            w.WriteDouble(v.Y, field);
        }

        private static Vec3 ReadVec3(CanonicalReader r, string field) =>
            new Vec3(r.ReadDouble(field), r.ReadDouble(field), r.ReadDouble(field));

        private static Vec2 ReadVec2(CanonicalReader r, string field) =>
            new Vec2(r.ReadDouble(field), r.ReadDouble(field));
    }

    public struct BallState
    {
        public int Id;

        /// <summary>Centre, m. Resting on the ground means z = ball radius.</summary>
        public Vec3 Position;

        /// <summary>m/s.</summary>
        public Vec3 Velocity;

        /// <summary>Angular velocity, rad/s. Kicks give sidespin about +z.</summary>
        public Vec3 Spin;

        /// <summary>
        /// Whether the ball was wholly beyond the goal line after the previous step. Needed so a
        /// crossing emits one event, not one per tick spent beyond the line.
        /// </summary>
        public bool BeyondGoalLine;
    }

    public struct PlayerState
    {
        public int Id;

        /// <summary>The controller whose commands move this player (INV-10).</summary>
        public int OwnerControllerId;

        /// <summary>What this footballer can do. Read by the simulation, never changed by it.</summary>
        public PlayerCapabilities Capabilities;

        /// <summary>m, on the pitch plane.</summary>
        public Vec2 Position;

        /// <summary>m/s.</summary>
        public Vec2 Velocity;

        /// <summary>rad, direction of travel; kept when the player stops. Kicks go this way.</summary>
        public double Heading;

        /// <summary>
        /// Kick cooldown: ticks left before this player can kick again. Set to the configured cooldown
        /// by a kick, decreased by one at the start of each later step.
        /// </summary>
        public int KickCooldown;
    }
}
