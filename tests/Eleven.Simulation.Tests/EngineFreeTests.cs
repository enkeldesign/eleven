using System;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// INV-2: the simulation depends on no engine. This fails as soon as the simulation
    /// assembly references a Unity assembly, whoever adds it.
    /// </summary>
    public class EngineFreeTests
    {
        [Fact]
        public void Simulation_assembly_references_no_engine_assemblies()
        {
            var engineReferences = typeof(SimulationInfo).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .Where(name => name.StartsWith("UnityEngine", StringComparison.Ordinal)
                    || name.StartsWith("UnityEditor", StringComparison.Ordinal)
                    || name.StartsWith("Unity.", StringComparison.Ordinal))
                .ToList();

            Assert.Empty(engineReferences);
        }
    }
}
