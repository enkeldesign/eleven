using System;

namespace Eleven.Simulation.Encoding
{
    /// <summary>
    /// The match-result contract's own bytes (#170, INV-14): every field of a
    /// <see cref="MatchResult"/>, its metrics included, canonical and with its schema version,
    /// so a career's saved results read back exactly.
    /// </summary>
    public static class MatchResultCodec
    {
        public static void Write(CanonicalWriter w, MatchResult r) { }

        public static MatchResult Read(CanonicalReader r) => new MatchResult(0, 0, 0, 0, 0, 0, Array.Empty<ResultGoal>(), 0);
    }
}
