using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Career;
using Eleven.Simulation.Encoding;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #170: the result contract's bytes, and a career saved and loaded. A played knockout's result
    /// reads back text for text, metrics included; and a career saved mid-season plays on, loaded,
    /// exactly as the one that never stopped.
    /// </summary>
    public class CareerSaveTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        [Fact]
        public void Five_slots_of_a_seasons_end_fit_the_smallest_browsers_storage()
        {
            // iOS Safari keeps about 2.5 million characters a page (WebKit stores UTF-16 in 5 MB);
            // each slot holds a current and a previous save, and one is staged while saving.
            World w = CareerSession.NewWorld(0x170);
            CareerSession s = CareerSession.Start(w, 0x170, w.Clubs[0].Id, CareerRole.ManagerOnly, Config);
            while (!s.SeasonOver) s.Simulate();
            byte[] bytes = SaveCodec.Encode(s.ToSave(638_700_000_000_000_000));
            int chars = Convert.ToBase64String(bytes).Length;
            Assert.True(chars * (2 * SaveSlots.Count + 1) < 2_500_000 / 2, $"a season's end is {chars} characters");
            SaveFile back = SaveCodec.Decode(bytes);
            Assert.Equal(s.Season.Results.Count, back.Season.Results.Count);
        }

        [Fact]
        public void A_played_result_reads_back_text_for_text_with_its_metrics()
        {
            World w = WorldGenerator.Generate(0x157);
            MatchResult r = CareerSquads.Play(Config.With("knockout", 1), w, w.Clubs[2].Id, w.Clubs[33].Id, 10, 0x170);
            Assert.True(r.HomeMetrics.Passes + r.AwayMetrics.Passes > 0);
            var writer = new CanonicalWriter(4096);
            MatchResultCodec.Write(writer, r);
            byte[] bytes = writer.ToArray();
            Assert.True(bytes.Length > 50, $"{bytes.Length} bytes");
            MatchResult back = MatchResultCodec.Read(new CanonicalReader(bytes));
            Assert.Equal(r.ToText(), back.ToText());
            Assert.Equal(r.HomeMetrics.Ppda, back.HomeMetrics.Ppda);
            Assert.Equal(r.AwayMetrics.Xg, back.AwayMetrics.Xg);
            Assert.Equal(r.Goals.Select(g => (g.Team, g.Scorer, g.Half, g.ClockSeconds)), back.Goals.Select(g => (g.Team, g.Scorer, g.Half, g.ClockSeconds)));
            var again = new CanonicalWriter(4096);
            MatchResultCodec.Write(again, back);
            Assert.Equal(bytes, again.ToArray());
        }

        [Fact]
        public void A_career_saved_mid_season_plays_on_loaded_as_it_would_have()
        {
            World w = CareerSession.NewWorld(0x170);
            Assert.NotEmpty(w.Clubs);
            CareerSession live = CareerSession.Start(w, 0x170, QuickMatch.Clubs(w)[7].Id, CareerRole.ManagerOnly, Config);
            for (int i = 0; i < 9; i++) live.Simulate();

            SaveFile save = live.ToSave(1234);
            Assert.Equal((0x170UL, live.Club, (byte)1, 1234L), (save.Seed, save.Club, save.Role, save.SavedAt));
            CareerSession loaded = CareerSession.FromSave(SaveCodec.Decode(SaveCodec.Encode(save)), Config);
            Assert.Equal((live.Seed, live.Club, live.Role, live.Season.Matchday), (loaded.Seed, loaded.Club, loaded.Role, loaded.Season.Matchday));

            for (int i = 0; i < 8; i++)
            {
                live.Simulate();
                loaded.Simulate();
            }

            Assert.Equal(live.Season.Results.OrderBy(x => x.Key).Select(x => x.Value.ToText()), loaded.Season.Results.OrderBy(x => x.Key).Select(x => x.Value.ToText()));
            Assert.Equal(live.Table().Select(r => (r.Club, r.Points)), loaded.Table().Select(r => (r.Club, r.Points)));
            Assert.Equal(WorldCodec.Encode(live.World), WorldCodec.Encode(loaded.World));
        }
    }
}
