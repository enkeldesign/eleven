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
        public static void Write(CanonicalWriter w, Season s)
        {
            w.WriteInt32(s.Number);
            w.WriteUInt64(s.Seed);
            w.WriteInt32(s.Divisions.Count);
            foreach (List<int> d in s.Divisions) Ints(w, d);
            Ints(w, s.CupRanking);
            w.WriteInt32(s.Fixtures.Count);
            foreach (Fixture f in s.Fixtures)
            {
                w.WriteInt32(f.Id);
                w.WriteInt32(f.Season);
                w.WriteByte((byte)f.Competition);
                w.WriteInt32(f.Round);
                w.WriteInt32(f.Index);
                w.WriteInt32(f.Home);
                w.WriteInt32(f.Away);
                w.WriteBool(f.Neutral);
            }

            // In fixture-id order, so the same season always gives the same bytes.
            var ids = new List<int>(s.Results.Keys);
            ids.Sort();
            w.WriteInt32(ids.Count);
            foreach (int id in ids)
            {
                w.WriteInt32(id);
                MatchResultCodec.Write(w, s.Results[id]);
            }

            w.WriteInt32(s.Matchday);
            Ints(w, s.CupAlive);
            w.WriteInt32(s.CupWinner);
        }

        public static Season Read(CanonicalReader r)
        {
            var s = new Season { Number = r.ReadInt32(), Seed = r.ReadUInt64() };
            int divisions = Count(r.ReadInt32());
            for (int d = 0; d < divisions; d++) s.Divisions.Add(Ints(r));
            s.CupRanking = Ints(r);
            int fixtures = Count(r.ReadInt32());
            for (int i = 0; i < fixtures; i++)
            {
                s.Fixtures.Add(new Fixture
                {
                    Id = r.ReadInt32(),
                    Season = r.ReadInt32(),
                    Competition = (CompetitionId)r.ReadByte(),
                    Round = r.ReadInt32(),
                    Index = r.ReadInt32(),
                    Home = r.ReadInt32(),
                    Away = r.ReadInt32(),
                    Neutral = r.ReadBool(),
                });
            }

            int results = Count(r.ReadInt32());
            for (int i = 0; i < results; i++)
            {
                int id = r.ReadInt32();
                s.Results[id] = MatchResultCodec.Read(r);
            }

            s.Matchday = r.ReadInt32();
            s.CupAlive = Ints(r);
            s.CupWinner = r.ReadInt32();
            return s;
        }

        private static void Ints(CanonicalWriter w, List<int> values)
        {
            w.WriteInt32(values.Count);
            foreach (int v in values) w.WriteInt32(v);
        }

        private static List<int> Ints(CanonicalReader r)
        {
            int n = Count(r.ReadInt32());
            var values = new List<int>(n);
            for (int i = 0; i < n; i++) values.Add(r.ReadInt32());
            return values;
        }

        internal static int Count(int n) => n < 0 || n > 1_000_000 ? throw new FormatException($"Invalid count {n}.") : n;
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

        public static byte[] Encode(SaveFile save)
        {
            byte[] world = WorldCodec.Encode(save.World);
            var w = new CanonicalWriter(world.Length + 64 * 1024);
            w.WriteInt32(Magic);
            w.WriteInt32(FormatVersion);
            w.WriteUInt64(save.Seed);
            w.WriteInt32(save.Club);
            w.WriteByte(save.Role);
            w.WriteInt64(save.SavedAt);
            w.WriteInt32(world.Length);
            w.WriteBytes(world);
            SeasonCodec.Write(w, save.Season);
            w.WriteUInt64(w.Hash());
            return w.ToArray();
        }

        /// <summary>The save in <paramref name="bytes"/>; a <see cref="FormatException"/> saying why for anything else.</summary>
        public static SaveFile Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) throw new FormatException("Not a save: too short.");
            ulong sum = 0;
            for (int i = 0; i < 8; i++) sum |= (ulong)bytes[bytes.Length - 8 + i] << (8 * i);
            if (Fnv1a64.Hash(bytes, 0, bytes.Length - 8) != sum) throw new FormatException("The save's checksum doesn't match: it is damaged.");
            var payload = new byte[bytes.Length - 8];
            Array.Copy(bytes, payload, payload.Length);
            try
            {
                var r = new CanonicalReader(payload);
                if (r.ReadInt32() != Magic) throw new FormatException("Not a save: wrong magic.");
                int format = r.ReadInt32();
                if (format != FormatVersion)
                {
                    if (format < FormatVersion && Migrations.TryGetValue(format, out Func<byte[], byte[]>? step)) return Decode(step(bytes));
                    throw new FormatException($"Save format {format}; this build reads format {FormatVersion}.");
                }

                var save = new SaveFile { Seed = r.ReadUInt64(), Club = r.ReadInt32(), Role = r.ReadByte(), SavedAt = r.ReadInt64() };
                int length = SeasonCodec.Count(r.ReadInt32());
                var world = new byte[length];
                for (int i = 0; i < length; i++) world[i] = r.ReadByte();
                save.World = WorldCodec.Decode(world);
                save.Season = SeasonCodec.Read(r);
                if (!r.AtEnd) throw new FormatException("Bytes after the save.");
                return save;
            }
            catch (Exception e) when (!(e is FormatException))
            {
                throw new FormatException("Not a whole save: " + e.Message, e);
            }
        }

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

        private readonly ISaveStore _store;

        public SaveSlots(ISaveStore store) => _store = store;

        private static string Key(int slot, string part)
        {
            if (slot < 0 || slot >= Count) throw new ArgumentOutOfRangeException(nameof(slot), slot, $"Slots run from 0 to {Count - 1}.");
            return $"eleven.save.{slot}.{part}";
        }

        /// <summary>
        /// Saves into <paramref name="slot"/>: the save is staged, read back and verified, and only
        /// then becomes current, the old current becoming previous. False, with the slot as it
        /// was, when the store refuses or damages it.
        /// </summary>
        public bool Save(int slot, SaveFile save)
        {
            string text = Convert.ToBase64String(SaveCodec.Encode(save));
            string staging = Key(slot, "staging"), current = Key(slot, "current"), previous = Key(slot, "previous");
            if (!_store.Write(staging, text) || _store.Read(staging) != text || Verify(_store.Read(staging)) == null)
            {
                _store.Delete(staging);
                return false;
            }

            string? old = _store.Read(current);
            if (old != null && !_store.Write(previous, old))
            {
                _store.Delete(staging);
                return false;
            }

            if (!_store.Write(current, text) || _store.Read(current) != text)
            {
                // The current key may be half written: the previous one is the old current, and a load falls back to it.
                _store.Delete(staging);
                return false;
            }

            _store.Delete(staging);
            return true;
        }

        private static SaveFile? Verify(string? text)
        {
            if (text == null) return null;
            try
            {
                return SaveCodec.Decode(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static (SaveFile? Save, string Problem) Read(string? text)
        {
            if (text == null) return (null, "empty");
            try
            {
                return (SaveCodec.Decode(Convert.FromBase64String(text)), "");
            }
            catch (FormatException e)
            {
                return (null, e.Message);
            }
        }

        /// <summary>The slot's current save if it verifies, else its previous one, and why the current one didn't.</summary>
        public (SaveFile? Save, bool FromPrevious, string Problem) Load(int slot)
        {
            (SaveFile? current, string problem) = Read(_store.Read(Key(slot, "current")));
            if (current != null) return (current, false, "");
            (SaveFile? previous, string _) = Read(_store.Read(Key(slot, "previous")));
            return previous != null ? (previous, true, problem) : (null, false, problem);
        }

        /// <summary>The slot as the list shows it: empty, or its club, season, matchday and saved time.</summary>
        public SlotSummary Summary(int slot)
        {
            (SaveFile? save, bool fromPrevious, string _) = Load(slot);
            if (save == null) return new SlotSummary();
            Club? club = save.World.Clubs.Find(c => c.Id == save.Club);
            return new SlotSummary
            {
                Empty = false,
                Club = save.Club,
                ClubName = club?.Name ?? "",
                ClubShort = club?.ShortName ?? "",
                Season = save.Season.Number,
                Matchday = save.Season.Matchday + 1,
                SavedAt = save.SavedAt,
                FromPrevious = fromPrevious,
            };
        }

        public void Delete(int slot)
        {
            _store.Delete(Key(slot, "current"));
            _store.Delete(Key(slot, "previous"));
            _store.Delete(Key(slot, "staging"));
        }
    }
}
