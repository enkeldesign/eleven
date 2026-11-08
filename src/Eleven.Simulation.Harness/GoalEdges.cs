// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.Harness
{
    /// <summary>One shot at the goal's edge (#68): where it meets the frame, and what comes of it.</summary>
    public sealed class GoalEdge
    {
        /// <summary><c>ground</c>, rolled at the left post, or <c>high</c>, flat at the crossbar.</summary>
        public string Kind = "";

        public double SpeedMps;

        /// <summary>
        /// m. Where the ball's centre is aimed, from the post's or the bar's inner face: the left
        /// post's for a ground shot (y − goalWidth / 2), the bar's underside for a high one
        /// (z − goalHeight). Negative is inside the mouth.
        /// </summary>
        public double OffsetMetres;

        /// <summary><c>goal</c>, <c>post</c> or <c>bar</c> (it hit and came out), <c>wide</c> or <c>over</c>.</summary>
        public string Outcome = "";

        /// <summary>The part of the frame it hit first: <c>none</c>, <c>left-post</c>, <c>right-post</c> or <c>crossbar</c>.</summary>
        public string Part = "none";

        /// <summary>m/s, the ball's speed after its first contact; NaN without one.</summary>
        public double ReboundMps = double.NaN;

        /// <summary>°, how far its first contact turned the ball's direction; NaN without one.</summary>
        public double TurnDegrees = double.NaN;
    }

    /// <summary>
    /// The goal's edges (#68): ground shots rolled straight at the left post, from well inside it
    /// to well wide of it, at a pass's and a shot's speed, and flat shots at the crossbar from under
    /// it to over it. A technical reference of the current geometry and response rule, not approved
    /// feel (docs/simulation/goal.md).
    /// </summary>
    public static class GoalEdges
    {
        private const int BallId = 100;

        // The offsets: −0.30 m to +0.50 m in 5 cm steps.
        private const int Steps = 17;
        private const double First = -0.30, Spacing = 0.05;

        public static List<GoalEdge> Characterise(SimConfig config)
        {
            var edges = new List<GoalEdge>();
            foreach (double speed in new[] { 8.0, 25.0 })
            {
                for (int k = 0; k < Steps; k++) edges.Add(Ground(config, speed, First + k * Spacing));
            }

            for (int k = 0; k < Steps; k++) edges.Add(High(config, 20, First + k * Spacing));
            return edges;
        }

        // Rolled straight along +x from 3 m out, at y = goalWidth / 2 + offset.
        private static GoalEdge Ground(SimConfig config, double speed, double offset)
        {
            var ball = new BallState
            {
                Id = BallId,
                Position = new Vec3(config.GoalLineX - 3, config.GoalWidth / 2 + offset, config.BallRadius),
                Velocity = new Vec3(speed, 0, 0),
            };

            return Shoot(config, ball, "ground", speed, offset, "wide");
        }

        // Flat along +x from 0.6 m out, at z = goalHeight + offset: gravity drops it a few
        // millimetres before it reaches the line.
        private static GoalEdge High(SimConfig config, double speed, double offset)
        {
            var ball = new BallState
            {
                Id = BallId,
                Position = new Vec3(config.GoalLineX - 0.6, 0, config.GoalHeight + offset),
                Velocity = new Vec3(speed, 0, 0),
            };

            return Shoot(config, ball, "high", speed, offset, "over");
        }

        private static GoalEdge Shoot(SimConfig config, BallState ball, string kind, double speed, double offset, string miss)
        {
            var edge = new GoalEdge { Kind = kind, SpeedMps = speed, OffsetMetres = offset };
            var sim = new Simulator(config, new SimState { Ball = ball });
            var events = new List<SimEvent>();
            bool goal = false, crossed = false;
            for (int t = 0; t < 3 * config.TickRate; t++)
            {
                Vec3 before = sim.PeekState().Ball.Velocity;
                events.Clear();
                sim.Step(Array.Empty<Command>(), events);
                foreach (SimEvent e in events)
                {
                    if (e.Kind == SimEventKind.Goal) goal = true;
                    if (e.Kind == SimEventKind.GoalLineCrossed) crossed = true;
                    if (e.Kind != SimEventKind.HitFrame || edge.Part != "none") continue;

                    edge.Part = PartName((FramePart)e.Other);
                    Vec3 after = sim.PeekState().Ball.Velocity;
                    edge.ReboundMps = SimMath.Sqrt(after.LengthSquared);
                    edge.TurnDegrees = SimMath.Atan2(SimMath.Sqrt(Vec3.Cross(before, after).LengthSquared), Dot(before, after)) * 180 / Math.PI;
                }
            }

            edge.Outcome = goal ? "goal" : edge.Part == "crossbar" ? "bar" : edge.Part != "none" ? "post" : crossed ? miss : "short";
            return edge;
        }

        private static string PartName(FramePart part) => part switch
        {
            FramePart.LeftPost => "left-post",
            FramePart.RightPost => "right-post",
            FramePart.Crossbar => "crossbar",
            _ => "none",
        };

        private static double Dot(Vec3 u, Vec3 v) => u.X * v.X + u.Y * v.Y + u.Z * v.Z;

        public static void WriteCsv(TextWriter w, SimConfig config, IReadOnlyList<GoalEdge> edges)
        {
            w.Write("config_hash,kind,speed_mps,offset_m,outcome,part,rebound_mps,turn_deg\n");
            string hash = config.Hash.ToString("x16", CultureInfo.InvariantCulture);
            foreach (GoalEdge e in edges)
            {
                w.Write(string.Join(",", hash, e.Kind, F(e.SpeedMps), F(e.OffsetMetres), e.Outcome, e.Part, F(e.ReboundMps), F(e.TurnDegrees)));
                w.Write('\n');
            }
        }

        /// <summary>
        /// The figures a reader wants first, one line: for each kind and speed, the offsets that
        /// score clean, meet the frame and miss.
        /// </summary>
        public static string KeyNumbers(SimConfig config, IReadOnlyList<GoalEdge> edges)
        {
            var parts = new List<string>
            {
                FormattableString.Invariant($"goal {config.GoalWidth:F2} × {config.GoalHeight:F2} m, posts {config.PostWidth * 100:F0} cm, restitution {config.FrameRestitution:F2}"),
            };

            string? kind = null;
            double speed = 0;
            var line = new List<string>();
            void Flush()
            {
                if (kind != null) parts.Add(FormattableString.Invariant($"{kind} {speed:F0} m/s: {string.Join(", ", line)}"));
                line.Clear();
            }

            string? run = null;
            double from = 0, to = 0;
            void Close()
            {
                if (run != null) line.Add(FormattableString.Invariant($"{run} {from:+0.00;-0.00} to {to:+0.00;-0.00} m"));
                run = null;
            }

            foreach (GoalEdge e in edges)
            {
                if (e.Kind != kind || e.SpeedMps != speed)
                {
                    Close();
                    Flush();
                    kind = e.Kind;
                    speed = e.SpeedMps;
                }

                string what = e.Outcome == "goal" && e.Part != "none" ? "in off the frame" : e.Outcome;
                if (what != run)
                {
                    Close();
                    run = what;
                    from = e.OffsetMetres;
                }

                to = e.OffsetMetres;
            }

            Close();
            Flush();
            return string.Join("; ", parts);
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
