namespace Eleven.Simulation
{
    /// <summary>
    /// An owned command (INV-10): one controller's intent for one tick, as recorded from an input
    /// adapter before any assistance.
    ///
    /// Rules, applied by <see cref="Simulator.Step"/>:
    /// <list type="bullet">
    /// <item>A command applies to the player whose <see cref="PlayerState.OwnerControllerId"/> equals its
    /// <see cref="ControllerId"/>. With no such player it is rejected as <see cref="CommandRejection.Unowned"/>.</item>
    /// <item>A command applies only at its <see cref="Tick"/>. Passed to a step after its tick it is
    /// rejected as <see cref="CommandRejection.Stale"/>; before its tick, as <see cref="CommandRejection.Early"/>.</item>
    /// <item>More than one command for the same controller at the same tick is a recording error:
    /// all of them are rejected as <see cref="CommandRejection.Duplicate"/>, so the result never
    /// depends on submission order.</item>
    /// <item>A non-finite value anywhere (move, aftertouch, or any part of the kick intent) is
    /// rejected as <see cref="CommandRejection.Invalid"/>. A move or aftertouch vector longer than 1 is
    /// scaled to length 1, and a kick's strength and loft are clamped to [0, 1].</item>
    /// <item>A manual <see cref="Kick"/> takes precedence over <see cref="QuickPass"/> in the same command:
    /// assistance never overrides or redirects a manual kick.</item>
    /// <item>Accepted commands apply in ascending controller id, whatever order they were passed in.</item>
    /// <item>A player with no accepted command this tick behaves as if given a neutral command
    /// (no move, no kick, no quick pass, no aftertouch).</item>
    /// </list>
    /// Every rejection emits a <see cref="SimEventKind.CommandRejected"/> event.
    /// </summary>
    public readonly struct Command
    {
        public readonly int ControllerId;
        public readonly long Tick;
        public readonly Vec2 Move;

        /// <summary>A manual kick asked for this tick, or null. See <see cref="KickIntent"/>.</summary>
        public readonly KickIntent? Kick;

        /// <summary>
        /// Shaping input for the player's last kick (#37), length at most 1, on the pitch plane. It
        /// acts only while that kick's aftertouch window is open; otherwise it is ignored. Separate
        /// from <see cref="Move"/>, so the gesture mapping decides whether the stick doubles as it.
        /// </summary>
        public readonly Vec2 Aftertouch;

        /// <summary>
        /// An assisted quick pass (#38): the footballer picks the receiver, the line and the weight
        /// inside the simulation, after this command has been recorded (INV-10). Ignored when
        /// <see cref="Kick"/> is set.
        /// </summary>
        public readonly bool QuickPass;

        /// <summary>
        /// A keeper's dive (#69): the point on the pitch, with its height, he dives for. Taken only by
        /// a keeper on his feet (<see cref="KeeperPhase.Position"/>); ignored otherwise, as for
        /// any other player. A non-finite target is <see cref="CommandRejection.Invalid"/>.
        /// </summary>
        public readonly Vec3? Dive;

        /// <summary>
        /// A CPU outfielder's new plan (#83), from his brain. Taken only from the controller of a
        /// CPU outfielder who may decide on this tick; otherwise the whole command is rejected as
        /// <see cref="CommandRejection.PlanRefused"/>. It moves no one: the simulation keeps it,
        /// and it takes effect after his reaction delay. A non-finite value in it is
        /// <see cref="CommandRejection.Invalid"/>.
        /// </summary>
        public readonly CpuPlan? Plan;

        /// <summary>
        /// Player switching (#93): the id of a teammate this controller takes over at the end of
        /// the step, or 0. The footballer it leaves is played by his side's CPU. Refused, as
        /// <see cref="CommandRejection.SwitchRefused"/>, unless the target is a teammate of the
        /// controller's footballer, not a keeper, and played by the CPU.
        /// </summary>
        public readonly int SwitchTo;

        /// <summary>
        /// A substitution (#110): the id of the footballer of this controller's side who goes off,
        /// and of the substitute on its bench who comes on in his slot and place; both 0 for none.
        /// Made at the start of the step, only at a stoppage or half-time, within the side's
        /// substitutions and windows; refused otherwise, with the reason as the rejection.
        /// </summary>
        public readonly int SubOff, SubOn;

        /// <summary>
        /// A switch of this controller's side's tactical plan (#126): 1 to 3 for its stored plans,
        /// 0 for none. Taken at the next stoppage or at once, by planSwitchAt; refused, with the
        /// reason, inside the cooldown or for the plan it already plays or awaits.
        /// </summary>
        public readonly byte TacticTo;

        public Command(int controllerId, long tick, Vec2 move, KickIntent? kick = null, Vec2 aftertouch = default, bool quickPass = false, Vec3? dive = null, CpuPlan? plan = null,
            int switchTo = 0, int subOff = 0, int subOn = 0, byte tacticTo = 0)
        {
            TacticTo = tacticTo;
            SubOff = subOff;
            SubOn = subOn;
            SwitchTo = switchTo;
            Plan = plan;
            ControllerId = controllerId;
            Tick = tick;
            Move = move;
            Kick = kick;
            Aftertouch = aftertouch;
            QuickPass = quickPass;
            Dive = dive;
        }

        /// <summary>This command with a substitution (#110) added: everything else as it is.</summary>
        public Command WithSubstitution(int off, int on) => new Command(ControllerId, Tick, Move, Kick, Aftertouch, QuickPass, Dive, Plan, SwitchTo, off, on, TacticTo);

        /// <summary>This command with a switch of tactical plan (#126) added: everything else as it is.</summary>
        public Command WithTactic(byte to) => new Command(ControllerId, Tick, Move, Kick, Aftertouch, QuickPass, Dive, Plan, SwitchTo, SubOff, SubOn, to);
    }

