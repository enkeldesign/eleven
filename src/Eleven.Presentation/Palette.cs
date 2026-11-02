namespace Eleven.Presentation
{
    /// <summary>
    /// ELEVEN's primitive palette (#48): matchday, pitch, programme and floodlights. It is a design
    /// hypothesis for Erik's device review, not an approved brand palette. Components never read
    /// these directly; they read <see cref="Role"/>s through a <see cref="Theme"/>.
    /// </summary>
    public static class Primitives
    {
        /// <summary>Cool match-programme and touchline neutral.</summary>
        public static readonly Rgba Chalk50 = default;

        /// <summary>Raised light surface.</summary>
        public static readonly Rgba Paper0 = default;

        /// <summary>Near-black with a cool navy cast.</summary>
        public static readonly Rgba Ink900 = default;

        /// <summary>Muted essential text, about 4.5:1 against Chalk.</summary>
        public static readonly Rgba Slate500 = default;

        /// <summary>Primary action: kit and floodlight blue.</summary>
        public static readonly Rgba Royal300 = default;

        /// <summary>Selection and football accent, not the pitch itself.</summary>
        public static readonly Rgba Grass300 = default;

        /// <summary>Supporting and sheet-opening action.</summary>
        public static readonly Rgba Violet200 = default;

        /// <summary>Information.</summary>
        public static readonly Rgba Sky300 = default;

        /// <summary>Warning and attention.</summary>
        public static readonly Rgba Gold300 = default;

        /// <summary>Success.</summary>
        public static readonly Rgba Green400 = default;

        /// <summary>Danger and destructive actions.</summary>
        public static readonly Rgba Red400 = default;

        /// <summary>Dark context: the page, a cool navy-black.</summary>
        public static readonly Rgba Night900 = default;

        /// <summary>Dark context: raised surfaces.</summary>
        public static readonly Rgba Night800 = default;

        /// <summary>Dark context: muted essential text. An Opus starting value, for review with the rest.</summary>
        public static readonly Rgba Slate300 = default;

        /// <summary>Every primitive with its name, for tests and the specimen.</summary>
        public static readonly (string Name, Rgba Colour)[] All = new (string, Rgba)[0];
    }

    /// <summary>
    /// The football domain's own colours: the pitch, its lines, the ball and the players. They are
    /// kept apart from generic UI roles. A component never takes its colour from here, and a theme
    /// never redefines these.
    /// </summary>
    public static class Domain
    {
        public static readonly Rgba Pitch = default;
        public static readonly Rgba PitchLine = default;
        public static readonly Rgba Ball = default;
        public static readonly Rgba BallShadow = default;

        /// <summary>The controlled player, 3.4:1 against the pitch (WCAG 1.4.11 asks 3:1 for graphics).</summary>
        public static readonly Rgba Player = default;

        public static readonly (string Name, Rgba Colour)[] All = new (string, Rgba)[0];
    }
}
