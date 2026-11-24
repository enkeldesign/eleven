using System;
using System.Linq;
using Eleven.Simulation;
using Xunit;

namespace Eleven.Career.Tests
{
    /// <summary>#191: a manager's memory of opponents is saved with the world, at format 5; a format-4 world reads with none.</summary>
    public class MemoryCodecTests
    {
        [Fact]
        public void The_worlds_bytes_keep_the_managers_memories_and_a_format_4_world_reads_with_none()
        {
            World w = WorldGenerator.Generate(0x191);
            Assert.Equal(8, WorldCodec.FormatVersion);
            byte[] plain = WorldCodec.Encode(w);

            // Format 4 is format 7 with no memories, development or training, less those sections' empty counts and the next id (#194, #196).
            var four = new Eleven.Simulation.Encoding.CanonicalWriter(plain.Length);
            four.WriteBytes(plain[..4]);
            four.WriteInt32(4);
            four.WriteBytes(plain[8..^(20 + Formats.Money(w))]);
            World old = WorldCodec.Decode(four.ToArray());
            Assert.All(old.Managers, m => Assert.Empty(m.Memory));
            Assert.Equal(plain, WorldCodec.Encode(old));

            Manager a = w.Managers[0], b = w.Managers[5];
            a.Memory.Add(new Meeting { Opponent = w.Clubs[5].Id, Season = 1, Matchday = 7, GoalsFor = 1, GoalsAgainst = 2, LateFor = 0, LateAgainst = 1, OpponentFormation = Formation.FourThreeThree });
            a.Memory.Add(new Meeting { Opponent = w.Clubs[9].Id, Season = 2, Matchday = 3, GoalsFor = 3, GoalsAgainst = 0, LateFor = 2, Full = true, Ppda = 7.25, OpponentPpda = 11.5, Possession = 0.5625 });
            b.Memory.Add(new Meeting { Opponent = w.Clubs[0].Id, Season = 1, Matchday = 7, GoalsFor = 2, GoalsAgainst = 1, LateFor = 1, OpponentFormation = Formation.ThreeFiveTwo });
            World back = WorldCodec.Decode(WorldCodec.Encode(w));
            string Of(Manager m) => string.Join(";", m.Memory.Select(x => $"{x.Opponent},{x.Season},{x.Matchday},{x.GoalsFor},{x.GoalsAgainst},{x.LateFor},{x.LateAgainst},{x.OpponentFormation},{x.Full},{x.Ppda},{x.OpponentPpda},{x.Possession}"));
            Assert.Equal(w.Managers.Select(Of), back.Managers.Select(Of));
            Assert.Equal(WorldCodec.Encode(w), WorldCodec.Encode(back));

            // A meeting with a club that isn't in the world is refused.
            a.Memory.Add(new Meeting { Opponent = 999_999, Season = 1, Matchday = 1 });
            Assert.Throws<FormatException>(() => WorldCodec.Decode(WorldCodec.Encode(w)));
        }
    }
}
