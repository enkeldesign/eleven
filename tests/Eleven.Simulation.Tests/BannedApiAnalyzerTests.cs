using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Eleven.Simulation.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// The banned-API analyzer against fixtures: one that must be caught, one that must pass.
    /// The real simulation building at all proves it passes the same check.
    /// </summary>
    public class BannedApiAnalyzerTests
    {
        private const string Banned = @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Eleven.Simulation
{
    internal sealed class Cheats
    {
        private readonly Random _random = new Random();                  // ELEVEN001 Random ×2
        public double Noise() => _random.NextDouble();                   // ELEVEN001 via field
        public long Now() => DateTime.Now.Ticks;                         // ELEVEN001 DateTime.Now
        public DateTime Utc() => DateTime.UtcNow;                        // ELEVEN001 DateTime.UtcNow
        public long Elapsed() => Stopwatch.StartNew().ElapsedTicks;      // ELEVEN001 Stopwatch
        public int Ticks() => Environment.TickCount;                     // ELEVEN001 TickCount
        public void Spawn() => new Thread(() => { }).Start();            // ELEVEN001 Thread
        public Task Later() => Task.Delay(1);                            // ELEVEN001 Task ×2
        public double Wave(double x) => Math.Sin(x);                     // ELEVEN002 outside backend
        public float Root(float x) => MathF.Sqrt(x);                     // ELEVEN002
        public double Power(double x) => Math.Pow(x, 2);                 // ELEVEN002
    }
}";

        private const string Allowed = @"
using System;

namespace Eleven.Simulation.Maths
{
    internal sealed class SystemMathBackend
    {
        public double Sin(double x) => Math.Sin(x);
        public double Atan2(double y, double x) => Math.Atan2(y, x);
        public double Sqrt(double x) => Math.Sqrt(x);
    }
}

namespace Eleven.Simulation
{
    internal static class Exact
    {
        public static double Tidy(double x) => Math.Abs(Math.Round(Math.Max(Math.Floor(x), Math.PI)));
        public static DateTime Fixed() => new DateTime(2026, 10, 25);
    }
}";

        [Fact]
        public void The_banned_fixture_is_caught_on_every_line()
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(Banned);

            string[] apis = diagnostics.Select(d => d.GetMessage()).ToArray();
            Assert.Contains(apis, m => m.Contains("System.Random"));
            Assert.Contains(apis, m => m.Contains("System.Random.NextDouble"));
            Assert.Contains(apis, m => m.Contains("System.DateTime.Now"));
            Assert.Contains(apis, m => m.Contains("System.DateTime.UtcNow"));
            Assert.Contains(apis, m => m.Contains("System.Diagnostics.Stopwatch"));
            Assert.Contains(apis, m => m.Contains("System.Environment.TickCount"));
            Assert.Contains(apis, m => m.Contains("System.Threading.Thread"));
            Assert.Contains(apis, m => m.Contains("System.Threading.Tasks.Task"));
            Assert.Contains(apis, m => m.Contains("System.Math.Sin"));
            Assert.Contains(apis, m => m.Contains("System.MathF.Sqrt"));
            Assert.Contains(apis, m => m.Contains("System.Math.Pow"));

            Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
            Assert.Equal(3, diagnostics.Count(d => d.Id == BannedApiAnalyzer.MathBoundaryId));

            // Every line marked in the fixture produces at least one diagnostic.
            int[] markedLines = Banned.Split('\n')
                .Select((line, index) => (line, index))
                .Where(x => x.line.Contains("// ELEVEN"))
                .Select(x => x.index)
                .ToArray();
            int[] reportedLines = diagnostics.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).Distinct().ToArray();
            Assert.Equal(markedLines, reportedLines.OrderBy(l => l).ToArray());
        }

        [Fact]
        public void The_allowed_fixture_passes_including_system_math_inside_its_backend()
        {
            Assert.Empty(Analyze(Allowed));
        }

        [Fact]
        public void The_backend_exception_does_not_extend_to_other_types_in_its_namespace()
        {
            string source = Allowed.Replace("class SystemMathBackend", "class SomeOtherBackend");

            Assert.Equal(3, Analyze(source).Count(d => d.Id == BannedApiAnalyzer.MathBoundaryId));
        }

        private static ImmutableArray<Diagnostic> Analyze(string source)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "Fixture",
                new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9)) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

            return compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BannedApiAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().Result;
        }
    }
}