    /// <summary>
    /// A generic manual kick (#37): where, how hard and how high. It names no action: a pass, a
    /// shot or a cross is a consequence of these values and the situation (#26).
    ///
    /// The simulation keeps the direction as given. It never retargets it; the kicker's own
    /// execution error is the only deviation, and it is bounded (<see cref="PlayerCapabilities.KickError"/>).
    /// </summary>
    public readonly struct KickIntent
    {
        /// <summary>
        /// The intended line on the pitch plane. Only its direction counts; exactly zero means the
        /// player's heading when the intent arrives.
        /// </summary>
        public readonly Vec2 Direction;

        /// <summary>0–1: the fraction of the player's kick power.</summary>
        public readonly double Strength;

        /// <summary>0–1: the fraction of the config's maximum loft, <see cref="SimConfig.KickLoft"/>.</summary>
        public readonly double Loft;

        public KickIntent(Vec2 direction, double strength, double loft)
        {
            Direction = direction;
            Strength = strength;
            Loft = loft;
        }

        /// <summary>Along the player's heading.</summary>
        public static KickIntent AlongHeading(double strength, double loft) => new KickIntent(Vec2.Zero, strength, loft);
    }

    /// <summary>Why a command was not applied. Numeric values are stable.</summary>
    public enum CommandRejection
    {
        None = 0,
        Unowned = 1,
        Stale = 2,
        Early = 3,
        Duplicate = 4,
        Invalid = 5,

        /// <summary>The command carried a CPU plan (#83) from a controller with no CPU outfielder, or before he may decide again.</summary>
        PlanRefused = 6,

        /// <summary>A switch (#93) to a footballer who isn't a CPU-played outfielder of the controller's side.</summary>
        SwitchRefused = 7,

        /// <summary>A substitution (#110) while the ball is in play, or after full time.</summary>
        SubNotStopped = 8,

        /// <summary>A substitution (#110) with the side's substitutions all made.</summary>
        SubNoneLeft = 9,

        /// <summary>A substitution (#110) in a new stoppage with the side's windows all used.</summary>
        SubNoWindow = 10,

        /// <summary>A substitution (#110) naming someone who can't go off (not of the side, or sent off) or can't come on (not on its bench, or already used).</summary>
        SubIneligible = 11,

        /// <summary>A switch of tactical plan (#126) inside the side's cooldown.</summary>
        TacticCooldown = 12,

        /// <summary>A switch of tactical plan (#126) to no stored plan, or to the one the side already plays or awaits.</summary>
        TacticRefused = 13,
    }
}
