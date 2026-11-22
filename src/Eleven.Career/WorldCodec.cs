#nullable enable
using System;
using Eleven.Simulation;
using Eleven.Simulation.Encoding;

namespace Eleven.Career
{
    /// <summary>
    /// A world as plain, versioned bytes (#157), and back: the start of M6.07's saves. Magic
    /// <c>ELWD</c>, the format version, then the seed and every club, footballer and manager in
    /// order, with the attribute and competence counts written so that a later format can migrate
    /// them (INV-7). Canonical: the same world always encodes to the same bytes.
    /// </summary>
    public static class WorldCodec
    {
        public const int Magic = 0x454C5744; // "ELWD"
        public const int FormatVersion = 1;

        public static byte[] Encode(World world)
        {
            var w = new CanonicalWriter(64 * 1024);
            w.WriteInt32(Magic);
            w.WriteInt32(FormatVersion);
            w.WriteUInt64(world.Seed);

            w.WriteInt32(world.Clubs.Count);
            foreach (Club c in world.Clubs)
            {
                w.WriteInt32(c.Id);
                w.WriteString(c.Name);
                w.WriteString(c.ShortName);
                w.WriteByte((byte)c.Shirt);
                w.WriteByte((byte)c.Shorts);
                w.WriteInt32(c.Reputation);
                w.WriteInt32(c.Division);
                w.WriteInt32(c.ManagerId);
                w.WriteInt32(c.Squad.Count);
                foreach (int id in c.Squad) w.WriteInt32(id);
            }

            w.WriteInt32(PlayerAttributes.Count);
            w.WriteInt32(world.Players.Count);
            foreach (Player p in world.Players)
            {
                w.WriteInt32(p.Id);
                w.WriteString(p.FirstName);
                w.WriteString(p.LastName);
                w.WriteInt32(p.Age);
                w.WriteInt32(p.ClubId);
                w.WriteInt32(p.Positions.Count);
                foreach (Position position in p.Positions) w.WriteByte((byte)position);
                w.WriteByte((byte)p.Foot);
                foreach (int a in p.Attributes) w.WriteByte((byte)a);
                w.WriteInt32(p.Potential);
                w.WriteInt32(p.Fitness);
                w.WriteInt32(p.Morale);
                w.WriteInt32(p.InjuredDays);
            }

            w.WriteInt32(ManagerCompetences.Count);
            w.WriteInt32(world.Managers.Count);
            foreach (Manager m in world.Managers)
            {
                w.WriteInt32(m.Id);
                w.WriteString(m.FirstName);
                w.WriteString(m.LastName);
                w.WriteInt32(m.Age);
                w.WriteInt32(m.ClubId);
                foreach (int c in m.Competences) w.WriteByte((byte)c);
                w.WriteByte((byte)m.PreferredFormation);
                w.WriteInt32(m.Temperament);
                w.WriteInt32(m.Reputation);
            }

            return w.ToArray();
        }

        /// <summary>The world in <paramref name="bytes"/>; a <see cref="FormatException"/> for anything that isn't a whole world of this format.</summary>
        public static World Decode(byte[] bytes)
        {
            try
            {
                return Read(new CanonicalReader(bytes));
            }
            catch (Exception e) when (!(e is FormatException))
            {
                throw new FormatException("Not a whole world: " + e.Message, e);
            }
        }

        private static World Read(CanonicalReader r)
        {
            if (r.ReadInt32() != Magic) throw new FormatException("Not a world: wrong magic.");
            int version = r.ReadInt32();
            if (version != FormatVersion) throw new FormatException($"World format {version}; this build reads {FormatVersion}.");
            var world = new World { Seed = r.ReadUInt64() };

            int clubs = Count(r.ReadInt32());
            for (int i = 0; i < clubs; i++)
            {
                var c = new Club
                {
                    Id = r.ReadInt32(),
                    Name = r.ReadString(),
                    ShortName = r.ReadString(),
                    Shirt = (KitColour)r.ReadByte(),
                    Shorts = (KitColour)r.ReadByte(),
                    Reputation = r.ReadInt32(),
                    Division = r.ReadInt32(),
                    ManagerId = r.ReadInt32(),
                };
                int squad = Count(r.ReadInt32());
                for (int s = 0; s < squad; s++) c.Squad.Add(r.ReadInt32());
                world.Clubs.Add(c);
            }

            if (r.ReadInt32() != PlayerAttributes.Count) throw new FormatException("The world's attributes differ from this build's.");
            int players = Count(r.ReadInt32());
            for (int i = 0; i < players; i++)
            {
                var p = new Player
                {
                    Id = r.ReadInt32(),
                    FirstName = r.ReadString(),
                    LastName = r.ReadString(),
                    Age = r.ReadInt32(),
                    ClubId = r.ReadInt32(),
                };
                int positions = Count(r.ReadInt32());
                for (int k = 0; k < positions; k++) p.Positions.Add((Position)r.ReadByte());
                p.Foot = (Foot)r.ReadByte();
                for (int a = 0; a < PlayerAttributes.Count; a++) p.Attributes[a] = r.ReadByte();
                p.Potential = r.ReadInt32();
                p.Fitness = r.ReadInt32();
                p.Morale = r.ReadInt32();
                p.InjuredDays = r.ReadInt32();
                world.Players.Add(p);
            }

            if (r.ReadInt32() != ManagerCompetences.Count) throw new FormatException("The world's manager competences differ from this build's.");
            int managers = Count(r.ReadInt32());
            for (int i = 0; i < managers; i++)
            {
                var m = new Manager
                {
                    Id = r.ReadInt32(),
                    FirstName = r.ReadString(),
                    LastName = r.ReadString(),
                    Age = r.ReadInt32(),
                    ClubId = r.ReadInt32(),
                };
                for (int c = 0; c < ManagerCompetences.Count; c++) m.Competences[c] = r.ReadByte();
                m.PreferredFormation = (Formation)r.ReadByte();
                m.Temperament = r.ReadInt32();
                m.Reputation = r.ReadInt32();
                world.Managers.Add(m);
            }

            if (!r.AtEnd) throw new FormatException("Bytes after the world.");
            return world;
        }

        // A count read from the bytes, refused when it can't be one.
        private static int Count(int n) => n < 0 || n > 1_000_000 ? throw new FormatException($"Invalid count {n}.") : n;
    }
}
