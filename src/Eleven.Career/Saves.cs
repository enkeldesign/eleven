#nullable enable
using System;
using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;

namespace Eleven.Career
{
    /// <summary>A season's bytes (#170): every field of a <see cref="Season"/>, its results through the result contract's codec.</summary>
    public static class SeasonCodec
    {
        public static void Write(CanonicalWriter w, Season s) { }

        public static Season Read(CanonicalReader r) => new Season();
    }

    /// <summary>What a save holds (#170): the career's seed, club, role and saved time, its world and its season.</summary>
    public sealed class SaveFile
    {
        public ulong Seed;
        public int Club;

        /// <summary>The human's role: 0 player-manager, 1 manager-only.</summary>
        public byte Role;

        /// <summary>When it was saved, as the host's clock gives it: shown, never played on.</summary>
        public long SavedAt;

        public World World = new World();
        public Season Season = new Season();
    }

    /// <summary>
    /// A save as bytes (#170): magic <c>ELSV</c>, the format version, the career, the world's and
    /// the season's bytes, then a 64-bit checksum over everything before it.
    /// </summary>
    public static class SaveCodec
    {
        public const int Magic = 0x454C5356; // "ELSV"
        public const int FormatVersion = 1;

        public static byte[] Encode(SaveFile save) => Array.Empty<byte>();

        /// <summary>The save in <paramref name="bytes"/>; a <see cref="FormatException"/> saying why for anything else.</summary>
        public static SaveFile Decode(byte[] bytes) => new SaveFile();

        /// <summary>The steps from each older format to the next, by the format they read. Format 1 is the first.</summary>
        public static readonly IReadOnlyDictionary<int, Func<byte[], byte[]>> Migrations = new Dictionary<int, Func<byte[], byte[]>>();
    }

    /// <summary>Where saves live: the host's key-value store (the page's local storage).</summary>
    public interface ISaveStore
    {
        string? Read(string key);

        /// <summary>False when the store refused it.</summary>
        bool Write(string key, string value);

        void Delete(string key);
    }

    /// <summary>A slot as the list shows it.</summary>
    public sealed class SlotSummary
    {
        public bool Empty = true;
        public int Club;
        public string ClubName = "";
        public string ClubShort = "";
        public int Season;
        public int Matchday;
        public long SavedAt;

        /// <summary>Whether the slot's current save failed and its previous one was read.</summary>
        public bool FromPrevious;
    }

    /// <summary>
    /// Five career slots (#170, #80) over the host's store. Each holds a current and a previous
    /// save. A write goes to a staging key, is read back and verified, and only then becomes
    /// current; a load takes current if it verifies, else previous.
    /// </summary>
    public sealed class SaveSlots
    {
        public const int Count = 5;

        public SaveSlots(ISaveStore store) { }

        public bool Save(int slot, SaveFile save) => false;

        public (SaveFile? Save, bool FromPrevious, string Problem) Load(int slot) => (null, false, "");

        public SlotSummary Summary(int slot) => new SlotSummary();

        public void Delete(int slot) { }
    }
}
