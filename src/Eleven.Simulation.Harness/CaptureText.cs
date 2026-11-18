// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace Eleven.Simulation.Harness
{
    /// <summary>
    /// A felt-wrong capture as text (#137), for apps that drop a shared file: a header line with its
    /// name, length and checksum, then its bytes in base64, then END. It pastes into any chat or
    /// note, and reads back whatever the app did to its line breaks and indentation.
    /// </summary>
    public static class CaptureText
    {
        public const string Header = "ELEVEN CAPTURE";
        private const string Version = "v1";
        private const string End = "END";
        private const int LineLength = 76;

        /// <summary>The capture's bytes as text, under <paramref name="name"/>.</summary>
        public static string Encode(byte[] capture, string name)
        {
            var text = new StringBuilder();
            text.Append(FormattableString.Invariant($"{Header} {Version} name={(name ?? "").Replace(' ', '_')} bytes={capture.Length} fnv={Fnv(capture):x8}\n"));
            string body = Convert.ToBase64String(capture);
            for (int i = 0; i < body.Length; i += LineLength) text.Append(body, i, Math.Min(LineLength, body.Length - i)).Append('\n');
            text.Append(End).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// A capture's bytes from a file: the binary capture as it is, or the text form, found
        /// anywhere in it. Null, with the reason, when it is neither or isn't whole.
        /// </summary>
        public static byte[]? Read(byte[] data, out string problem)
        {
            problem = "";
            if (data.Length >= 4 && data[0] == 'E' && data[1] == 'L' && data[2] == 'F' && data[3] == 'C') return data;

            string text = System.Text.Encoding.UTF8.GetString(data);
            int at = text.IndexOf(Header + " ", StringComparison.Ordinal);
            if (at < 0)
            {
                problem = $"No \"{Header}\" header: this isn't a capture, as a file or as text.";
                return null;
            }

            string[] tokens = text.Substring(at + Header.Length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens[0] != Version)
            {
                problem = $"The capture's text is in format {(tokens.Length > 0 ? tokens[0] : "?")}; this build reads {Version}.";
                return null;
            }

            int length = -1;
            uint fnv = 0;
            bool hasFnv = false;
            int k = 1;
            // Header fields by name: a body joined onto one line ends in base64's `=` padding (Codex on #146).
            for (; k < tokens.Length && IsField(tokens[k]); k++)
            {
                string[] pair = tokens[k].Split(new[] { '=' }, 2);
                if (pair[0] == "bytes") int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out length);
                else if (pair[0] == "fnv") hasFnv = uint.TryParse(pair[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out fnv);
            }

            if (length < 0 || !hasFnv)
            {
                problem = "The capture's header has no length or checksum.";
                return null;
            }

            var body = new StringBuilder();
            for (; k < tokens.Length && tokens[k] != End; k++) body.Append(tokens[k]);

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(body.ToString());
            }
            catch (FormatException)
            {
                problem = $"The capture's text holds about {body.Length * 3 / 4} of its {length} bytes: it was cut or changed.";
                return null;
            }

            if (bytes.Length != length)
            {
                problem = $"The capture's text holds {bytes.Length} of its {length} bytes: it was cut.";
                return null;
            }

            if (Fnv(bytes) != fnv)
            {
                problem = "The capture's checksum differs: its text was changed.";
                return null;
            }

            return bytes;
        }

        private static bool IsField(string token) =>
            token.StartsWith("name=", StringComparison.Ordinal) || token.StartsWith("bytes=", StringComparison.Ordinal) || token.StartsWith("fnv=", StringComparison.Ordinal);

        // FNV-1a, 32 bits: enough to tell a changed character, not a security check.
        private static uint Fnv(byte[] data)
        {
            uint hash = 2166136261;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 16777619;
            }

            return hash;
        }
    }
}
