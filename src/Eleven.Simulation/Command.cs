namespace Eleven.Simulation
{
    /// <summary>
    /// An owned command (INV-10): one controller's intent for one tick, as recorded from an input
    /// adapter before any assistance.
    ///
    /// Rules, applied by <see cref="Simulator.Step"/>:
    /// <list type="bullet">
    /// <item>A command applies to the dot whose <see cref="DotState.OwnerControllerId"/> equals its
    /// <see cref="ControllerId"/>. With no such dot it is rejected as <see cref="CommandRejection.Unowned"/>.</item>
    /// <item>A command applies only at its <see cref="Tick"/>. Passed to a step after its tick it is
    /// rejected as <see cref="CommandRejection.Stale"/>; before its tick, as <see cref="CommandRejection.Early"/>.</item>
    /// <item>More than one command for the same controller at the same tick is a recording error:
    /// all of them are rejected as <see cref="CommandRejection.Duplicate"/>, so the result never
    /// depends on submission order.</item>
    /// <item>A non-finite move vector is rejected as <see cref="CommandRejection.Invalid"/>; a move
    /// vector longer than 1 is scaled to length 1.</item>
    /// <item>Accepted commands apply in ascending controller id, whatever order they were passed in.</item>
    /// <item>A dot with no accepted command this tick behaves as if given a neutral command
    /// (no move, no kick).</item>
    /// </list>
    /// Every rejection emits a <see cref="SimEventKind.CommandRejected"/> event.
    /// </summary>
    public readonly struct Command
    {
        public readonly int ControllerId;
        public readonly long Tick;
        public readonly Vec2 Move;
        public readonly bool Kick;

        public Command(int controllerId, long tick, Vec2 move, bool kick)
        {
            ControllerId = controllerId;
            Tick = tick;
            Move = move;
            Kick = kick;
        }
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
    }
}
