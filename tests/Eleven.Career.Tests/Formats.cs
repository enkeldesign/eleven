namespace Eleven.Career.Tests
{
    /// <summary>The world's bytes' later sections, for the tests that read an older format by cutting them.</summary>
    internal static class Formats
    {
        /// <summary>Format 8's section (#198): each club's balance and two ledgers, each footballer's wage.</summary>
        public static int Money(World w) => 4 + w.Clubs.Count * (4 + 2 * Ledger.FirstLines * 4) + 4 + w.Players.Count * 2;

        /// <summary>
        /// Format 9's section (#200): each club's expected place, league and cup targets in a byte
        /// each, its confidence, last season's place, expected place and target, its confidence
        /// then and its reputation before the turn.
        /// </summary>
        public static int Board(World w) => 4 + w.Clubs.Count * (3 + 8 + 3 + 8 + 1);

        /// <summary>
        /// Format 10's section (#201) for a world with no moves: each footballer's contract in a
        /// byte and his free season in two, each club's two ledgers' days in two bytes each, then
        /// the turn's moves, none.
        /// </summary>
        public static int Contracts(World w) => 4 + w.Players.Count * 3 + 4 + w.Clubs.Count * 4 + 4;

        /// <summary>Format 11's section (#203) for a world with no transfers: each club's two transfers lines, then the window's transfers, none.</summary>
        public static int Transfers(World w) => 4 + w.Clubs.Count * 8 + 4;

        /// <summary>Every section from format 8 on: format 7's world is the rest.</summary>
        public static int From8(World w) => Money(w) + From9(w);

        /// <summary>Every section from format 9 on: format 8's world is the rest.</summary>
        public static int From9(World w) => Board(w) + From10(w);

        /// <summary>Every section from format 10 on: format 9's world is the rest.</summary>
        public static int From10(World w) => Contracts(w) + Transfers(w);
    }
}
