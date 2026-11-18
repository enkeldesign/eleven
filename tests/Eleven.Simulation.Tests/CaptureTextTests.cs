using System;
using System.IO;
using System.Linq;

using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #137: both captures Erik shared arrived as "ELEVEN capture", the share's title, with the file
    /// dropped. A capture as text pastes into any chat; it reads back whole or says why not.
    /// </summary>
    public class CaptureTextTests
    {
        private static byte[] Fixture => File.ReadAllBytes(Path.Combine(BallCurvesTests.RepositoryRoot(), "tests", "Eleven.Simulation.Tests", "Fixtures", "spike-felt-wrong.elfc"));

        private static byte[]? Read(string text) => CaptureText.Read(System.Text.Encoding.UTF8.GetBytes(text), out _);

        [Fact]
        public void It_starts_with_a_header_naming_the_capture_and_reads_back_byte_for_byte()
        {
            byte[] capture = Fixture;
            string text = CaptureText.Encode(capture, "eleven-felt-wrong-1-abc-t600");
            string first = text.Split('\n')[0];
            Assert.StartsWith(CaptureText.Header, first);
            Assert.Contains("eleven-felt-wrong-1-abc-t600", first);
            Assert.Contains($"bytes={capture.Length}", first);
            Assert.True(text.Split('\n').All(l => l.TrimEnd('\r').Length <= 76), "lines short enough for any chat");
            Assert.Equal(capture, Read(text));
        }

        [Fact]
        public void It_survives_what_a_chat_does_to_it()
        {
            byte[] capture = Fixture;
            string text = CaptureText.Encode(capture, "c");
            string chat = "Erik: here it is\r\n\r\n" + string.Join("\r\n", text.Split('\n').Select(l => "  " + l + "  ")) + "\r\nsent from my phone";
            Assert.Equal(capture, Read(chat));
            Assert.Equal(capture, Read(text.Replace("\n", " ")));
        }

        [Fact]
        public void A_binary_capture_reads_as_it_is()
        {
            Assert.Equal(Fixture, CaptureText.Read(Fixture, out _));
        }

        [Fact]
        public void A_cut_or_altered_capture_or_none_is_refused_with_the_reason()
        {
            string text = CaptureText.Encode(Fixture, "c");
            string[] lines = text.Split('\n');
            string cut = string.Join("\n", lines.Take(lines.Length / 2));
            Assert.Null(CaptureText.Read(System.Text.Encoding.UTF8.GetBytes(cut), out string why));
            Assert.Contains("bytes", why);

            int at = text.IndexOf('\n') + 10;
            char c = text[at] == 'A' ? 'B' : 'A';
            string altered = text.Substring(0, at) + c + text.Substring(at + 1);
            Assert.Null(CaptureText.Read(System.Text.Encoding.UTF8.GetBytes(altered), out why));
            Assert.Contains("checksum", why);

            Assert.Null(CaptureText.Read(System.Text.Encoding.UTF8.GetBytes("ELEVEN capture"), out why));
            Assert.NotEqual("", why);
        }

        [Fact]
        public void The_text_replays_as_the_file_does()
        {
            byte[]? bytes = Read(CaptureText.Encode(Fixture, "c"));
            Assert.NotNull(bytes);
            Assert.True(FeelCapture.Decode(bytes!).Replay().Matches);
        }
    }
}
