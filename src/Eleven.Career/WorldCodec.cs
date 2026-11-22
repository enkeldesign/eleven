#nullable enable

namespace Eleven.Career
{
    /// <summary>A world as plain, versioned bytes (#157), and back: the start of M6.07's saves.</summary>
    public static class WorldCodec
    {
        public const int FormatVersion = 1;

        /// <summary>Plumbing: nothing is written yet.</summary>
        public static byte[] Encode(World world) => new byte[0];

        /// <summary>Plumbing: an empty world.</summary>
        public static World Decode(byte[] bytes) => new World();
    }
}
