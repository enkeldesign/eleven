// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A felt-wrong capture as text (#137), for apps that drop a shared file: a header line with its
    /// name, length and checksum, then its bytes in base64, then END. It pastes into any chat or
    /// note, and reads back whatever the app did to its line breaks.
    /// </summary>
    public static class CaptureText
    {
        public const string Header = "ELEVEN CAPTURE";

        /// <summary>The capture's bytes as text, under <paramref name="name"/>.</summary>
        public static string Encode(byte[] capture, string name) => "";

        /// <summary>
        /// A capture's bytes from a file: the binary capture as it is, or the text form, found
        /// anywhere in it. Null, with the reason, when it is neither or isn't whole.
        /// </summary>
        public static byte[]? Read(byte[] data, out string problem)
        {
            problem = "not implemented";
            return null;
        }
    }
}
