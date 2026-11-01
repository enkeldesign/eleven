using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// Semantic colour tokens for the spike host. Everything the host draws takes its colour
    /// from here; nothing else hard-codes a colour.
    /// </summary>
    internal static class HostPalette
    {
        public static readonly Color Pitch = new Color32(0x2E, 0x6B, 0x3A, 0xFF);
        public static readonly Color PitchLine = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
        public static readonly Color Ball = new Color32(0xF7, 0xE2, 0x6B, 0xFF);
        public static readonly Color BallShadow = new Color(0f, 0f, 0f, 0.35f);
        // The player. Light green, 3.4:1 against the pitch (WCAG 1.4.11 asks 3:1 for graphics).
        public static readonly Color Player = new Color32(0x99, 0xCC, 0x44, 0xFF);
        public static readonly Color HudText = Color.white;
        public static readonly Color HudPanel = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color ControlIdle = new Color(1f, 1f, 1f, 0.35f);
        public static readonly Color ControlActive = new Color(1f, 1f, 1f, 0.7f);
        public static readonly Color ControlOutline = new Color(0f, 0f, 0f, 0.7f);
        // Keyboard focus on page controls (#44): the ball's yellow, 4.6:1 against the pitch and
        // about 11:1 against the HUD panel (WCAG 1.4.11 asks 3:1).
        public static readonly Color FocusRing = new Color32(0xF7, 0xE2, 0x6B, 0xFF);

        // The feel test bench's sheet (#45): near-opaque, so its text reads the same over any part of
        // the pitch. White text on it is about 17:1; fields are a step lighter, edged in HudText.
        public static readonly Color BenchSheet = new Color32(0x10, 0x1A, 0x14, 0xF5);
        public static readonly Color BenchField = new Color32(0x22, 0x30, 0x28, 0xFF);

        // A changed value's marker on the bench, beside its "changed" text: the focus yellow, about
        // 13:1 against the sheet.
        public static readonly Color BenchChanged = FocusRing;

        /// <summary>A token as a CSS colour, for page controls drawn by the browser.</summary>
        public static string Css(Color c) => System.FormattableString.Invariant(
            $"rgba({Mathf.RoundToInt(c.r * 255)},{Mathf.RoundToInt(c.g * 255)},{Mathf.RoundToInt(c.b * 255)},{c.a:0.###})");
    }
}
