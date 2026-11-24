#nullable enable
using System;
using System.Collections.Generic;
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
        public const int FormatVersion = 8;

        /// <summary>Format 1 (#157), read as format 2 with nobody banned or cautioned.</summary>
        public const int FirstFormat = 1;

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

            // Format 2 (#176): each footballer with a ban or a caution, in the world's order.
            var marked = new List<Player>();
            foreach (Player p in world.Players)
            {
                if (p.LeagueBan != 0 || p.CupBan != 0 || p.LeagueCautions != 0 || p.CupCautions != 0) marked.Add(p);
            }

            w.WriteInt32(marked.Count);
            foreach (Player p in marked)
            {
                w.WriteInt32(p.Id);
                w.WriteInt32(p.LeagueBan);
                w.WriteInt32(p.CupBan);
                w.WriteInt32(p.LeagueCautions);
                w.WriteInt32(p.CupCautions);
            }

            // Format 3 (#179): each club with picks, its id, then its picks. Format 4 (#180) follows
            // with each manager's philosophy.
            var picking = world.Clubs.FindAll(c => c.Picks.Count > 0);
            w.WriteInt32(picking.Count);
            foreach (Club c in picking)
            {
                w.WriteInt32(c.Id);
                w.WriteInt32(c.Picks.Count);
                foreach (int id in c.Picks) w.WriteInt32(id);
            }

            var minded = world.Managers.FindAll(m => m.Pressing != 0 || m.Depth != 0);
            w.WriteInt32(minded.Count);
            foreach (Manager m in minded)
            {
                w.WriteInt32(m.Id);
                w.WriteByte((byte)m.Pressing);
                w.WriteByte((byte)m.Depth);
            }

            // Format 5 (#191): each manager with memories, his id, then his meetings grouped by
            // opponent: its id and count, then each meeting in a few bytes, the season in two, the
            // formation and whether it was played in full in one, and its metrics only if it was.
            var remembering = world.Managers.FindAll(m => m.Memory.Count > 0);
            w.WriteInt32(remembering.Count);
            foreach (Manager m in remembering)
            {
                w.WriteInt32(m.Id);
                var opponents = new List<int>();
                foreach (Meeting x in m.Memory)
                {
                    if (!opponents.Contains(x.Opponent)) opponents.Add(x.Opponent);
                }

                w.WriteInt32(opponents.Count);
                foreach (int opponent in opponents)
                {
                    List<Meeting> met = m.Memory.FindAll(x => x.Opponent == opponent);
                    w.WriteInt32(opponent);
                    w.WriteByte(Small(met.Count));
                    foreach (Meeting x in met)
                    {
                        int season = Math.Clamp(x.Season, 0, ushort.MaxValue);
                        w.WriteByte((byte)(season >> 8));
                        w.WriteByte((byte)season);
                        w.WriteByte(Small(x.Matchday));
                        w.WriteByte(Small(x.GoalsFor));
                        w.WriteByte(Small(x.GoalsAgainst));
                        w.WriteByte(Small(x.LateFor));
                        w.WriteByte(Small(x.LateAgainst));
                        w.WriteByte((byte)((byte)x.OpponentFormation | (x.Full ? FullMatch : 0)));
                        if (!x.Full) continue;
                        w.WriteDouble(x.Ppda, "ppda");
                        w.WriteDouble(x.OpponentPpda, "opponentPpda");
                        w.WriteDouble(x.Possession, "possession");
                    }
                }
            }

            // Format 6 (#194): the next footballer's id; each footballer with development, his id,
            // his progress, starts, this season's and last season's points and the season he
            // joined; then those who retired at the last season's turn.
            w.WriteInt32(world.NextPlayerId);
            var developing = world.Players.FindAll(p => p.Progress != 0 || p.Starts != 0 || p.SeasonPoints != 0 || p.LastSeasonPoints != 0 || p.Joined != 0);
            w.WriteInt32(developing.Count);
            foreach (Player p in developing)
            {
                w.WriteInt32(p.Id);
                w.WriteDouble(p.Progress, "progress");
                w.WriteByte(Small(p.Starts));
                WriteShort(w, p.SeasonPoints);
                WriteShort(w, p.LastSeasonPoints);
                WriteShort(w, p.Joined);
            }

            w.WriteInt32(world.LastRetired.Count);
            foreach (Retired x in world.LastRetired)
            {
                w.WriteInt32(x.Id);
                w.WriteInt32(x.ClubId);
                w.WriteByte(Small(x.Age));
                w.WriteString(x.FirstName);
                w.WriteString(x.LastName);
                w.WriteByte((byte)x.Position);
            }

            // Format 7 (#196): each club not on Auto, its id and its training.
            var training = world.Clubs.FindAll(c => c.Training != TrainingIntensity.Auto);
            w.WriteInt32(training.Count);
            foreach (Club c in training)
            {
                w.WriteInt32(c.Id);
                w.WriteByte((byte)c.Training);
            }

            // Format 8 (#198): each club in order, its balance and this season's and last season's
            // ledgers; then each footballer in order, his weekly wage in two bytes.
            w.WriteInt32(world.Clubs.Count);
            foreach (Club c in world.Clubs)
            {
                w.WriteInt32(Money(c.Balance));
                foreach (long a in c.Season.Amounts) w.WriteInt32(Money(a));
                foreach (long a in c.LastSeason.Amounts) w.WriteInt32(Money(a));
            }

            w.WriteInt32(world.Players.Count);
            foreach (Player p in world.Players) WriteShort(w, p.Wage);

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
            if (version < FirstFormat || version > FormatVersion) throw new FormatException($"World format {version}; this build reads {FirstFormat} to {FormatVersion}.");
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

            if (version >= 2)
            {
                var byId = new Dictionary<int, Player>(world.Players.Count);
                foreach (Player p in world.Players) byId[p.Id] = p;
                int marked = Count(r.ReadInt32());
                for (int i = 0; i < marked; i++)
                {
                    int id = r.ReadInt32();
                    if (!byId.TryGetValue(id, out Player? p)) throw new FormatException($"A ban for footballer {id}, who isn't in the world.");
                    p.LeagueBan = NonNegative(r.ReadInt32());
                    p.CupBan = NonNegative(r.ReadInt32());
                    p.LeagueCautions = NonNegative(r.ReadInt32());
                    p.CupCautions = NonNegative(r.ReadInt32());
                }
            }

            if (version >= 3)
            {
                var known = new HashSet<int>();
                foreach (Player p in world.Players) known.Add(p.Id);
                int picking = Count(r.ReadInt32());
                for (int i = 0; i < picking; i++)
                {
                    int id = r.ReadInt32();
                    Club? c = world.Clubs.Find(x => x.Id == id) ?? throw new FormatException($"Picks for club {id}, which isn't in the world.");
                    int n = Count(r.ReadInt32());
                    for (int k = 0; k < n; k++)
                    {
                        int pick = r.ReadInt32();
                        if (!known.Contains(pick)) throw new FormatException($"Club {id} picks footballer {pick}, who isn't in the world.");
                        c.Picks.Add(pick);
                    }
                }
            }

            if (version >= 4)
            {
                int minded = Count(r.ReadInt32());
                for (int i = 0; i < minded; i++)
                {
                    int id = r.ReadInt32();
                    Manager m = world.Managers.Find(x => x.Id == id) ?? throw new FormatException($"A philosophy for manager {id}, who isn't in the world.");
                    m.Pressing = r.ReadByte();
                    m.Depth = r.ReadByte();
                    if (m.Pressing < 1 || m.Pressing > 20 || m.Depth < 1 || m.Depth > 20) throw new FormatException($"Manager {id}'s philosophy is out of range.");
                }
            }

            if (version >= 5)
            {
                int remembering = Count(r.ReadInt32());
                var clubIds = new HashSet<int>();
                foreach (Club c in world.Clubs) clubIds.Add(c.Id);
                for (int i = 0; i < remembering; i++)
                {
                    int id = r.ReadInt32();
                    Manager m = world.Managers.Find(x => x.Id == id) ?? throw new FormatException($"Memories for manager {id}, who isn't in the world.");
                    int opponents = Count(r.ReadInt32());
                    for (int o = 0; o < opponents; o++)
                    {
                        int opponent = r.ReadInt32();
                        if (!clubIds.Contains(opponent)) throw new FormatException($"Manager {id} remembers club {opponent}, which isn't in the world.");
                        int meetings = r.ReadByte();
                        for (int k = 0; k < meetings; k++)
                        {
                            var x = new Meeting { Opponent = opponent, Season = (r.ReadByte() << 8) | r.ReadByte(), Matchday = r.ReadByte(), GoalsFor = r.ReadByte(), GoalsAgainst = r.ReadByte(), LateFor = r.ReadByte(), LateAgainst = r.ReadByte() };
                            byte packed = r.ReadByte();
                            var formation = (Formation)(packed & ~FullMatch);
                            if (!Enum.IsDefined(typeof(Formation), formation)) throw new FormatException($"Manager {id} remembers an unknown formation {(int)formation}.");
                            x.OpponentFormation = formation;
                            x.Full = (packed & FullMatch) != 0;
                            if (x.Full)
                            {
                                x.Ppda = r.ReadDouble("ppda");
                                x.OpponentPpda = r.ReadDouble("opponentPpda");
                                x.Possession = r.ReadDouble("possession");
                                if (x.Ppda < 0 || x.OpponentPpda < 0 || x.Possession < 0 || x.Possession > 1) throw new FormatException($"Manager {id}'s memory has metrics out of range.");
                            }

                            m.Memory.Add(x);
                        }
                    }
                }
            }

            int highest = 0;
            foreach (Player p in world.Players) highest = Math.Max(highest, p.Id);
            if (version >= 6)
            {
                world.NextPlayerId = r.ReadInt32();
                if (world.NextPlayerId <= highest) throw new FormatException($"The next footballer's id {world.NextPlayerId} isn't past every footballer's.");
                var byId = new Dictionary<int, Player>(world.Players.Count);
                foreach (Player p in world.Players) byId[p.Id] = p;
                int developing = Count(r.ReadInt32());
                for (int i = 0; i < developing; i++)
                {
                    int id = r.ReadInt32();
                    if (!byId.TryGetValue(id, out Player? p)) throw new FormatException($"Development for footballer {id}, who isn't in the world.");
                    p.Progress = r.ReadDouble("progress");
                    if (p.Progress < 0 || p.Progress >= 1) throw new FormatException($"Footballer {id}'s progress {p.Progress} isn't in [0, 1).");
                    p.Starts = r.ReadByte();
                    p.SeasonPoints = ReadShort(r);
                    p.LastSeasonPoints = ReadShort(r);
                    p.Joined = ReadShort(r);
                }

                int retired = Count(r.ReadInt32());
                var clubIds = new HashSet<int>();
                foreach (Club c in world.Clubs) clubIds.Add(c.Id);
                for (int i = 0; i < retired; i++)
                {
                    var x = new Retired { Id = r.ReadInt32(), ClubId = r.ReadInt32(), Age = r.ReadByte(), FirstName = r.ReadString(), LastName = r.ReadString() };
                    byte position = r.ReadByte();
                    if (!clubIds.Contains(x.ClubId)) throw new FormatException($"Retired footballer {x.Id}'s club {x.ClubId} isn't in the world.");
                    if (!Enum.IsDefined(typeof(Position), (Position)position)) throw new FormatException($"Retired footballer {x.Id}'s position {position} is unknown.");
                    x.Position = (Position)position;
                    world.LastRetired.Add(x);
                }
            }
            else
            {
                // A world from before #194: the next id is past the highest.
                world.NextPlayerId = highest + 1;
            }

            if (version >= 7)
            {
                int training = Count(r.ReadInt32());
                for (int i = 0; i < training; i++)
                {
                    int id = r.ReadInt32();
                    Club c = world.Clubs.Find(x => x.Id == id) ?? throw new FormatException($"Training for club {id}, which isn't in the world.");
                    byte t = r.ReadByte();
                    if (!Enum.IsDefined(typeof(TrainingIntensity), (TrainingIntensity)t)) throw new FormatException($"Club {id}'s training {t} is unknown.");
                    c.Training = (TrainingIntensity)t;
                }
            }

            if (version >= 8)
            {
                if (r.ReadInt32() != world.Clubs.Count) throw new FormatException("The clubs' money doesn't count every club.");
                foreach (Club c in world.Clubs)
                {
                    c.Balance = r.ReadInt32();
                    for (int i = 0; i < Ledger.Lines; i++) c.Season.Amounts[i] = r.ReadInt32();
                    for (int i = 0; i < Ledger.Lines; i++) c.LastSeason.Amounts[i] = r.ReadInt32();
                }

                if (r.ReadInt32() != world.Players.Count) throw new FormatException("The wages don't count every footballer.");
                foreach (Player p in world.Players)
                {
                    p.Wage = ReadShort(r);
                    if (p.Wage < 0) throw new FormatException($"Footballer {p.Id}'s wage {p.Wage} is negative.");
                }
            }
            else
            {
                // A world from before #198: each club's opening balance, each footballer's wage by the rule.
                var division = new Dictionary<int, int>();
                foreach (Club c in world.Clubs)
                {
                    c.Balance = Finance.StartBalance(c);
                    division[c.Id] = c.Division;
                }

                foreach (Player p in world.Players) p.Wage = Finance.Wage(p, division.TryGetValue(p.ClubId, out int d) ? d : 1);
            }

            if (!r.AtEnd) throw new FormatException("Bytes after the world.");
            return world;
        }

        // An amount of k in four bytes, at most ±2,147,483,647k.
        private static int Money(long k) => (int)Math.Clamp(k, int.MinValue, int.MaxValue);

        // A signed count in two bytes, high first: points and seasons within ±32,767.
        private static void WriteShort(CanonicalWriter w, int n)
        {
            int v = Math.Clamp(n, short.MinValue, short.MaxValue) & 0xFFFF;
            w.WriteByte((byte)(v >> 8));
            w.WriteByte((byte)v);
        }

        private static int ReadShort(CanonicalReader r) => (short)((r.ReadByte() << 8) | r.ReadByte());

        // A remembered meeting's formation byte: its high bit says the match was played in full.
        private const byte FullMatch = 0x80;

        // A small count as a byte, at most 255.
        private static byte Small(int n) => (byte)Math.Clamp(n, 0, 255);

        // A count read from the bytes, refused when it can't be one.
        private static int Count(int n) => n < 0 || n > 1_000_000 ? throw new FormatException($"Invalid count {n}.") : n;

        private static int NonNegative(int n) => n < 0 ? throw new FormatException($"Invalid ban or caution count {n}.") : n;
    }
}
