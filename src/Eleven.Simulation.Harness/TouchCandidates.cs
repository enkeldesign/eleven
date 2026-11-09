// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;

namespace Eleven.Simulation.Harness
{
    /// <summary>The touch control candidates (#28), switchable live.</summary>
    public enum ControlCandidate
    {
        /// <summary>A, ACTION SURFACE (#57).</summary>
        ActionSurface,

        /// <summary>B, SPLIT: PASS and KICK (#76).</summary>
        Split,
    }

    /// <summary>
    /// The live switch between touch candidates (#28, #76): one controller's adapters, one active.
    /// Only the adapter changes: the simulation, its config and its state are the host's and are
    /// never touched, so a switch makes no step and no command. A gesture in progress is cancelled,
    /// and the candidate taking over ignores every finger already down until it lifts, so nothing
    /// held across a switch becomes a phantom pass, kick, move or aftertouch.
    /// </summary>
    public sealed class TouchCandidates
    {
        public TouchCandidates(int controllerId, ActionSurfaceSettings actionSurface, SplitSettings split)
        {
            ActionSurface = new ActionSurfaceAdapter(controllerId, actionSurface);
            Split = new SplitAdapter(controllerId, split);
            ActionSurface.Delivered += report => Delivered?.Invoke(report);
            Split.Delivered += report => Delivered?.Invoke(report);
        }

        public ActionSurfaceAdapter ActionSurface { get; }

        public SplitAdapter Split { get; }

        public ControlCandidate Active { get; private set; } = ControlCandidate.ActionSurface;

        /// <summary>The active candidate's adapter.</summary>
        public GestureAdapter Current => Active == ControlCandidate.Split ? Split : (GestureAdapter)ActionSurface;

        /// <summary>Raised when the active adapter delivers a gesture's command.</summary>
        public event Action<ActionReport>? Delivered;

        /// <summary>Another candidate, at once.</summary>
        public void Switch(ControlCandidate to)
        {
            Active = to;
        }

        /// <summary>Where the controls are, for every candidate, so a switch takes effect at once.</summary>
        public void SetLayout(in TouchLayout layout)
        {
            ActionSurface.SetLayout(layout);
            Split.SetLayout(layout);
        }

        /// <summary>A new view (#72) for every candidate: gestures in progress are cancelled.</summary>
        public void Orient(PitchOrientation orientation)
        {
            ActionSurface.Orient(orientation);
            Split.Orient(orientation);
        }

        /// <summary>Whether a kick is winding up or its window is open: read by the active candidate.</summary>
        public bool ShapingOpen
        {
            set
            {
                ActionSurface.ShapingOpen = value;
                Split.ShapingOpen = value;
            }
        }

        public TouchRole Feed(in TouchSample sample) => Current.Feed(sample);

        public Command CommandFor(long tick) => Current.CommandFor(tick);

        /// <summary>Cancels the active candidate's gestures: a reset, a retry or a sheet opening.</summary>
        public void Cancel() => Current.Cancel();

        /// <summary>The active candidate and every value, for a capture.</summary>
        public string Describe() => Current.Describe();

        /// <summary>The short name a log or the HUD shows: <c>action-surface</c> or <c>split</c>.</summary>
        public string Name => Active == ControlCandidate.Split ? SplitSettings.Candidate : ActionSurfaceSettings.Candidate;
    }
}
