using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// Where the on-screen controls are, in screen pixels with the origin at the top left (the
    /// IMGUI convention). Derived from the screen size, safe area, number of result lines and what
    /// the top row's labels need only, so the CI smoke test can be told the same positions through
    /// the log.
    ///
    /// <see cref="PlayArea"/> is the part of the screen no HUD element covers; the camera frames the
    /// play inside it.
    /// </summary>
    internal readonly struct HudLayout
    {
        /// <summary>Most lines the result panel holds: the scenario's time, digest and final hash.</summary>
        public const int MaxResultLines = 3;

        /// <summary>Widest result line, e.g. "digest 0123456789abcdef".</summary>
        public const int ResultLineCharacters = 23;

        /// <summary>Widest build line, e.g. "ELEVEN 123+0123abc · load 12.3 s · 123 dropped in 60 s".</summary>
        public const int BuildLineCharacters = 54;

        public readonly int ScreenWidth;
        public readonly int ScreenHeight;
        public readonly float Unit;
        public readonly float Padding;
        public readonly int FontSize;
        public readonly float LineHeight;
        public readonly int BuildFontSize;
        public readonly int ResultFontSize;
        public readonly float ResultLineHeight;

        public readonly Rect BuildLabel;
        public readonly Rect ResetButton;
        public readonly Rect RunButton;
        public readonly Rect Result;
        public readonly Rect StickZone;
        public readonly Vector2 StickRest;
        public readonly float StickRadius;
        public readonly Vector2 KickCentre;
        public readonly float KickRadius;
        public readonly Rect PlayArea;

        /// <summary>
        /// Where the page lays its THIS FELT WRONG button (#44), outside the play area: between the
        /// stick and the kick button at the bottom (portrait band), or above the kick button in its
        /// column (landscape). The page keeps it at least 44 × 44 pt.
        /// </summary>
        public readonly Rect FeltWrongButton;

        /// <summary>Lines the result panel was laid out for.</summary>
        public readonly int ResultLines;

        private HudLayout(int width, int height, Rect safe, int resultLines, Vector2 resetNeeds, Vector2 runNeeds)
        {
            ScreenWidth = width;
            ScreenHeight = height;
            Unit = Mathf.Min(safe.width, safe.height) / 9f;
            Padding = Unit * 0.3f;
            FontSize = Mathf.Max(12, Mathf.RoundToInt(Unit * 0.36f));
            LineHeight = FontSize * 1.3f;

            BuildLabel = new Rect(safe.xMin + Padding, safe.yMin + Padding, safe.width - 2 * Padding, 0.7f * Unit);
            BuildFontSize = Mathf.Max(12, Mathf.Min(FontSize, FontToFit(BuildLabel.width - 16, BuildLineCharacters)));
            // The top row's actions widen to hold their labels on one line, as far as the row allows,
            // and take the height that gives: enlarged text (WCAG 1.4.4) or a longer label never
            // overflows them (#48). Side by side where they fit, otherwise Reset goes below Run.
            float row = safe.width - 2 * Padding;
            float runWidth = Mathf.Min(row, Mathf.Max(4.5f * Unit, runNeeds.x));
            float resetWidth = Mathf.Min(row, Mathf.Max(2.6f * Unit, resetNeeds.x));
            float actionHeight = Mathf.Max(1.1f * Unit, Mathf.Max(runNeeds.y, resetNeeds.y));
            RunButton = new Rect(safe.xMax - Padding - runWidth, BuildLabel.yMax + Padding * 0.5f, runWidth, actionHeight);
            ResetButton = runWidth + Padding + resetWidth <= row
                ? new Rect(RunButton.x - Padding - resetWidth, RunButton.y, resetWidth, actionHeight)
                : new Rect(safe.xMax - Padding - resetWidth, RunButton.yMax + Padding * 0.5f, resetWidth, actionHeight);
            float actionsLeft = Mathf.Min(RunButton.x, ResetButton.x);
            float actionsBottom = Mathf.Max(RunButton.yMax, ResetButton.yMax);
            // Sized for the lines it shows, and empty until a scenario has run: beside the buttons
            // where it fits (landscape), with its text shrunk to no less than 60 % of the HUD font;
            // otherwise below them, across the screen.
            int lines = Mathf.Clamp(resultLines, 0, MaxResultLines);
            ResultLines = lines;
            int smallest = Mathf.Max(12, Mathf.CeilToInt(FontSize * 0.6f));
            float besideButtons = actionsLeft - safe.xMin - 2 * Padding;
            int fontBeside = FontToFit(besideButtons - 2 * Padding, ResultLineCharacters);
            int fontBelow = FontToFit(safe.width - 4 * Padding, ResultLineCharacters);
            bool beside = fontBeside >= smallest;
            ResultFontSize = Mathf.Max(12, Mathf.Min(FontSize, beside ? fontBeside : fontBelow));
            ResultLineHeight = ResultFontSize * 1.3f;
            float resultHeight = lines == 0 ? 0 : lines * ResultLineHeight + Padding;
            Result = beside
                ? new Rect(safe.xMin + Padding, RunButton.y, besideButtons, resultHeight)
                : new Rect(safe.xMin + Padding, actionsBottom + Padding * 0.5f, safe.width - 2 * Padding, resultHeight);

            float belowHud = Mathf.Max(lines == 0 ? 0 : Result.yMax, actionsBottom) + Padding * 0.5f;
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

            // C# 9 struct rule: every field is assigned before StickDrawnRadius reads `this` below.
            FeltWrongButton = default;

            // Last resort for cramped screens: the stick shrinks to fit its zone, and rests inside it,
            // so its base can always be drawn whole without covering the play.
            StickRadius = Mathf.Min(StickRadius, (Mathf.Min(StickZone.width, StickZone.height) * 0.5f - 2f) / DrawnRadiusScale);
            StickRest = new Vector2(
                Mathf.Clamp(StickRest.x, StickZone.xMin + StickDrawnRadius, StickZone.xMax - StickDrawnRadius),
                Mathf.Clamp(StickRest.y, StickZone.yMin + StickDrawnRadius, StickZone.yMax - StickDrawnRadius));

            float buttonWidth = 2.6f * Unit, buttonHeight = 1.1f * Unit;
            if (useBand)
            {
                float left = StickRest.x + StickDrawnRadius + Padding;
                float right = KickCentre.x - KickRadius - Padding;
                buttonWidth = Mathf.Max(0, Mathf.Min(buttonWidth, right - left));
                FeltWrongButton = new Rect((left + right - buttonWidth) * 0.5f, safe.yMax - Padding - buttonHeight, buttonWidth, buttonHeight);
            }
            else
            {
                buttonWidth = Mathf.Min(buttonWidth, 2 * KickRadius + Padding);
                FeltWrongButton = new Rect(KickCentre.x - buttonWidth * 0.5f, KickCentre.y - KickRadius - Padding - buttonHeight, buttonWidth, buttonHeight);
            }
        }

        /// <summary>The stick base's outline is drawn this much wider than its radius.</summary>
        public const float DrawnRadiusScale = 1.06f;

        /// <summary>Radius of the stick's base as drawn, outline included.</summary>
        public float StickDrawnRadius => StickRadius * DrawnRadiusScale + 2f;

        /// <param name="safeArea">Unity's <c>Screen.safeArea</c>, whose origin is the bottom left.</param>
        /// <param name="resultLines">Lines the result panel shows now.</param>
        /// <param name="resetNeeds">What Reset needs for its label, in screen pixels: its width with the label on one line, and its height at the width it has. Zero until measured.</param>
        /// <param name="runNeeds">The same for Run scenario.</param>
        public static HudLayout For(int width, int height, Rect safeArea, int resultLines, Vector2 resetNeeds = default, Vector2 runNeeds = default)
        {
            var safe = new Rect(safeArea.x, height - safeArea.yMax, safeArea.width, safeArea.height);
            return new HudLayout(width, height, safe, resultLines, resetNeeds, runNeeds);
        }

        /// <summary>
        /// Where to draw the stick's base: as near <paramref name="centre"/> as fits inside the stick
        /// zone, which the layout guarantees can hold it, so it never covers the play area or leaves
        /// the screen.
        /// </summary>
        public Vector2 KeepInStickZone(Vector2 centre)
        {
            float r = StickDrawnRadius;
            return new Vector2(
                Mathf.Clamp(centre.x, StickZone.xMin + r, StickZone.xMax - r),
                Mathf.Clamp(centre.y, StickZone.yMin + r, StickZone.yMax - r));
        }

        public bool InKick(Vector2 point) => (point - KickCentre).sqrMagnitude <= KickRadius * KickRadius;

        /// <summary>One log line the CI smoke test parses to find the controls.</summary>
        public string Describe() =>
            System.FormattableString.Invariant($"screen={ScreenWidth}x{ScreenHeight} run={Point(RunButton.center)} reset={Point(ResetButton.center)} kick={Point(KickCentre)} stick={Point(StickRest)} stickRadius={StickRadius:F0} play={PlayArea.x:F0},{PlayArea.y:F0},{PlayArea.width:F0},{PlayArea.height:F0} feltWrong={FeltWrongButton.x:F0},{FeltWrongButton.y:F0},{FeltWrongButton.width:F0},{FeltWrongButton.height:F0}");

        // The largest font at which a line of this many characters fits in this width.
        private static int FontToFit(float width, int characters) => Mathf.FloorToInt(width / (characters * 0.62f));

        private static float Area(Rect r) => Mathf.Max(0, r.width) * Mathf.Max(0, r.height);

        private static string Point(Vector2 p) => System.FormattableString.Invariant($"{p.x:F0},{p.y:F0}");
    }
}
