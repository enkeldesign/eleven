#nullable enable

namespace Eleven.Career
{
    /// <summary>
    /// The fictional world from a seed (#157): <see cref="Divisions"/> divisions of
    /// <see cref="ClubsPerDivision"/> clubs, <see cref="SquadSize"/> footballers a club, one
    /// manager each. Deterministic: counter-based draws on the world's own stream (INV-6).
    /// </summary>
    public static class WorldGenerator
    {
        public const int Divisions = 3, ClubsPerDivision = 12, SquadSize = 23, KeepersPerSquad = 3;

        /// <summary>Plumbing: an empty world.</summary>
        public static World Generate(ulong seed) => new World { Seed = seed };
    }
}
