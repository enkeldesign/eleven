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
        public Vector2 Bench;
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

        /// <summary>TEST BENCH (#45): in the top row with Reset and Run scenario, away from the thumbs.</summary>
        public readonly Rect BenchButton;

        /// <summary>The scenario result's panel, empty until a scenario has run.</summary>
        public readonly Rect Result;
        public readonly Rect StickZone;
        public readonly Vector2 StickRest;
        public readonly float StickRadius;
        /// <summary>Where the ACTION disc is drawn: the right thumb's resting place, inside <see cref="ActionZone"/>.</summary>
        public readonly Vector2 KickCentre;
        public readonly float KickRadius;
        public readonly Rect PlayArea;

        /// <summary>
        /// Candidate A's ACTION surface (#28): the whole of the screen's right-hand control area, the
        /// stick zone's mirror, so the right thumb needn't find a button. A press on a UI action
        /// inside it (THIS FELT WRONG, SAVE CAPTURE) belongs to the UI.
        /// </summary>
        public readonly Rect ActionZone;

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

        /// <summary>
        /// Candidate B's PASS (#76): a bounded disc in the right-hand control area's bottom corner,
        /// under the resting thumb. A press on it is the quick pass.
        /// </summary>
        public readonly Vector2 PassCentre;

        public readonly float PassRadius;

        /// <summary>
        /// Where Candidate B's KICK is marked (#76), the same size as PASS. KICK is the whole
        /// right-hand control area outside PASS; the marker is drawn beside PASS where it fits whole
        /// in that area, clear of PASS and the capture actions: to its left, up and to its left, or
        /// above it, a little smaller if it must. A radius of 0 means nothing fits, and KICK is
        /// labelled on PASS's left instead.
        /// </summary>
        public readonly Vector2 KickMarker;

        public readonly float KickMarkerRadius;

        /// <summary>Lines the result panel was laid out for.</summary>
        public readonly int ResultLines;

        /// <summary>The screen's safe area (#31): everything above is laid out inside it.</summary>
        public readonly Rect Safe;

        private HudLayout(int width, int height, Rect safe, int resultLines, HudNeeds needs)
        {
            ScreenWidth = width;
            ScreenHeight = height;
            Safe = safe;
            Unit = Mathf.Min(safe.width, safe.height) / 9f;
            Padding = Unit * 0.3f;
            FontSize = Mathf.Max(12, Mathf.RoundToInt(Unit * 0.36f));
            float row = safe.width - 2 * Padding;

            BuildLabel = new Rect(safe.xMin + Padding, safe.yMin + Padding, row, Mathf.Max(0.7f * Unit, needs.Build.y));

            // The top row's actions widen to hold their labels on one line, as far as the row allows,
            // and take the height that gives: enlarged text (WCAG 1.4.4) or a longer label never
            // overflows them (#48). They flow from the right, Run scenario, Reset, then TEST BENCH, and
            // an action that no longer fits its row starts the next one, right-aligned.
            float runWidth = Mathf.Min(row, Mathf.Max(4.5f * Unit, needs.Run.x));
            float resetWidth = Mathf.Min(row, Mathf.Max(2.6f * Unit, needs.Reset.x));
            float benchWidth = Mathf.Min(row, Mathf.Max(2.6f * Unit, needs.Bench.x));
            float actionHeight = Mathf.Max(1.1f * Unit, Mathf.Max(needs.Run.y, Mathf.Max(needs.Reset.y, needs.Bench.y)));
            RunButton = new Rect(safe.xMax - Padding - runWidth, BuildLabel.yMax + Padding * 0.5f, runWidth, actionHeight);
            float rowLeft = RunButton.x;
            float rowTop = RunButton.y;
            ResetButton = Flow(resetWidth, actionHeight, safe, Padding, ref rowLeft, ref rowTop);
            BenchButton = Flow(benchWidth, actionHeight, safe, Padding, ref rowLeft, ref rowTop);
            float actionsLeft = Mathf.Min(RunButton.x, Mathf.Min(ResetButton.x, BenchButton.x));
            float actionsBottom = Mathf.Max(RunButton.yMax, Mathf.Max(ResetButton.yMax, BenchButton.yMax));

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
                ActionZone = new Rect(zoneRight, PlayArea.yMax, safe.xMax - zoneRight, safe.yMax - PlayArea.yMax);
            }
            else
            {
                StickZone = new Rect(safe.xMin, belowHud, PlayArea.xMin - safe.xMin, safe.yMax - belowHud);
                ActionZone = new Rect(PlayArea.xMax, belowHud, safe.xMax - PlayArea.xMax, safe.yMax - belowHud);
            }

            // The status line states what just happened over the top of the play area, as wide as
            // its text needs.
            float statusWidth = Mathf.Min(PlayArea.width - 2 * Padding, needs.Status.x);
            StatusArea = new Rect(PlayArea.center.x - statusWidth * 0.5f, PlayArea.yMin + Padding * 0.5f, statusWidth, needs.Status.y);

            // Candidate B (#76): PASS in the right-hand area's bottom corner, under the resting
            // thumb, and KICK's marker the first place beside it that fits whole, clear of PASS and
            // the capture actions; a little smaller where the full size doesn't fit.
            PassRadius = 0.9f * Unit;
            PassCentre = new Vector2(safe.xMax - Padding - PassRadius, safe.yMax - Padding - PassRadius);
            KickMarker = Vector2.zero;
            KickMarkerRadius = 0;
            foreach (float markerRadius in new[] { PassRadius, 0.7f * Unit })
            {
                float gap = PassRadius + Padding + markerRadius;
                float diagonal = gap * 0.7071f;
                foreach (Vector2 offset in new[] { new Vector2(-gap, 0), new Vector2(-diagonal, -diagonal), new Vector2(0, -gap) })
                {
                    Vector2 c = PassCentre + offset;
                    var box = new Rect(c.x - markerRadius, c.y - markerRadius, 2 * markerRadius, 2 * markerRadius);
                    bool inside = box.xMin >= ActionZone.xMin && box.xMax <= ActionZone.xMax && box.yMin >= ActionZone.yMin && box.yMax <= ActionZone.yMax;
                    if (!inside || box.Overlaps(FeltWrongButton) || box.Overlaps(SaveCaptureButton)) continue;
                    KickMarker = c;
                    KickMarkerRadius = markerRadius;
                    break;
                }

                if (KickMarkerRadius > 0) break;
            }

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

        // The next action in the top row: left of the last where it fits, otherwise the start of a new
        // row below, right-aligned.
        private static Rect Flow(float width, float height, Rect safe, float padding, ref float rowLeft, ref float rowTop)
        {
            if (rowLeft - padding - width < safe.xMin + padding)
            {
                rowTop += height + padding * 0.5f;
                rowLeft = safe.xMax - padding;
                var start = new Rect(rowLeft - width, rowTop, width, height);
                rowLeft = start.x;
                return start;
            }

            var next = new Rect(rowLeft - padding - width, rowTop, width, height);
            rowLeft = next.x;
            return next;
        }

        /// <summary>Whether every element sits where it does in <paramref name="other"/>.</summary>
        public bool SamePlaces(in HudLayout other) =>
            BuildLabel == other.BuildLabel && ResetButton == other.ResetButton && RunButton == other.RunButton && BenchButton == other.BenchButton
            && Result == other.Result && FeltWrongButton == other.FeltWrongButton && SaveCaptureButton == other.SaveCaptureButton
            && StatusArea == other.StatusArea && PlayArea == other.PlayArea && StickZone == other.StickZone && ActionZone == other.ActionZone;

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

        /// <summary>One log line the CI smoke test parses to find the controls.</summary>
        public string Describe() =>
            System.FormattableString.Invariant($"screen={ScreenWidth}x{ScreenHeight} run={Point(RunButton.center)} reset={Point(ResetButton.center)} kick={Point(KickCentre)} stick={Point(StickRest)} stickRadius={StickRadius:F0} play={Box(PlayArea)} feltWrong={Box(FeltWrongButton)} save={Box(SaveCaptureButton)} bench={Box(BenchButton)} action={Box(ActionZone)} pass={Point(PassCentre)},{PassRadius:F0} kickMarker={Point(KickMarker)},{KickMarkerRadius:F0} safe={Box(Safe)} stickZone={Box(StickZone)} kickRadius={KickRadius:F0}");

        private static float Area(Rect r) => Mathf.Max(0, r.width) * Mathf.Max(0, r.height);

        private static string Point(Vector2 p) => System.FormattableString.Invariant($"{p.x:F0},{p.y:F0}");

        private static string Box(Rect r) => System.FormattableString.Invariant($"{r.x:F0},{r.y:F0},{r.width:F0},{r.height:F0}");
    }
}
