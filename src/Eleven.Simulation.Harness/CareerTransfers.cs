using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eleven.Career;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// The transfer market measured (#203): a career rolled forward headless, each window's
    /// transfers and fees by the divisions they went from and to, fees against values, wage bills
    /// over budget before and after, and the top division's footballers bought from below.
    /// </summary>
    public static class CareerTransfers
    {
        /// <summary>One window.</summary>
        public sealed class WindowView
        {
            public int Season;
            public TransferWindow Window;

            /// <summary>Transfers and their fees by the division from, then to.</summary>
            public int[,] Count = new int[3, 3];

            public long[,] Fees = new long[3, 3];

            /// <summary>The mean fee over the footballer's value.</summary>
            public double FeeOverValue;

            /// <summary>Each division's mean wage bill over budget, before the window and after.</summary>
            public double[] OverBefore = new double[3], OverAfter = new double[3];
        }

        /// <summary>The windows, and the share of the top division's footballers its clubs bought from below by the end.</summary>
        public sealed class Measurement
        {
            public List<WindowView> Windows = new List<WindowView>();

            public double TopBoughtFromBelow;

            /// <summary>Every fee paid, less every fee received, over every window: 0 when the money is conserved.</summary>
            public long FeesNet;
        }

        /// <summary><paramref name="seasons"/> seasons from <paramref name="seed"/>, each turned.</summary>
        public static Measurement Measure(ulong seed, int seasons) => new Measurement();

        /// <summary>The measurement as text.</summary>
        public static string Report(ulong seed, int seasons) => "";
    }
}
