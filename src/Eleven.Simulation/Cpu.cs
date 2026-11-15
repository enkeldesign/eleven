using System;
using System.Collections.ObjectModel;

namespace Eleven.Simulation
{
    /// <summary>
    /// What a CPU outfielder is doing (#83): the mode of his plan. A label for tools, tests and
    /// presentation; the simulation only stores it. Numeric values are stable.
    /// </summary>
    public enum CpuMode
    {
        /// <summary>No plan yet.</summary>
        None = 0,

        /// <summary>Running to where he reads a loose ball will be.</summary>
        SeekBall = 1,

        /// <summary>Closing on the ball, or on the opponent who has it.</summary>
        Approach = 2,

        /// <summary>Running with the ball toward the goal he attacks.</summary>
        Carry = 3,

        /// <summary>Kicking at the goal he attacks.</summary>
        Shoot = 4,

        /// <summary>Out of possession: getting between the ball and his own goal.</summary>
        Defend = 5,

        /// <summary>A keeper holds the ball: taking up a position and waiting.</summary>
        Recover = 6,
    }

    /// <summary>
    /// A CPU outfielder's plan (#83): what his brain decided on one observation. The stick he holds
    /// until his next plan, and a kick or quick pass he makes once, on the tick the plan takes
    /// effect. The same intents a human's adapter sends, no more.
    /// </summary>
    public readonly struct CpuPlan
    {
        public CpuPlan(CpuMode mode, Vec2 move, KickIntent? kick = null, bool quickPass = false)
        {
            Mode = mode;
            Move = move;
            Kick = kick;
            QuickPass = quickPass;
        }

        public CpuMode Mode { get; }

        /// <summary>Length at most 1, as a stick's.</summary>
        public Vec2 Move { get; }

        public KickIntent? Kick { get; }

        public bool QuickPass { get; }
    }

    /// <summary>
    /// How well a CPU footballer perceives and how often he decides (#83): the whole of difficulty.
    /// Nothing here touches physics, a capability or a command: a CPU footballer's body is his
    /// <see cref="PlayerCapabilities"/>, as a human's is. Untuned technical references.
    /// </summary>
    public struct CpuSkill
    {
        /// <summary>s. From one decision to the next.</summary>
        public double DecisionInterval;

        /// <summary>s. From the observation a plan is made on until he acts on it.</summary>
        public double ReactionDelay;

        /// <summary>Fraction of distance. How far off he may place the ball or another footballer he looks at, per metre away.</summary>
        public double PerceptionError;

        /// <summary>m. Other footballers further off than this he doesn't see. The ball he always sees.</summary>
        public double VisionRange;

        public const string Group = "Cpu";

        /// <summary>The values, with their valid ranges and #83's research starting ranges where it gives them.</summary>
        public static readonly ReadOnlyCollection<TunableParameter> Parameters = Array.AsReadOnly(new[]
        {
            new TunableParameter(Group, "decisionInterval", "s", 0.05, 1, 0.10, 0.25),
            new TunableParameter(Group, "reactionDelay", "s", 0, 1, 0.10, 0.30),
            new TunableParameter(Group, "perceptionError", "fraction of distance", 0, 0.5),
            new TunableParameter(Group, "visionRange", "m", 5, 200),
        });

        /// <summary>The four difficulty tiers (#80), weakest first. Technical references, not approved feel.</summary>
        public static CpuSkill Apprentice => new CpuSkill { DecisionInterval = 0.25, ReactionDelay = 0.30, PerceptionError = 0.08, VisionRange = 30 };

        public static CpuSkill Club => new CpuSkill { DecisionInterval = 0.20, ReactionDelay = 0.22, PerceptionError = 0.05, VisionRange = 45 };

        /// <summary>The reference tier: #83's reference decision interval.</summary>
        public static CpuSkill Pro => new CpuSkill { DecisionInterval = 0.15, ReactionDelay = 0.15, PerceptionError = 0.025, VisionRange = 70 };

        public static CpuSkill Elite => new CpuSkill { DecisionInterval = 0.10, ReactionDelay = 0.10, PerceptionError = 0.01, VisionRange = 120 };

        /// <summary>A tier by its name, in any case; null for any other name.</summary>
        public static CpuSkill? Named(string name) => (name ?? "").ToLowerInvariant() switch
        {
            "apprentice" => Apprentice,
            "club" => Club,
            "pro" => Pro,
            "elite" => Elite,
            _ => (CpuSkill?)null,
        };

        public double Get(string name) => name switch
        {
            "decisionInterval" => DecisionInterval,
            "reactionDelay" => ReactionDelay,
            "perceptionError" => PerceptionError,
            "visionRange" => VisionRange,
            _ => throw new ArgumentException($"'{name}' is not a CPU skill value.", nameof(name)),
        };

        public CpuSkill With(string name, double value)
        {
            CpuSkill copy = this;
            switch (name)
            {
                case "decisionInterval": copy.DecisionInterval = value; break;
                case "reactionDelay": copy.ReactionDelay = value; break;
                case "perceptionError": copy.PerceptionError = value; break;
                case "visionRange": copy.VisionRange = value; break;
                default: throw new ArgumentException($"'{name}' is not a CPU skill value.", nameof(name));
            }

            return copy;
        }

        internal void CheckRanges(string owner)
        {
            foreach (TunableParameter p in Parameters)
            {
                double value = Get(p.Name);
                if (!(value >= p.Min && value <= p.Max))
                {
                    throw new ArgumentOutOfRangeException(p.Name, value, $"{owner}: {p.Name} must be within [{p.Min}, {p.Max}] {p.Unit}.");
                }
            }
        }
    }

    /// <summary>One plan as the state keeps it (#83): the plan, the tick it was observed on and the tick it takes effect.</summary>
    public struct CpuPlanState
    {
        public CpuMode Mode;
        public Vec2 Move;
        public bool HasKick;
        public Vec2 KickDirection;
        public double KickStrength;
        public double KickLoft;
        public bool QuickPass;

        /// <summary>The tick of the observation it was decided on.</summary>
        public long ObservedTick;

        /// <summary>The tick it takes effect: its kick or quick pass is made on this tick, once.</summary>
        public long ActsTick;

        public KickIntent? Kick => HasKick ? new KickIntent(KickDirection, KickStrength, KickLoft) : (KickIntent?)null;
    }

    /// <summary>
    /// A CPU outfielder (#83): the player <see cref="PlayerId"/>, owned by an AI controller, with
    /// his skill, the plan he is acting on and the one he has decided but not yet reacted to.
    /// Plain data (INV-3). The simulation times his decisions and his reaction (INV-12): his brain
    /// can ask for a plan, never for when.
    /// </summary>
    public struct CpuState
    {
        public int PlayerId;

        public CpuSkill Skill;

        /// <summary>The first tick on which he may decide again.</summary>
        public long NextDecisionTick;

        /// <summary>The plan in effect. <see cref="CpuMode.None"/> before his first.</summary>
        public CpuPlanState Plan;

        /// <summary>Whether <see cref="Next"/> holds a plan waiting out his reaction delay.</summary>
        public bool HasNext;

        public CpuPlanState Next;

        /// <summary>The plan he acts on at <paramref name="tick"/>: the waiting one once its tick has come.</summary>
        public CpuPlanState InEffect(long tick) => HasNext && tick >= Next.ActsTick ? Next : Plan;

        /// <summary>Whether he may decide at <paramref name="tick"/>: his interval has passed and no plan is waiting.</summary>
        public bool MayDecide(long tick) => tick >= NextDecisionTick && !(HasNext && tick < Next.ActsTick);
    }
}
