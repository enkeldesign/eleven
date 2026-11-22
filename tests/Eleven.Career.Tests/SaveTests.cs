using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #170: saves. A save round-trips a career mid-season byte for byte; anything cut, flipped,
    /// foreign or from a later format is refused; five slots keep a current and a previous save
    /// each, write transactionally and fall back when the current one is damaged.
    /// </summary>
    public class SaveTests
    {
        private static readonly CompetitionRules Rules = CompetitionRules.Reference;

        private static MatchResult Result(int home, int away, int homePens = 0, int awayPens = 0) =>
            new MatchResult(1, 2, home, away, 180, 360, new[] { new ResultGoal(1, 3, 1, 12.5) }, 4, homeFouls: home + 1, awayFouls: away + 2,
                extraTime: homePens + awayPens > 0 ? 1 : 0, homeShootoutKicks: homePens + awayPens > 0 ? 5 : 0, awayShootoutKicks: homePens + awayPens > 0 ? 5 : 0,
                homeShootoutGoals: homePens, awayShootoutGoals: awayPens);

        private static MatchResult Scripted(Fixture f) =>
            f.Competition == CompetitionId.Cup ? (f.Home < f.Away ? Result(2, 1) : Result(1, 1, 3, 4)) : (f.Home % 3 == 0 ? Result(1, 1) : Result(f.Home < f.Away ? 2 : 0, 1));

        // A career ten matchdays in, the cup's first round among them.
        private static SaveFile MidSeason()
        {
            World w = WorldGenerator.Generate(0x170);
            Season s = SeasonEngine.Start(w, 1, 0xCA4EE5, SeasonEngine.FirstRanking(w), Rules);
            for (int day = 0; day < 10; day++)
            {
                foreach (Fixture f in SeasonEngine.Next(s, Rules)) SeasonEngine.Record(s, Rules, f.Id, Scripted(f));
                SeasonEngine.Advance(s, Rules);
            }

            return new SaveFile { Seed = 0xCA4EE5, Club = w.Clubs[3].Id, Role = 1, SavedAt = 638_700_000_000_000_000, World = w, Season = s };
        }

        [Fact]
        public void A_save_round_trips_a_career_mid_season_byte_for_byte()
        {
            SaveFile save = MidSeason();
            byte[] bytes = SaveCodec.Encode(save);
            Assert.True(bytes.Length > 70_000, $"{bytes.Length} bytes");
            SaveFile back = SaveCodec.Decode(bytes);
            Assert.Equal(bytes, SaveCodec.Encode(back));
            Assert.Equal((save.Seed, save.Club, save.Role, save.SavedAt), (back.Seed, back.Club, back.Role, back.SavedAt));
            Assert.Equal(WorldCodec.Encode(save.World), WorldCodec.Encode(back.World));
            Season a = save.Season, b = back.Season;
            Assert.Equal((a.Number, a.Seed, a.Matchday, a.CupWinner), (b.Number, b.Seed, b.Matchday, b.CupWinner));
            Assert.Equal(a.Divisions.SelectMany(d => d), b.Divisions.SelectMany(d => d));
            Assert.Equal(a.CupRanking, b.CupRanking);
            Assert.Equal(a.CupAlive, b.CupAlive);
            Assert.Equal(a.Fixtures.Select(f => (f.Id, f.Season, f.Competition, f.Round, f.Index, f.Home, f.Away, f.Neutral)),
                b.Fixtures.Select(f => (f.Id, f.Season, f.Competition, f.Round, f.Index, f.Home, f.Away, f.Neutral)));
            Assert.Equal(a.Results.OrderBy(x => x.Key).Select(x => (x.Key, x.Value.ToText())), b.Results.OrderBy(x => x.Key).Select(x => (x.Key, x.Value.ToText())));

            // And the season goes on from where it was saved, the same.
            foreach (Season s in new[] { a, b })
            {
                foreach (Fixture f in SeasonEngine.Next(s, Rules)) SeasonEngine.Record(s, Rules, f.Id, Scripted(f));
                SeasonEngine.Advance(s, Rules);
            }

            Assert.Equal(SeasonEngine.Table(a, CompetitionId.Division2, Rules).Select(r => (r.Club, r.Points)), SeasonEngine.Table(b, CompetitionId.Division2, Rules).Select(r => (r.Club, r.Points)));
        }

        // The bytes with their checksum made good again, as a later build would write them.
        private static byte[] Resealed(byte[] bytes)
        {
            var copy = (byte[])bytes.Clone();
            ulong sum = Fnv1a64.Hash(copy, 0, copy.Length - 8);
            for (int i = 0; i < 8; i++) copy[copy.Length - 8 + i] = (byte)(sum >> (8 * i));
            return copy;
        }

        [Fact]
        public void A_cut_flipped_foreign_or_later_save_is_refused()
        {
            byte[] bytes = SaveCodec.Encode(MidSeason());
            Assert.NotEmpty(bytes);
            Assert.Throws<FormatException>(() => SaveCodec.Decode(bytes.Take(bytes.Length / 2).ToArray()));
            Assert.Throws<FormatException>(() => SaveCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<FormatException>(() => SaveCodec.Decode(Array.Empty<byte>()));
            foreach (int at in new[] { 0, 4, 9, bytes.Length / 3, bytes.Length / 2, bytes.Length - 9, bytes.Length - 1 })
            {
                var flipped = (byte[])bytes.Clone();
                flipped[at] ^= 0x20;
                Assert.Throws<FormatException>(() => SaveCodec.Decode(flipped));
            }

            // A later format, its checksum good: refused, not misread.
            var later = (byte[])bytes.Clone();
            later[4] = (byte)(SaveCodec.FormatVersion + 1);
            FormatException e = Assert.Throws<FormatException>(() => SaveCodec.Decode(Resealed(later)));
            Assert.Contains("format", e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(SaveCodec.Migrations);
        }

        private sealed class Store : ISaveStore
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public Func<string, bool> Refuse = _ => false;

            public string? Read(string key) => Values.TryGetValue(key, out string? v) ? v : null;

            public bool Write(string key, string value)
            {
                if (Refuse(key)) return false;
                Values[key] = value;
                return true;
            }

            public void Delete(string key) => Values.Remove(key);
        }

        [Fact]
        public void Five_slots_keep_a_current_and_a_previous_save_each()
        {
            var store = new Store();
            var slots = new SaveSlots(store);
            Assert.Equal(5, SaveSlots.Count);
            Assert.All(Enumerable.Range(0, 5), i => Assert.True(slots.Summary(i).Empty));

            SaveFile first = MidSeason();
            Assert.True(slots.Save(2, first));
            SlotSummary sum = slots.Summary(2);
            Assert.False(sum.Empty);
            Assert.Equal((first.Club, 1, 11, first.SavedAt), (sum.Club, sum.Season, sum.Matchday, sum.SavedAt));
            Assert.Equal(first.World.Clubs.Single(c => c.Id == first.Club).Name, sum.ClubName);
            Assert.All(new[] { 0, 1, 3, 4 }, i => Assert.True(slots.Summary(i).Empty));

            (SaveFile? loaded, bool fromPrevious, string problem) = slots.Load(2);
            Assert.NotNull(loaded);
            Assert.False(fromPrevious);
            Assert.Equal("", problem);
            Assert.Equal(SaveCodec.Encode(first), SaveCodec.Encode(loaded!));

            // A newer save: the first becomes previous. Damage the current, and the previous loads.
            SaveFile second = MidSeason();
            second.SavedAt += 1;
            foreach (Fixture f in SeasonEngine.Next(second.Season, Rules)) SeasonEngine.Record(second.Season, Rules, f.Id, Scripted(f));
            SeasonEngine.Advance(second.Season, Rules);
            Assert.True(slots.Save(2, second));
            Assert.Equal(12, slots.Summary(2).Matchday);
            string current = store.Values.Keys.Single(k => store.Values[k] == Convert.ToBase64String(SaveCodec.Encode(second)));
            store.Values[current] = store.Values[current].Substring(0, store.Values[current].Length / 2);
            (loaded, fromPrevious, problem) = slots.Load(2);
            Assert.NotNull(loaded);
            Assert.True(fromPrevious);
            Assert.NotEqual("", problem);
            Assert.Equal(first.SavedAt, loaded!.SavedAt);
            Assert.True(slots.Summary(2).FromPrevious);

            slots.Delete(2);
            Assert.True(slots.Summary(2).Empty);
            Assert.Null(slots.Load(2).Save);
        }

        [Fact]
        public void A_failed_write_leaves_the_slot_as_it_was()
        {
            var store = new Store();
            var slots = new SaveSlots(store);
            SaveFile first = MidSeason();
            Assert.True(slots.Save(0, first));
            var before = new Dictionary<string, string>(store.Values);
            Assert.NotEmpty(before);

            // The store refuses every write: nothing changes.
            store.Refuse = _ => true;
            SaveFile second = MidSeason();
            second.SavedAt += 5;
            Assert.False(slots.Save(0, second));
            Assert.Equal(before, store.Values);
            Assert.Equal(first.SavedAt, slots.Load(0).Save!.SavedAt);

            // A store that drops what it was given: the staged save doesn't verify, and current stays.
            var dropping = new Store();
            var dropped = new SaveSlots(dropping);
            Assert.True(dropped.Save(1, first));
            var bad = new BadStore(dropping);
            Assert.False(new SaveSlots(bad).Save(1, second));
            Assert.Equal(first.SavedAt, new SaveSlots(dropping).Load(1).Save!.SavedAt);
        }

        // A store whose writes land damaged: it says yes, and keeps half.
        private sealed class BadStore : ISaveStore
        {
            private readonly Store _inner;

            public BadStore(Store inner) => _inner = inner;

            public string? Read(string key) => _inner.Read(key);

            public bool Write(string key, string value)
            {
                _inner.Values[key] = value.Substring(0, value.Length / 2);
                return true;
            }

            public void Delete(string key) => _inner.Delete(key);
        }

        [Fact]
        public void A_season_round_trips_through_its_codec()
        {
            Season s = MidSeason().Season;
            var w = new CanonicalWriter(1 << 16);
            SeasonCodec.Write(w, s);
            byte[] bytes = w.ToArray();
            Assert.True(bytes.Length > 1000);
            Season back = SeasonCodec.Read(new CanonicalReader(bytes));
            var again = new CanonicalWriter(1 << 16);
            SeasonCodec.Write(again, back);
            Assert.Equal(bytes, again.ToArray());
        }
    }
}
