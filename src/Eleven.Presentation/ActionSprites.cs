using System;

namespace Eleven.Presentation
{
    /// <summary>
    /// A footballer's drawn action (#101): chosen from the simulation's state each frame and
    /// never written back. Numeric values are the sprite sheet's order.
    /// </summary>
    public enum Pose : byte
    {
        Stand = 0,
        RunA = 1,
        RunB = 2,
        Receive = 3,
        Kick = 4,
        Shield = 5,
        Slide = 6,
        Grounded = 7,
        Header = 8,
        Volley = 9,
        KeeperDive = 10,
        KeeperHold = 11,
    }

    /// <summary>
    /// The first action sprites (#101): top-down, low-res pixel footballers, generated at start from
    /// the grids below (no external assets). Each faces up the sheet; the host turns them to the
    /// footballer's heading in eight steps. A silhouette reads before a colour: the outline is ink,
    /// and the kit is the side's domain token, unchanged. Technical references, not final art (M7).
    /// </summary>
    public static class ActionSprites
    {
        /// <summary>px. A sprite's width and height.</summary>
        public const int Size = 16;

        public static readonly Pose[] All = (Pose[])Enum.GetValues(typeof(Pose));

        /// <summary>The fixed colours: the outline, hair, skin, boots and gloves. The kit and its shorts come from the side.</summary>
        public static readonly Rgba Outline = Primitives.Ink900;
        public static readonly Rgba Hair = Rgba.Hex("#3B2A20");
        public static readonly Rgba Skin = Rgba.Hex("#D9A066");
        public static readonly Rgba Boot = Rgba.Hex("#2A2A2A");
        public static readonly Rgba Glove = Primitives.Chalk50;

        // '.' clear, 'o' outline, 'k' kit, 's' shorts, 'h' hair, 'f' skin, 'b' boot, 'g' glove.
        // Row 0 is the sprite's top: the way he faces.
        private static readonly string[][] Grids =
        {
            new[] // Stand
            {
                "................",
                "................",
                "................",
                "................",
                ".....bb..bb.....",
                "....oooooooo....",
                "...okkkhhkkko...",
                "..fokkhhhhkkof..",
                "..fokkhhhhkkof..",
                "...okkkhhkkko...",
                "....osssssso....",
                ".....oooooo.....",
                "................",
                "................",
                "................",
                "................",
            },
            new[] // RunA: the left foot ahead, the right behind, the arms swinging
            {
                "................",
                "................",
                ".....bb.........",
                ".....bb.........",
                ".....ss......f..",
                "....oooooooo.f..",
                "...okkkhhkkko...",
                "...okkhhhhkko...",
                "..fokkhhhhkko...",
                "..f.okkhhkko....",
                "....osssssso....",
                ".....oooooo.....",
                "..........ss....",
                "..........bb....",
                "..........bb....",
                "................",
            },
            new[] // RunB: the other stride
            {
                "................",
                "................",
                "..........bb....",
                "..........bb....",
                "..f.......ss....",
                "..f.oooooooo....",
                "...okkkhhkkko...",
                "...okkhhhhkko...",
                "...okkhhhhkkof..",
                "....okkhhkko.f..",
                "....osssssso....",
                ".....oooooo.....",
                ".....ss.........",
                ".....bb.........",
                ".....bb.........",
                "................",
            },
            new[] // Receive: a foot up to meet the ball, the arms out for balance
            {
                "................",
                "................",
                "................",
                ".......bb.......",
                ".......ss.......",
                "....oooooooo....",
                "ff.okkkhhkkko.ff",
                ".ffokkhhhhkkoff.",
                "...okkhhhhkko...",
                "...okkkhhkkko...",
                "....osssssso....",
                ".....oooooo.....",
                "..........bb....",
                "................",
                "................",
                "................",
            },
            new[] // Kick: the kicking leg swung through, long ahead of him
            {
                "......bb........",
                "......bb........",
                "......ss........",
                "......ss........",
                "....oooooooo....",
                "...okkkhhkkko...",
                "..fokkhhhhkkof..",
                "..fokkhhhhkkof..",
                "...okkkhhkkko...",
                "....osssssso....",
                ".....oooooo.....",
                "..........bb....",
                "..........bb....",
                "................",
                "................",
                "................",
            },
            new[] // Shield: wide, low, the arms out, the feet planted apart
            {
                "................",
                "................",
                "................",
                "................",
                "...oooooooooo...",
                "..okkkkhhkkkko..",
                "ffokkkhhhhkkkoff",
                "ffokkkhhhhkkkoff",
                "..okkkkhhkkkko..",
                "...osssssssso...",
                "....oooooooo....",
                "..bb........bb..",
                "..bb........bb..",
                "................",
                "................",
                "................",
            },
            new[] // Slide: feet first along the ground, the body stretched out behind
            {
                ".......bb.......",
                ".......bb.......",
                ".......ss.......",
                ".......ss.......",
                "......ssss......",
                ".....oooooo.....",
                ".....okkkko.....",
                "....fokkkkof....",
                "....fokkkkof....",
                ".....okkkko.....",
                ".....ohhhho.....",
                ".....ohhhho.....",
                "......oooo......",
                "................",
                "................",
                "................",
            },
            new[] // Grounded: down on his side across his line
            {
                "................",
                "................",
                "................",
                "................",
                "................",
                ".oooo.oooooo....",
                "ohhhhokkkkkksbb.",
                "ohhhhokkkkkksbb.",
                ".oooo.oofoooo...",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
            },
            new[] // Header: up off the ground, the head thrust forward, the arms out
            {
                "................",
                "................",
                ".....oooooo.....",
                "....ohhhhhho....",
                "....ohhhhhho....",
                "....ohhhhhho....",
                "..ooookkkkoooo..",
                "ffokkkkkkkkkkoff",
                "..okkkkkkkkkko..",
                "...okkkkkkkko...",
                "....osssssso....",
                ".....oooooo.....",
                ".....bb..bb.....",
                "................",
                "................",
                "................",
            },
            new[] // Volley: the kicking leg up and across, off the ground
            {
                "................",
                "..........bb....",
                ".........bb.....",
                "........ss......",
                ".......ss.......",
                "....oooooooo....",
                "...okkkhhkkko.f.",
                "..fokkhhhhkkof..",
                ".f.okkhhhhkko...",
                "...okkkhhkkko...",
                "....osssssso....",
                ".....oooooo.....",
                ".....bb.........",
                "................",
                "................",
                "................",
            },
            new[] // KeeperDive: stretched full length sideways, gloves leading
            {
                "................",
                "................",
                "................",
                "................",
                "................",
                "gg.oooooooooo...",
                "ggfokkkkkkkkoooo",
                "..fokhhhkkkksbb.",
                "ggfokhhhkkkksbb.",
                "gg.oooooooooooo.",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
            },
            new[] // KeeperHold: the ball gathered to his chest in both gloves
            {
                "................",
                "................",
                "................",
                "......gggg......",
                ".....gf..fg.....",
                "....offoofo.....",
                "...okkkhhkkko...",
                "...okkhhhhkko...",
                "...okkhhhhkko...",
                "...okkkhhkkko...",
                "....osssssso....",
                ".....oooooo.....",
                ".....bb..bb.....",
                "................",
                "................",
                "................",
            },
        };

