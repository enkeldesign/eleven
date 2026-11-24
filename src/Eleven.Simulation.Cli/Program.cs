using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Eleven.Simulation;
using Eleven.Simulation.Harness;

/// <summary>
/// eleven-sim hashlog &lt;dir&gt;  write the scenario's per-tick hash log
/// eleven-sim bench [runs]      measure mean simulation-step cost per tick
/// eleven-sim trace             print the ball and player every 10 ticks (for scripting the scenario)
/// eleven-sim curves &lt;dir&gt; [name=value ...]
///                              characterise the ball under the default config with any tunables
///                              overridden (#24), e.g. curves out rollingFriction=0.15 kickSpeed=28
/// eleven-sim movement &lt;dir&gt; [name=value ...]
///                              characterise player movement for a capability profile (the reference
///                              by default) and the starting-range grid (#36), e.g. movement out maxSpeed=9
/// eleven-sim kicks &lt;dir&gt; [name=value ...]
///                              characterise the manual kick and aftertouch (#37) under the default config
///                              and the reference profile, either overridden by name, e.g. kicks out kickPower=0.8 aftertouchBend=0
/// eleven-sim training [seasons] training's intensities over a headless career (#196): growth, condition, training injuries, drift
/// eleven-sim development [seasons] development and ageing over a headless career (#194): levels by age band, drift, retirements and youth
/// eleven-sim migration-fixture &lt;dir&gt; a recording under this state schema and its play, for a later schema's migration test (#166)
/// eleven-sim replay &lt;capture.elfc&gt; replay a "this felt wrong" capture (#44) and compare every tick; exit 1 on any mismatch
/// eleven-sim cpu &lt;dir&gt;         CPU against CPU in the 1v1 world (#83): how each tier's trials end, kicks, recoveries, idle stretches
/// eleven-sim challenge &lt;dir&gt;   the standing challenge (#84): approach angles, dribbler speeds and tackling profiles
/// eleven-sim restarts &lt;dir&gt;    CPU-against-CPU matches with the referee on (#85): restarts by kind, dead-ball time, stalls
/// eleven-sim matches &lt;dir&gt;     whole CPU-against-CPU matches (#86): scores, goals by half, length, stuck matches
/// eleven-sim sides             the same shots at the +x goal and at the −x goal (#82): goals, frame hits and saves by end
/// eleven-sim capture &lt;file&gt; [seconds]
///                              capture the last 10 s of the scripted spike session at `seconds` (default 20),
///                              through the host's FeelRecorder: the committed fixture
/// eleven-sim candidate [name=value ...]
///                              a feel test bench candidate (#45): the reference values with any knobs changed,
///                              its config hash and practice start hash, e.g. candidate kickSpeed=28 maxSpeed=8.5
/// eleven-sim turns &lt;dir&gt; [name=value ...]
///                              characterise the turn and the cut with the ball (#66, #75): 45°, 90°, 135° and 180°,
///                              snapped and swept, under FREE and ATTACHED, the turn and cut off and on, from every
///                              touch phase, e.g. turns out cutStrength=0
/// eleven-sim passes &lt;dir&gt; [name=value ...]
///                              measure the quick pass (#38) in the practice drills under the reference profile
///                              (or one with capabilities changed) and three contrasts, e.g. passes out passAnticipation=1
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string command = args.Length > 0 ? args[0] : "";
        switch (command)
        {
            case "hashlog" when args.Length == 2:
                return WriteHashLogs(args[1]);
            case "bench":
                return Bench(args.Length > 1 ? int.Parse(args[1]) : 30);
            case "trace":
                return Trace();
            case "curves" when args.Length >= 2:
                return Curves(args[1], args.Skip(2));
            case "movement" when args.Length >= 2:
                return Movement(args[1], args.Skip(2));
            case "kicks" when args.Length >= 2:
                return Kicks(args[1], args.Skip(2));
            case "dribble" when args.Length >= 2:
                return Dribble(args[1], args.Skip(2));
            case "passes" when args.Length >= 2:
                return Passes(args[1], args.Skip(2));
            case "goal" when args.Length >= 2:
                return Goal(args[1], args.Skip(2));
            case "keeper" when args.Length >= 2:
                return Keeper(args[1], args.Skip(2));
            case "turns" when args.Length >= 2:
                return Turns(args[1], args.Skip(2));
            case "push" when args.Length >= 2:
                return Push(args[1], args.Skip(2));
            case "replay" when args.Length == 2:
                return Replay(args[1]);
            case "candidate":
                return Candidate(args.Skip(1));
            case "preset" when args.Length >= 2:
                return Preset(args[1], args.Length > 2 ? args[2] : null);
            case "cpu" when args.Length >= 2:
            {
                // CPU against CPU in the 1v1 world (#83), every tier, a dozen seeds each.
                SimConfig cpuConfig = SpikeScenario.Config();
                List<CpuTrial> trials = CpuTrials.Run(cpuConfig);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "cpu-trials.csv")))
                {
                    CpuTrials.WriteCsv(csv, cpuConfig, trials);
                }

                Console.WriteLine($"config {cpuConfig.Hash:x16}, {CpuTrials.KeyNumbers(trials)} -> {args[1]}");
                return 0;
            }

            case "challenge" when args.Length >= 2:
            {
                // The standing challenge (#84): approach angles, dribbler speeds, tackling.
                SimConfig challengeConfig = SpikeScenario.Config();
                List<ChallengeCase> cases = ChallengeBench.Characterise(challengeConfig);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "challenge-bench.csv")))
                {
                    ChallengeBench.WriteCsv(csv, challengeConfig, cases);
                }

                Console.WriteLine($"config {challengeConfig.Hash:x16}, {ChallengeBench.KeyNumbers(cases)} -> {args[1]}");
                return 0;
            }

            case "restarts" when args.Length >= 2:
            {
                // The restart sequencer over CPU-against-CPU matches (#85).
                SimConfig restartConfig = SpikeScenario.Config();
                List<RestartTrial> matches = RestartTrials.Run(restartConfig);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "restart-trials.csv")))
                {
                    RestartTrials.WriteCsv(csv, restartConfig, matches);
                }

                Console.WriteLine($"config {restartConfig.Hash:x16}, {RestartTrials.KeyNumbers(matches)} -> {args[1]}");
                return 0;
            }

            case "matches" when args.Length >= 2:
            {
                // Whole CPU-against-CPU matches (#86).
                SimConfig matchConfig = SpikeScenario.Config();
                // A count after the directory plays that many: the milestone gate's 50 (#87).
                int count = args.Length >= 3 && int.TryParse(args[2], out int asked) && asked > 0 ? asked : MatchTrials.Seeds;
                List<MatchTrial> played = MatchTrials.Run(matchConfig, count);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "match-trials.csv")))
                {
                    MatchTrials.WriteCsv(csv, matchConfig, played);
                }

                File.WriteAllText(Path.Combine(args[1], "first-result.txt"), played[0].Result);
                Console.WriteLine($"config {matchConfig.Hash:x16}, {MatchTrials.KeyNumbers(played)} -> {args[1]}");
                return 0;
            }

            case "squads" when args.Length >= 2:
            {
                // N-a-side CPU-against-CPU matches (#92): eleven-sim squads <dir> [perSide] [count] [seat] [auto|ball|manual].
                // With `seat`, home's first outfielder is a human seat that switches (#95), in the mode given (#136).
                SimConfig squadConfig = SpikeScenario.Config();
                int perSide = args.Length >= 3 && int.TryParse(args[2], out int n) && n > 0 ? n : 2;
                int seeds = args.Length >= 4 && int.TryParse(args[3], out int k) && k > 0 ? k : SquadTrials.Seeds;
                bool seat = args.Length >= 5 && args[4] == "seat";
                string modeName = args.Length >= 6 ? args[5] : "auto";
                SwitchMode mode = modeName == "ball" ? SwitchMode.OnTheBall : modeName == "manual" ? SwitchMode.Manual : SwitchMode.Auto;
                List<SquadTrial> squads = SquadTrials.Run(squadConfig, perSide, seeds, seat, mode);
                Directory.CreateDirectory(args[1]);
                string suffix = seat ? (mode == SwitchMode.Auto ? "-seat" : $"-seat-{modeName}") : "";
                using (var csv = new StreamWriter(Path.Combine(args[1], $"squad-{perSide}v{perSide}{suffix}-trials.csv")))
                {
                    SquadTrials.WriteCsv(csv, squadConfig, squads);
                }

                Console.WriteLine($"config {squadConfig.Hash:x16}, {SquadTrials.KeyNumbers(squads, perSide)} -> {args[1]}");
                return 0;
            }

            case "league" when args.Length >= 2:
            {
                // M3's gate (#103): a double round robin of six sides at five a side. Fails if a
                // match is stuck or the strongest tier takes no more points than the weakest.
                SimConfig leagueConfig = SpikeScenario.Config();
                IReadOnlyList<LeagueSide> sides = League.Sides();
                List<LeagueMatch> played = League.Run(leagueConfig, sides);
                List<LeagueRow> table = League.Table(sides, played);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "league-matches.csv"))) League.WriteMatches(csv, leagueConfig, sides, played);
                using (var csv = new StreamWriter(Path.Combine(args[1], "league-table.csv"))) League.WriteTable(csv, table);
                Console.WriteLine($"config {leagueConfig.Hash:x16}, {League.KeyNumbers(played, table)} -> {args[1]}");
                var tiers = League.ByTier(table);
                if (played.Any(m => m.Stuck) || tiers[tiers.Count - 1].PointsPerMatch <= tiers[0].PointsPerMatch)
                {
                    Console.Error.WriteLine("The league failed its gate: a match stuck, or no spread by tier.");
                    return 1;
                }

                return 0;
            }

            case "gate11" when args.Length >= 2:
            {
                // M4's gate (#112): a round robin and a knockout cup at eleven a side, every law
                // of M4, each cup tie replayed. Fails if a match is stuck, a replay differs, or
                // the cup has no winner.
                SimConfig gateConfig = SpikeScenario.Config();
                IReadOnlyList<LeagueSide> gateSides = League.Sides();
                List<GateMatch> gate = Gate11.Run(gateConfig, gateSides);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "gate11-matches.csv"))) Gate11.WriteMatches(csv, gateConfig, gateSides, gate);
                Console.WriteLine($"config {gateConfig.Hash:x16}, {Gate11.KeyNumbers(gateSides, gate)} -> {args[1]}");
                if (!Gate11.Passes(gate))
                {
                    Console.Error.WriteLine("The 11v11 gate failed: a match stuck, a replay differed, or the cup had no winner.");
                    return 1;
                }

                return 0;
            }

            case "quicksim" when args.Length >= 3 && args[1] == "fit":
            {
                // #129: the quick-sim's reference refitted from a calibration sample, and its comparison;
                // #151: each parameter with its standard error.
                IReadOnlyList<LeagueSide> fitSides = QuickSimCalibration.Sides();
                List<CalibrationMatch> fitSample = QuickSimCalibration.Read(File.ReadAllLines(args[2]), fitSides);
                QuickSimFitResult fitted = QuickSimFit.FitWithErrors(fitSample, fitSides);
                QuickSimModel fit = fitted.Model;
                Console.WriteLine(FormattableString.Invariant(
                    $"fit over {fitSample.Count} matches: mu {fit.BaseRate:F6} ± {fitted.BaseRateError:F3}, home {fit.Home:F6} ± {fitted.HomeError:F3}, tiers 0 {fit.TierRating(1):F6} ± {fitted.TierErrors[1]:F3} {fit.TierRating(2):F6} ± {fitted.TierErrors[2]:F3} {fit.TierRating(3):F6} ± {fitted.TierErrors[3]:F3}, capabilities {fit.CapabilityWeight:F6} ± {fitted.CapabilityWeightError:F3}, rho {fit.Rho:F3} ± {fitted.RhoError:F3}, fouls {fit.FoulsPerSide:R}, cautions {fit.CautionsPerSide:R}, sendings-off {fit.SendingsOffPerSide:R} a side"));
                Console.WriteLine(QuickSimCalibration.Compare(SpikeScenario.Config(), fitSample, fitSides, 50, fit));
                return 0;
            }

            case "predictions":
            {
                // M5's gate (#130), INV-13: the CPU's flight predictions against the simulation's own ball,
                // through 5v5 and 11v11 CPU matches of the reference halves. Fails on any difference.
                SimConfig pc = SpikeScenario.Config();
                long checkedAll = 0, differed = 0;
                int predictions = 0;
                foreach ((int side, ulong seed) in new[] { (5, 0x130000001UL), (5, 0x130000002UL), (10, 0x130000003UL), (10, 0x130000004UL) })
                {
                    PredictionTrial pt = PredictionTrials.One(pc, side, seed, 2 * pc.HalfLength);
                    checkedAll += pt.Checked;
                    differed += pt.Differed;
                    predictions += pt.Predictions;
                    if (pt.Differed > 0) Console.Error.WriteLine($"{side} a side, seed {seed:x}: {pt.Differed} differed; the first: {pt.FirstDifference}");
                }

                Console.WriteLine(FormattableString.Invariant($"{predictions} predictions over 4 matches, {checkedAll} predicted positions checked against the simulation's ball, {differed} differed"));
                return differed == 0 ? 0 : 1;
            }

            case "throughput":
            {
                // #129: full 11v11 matches a second, headless, on one core; and quick-simmed ones.
                SimConfig tc = SpikeScenario.Config();
                IReadOnlyList<LeagueSide> ts = League.Sides();
                int full = args.Length >= 2 && int.TryParse(args[1], out int tf) && tf > 0 ? tf : 3;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                long steps = 0;
                for (int i = 0; i < full; i++)
                {
                    GateMatch g = Gate11.Play(tc, ts, i % ts.Count, (i + 1) % ts.Count, 0x129100000UL + (ulong)i, "throughput");
                    steps += (long)Math.Round(g.LengthSeconds * tc.TickRate);
                }

                double fullSeconds = watch.Elapsed.TotalSeconds;
                watch.Restart();
                const int Quick = 100_000;
                int goals = 0;
                for (int i = 0; i < Quick; i++) goals += QuickSim.Play(tc, QuickSides.Of(ts[i % ts.Count]), QuickSides.Of(ts[(i + 1) % ts.Count]), (ulong)i).HomeGoals;
                double quickSeconds = watch.Elapsed.TotalSeconds;
                Console.WriteLine(FormattableString.Invariant(
                    $"full simulation: {full} matches in {fullSeconds:F1} s, {full / fullSeconds:F3} matches/s, {1000 * fullSeconds / steps:F3} ms a step; quick-sim: {Quick / quickSeconds:F0} matches/s ({goals} home goals)"));
                return 0;
            }

            case "migration-fixture" when args.Length >= 2:
            {
                // #166: a recording under this build's state schema and its play, for a later
                // schema's migration to replay (tests/Eleven.Simulation.Tests/Fixtures).
                SimConfig fixtureConfig = SpikeScenario.Config();
                CommandRecording fixture = MigrationFixture.Record(fixtureConfig);
                Directory.CreateDirectory(args[1]);
                string name = $"migration-{SimulationInfo.StateSchemaVersion}";
                File.WriteAllBytes(Path.Combine(args[1], name + ".elrc"), fixture.Encode());
                string play = MigrationFixture.Play(fixture, fixtureConfig);
                File.WriteAllText(Path.Combine(args[1], name + ".txt"), play);
                Console.Write(play);
                return 0;
            }

            case "training":
            {
                // #196: training's intensities against each other over a headless career.
                int trainSeasons = args.Length >= 2 && int.TryParse(args[1], out int ts) && ts > 0 ? ts : 3;
                Console.Write(CareerTraining.Report(0x196, trainSeasons));
                return 0;
            }

            case "development":
            {
                // #194: development and ageing over a headless career, the human at the first club.
                int devSeasons = args.Length >= 2 && int.TryParse(args[1], out int ds) && ds > 0 ? ds : 3;
                Console.Write(CareerDevelopment.Report(0x194, devSeasons));
                return 0;
            }

            case "memory":
            {
                // #191: a career's managers' memories after seasons rolled forward headless, their
                // size in a save, and what they do to the human's rivals' plans for meeting him:
                // `memory [seasons]`.
                int memorySeasons = args.Length >= 2 && int.TryParse(args[1], out int msn) && msn > 0 ? msn : 3;
                SimConfig memoryConfig = SpikeScenario.Config().With("halfLength", 180);
                Eleven.Career.World memoryWorld = CareerSession.NewWorld(0x191);
                CareerSession career = CareerSession.Start(memoryWorld, 0x191, memoryWorld.Clubs[0].Id, CareerRole.ManagerOnly, memoryConfig);
                for (int i = 1; i <= memorySeasons; i++)
                {
                    while (!career.SeasonOver) career.Simulate();
                    if (i < memorySeasons) career.NextSeason();
                }

                int meetings = memoryWorld.Managers.Sum(m => m.Memory.Count);
                byte[] withMemory = Eleven.Career.WorldCodec.Encode(memoryWorld);
                var kept = memoryWorld.Managers.Select(m => m.Memory).ToList();
                foreach (Eleven.Career.Manager m in memoryWorld.Managers) m.Memory = new List<Eleven.Career.Meeting>();
                byte[] withoutMemory = Eleven.Career.WorldCodec.Encode(memoryWorld);
                int plainChars = Convert.ToBase64String(Eleven.Career.SaveCodec.Encode(career.ToSave(0))).Length;
                for (int i = 0; i < kept.Count; i++) memoryWorld.Managers[i].Memory = kept[i];
                int savedChars = Convert.ToBase64String(Eleven.Career.SaveCodec.Encode(career.ToSave(0))).Length;
                Console.WriteLine(FormattableString.Invariant(
                    $"memory: {meetings} meetings over {memoryWorld.Managers.Count} managers after {memorySeasons} seasons; {withMemory.Length - withoutMemory.Length} of the world's {withMemory.Length} bytes; a save of {savedChars} characters, {plainChars} without them"));

                // The human's division: each rival's head-to-head with his club, and what it does to the rival's plans.
                int human = career.Club, division = memoryWorld.Clubs.Single(c => c.Id == human).Division;
                foreach (Eleven.Career.Club rival in memoryWorld.Clubs.Where(c => c.Division == division && c.Id != human))
                {
                    Eleven.Career.Manager m = memoryWorld.Managers.Single(x => x.Id == rival.ManagerId);
                    List<Eleven.Career.Meeting> met = ManagerMemory.With(m, human);
                    (double line, double step, double earlier) = ManagerMemory.Shift(m, human);
                    Console.WriteLine(FormattableString.Invariant(
                        $"  {rival.ShortName}: {met.Count} meetings, {met.Sum(x => x.GoalsFor)}-{met.Sum(x => x.GoalsAgainst)}, late {met.Sum(x => x.LateFor)}-{met.Sum(x => x.LateAgainst)}: line {line:+0.0;-0.0;0.0} m, step-up {step:+0;-0;0}, protects {earlier:+0.00;-0.00;0.00} earlier"));
                }

                return 0;
            }

            case "seasons":
            {
                // #164: a new career's seasons rolled forward headless, every fixture quick-simmed:
                // `seasons [count] [career seed]`. Each season's tables, moves, cup winner and timing.
                int count = args.Length >= 2 && int.TryParse(args[1], out int sc) && sc > 0 ? sc : 3;
                ulong careerSeed = args.Length >= 3 && ulong.TryParse(args[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong cs) ? cs : 0x164;
                Eleven.Career.World seasonWorld = Eleven.Career.WorldGenerator.Generate(0x157);
                Eleven.Career.CompetitionRules seasonRules = Eleven.Career.CompetitionRules.Reference;
                var seasonWatch = Stopwatch.StartNew();
                // RollForward's own loop, with who was out of each fixture counted (#176).
                var seasons = new List<Eleven.Career.Season>(count);
                var outs = new List<(int Injuries, int Serious, int SentOff, int Injured, int Banned)>();
                var condition = new List<(double LeagueFit, double CupFit, int LeagueRests, int CupRests, int LeagueXis, int CupXis, int Lowest)>();
                Eleven.Career.Season played = Eleven.Career.SeasonEngine.Start(seasonWorld, 1, careerSeed, Eleven.Career.SeasonEngine.FirstRanking(seasonWorld), seasonRules);
                for (int i = 1; i <= count; i++)
                {
                    int injuries = 0, serious = 0, sentOff = 0, injured = 0, banned = 0;
                    // #177: each XI's condition at kick-off, and the starters rotation rested, league and midweek cup.
                    double leagueFit = 0, cupFit = 0;
                    int leagueRests = 0, cupRests = 0, leagueXis = 0, cupXis = 0, lowest = 100;
                    while (!Eleven.Career.SeasonEngine.Finished(played, seasonRules))
                    {
                        Eleven.Career.MatchdayKind kind = Eleven.Career.SeasonCalendar.Of(seasonRules)[played.Matchday].Kind;
                        List<Eleven.Career.Fixture> day = Eleven.Career.SeasonEngine.Next(played, seasonRules);
                        foreach (Eleven.Career.Fixture f in day)
                        {
                            foreach (int club in new[] { f.Home, f.Away })
                            {
                                HashSet<int> clubOut = Eleven.Career.Availability.OutOf(seasonWorld, club, kind);
                                var rotated = CareerSquads.Selection(seasonWorld, club, 10, clubOut, Eleven.Career.SquadRules.Reference);
                                var plain = CareerSquads.Selection(seasonWorld, club, 10, clubOut);
                                int rests = rotated.Starters.Count(x => !plain.Starters.Contains(x));
                                double fit = rotated.Starters.Average(x => x.Fitness);
                                lowest = Math.Min(lowest, rotated.Starters.Min(x => x.Fitness));
                                if (kind == Eleven.Career.MatchdayKind.Cup)
                                {
                                    cupFit += fit;
                                    cupRests += rests;
                                    cupXis++;
                                }
                                else
                                {
                                    leagueFit += fit;
                                    leagueRests += rests;
                                    leagueXis++;
                                }

                                foreach (Eleven.Career.Player p in seasonWorld.Players)
                                {
                                    if (p.ClubId != club || !Eleven.Career.Availability.Out(p, kind)) continue;
                                    if (p.InjuredDays > 0) injured++;
                                    else banned++;
                                }
                            }
                        }

                        CareerSeasons.PlayMatchday(CareerQuickSimCalibration.Config(), seasonWorld, played, seasonRules, 10);
                        foreach (Eleven.Career.Fixture f in day)
                        {
                            foreach (ResultPlayer line in played.Results[f.Id].Players)
                            {
                                if (line.Injury > 0) injuries++;
                                if (line.Injury == 2) serious++;
                                if (line.SentOff) sentOff++;
                            }
                        }
                    }

                    seasons.Add(played);
                    outs.Add((injuries, serious, sentOff, injured, banned));
                    condition.Add((leagueFit / Math.Max(1, leagueXis), cupFit / Math.Max(1, cupXis), leagueRests, cupRests, leagueXis, cupXis, lowest));
                    if (i < count) played = Eleven.Career.SeasonEngine.Rollover(seasonWorld, played, careerSeed, seasonRules);
                }

                double seasonSeconds = seasonWatch.Elapsed.TotalSeconds;
                string Short(int id) => seasonWorld.Clubs.Find(c => c.Id == id)!.ShortName;
                foreach (Eleven.Career.Season s in seasons)
                {
                    int goals = 0, level = 0, league = 0, extra = 0, shootouts = 0;
                    foreach (Eleven.Career.Fixture f in s.Fixtures)
                    {
                        MatchResult r = s.Results[f.Id];
                        if (f.Competition == Eleven.Career.CompetitionId.Cup)
                        {
                            extra += r.ExtraTime;
                            if (r.HomeShootoutKicks > 0) shootouts++;
                            continue;
                        }

                        league++;
                        goals += r.HomeGoals + r.AwayGoals;
                        if (r.HomeGoals == r.AwayGoals) level++;
                    }

                    (List<int> up, List<int> down) = Eleven.Career.SeasonEngine.Movement(s, seasonRules);
                    Console.WriteLine(FormattableString.Invariant(
                        $"season {s.Number} (seed {s.Seed:x16}): {league} league matches, {(double)goals / league:F2} goals a match, {100.0 * level / league:F0} % drawn; cup won by {Short(s.CupWinner)}, {extra} ties to extra time, {shootouts} to penalties; up {string.Join(" ", up.Select(Short))}, down {string.Join(" ", down.Select(Short))}"));
                    var o = outs[s.Number - 1];
                    int clubs = seasonWorld.Clubs.Count;
                    Console.WriteLine(FormattableString.Invariant(
                        $"  squads: {o.Injuries} injuries ({o.Serious} serious) and {o.SentOff} sendings-off; footballer-matches missed {o.Injured} injured and {o.Banned} banned, {(double)(o.Injured + o.Banned) / clubs:F1} a club"));
                    var k = condition[s.Number - 1];
                    Console.WriteLine(FormattableString.Invariant(
                        $"  condition: XIs at kick-off {k.LeagueFit:F1} in the league, {k.CupFit:F1} midweek in the cup, lowest starter {k.Lowest}; rested {k.LeagueRests} league and {k.CupRests} cup starters ({(double)(k.LeagueRests + k.CupRests) / clubs:F1} a club)"));
                    for (int d = 1; d <= s.Divisions.Count; d++)
                    {
                        var table = Eleven.Career.SeasonEngine.Table(s, (Eleven.Career.CompetitionId)d, seasonRules);
                        Console.WriteLine($"  division {d}: " + string.Join(", ", table.Select(t => FormattableString.Invariant($"{Short(t.Club)} {t.Points}"))));
                    }
                }

                Console.WriteLine(FormattableString.Invariant($"{count} seasons, {seasons.Sum(x => x.Results.Count)} results, in {seasonSeconds:F2} s"));
                return 0;
            }

            case "quicksim" when args.Length >= 3 && args[1] == "career-fit":
            {
                // #163: the eleven-a-side model refitted from its sample, and its comparison by division gap.
                Eleven.Career.World fitWorld = CareerQuickSimCalibration.World();
                List<CareerCalibrationMatch> careerSample = CareerQuickSimCalibration.Read(File.ReadAllLines(args[2]));
                CareerFitResult careerFit = CareerQuickSimCalibration.Fit(fitWorld, careerSample);
                QuickSimModel e = careerFit.Model;
                Console.WriteLine(FormattableString.Invariant(
                    $"fit over {careerSample.Count} matches: mu {e.BaseRate:F6} ± {careerFit.BaseRateError:F3}, home {e.Home:F6} ± {careerFit.HomeError:F3}, scale {careerFit.Scale:F6} ± {careerFit.ScaleError:F3} (tiers 0 {e.TierRating(1):F6} {e.TierRating(2):F6} {e.TierRating(3):F6}, capabilities {e.CapabilityWeight:F6}), rho {e.Rho:F3} ± {careerFit.RhoError:F3}, fouls {e.FoulsPerSide:R}, cautions {e.CautionsPerSide:R}, sendings-off {e.SendingsOffPerSide:R}, injuries {e.MinorInjuriesPerSide:R} minor and {e.SeriousInjuriesPerSide:R} serious a side"));
                Console.Write(CareerQuickSimCalibration.Compare(CareerQuickSimCalibration.Config(), fitWorld, careerSample, 100, e));
                return 0;
            }

            case "condition" when args.Length >= 3 && args[1] == "fit":
            {
                // #177: the quick-sim's fatigue weight fitted to the condition sample, and the comparison by level.
                Eleven.Career.World condWorld = CareerQuickSimCalibration.World();
                List<ConditionMatch> condSample = CareerConditionCalibration.Read(File.ReadAllLines(args[2]));
                (double weight, double error) = CareerConditionCalibration.Fit(condWorld, condSample, QuickSimModel.ElevenASide);
                (double unbounded, _) = CareerConditionCalibration.Fit(condWorld, condSample, QuickSimModel.ElevenASide, bounded: false);
                Console.WriteLine(FormattableString.Invariant($"fit over {condSample.Count} matches: fatigue weight {weight:F6} ± {error:F3} (unbounded {unbounded:F6})"));
                Console.Write(CareerConditionCalibration.Compare(CareerQuickSimCalibration.Config(), condWorld, condSample, 100, CareerConditionCalibration.With(QuickSimModel.ElevenASide, weight)));
                return 0;
            }

            case "condition" when args.Length >= 2:
            {
                // #177: a tired start's effect on the full simulation. `condition <dir> [count]`: every
                // third eleven-a-side calibration fixture, the home side's outfielders starting at each
                // fatigue level, the away side fresh; the first `count` matches only, for CI's prefix.
                SimConfig condConfig = CareerQuickSimCalibration.Config();
                Eleven.Career.World condWorld = CareerQuickSimCalibration.World();
                var matches = CareerConditionCalibration.Matches(condWorld);
                if (args.Length >= 3 && int.TryParse(args[2], out int cc) && cc > 0 && cc < matches.Count) matches = matches.Take(cc).ToList();
                var rows = new ConditionMatch[matches.Count];
                var condWatch = Stopwatch.StartNew();
                System.Threading.Tasks.Parallel.For(0, rows.Length, k => rows[k] = CareerConditionCalibration.Play(condConfig, condWorld, matches[k].Fixture, matches[k].Index, matches[k].Level));
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "condition.csv"))) CareerConditionCalibration.Write(csv, rows);
                Console.WriteLine(FormattableString.Invariant($"config {condConfig.Hash:x16}, {rows.Length} full 11v11 matches in {condWatch.Elapsed.TotalSeconds:F0} s on {Environment.ProcessorCount} cores -> {args[1]}"));
                return 0;
            }

            case "quicksim" when args.Length >= 3 && args[1] == "career":
            {
                // #163: the eleven-a-side sample, the full simulation between the reference world's clubs.
                // `quicksim career <dir> [count]` plays the first `count` fixtures: CI's prefix.
                SimConfig careerConfig = CareerQuickSimCalibration.Config();
                Eleven.Career.World careerWorld = CareerQuickSimCalibration.World();
                var careerFixtures = CareerQuickSimCalibration.Fixtures(careerWorld);
                if (args.Length >= 4 && int.TryParse(args[3], out int careerCount) && careerCount > 0 && careerCount < careerFixtures.Count) careerFixtures = careerFixtures.Take(careerCount).ToList();
                var careerWatch = System.Diagnostics.Stopwatch.StartNew();
                var careerPlayed = new CareerCalibrationMatch[careerFixtures.Count];
                System.Threading.Tasks.Parallel.For(0, careerFixtures.Count, i => careerPlayed[i] = CareerQuickSimCalibration.Play(careerConfig, careerWorld, careerFixtures[i], i));
                Directory.CreateDirectory(args[2]);
                using (var csv = new StreamWriter(Path.Combine(args[2], "quicksim-career.csv"))) CareerQuickSimCalibration.Write(csv, careerPlayed);
                Console.WriteLine(FormattableString.Invariant($"config {careerConfig.Hash:x16}, {careerPlayed.Length} full 11v11 matches in {careerWatch.Elapsed.TotalSeconds:F0} s on {Environment.ProcessorCount} cores -> {args[2]}"));
                return 0;
            }

            case "quicksim" when args.Length >= 2:
            {
                // The quick-sim's calibration sample (#129, #151): the full simulation over the calibration
                // league, every ordered pair once a round. `quicksim <dir> [rounds] [first]` plays the
                // first `first` fixtures only: CI's prefix, diffed against the reference's first rows.
                SimConfig quickConfig = SpikeScenario.Config();
                IReadOnlyList<LeagueSide> quickSides = QuickSimCalibration.Sides();
                int rounds = args.Length >= 3 && int.TryParse(args[2], out int qr) && qr > 0 ? qr : QuickSimCalibration.Rounds;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                // The matches are independent and each deterministic: played at once, written in order.
                var fixtures = QuickSimCalibration.Fixtures(quickSides.Count, rounds);
                if (args.Length >= 4 && int.TryParse(args[3], out int first) && first > 0 && first < fixtures.Count) fixtures = fixtures.Take(first).ToList();
                var played = new GateMatch[fixtures.Count];
                System.Threading.Tasks.Parallel.For(0, fixtures.Count, i => played[i] = QuickSimCalibration.Play(quickConfig, quickSides, fixtures[i]));
                List<GateMatch> sample = played.ToList();
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "quicksim-calibration.csv"))) Gate11.WriteMatches(csv, quickConfig, quickSides, sample);
                Console.WriteLine(FormattableString.Invariant($"config {quickConfig.Hash:x16}, {sample.Count} full matches in {watch.Elapsed.TotalSeconds:F0} s on {Environment.ProcessorCount} cores -> {args[1]}"));
                return 0;
            }

            case "formations" when args.Length >= 2:
            {
                // #125's contrast: home in each formation against the 4-4-2, CPU against CPU, over fixed seeds.
                SimConfig formationConfig = SpikeScenario.Config();
                int formationSeeds = args.Length >= 3 && int.TryParse(args[2], out int fseeds) && fseeds > 0 ? fseeds : FormationTrials.Seeds;
                List<FormationTrial> formations = FormationTrials.Run(formationConfig, formationSeeds);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "formation-trials.csv"))) FormationTrials.WriteCsv(csv, formationConfig, formations);
                Console.WriteLine($"config {formationConfig.Hash:x16}, {FormationTrials.KeyNumbers(formations)} -> {args[1]}");
                return formations.All(t => t.Finished) ? 0 : 1;
            }

            case "plans" when args.Length >= 2:
            {
                // #126: the plans' contrast and the switch's cost, CPU against CPU, over fixed seeds.
                SimConfig planConfig = SpikeScenario.Config();
                List<TacticTrial> plans = TacticTrials.Run(planConfig);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "tactic-trials.csv"))) TacticTrials.WriteCsv(csv, planConfig, plans);
                Console.WriteLine($"config {planConfig.Hash:x16}, {TacticTrials.KeyNumbers(plans)} -> {args[1]}");
                return plans.All(t => t.Finished) ? 0 : 1;
            }

            case "managers" when args.Length >= 2 && args[1] == "career":
            {
                // #182: the world's managers managing career matches: each side's plan switches and
                // substitutions a match, by its manager's temperament. `managers career [count]`.
                int mcount = args.Length >= 3 && int.TryParse(args[2], out int mc) && mc > 0 ? mc : 96;
                SimConfig mconfig = CareerQuickSimCalibration.Config();
                Eleven.Career.World mworld = CareerQuickSimCalibration.World();
                var mfixtures = CareerQuickSimCalibration.Fixtures(mworld).Take(mcount).ToList();
                var tallies = new (int Temperament, int Switches, int Chase, int Protect, int Subs)[mfixtures.Count * 2];
                System.Threading.Tasks.Parallel.For(0, mfixtures.Count, i =>
                {
                    (int home, int away, ulong seed) = mfixtures[i];
                    int hs = 0, asw = 0, hc = 0, ac = 0, hp = 0, ap = 0;
                    MatchResult r = CareerSquads.Play(mconfig, mworld, home, away, 10, seed, observe: e =>
                    {
                        if (e.Kind != SimEventKind.TacticSwitched) return;
                        bool h = e.Subject == Squads.HomeTeam;
                        if (h) hs++; else asw++;
                        if (e.Other == 1) { if (h) hc++; else ac++; }
                        if (e.Other == 2) { if (h) hp++; else ap++; }
                    });
                    int T(int club) => mworld.Managers.Single(m => m.Id == mworld.Clubs.Single(c => c.Id == club).ManagerId).Temperament;
                    tallies[2 * i] = (T(home), hs, hc, hp, r.HomeSubstitutions);
                    tallies[2 * i + 1] = (T(away), asw, ac, ap, r.AwaySubstitutions);
                });
                Console.WriteLine("| Temperament | Sides | Switches a match | To chase | To protect | Substitutions a match |");
                Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
                foreach ((string name, int lo, int hi) in new[] { ("cautious, 1–7", 1, 7), ("middle, 8–13", 8, 13), ("bold, 14–20", 14, 20) })
                {
                    var at = tallies.Where(t => t.Temperament >= lo && t.Temperament <= hi).ToList();
                    if (at.Count == 0) continue;
                    Console.WriteLine(FormattableString.Invariant($"| {name} | {at.Count} | {at.Average(t => t.Switches):F2} | {at.Sum(t => t.Chase)} | {at.Sum(t => t.Protect)} | {at.Average(t => t.Subs):F2} |"));
                }

                Console.WriteLine(FormattableString.Invariant($"| all | {tallies.Length} | {tallies.Average(t => t.Switches):F2} | {tallies.Sum(t => t.Chase)} | {tallies.Sum(t => t.Protect)} | {tallies.Average(t => t.Subs):F2} |"));
                return 0;
            }

            case "managers" when args.Length >= 2:
            {
                // #127: the CPU managers' decision distributions under each policy preset, and the scripted seat against them.
                SimConfig managerConfig = SpikeScenario.Config();
                List<ManagerTrial> managers = ManagerTrials.Run(managerConfig);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "manager-trials.csv"))) ManagerTrials.WriteCsv(csv, managerConfig, managers);
                Console.WriteLine($"config {managerConfig.Hash:x16}, {ManagerTrials.KeyNumbers(managers)} -> {args[1]}");
                return ManagerTrials.Passes(managers) ? 0 : 1;
            }

            case "fairness" when args.Length >= 2:
            {
                // Off-screen threat fairness (#102): how often the human's CPU opponents decide on a
                // footballer outside his view, by tier. Fails past the recorded bounds.
                SimConfig fairConfig = SpikeScenario.Config();
                int fairSeeds = args.Length >= 3 && int.TryParse(args[2], out int fs) && fs > 0 ? fs : FairnessTrials.Seeds;
                List<FairnessTrial> fair = FairnessTrials.Run(fairConfig, fairSeeds);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "fairness.csv"))) FairnessTrials.WriteCsv(csv, fairConfig, fair);
                Console.WriteLine($"config {fairConfig.Hash:x16}, {FairnessTrials.KeyNumbers(fair)} -> {args[1]}");
                if (!fair.All(FairnessTrials.Within))
                {
                    Console.Error.WriteLine("Off-screen dependence is past its bound.");
                    return 1;
                }

                return 0;
            }

            case "budget" when args.Length >= 2:
            {
                // The tick budget (#106): whole CPU 11v11 matches at 60 and at 120 Hz, the same
                // seeds and knobs, each step timed. Fails if a 60 Hz step averages over a quarter of
                // its 16.7 ms frame. Timing is the runner's: published, not diffed.
                int budgetSeeds = args.Length >= 3 && int.TryParse(args[2], out int bs) && bs > 0 ? bs : 3;
                SimConfig budgetConfig = SpikeScenario.Config();
                Directory.CreateDirectory(args[1]);
                var lines = new List<string> { "tick_rate,seed,finished,home,away,steps,mean_step_ms,p99_step_ms,max_step_ms" };
                var summary = new List<string>();
                double mean60 = 0;
                foreach (int rate in new[] { 60, 120 })
                {
                    SimConfig at = budgetConfig.AtTickRate(rate);
                    var all = new List<double>();
                    int goals = 0, finished = 0;
                    for (int i = 1; i <= budgetSeeds; i++)
                    {
                        ulong seed = 0x106000000UL + (ulong)i;
                        var sim = new Simulator(at, Squads.Match(at, seed, 10, bothCpu: true));
                        var recorder = new MatchRecorder();
                        var commands = new List<Command>();
                        var events = new List<SimEvent>();
                        var times = new List<double>();
                        var watch = new Stopwatch();
                        while (sim.Tick < 3 * 2 * at.HalfTicks && recorder.Result == null)
                        {
                            watch.Restart();
                            commands.Clear();
                            events.Clear();
                            KeeperControllers.Append(sim, commands);
                            CpuControllers.Append(sim, commands);
                            RestartControllers.Append(sim, commands);
                            ManagerControllers.Append(sim, commands);
                            sim.Step(commands, events);
                            watch.Stop();
                            times.Add(watch.Elapsed.TotalMilliseconds);
                            recorder.Observe(at, sim.PeekState(), events);
                        }

                        times.Sort();
                        all.AddRange(times);
                        MatchResult? r = recorder.Result;
                        if (r != null)
                        {
                            finished++;
                            goals += r.HomeGoals + r.AwayGoals;
                        }

                        lines.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:x},{2},{3},{4},{5},{6:F4},{7:F4},{8:F4}", rate, seed, r != null ? "yes" : "no",
                            r?.HomeGoals ?? 0, r?.AwayGoals ?? 0, times.Count, times.Average(), times[(int)(0.99 * (times.Count - 1))], times[times.Count - 1]));
                    }

                    all.Sort();
                    double mean = all.Average();
                    if (rate == 60) mean60 = mean;
                    summary.Add(string.Format(CultureInfo.InvariantCulture, "{0} Hz: {1} of {2} finished, {3} goals, step {4:F3} ms mean, {5:F3} ms p99, {6:F2} ms a second of play",
                        rate, finished, budgetSeeds, goals, mean, all[(int)(0.99 * (all.Count - 1))], mean * rate));
                }

                File.WriteAllLines(Path.Combine(args[1], "budget.csv"), lines);
                Console.WriteLine($"config {budgetConfig.Hash:x16}, 11v11 tick budget: {string.Join("; ", summary)} -> {args[1]}");
                const double Budget = 16.7 / 4;
                if (mean60 > Budget)
                {
                    Console.Error.WriteLine($"A 60 Hz step averages {mean60:F3} ms, over its budget of {Budget:F2} ms.");
                    return 1;
                }

                return 0;
            }

            case "air" when args.Length >= 2:
            {
                // Headers and volleys (#94): contact by height, incoming speed and heading, and in CPU matches.
                SimConfig airConfig = SpikeScenario.Config();
                List<AirCurve> air = AirCurves.Characterise(airConfig, PlayerCapabilities.Reference);
                List<AirTrial> inPlay = AirTrials.Run(airConfig);
                Directory.CreateDirectory(args[1]);
                using (var csv = new StreamWriter(Path.Combine(args[1], "air-curves.csv"))) AirCurves.WriteCsv(csv, airConfig, air);
                using (var csv = new StreamWriter(Path.Combine(args[1], "air-trials.csv"))) AirTrials.WriteCsv(csv, airConfig, inPlay);
                Console.WriteLine($"config {airConfig.Hash:x16}, {AirCurves.KeyNumbers(air)}; {AirTrials.KeyNumbers(inPlay)} -> {args[1]}");
                return 0;
            }

            case "sides":
                // The two ends compared (#82): the same shots at +x and at −x.
                foreach (SideCount count in SideParity.Measure(SpikeScenario.Config())) Console.WriteLine(count);
                return 0;
            case "capture" when args.Length >= 2:
                return Capture(args[1], args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 20);
            default:
                Console.Error.WriteLine("usage: eleven-sim hashlog <dir> | bench [runs] | trace | curves <dir> [name=value ...] | movement <dir> [name=value ...] | kicks <dir> [name=value ...] | passes <dir> [name=value ...] | goal <dir> [name=value ...] | keeper <dir> [name=value ...] | dribble <dir> [name=value ...] | turns <dir> [name=value ...] | push <dir> [name=value ...] | replay <capture> | capture <file> [seconds] | candidate [name=value ...] | preset <file> [dir] | sides | cpu <dir> | challenge <dir> | restarts <dir> | matches <dir> [count] | squads <dir> [perSide] [count] [seat] | air <dir> | league <dir> | fairness <dir> [seeds] | budget <dir> [seeds] | gate11 <dir> | formations <dir> [seeds] | plans <dir> | managers <dir>");
                return 2;
        }
    }

    /// <summary>
    /// The candidate the test bench would apply for these changes, and the hashes that identify its
    /// run: the same values give the same hashes in every runtime.
    /// </summary>
    // A saved preset (#46): what it is, how it differs from the technical reference, and, given a
    // directory, every characterisation curve under its values. The curves describe the preset;
    // they are no baseline until Erik promotes it.
    private static int Preset(string path, string? directory)
    {
        if (!BenchPreset.TryRead(File.ReadAllText(path), out BenchPreset? preset, out string problem))
        {
            Console.Error.WriteLine($"{path}: {problem}");
            return 1;
        }

        BenchCandidate candidate = preset!.Candidate;
        Console.WriteLine($"preset: {preset.Name}, saved by build {(preset.Build.Length > 0 ? preset.Build : "unknown")}, controls {TouchCandidates.NameOf(preset.Controls)}");
        Console.WriteLine($"config: {preset.ConfigHash:x16}, practice start {candidate.PracticeStartHash():x16}, {candidate.ChangesFrom(BenchCandidate.Reference)} change(s) from the technical reference");
        var changed = new List<string>();
        foreach (BenchKnob k in BenchCandidate.Knobs)
        {
            double value = candidate.Get(k.Name);
            if (BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(BenchCandidate.Reference.Get(k.Name))) continue;
            Console.WriteLine($"  {k.Name}={value.ToString("R", CultureInfo.InvariantCulture)}");
            if (!k.OnPlayer && !k.OnKeeper && !k.OnAdapter && !k.OnSplit && !k.OnFire) changed.Add(k.Name);
        }

        if (directory == null) return 0;

        // Every curve tool, under the preset's own config and footballer.
        SimConfig config = candidate.Config;
        PlayerCapabilities profile = candidate.Capabilities;
        int result = Curves(directory, config, changed);
        if (result == 0) result = Movement(directory, config, profile);
        if (result == 0) result = Kicks(directory, config, profile);
        if (result == 0) result = Passes(directory, config, profile);
        if (result == 0) result = Dribble(directory, config, profile);
        if (result == 0) result = Turns(directory, config, profile);
        if (result == 0) result = Push(directory, config, profile);
        if (result == 0) result = Goal(directory, config);
        if (result == 0) result = Keeper(directory, config, candidate.Keeper);
        if (result == 0) Console.WriteLine($"curves for \"{preset.Name}\" written to {directory}");
        return result;
    }

    private static int Candidate(IEnumerable<string> assignments)
    {
        var changes = new List<KeyValuePair<string, double>>();
        foreach (string a in assignments)
        {
            int equals = a.IndexOf('=');
            if (equals <= 0 || !double.TryParse(a.Substring(equals + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                Console.Error.WriteLine($"not name=value: {a}");
                return 2;
            }

            changes.Add(new KeyValuePair<string, double>(a.Substring(0, equals), value));
        }

        if (!BenchCandidate.Reference.TryWith(changes, out BenchCandidate? candidate, out string problem))
        {
            Console.Error.WriteLine(problem);
            return 1;
        }

        Console.WriteLine($"config {candidate!.Config.Hash:x16}  practice start {candidate.PracticeStartHash():x16}  preset {candidate.PresetName}  {candidate.ChangesFrom(BenchCandidate.Reference)} change(s) from the technical reference");
        string group = "";
        foreach (BenchKnob k in BenchCandidate.Knobs)
        {
            if (k.Group != group) Console.WriteLine($"{(group = k.Group)}:");
            TunableParameter p = k.Parameter;
            string start = p.StartMin.HasValue ? $"research start {p.StartMin:G6}–{p.StartMax:G6}" : "technical reference";
            string changed = candidate.Get(k.Name).Equals(BenchCandidate.Reference.Get(k.Name)) ? "" : $"  (was {BenchCandidate.Reference.Get(k.Name):G6})";
            Console.WriteLine($"  {k.Label,-28} {candidate.Get(k.Name),10:G6} {p.Unit,-22} valid {p.Min:G6}–{p.Max:G6}, {start}, step {k.Step:G6}{changed}");
        }

        return 0;
    }

    private static int WriteHashLogs(string directory)
    {
        Directory.CreateDirectory(directory);
        ScenarioRun run = ScenarioRunner.Run(SpikeScenario.Config());
        string path = Path.Combine(directory, $"hashlog-{SpikeScenario.Id}-v{SpikeScenario.Version}.txt");
        using (var writer = new StreamWriter(path))
        {
            HashLog.Write(writer, run);
        }

        Console.WriteLine($"{run.TickHashes.Count} tick hashes, digest {run.SequenceDigest():x16} -> {path}");
        return 0;
    }

    /// <summary>
    /// Times only <see cref="Simulator.Step"/>: commands are generated before the timed loop and
    /// state hashing is timed separately. Warm-up runs come first, so tiered JIT compilation has
    /// settled before anything is timed.
    /// </summary>
    private static int Bench(int runs)
    {
        const int warmUpRuns = 20;
        Console.WriteLine($"runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"cpu count: {Environment.ProcessorCount}");
#if DEBUG
        Console.WriteLine("WARNING: Debug build; measure in Release.");
#endif
        Console.WriteLine($"method: {SpikeScenario.Name} ({SpikeScenario.DurationSeconds} s), {warmUpRuns} warm-up runs, " +
            $"then {runs} timed runs; Step only, state hashing timed separately");

        var bench = new ScenarioBench();
        for (int i = 0; i < warmUpRuns; i++) bench.Run(record: false);
        for (int i = 0; i < runs; i++) bench.Run(record: true);

        Console.WriteLine(bench.Summary());
        return 0;
    }

    private sealed class ScenarioBench
    {
        private readonly SimConfig _config;
        private readonly List<Command>[] _commands;
        private readonly List<SimEvent> _events = new List<SimEvent>(64);
        private readonly List<double> _stepNs = new List<double>();
        private readonly List<double> _hashNs = new List<double>();

        public ScenarioBench()
        {
            _config = SpikeScenario.Config();
            _commands = new List<Command>[SpikeScenario.DurationTicks(_config)];
            for (int t = 0; t < _commands.Length; t++)
            {
                _commands[t] = new List<Command>();
                SpikeScenario.CommandsFor(t, _commands[t]);
            }
        }

        public void Run(bool record)
        {
            int ticks = _commands.Length;
            var simulator = new Simulator(_config, SpikeScenario.InitialState(_config));
            _events.Clear();

            long start = Stopwatch.GetTimestamp();
            for (int t = 0; t < ticks; t++)
            {
                simulator.Step(_commands[t], _events);
            }

            long stepped = Stopwatch.GetTimestamp();
            ulong sink = 0;
            for (int t = 0; t < ticks; t++)
            {
                sink ^= simulator.StateHash();
            }

            long hashed = Stopwatch.GetTimestamp();
            GC.KeepAlive(sink);

            if (!record) return;
            _stepNs.Add((stepped - start) * 1e9 / Stopwatch.Frequency / ticks);
            _hashNs.Add((hashed - stepped) * 1e9 / Stopwatch.Frequency / ticks);
        }

        public string Summary()
        {
            var sorted = new List<double>(_stepNs);
            sorted.Sort();
            return $"step mean {Mean(_stepNs):F0} ns/tick (min {sorted[0]:F0}, median {sorted[sorted.Count / 2]:F0}, " +
                $"max {sorted[sorted.Count - 1]:F0}); state hash mean {Mean(_hashNs):F0} ns/tick";
        }
    }

    private static List<KeyValuePair<string, double>>? ParseSettings(IEnumerable<string> settings)
    {
        var overrides = new List<KeyValuePair<string, double>>();
        foreach (string setting in settings)
        {
            int eq = setting.IndexOf('=');
            if (eq <= 0 || !double.TryParse(setting.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                Console.Error.WriteLine($"not a name=value setting: {setting}");
                return null;
            }

            overrides.Add(new KeyValuePair<string, double>(setting.Substring(0, eq), value));
        }

        return overrides;
    }

    private static int Curves(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        return Curves(directory, new SimConfig().With(overrides), overrides.Select(o => o.Key).ToList());
    }

    private static int Curves(string directory, SimConfig config, List<string> changed)
    {
        List<BallCurve> curves = BallCurves.Characterise(config);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "ball-curves.csv")))
        {
            BallCurves.WriteCsv(csv, config, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "ball-curves.txt")))
        {
            BallCurves.WriteSummary(summary, config, changed, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}: {BallCurves.KeyNumbers(config, curves)} -> {directory}");
        return 0;
    }

    private static int Dribble(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        return Dribble(directory, SpikeScenario.Config().With(overrides), PlayerCapabilities.Reference);
    }

    private static int Dribble(string directory, SimConfig config, PlayerCapabilities profile)
    {
        List<DribbleCurve> curves = DribbleCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "dribble-curves.csv")))
        {
            DribbleCurves.WriteCsv(csv, config, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {DribbleCurves.KeyNumbers(curves)} -> {directory}");
        return 0;
    }

    private static int Turns(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        return Turns(directory, SpikeScenario.Config().With(overrides), PlayerCapabilities.Reference);
    }

    private static int Turns(string directory, SimConfig config, PlayerCapabilities profile)
    {
        List<TurnCurve> curves = TurnCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "turn-curves.csv")))
        {
            TurnCurves.WriteCsv(csv, config, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {TurnCurves.KeyNumbers(curves)} -> {directory}");
        return 0;
    }

    // Push-and-chase beside FREE's short touches (#63).
    private static int Push(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        return Push(directory, SpikeScenario.Config().With(overrides), PlayerCapabilities.Reference);
    }

    private static int Push(string directory, SimConfig config, PlayerCapabilities profile)
    {
        List<PushCurve> curves = PushCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "push-curves.csv")))
        {
            PushCurves.WriteCsv(csv, config, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {PushCurves.KeyNumbers(curves)} -> {directory}");
        return 0;
    }

    private static int Goal(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        return Goal(directory, new SimConfig().With(overrides));
    }

    private static int Goal(string directory, SimConfig config)
    {
        List<GoalEdge> edges = GoalEdges.Characterise(config);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "goal-edges.csv")))
        {
            GoalEdges.WriteCsv(csv, config, edges);
        }

        Console.WriteLine($"config {config.Hash:x16}, {GoalEdges.KeyNumbers(config, edges)} -> {directory}");
        return 0;
    }

    private static int Keeper(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        return Keeper(directory, new SimConfig().With(overrides), null);
    }

    private static int Keeper(string directory, SimConfig config, KeeperCapabilities? bench)
    {
        List<KeeperShot> shots = KeeperShots.Characterise(config, bench);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "keeper-shots.csv")))
        {
            KeeperShots.WriteCsv(csv, config, shots);
        }

        Console.WriteLine($"config {config.Hash:x16}, {KeeperShots.KeyNumbers(shots)} -> {directory}");
        return 0;
    }

    private static int Movement(string directory, IEnumerable<string> settings)
    {
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return 2;

        PlayerCapabilities profile = PlayerCapabilities.Reference;
        foreach (KeyValuePair<string, double> o in overrides) profile = profile.With(o.Key, o.Value);

        return Movement(directory, new SimConfig(), profile);
    }

    private static int Movement(string directory, SimConfig config, PlayerCapabilities profile)
    {
        List<MovementCurve> curves = MovementCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "movement-curves.csv")))
        {
            MovementCurves.WriteCsv(csv, config, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "movement-curves.txt")))
        {
            MovementCurves.WriteSummary(summary, config, profile, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {MovementCurves.KeyNumbers(curves)} -> {directory}");
        return 0;
    }

    // A name is a player capability or a config tunable; each fails on what it doesn't know.
    private static bool SplitSettings(IEnumerable<string> settings, out SimConfig config, out PlayerCapabilities profile)
    {
        config = new SimConfig();
        profile = PlayerCapabilities.Reference;
        List<KeyValuePair<string, double>>? overrides = ParseSettings(settings);
        if (overrides == null) return false;

        var configOverrides = new List<KeyValuePair<string, double>>();
        foreach (KeyValuePair<string, double> o in overrides)
        {
            if (PlayerCapabilities.Parameters.Any(p => p.Name == o.Key)) profile = profile.With(o.Key, o.Value);
            else configOverrides.Add(o);
        }

        config = config.With(configOverrides);
        return true;
    }

    private static int Passes(string directory, IEnumerable<string> settings)
    {
        if (!SplitSettings(settings, out SimConfig config, out PlayerCapabilities profile)) return 2;
        return Passes(directory, config, profile);
    }

    private static int Passes(string directory, SimConfig config, PlayerCapabilities profile)
    {

        List<PassCurve> curves = PassCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "pass-curves.csv")))
        {
            PassCurves.WriteCsv(csv, config, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "pass-curves.txt")))
        {
            PassCurves.WriteSummary(summary, config, profile, curves);
        }

        // The free pass with no receiver (#65), beside the drills.
        List<FreePassCurve> free = FreePassCurves.Characterise(config, profile);
        using (var csv = new StreamWriter(Path.Combine(directory, "free-pass-curves.csv")))
        {
            FreePassCurves.WriteCsv(csv, config, free);
        }

        Console.WriteLine($"config {config.Hash:x16}, {PassCurves.KeyNumbers(curves)}; free passes: {FreePassCurves.KeyNumbers(free)} -> {directory}");
        return 0;
    }

    private static int Kicks(string directory, IEnumerable<string> settings)
    {
        if (!SplitSettings(settings, out SimConfig config, out PlayerCapabilities profile)) return 2;
        return Kicks(directory, config, profile);
    }

    private static int Kicks(string directory, SimConfig config, PlayerCapabilities profile)
    {
        List<KickCurve> curves = KickCurves.Characterise(config, profile);
        Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(directory, "kick-curves.csv")))
        {
            KickCurves.WriteCsv(csv, curves);
        }

        using (var summary = new StreamWriter(Path.Combine(directory, "kick-curves.txt")))
        {
            KickCurves.WriteSummary(summary, config, profile, curves);
        }

        Console.WriteLine($"config {config.Hash:x16}, {KickCurves.KeyNumbers(config, curves)} -> {directory}");
        return 0;
    }

    private static int Replay(string path)
    {
        FeelCapture capture;
        try
        {
            // The file as saved, or the capture as text (#137), pasted from wherever it was sent.
            byte[]? bytes = CaptureText.Read(File.ReadAllBytes(path), out string problem);
            if (bytes == null)
            {
                Console.Error.WriteLine($"{path}: {problem}");
                return 1;
            }

            capture = FeelCapture.Decode(bytes);
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine($"{path}: {e.Message}");
            return 1;
        }

        Console.WriteLine($"capture: host {capture.HostBuild}, simulation {capture.SimulationBuild}, preset {capture.ActivePreset}, control {capture.ControlCandidate}");
        Console.WriteLine($"window: ticks {capture.StartTick}..{capture.StartTick + capture.StepCount}, seed {capture.Seed:x16}, config {capture.ConfigHash:x16}");
        if (capture.Note.Length > 0) Console.WriteLine($"note: {capture.Note}");

        FeelReplay replay = capture.Replay();
        if (!replay.Matches)
        {
            Console.Error.WriteLine($"{path}: {replay.Problem}");
            return 1;
        }

        Console.WriteLine($"replayed exactly: {replay.TicksCompared} tick hashes and {capture.Events.Count} events match");
        return 0;
    }

    private static int Capture(string path, double seconds)
    {
        SimConfig config = SpikeScenario.Config();
        var sim = new Simulator(config, SpikeScenario.InitialState(config));
        var recorder = new FeelRecorder();
        recorder.Start(sim);
        var commands = new List<Command>();
        var events = new List<SimEvent>();
        long end = (long)(seconds * config.TickRate);
        while (sim.Tick < end)
        {
            commands.Clear();
            SpikeScenario.CommandsFor(sim.Tick, commands);
            int first = events.Count;
            sim.Step(commands, events);
            recorder.Record(sim, commands, events, first);
        }

        FeelCapture capture = recorder.Capture(sim, $"eleven-sim {SpikeScenario.Name}", "technical-reference", "none",
            $"scripted fixture: the last {FeelRecorder.WindowSeconds:0} s of {SpikeScenario.Name} at {seconds:0.##} s");
        File.WriteAllBytes(path, capture.Encode());
        Console.WriteLine($"ticks {capture.StartTick}..{capture.StartTick + capture.StepCount}, {capture.Events.Count} events -> {path}");
        return 0;
    }

    private static int Trace()
    {
        ScenarioRunner.Run(SpikeScenario.Config(), state =>
        {
            if (state.Tick % 10 != 0) return;
            BallState b = state.Ball;
            PlayerState d = state.Players[0];
            Console.WriteLine(
                $"{state.Tick,5} ball ({b.Position.X,7:F2} {b.Position.Y,6:F2} {b.Position.Z,5:F2}) " +
                $"v ({b.Velocity.X,6:F2} {b.Velocity.Y,6:F2} {b.Velocity.Z,6:F2}) spin {b.Spin.Z,5:F2} " +
                $"player ({d.Position.X,7:F2} {d.Position.Y,6:F2}) h {d.Heading,5:F2} cd {d.KickCooldown} wind-up {d.WindUpTicks} window {d.AftertouchTicks}");
        }).Events.ForEach(e => Console.WriteLine($"event {e}"));
        return 0;
    }

    private static double Mean(List<double> values)
    {
        double sum = 0;
        foreach (double v in values) sum += v;
        return sum / values.Count;
    }

    private static void ForEach<T>(this IReadOnlyList<T> list, Action<T> action)
    {
        foreach (T item in list) action(item);
    }
}
