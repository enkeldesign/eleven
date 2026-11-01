// Unity compiles this file with nullable annotations off; the .NET build has them on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace Eleven.Simulation.Harness
{
    /// <summary>One drill run by one passer profile.</summary>
    public sealed class PassCurve
    {
        public string Drill = "";
        public string Profile = "";
        public PlayerCapabilities Capabilities;
        public PassOutcome Outcome = new PassOutcome();
    }

    /// <summary>
    /// Headless quick-pass measurements (#38): the practice drills under contrasting passer
    /// profiles, under the real simulator. A technical reference of the current model and values,
    /// not approved feel (docs/simulation/pass.md).
    /// </summary>
    public static class PassCurves
    {
        /// <summary>The drills, by name.</summary>
        public static readonly ReadOnlyCollection<(string Name, IReadOnlyList<PracticeReceiver> Receivers)> Drills = Array.AsReadOnly(new[]
        {
            ("standard", PassDrill.Standard),
            ("crossing-run", PassDrill.CrossingRun),
        });

        /// <summary>
        /// The passer profiles: the one asked for (the reference, or the reference with capabilities
        /// changed), then three fixed contrasts from the reference: a literal passer with a narrow
        /// view who aims where receivers are, a wide reader who sees more and reads runs fully, and
        /// one with all-round view.
        /// </summary>
        public static List<(string Name, PlayerCapabilities Capabilities)> Profiles(PlayerCapabilities profile)
        {
            PlayerCapabilities reference = PlayerCapabilities.Reference;
            return new List<(string, PlayerCapabilities)>
            {
                ("profile", profile),
                ("literal-narrow", reference.With("passVision", 0.6).With("passAnticipation", 0)),
                ("reader-wide", reference.With("passVision", 2.1).With("passAnticipation", 1)),
                ("all-round", reference.With("passVision", Math.PI)),
            };
        }

        public static List<PassCurve> Characterise(SimConfig config, PlayerCapabilities profile)
        {
            var curves = new List<PassCurve>();
            foreach ((string drill, IReadOnlyList<PracticeReceiver> receivers) in Drills)
            foreach ((string name, PlayerCapabilities capabilities) in Profiles(profile))
            {
                curves.Add(new PassCurve
                {
                    Drill = drill,
                    Profile = name,
                    Capabilities = capabilities,
                    Outcome = PassDrill.Run(config, capabilities, receivers),
                });
            }

            return curves;
        }

        public const string CsvHeader =
            "config_hash,drill,profile,pass_vision_rad,pass_anticipation,kick_error_rad,target,line_deg,lead_m,launch_speed_mps," +
            "closest_m,closest_s,arrival_speed_mps";

        public static void WriteCsv(TextWriter writer, SimConfig config, IEnumerable<PassCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine(CsvHeader);
            foreach (PassCurve c in curves)
            {
                PassOutcome o = c.Outcome;
                writer.WriteLine(string.Join(",",
                    config.Hash.ToString("x16", CultureInfo.InvariantCulture), c.Drill, c.Profile,
                    F(c.Capabilities.PassVision), F(c.Capabilities.PassAnticipation), F(c.Capabilities.KickError),
                    o.Target ?? "free", F(o.Line * 180 / Math.PI), F(o.LeadMetres), F(o.LaunchSpeedMps),
                    o.Target == null ? "" : F(o.ClosestApproachMetres), o.Target == null ? "" : F(o.ClosestApproachSeconds),
                    o.Target == null ? "" : F(o.ArrivalSpeedMps)));
            }
        }

        /// <summary>Who made the curves (build, config, every tunable and the profile, written exactly), the drills and the key numbers.</summary>
        public static void WriteSummary(TextWriter writer, SimConfig config, PlayerCapabilities profile, IReadOnlyList<PassCurve> curves)
        {
            writer.NewLine = "\n";
            writer.WriteLine("# eleven pass curves v1");
            writer.WriteLine($"build: {SimulationInfo.BuildId}");
            writer.WriteLine($"config-hash: {config.Hash:x16}");
            writer.WriteLine($"config-format: {SimConfig.FormatVersion}");
            writer.WriteLine($"tick-rate: {config.TickRate}");
            writer.WriteLine("status: technical reference of the current values, not approved feel");
            var defaults = new SimConfig();
            foreach (TunableParameter p in SimConfig.Tunables)
            {
                string mark = config.Get(p.Name) != defaults.Get(p.Name) ? "  (override)" : "";
                writer.WriteLine($"{p.Name}: {R(config.Get(p.Name))} {p.Unit}{mark}");
            }

            foreach (TunableParameter p in PlayerCapabilities.Parameters)
            {
                writer.WriteLine($"{p.Name}: {R(profile.Get(p.Name))} {p.Unit}");
            }

            foreach ((string name, IReadOnlyList<PracticeReceiver> receivers) in Drills)
            {
                var parts = new List<string>();
                foreach (PracticeReceiver r in receivers)
                {
                    parts.Add($"{r.Name} at ({R(r.Position.X)}, {R(r.Position.Y)}) stick ({R(r.Stick.X)}, {R(r.Stick.Y)})");
                }

                writer.WriteLine($"drill {name}: passer at the origin facing +x, ball at (0.5, 0); {string.Join("; ", parts)}");
            }

            writer.WriteLine($"key: {KeyNumbers(curves)}");
        }

        public static string KeyNumbers(IReadOnlyList<PassCurve> curves)
        {
            var parts = new List<string>();
            foreach (PassCurve c in curves)
            {
                PassOutcome o = c.Outcome;
                string meet = o.Target == null ? "free pass" : $"{o.Target}, lead {F1(o.LeadMetres)} m, misses by {F1(o.ClosestApproachMetres)} m";
                parts.Add($"{c.Drill}/{c.Profile}: {meet}");
            }

            return string.Join("; ", parts);
        }

        private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // Adding +0 turns −0 into +0, so a sign-less zero never prints as "-0.000".
        private static string F(double value) => (value + 0.0).ToString("0.000", CultureInfo.InvariantCulture);

        private static string F1(double value) => (value + 0.0).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
