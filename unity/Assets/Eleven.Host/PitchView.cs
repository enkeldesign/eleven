using System.Collections.Generic;
using Eleven.Presentation;
using Eleven.Simulation;
using Eleven.Simulation.Harness;
using UnityEngine;
using Pose = Eleven.Presentation.Pose;

namespace Eleven.Host
{
    /// <summary>
    /// Draws the pitch, the player and the ball as simple shapes, top-down. Built entirely at
    /// runtime: no scene objects, prefabs or imported art.
    ///
    /// The pitch is vertical (#72): the camera rolls so that the controlled team's attacked end is
    /// up the screen, through <see cref="PitchOrientation"/>, the same mapping the touch adapter
    /// reads gestures through. The scene stays in world coordinates, so the lines, the mown pattern,
    /// the goal, the players and the shadow turn with the view; the HUD, an overlay, doesn't.
    ///
    /// Presentation only: it reads interpolated positions and never feeds anything back into the
    /// simulation (INV-12).
    ///
    /// The grass is mown (#58): two shades in a pattern fixed to the pitch, so movement reads under
    /// the following camera wherever it is, not only when a line is in view.
    /// </summary>
    internal sealed class PitchView
    {
        /// <summary>How the grass is mown (#58).</summary>
        public enum Mowing
        {
            /// <summary>Squares, mown both ways: movement reads in every direction. The default.</summary>
            Crosscut,

            /// <summary>Bands parallel to the goal lines, as in SWOS and Kick Off 2: goal-to-goal movement reads.</summary>
            Stripes,

            /// <summary>One shade, as before #58, for comparison.</summary>
            Plain,
        }

        /// <summary>The width of one mown band, in metres: a technical reference (real pitches are mown in 5–6 m bands).</summary>
        public const float MownBand = 5.5f;

        // Presentation constants, not simulation config.
        private const float MinHalfExtent = 7f;

        /// <summary>
        /// The widest view (#31): the play area's shorter side never shows more than this many
        /// metres either side of its centre, so the footballer and the ball keep a useful size
        /// however far apart they are. 17.5 m is a 35 m view, about a third of the pitch's length,
        /// as Sensible Soccer shows. A technical reference; <c>?maxview=</c> tries others.
        /// </summary>
        public const float MaxHalfExtent = 17.5f;
        private const float PitchHalfWidth = 34f;
        private const float LineWidth = 0.12f;
        private const float PlayerDiameter = 1.8f;
        private const float BallExaggeration = 3f;
        private const float HeightLift = 0.5f;
        private const float HeightGrowth = 0.08f;

        // The posts and crossbar are drawn this many times their width, so 12 cm reads on a phone.
        private const float FrameExaggeration = 2.5f;

        private SimConfig _config;
        private readonly Camera _camera;
        private readonly Transform _player;
        private readonly Transform _keeper;
        private readonly Transform _ball;
        private readonly Transform _ballShadow;
        private readonly Transform _crossbar;
        private readonly Transform _rightPost;
        private readonly Transform _leftPost;

        // The match's far goal and opponents (#87), shown only in a match.
        private readonly Transform _farCrossbar;
        private readonly Transform _farRightPost;
        private readonly Transform _farLeftPost;
        private readonly Transform _opponent;
        private readonly Transform _opponentKeeper;

        // A 2v2 match's other footballers (#95): the controlled player's teammates in his colour,
        // the opponents beyond the first in theirs, made as a match needs them. The marker is a halo
        // under the controlled player, so who the human has reads at a glance (SWOS's marker).
        private readonly Transform _root;
        private readonly Sprite _discSprite;
        private readonly System.Collections.Generic.List<Transform> _mates = new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<Transform> _opponents = new System.Collections.Generic.List<Transform>();
        private readonly Transform _marker;

        // Who SWITCH would take (#136), a faint halo; and the pulse of the controlled one's marker
        // when control moved on its own, from 1 down to 0 over PulseSeconds.
        private readonly Transform _nextMarker;
        private float _pulse;
        private const float PulseSeconds = 0.5f;
        private int _perSide = 1;

        // The off-screen cues (#102): a pointer at the play area's edge for each footballer just
        // outside it, in his side's colour, made as they are needed. Drawn over the play, under
        // the HUD; they take no touch, since the pitch's sprites never do.
        private readonly System.Collections.Generic.List<SpriteRenderer> _cues = new System.Collections.Generic.List<SpriteRenderer>();
        private Sprite _cueSprite;
        private int _cuesShown;

        // The play area the cues were last placed in, and where on screen each went: the trace
        // reports these, not a re-projection through a camera or a layout that may have changed
        // since (the half-time turn happens between a frame and the next step's trace).
        private Rect _cuedIn;
        private readonly System.Collections.Generic.List<string> _placed = new System.Collections.Generic.List<string>();

