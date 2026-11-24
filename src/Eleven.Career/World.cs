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

        /// <summary>The next footballer's id (#194): a youth's, never one a retired footballer had.</summary>
        public int NextPlayerId;

        /// <summary>Those who retired at the last season's turn (#194), for its review.</summary>
        public List<Retired> LastRetired = new List<Retired>();
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

        /// <summary>
        /// The human's picks (#179): his XI's 11 footballers, then his bench. Empty: the AI's rule
        /// picks, as it does for every other club.
        /// </summary>
        public List<int> Picks = new List<int>();

        /// <summary>How hard the club trains (#196): Auto is its manager's choice, matchday by matchday.</summary>
        public TrainingIntensity Training;
    }

    /// <summary>A club's training (#196). Numeric values are stable and encoded as one byte.</summary>
    public enum TrainingIntensity : byte
    {
        /// <summary>The manager chooses, matchday by matchday (<see cref="Training.Choose"/>).</summary>
        Auto = 0,

        Light = 1,
        Normal = 2,
        Hard = 3,
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

        /// <summary>
        /// Development (#194): attribute points accrued toward his next, 0 to under 1, so no growth
        /// is lost to rounding.
        /// </summary>
        public double Progress;

        /// <summary>His starts for his club this season (#194).</summary>
        public int Starts;

        /// <summary>His attribute points gained, less those lost, this season and last (#194).</summary>
        public int SeasonPoints, LastSeasonPoints;

        /// <summary>The season he joined from his club's youth (#194); 0 for one from the world's start.</summary>
        public int Joined;

        public int Get(string attribute) => Attributes[PlayerAttributes.Index(attribute)];
    }

    /// <summary>A manager: identity, competences (<see cref="ManagerCompetences"/>), style, temperament and memory (#191).</summary>
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

        /// <summary>
        /// His philosophy (#180), 1–20 each: how far up his side presses, and how high its line
        /// stands. 0 in a world from before it: he plays the reference plan.
        /// </summary>
        public int Pressing, Depth;
        /// <summary>1–20.</summary>
        public int Reputation;

        /// <summary>His meetings with other clubs (#191), the latest few with each kept, oldest first within each.</summary>
        public List<Meeting> Memory = new List<Meeting>();
    }
}
