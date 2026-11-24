namespace Eleven.Career.Tests
{
    /// <summary>The world's bytes' later sections, for the tests that read an older format by cutting them.</summary>
    internal static class Formats
    {
        /// <summary>Format 8's section (#198): each club's balance and two ledgers, each footballer's wage.</summary>
        public static int Money(World w) => 4 + w.Clubs.Count * (4 + 2 * Ledger.Lines * 4) + 4 + w.Players.Count * 2;

        /// <summary>
        /// Format 9's section (#200): each club's expected place, league and cup targets in a byte
        /// each, its confidence, last season's place, expected place and target, its confidence
        /// then and its reputation before the turn.
        /// </summary>
        public static int Board(World w) => 4 + w.Clubs.Count * (3 + 8 + 3 + 8 + 1);

        /// <summary>Every section from format 8 on: format 7's world is the rest.</summary>
        public static int From8(World w) => Money(w) + Board(w);
    }
}