        // The action sprites (#101): each kit's poses, generated at start from ActionSprites' grids,
        // in point filtering so they stay crisp pixels. Discs with ?sprites=0, as before.
        private readonly System.Collections.Generic.Dictionary<Color, Sprite[]> _poses = new System.Collections.Generic.Dictionary<Color, Sprite[]>();
        private readonly System.Collections.Generic.List<Transform> _sheet = new System.Collections.Generic.List<Transform>();

        /// <summary>m. A sprite's side: its body, about ten of its sixteen pixels across, is a footballer's diameter.</summary>
        private const float SpriteSide = PlayerDiameter * ActionSprites.Size / 10f;

        /// <summary>Whether footballers are drawn as action sprites (#101) rather than discs.</summary>
        public bool Sprites { get; set; } = true;

        /// <summary>m. How far outside the view a footballer is cued; 0 for none (<c>?cues=</c>, #102).</summary>
        public float CueReach { get; set; } = (float)Eleven.Presentation.EdgeCues.Reach;

        /// <summary>The HUD's scale, for the cues' sizes in points.</summary>
        public float PixelsPerPoint { get; set; } = 1;

        /// <summary>For the trace: how many cues the last frame drew.</summary>
        public int CuesShown => _cuesShown;

        private float _ballDiameter;
        private Vector3 _ballShown;
        private PitchOrientation _orientation = PitchOrientation.Practice;
        private readonly float _minAhead;
        private readonly float _maxHalfExtent;
        private float _ahead;
        private float _across;
        private float _pixelsPerMetre;

        /// <summary>
        /// The camera's easing (#139). Its references: out 0.3 s, in 1.5 s, a 10 % dead zone, the
        /// centre panning at 0.25 s; <c>?zoomOut=</c>, <c>?zoomIn=</c>, <c>?zoomHold=</c> and
        /// <c>?pan=</c> set them, and 0 follows at once, as before.
        /// </summary>
        public CameraEase Ease { get; } = new CameraEase { OutSeconds = 0.3, InSeconds = 1.5, Hold = 0.1, PanSeconds = 0.25 };

        private float _zoomTravel, _fittedTravel, _fittedZoom;

        /// <summary>
        /// For the trace (#139): how far the zoom has moved since the last call, shown and as fitted
        /// unsmoothed (the camera before the easing), each the sum of every frame's change in its
        /// logarithm (0.69 is a doubling or a halving); then both start again.
        /// </summary>
        public (float Shown, float Fitted) TakeZoomTravel()
        {
            (float, float) travel = (_zoomTravel, _fittedTravel);
            _zoomTravel = _fittedTravel = 0;
            return travel;
        }

        /// <summary>Whether play is paused or waiting (#139): the camera's easing holds, as the play does.</summary>
        public bool Frozen { get; set; }

