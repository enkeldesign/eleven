using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// What the placed HUD elements need, in screen pixels, as the HUD measured them after layout
    /// (#48, #53): an action's width with its label on one line and its height at the width it has,
    /// or a panel's own size. Zero until measured. None of it depends on where an element is placed,
    /// so laying out again with what was measured settles.
    /// </summary>
    internal sealed class HudNeeds
    {
        public Vector2 Build;
        public Vector2 Reset;
        public Vector2 Run;
        public Vector2 FeltWrong;
        public Vector2 SaveCapture;
        public Vector2 Result;
        public Vector2 Status;
    }

    /// <summary>
    /// Where the on-screen controls are, in screen pixels with the origin at the top left (the
    /// IMGUI convention). Derived from the screen size, safe area, number of result lines and what
    /// the placed elements need only, so the CI smoke test can be told the same positions through
    /// the log.
    ///
    /// <see cref="PlayArea"/> is the part of the screen no HUD element covers; the camera frames the
    /// play inside it.
    /// </summary>
    internal readonly struct HudLayout
    {
        /// <summary>Most lines the result panel holds: the scenario's time, digest and final hash.</summary>
        public const int MaxResultLines = 3;

        public readonly int ScreenWidth;
        public readonly int ScreenHeight;
        public readonly float Unit;
        public readonly float Padding;
        public readonly int FontSize;

        /// <summary>The build line's panel: its top left, the row's width, and the height it needs.</summary>
        public readonly Rect BuildLabel;
        public readonly Rect ResetButton;
        public readonly Rect RunButton;

        /// <summary>The scenario result's panel, empty until a scenario has run.</summary>
        public readonly Rect Result;
        public readonly Rect StickZone;
        public readonly Vector2 StickRest;
        public readonly float StickRadius;
        public readonly Vector2 KickCentre;
        public readonly float KickRadius;
        public readonly Rect PlayArea;

        /// <summary>
        /// THIS FELT WRONG (#44, #53): just above the kick button, where the right thumb is, outside
        /// the play area. It was between the stick and the kick button in portrait, but a labelled
        /// action doesn't fit there on phones (#53).
        /// </summary>
        public readonly Rect FeltWrongButton;

        /// <summary>
        /// SAVE CAPTURE (#44, #53): beside THIS FELT WRONG where the row holds both, otherwise above
        /// it. Its place is kept while it is hidden, so nothing moves when a capture appears.
        /// </summary>
        public readonly Rect SaveCaptureButton;

        /// <summary>The status line's place: centred at the top of the play area, over it. It takes no input.</summary>
        public readonly Rect StatusArea;

        /// <summary>Lines the result panel was laid out for.</summary>
        public readonly int ResultLines;

        private HudLayout(int width, int height, Rect safe, int resultLines, HudNeeds needs)
        {
            ScreenWidth = width;
            ScreenHeight = height;
            Unit = Mathf.Min(safe.width, safe.height) / 9f;
            Padding = Unit * 0.3f;
            FontSize = Mathf.Max(12, Mathf.RoundToInt(Unit * 0.36f));
            float row = safe.width - 2 * Padding;

            BuildLabel = new Rect(safe.xMin + Padding, safe.yMin + Padding, row, Mathf.Max(0.7f * Unit, needs.Build.y));

            // The top row's actions widen to hold their labels on one line, as far as the row allows,
            // and take the height that gives: enlarged text (WCAG 1.4.4) or a longer label never
            // overflows them (#48). Side by side where they fit, otherwise Reset goes below Run.
            float runWidth = Mathf.Min(row, Mathf.Max(4.5f * Unit, needs.Run.x));
            float resetWidth = Mathf.Min(row, Mathf.Max(2.6f * Unit, needs.Reset.x));
            float actionHeight = Mathf.Max(1.1f * Unit, Mathf.Max(needs.Run.y, needs.Reset.y));
            RunButton = new Rect(safe.xMax - Padding - runWidth, BuildLabel.yMax + Padding * 0.5f, runWidth, actionHeight);
            ResetButton = runWidth + Padding + resetWidth <= row
                ? new Rect(RunButton.x - Padding - resetWidth, RunButton.y, resetWidth, actionHeight)
                : new Rect(safe.xMax - Padding - resetWidth, RunButton.yMax + Padding * 0.5f, resetWidth, actionHeight);
            float actionsLeft = Mathf.Min(RunButton.x, ResetButton.x);
            float actionsBottom = Mathf.Max(RunButton.yMax, ResetButton.yMax);

            // Empty until a scenario has run, then the size its panel needs: beside the actions where
            // it fits (landscape), otherwise below them.
            int lines = Mathf.Clamp(resultLines, 0, MaxResultLines);
            ResultLines = lines;
            float besideActions = actionsLeft - safe.xMin - 2 * Padding;
            bool shown = lines > 0;
            bool beside = shown && needs.Result.x > 0 && needs.Result.x <= besideActions;
            float resultHeight = shown ? needs.Result.y : 0;
            Result = beside
                ? new Rect(safe.xMin + Padding, RunButton.y, needs.Result.x, resultHeight)
                : new Rect(safe.xMin + Padding, actionsBottom + Padding * 0.5f, Mathf.Min(row, needs.Result.x), resultHeight);

            float belowHud = Mathf.Max(shown ? Result.yMax : 0, actionsBottom) + Padding * 0.5f;
            StickRadius = 1.4f * Unit;
            StickRest = new Vector2(safe.xMin + Padding + 2f * Unit, safe.yMax - Padding - 2f * Unit);

            KickRadius = 1.3f * Unit;
            KickCentre = new Vector2(safe.xMax - Padding - 1.8f * Unit, safe.yMax - Padding - 1.8f * Unit);

            // The capture actions, above the kick button and right-aligned to the safe area: side by
            // side where allowed and the row holds both, otherwise SAVE CAPTURE above THIS FELT WRONG.
            float captureHeight = Mathf.Max(1.1f * Unit, Mathf.Max(needs.FeltWrong.y, needs.SaveCapture.y));
            float feltWidth = Mathf.Min(row, Mathf.Max(2.6f * Unit, needs.FeltWrong.x));
            float saveWidth = Mathf.Min(row, Mathf.Max(2.6f * Unit, needs.SaveCapture.x));
            var felt = new Rect(safe.xMax - Padding - feltWidth, KickCentre.y - KickRadius - Padding - captureHeight, feltWidth, captureHeight);
            bool fitsRow = feltWidth + Padding + saveWidth <= row;
            Rect saveBeside = new Rect(felt.x - Padding - saveWidth, felt.y, saveWidth, captureHeight);
            Rect saveAbove = new Rect(safe.xMax - Padding - saveWidth, felt.y - Padding * 0.5f - captureHeight, saveWidth, captureHeight);

            // Two candidates for the uncovered play area: the full-width band between the top HUD
            // and the controls (suits portrait), or the column between the stick and the kick
            // button running to the bottom (suits landscape). The larger one wins. The band ends
            // above the capture actions; the column keeps left of them, so they stack there.
            float stickTop = StickRest.y - StickRadius;
            Rect bandSave = fitsRow ? saveBeside : saveAbove;
            float controlsTop = Mathf.Min(Mathf.Min(KickCentre.y - KickRadius, stickTop), Mathf.Min(felt.y, bandSave.y)) - Padding;
            var band = new Rect(safe.xMin, belowHud, safe.width, controlsTop - belowHud);
            float columnLeft = StickRest.x + StickRadius + Padding;
            float columnRight = Mathf.Min(KickCentre.x - KickRadius, Mathf.Min(felt.x, saveAbove.x)) - Padding;
            var column = new Rect(columnLeft, belowHud, columnRight - columnLeft, safe.yMax - belowHud);
            bool useBand = Area(band) >= Area(column);
            PlayArea = useBand ? band : column;
            if (PlayArea.width <= 0 || PlayArea.height <= 0) PlayArea = safe;
            FeltWrongButton = felt;
            SaveCaptureButton = useBand ? bandSave : saveAbove;

            // The floating stick may start anywhere in its zone, so the zone is kept out of the
            // play area: below the band, or left of the column. In the band it also starts below any
            // capture action that reaches into it.
            if (useBand)
            {
                float zoneTop = PlayArea.yMax;
                float zoneRight = safe.xMin + safe.width * 0.5f;
                if (FeltWrongButton.xMin < zoneRight) zoneTop = Mathf.Max(zoneTop, FeltWrongButton.yMax + Padding * 0.5f);
                if (SaveCaptureButton.xMin < zoneRight) zoneTop = Mathf.Max(zoneTop, SaveCaptureButton.yMax + Padding * 0.5f);
                StickZone = new Rect(safe.xMin, zoneTop, safe.width * 0.5f, safe.yMax - zoneTop);
            }
            else
            {
                StickZone = new Rect(safe.xMin, belowHud, PlayArea.xMin - safe.xMin, safe.yMax - belowHud);
            }

            // The status line states what just happened over the top of the play area, as wide as
            // its text needs.
            float statusWidth = Mathf.Min(PlayArea.width - 2 * Padding, needs.Status.x);
            StatusArea = new Rect(PlayArea.center.x - statusWidth * 0.5f, PlayArea.yMin + Padding * 0.5f, statusWidth, needs.Status.y);

            // Last resort for cramped screens: the stick shrinks to fit its zone, and rests inside it,
            // so its base can always be drawn whole without covering the play. (C# 9 struct rule:
            // every field is assigned before StickDrawnRadius reads `this`.)
            StickRadius = Mathf.Min(StickRadius, (Mathf.Min(StickZone.width, StickZone.height) * 0.5f - 2f) / DrawnRadiusScale);
            StickRest = new Vector2(
                Mathf.Clamp(StickRest.x, StickZone.xMin + StickDrawnRadius, StickZone.xMax - StickDrawnRadius),
                Mathf.Clamp(StickRest.y, StickZone.yMin + StickDrawnRadius, StickZone.yMax - StickDrawnRadius));
        }

        /// <summary>The stick base's outline is drawn this much wider than its radius.</summary>
        public const float DrawnRadiusScale = 1.06f;

        /// <summary>Radius of the stick's base as drawn, outline included.</summary>
        public float StickDrawnRadius => StickRadius * DrawnRadiusScale + 2f;

        /// <param name="safeArea">Unity's <c>Screen.safeArea</c>, whose origin is the bottom left.</param>
        /// <param name="resultLines">Lines the result panel shows now.</param>
        /// <param name="needs">What the placed elements need, as measured; null before anything is.</param>
        public static HudLayout For(int width, int height, Rect safeArea, int resultLines, HudNeeds needs = null)
        {
            var safe = new Rect(safeArea.x, height - safeArea.yMax, safeArea.width, safeArea.height);
            return new HudLayout(width, height, safe, resultLines, needs ?? new HudNeeds());
        }

        /// <summary>Whether every element sits where it does in <paramref name="other"/>.</summary>
        public bool SamePlaces(in HudLayout other) =>
            BuildLabel == other.BuildLabel && ResetButton == other.ResetButton && RunButton == other.RunButton
            && Result == other.Result && FeltWrongButton == other.FeltWrongButton && SaveCaptureButton == other.SaveCaptureButton
            && StatusArea == other.StatusArea && PlayArea == other.PlayArea && StickZone == other.StickZone;

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
            System.FormattableString.Invariant($"screen={ScreenWidth}x{ScreenHeight} run={Point(RunButton.center)} reset={Point(ResetButton.center)} kick={Point(KickCentre)} stick={Point(StickRest)} stickRadius={StickRadius:F0} play={Box(PlayArea)} feltWrong={Box(FeltWrongButton)} save={Box(SaveCaptureButton)}");

        private static float Area(Rect r) => Mathf.Max(0, r.width) * Mathf.Max(0, r.height);

        private static string Point(Vector2 p) => System.FormattableString.Invariant($"{p.x:F0},{p.y:F0}");

        private static string Box(Rect r) => System.FormattableString.Invariant($"{r.x:F0},{r.y:F0},{r.width:F0},{r.height:F0}");
    }
}
