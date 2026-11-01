using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>The ball-feel surface (#24): names, ranges, and changing values only through <see cref="SimConfig.With(string, double)"/>.</summary>
    public class TunablesTests
    {
        [Fact]
        public void Every_default_is_within_its_documented_range()
        {
            var config = new SimConfig();
            Assert.All(SimConfig.Tunables, p => Assert.InRange(config.Get(p.Name), p.Min, p.Max));
        }

        [Fact]
        public void Both_ends_of_every_range_are_accepted()
        {
            foreach (TunableParameter p in SimConfig.Tunables)
            {
                Assert.Equal(p.Min, new SimConfig().With(p.Name, p.Min).Get(p.Name));
                Assert.Equal(p.Max, new SimConfig().With(p.Name, p.Max).Get(p.Name));
            }
        }

        [Fact]
        public void Values_outside_a_range_and_unknown_names_are_refused()
        {
            foreach (TunableParameter p in SimConfig.Tunables)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new SimConfig().With(p.Name, p.Max + Math.Max(1e-9, Math.Abs(p.Max) * 1e-9)));
                Assert.Throws<ArgumentOutOfRangeException>(() => new SimConfig().With(p.Name, p.Min - 1e-9));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => new SimConfig(bounceRestitution: 1.01));
            Assert.Throws<ArgumentException>(() => new SimConfig().With("tickRate", 30));
            Assert.Throws<ArgumentException>(() => new SimConfig().Get("noSuchValue"));
        }

        [Fact]
        public void With_changes_only_the_named_values_and_gives_a_new_hash()
        {
            var original = new SimConfig();
            SimConfig changed = original.With(new[]
            {
                new KeyValuePair<string, double>("rollingFriction", 0.15),
                new KeyValuePair<string, double>("kickSpeed", 25),
            });

            Assert.Equal(0.15, changed.RollingFriction);
            Assert.Equal(25, changed.KickSpeed);
            foreach (TunableParameter p in SimConfig.Tunables.Where(p => p.Name != "rollingFriction" && p.Name != "kickSpeed"))
            {
                Assert.Equal(original.Get(p.Name), changed.Get(p.Name));
            }

            Assert.Equal(original.TickRate, changed.TickRate);
            Assert.Equal(original.KickCooldownTicks, changed.KickCooldownTicks);
            Assert.Equal(original.GoalLineX, changed.GoalLineX);
            Assert.NotEqual(original.Hash, changed.Hash);
            Assert.Equal(original.Hash, original.With(Array.Empty<KeyValuePair<string, double>>()).Hash);
        }

        [Fact]
        public void Research_starting_ranges_lie_within_the_valid_ranges_and_contain_the_defaults()
        {
            var config = new SimConfig();
            TunableParameter[] researched = SimConfig.Tunables.Where(p => p.StartMin.HasValue).ToArray();

            // #24: kick speed, loft, spin/curve, bounce and ground friction have research-derived starting ranges.
            foreach (string name in new[] { "kickSpeed", "kickLoft", "kickSideSpin", "magnusCoefficient", "bounceRestitution", "bounceTangentialRetention", "rollingFriction" })
            {
                Assert.Contains(researched, p => p.Name == name);
            }

            foreach (TunableParameter p in researched)
            {
                Assert.True(p.StartMax.HasValue, p.Name);
                Assert.InRange(p.StartMin!.Value, p.Min, p.StartMax!.Value);
                Assert.InRange(p.StartMax.Value, p.StartMin.Value, p.Max);
                Assert.InRange(config.Get(p.Name), p.StartMin.Value, p.StartMax.Value);
            }
        }

        [Fact]
        public void Every_tunable_name_is_readable()
        {
            Assert.Equal(SimConfig.Tunables.Count, SimConfig.Tunables.Select(p => p.Name).Distinct().Count());
            Assert.All(SimConfig.Tunables, p => new SimConfig().Get(p.Name));
        }
    }
}
