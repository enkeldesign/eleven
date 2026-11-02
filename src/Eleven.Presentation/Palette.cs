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
        public static readonly Rgba Chalk50 = Rgba.Hex("#F4F5EE");

        /// <summary>Raised light surface.</summary>
        public static readonly Rgba Paper0 = Rgba.Hex("#FCFDF8");

        /// <summary>Near-black with a cool navy cast.</summary>
        public static readonly Rgba Ink900 = Rgba.Hex("#11151C");

        /// <summary>Muted essential text, about 4.5:1 against Chalk.</summary>
        public static readonly Rgba Slate500 = Rgba.Hex("#68707C");

        /// <summary>Primary action: kit and floodlight blue.</summary>
        public static readonly Rgba Royal300 = Rgba.Hex("#6C83FF");

        /// <summary>Selection and football accent, not the pitch itself.</summary>
        public static readonly Rgba Grass300 = Rgba.Hex("#B3DB6B");

        /// <summary>Supporting and sheet-opening action.</summary>
        public static readonly Rgba Violet200 = Rgba.Hex("#C6A8FF");

        /// <summary>Information.</summary>
        public static readonly Rgba Sky300 = Rgba.Hex("#77C7D8");

        /// <summary>Warning and attention.</summary>
        public static readonly Rgba Gold300 = Rgba.Hex("#F0C95A");

        /// <summary>Success.</summary>
        public static readonly Rgba Green400 = Rgba.Hex("#69C57A");

        /// <summary>Danger and destructive actions.</summary>
        public static readonly Rgba Red400 = Rgba.Hex("#DF5C6C");

        /// <summary>Dark context: the page, a cool navy-black.</summary>
        public static readonly Rgba Night900 = Rgba.Hex("#141922");

        /// <summary>Dark context: raised surfaces.</summary>
        public static readonly Rgba Night800 = Rgba.Hex("#1D2430");

        /// <summary>Dark context: muted essential text. An Opus starting value, for review with the rest.</summary>
        public static readonly Rgba Slate300 = Rgba.Hex("#9AA3B0");

        /// <summary>Every primitive with its name, for tests and the specimen.</summary>
        public static readonly (string Name, Rgba Colour)[] All =
        {
            ("chalk-50", Chalk50), ("paper-0", Paper0), ("ink-900", Ink900), ("slate-500", Slate500),
            ("royal-300", Royal300), ("grass-300", Grass300), ("violet-200", Violet200), ("sky-300", Sky300),
            ("gold-300", Gold300), ("green-400", Green400), ("red-400", Red400),
            ("night-900", Night900), ("night-800", Night800), ("slate-300", Slate300),
        };
    }

    /// <summary>
    /// The football domain's own colours: the pitch, its lines, the ball and the players. They are
    /// kept apart from generic UI roles. A component never takes its colour from here, and a theme
    /// never redefines these.
    /// </summary>
    public static class Domain
    {
        public static readonly Rgba Pitch = Rgba.Hex("#2E6B3A");
        public static readonly Rgba PitchLine = Rgba.Hex("#F2F2F2");
        public static readonly Rgba Ball = Rgba.Hex("#F7E26B");
        public static readonly Rgba BallShadow = Rgba.Hex("#00000059");

        /// <summary>The controlled player, 3.4:1 against the pitch (WCAG 1.4.11 asks 3:1 for graphics).</summary>
        public static readonly Rgba Player = Rgba.Hex("#99CC44");

        public static readonly (string Name, Rgba Colour)[] All =
        {
            ("Pitch", Pitch), ("PitchLine", PitchLine), ("Ball", Ball), ("BallShadow", BallShadow), ("Player", Player),
        };
    }
}
