using System.Collections.Generic;
using Eleven.Career;
using Eleven.Simulation.Harness;
using Xunit;

namespace Eleven.Simulation.Tests
{
    /// <summary>
    /// Codex on #183: a save the device refuses never loses the career in play. Its own slot offers
    /// it back as it is, at its matchday and said not to be saved, whatever the slot's save says;
    /// every other slot offers its save, or a new career.
    /// </summary>
    public class CareerSlotTests
    {
        private static readonly SimConfig Config = SpikeScenario.Config().With("halfLength", 60);

        // The page's storage, which can refuse every write as a full device does.
        private sealed class Store : ISaveStore
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

            public bool Refuse;

            public string? Read(string key) => _values.TryGetValue(key, out string? v) ? v : null;

            public bool Write(string key, string value)
            {
                if (Refuse) return false;
                _values[key] = value;
                return true;
            }

            public void Delete(string key) => _values.Remove(key);
        }

        [Fact]
        public void With_no_career_in_play_a_slot_offers_its_save_or_a_new_career()
        {
            var store = new Store();
            var slots = new SaveSlots(store);
            World w = CareerSession.NewWorld(0x183);
            CareerSession s = CareerSession.Start(w, 0x183, w.Clubs[3].Id, CareerRole.ManagerOnly, Config);
            s.Simulate();
            Assert.True(slots.Save(2, s.ToSave(1)));

            Assert.Equal(SlotChoice.New, CareerHub.SlotOffer(0, slots.Summary(0), null, -1));
            Assert.Equal("1. Empty", CareerHub.SlotLine(0, slots.Summary(0), null, -1));
            Assert.Equal(SlotChoice.Continue, CareerHub.SlotOffer(2, slots.Summary(2), null, -1));
            Assert.Equal($"3. {w.Clubs[3].Name} ({w.Clubs[3].ShortName}) · Season 1 · Matchday 2", CareerHub.SlotLine(2, slots.Summary(2), null, -1));
        }

        [Fact]
        public void The_career_in_play_resumes_from_its_own_slot_as_it_is_when_its_save_was_refused()
        {
            var store = new Store();
            var slots = new SaveSlots(store);
            World w = CareerSession.NewWorld(0x183);
            CareerSession s = CareerSession.Start(w, 0x183, w.Clubs[5].Id, CareerRole.ManagerOnly, Config);
            Assert.True(slots.Save(1, s.ToSave(1)));
            string club = $"{w.Clubs[5].Name} ({w.Clubs[5].ShortName})";

            // Saved: its slot resumes it, at the matchday its save says too.
            Assert.Equal(SlotChoice.Resume, CareerHub.SlotOffer(1, slots.Summary(1), s, 1));
            Assert.Equal($"2. {club} · Season 1 · Matchday 1 · in play", CareerHub.SlotLine(1, slots.Summary(1), s, 1));

            // The device refuses the next saves: the slot still holds matchday 1, the career is at 3.
            s.Simulate();
            store.Refuse = true;
            Assert.False(slots.Save(1, s.ToSave(2)));
            s.Simulate();
            Assert.False(slots.Save(1, s.ToSave(3)));
            Assert.Equal(1, slots.Summary(1).Matchday);
            Assert.Equal(SlotChoice.Resume, CareerHub.SlotOffer(1, slots.Summary(1), s, 1));
            Assert.Equal($"2. {club} · Season 1 · Matchday 3 · in play, not saved", CareerHub.SlotLine(1, slots.Summary(1), s, 1));

            // Every other slot is as its save says, the career in play or not.
            Assert.Equal(SlotChoice.New, CareerHub.SlotOffer(0, slots.Summary(0), s, 1));
            Assert.Equal("1. Empty", CareerHub.SlotLine(0, slots.Summary(0), s, 1));

            // Once the device takes a save, the line says nothing more.
            store.Refuse = false;
            Assert.True(slots.Save(1, s.ToSave(4)));
            Assert.Equal($"2. {club} · Season 1 · Matchday 3 · in play", CareerHub.SlotLine(1, slots.Summary(1), s, 1));
        }

        [Fact]
        public void A_career_whose_first_save_was_refused_is_offered_back_not_a_new_career()
        {
            var store = new Store { Refuse = true };
            var slots = new SaveSlots(store);
            World w = CareerSession.NewWorld(0x183);
            CareerSession s = CareerSession.Start(w, 0x183, w.Clubs[0].Id, CareerRole.PlayerManager, Config);
            Assert.False(slots.Save(4, s.ToSave(1)));
            Assert.True(slots.Summary(4).Empty);

            Assert.Equal(SlotChoice.Resume, CareerHub.SlotOffer(4, slots.Summary(4), s, 4));
            Assert.Equal($"5. {w.Clubs[0].Name} ({w.Clubs[0].ShortName}) · Season 1 · Matchday 1 · in play, not saved", CareerHub.SlotLine(4, slots.Summary(4), s, 4));
        }
    }
}
