// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;

namespace Eleven.Simulation.Harness
{
    /// <summary>The touch control candidates (#28), switchable live.</summary>
    public enum ControlCandidate
    {
        /// <summary>A, ACTION SURFACE (#57).</summary>
        ActionSurface,

        /// <summary>B, SPLIT: PASS and KICK (#76).</summary>
        Split,

        /// <summary>C, ONE FIRE: a tap passes, a hold kicks along the heading (#77).</summary>
        OneFire,
    }

    /// <summary>
    /// The live switch between touch candidates (#28, #76, #77): one controller's adapters, one
    /// active. Only the adapter changes: the simulation, its config and its state are the host's and
    /// are never touched, so a switch makes no step and no command. A gesture in progress is
    /// cancelled, and the candidate taking over ignores every finger already down until it lifts, so
    /// nothing held across a switch becomes a phantom pass, kick, move or aftertouch.
    /// </summary>
    public sealed class TouchCandidates
    {
        private readonly GestureAdapter[] _all;

        public TouchCandidates(int controllerId, ActionSurfaceSettings actionSurface, SplitSettings split, OneFireSettings oneFire)
        {
            ActionSurface = new ActionSurfaceAdapter(controllerId, actionSurface);
            Split = new SplitAdapter(controllerId, split);
            OneFire = new OneFireAdapter(controllerId, oneFire);
            // In ControlCandidate's order.
            _all = new GestureAdapter[] { ActionSurface, Split, OneFire };
            foreach (GestureAdapter adapter in _all) adapter.Delivered += report => Delivered?.Invoke(report);
        }

        public ActionSurfaceAdapter ActionSurface { get; }

        public SplitAdapter Split { get; }

        public OneFireAdapter OneFire { get; }

        public ControlCandidate Active { get; private set; } = ControlCandidate.ActionSurface;

        /// <summary>The active candidate's adapter.</summary>
        public GestureAdapter Current => _all[(int)Active];

        /// <summary>Raised when the active adapter delivers a gesture's command.</summary>
        public event Action<ActionReport>? Delivered;

        /// <summary>
        /// Another candidate, at once. The one giving way cancels its gestures, and the one taking
        /// over starts clean and ignores every finger the other held until it lifts.
        /// </summary>
        public void Switch(ControlCandidate to)
        {
            if (to == Active) return;
            if ((int)to < 0 || (int)to >= _all.Length) throw new ArgumentOutOfRangeException(nameof(to));
            GestureAdapter from = Current;
            var held = new List<int>(from.HeldPointers());
            from.Cancel();
            Active = to;
            Current.Cancel();
            Current.IgnoreUntilLifted(held);
        }

        /// <summary>Where the controls are, for every candidate, so a switch takes effect at once.</summary>
        public void SetLayout(in TouchLayout layout)
        {
            foreach (GestureAdapter adapter in _all) adapter.SetLayout(layout);
        }

        /// <summary>A new view (#72) for every candidate: gestures in progress are cancelled.</summary>
        public void Orient(PitchOrientation orientation)
        {
            foreach (GestureAdapter adapter in _all) adapter.Orient(orientation);
        }

        /// <summary>Whether a kick is winding up or its window is open: read by the active candidate.</summary>
        public bool ShapingOpen
        {
            set
            {
                foreach (GestureAdapter adapter in _all) adapter.ShapingOpen = value;
            }
        }

        /// <summary>
        /// One sample, for the active candidate. A lift also reaches the inactive ones, which may
        /// still be ignoring that finger from before a switch.
        /// </summary>
        public TouchRole Feed(in TouchSample sample)
        {
            TouchRole role = Current.Feed(sample);
            if (sample.Phase == TouchPhase.Ended || sample.Phase == TouchPhase.Canceled)
            {
                foreach (GestureAdapter adapter in _all)
                {
                    if (adapter != Current) adapter.Lifted(sample.Pointer);
                }
            }

            return role;
        }

        public Command CommandFor(long tick) => Current.CommandFor(tick);

        /// <summary>Cancels the active candidate's gestures: a reset, a retry or a sheet opening.</summary>
        public void Cancel() => Current.Cancel();

        /// <summary>The active candidate and every value, for a capture.</summary>
        public string Describe() => Current.Describe();

        /// <summary>The short name a log or the HUD shows: <c>action-surface</c>, <c>split</c> or <c>one-fire</c>.</summary>
        public string Name => NameOf(Active);

        /// <summary>A candidate's short name.</summary>
        public static string NameOf(ControlCandidate candidate) => candidate switch
        {
            ControlCandidate.Split => SplitSettings.Candidate,
            ControlCandidate.OneFire => OneFireSettings.Candidate,
            _ => ActionSurfaceSettings.Candidate,
        };

        /// <summary>The candidate a short name is, as in <c>?controls=</c>; null if it names none.</summary>
        public static ControlCandidate? Named(string name)
        {
            foreach (ControlCandidate candidate in new[] { ControlCandidate.ActionSurface, ControlCandidate.Split, ControlCandidate.OneFire })
            {
                if (NameOf(candidate) == name) return candidate;
            }

            return null;
        }
    }
}
