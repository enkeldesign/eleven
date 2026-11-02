using Eleven.Presentation;
using UnityEngine;

namespace Eleven.Host
{
    /// <summary>Converts the visual system's colours (#48) for Unity. The only place that does.</summary>
    internal static class UiColours
    {
        public static Color ToColor(this Rgba c) => new Color32(c.R, c.G, c.B, c.A);
    }
}
