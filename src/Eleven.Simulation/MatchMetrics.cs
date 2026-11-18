using System;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation
{
    /// <summary>
    /// One side's match metrics (#128, result schema 7): read from the match's events and state by
    /// <see cref="MatchRecorder"/>, never part of the football (INV-1).
    /// </summary>
    public readonly struct SideMetrics
    {
        public SideMetrics(double possession, int shots, int onTarget, double xg, int passes, int passesCompleted, int corners, double ppda)
        {
            Possession = possession;
            Shots = shots;
            OnTarget = onTarget;
            Xg = xg;
            Passes = passes;
            PassesCompleted = passesCompleted;
            Corners = corners;
            Ppda = ppda;
        }

        /// <summary>Share of live play (0–1) in which the side last played the ball.</summary>
        public double Possession { get; }

        /// <summary>A kick, header or volley that next reaches the goal line at the end the side attacks, the frame, or the other keeper.</summary>
        public int Shots { get; }

        /// <summary>Shots that scored or that the other keeper held or parried.</summary>
        public int OnTarget { get; }

        /// <summary>Expected goals: the sum of each shot's chance by <see cref="MetricsModel"/>.</summary>
        public double Xg { get; }

        /// <summary>Quick passes played, and those a teammate played next.</summary>
        public int Passes { get; }

        public int PassesCompleted { get; }

        /// <summary>Corners awarded to the side.</summary>
        public int Corners { get; }

        /// <summary>Passes per defensive action: the other side's passes in its own 60 % per this side's defensive actions there; 0 with none.</summary>
        public double Ppda { get; }
    }

    /// <summary>
    /// The expected-goals reference model (#128): a logistic on a shot's distance to the goal's
    /// centre and the angle its mouth subtends, of the shape public xG models use. A reader's
    /// model: its parameters are metric references, not simulation config, and change no match.
    /// </summary>
    public sealed class MetricsModel
    {
        public MetricsModel(double intercept = -1.0, double perMetre = -0.12, double perRadian = 1.0, double penalty = 0.76)
        {
            Intercept = intercept;
            PerMetre = perMetre;
            PerRadian = perRadian;
            Penalty = penalty;
        }

        public static MetricsModel Reference { get; } = new MetricsModel();

        public double Intercept { get; }

        /// <summary>1/m. The logit's change per metre from the goal's centre.</summary>
        public double PerMetre { get; }

        /// <summary>1/rad. The logit's change per radian of goal mouth seen.</summary>
        public double PerRadian { get; }

        /// <summary>A penalty's chance, whatever the geometry.</summary>
        public double Penalty { get; }

        /// <summary>A shot's chance from <paramref name="distance"/> m, seeing <paramref name="angle"/> rad of the goal mouth.</summary>
        public double Chance(double distance, double angle) => 1 / (1 + SimMath.Exp(-(Intercept + PerMetre * distance + PerRadian * angle)));

        /// <summary>
        /// A shot's chance from <paramref name="at"/>, toward the goal on the line at x =
        /// <paramref name="goalX"/>: its distance to the goal's centre and the angle between the
        /// lines to its posts.
        /// </summary>
        public double Chance(Vec2 at, double goalX, double goalWidth)
        {
            double dx = goalX > 0 ? goalX - at.X : at.X - goalX;
            if (dx < 0) dx = 0;
            double dy = at.Y;
            double distance = SimMath.Sqrt(dx * dx + dy * dy);
            double half = goalWidth / 2;
            double angle = SimMath.Atan2(goalWidth * dx, dx * dx + dy * dy - half * half);
            if (angle < 0) angle = -angle;
            return Chance(distance, angle);
        }
    }
}
