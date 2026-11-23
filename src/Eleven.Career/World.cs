#nullable enable
using System.Collections.Generic;
using Eleven.Simulation;

namespace Eleven.Career
{
    /// <summary>
    /// The fictional world (#157): every club, footballer and manager, each with a stable id apart
    /// from its editable display name. Plain data (INV-3): it is the career's state, encoded by
    /// <see cref="WorldCodec"/>.
    /// </summary>
    public sealed class World
    {
        /// <summary>The seed it was generated from; 0 for a world made otherwise.</summary>
        public ulong Seed;
        public List<Club> Clubs = new List<Club>();
        public List<Player> Players = new List<Player>();
        public List<Manager> Managers = new List<Manager>();
    }

    /// <summary>A club: its identity, division and squad (the ids of its players).</summary>
    public sealed class Club
    {
        public int Id;
        public string Name = "";
        public string ShortName = "";
        /// <summary>Home kit: shirt and shorts, as design tokens (<see cref="KitColour"/>).</summary>
        public KitColour Shirt, Shorts;
        /// <summary>1–20: how the football world sees the club.</summary>
        public int Reputation;
        /// <summary>1 is the top division.</summary>
        public int Division;
        public int ManagerId;
        public List<int> Squad = new List<int>();
    }

    /// <summary>A footballer: identity, positions, attributes (<see cref="PlayerAttributes"/>) and condition.</summary>
    public sealed class Player
    {
        public int Id;
        public string FirstName = "", LastName = "";
        public int Age;
        public int ClubId;
        /// <summary>Where he plays, best first.</summary>
        public List<Position> Positions = new List<Position>();
        public Foot Foot;
        /// <summary>One value a <see cref="PlayerAttributes"/> entry, 1–20, in its order.</summary>
        public int[] Attributes = new int[PlayerAttributes.Count];
        /// <summary>1–20: the level he could reach.</summary>
        public int Potential;
        /// <summary>0–100.</summary>
        public int Fitness = 100;
        /// <summary>1–20.</summary>
        public int Morale = 10;
        /// <summary>Days out injured; 0 when fit.</summary>
        public int InjuredDays;

        /// <summary>His club's matches he must still sit out (#176): in the league, and in the cup.</summary>
        public int LeagueBan, CupBan;

        /// <summary>This season's cautions toward a ban (#176), in the league and in the cup.</summary>
        public int LeagueCautions, CupCautions;

        public int Get(string attribute) => Attributes[PlayerAttributes.Index(attribute)];
    }

    /// <summary>A manager: identity, competences (<see cref="ManagerCompetences"/>), style and temperament. Memory comes in M6.09.</summary>
    public sealed class Manager
    {
        public int Id;
        public string FirstName = "", LastName = "";
        public int Age;
        public int ClubId;
        /// <summary>One value a <see cref="ManagerCompetences"/> entry, 1–20.</summary>
        public int[] Competences = new int[ManagerCompetences.Count];
        public Formation PreferredFormation = Formation.FourFourTwo;
        /// <summary>1 (cautious) – 20 (bold).</summary>
        public int Temperament = 10;
        /// <summary>1–20.</summary>
        public int Reputation;
    }
}
