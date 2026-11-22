using System;
using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation;

namespace Eleven.Simulation.Harness
{
    /// <summary>The hub's one forward action (#168).</summary>
    public enum HubAction
    {
        /// <summary>Play the human's fixture: player-manager.</summary>
        Play,

        /// <summary>Watch the human's fixture: manager-only.</summary>
        Watch,

        /// <summary>No fixture of the human's this matchday: play the others.</summary>
        NextMatchday,

        /// <summary>The season is over: roll into the next.</summary>
        NextSeason,
    }

    /// <summary>One line of the hub's table: its text, and whether it is the human's club.</summary>
    public readonly struct HubRow
    {
        public HubRow(string text, bool mine)
        {
            Text = text;
            Mine = mine;
        }

        public string Text { get; }

        public bool Mine { get; }
    }

    /// <summary>
    /// What the career hub says (#168), from #167's session: engine-free, so the host only
    /// shows it. Every line in plain English; M7 owns the Swedish.
    /// </summary>
    public static class CareerHub
    {
        public static string Ordinal(int n) => "";

        public static string Header(CareerSession s) => "";

        public static string Standing(CareerSession s) => "";

        public static string NextLine(CareerSession s) => "";

        public static List<HubRow> TableRows(CareerSession s) => new List<HubRow>();

        public static List<string> ResultLines(CareerSession s, int count) => new List<string>();

        public static string Summary(CareerSession s) => "";

        public static HubAction Action(CareerSession s) => HubAction.Play;
    }
}
