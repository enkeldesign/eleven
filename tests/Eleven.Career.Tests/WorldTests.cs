using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>
    /// #157: the fictional world. A seed gives the same world byte for byte; the world has three
    /// divisions of twelve clubs with 23-man squads and a manager each; every rating is 1–20; ids
    /// are unique; the divisions are ordered by strength; and a world round-trips through its codec.
    /// </summary>
    public class WorldTests
    {
        private static readonly World Reference = WorldGenerator.Generate(0x157);

        private static bool Keeper(Player p) => p.Positions.Count > 0 && p.Positions[0] == Position.Goalkeeper;

        private static readonly int[] KeeperAttributes =
            new[] { "keeperReflexes", "keeperHandling", "keeperPositioning" }.Select(PlayerAttributes.Index).ToArray();

        // An outfielder's level: the mean of every attribute but the keepers'.
        private static double OutfieldLevel(Player p) =>
            Enumerable.Range(0, PlayerAttributes.Count).Where(i => !KeeperAttributes.Contains(i)).Average(i => p.Attributes[i]);

        [Fact]
        public void A_seed_gives_the_same_world_byte_for_byte_and_another_seed_another()
        {
            byte[] a = WorldCodec.Encode(WorldGenerator.Generate(0x157));
            byte[] b = WorldCodec.Encode(WorldGenerator.Generate(0x157));
            byte[] c = WorldCodec.Encode(WorldGenerator.Generate(0x158));
            Assert.True(a.Length > 1000, $"{a.Length} bytes");
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void Three_divisions_of_twelve_clubs_each_with_a_23_man_squad_and_a_manager()
        {
            World w = Reference;
            Assert.Equal(WorldGenerator.Divisions * WorldGenerator.ClubsPerDivision, w.Clubs.Count);
            for (int d = 1; d <= WorldGenerator.Divisions; d++) Assert.Equal(WorldGenerator.ClubsPerDivision, w.Clubs.Count(c => c.Division == d));
            Assert.Equal(w.Clubs.Count * WorldGenerator.SquadSize, w.Players.Count);
            Assert.Equal(w.Clubs.Count, w.Managers.Count);
            foreach (Club club in w.Clubs)
            {
                Assert.Equal(WorldGenerator.SquadSize, club.Squad.Count);
                List<Player> squad = club.Squad.Select(id => w.Players.Single(p => p.Id == id)).ToList();
                Assert.All(squad, p => Assert.Equal(club.Id, p.ClubId));
                Assert.Equal(WorldGenerator.KeepersPerSquad, squad.Count(Keeper));
                Manager m = w.Managers.Single(x => x.Id == club.ManagerId);
                Assert.Equal(club.Id, m.ClubId);
                Assert.False(string.IsNullOrWhiteSpace(club.Name));
                Assert.InRange(club.ShortName.Length, 2, 4);
            }
        }

        [Fact]
        public void Every_rating_is_between_1_and_20_and_every_player_has_positions()
        {
            Assert.NotEmpty(Reference.Players);
            foreach (Player p in Reference.Players)
            {
                Assert.Equal(PlayerAttributes.Count, p.Attributes.Length);
                Assert.All(p.Attributes, v => Assert.InRange(v, PlayerAttributes.Min, PlayerAttributes.Max));
                Assert.InRange(p.Potential, PlayerAttributes.Min, PlayerAttributes.Max);
                Assert.InRange(p.Age, 16, 40);
                Assert.NotEmpty(p.Positions);
                Assert.False(string.IsNullOrWhiteSpace(p.LastName));
            }

            foreach (Manager m in Reference.Managers)
            {
                Assert.Equal(ManagerCompetences.Count, m.Competences.Length);
                Assert.All(m.Competences, v => Assert.InRange(v, 1, 20));
                Assert.InRange(m.Temperament, 1, 20);
            }

            foreach (Club c in Reference.Clubs) Assert.InRange(c.Reputation, 1, 20);
        }

        [Fact]
        public void Ids_and_club_names_are_unique()
        {
            Assert.NotEmpty(Reference.Clubs);
            Assert.Equal(Reference.Clubs.Count, Reference.Clubs.Select(c => c.Id).Distinct().Count());
            Assert.Equal(Reference.Players.Count, Reference.Players.Select(p => p.Id).Distinct().Count());
            Assert.Equal(Reference.Managers.Count, Reference.Managers.Select(m => m.Id).Distinct().Count());
            Assert.Equal(Reference.Clubs.Count, Reference.Clubs.Select(c => c.Name).Distinct().Count());
            Assert.Equal(Reference.Clubs.Count, Reference.Clubs.Select(c => c.ShortName).Distinct().Count());
        }

        [Fact]
        public void The_divisions_are_ordered_by_strength_and_keepers_are_keepers()
        {
            Assert.Equal(WorldGenerator.Divisions * WorldGenerator.ClubsPerDivision, Reference.Clubs.Count);
            double Level(int division) => Reference.Clubs.Where(c => c.Division == division)
                .SelectMany(c => c.Squad).Select(id => Reference.Players.Single(p => p.Id == id)).Where(p => !Keeper(p)).Average(OutfieldLevel);
            double top = Level(1), middle = Level(2), bottom = Level(3);
            Assert.True(top > middle + 0.5 && middle > bottom + 0.5, $"division levels {top:F2}, {middle:F2}, {bottom:F2}");

            double keepers = Reference.Players.Where(Keeper).Average(p => KeeperAttributes.Average(i => p.Attributes[i]));
            double others = Reference.Players.Where(p => !Keeper(p)).Average(p => KeeperAttributes.Average(i => p.Attributes[i]));
            Assert.True(keepers > others + 6, $"keepers' keeping {keepers:F1}, outfielders' {others:F1}");
        }

        [Fact]
        public void A_world_round_trips_through_its_codec()
        {
            Assert.True(Reference.Players.Count > 100, $"{Reference.Players.Count} players");
            byte[] bytes = WorldCodec.Encode(Reference);
            World back = WorldCodec.Decode(bytes);
            Assert.Equal(bytes, WorldCodec.Encode(back));
            Assert.Equal(Reference.Players.Count, back.Players.Count);
            Assert.Equal(Reference.Clubs[5].Name, back.Clubs[5].Name);
            Assert.Equal(Reference.Players[100].Attributes, back.Players[100].Attributes);
            Assert.Throws<FormatException>(() => WorldCodec.Decode(bytes.Take(bytes.Length / 2).ToArray()));
        }

        [Fact]
        public void Every_club_wears_kit_tokens_the_design_system_resolves_with_shirt_and_shorts_apart()
        {
            // Codex on #158: colours come from semantic design tokens (AGENTS.md). The world
            // stores the token; the design system's kit tier holds the colour.
            var tokens = Eleven.Presentation.Kit.All.Select(t => t.Name).ToList();
            foreach (KitColour k in Enum.GetValues(typeof(KitColour))) Assert.Contains(k.ToString(), tokens);
            Assert.All(Reference.Clubs, c => Assert.NotEqual(c.Shirt, c.Shorts));
            Assert.True(Reference.Clubs.Select(c => c.Shirt).Distinct().Count() >= 6, "the clubs' shirts vary");
        }

        // ADR 0005: the career reaches into the simulation only for the result contract, the
        // randomness, the encoding, the portable maths and the formations' names. Read from the
        // career assembly's own type references, so any new use of a simulation type fails here.
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Eleven.Simulation.MatchResult", "Eleven.Simulation.ResultGoal", "Eleven.Simulation.Formation",
            "Eleven.Simulation.Randomness.CounterRng", "Eleven.Simulation.Randomness.RandomStream",
            "Eleven.Simulation.Encoding.CanonicalWriter", "Eleven.Simulation.Encoding.CanonicalReader", "Eleven.Simulation.Encoding.Fnv1a64",
            // #170: the result contract's own codec, for a season's saved results.
            "Eleven.Simulation.Encoding.MatchResultCodec",
            "Eleven.Simulation.Maths.SimMath",
        };

        [Fact]
        public void The_career_uses_only_the_simulations_contract_randomness_encoding_and_maths()
        {
            string path = typeof(World).Assembly.Location;
            using var stream = System.IO.File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader md = pe.GetMetadataReader();
            var used = new List<string>();
            foreach (TypeReferenceHandle h in md.TypeReferences)
            {
                TypeReference t = md.GetTypeReference(h);
                if (t.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
                AssemblyReference a = md.GetAssemblyReference((AssemblyReferenceHandle)t.ResolutionScope);
                if (md.GetString(a.Name) != "Eleven.Simulation") continue;
                used.Add($"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}");
            }

            Assert.Contains("Eleven.Simulation.Randomness.CounterRng", used);
            Assert.Empty(used.Where(u => !Allowed.Contains(u)));
        }
    }
}
