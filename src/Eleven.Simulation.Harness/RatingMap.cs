using System;
using System.Collections.ObjectModel;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// One simulation value from a footballer's ratings (#159): the mean of
    /// <see cref="Attributes"/> (1–20), mapped piecewise-linearly through three points. At 1 it
    /// is <see cref="AtOne"/>, at <see cref="RatingMap.ReferenceRating"/> exactly the
    /// simulation's reference <see cref="AtReference"/>, and at 20 <see cref="AtTwenty"/>.
    /// </summary>
    public sealed class RatingEntry
    {
        public RatingEntry(string target, double atOne, double atReference, double atTwenty, params string[] attributes)
        {
            Target = target;
            AtOne = atOne;
            AtReference = atReference;
            AtTwenty = atTwenty;
            Attributes = Array.AsReadOnly(attributes);
            Indices = Array.ConvertAll(attributes, PlayerAttributes.Index);
        }

        /// <summary>The simulation value's tunable name.</summary>
        public string Target { get; }

        public double AtOne { get; }

        public double AtReference { get; }

        public double AtTwenty { get; }

        public ReadOnlyCollection<string> Attributes { get; }

        private int[] Indices { get; }

        /// <summary>The footballer's rating for this value: the mean of its attributes.</summary>
        public double Rating(Player p)
        {
            double sum = 0;
            foreach (int i in Indices) sum += p.Attributes[i];
            return sum / Indices.Length;
        }

        /// <summary>The value at a rating, held within 1–20.</summary>
        public double At(double rating)
        {
            double r = rating < PlayerAttributes.Min ? PlayerAttributes.Min : rating > PlayerAttributes.Max ? PlayerAttributes.Max : rating;
            double half = RatingMap.ReferenceRating - PlayerAttributes.Min;
            return r <= RatingMap.ReferenceRating
                ? AtOne + (AtReference - AtOne) * (r - PlayerAttributes.Min) / half
                : AtReference + (AtTwenty - AtReference) * (r - RatingMap.ReferenceRating) / (PlayerAttributes.Max - RatingMap.ReferenceRating);
        }

        public double Value(Player p) => At(Rating(p));
    }

    /// <summary>
    /// A footballer's ratings onto the simulation's inputs (#159): his outfield capabilities, his
    /// keeping and his CPU skill. Data: each value names its attributes and its value at 1, at the
    /// reference rating and at 20. A world of average players (10.5 throughout) plays exactly as
    /// the technical reference. Every value depends only on the footballer's own attributes: no
    /// overall rating, no bonus (#80). Technical references, not approved feel.
    /// </summary>
    public static class RatingMap
    {
        /// <summary>The rating that gives the reference: the middle of 1–20.</summary>
        public const double ReferenceRating = 10.5;

        /// <summary>The outfield capabilities (<see cref="PlayerCapabilities"/>).</summary>
        public static readonly ReadOnlyCollection<RatingEntry> Outfield = Array.AsReadOnly(new[]
        {
            new RatingEntry("maxSpeed", 6.0, 7.0, 8.5, "topSpeed"),
            new RatingEntry("acceleration", 15, 25, 35, "acceleration"),
            // The reference is already the whole of kickSpeed: above it, finishing pays through kick error.
            new RatingEntry("kickPower", 0.75, 1.0, 1.0, "shootingPower"),
            new RatingEntry("kickError", 0.12, 0.05, 0.02, "finishing", "shortPassing", "longPassing", "curve"),
            new RatingEntry("passVision", 0.7, 1.05, 1.4, "vision"),
            new RatingEntry("passAnticipation", 0.55, 0.75, 0.9, "anticipation"),
            new RatingEntry("ballControl", 0.45, 0.7, 0.9, "firstTouch", "dribbling"),
            new RatingEntry("tackling", 0.45, 0.7, 0.9, "tackling", "marking"),
            new RatingEntry("heading", 0.35, 0.6, 0.85, "heading", "jumping"),
            new RatingEntry("strength", 0.35, 0.6, 0.85, "strength"),
            new RatingEntry("stamina", 0.35, 0.6, 0.85, "stamina"),
        });

        /// <summary>A keeper's keeping (<see cref="KeeperCapabilities"/>).</summary>
        public static readonly ReadOnlyCollection<RatingEntry> Keeping = Array.AsReadOnly(new[]
        {
            new RatingEntry("reactionTime", 0.32, 0.20, 0.14, "keeperReflexes"),
            new RatingEntry("diveSpeed", 2.3, 3.5, 4.5, "keeperReflexes", "agility"),
            new RatingEntry("reach", 0.6, 0.8, 1.0, "keeperPositioning", "jumping"),
            new RatingEntry("handling", 0.4, 0.7, 0.9, "keeperHandling"),
        });

        /// <summary>A CPU footballer's skill (<see cref="CpuSkill"/>): the Apprentice tier's at 1, Pro's at the reference, Elite's at 20.</summary>
        public static readonly ReadOnlyCollection<RatingEntry> Cpu = Array.AsReadOnly(new[]
        {
            new RatingEntry("decisionInterval", 0.25, 0.15, 0.10, "decisions"),
            new RatingEntry("reactionDelay", 0.30, 0.15, 0.10, "anticipation"),
            new RatingEntry("perceptionError", 0.08, 0.025, 0.01, "vision", "anticipation"),
            new RatingEntry("visionRange", 30, 70, 120, "vision"),
        });

        /// <summary>Plumbing: the reference, whatever the ratings.</summary>
        public static PlayerCapabilities Capabilities(Player p) => PlayerCapabilities.Reference;

        /// <summary>Plumbing: the reference keeper.</summary>
        public static KeeperCapabilities Keeper(Player p) => KeeperCapabilities.Reference;

        /// <summary>Plumbing: the Pro tier.</summary>
        public static CpuSkill Skill(Player p) => CpuSkill.Pro;
    }
}
