using Eleven.Simulation;
using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// Draws the pitch, the player and the ball as simple shapes, top-down. Built entirely at
    /// runtime: no scene objects, prefabs or imported art. Pitch x runs right, y up the screen.
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

        private SimConfig _config;
        private readonly Camera _camera;
        private readonly Transform _player;
        private readonly Transform _ball;
        private readonly Transform _ballShadow;
        private float _ballDiameter;
        private Vector3 _ballShown;

        public PitchView(SimConfig config, Transform root, Mowing mowing = Mowing.Crosscut)
        {
            Grass = mowing;
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

            float goalLine = (float)config.GoalLineX;
            float length = goalLine * 2;
            if (mowing != Mowing.Plain) Mow(root, mowing, new Vector2(length, PitchHalfWidth * 2));
            Line(root, square, "Goal line", new Vector2(goalLine, 0), new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Far goal line", new Vector2(-goalLine, 0), new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Halfway line", Vector2.zero, new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Touchline", new Vector2(0, PitchHalfWidth), new Vector2(length, LineWidth));
            Line(root, square, "Far touchline", new Vector2(0, -PitchHalfWidth), new Vector2(length, LineWidth));

            _ballDiameter = (float)config.BallRadius * 2 * BallExaggeration;
            _ballShadow = Shape(root, disc, "Ball shadow", HostPalette.BallShadow, 2, _ballDiameter);
            _player = Shape(root, disc, "Player", HostPalette.Player, 3, PlayerDiameter);
            _ball = Shape(root, disc, "Ball", HostPalette.Ball, 4, _ballDiameter);
        }

        /// <summary>
        /// Draws a run under another config (#45, APPLY / RETRY): the ball's size follows its radius.
        /// The goal line is not tunable, so the pitch stays.
        /// </summary>
        public void Use(SimConfig config)
        {
            _config = config;
            _ballDiameter = (float)config.BallRadius * 2 * BallExaggeration;
            _ballShadow.localScale = Vector3.one * _ballDiameter;
            _ball.localScale = Vector3.one * _ballDiameter;
        }

        /// <summary>A white anti-aliased disc, also used by the HUD.</summary>
        public Texture2D Disc { get; }

        /// <summary>How the grass is mown.</summary>
        public Mowing Grass { get; }

        /// <param name="ball">x, y on the pitch and z, the height of the ball's centre.</param>
        /// <param name="layout">The HUD, whose <see cref="HudLayout.PlayArea"/> the play is framed in.</param>
        public void Render(Vector2 player, Vector3 ball, in HudLayout layout)
        {
            _player.localPosition = new Vector3(player.x, player.y, 0);
            _ballShadow.localPosition = new Vector3(ball.x, ball.y, 0);

            // Height is shown by lifting the ball up the screen and growing it slightly.
            float height = Mathf.Max(0, ball.z - (float)_config.BallRadius);
            _ball.localPosition = new Vector3(ball.x, ball.y + height * HeightLift, 0);
            _ball.localScale = Vector3.one * (_ballDiameter * (1 + height * HeightGrowth));

            _ballShown = _ball.localPosition;
            Frame(player, new Vector2(_ballShown.x, _ballShown.y), layout);
        }

        /// <summary>For the smoke test: whether the player and the drawn ball are inside the play area.</summary>
        public string Visibility(in HudLayout layout) =>
            $"player:{(InPlay(_player.localPosition, layout) ? "in" : "out")},ball:{(InPlay(_ballShown, layout) ? "in" : "out")}";

        /// <summary>
        /// Frames the player and the ball inside the HUD's play area: centred between them, zoomed
        /// out just enough to keep both in view with a margin. The play area's shorter side always
        /// shows at least <see cref="MinHalfExtent"/> metres either side of the centre. The camera
        /// still renders the whole screen; it is shifted so the play lands in the uncovered part.
        /// </summary>
        private void Frame(Vector2 player, Vector2 ball, in HudLayout layout)
        {
            Rect play = layout.PlayArea;
            Vector2 centre = Vector2.Lerp(player, ball, 0.5f);
            Vector2 halfSpan = new Vector2(Mathf.Abs(ball.x - player.x), Mathf.Abs(ball.y - player.y)) * 0.5f;

            // Pixels per metre: the closest zoom allowed, reduced until both fit.
            float pixelsPerMetre = Mathf.Min(play.width, play.height) * 0.5f / MinHalfExtent;
            pixelsPerMetre = Mathf.Min(pixelsPerMetre, play.width * 0.5f / (halfSpan.x + FramingMargin));
            pixelsPerMetre = Mathf.Min(pixelsPerMetre, play.height * 0.5f / (halfSpan.y + FramingMargin));
            _camera.orthographicSize = layout.ScreenHeight * 0.5f / pixelsPerMetre;

            // Shift so the pitch point `centre` appears at the play area's centre. HUD y grows
            // downwards, the pitch's upwards.
            Vector2 offset = play.center - new Vector2(layout.ScreenWidth * 0.5f, layout.ScreenHeight * 0.5f);
            Vector2 cameraAt = centre - new Vector2(offset.x, -offset.y) / pixelsPerMetre;
            _camera.transform.position = new Vector3(cameraAt.x, cameraAt.y, -10);
        }

        private bool InPlay(Vector3 world, in HudLayout layout)
        {
            Vector3 screen = _camera.WorldToScreenPoint(world);
            return layout.PlayArea.Contains(new Vector2(screen.x, layout.ScreenHeight - screen.y));
        }

        // The mown pattern under the lines: one texel per band, point-sampled so the bands keep crisp
        // edges, tiled across the playing area. Cross-cut alternates both ways, stripes along x only,
        // so their bands run parallel to the goal lines. The halfway line falls on a band edge.
        private static void Mow(Transform root, Mowing mowing, Vector2 size)
        {
            int rows = mowing == Mowing.Crosscut ? 2 : 1;
            var texture = new Texture2D(2, rows, TextureFormat.RGBA32, false)
            {
                name = "Mown grass",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            Color32 pitch = HostPalette.Pitch;
            Color32 mown = HostPalette.PitchMown;
            texture.SetPixels32(rows == 2 ? new[] { mown, pitch, pitch, mown } : new[] { mown, pitch });
            texture.Apply(false, true);

            Sprite tile = Sprite.Create(texture, new Rect(0, 0, 2, rows), new Vector2(0.5f, 0.5f), 1f / MownBand, 0, SpriteMeshType.FullRect);
            var grass = new GameObject("Mown grass");
            grass.transform.SetParent(root, false);
            var renderer = grass.AddComponent<SpriteRenderer>();
            renderer.sprite = tile;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = size;
            renderer.sortingOrder = 0;
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
