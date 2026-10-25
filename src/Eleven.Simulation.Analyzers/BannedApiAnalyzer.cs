using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Eleven.Simulation.Analyzers
{
    /// <summary>
    /// The simulation's banned-API policy (AGENTS.md, Simulation code rules), as compile errors:
    ///
    /// ELEVEN001: nondeterministic APIs — <c>System.Random</c>, wall-clock time
    /// (<c>DateTime.Now/UtcNow/Today</c>, <c>DateTimeOffset.Now/UtcNow</c>, <c>Stopwatch</c>,
    /// <c>Environment.TickCount</c>/<c>TickCount64</c>) and threads and tasks (anything in
    /// <c>System.Threading</c>).
    ///
    /// ELEVEN002: any <c>System.Math</c>/<c>MathF</c> member other than the exact helpers below,
    /// outside the System.Math backend type. Transcendentals go through the maths boundary.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BannedApiAnalyzer : DiagnosticAnalyzer
    {
        public const string NondeterministicId = "ELEVEN001";
        public const string MathBoundaryId = "ELEVEN002";

        /// <summary>The one type allowed to call System.Math transcendentals.</summary>
        public const string SystemMathBackendType = "Eleven.Simulation.Maths.SystemMathBackend";

        private static readonly DiagnosticDescriptor Nondeterministic = new DiagnosticDescriptor(
            NondeterministicId,
            "Nondeterministic API in simulation code",
            "'{0}' is banned in simulation code: {1}",
            "Determinism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MathBoundary = new DiagnosticDescriptor(
            MathBoundaryId,
            "Maths call outside the maths boundary",
            "'{0}' is banned in simulation code: call SimMath instead",
            "Determinism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        // Exact operations whose IEEE-754 results don't depend on the platform.
        private static readonly HashSet<string> ExactMathMembers = new HashSet<string>
        {
            "Abs", "Min", "Max", "Floor", "Ceiling", "Round", "Truncate", "Sign", "Clamp", "CopySign", "PI", "E", "Tau",
        };

        private static readonly Dictionary<string, string> BannedTypes = new Dictionary<string, string>
        {
            ["System.Random"] = "use the counter-based CounterRng",
            ["System.Diagnostics.Stopwatch"] = "wall-clock time; measure in the harness",
        };

        private static readonly Dictionary<string, string> BannedMembers = new Dictionary<string, string>
        {
            ["System.DateTime.Now"] = "wall-clock time",
            ["System.DateTime.UtcNow"] = "wall-clock time",
            ["System.DateTime.Today"] = "wall-clock time",
            ["System.DateTimeOffset.Now"] = "wall-clock time",
            ["System.DateTimeOffset.UtcNow"] = "wall-clock time",
            ["System.Environment.TickCount"] = "wall-clock time",
            ["System.Environment.TickCount64"] = "wall-clock time",
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Nondeterministic, MathBoundary);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        }

        private static void AnalyzeName(SyntaxNodeAnalysisContext context)
        {
            var name = (SimpleNameSyntax)context.Node;
            SymbolInfo info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
            ISymbol? symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
            if (symbol == null) return;

            if (symbol is INamedTypeSymbol type)
            {
                string? reason = BannedTypeReason(type);
                if (reason != null) Report(context, Nondeterministic, name, type.ToDisplayString(), reason);
                return;
            }

            if (symbol is IMethodSymbol method && method.ReducedFrom != null) symbol = method.ReducedFrom;
            if (!(symbol is IMethodSymbol || symbol is IPropertySymbol || symbol is IFieldSymbol || symbol is IEventSymbol)) return;

            INamedTypeSymbol? containing = symbol.ContainingType;
            if (containing == null) return;
            string containingName = containing.ToDisplayString();
            string memberName = containingName + "." + symbol.Name;

            string? typeReason = BannedTypeReason(containing);
            if (typeReason != null)
            {
                // Already reported when the type name itself appears, as in Stopwatch.StartNew();
                // a member reached without naming the type (via var or a field) is reported here.
                if (!NamesTypeDirectly(context, name, containing)) Report(context, Nondeterministic, name, memberName, typeReason);
                return;
            }

            if (BannedMembers.TryGetValue(memberName, out string memberReason))
            {
                Report(context, Nondeterministic, name, memberName, memberReason);
                return;
            }

            if ((containingName == "System.Math" || containingName == "System.MathF")
                && !ExactMathMembers.Contains(symbol.Name)
                && !InsideSystemMathBackend(context))
            {
                Report(context, MathBoundary, name, memberName, "");
            }
        }

        private static string? BannedTypeReason(INamedTypeSymbol type)
        {
            INamedTypeSymbol original = type.OriginalDefinition;
            if (BannedTypes.TryGetValue(original.ToDisplayString(), out string reason)) return reason;

            string? ns = original.ContainingNamespace?.ToDisplayString();
            if (ns == "System.Threading" || (ns != null && ns.StartsWith("System.Threading.")))
            {
                return "threads and tasks make simulation order nondeterministic";
            }

            return null;
        }

        // True when the member is reached through the banned type's own name, as in
        // Stopwatch.StartNew(): the type name is reported already.
        private static bool NamesTypeDirectly(SyntaxNodeAnalysisContext context, SimpleNameSyntax name, INamedTypeSymbol type) =>
            name.Parent is MemberAccessExpressionSyntax access && access.Name == name
            && SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetSymbolInfo(access.Expression, context.CancellationToken).Symbol?.OriginalDefinition,
                type.OriginalDefinition);

        private static bool InsideSystemMathBackend(SyntaxNodeAnalysisContext context)
        {
            for (SyntaxNode? node = context.Node; node != null; node = node.Parent)
            {
                if (node is TypeDeclarationSyntax declaration)
                {
                    ISymbol? declared = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
                    return declared?.ToDisplayString() == SystemMathBackendType;
                }
            }

            return false;
        }

        private static void Report(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor,
            SimpleNameSyntax name, string api, string reason) =>
            context.ReportDiagnostic(Diagnostic.Create(descriptor, name.GetLocation(), api, reason));
    }
}
