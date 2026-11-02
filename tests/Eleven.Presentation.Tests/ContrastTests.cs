using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Eleven.Presentation.Tests
{
    /// <summary>The contrast maths, against known WCAG values.</summary>
    public class ContrastMathsTests
    {
        [Fact]
        public void Hex_round_trips_with_and_without_alpha()
        {
            Assert.Equal(new Rgba(0x11, 0x15, 0x1C), Rgba.Hex("#11151C"));
            Assert.Equal(new Rgba(0x11, 0x15, 0x1C, 0x80), Rgba.Hex("#11151c80"));
            Assert.Equal("#F4F5EE", Rgba.Hex("#F4F5EE").ToHex());
            Assert.Equal("#11151C80", new Rgba(0x11, 0x15, 0x1C, 0x80).ToHex());
            Assert.Throws<FormatException>(() => Rgba.Hex("11151C"));
            Assert.Throws<FormatException>(() => Rgba.Hex("#11151"));
        }

        [Fact]
        public void Contrast_matches_the_wcag_reference_values()
        {
            var white = new Rgba(255, 255, 255);
            var black = new Rgba(0, 0, 0);
            Assert.Equal(21, Rgba.Contrast(white, black), 6);
            Assert.Equal(21, Rgba.Contrast(black, white), 6);
            Assert.Equal(1, Rgba.Contrast(white, white), 6);
            Assert.Equal(4.48, Rgba.Contrast(Rgba.Hex("#777777"), white), 2);  // the classic just-fails-AA grey
            Assert.Equal(4.54, Rgba.Contrast(Rgba.Hex("#767676"), white), 2);  // and the classic just-passes one
        }

        [Fact]
        public void Translucent_colours_are_composited_before_contrast()
        {
            var half = new Rgba(0, 0, 0, 128);
            Rgba grey = half.Over(new Rgba(255, 255, 255));
            Assert.Equal(255, grey.A);
            Assert.InRange((int)grey.R, 126, 128);
            Assert.Equal(new Rgba(0, 0, 0, 51), new Rgba(0, 0, 0).WithAlpha(0.2));
        }
    }

    /// <summary>
    /// The palette and its mappings (#48): the starting values, every intended pairing at WCAG AA or
    /// 1.4.11, and generic UI kept apart from football-domain colour.
    /// </summary>
    public class PaletteTests
    {
        public static IEnumerable<object[]> Themes() => Theme.All.Select(t => new object[] { t.Name });

        public static IEnumerable<object[]> PageContexts() => new[] { Theme.Ordinary, Theme.Dark }.Select(t => new object[] { t.Name });

        private static Theme ThemeNamed(string name) => Theme.All.Single(t => t.Name == name);

        [Fact]
        public void The_primitives_are_the_starting_palette()
        {
            var expected = new Dictionary<string, string>
            {
                ["chalk-50"] = "#F4F5EE", ["paper-0"] = "#FCFDF8", ["ink-900"] = "#11151C", ["slate-500"] = "#68707C",
                ["royal-300"] = "#6C83FF", ["grass-300"] = "#B3DB6B", ["violet-200"] = "#C6A8FF", ["sky-300"] = "#77C7D8",
                ["gold-300"] = "#F0C95A", ["green-400"] = "#69C57A", ["red-400"] = "#DF5C6C",
                ["night-900"] = "#141922", ["night-800"] = "#1D2430", ["slate-300"] = "#9AA3B0",
                // Erik, 2026-11-02: buttons are darker and desaturated, with white text.
                ["royal-700"] = "#4E60A8", ["violet-700"] = "#6E5596", ["sky-700"] = "#2F6B7C",
                ["grass-700"] = "#4D6B2E", ["slate-700"] = "#3A4250", ["red-700"] = "#A23B4B",
            };
            Assert.Equal(expected.Keys.OrderBy(k => k), Primitives.All.Select(p => p.Name).OrderBy(k => k));
            foreach ((string name, Rgba colour) in Primitives.All) Assert.Equal(expected[name], colour.ToHex());
        }

        [Theory]
        [MemberData(nameof(Themes))]
        public void Every_role_is_mapped_and_every_mapped_colour_is_a_primitive(string themeName)
        {
            Theme theme = ThemeNamed(themeName);
            var primitives = Primitives.All.Select(p => p.Colour).ToList();
            Assert.Equal(24, Theme.Roles.Length);
            foreach (Role role in Theme.Roles)
            {
                Rgba colour = theme[role];
                Assert.True(colour.A > 0, $"{themeName}: {role} is unmapped");
                // Translucent roles are a primitive at an alpha.
                Assert.Contains(primitives, p => p.R == colour.R && p.G == colour.G && p.B == colour.B);
            }
        }

        [Theory]
        [MemberData(nameof(Themes))]
        public void Essential_text_meets_AA_on_every_surface(string themeName)
        {
            Theme theme = ThemeNamed(themeName);
            Assert.NotEmpty(Theme.Surfaces);
            foreach (Role surface in Theme.Surfaces)
            {
                foreach (Role text in Theme.TextRoles)
                {
                    double ratio = Rgba.Contrast(theme[text], theme[surface]);
                    Assert.True(ratio >= 4.5, $"{themeName}: {text} on {surface} is {ratio:F2}:1");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Themes))]
        public void Button_text_is_white_on_every_action_fill_at_5_to_1_or_better(string themeName)
        {
            // Erik, 2026-11-02: buttons never mix white and black text; dark fills at about 5:1.
            Theme theme = ThemeNamed(themeName);
            Assert.Equal(Primitives.Paper0, theme[Role.OnAction]);
            Assert.NotEmpty(Theme.ActionFills);
            foreach (Role fill in Theme.ActionFills)
            {
                double ratio = Rgba.Contrast(theme[Role.OnAction], theme[fill]);
                Assert.True(ratio >= 5, $"{themeName}: button text on {fill} is {ratio:F2}:1");
            }
        }

        [Theory]
        [MemberData(nameof(Themes))]
        public void State_colours_that_are_not_buttons_carry_ink_text_at_AA(string themeName)
        {
            Theme theme = ThemeNamed(themeName);
            Assert.Equal(Primitives.Ink900, theme[Role.OnState]);
            Assert.NotEmpty(Theme.StateFills);
            foreach (Role fill in Theme.StateFills)
            {
                double ratio = Rgba.Contrast(theme[Role.OnState], theme[fill]);
                Assert.True(ratio >= 4.5, $"{themeName}: state text on {fill} is {ratio:F2}:1");
            }
        }

        [Theory]
        [MemberData(nameof(PageContexts))]
        public void Outlines_and_the_focus_ring_meet_3_to_1_against_every_surface(string themeName)
        {
            // WCAG 1.4.11. The focus ring sits past a gap of the surface, so the surface is what it is
            // seen against; TURN's focus colour failed exactly this.
            Theme theme = ThemeNamed(themeName);
            Assert.NotEmpty(Theme.Surfaces);
            foreach (Role surface in Theme.Surfaces)
            {
                foreach (Role boundary in new[] { Role.Outline, Role.FocusRing })
                {
                    double ratio = Rgba.Contrast(theme[boundary], theme[surface]);
                    Assert.True(ratio >= 3, $"{themeName}: {boundary} on {surface} is {ratio:F2}:1");
                }
            }
        }

        [Fact]
        public void Controls_over_the_pitch_get_chalk_borders()
        {
            // Erik, 2026-11-02: borders carry button contrast; over the pitch, chalk borders. Dark
            // fills are about 1:1 against the pitch, so the border is the whole boundary.
            Theme theme = Theme.Pitch;
            Assert.Equal(Primitives.Chalk50, theme[Role.Outline]);
            double outline = Rgba.Contrast(theme[Role.Outline], Domain.Pitch);
            Assert.True(outline >= 3, $"chalk border {outline:F2}:1 against the pitch");
            Assert.Contains(Theme.Pitch, Theme.All);
        }

        [Theory]
        [MemberData(nameof(Themes))]
        public void The_two_colour_focus_indicator_reads_on_every_background_and_against_itself(string themeName)
        {
            // WCAG technique C40: the ring and its gap differ by 3:1, and one of them reaches 3:1
            // against whatever the control sits on, the pitch included.
            Theme theme = ThemeNamed(themeName);
            double pair = Rgba.Contrast(theme[Role.FocusRing], theme[Role.FocusGap]);
            Assert.True(pair >= 3, $"{themeName}: focus ring on its gap is {pair:F2}:1");
            IEnumerable<(string, Rgba)> backgrounds = Theme.Surfaces.Select(s => (s.ToString(), theme[s])).Append(("pitch", Domain.Pitch));
            foreach ((string name, Rgba background) in backgrounds)
            {
                double ring = Rgba.Contrast(theme[Role.FocusRing], background);
                double gap = Rgba.Contrast(theme[Role.FocusGap], background);
                Assert.True(Math.Max(ring, gap) >= 3, $"{themeName}: focus indicator on {name}: ring {ring:F2}:1, gap {gap:F2}:1");
            }
        }

        [Fact]
        public void Generic_roles_and_domain_colours_are_separate()
        {
            var generic = Theme.All.SelectMany(t => Theme.Roles.Select(r => t[r])).Select(c => (c.R, c.G, c.B)).ToHashSet();
            Assert.NotEmpty(Domain.All);
            foreach ((string name, Rgba colour) in Domain.All)
            {
                Assert.DoesNotContain((colour.R, colour.G, colour.B), generic);
            }

            Assert.DoesNotContain(Theme.Roles, r => r.ToString().IndexOf("football", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Equal(new[] { "Pitch", "PitchLine", "Ball", "BallShadow", "Player" }, Domain.All.Select(d => d.Name));
            Assert.Equal("#2E6B3A", Domain.Pitch.ToHex());
        }

        [Fact]
        public void The_dark_context_remaps_surfaces_and_text_but_keeps_the_buttons()
        {
            Assert.NotEqual(Theme.Ordinary[Role.SurfacePage], Theme.Dark[Role.SurfacePage]);
            Assert.NotEqual(Theme.Ordinary[Role.Text], Theme.Dark[Role.Text]);
            Assert.Equal(Primitives.Paper0, Theme.Dark[Role.OnAction]);
            Assert.Equal(Theme.Ordinary[Role.ActionPrimary], Theme.Dark[Role.ActionPrimary]);
        }

        [Fact]
        public void No_racing_semantics_in_the_generic_system()
        {
            string[] racing = { "gas", "drift", "boost", "lock", "shift", "overcharge", "track", "tyre", "checker", "garage" };
            IEnumerable<string> names = Theme.Roles.Select(r => r.ToString()).Concat(Primitives.All.Select(p => p.Name));
            Assert.NotEmpty(Primitives.All);
            foreach (string name in names)
            {
                Assert.DoesNotContain(racing, word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
    }

    /// <summary>The canonical scales (#48), each represented once.</summary>
    public class ScaleTests
    {
        [Fact]
        public void Spacing_is_the_family_rhythm()
        {
            Assert.Equal(new[] { 3, 6, 9, 12, 18, 24, 36 }, Spacing.Steps);
            Assert.Equal(new[] { Spacing.Xxs, Spacing.Xs, Spacing.S, Spacing.M, Spacing.L, Spacing.Xl, Spacing.Xxl }, Spacing.Steps);
        }

        [Fact]
        public void Shapes_are_the_family_recipes()
        {
            Assert.Equal((12, 3, 3), (Shapes.Control.Radius, Shapes.Control.Border, Shapes.Control.Shadow));
            Assert.Equal((12, 3, 4), (Shapes.PrimaryAction.Radius, Shapes.PrimaryAction.Border, Shapes.PrimaryAction.Shadow));
            Assert.Equal((18, 4, 6), (Shapes.Card.Radius, Shapes.Card.Border, Shapes.Card.Shadow));
            Assert.Equal((24, 4, 8), (Shapes.Sheet.Radius, Shapes.Sheet.Border, Shapes.Sheet.Shadow));
        }

        [Fact]
        public void Type_runs_from_micro_to_display_with_heavy_headings_and_tabular_data()
        {
            TypeStyle[] ladder = { TypeScale.Micro, TypeScale.Small, TypeScale.Body, TypeScale.Title, TypeScale.Heading, TypeScale.Display };
            Assert.Equal(new[] { 11, 13, 15 }, ladder.Take(3).Select(t => t.Size));
            Assert.InRange(TypeScale.Title.Size, 17, 21);
            Assert.InRange(TypeScale.Heading.Size, 22, 30);
            Assert.InRange(TypeScale.Display.Size, 28, 44);
            Assert.True(ladder.Zip(ladder.Skip(1), (a, b) => a.Size < b.Size).All(x => x), "sizes ascend");
            Assert.Equal(Weight.Heavy, TypeScale.Display.Weight);
            Assert.Equal(Weight.Heavy, TypeScale.Heading.Weight);
            Assert.Equal(Weight.Regular, TypeScale.Body.Weight);
            Assert.True(TypeScale.Display.Tracking < 0, "display is tightly tracked");
            Assert.True(TypeScale.Label.Uppercase && TypeScale.Label.Weight != Weight.Regular, "labels are heavy and uppercase");
            Assert.True(TypeScale.Data.Tabular, "data uses tabular figures");
        }

        [Fact]
        public void Motion_has_two_durations_and_reduced_motion_removes_movement()
        {
            Assert.Equal(120, MotionScale.FastMilliseconds);
            Assert.Equal(180, MotionScale.BaseMilliseconds);
            Assert.Equal(0, MotionScale.Duration(MotionScale.BaseMilliseconds, reducedMotion: true));
            Assert.Equal(180, MotionScale.Duration(MotionScale.BaseMilliseconds, reducedMotion: false));
            Assert.NotEqual(default, MotionScale.Easing);
        }

        [Fact]
        public void Layers_are_named_and_strictly_ordered()
        {
            var layers = (Layer[])Enum.GetValues(typeof(Layer));
            Assert.Equal(new[] { Layer.Match, Layer.Hud, Layer.Controls, Layer.Overlay, Layer.Sheet, Layer.Dialog, Layer.Status }, layers);
            Assert.Equal(layers.Length, layers.Select(l => (int)l).Distinct().Count());
            Assert.True(layers.Zip(layers.Skip(1), (a, b) => (int)a < (int)b).All(x => x), "ascending");
        }

        [Fact]
        public void Targets_are_at_least_44_points_and_focus_has_a_gap_and_a_ring()
        {
            Assert.Equal(44, Interaction.MinTarget);
            Assert.InRange(Interaction.FocusGap, 2, 4);
            Assert.InRange(Interaction.FocusRing, 2, 4);
            Assert.InRange(Interaction.InnerEdge, 1, Shapes.Control.Border);
        }
    }
}
