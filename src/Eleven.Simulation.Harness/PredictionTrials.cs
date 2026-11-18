using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>What a prediction trial found: how many predicted ball positions were checked, and how many differed.</summary>
    public sealed class PredictionTrial
    {
        public int PerSide;
        public ulong Seed;

        /// <summary>Predictions made: one a tick the ball was free.</summary>
        public int Predictions;

        /// <summary>Predicted positions compared with the simulation's own ball, and those that differed.</summary>
        public long Checked, Differed;

        /// <summary>The first difference, for the report: tick, steps ahead, predicted and actual.</summary>
        public string FirstDifference = "";
    }

    /// <summary>
    /// M5's gate (#130), INV-13's same-sim prediction: in CPU matches, each tick the ball is free, the
    /// CPU's flight prediction (<see cref="BallFlight.Predict"/>, as its brains read it) is compared
    /// with where the simulation then puts the ball, step by step, until something plays it or play
    /// stops. Any difference fails. It reads the state; it changes nothing.
    /// </summary>
    public static class PredictionTrials
    {
        public static PredictionTrial One(SimConfig config, int perSide, ulong seed, double seconds) =>
            new PredictionTrial { PerSide = perSide, Seed = seed };
    }
}
