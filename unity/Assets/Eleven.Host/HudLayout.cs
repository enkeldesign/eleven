using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// Where the on-screen controls are, in screen pixels with the origin at the top left (the
    /// IMGUI convention). Derived from the screen size, safe area and number of result lines only,
    /// so the CI smoke test can be told the same positions through the log.
    ///
    /// <see cref="PlayArea"/> is the part of the screen no HUD element covers; the camera frames the
    /// play inside it.
    /// </summary>
    internal readonly struct HudLayout
    {
        /// <summary>Most lines the result panel holds: three per maths backend.</summary>
        public const int MaxResultLines = 6;

        /// <summary>Widest result line, e.g. "digest 0123456789abcdef".</summary>
        public const int ResultLineCharacters = 23;

        public readonly int ScreenWidth;
        public readonly int ScreenHeight;
        public readonly float Unit;
        public readonly float Padding;
        public readonly int FontSize;
        public readonly float LineHeight;
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
        public readonly Rect PlayArea;

        /// <summary>Lines the result panel was laid out for.</summary>
        public readonly int ResultLines;

        private HudLayout(int width, int height, Rect safe, int resultLines)
        {
            ScreenWidth = width;
            ScreenHeight = height;
            Unit = Mathf.Min(safe.width, safe.height) / 9f;
            Padding = Unit * 0.3f;
            FontSize = Mathf.Max(12, Mathf.RoundToInt(Unit * 0.36f));
            LineHeight = FontSize * 1.3f;

            BuildLabel = new Rect(safe.xMin + Padding, safe.yMin + Padding, safe.width - 2 * Padding, 0.7f * Unit);
            RunButton = new Rect(safe.xMax - Padding - 4.5f * Unit, BuildLabel.yMax + Padding * 0.5f, 4.5f * Unit, 1.1f * Unit);
            MathsButton = new Rect(RunButton.x, RunButton.yMax + Padding * 0.5f, RunButton.width, 0.9f * Unit);
            ResetButton = new Rect(RunButton.x - Padding - 2.6f * Unit, RunButton.y, 2.6f * Unit, RunButton.height);
            // Sized for the lines it shows, and empty until a scenario has run. Beside the buttons
            // when there is room (landscape), else below them (portrait).
            int lines = Mathf.Clamp(resultLines, 0, MaxResultLines);
            ResultLines = lines;
            float resultWidth = ResultLineCharacters * 0.62f * FontSize + 2 * Padding;
            float resultHeight = lines == 0 ? 0 : lines * LineHeight + Padding;
            float besideButtons = ResetButton.x - safe.xMin - 2 * Padding;
            Result = besideButtons >= resultWidth
                ? new Rect(safe.xMin + Padding, RunButton.y, besideButtons, resultHeight)
                : new Rect(safe.xMin + Padding, MathsButton.yMax + Padding * 0.5f, safe.width - 2 * Padding, resultHeight);

            float belowHud = Mathf.Max(lines == 0 ? 0 : Result.yMax, MathsButton.yMax) + Padding * 0.5f;
            StickRadius = 1.4f * Unit;
            StickRest = new Vector2(safe.xMin + Padding + 2f * Unit, safe.yMax - Padding - 2f * Unit);

            KickRadius = 1.3f * Unit;
            KickCentre = new Vector2(safe.xMax - Padding - 1.8f * Unit, safe.yMax - Padding - 1.8f * Unit);

            // Two candidates for the uncovered play area: the full-width band between the top HUD
            // and the controls (suits portrait), or the column between the stick and the kick
            // button running to the bottom (suits landscape). The larger one wins.
            float controlsTop = Mathf.Min(KickCentre.y - KickRadius, StickRest.y - StickRadius) - Padding;
            var band = new Rect(safe.xMin, belowHud, safe.width, controlsTop - belowHud);
            float columnLeft = StickRest.x + StickRadius + Padding;
            float columnRight = KickCentre.x - KickRadius - Padding;
            var column = new Rect(columnLeft, belowHud, columnRight - columnLeft, safe.yMax - belowHud);
            bool useBand = Area(band) >= Area(column);
            PlayArea = useBand ? band : column;
            if (PlayArea.width <= 0 || PlayArea.height <= 0) PlayArea = safe;

            // The floating stick may start anywhere in its zone, so the zone is kept out of the
            // play area: below the band, or left of the column.
            StickZone = useBand
                ? new Rect(safe.xMin, PlayArea.yMax, safe.width * 0.5f, safe.yMax - PlayArea.yMax)
                : new Rect(safe.xMin, belowHud, PlayArea.xMin - safe.xMin, safe.yMax - belowHud);
        }

        /// <param name="safeArea">Unity's <c>Screen.safeArea</c>, whose origin is the bottom left.</param>
        /// <param name="resultLines">Lines the result panel shows now.</param>
        public static HudLayout For(int width, int height, Rect safeArea, int resultLines)
        {
            var safe = new Rect(safeArea.x, height - safeArea.yMax, safeArea.width, safeArea.height);
            return new HudLayout(width, height, safe, resultLines);
        }

        /// <summary>
        /// Where to draw the stick's base of <paramref name="radius"/>: as near <paramref name="centre"/>
        /// as fits inside the stick zone, so it never covers the play area or leaves the screen.
        /// </summary>
        public Vector2 KeepInStickZone(Vector2 centre, float radius) => new Vector2(
            Mathf.Clamp(centre.x, StickZone.xMin + radius, Mathf.Max(StickZone.xMin + radius, StickZone.xMax - radius)),
            Mathf.Clamp(centre.y, StickZone.yMin + radius, Mathf.Max(StickZone.yMin + radius, StickZone.yMax - radius)));

        public bool InKick(Vector2 point) => (point - KickCentre).sqrMagnitude <= KickRadius * KickRadius;

        /// <summary>One log line the CI smoke test parses to find the controls.</summary>
        public string Describe() =>
            System.FormattableString.Invariant($"screen={ScreenWidth}x{ScreenHeight} run={Point(RunButton.center)} maths={Point(MathsButton.center)} reset={Point(ResetButton.center)} kick={Point(KickCentre)} stick={Point(StickRest)} stickRadius={StickRadius:F0} play={PlayArea.x:F0},{PlayArea.y:F0},{PlayArea.width:F0},{PlayArea.height:F0}");

        private static float Area(Rect r) => Mathf.Max(0, r.width) * Mathf.Max(0, r.height);

        private static string Point(Vector2 p) => System.FormattableString.Invariant($"{p.x:F0},{p.y:F0}");
    }
}
