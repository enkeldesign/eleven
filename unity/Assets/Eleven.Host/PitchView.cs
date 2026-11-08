using System.Collections.Generic;
using Eleven.Simulation;
using Eleven.Simulation.Harness;
using UnityEngine;

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
        private const float FramingMargin = 3f;
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
        private float _ballDiameter;
        private Vector3 _ballShown;
        private PitchOrientation _orientation = PitchOrientation.Practice;
        private readonly float _minAhead;
        private float _ahead;
        private float _across;

        /// <param name="minAhead">
        /// The least the play shows up-field either side of the action, in metres (#72, <c>?ahead=</c>);
        /// the camera zooms out to give it. The reference is the closest zoom's, so it changes nothing.
        /// </param>
        public PitchView(SimConfig config, Transform root, Mowing mowing = Mowing.Crosscut, float minAhead = MinHalfExtent)
        {
            Grass = mowing;
            _minAhead = Mathf.Max(MinHalfExtent, minAhead);
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
            PlaceGoal(config);

            _ballDiameter = (float)config.BallRadius * 2 * BallExaggeration;
            _ballShadow = Shape(root, disc, "Ball shadow", HostPalette.BallShadow, 3, _ballDiameter);
            _player = Shape(root, disc, "Player", HostPalette.Player, 4, PlayerDiameter);
            _keeper = Shape(root, disc, "Keeper", HostPalette.Keeper, 4, PlayerDiameter);
            _keeper.gameObject.SetActive(false);
            _ball = Shape(root, disc, "Ball", HostPalette.Ball, 5, _ballDiameter);
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
                $"orientation={_orientation} up={(_orientation.Up > 0 ? "+x" : "-x")} player={At(_player.localPosition)} attacked={At(attacked)} goal={At(goal)} posts={At(_leftPost.localPosition)};{At(_rightPost.localPosition)} ahead={_ahead:F1}m across={_across:F1}m");
        }

        /// <summary>
        /// Frames the player and the ball inside the HUD's play area: centred between them, zoomed
        /// out just enough to keep both in view with a margin. The play area's shorter side always
        /// shows at least <see cref="MinHalfExtent"/> metres either side of the centre, and its
        /// height, up-field, at least the least look-ahead. Player and ball are kept below the
        /// status line, which sits over the top of the play area. The camera still renders the
        /// whole screen; it is shifted so the play lands in the uncovered part. All of it is worked
        /// out on screen, through the orientation (#72).
        /// </summary>
        private void Frame(Vector2 player, Vector2 ball, in HudLayout layout)
        {
            Rect play = layout.PlayArea;
            Vector2 centre = Vector2.Lerp(player, ball, 0.5f);
            Vec2 span = _orientation.ToScreen(new Vec2(ball.x - player.x, ball.y - player.y));
            Vector2 halfSpan = new Vector2(Mathf.Abs((float)span.X), Mathf.Abs((float)span.Y)) * 0.5f;

            // The status line sits over the top of the play area (#53). On the vertical pitch that
            // is where the play runs up-field, so the action is framed in the part below it: a
            // message never covers the ball, and the pitch under it is more to see ahead (#72).
            float band = Mathf.Clamp(layout.StatusArea.yMax - play.yMin, 0, play.height * 0.5f);
            var framed = new Rect(play.x, play.y + band, play.width, play.height - band);

            // Pixels per metre: the closest zoom allowed, reduced until both fit.
            float pixelsPerMetre = Mathf.Min(play.width, play.height) * 0.5f / MinHalfExtent;
            pixelsPerMetre = Mathf.Min(pixelsPerMetre, play.height * 0.5f / _minAhead);
            pixelsPerMetre = Mathf.Min(pixelsPerMetre, framed.width * 0.5f / (halfSpan.x + FramingMargin));
            pixelsPerMetre = Mathf.Min(pixelsPerMetre, framed.height * 0.5f / (halfSpan.y + FramingMargin));
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
