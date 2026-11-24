using System;
using System.Collections.Generic;
using System.Linq;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #196: training. Each intensity sets development's pace, recovery's and training injuries';
    /// an injury in training has its layoff in the minor bands and never strikes the injured; Auto
    /// is the manager's choice by his XI's condition and the days to the next match; the same days
    /// give the same injuries; and the world's bytes keep each club's training.
    /// </summary>
    public class TrainingTests
    {
        private static readonly SquadRules Squad = SquadRules.Reference;
        private static readonly TrainingRules Rules = TrainingRules.Reference;

        private static (World World, Season Season) New(ulong seed = 0x196)
        {
            World w = WorldGenerator.Generate(seed);
            Season s = SeasonEngine.Start(w, 1, seed, SeasonEngine.FirstRanking(w), CompetitionRules.Reference);
            return (w, s);
        }

        private static Dictionary<int, TrainingIntensity> All(World w, TrainingIntensity t) => w.Clubs.ToDictionary(c => c.Id, _ => t);

        [Fact]
        public void Each_intensity_sets_developments_pace_recoverys_and_training_injuries()
        {
            Assert.Equal((0.8, 1.0, 1.25), (Training.Of(Rules.Growth, TrainingIntensity.Light), Training.Of(Rules.Growth, TrainingIntensity.Normal), Training.Of(Rules.Growth, TrainingIntensity.Hard)));
            Assert.Equal((1.2, 1.0, 0.8), (Training.Of(Rules.Recovery, TrainingIntensity.Light), Training.Of(Rules.Recovery, TrainingIntensity.Normal), Training.Of(Rules.Recovery, TrainingIntensity.Hard)));
            Assert.Equal((0.6, 1.0, 1.5), (Training.Of(Rules.Injury, TrainingIntensity.Light), Training.Of(Rules.Injury, TrainingIntensity.Normal), Training.Of(Rules.Injury, TrainingIntensity.Hard)));
            Assert.Equal(Training.Of(Rules.Growth, TrainingIntensity.Normal), Training.Of(Rules.Growth, TrainingIntensity.Auto));

            // Three days' recovery from 40, by each club's intensity; no injuries in training here.
            (World w, Season s) = New();
            var none = new TrainingRules { InjuryPerDay = 0 };
            foreach (Player p in w.Players) p.Fitness = 40;
            var intensity = All(w, TrainingIntensity.Normal);
            intensity[w.Clubs[0].Id] = TrainingIntensity.Light;
            intensity[w.Clubs[1].Id] = TrainingIntensity.Hard;
            Assert.Empty(Training.Pass(w, s, 3, intensity, Squad, none));
            int Expected(double factor) => Math.Min(100, 40 + (int)Math.Round(3 * Squad.ConditionCostPerMatch / Squad.RecoveryDays * factor));
            Assert.All(w.Players, p => Assert.Equal(Expected(Training.Of(Rules.Recovery, intensity[p.ClubId])), p.Fitness));
            Assert.True(Expected(1.2) > Expected(1.0) && Expected(1.0) > Expected(0.8));
        }

        [Fact]
        public void A_training_injury_is_likelier_the_harder_the_training_its_layoff_in_the_minor_bands_and_never_for_the_injured()
        {
            var rules = new TrainingRules { InjuryPerDay = 0.02 };
            var counts = new Dictionary<TrainingIntensity, int>();
            foreach (TrainingIntensity t in new[] { TrainingIntensity.Light, TrainingIntensity.Normal, TrainingIntensity.Hard })
            {
                (World w, Season s) = New();
                List<Player> hurt = w.Players.Where((p, i) => i % 10 == 0).ToList();
                foreach (Player p in hurt) p.InjuredDays = 30;
                List<TrainingInjury> injuries = Training.Pass(w, s, 7, All(w, t), Squad, rules);
                counts[t] = injuries.Count;

                // Expected: each fit footballer's chance a day over seven days.
                int fit = w.Players.Count - hurt.Count;
                double expected = fit * (1 - Math.Pow(1 - rules.InjuryPerDay * Training.Of(rules.Injury, t), 7));
                Assert.InRange(injuries.Count, expected * 0.75, expected * 1.25);
                Assert.All(injuries, i => Assert.InRange(i.Days, 1, Squad.BandDays[2][1]));
                Assert.DoesNotContain(injuries, i => hurt.Any(p => p.Id == i.Player));
                Assert.All(injuries, i => Assert.True(w.Players.Single(p => p.Id == i.Player).InjuredDays <= i.Days));
                Assert.Equal(injuries.Count, injuries.Select(i => i.Player).Distinct().Count());
            }

            Assert.True(counts[TrainingIntensity.Light] < counts[TrainingIntensity.Normal] && counts[TrainingIntensity.Normal] < counts[TrainingIntensity.Hard], string.Join(", ", counts));
        }

        [Fact]
        public void Auto_is_the_managers_choice_by_his_xis_condition_and_the_days_to_his_next_match()
        {
            Assert.Equal(TrainingIntensity.Light, Training.Choose(84.9, 7));
            Assert.Equal(TrainingIntensity.Light, Training.Choose(50, 14));
            Assert.Equal(TrainingIntensity.Normal, Training.Choose(85, 3));
            Assert.Equal(TrainingIntensity.Normal, Training.Choose(96.9, 7));
            Assert.Equal(TrainingIntensity.Hard, Training.Choose(97, 7));
            Assert.Equal(TrainingIntensity.Hard, Training.Choose(100, 14));
            Assert.Equal(TrainingIntensity.Normal, Training.Choose(97, 6));
            Assert.Equal(TrainingIntensity.Normal, Training.Choose(100, 3));
        }

        [Fact]
        public void The_same_days_twice_give_the_same_injuries()
        {
            var rules = new TrainingRules { InjuryPerDay = 0.01 };
            (List<TrainingInjury>, byte[]) Run()
            {
                (World w, Season s) = New(0x1961);
                var injuries = new List<TrainingInjury>();
                for (int day = 0; day < 4; day++) injuries.AddRange(Training.Pass(w, s, 4, All(w, TrainingIntensity.Hard), Squad, rules));
                return (injuries, WorldCodec.Encode(w));
            }

            (List<TrainingInjury> a, byte[] wa) = Run();
            (List<TrainingInjury> b, byte[] wb) = Run();
            Assert.NotEmpty(a);
            Assert.Equal(a.Select(i => (i.Player, i.Days)), b.Select(i => (i.Player, i.Days)));
            Assert.Equal(wa, wb);
        }

        [Fact]
        public void The_worlds_bytes_keep_each_clubs_training_and_a_format_6_world_reads_auto()
        {
            (World w, _) = New();
            Assert.Equal(9, WorldCodec.FormatVersion);
            byte[] plain = WorldCodec.Encode(w);

            // Format 6 is format 7 with every club on Auto, less its section's empty count.
            var six = new CanonicalWriter(plain.Length);
            six.WriteBytes(plain[..4]);
            six.WriteInt32(6);
            six.WriteBytes(plain[8..^(4 + Formats.From8(w))]);
            World old = WorldCodec.Decode(six.ToArray());
            Assert.All(old.Clubs, c => Assert.Equal(TrainingIntensity.Auto, c.Training));
            Assert.Equal(plain, WorldCodec.Encode(old));

            w.Clubs[0].Training = TrainingIntensity.Hard;
            w.Clubs[7].Training = TrainingIntensity.Light;
            w.Clubs[9].Training = TrainingIntensity.Normal;
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            Assert.Equal(w.Clubs.Select(c => c.Training), back.Clubs.Select(c => c.Training));
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));
        }
    }
}
