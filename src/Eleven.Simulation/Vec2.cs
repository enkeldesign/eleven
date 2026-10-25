namespace Eleven.Simulation
{
    /// <summary>A 2D vector on the pitch plane, in metres or metres per second.</summary>
    public readonly struct Vec2
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);

        public double LengthSquared => X * X + Y * Y;

        public override string ToString() => $"({X}, {Y})";
    }
}