        /// <summary>The kit's shorts: the kit darkened, so the body reads in two tones.</summary>
        public static Rgba Shorts(Rgba kit) => new Rgba((byte)(kit.R * 0.6), (byte)(kit.G * 0.6), (byte)(kit.B * 0.6));

        /// <summary>
        /// One sprite, facing up, in <paramref name="kit"/>: <see cref="Size"/> × <see cref="Size"/>
        /// pixels, row by row from the bottom (as a texture's are), clear where nothing is drawn.
        /// </summary>
        public static Rgba[] Draw(Pose pose, Rgba kit)
        {
            string[] grid = Grids[(int)pose];
            var pixels = new Rgba[Size * Size];
            Rgba shorts = Shorts(kit);
            for (int row = 0; row < Size; row++)
            {
                for (int column = 0; column < Size; column++)
                {
                    Rgba c = grid[row][column] switch
                    {
                        'o' => Outline,
                        'k' => kit,
                        's' => shorts,
                        'h' => Hair,
                        'f' => Skin,
                        'b' => Boot,
                        'g' => Glove,
                        _ => new Rgba(0, 0, 0, 0),
                    };
                    pixels[(Size - 1 - row) * Size + column] = c;
                }
            }

            return pixels;
        }

        /// <summary>
        /// The pose's grid row, for tests and the docs: '.' clear and a letter for each colour.
        /// </summary>
        public static string Row(Pose pose, int row) => Grids[(int)pose][row];

        /// <summary>
        /// The eight drawn facings (#101): a heading in radians on screen, counter-clockwise from
        /// up, to the nearest eighth of a turn, in degrees for the sprite's turn.
        /// </summary>
        public static float Facing(double screenRadians)
        {
            double eighths = Math.Round(screenRadians / (Math.PI / 4));
            return (float)(eighths * 45 % 360 + 360) % 360;
        }
    }
}