        /// <param name="minAhead">
        /// The least the play shows up-field either side of the action, in metres (#72, <c>?ahead=</c>);
        /// the camera zooms out to give it. The reference is the closest zoom's, so it changes nothing.
        /// </param>
        /// <param name="maxHalfExtent">The widest view, in metres either side of the play area's centre on its shorter side (#31, <c>?maxview=</c>).</param>
        public PitchView(SimConfig config, Transform root, Mowing mowing = Mowing.Crosscut, float minAhead = MinHalfExtent, float maxHalfExtent = MaxHalfExtent)
        {
            Grass = mowing;
            _minAhead = Mathf.Max(MinHalfExtent, minAhead);
            _maxHalfExtent = Mathf.Max(MinHalfExtent, maxHalfExtent);
            _config = config;
            Disc = MakeDiscTexture(64);
            Sprite disc = Sprite.Create(Disc, new Rect(0, 0, Disc.width, Disc.height), new Vector2(0.5f, 0.5f), Disc.width);
            Sprite square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);

            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(root, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = MinHalfExtent;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = HostPalette.Pitch;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(0, 0, -10);
            Orient(_orientation);

            float goalLine = (float)config.GoalLineX;
            float length = goalLine * 2;
            if (mowing != Mowing.Plain) Mow(root, mowing, new Vector2(length, PitchHalfWidth * 2));
            // The goal lines lie inside the pitch: GoalLineX is the line's outer edge (#68).
            Line(root, square, "Goal line", new Vector2(goalLine - LineWidth / 2, 0), new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Far goal line", new Vector2(-goalLine + LineWidth / 2, 0), new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Halfway line", Vector2.zero, new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Touchline", new Vector2(0, PitchHalfWidth), new Vector2(length, LineWidth));
            Line(root, square, "Far touchline", new Vector2(0, -PitchHalfWidth), new Vector2(length, LineWidth));

            // The goal (#68), from the simulation's geometry: seen from above, the crossbar is a
            // thicker stretch of the goal line between the two posts. White, as Law 1 asks.
            _crossbar = Shape(root, square, "Crossbar", HostPalette.PitchLine, 2, 1);
            _rightPost = Shape(root, disc, "Right post", HostPalette.PitchLine, 2, 1);
            _leftPost = Shape(root, disc, "Left post", HostPalette.PitchLine, 2, 1);
            _farCrossbar = Shape(root, square, "Far crossbar", HostPalette.PitchLine, 2, 1);
            _farRightPost = Shape(root, disc, "Far right post", HostPalette.PitchLine, 2, 1);
            _farLeftPost = Shape(root, disc, "Far left post", HostPalette.PitchLine, 2, 1);
            PlaceGoal(config);

            _ballDiameter = (float)config.BallRadius * 2 * BallExaggeration;
            _ballShadow = Shape(root, disc, "Ball shadow", HostPalette.BallShadow, 3, _ballDiameter);
            _root = root;
            _discSprite = disc;
            _marker = Shape(root, disc, "Controlled marker", HostPalette.Marker, 3, PlayerDiameter * MarkerScale);
            _marker.gameObject.SetActive(false);
            _nextMarker = Shape(root, disc, "Next marker", HostPalette.NextMarker, 3, PlayerDiameter * MarkerScale);
            _nextMarker.gameObject.SetActive(false);
            _player = Shape(root, disc, "Player", HostPalette.Player, 4, PlayerDiameter);
            _keeper = Shape(root, disc, "Keeper", HostPalette.Keeper, 4, PlayerDiameter);
            _keeper.gameObject.SetActive(false);
            _opponent = Shape(root, disc, "Opponent", HostPalette.Opponent, 4, PlayerDiameter);
            _opponentKeeper = Shape(root, disc, "Opponent keeper", HostPalette.OpponentKeeper, 4, PlayerDiameter);
            _opponents.Add(_opponent);
            _ball = Shape(root, disc, "Ball", HostPalette.Ball, 5, _ballDiameter);
            foreach (Rgba kit in new[] { Domain.Player, Domain.Keeper, Domain.Opponent, Domain.OpponentKeeper }) _poses[kit.ToColor()] = MakePoses(kit);
            Texture2D pointer = MakePointerTexture(32);
            _cueSprite = Sprite.Create(pointer, new Rect(0, 0, pointer.width, pointer.height), new Vector2(0.5f, 0.5f), pointer.width);
            ShowMatch(false);
        }

        /// <summary>For the trace: the goal as drawn, from the simulation's geometry.</summary>
        public string Goal => System.FormattableString.Invariant($"{_config.GoalWidth:F2}x{_config.GoalHeight:F2} posts={_config.PostWidth:F2} at={_config.FrameAxisX:F2},{_config.PostAxisY:F2}");

        // The posts on their axes, the crossbar between them, at the simulation's sizes.
        private void PlaceGoal(SimConfig config)
        {
            float x = (float)config.FrameAxisX, y = (float)config.PostAxisY, width = (float)config.PostWidth * FrameExaggeration;
            _crossbar.localPosition = new Vector3(x, 0, 0);
            _crossbar.localScale = new Vector3(width * 0.6f, y * 2, 1);
            _rightPost.localPosition = new Vector3(x, -y, 0);
            _leftPost.localPosition = new Vector3(x, y, 0);
            _rightPost.localScale = _leftPost.localScale = Vector3.one * width;

            // The far goal is the near one turned half a turn about the centre spot (#82).
            _farCrossbar.localPosition = new Vector3(-x, 0, 0);
            _farCrossbar.localScale = _crossbar.localScale;
            _farRightPost.localPosition = new Vector3(-x, y, 0);
            _farLeftPost.localPosition = new Vector3(-x, -y, 0);
            _farRightPost.localScale = _farLeftPost.localScale = _rightPost.localScale;
        }

        /// <summary>The marker's diameter, as a multiple of a footballer's.</summary>
        private const float MarkerScale = 1.5f;

        /// <summary>
        /// A match's pitch (#87): the far goal and the opponents shown; practice hides them. With
        /// <paramref name="perSide"/> outfielders a side (#95), his teammates too, and the marker
        /// once he has a teammate to tell him from.
        /// </summary>
        public void ShowMatch(bool match, int perSide = 1)
        {
            Ease.Snap();
            _perSide = match ? perSide : 1;
            _farCrossbar.gameObject.SetActive(match);
            _farRightPost.gameObject.SetActive(match);
            _farLeftPost.gameObject.SetActive(match);
            _opponentKeeper.gameObject.SetActive(match);
            while (_mates.Count < _perSide - 1) _mates.Add(Shape(_root, _discSprite, $"Teammate {_mates.Count + 1}", HostPalette.Player, 4, PlayerDiameter));
            while (_opponents.Count < _perSide) _opponents.Add(Shape(_root, _discSprite, $"Opponent {_opponents.Count + 1}", HostPalette.Opponent, 4, PlayerDiameter));
            for (int i = 0; i < _mates.Count; i++) _mates[i].gameObject.SetActive(match && i < _perSide - 1);
            for (int i = 0; i < _opponents.Count; i++) _opponents[i].gameObject.SetActive(match && i < _perSide);
            _marker.gameObject.SetActive(match && _perSide > 1);
            _nextMarker.gameObject.SetActive(false);
            _pulse = 0;
        }

        /// <summary>
        /// Marks the teammate SWITCH would take (#136), or none. Always shown in a match with
        /// teammates, whatever the switching mode.
        /// </summary>
        public void ShowNext(Vector2? at)
        {
            bool shown = at.HasValue && _marker.gameObject.activeSelf;
            _nextMarker.gameObject.SetActive(shown);
            if (shown) _nextMarker.localPosition = new Vector3(at.Value.x, at.Value.y, 0);
        }

        /// <summary>Where the next marker is, in screen pixels from the top left, or "none": for the smoke.</summary>
        public string NextReport(in HudLayout layout)
        {
            if (!_nextMarker.gameObject.activeSelf) return "none";
            Vector3 s = _camera.WorldToScreenPoint(_nextMarker.localPosition);
            return System.FormattableString.Invariant($"{s.x:F0},{layout.ScreenHeight - s.y:F0}");
        }

        /// <summary>Control moved on its own (#136): the controlled marker swells and settles back. Not under reduced motion.</summary>
        public void Pulse()
        {
            if (!UiRoot.ReducedMotion) _pulse = 1;
        }

        /// <summary>Where a match's other footballers are drawn this frame (#87, #95): his teammates, the opponents' outfielders, their keeper.</summary>
        public void RenderSides(System.Collections.Generic.IReadOnlyList<Vector2> mates, System.Collections.Generic.IReadOnlyList<Vector2> opponents, Vector2 opponentKeeper)
        {
            for (int i = 0; i < mates.Count && i < _mates.Count; i++) _mates[i].localPosition = new Vector3(mates[i].x, mates[i].y, 0);
            for (int i = 0; i < opponents.Count && i < _opponents.Count; i++) _opponents[i].localPosition = new Vector3(opponents[i].x, opponents[i].y, 0);
            _opponentKeeper.localPosition = new Vector3(opponentKeeper.x, opponentKeeper.y, 0);
        }

        /// <summary>
        /// Draws a run under another config (#45, APPLY / RETRY): the ball's size follows its radius,
        /// and the goal its size (#68). The goal line is not tunable, so the pitch stays.
        /// </summary>
        public void Use(SimConfig config)
        {
            _config = config;
            PlaceGoal(config);
            _ballDiameter = (float)config.BallRadius * 2 * BallExaggeration;
            _ballShadow.localScale = Vector3.one * _ballDiameter;
            _ball.localScale = Vector3.one * _ballDiameter;
        }

        /// <summary>
        /// Shows the pitch with <paramref name="orientation"/>'s end up (#72): a half-time flip turns
        /// the view by half a turn. Presentation only: nothing in the simulation moves.
        /// </summary>
        public void Orient(PitchOrientation orientation)
        {
            Ease.Snap();
            _orientation = orientation;
            _camera.transform.rotation = Quaternion.Euler(0, 0, (float)orientation.CameraRoll);
        }

        public PitchOrientation Orientation => _orientation;

        /// <summary>A white anti-aliased disc, also used by the HUD.</summary>
        public Texture2D Disc { get; }

        /// <summary>How the grass is mown.</summary>
        public Mowing Grass { get; }

        /// <param name="keeper">The keeper's position (#69), or null when there is none.</param>
        /// <param name="ball">x, y on the pitch and z, the height of the ball's centre.</param>
        /// <param name="layout">The HUD, whose <see cref="HudLayout.PlayArea"/> the play is framed in.</param>
        public void Render(Vector2 player, Vector2? keeper, Vector3 ball, in HudLayout layout)
        {
            _player.localPosition = new Vector3(player.x, player.y, 0);
            _marker.localPosition = _player.localPosition;
            float pulseDt = Frozen ? 0 : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _pulse = Mathf.Max(0, _pulse - pulseDt / PulseSeconds);
            _marker.localScale = Vector3.one * (PlayerDiameter * MarkerScale * (1 + 0.6f * _pulse));
            _keeper.gameObject.SetActive(keeper.HasValue);
            if (keeper.HasValue) _keeper.localPosition = new Vector3(keeper.Value.x, keeper.Value.y, 0);
            _ballShadow.localPosition = new Vector3(ball.x, ball.y, 0);

            // Height is shown by lifting the ball up the screen and growing it slightly. Up the
            // screen is toward the attacked end (#72); the shadow stays on the ground.
            float height = Mathf.Max(0, ball.z - (float)_config.BallRadius);
            Vector2 up = World(new Vec2(0, 1));
            _ball.localPosition = new Vector3(ball.x + up.x * height * HeightLift, ball.y + up.y * height * HeightLift, 0);
            _ball.localScale = Vector3.one * (_ballDiameter * (1 + height * HeightGrowth));

            _ballShown = _ball.localPosition;
            Frame(player, new Vector2(_ballShown.x, _ballShown.y), layout);
            PlaceCues(layout);
            PlaceSheet(layout);
        }

        // A cue for each of the match's footballers drawn outside the play area, within the reach.
        private void PlaceCues(in HudLayout layout)
        {
            _cuedIn = layout.PlayArea;
            _placed.Clear();
            int shown = 0;
            if (_perSide > 0 && _opponentKeeper.gameObject.activeSelf)
            {
                for (int i = 0; i < _mates.Count; i++) Cue(_mates[i], HostPalette.Player, layout, ref shown);
                for (int i = 0; i < _opponents.Count; i++) Cue(_opponents[i], HostPalette.Opponent, layout, ref shown);
                Cue(_opponentKeeper, HostPalette.OpponentKeeper, layout, ref shown);
                Cue(_keeper, HostPalette.Keeper, layout, ref shown);
            }

            for (int i = shown; i < _cues.Count; i++) _cues[i].gameObject.SetActive(false);
            _cuesShown = shown;
        }

        private void Cue(Transform footballer, Color colour, in HudLayout layout, ref int shown)
        {
            if (!footballer.gameObject.activeSelf) return;
            Vector3 screen = _camera.WorldToScreenPoint(footballer.position);
            Rect play = layout.PlayArea;
            Eleven.Presentation.EdgeCue? cue = Eleven.Presentation.EdgeCues.Place(screen.x, layout.ScreenHeight - screen.y, play.x, play.y, play.width, play.height,
                _pixelsPerMetre, PixelsPerPoint, CueReach);
            if (!cue.HasValue) return;
            if (shown == _cues.Count)
            {
                Transform made = Shape(_root, _cueSprite, $"Cue {shown + 1}", colour, 6, 1);
                _cues.Add(made.GetComponent<SpriteRenderer>());
            }

            SpriteRenderer r = _cues[shown++];
            Eleven.Presentation.EdgeCue c = cue.Value;
            _placed.Add(System.FormattableString.Invariant($"{c.X:F0},{c.Y:F0},{c.Size:F0}"));
            r.color = colour;
            r.gameObject.SetActive(true);
            Vector3 world = _camera.ScreenToWorldPoint(new Vector3((float)c.X, layout.ScreenHeight - (float)c.Y, 10));
            r.transform.position = new Vector3(world.x, world.y, 0);
            r.transform.localScale = Vector3.one * ((float)c.Size / _pixelsPerMetre);
            // The cue's angle is on screen with y down; the sprite points right in the camera's frame.
            r.transform.rotation = Quaternion.Euler(0, 0, _camera.transform.eulerAngles.z - (float)c.Angle * Mathf.Rad2Deg);
        }

        /// <summary>One footballer's drawn action this frame (#101): his pose and the way it faces, in world radians.</summary>
        public readonly struct Drawn
        {
            public Drawn(Pose pose, double facing)
            {
                Pose = pose;
                Facing = facing;
            }

            public Pose Pose { get; }
            public double Facing { get; }
        }

        /// <summary>
        /// The poses of this frame's footballers (#101), in RenderSides' order: the controlled
        /// footballer, his keeper, his teammates, the opponents' outfielders and their keeper.
        /// Presentation only: chosen from the state, never written back.
        /// </summary>
        public void SetPoses(Drawn me, Drawn keeper, System.Collections.Generic.IReadOnlyList<Drawn> mates, System.Collections.Generic.IReadOnlyList<Drawn> opponents, Drawn theirKeeper)
        {
            Dress(_player, HostPalette.Player, me);
            Dress(_keeper, HostPalette.Keeper, keeper);
            for (int i = 0; i < mates.Count && i < _mates.Count; i++) Dress(_mates[i], HostPalette.Player, mates[i]);
            for (int i = 0; i < opponents.Count && i < _opponents.Count; i++) Dress(_opponents[i], HostPalette.Opponent, opponents[i]);
            Dress(_opponentKeeper, HostPalette.OpponentKeeper, theirKeeper);
        }

        private void Dress(Transform footballer, Color kit, Drawn drawn)
        {
            var r = footballer.GetComponent<SpriteRenderer>();
            if (!Sprites)
            {
                r.sprite = _discSprite;
                footballer.localScale = Vector3.one * PlayerDiameter;
                footballer.localRotation = Quaternion.identity;
                return;
            }

            r.sprite = _poses[kit][(int)drawn.Pose];
            r.color = Color.white;
            footballer.localScale = Vector3.one * SpriteSide;
            // The sprite faces up its sheet: turned to his facing, to the nearest eighth of a turn.
            footballer.localRotation = Quaternion.Euler(0, 0, ActionSprites.Facing(drawn.Facing) - 90);
        }

        /// <summary>
        /// The sprite sheet (#101, <c>?sheet=1</c>): every pose in every kit, facing up the screen,
        /// in a grid over the play area, for the screenshot regression. The footballers and the
        /// ball are hidden under it.
        /// </summary>
        public void ShowSheet(bool show)
        {
            if (show && _sheet.Count == 0)
            {
                foreach (Rgba kit in SheetKits)
                {
                    foreach (Pose pose in ActionSprites.All)
                    {
                        Transform t = Shape(_camera.transform, _poses[kit.ToColor()][(int)pose], $"Sheet {kit} {pose}", Color.white, 7, 1);
                        _sheet.Add(t);
                    }
                }
            }

            foreach (Transform t in _sheet) t.gameObject.SetActive(show);
            _player.gameObject.SetActive(!show);
            _ball.gameObject.SetActive(!show);
            _ballShadow.gameObject.SetActive(!show);
        }

        /// <summary>The sheet's kits, a row of poses each, in order.</summary>
        public static readonly Rgba[] SheetKits = { Domain.Player, Domain.Keeper, Domain.Opponent, Domain.OpponentKeeper };

        // The sheet's columns: whichever count gives the largest cells in the area.
        private int SheetColumns(in HudLayout layout)
        {
            Rect area = SheetArea(layout);
            int best = 1;
            float largest = 0;
            for (int columns = 1; columns <= Mathf.Max(1, _sheet.Count); columns++)
            {
                int rows = (_sheet.Count + columns - 1) / columns;
                float cell = Mathf.Min(area.width / columns, area.height / Mathf.Max(1, rows));
                if (cell > largest)
                {
                    largest = cell;
                    best = columns;
                }
            }

            return best;
        }

        /// <summary>For the smoke test (#101): each sheet sprite's centre and side, x,y,size in HUD pixels with y down, in the sheet's order.</summary>
        public string SheetReport(in HudLayout layout)
        {
            var parts = new System.Collections.Generic.List<string>();
            float cell = SheetCell(layout);
            Rect play = SheetArea(layout);
            int columns = SheetColumns(layout);
            for (int i = 0; i < _sheet.Count; i++)
            {
                float x = play.x + (i % columns + 0.5f) * cell, y = play.y + (i / columns + 0.5f) * cell;
                parts.Add(System.FormattableString.Invariant($"{x:F1},{y:F1},{cell * 0.8f:F1}"));
            }

            return string.Join(";", parts);
        }

        private float SheetCell(in HudLayout layout)
        {
            Rect area = SheetArea(layout);
            int columns = SheetColumns(layout);
            return Mathf.Min(area.width / columns, area.height / Mathf.Max(1, (_sheet.Count + columns - 1) / columns));
        }

        // The play area below the status line, which sits over its top (#53): the sheet is laid out
        // where nothing of the HUD covers it, as the match is framed.
        private static Rect SheetArea(in HudLayout layout)
        {
            Rect play = layout.PlayArea;
            float band = Mathf.Clamp(layout.StatusArea.yMax - play.yMin, 0, play.height * 0.5f);
            return new Rect(play.x, play.y + band, play.width, play.height - band);
        }

        /// <summary>For the smoke test: how many sheet sprites are shown.</summary>
        public int SheetShown => _sheet.FindAll(t => t.gameObject.activeSelf).Count;

        // The sheet's grid: a row of poses for each kit, inside the play area, upright on screen.
        private void PlaceSheet(in HudLayout layout)
        {
            if (_sheet.Count == 0 || !_sheet[0].gameObject.activeSelf) return;
            Rect play = SheetArea(layout);
            float cell = SheetCell(layout);
            int columns = SheetColumns(layout);
            for (int i = 0; i < _sheet.Count; i++)
            {
                float x = play.x + (i % columns + 0.5f) * cell, y = play.y + (i / columns + 0.5f) * cell;
                Vector3 world = _camera.ScreenToWorldPoint(new Vector3(x, layout.ScreenHeight - y, 10));
                _sheet[i].position = new Vector3(world.x, world.y, 0);
                _sheet[i].rotation = _camera.transform.rotation;
                _sheet[i].localScale = Vector3.one * (cell * 0.8f / _pixelsPerMetre);
            }
        }

        private static Sprite[] MakePoses(Rgba kit)
        {
            var sprites = new Sprite[ActionSprites.All.Length];
            foreach (Pose pose in ActionSprites.All)
            {
                Rgba[] pixels = ActionSprites.Draw(pose, kit);
                var colours = new Color32[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) colours[i] = new Color32(pixels[i].R, pixels[i].G, pixels[i].B, pixels[i].A);
                var texture = new Texture2D(ActionSprites.Size, ActionSprites.Size, TextureFormat.RGBA32, false)
                {
                    name = $"{pose} {kit}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixels32(colours);
                texture.Apply(false, true);
                sprites[(int)pose] = Sprite.Create(texture, new Rect(0, 0, ActionSprites.Size, ActionSprites.Size), new Vector2(0.5f, 0.5f), ActionSprites.Size);
            }

            return sprites;
        }

        /// <summary>
        /// For the smoke test (#102): the play area the cues were placed in, then each cue drawn as
        /// x,y,size in HUD pixels with y down.
        /// </summary>
        public string CueReport()
        {
            return System.FormattableString.Invariant($"play={_cuedIn.x:F0},{_cuedIn.y:F0},{_cuedIn.width:F0},{_cuedIn.height:F0} at={string.Join(";", _placed)}");
        }

        /// <summary>For the smoke test: whether the player and the drawn ball are inside the play area.</summary>
        public string Visibility(in HudLayout layout) =>
            $"player:{(InPlay(_player.localPosition, layout) ? "in" : "out")},ball:{(InPlay(_ballShown, layout) ? "in" : "out")}";

        /// <summary>
        /// For the smoke test (#72): the orientation, and where these fall on screen, in HUD pixels
        /// with y down: the player, the attacked end's goal-line centre, the practice goal's centre and
        /// its two posts. Then how far the play shows up-field and across, either side of the action.
        /// </summary>
        public string Report(in HudLayout layout)
        {
            int screenHeight = layout.ScreenHeight;
            string At(Vector3 world)
            {
                Vector3 s = _camera.WorldToScreenPoint(world);
                return System.FormattableString.Invariant($"{s.x:F0},{screenHeight - s.y:F0}");
            }

            var attacked = new Vector3((float)_config.GoalLineX * _orientation.Up, 0, 0);
            var goal = new Vector3((float)_config.FrameAxisX, 0, 0);
            return System.FormattableString.Invariant(
                $"orientation={_orientation} up={(_orientation.Up > 0 ? "+x" : "-x")} player={At(_player.localPosition)} attacked={At(attacked)} goal={At(goal)} posts={At(_leftPost.localPosition)};{At(_rightPost.localPosition)} ahead={_ahead:F1}m across={_across:F1}m ppm={_pixelsPerMetre:F2} playerPx={PlayerDiameter * _pixelsPerMetre:F1} ballPx={_ballDiameter * _pixelsPerMetre:F1} maxview={_maxHalfExtent:F1}m zoomOut={Ease.OutSeconds:F2} zoomIn={Ease.InSeconds:F2} zoomHold={Ease.Hold:F2} pan={Ease.PanSeconds:F2}");
        }

        /// <summary>
        /// Frames the player and the ball inside the HUD's play area: centred between them, zoomed
        /// out just enough to keep both in view with a margin. The play area's shorter side always
        /// shows at least <see cref="MinHalfExtent"/> metres either side of the centre, and its
        /// height, up-field, at least the least look-ahead. It never shows more than the widest
        /// view (#31): a ball further off than that leaves the frame, and the footballer stays in
        /// it, toward the edge the ball left by. Player and ball are kept below the
        /// status line, which sits over the top of the play area. The camera still renders the
        /// whole screen; it is shifted so the play lands in the uncovered part. All of it is worked
        /// out on screen, through the orientation (#72).
        /// </summary>
        private void Frame(Vector2 player, Vector2 ball, in HudLayout layout)
        {
            Rect play = layout.PlayArea;
            float dt = Frozen ? 0 : Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // The status line sits over the top of the play area (#53). On the vertical pitch that
            // is where the play runs up-field, so the action is framed in the part below it: a
            // message never covers the ball, and the pitch under it is more to see ahead (#72).
            float band = Mathf.Clamp(layout.StatusArea.yMax - play.yMin, 0, play.height * 0.5f);
            var framed = new Rect(play.x, play.y + band, play.width, play.height - band);

            // Pixels per metre: the closest zoom allowed, never past the widest view (#31).
            float closest = Mathf.Min(play.width, play.height) * 0.5f / MinHalfExtent;
            closest = Mathf.Min(closest, play.height * 0.5f / _minAhead);
            float widest = Mathf.Min(closest, Mathf.Min(play.width, play.height) * 0.5f / _maxHalfExtent);

            // Fitted and eased (#139) in CameraEase, on axes aligned with the screen.
            Vec2 p = _orientation.ToScreen(new Vec2(player.x, player.y)), b = _orientation.ToScreen(new Vec2(ball.x, ball.y));
            (double cx, double cy, double zoom) = Ease.Fit(p.X, p.Y, b.X, b.Y, framed.width * 0.5f, framed.height * 0.5f,
                closest, widest, dt);
            Vector2 centre = World(new Vec2(cx, cy));
            float pixelsPerMetre = (float)zoom;

            // The zoom as fitted, unsmoothed: what the camera showed before the easing (#139), for the trace.
            if (_fittedZoom > 0) _fittedTravel += Mathf.Abs(Mathf.Log((float)(Ease.Fitted / _fittedZoom)));
            _fittedZoom = (float)Ease.Fitted;
            if (_pixelsPerMetre > 0) _zoomTravel += Mathf.Abs(Mathf.Log(pixelsPerMetre / _pixelsPerMetre));
            _pixelsPerMetre = pixelsPerMetre;

            _camera.orthographicSize = layout.ScreenHeight * 0.5f / pixelsPerMetre;
            _ahead = (framed.height * 0.5f + band) / pixelsPerMetre;
            _across = play.width * 0.5f / pixelsPerMetre;

            // Shift so the pitch point `centre` appears at the framed area's centre. HUD y grows
            // downwards, the screen's up.
            Vector2 offset = framed.center - new Vector2(layout.ScreenWidth * 0.5f, layout.ScreenHeight * 0.5f);
            Vector2 cameraAt = centre - World(new Vec2(offset.x, -offset.y)) / pixelsPerMetre;
            _camera.transform.position = new Vector3(cameraAt.x, cameraAt.y, -10);
        }

        // A screen vector, +x right and +y up, as the world direction it shows.
        private Vector2 World(Vec2 screen)
        {
            Vec2 w = _orientation.ToWorld(screen);
            return new Vector2((float)w.X, (float)w.Y);
        }

        private bool InPlay(Vector3 world, in HudLayout layout)
        {
            Vector3 screen = _camera.WorldToScreenPoint(world);
            return layout.PlayArea.Contains(new Vector2(screen.x, layout.ScreenHeight - screen.y));
        }

        // The mown pattern under the lines (#58): only the mown squares (or bands) are drawn, in the
        // mown shade over the pitch's own colour, which the camera clears to. One mesh, one draw,
        // vertex colours and no texture of its own, so the grass costs half the screen's fill rather
        // than all of it: the CI's software renderer showed a full-screen tiled sprite costing a
        // third of the phone pass's frame rate. Cross-cut alternates both ways; stripes run along x
        // only, so their bands are parallel to the goal lines. The halfway line falls on a band edge.
        private static void Mow(Transform root, Mowing mowing, Vector2 size)
        {
            float halfX = size.x * 0.5f, halfY = size.y * 0.5f;
            int columns = Mathf.CeilToInt(halfX / MownBand), rows = Mathf.CeilToInt(halfY / MownBand);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = -columns; i < columns; i++)
            {
                if (mowing == Mowing.Stripes)
                {
                    if ((i & 1) == 0) Quad(vertices, triangles, i * MownBand, (i + 1) * MownBand, -halfY, halfY, halfX, halfY);
                    continue;
                }

                for (int j = -rows; j < rows; j++)
                {
                    if (((i + j) & 1) == 0) Quad(vertices, triangles, i * MownBand, (i + 1) * MownBand, j * MownBand, (j + 1) * MownBand, halfX, halfY);
                }
            }

            var mesh = new Mesh { name = "Mown grass" };
            mesh.SetVertices(vertices);
            var colours = new Color32[vertices.Count];
            for (int k = 0; k < colours.Length; k++) colours[k] = HostPalette.PitchMown;
            mesh.colors32 = colours;
            mesh.SetTriangles(triangles, 0);

            var grass = new GameObject("Mown grass");
            grass.transform.SetParent(root, false);
            grass.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = grass.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SpriteMaterial();
            renderer.sortingOrder = 0;
        }

        // The built-in sprite material every SpriteRenderer starts with: vertex colour times a white
        // texture, and always in the build, unlike a shader looked up by name.
        private static Material SpriteMaterial()
        {
            var probe = new GameObject("Sprite material probe");
            Material material = probe.AddComponent<SpriteRenderer>().sharedMaterial;
            Object.Destroy(probe);
            return material;
        }

        // One square of the pattern, clipped to the playing area.
        private static void Quad(List<Vector3> vertices, List<int> triangles, float x0, float x1, float y0, float y1, float halfX, float halfY)
        {
            x0 = Mathf.Max(x0, -halfX);
            x1 = Mathf.Min(x1, halfX);
            y0 = Mathf.Max(y0, -halfY);
            y1 = Mathf.Min(y1, halfY);
            if (x1 <= x0 || y1 <= y0) return;

            int first = vertices.Count;
            vertices.Add(new Vector3(x0, y0, 0));
            vertices.Add(new Vector3(x0, y1, 0));
            vertices.Add(new Vector3(x1, y1, 0));
            vertices.Add(new Vector3(x1, y0, 0));
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }

        private static void Line(Transform root, Sprite square, string name, Vector2 centre, Vector2 size)
        {
            Transform line = Shape(root, square, name, HostPalette.PitchLine, 1, 1);
            line.localPosition = new Vector3(centre.x, centre.y, 0);
            line.localScale = new Vector3(size.x, size.y, 1);
        }

        private static Transform Shape(Transform root, Sprite sprite, string name, Color colour, int order, float size)
        {
            var shape = new GameObject(name);
            shape.transform.SetParent(root, false);
            shape.transform.localScale = Vector3.one * size;
            var spriteRenderer = shape.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = colour;
            spriteRenderer.sortingOrder = order;
            return shape.transform;
        }

        // A white pointer, pointing right: a triangle with a soft edge, so a cue reads by its shape
        // before its colour.
        private static Texture2D MakePointerTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Pointer",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Inside when |y - half| is under the edge that narrows from the back to the tip.
                    float px = x + 0.5f, py = Mathf.Abs(y + 0.5f - half);
                    float edge = half * (1 - px / size);
                    float alpha = Mathf.Clamp01(edge - py) * Mathf.Clamp01(px) * Mathf.Clamp01(size - px);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D MakeDiscTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Disc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            float radius = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = new Vector2(x + 0.5f - radius, y + 0.5f - radius).magnitude;
                    float alpha = Mathf.Clamp01(radius - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
