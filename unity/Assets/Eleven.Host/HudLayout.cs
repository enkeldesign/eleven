using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// Where the on-screen controls are, in screen pixels with the origin at the top left (the
    /// IMGUI convention). Derived from the screen size and safe area only, so the CI smoke test
    /// can be told the same positions through the log.
    /// </summary>
    internal readonly struct HudLayout
    {
        public readonly int ScreenWidth;
        public readonly int ScreenHeight;
        public readonly float Unit;
        public readonly float Padding;
        public readonly Rect BuildLabel;
        public readonly Rect ResetButton;
        public readonly Rect RunButton;
        public readonly Rect MathsButton;
        public readonly Rect Result;
        public readonly Rect StickZone;
        public readonly Vector2 StickRest;
        public readonly float StickRadius;
        public readonly Vector2 KickCentre;
        public readonly float KickRadius;

        private HudLayout(int width, int height, Rect safe)
        {
            ScreenWidth = width;
            ScreenHeight = height;
            Unit = Mathf.Min(safe.width, safe.height) / 9f;
            Padding = Unit * 0.3f;

            BuildLabel = new Rect(safe.xMin + Padding, safe.yMin + Padding, safe.width - 2 * Padding, 0.7f * Unit);
            RunButton = new Rect(safe.xMax - Padding - 4.5f * Unit, BuildLabel.yMax + Padding * 0.5f, 4.5f * Unit, 1.1f * Unit);
            MathsButton = new Rect(RunButton.x, RunButton.yMax + Padding * 0.5f, RunButton.width, 0.9f * Unit);
            ResetButton = new Rect(RunButton.x - Padding - 2.6f * Unit, RunButton.y, 2.6f * Unit, RunButton.height);
            // Beside the buttons, not across the middle of the screen where the camera keeps the play.
            Result = new Rect(safe.xMin + Padding, ResetButton.yMax + Padding * 0.5f, MathsButton.x - safe.xMin - 2 * Padding, 1.2f * Unit);

            float belowHud = Mathf.Max(Result.yMax, MathsButton.yMax);
            StickZone = new Rect(safe.xMin, belowHud, safe.width * 0.5f, safe.yMax - belowHud);
            StickRadius = 1.4f * Unit;
            StickRest = new Vector2(StickZone.xMin + Padding + 2f * Unit, StickZone.yMax - Padding - 2f * Unit);

            KickRadius = 1.3f * Unit;
            KickCentre = new Vector2(safe.xMax - Padding - 1.8f * Unit, safe.yMax - Padding - 1.8f * Unit);
        }

        /// <param name="safeArea">Unity's <c>Screen.safeArea</c>, whose origin is the bottom left.</param>
        public static HudLayout For(int width, int height, Rect safeArea)
        {
            var safe = new Rect(safeArea.x, height - safeArea.yMax, safeArea.width, safeArea.height);
            return new HudLayout(width, height, safe);
        }

        public bool InKick(Vector2 point) => (point - KickCentre).sqrMagnitude <= KickRadius * KickRadius;

        /// <summary>One log line the CI smoke test parses to find the controls.</summary>
        public string Describe() =>
            System.FormattableString.Invariant($"screen={ScreenWidth}x{ScreenHeight} run={Point(RunButton.center)} maths={Point(MathsButton.center)} reset={Point(ResetButton.center)} kick={Point(KickCentre)} stick={Point(StickRest)} stickRadius={StickRadius:F0}");

        private static string Point(Vector2 p) => System.FormattableString.Invariant($"{p.x:F0},{p.y:F0}");
    }
}
