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
        public static readonly Color Dot = new Color32(0xE0, 0x4A, 0x3F, 0xFF);
        public static readonly Color HudText = Color.white;
        public static readonly Color HudPanel = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color ControlIdle = new Color(1f, 1f, 1f, 0.35f);
        public static readonly Color ControlActive = new Color(1f, 1f, 1f, 0.7f);
        public static readonly Color ControlOutline = new Color(0f, 0f, 0f, 0.7f);
    }
}
