using System;
using System.Linq;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// INV-2: the simulation and its scenario harness depend on no engine. This fails as soon as the simulation
    /// assembly references a Unity assembly, whoever adds it.
    /// </summary>
    public class EngineFreeTests
    {
        [Theory]
        [InlineData(typeof(SimulationInfo))]
        [InlineData(typeof(Harness.SpikeScenario))]
        public void Simulation_assemblies_reference_no_engine_assemblies(Type typeInAssembly)
        {
            var engineReferences = typeInAssembly.Assembly
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
