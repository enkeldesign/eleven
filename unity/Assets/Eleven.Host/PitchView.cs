using Eleven.Simulation;
using UnityEngine;

namespace Eleven.Host
{
    /// <summary>
    /// Draws the pitch, the dot and the ball as simple shapes, top-down. Built entirely at
    /// runtime: no scene objects, prefabs or imported art. Pitch x runs right, y up the screen.
    ///
    /// Presentation only: it reads interpolated positions and never feeds anything back into the
    /// simulation (INV-12).
    /// </summary>
    internal sealed class PitchView
    {
        // Presentation constants, not simulation config.
        private const float CameraHalfHeight = 9f;
        private const float PitchHalfWidth = 34f;
        private const float LineWidth = 0.12f;
        private const float DotDiameter = 0.9f;
        private const float BallExaggeration = 3f;
        private const float HeightLift = 0.5f;
        private const float HeightGrowth = 0.08f;

        private readonly SimConfig _config;
        private readonly Camera _camera;
        private readonly Transform _dot;
        private readonly Transform _ball;
        private readonly Transform _ballShadow;
        private readonly float _ballDiameter;

        public PitchView(SimConfig config, Transform root)
        {
            _config = config;
            Disc = MakeDiscTexture(64);
            Sprite disc = Sprite.Create(Disc, new Rect(0, 0, Disc.width, Disc.height), new Vector2(0.5f, 0.5f), Disc.width);
            Sprite square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);

            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(root, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = CameraHalfHeight;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = HostPalette.Pitch;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(0, 0, -10);

            float goalLine = (float)config.GoalLineX;
            float length = goalLine * 2;
            Line(root, square, "Goal line", new Vector2(goalLine, 0), new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Far goal line", new Vector2(-goalLine, 0), new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Halfway line", Vector2.zero, new Vector2(LineWidth, PitchHalfWidth * 2));
            Line(root, square, "Touchline", new Vector2(0, PitchHalfWidth), new Vector2(length, LineWidth));
            Line(root, square, "Far touchline", new Vector2(0, -PitchHalfWidth), new Vector2(length, LineWidth));

            _ballDiameter = (float)config.BallRadius * 2 * BallExaggeration;
            _ballShadow = Shape(root, disc, "Ball shadow", HostPalette.BallShadow, 2, _ballDiameter);
            _dot = Shape(root, disc, "Dot", HostPalette.Dot, 3, DotDiameter);
            _ball = Shape(root, disc, "Ball", HostPalette.Ball, 4, _ballDiameter);
        }

        /// <summary>A white anti-aliased disc, also used by the HUD.</summary>
        public Texture2D Disc { get; }

        /// <param name="ball">x, y on the pitch and z, the height of the ball's centre.</param>
        public void Render(Vector2 dot, Vector3 ball)
        {
            _dot.localPosition = new Vector3(dot.x, dot.y, 0);
            _ballShadow.localPosition = new Vector3(ball.x, ball.y, 0);

            // Height is shown by lifting the ball up the screen and growing it slightly.
            float height = Mathf.Max(0, ball.z - (float)_config.BallRadius);
            _ball.localPosition = new Vector3(ball.x, ball.y + height * HeightLift, 0);
            _ball.localScale = Vector3.one * (_ballDiameter * (1 + height * HeightGrowth));

            Vector2 focus = Vector2.Lerp(dot, new Vector2(ball.x, ball.y), 0.5f);
            _camera.transform.position = new Vector3(focus.x, focus.y, -10);
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
