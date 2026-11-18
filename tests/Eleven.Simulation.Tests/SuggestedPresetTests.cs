using System.Linq;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// #135: a built-in preset loosening the won't-reach rule for the human, so Erik can compare it
    /// with the technical reference on his device. Like the reference, it can be loaded but not
    /// deleted or overwritten, and a capture records its name.
    /// </summary>
    public class SuggestedPresetTests
    {
        private static BenchPreset Loose => Assert.Single(BenchPreset.Suggested, p => p.Name == "LOOSE TACKLE");

        [Fact]
        public void Loose_tackle_is_the_technical_reference_with_a_human_leeway()
        {
            BenchPreset loose = Loose;
            Assert.Equal(1, loose.Candidate.ChangesFrom(BenchCandidate.Reference));
            Assert.True(loose.Candidate.Config.HumanChallengeLeeway > 0);
            Assert.Equal(BenchPreset.TechnicalReference.Model, loose.Model);
            Assert.Equal(BenchPreset.TechnicalReference.Controls, loose.Controls);
        }

        [Fact]
        public void A_suggested_name_cant_be_saved_over()
        {
            Assert.NotEqual("", BenchPreset.NameProblem("loose tackle"));
        }

        [Fact]
        public void Its_values_are_recognised_and_the_reference_isnt_taken_for_it()
        {
            BenchPreset loose = Loose;
            Assert.Same(loose, BenchPreset.SuggestedMatching(loose.Candidate, loose.Controls));
            Assert.Null(BenchPreset.SuggestedMatching(BenchCandidate.Reference, ControlCandidate.ActionSurface));
            Assert.Null(BenchPreset.SuggestedMatching(loose.Candidate, ControlCandidate.Split));
        }
    }
}
