// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// Who the human controls (#93, #80's default): a switch target for his command on each step,
    /// or 0. Automatic at the ball's changes of hands, by who reaches it first; and on the
    /// human's own request, which holds until the next possession event. Its decisions go into
    /// his owned command and are recorded (INV-10), so a replay needs no switcher.
    /// </summary>
    public sealed class SwitchController
    {
        private readonly int _controllerId;
        private bool _manualHold;

        public SwitchController(int controllerId, SwitchMode mode = SwitchMode.Auto)
        {
            _controllerId = controllerId;
            Mode = mode;
        }

        /// <summary>How automatic switching is (#136). It may change at any step.</summary>
        public SwitchMode Mode { get; set; }

        /// <summary>Why the last <see cref="Decide"/> switched, or None when it didn't.</summary>
        public SwitchCause LastCause { get; private set; }

        /// <summary>
        /// The teammate SWITCH would take now, or 0 (#136): for the host's marker. It changes
        /// nothing.
        /// </summary>
        public int Next(Simulator simulator) => 0;

        /// <summary>Whether a manual switch is holding off the automatic one.</summary>
        public bool ManualHold => _manualHold;

        /// <summary>
        /// The teammate to switch to before the next step, or 0. <paramref name="lastEvents"/> are
        /// the events of the step just run; <paramref name="manual"/> is the human's own request.
        /// </summary>
        public int Decide(Simulator simulator, IReadOnlyList<SimEvent> lastEvents, bool manual)
        {
            SimState s = simulator.PeekState();
            int me = Array.FindIndex(s.Players, p => p.OwnerControllerId == _controllerId);
            if (me < 0) return 0;
            PlayerState mine = s.Players[me];

            // A possession event ends a manual hold.
            bool possession = false;
            foreach (SimEvent e in lastEvents)
            {
                possession |= e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Kicked || e.Kind == SimEventKind.KeeperHeld
                    || e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.RestartPlaced || e.Kind == SimEventKind.Attached;
            }

            if (manual)
            {
                int target = FirstToBall(s, simulator.Config, mine, exceptMe: true);
                _manualHold = target != 0;
                return target;
            }

            if (possession) _manualHold = false;
            if (_manualHold) return 0;

            foreach (SimEvent e in lastEvents)
            {
                // A pass he played: he takes over its receiver.
                if (e.Kind == SimEventKind.QuickPassResolved && e.Subject == mine.Id && IsCpuMate(s, mine, e.Other)) return e.Other;

                // A teammate has it, or takes his side's restart: he takes over that teammate.
                if ((e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Attached || e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.RestartPlaced)
                    && IsCpuMate(s, mine, e.Subject))
                {
                    return e.Subject;
                }
            }

            // An opponent has it: the teammate who reaches the ball first.
            foreach (SimEvent e in lastEvents)
            {
                bool turnover = e.Kind == SimEventKind.Touched || e.Kind == SimEventKind.Attached || e.Kind == SimEventKind.ChallengeWon || e.Kind == SimEventKind.KeeperHeld;
                if (!turnover || TeamOf(s, e.Subject) == mine.TeamId || TeamOf(s, e.Subject) == 0) continue;
                return FirstToBall(s, simulator.Config, mine, exceptMe: false);
            }

            return 0;
        }

        private static bool IsCpuMate(SimState s, PlayerState mine, int id) =>
            id != mine.Id && Array.FindIndex(s.Cpus, c => c.PlayerId == id) >= 0 && TeamOf(s, id) == mine.TeamId && Array.FindIndex(s.Keepers, k => k.PlayerId == id) < 0;

        private static int TeamOf(SimState s, int id)
        {
            int i = Array.FindIndex(s.Players, p => p.Id == id);
            return i >= 0 ? s.Players[i].TeamId : 0;
        }

        // The CPU teammate who reaches the ball first by its own flight (INV-13): 0 when it is the
        // human's own footballer, unless he asked to switch.
        private static int FirstToBall(SimState s, SimConfig config, PlayerState mine, bool exceptMe)
        {
            var path = new List<Vec3>();
            BallFlight.Predict(config, s.Ball.Position, s.Ball.Velocity, s.Ball.Spin, 4 * config.TickRate, path, bothGoals: s.Teams.Length == 2);
            double Reach(PlayerState p)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    double t = (i + 1) * config.TickSeconds;
                    if (SimMath.Sqrt((path[i].Horizontal - p.Position).LengthSquared) <= p.Capabilities.MaxSpeed * t) return t;
                }

                return 4 + SimMath.Sqrt((path[path.Count - 1].Horizontal - p.Position).LengthSquared) / p.Capabilities.MaxSpeed;
            }

            int best = 0;
            double soonest = exceptMe ? double.PositiveInfinity : Reach(mine);
            foreach (PlayerState p in s.Players)
            {
                if (!IsCpuMate(s, mine, p.Id)) continue;
                double t = Reach(p);
                if (t < soonest)
                {
                    soonest = t;
                    best = p.Id;
                }
            }

            return best;
        }
    }

    /// <summary>How automatic switching is (#136).</summary>
    public enum SwitchMode : byte
    {
        /// <summary>#93's: to a pass's receiver, to a teammate who has the ball, at a turnover, and on SWITCH.</summary>
        Auto = 0,

        /// <summary>To a pass's receiver and on SWITCH; never at a turnover.</summary>
        OnTheBall = 1,

        /// <summary>Only on SWITCH.</summary>
        Manual = 2,
    }

    /// <summary>Why the switcher switched (#136).</summary>
    public enum SwitchCause : byte
    {
        None = 0,

        /// <summary>To the receiver of the human's pass.</summary>
        Pass = 1,

        /// <summary>To a teammate who touched, took or won the ball, or took his side's restart.</summary>
        MateHasBall = 2,

        /// <summary>An opponent gained the ball: to the teammate who reaches it first.</summary>
        Turnover = 3,

        /// <summary>The human pressed SWITCH.</summary>
        Asked = 4,
    }
}
