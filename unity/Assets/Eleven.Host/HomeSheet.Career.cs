using System;
using System.Collections.Generic;
using Eleven.Presentation;
using Eleven.Simulation.Harness;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    using Club = Eleven.Career.Club;
    using World = Eleven.Career.World;

    /// <summary>
    /// Home's career pages (#168): New career, then the hub over #167's session. What the hub
    /// says comes from <see cref="CareerHub"/>; the host plays the matches.
    /// </summary>
    internal sealed partial class HomeSheet
    {
        /// <summary>The career being played, if any.</summary>
        public Func<CareerSession> Career = () => null;

        /// <summary>Start a career: its world, seed, club and role.</summary>
        public Action<World, ulong, int, CareerRole> StartCareer = (world, seed, club, role) => { };

        /// <summary>Play (or watch) the human's fixture: Home closes and the match starts.</summary>
        public System.Action CareerPlay = () => { };

        /// <summary>Quick-sim the matchday.</summary>
        public System.Action CareerSimulate = () => { };

        /// <summary>Roll into the next season.</summary>
        public System.Action CareerNextSeason = () => { };

        private static readonly string[] Roles = { "Player-manager", "Manager only" };

        private UiKit.Surface _newCareerPage;
        private UiKit.Surface _hubPage;
        private Picker _careerPicker;
        private UiKit.Segmented _roles;
        private UiKit.Action _careerBack;
        private UiKit.Action _careerStart;
        private UiKit.Action _hubHome;
        private UiKit.Action _hubSimulate;
        private UiKit.Action _hubPrimary;
        private Label _hubStanding;
        private Label _hubNext;
        private Label _hubResults;
        private Label _hubSummary;
        private VisualElement _hubTable;
        private List<Club> _careerClubs = new List<Club>();
        private World _careerWorld;
        private ulong _careerSeed;
        private int _careerClub;
        private int _careerRole;

        private void BuildCareerPages()
        {
            // New career: a new world's clubs, the role, then Start.
            _newCareerPage = Page("New career page", "New career");
            ScrollView list = UiKit.MakeScroll();
            list.style.flexShrink = 1;
            list.style.minHeight = 0;
            _newCareerPage.Content.Add(list);
            Label about = UiKit.Text(_theme, TypeScale.Body, "A new world of 36 clubs in three divisions. Until saves arrive, a career lasts as long as this page.", Role.Text);
            about.style.marginBottom = Spacing.M;
            list.Add(about);
            _careerPicker = MakePicker("Your club", StepCareerClub);
            list.Add(_careerPicker.Element);
            list.Add(Heading("Your role"));
            _roles = UiKit.MakeSegmented(_ui, _theme, Roles, 0, index =>
            {
                _careerRole = index;
                Log($"ELEVEN home pick role={index}");
            });
            list.Add(_roles.Element);
            _careerBack = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, "Back", ShowHome);
            _careerStart = UiKit.MakeAction(_ui, _theme, ActionRole.Primary, "Start", StartPressed);
            _newCareerPage.Actions.Add(_careerBack.Element);
            _newCareerPage.Actions.Add(_careerStart.Element);
            UiKit.Gaps(_newCareerPage.Actions);
            _newCareerPage.Element.style.display = DisplayStyle.None;

            // The hub: the matchday, the standing, the next fixture, the table and the results.
            _hubPage = Page("Hub page", "Season");
            ScrollView hub = UiKit.MakeScroll();
            hub.style.flexShrink = 1;
            hub.style.minHeight = 0;
            _hubPage.Content.Add(hub);
            _hubStanding = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _hubStanding.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hubStanding.style.marginBottom = Spacing.Xs;
            hub.Add(_hubStanding);
            _hubNext = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _hubNext.style.marginBottom = Spacing.M;
            hub.Add(_hubNext);
            _hubSummary = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _hubSummary.style.marginBottom = Spacing.M;
            _hubSummary.style.display = DisplayStyle.None;
            hub.Add(_hubSummary);
            hub.Add(Heading("Table"));
            _hubTable = new VisualElement { name = "Table", pickingMode = PickingMode.Ignore };
            _hubTable.style.marginBottom = Spacing.M;
            hub.Add(_hubTable);
            hub.Add(Heading("Your last results"));
            _hubResults = UiKit.Text(_theme, TypeScale.Small, "", Role.Text);
            hub.Add(_hubResults);
            Label note = UiKit.Text(_theme, TypeScale.Small, "Until saves arrive, this career lasts as long as the page.", Role.TextMuted);
            note.style.marginTop = Spacing.M;
            hub.Add(note);
            _hubHome = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, "Home", ShowHome);
            _hubSimulate = UiKit.MakeAction(_ui, _theme, ActionRole.Supporting, "Simulate", HubSimulate);
            _hubPrimary = UiKit.MakeAction(_ui, _theme, ActionRole.Primary, "Play", HubPrimary);
            _hubPage.Actions.Add(_hubHome.Element);
            _hubPage.Actions.Add(_hubSimulate.Element);
            _hubPage.Actions.Add(_hubPrimary.Element);
            _hubPage.Actions.style.flexWrap = Wrap.Wrap;
            UiKit.Gaps(_hubPage.Actions);
            _hubPage.Element.style.display = DisplayStyle.None;

            _scrim.Add(_newCareerPage.Element);
            _scrim.Add(_hubPage.Element);
            _newCareerPage.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
            _hubPage.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
        }

        // Career on Home: the hub of the career being played, or a new one.
        private void OpenCareer()
        {
            if (Career() != null)
            {
                ShowHub(null);
                return;
            }

            _careerSeed = (ulong)DateTime.UtcNow.Ticks;
            _careerWorld = CareerSession.NewWorld(_careerSeed);
            _careerClubs = QuickMatch.Clubs(_careerWorld);
            _careerClub = _careerClubs[0].Id;
            _careerRole = 0;
            UiKit.Select(_theme, _roles, 0);
            ShowClub(_careerPicker, _careerClubs[0]);
            ShowPage(Shown.NewCareer);
        }

        private void StepCareerClub(int step)
        {
            int at = _careerClubs.FindIndex(c => c.Id == _careerClub);
            at = (at + step + _careerClubs.Count) % _careerClubs.Count;
            _careerClub = _careerClubs[at].Id;
            ShowClub(_careerPicker, _careerClubs[at]);
            Log($"ELEVEN home pick career={at} club={_careerClub}");
        }

        private void StartPressed()
        {
            StartCareer(_careerWorld, _careerSeed, _careerClub, (CareerRole)_careerRole);
            ShowHub(null);
        }

        /// <summary>Opens the hub, Home's sheet with it if it is closed. <paramref name="via"/> says how, for the log; null keeps the last.</summary>
        public void ShowHub(string via)
        {
            if (via != null) _via = via;
            OpenSheet();
            RefreshHub();
            ShowPage(Shown.Hub);
        }

        private static string ActionName(HubAction action) => action switch
        {
            HubAction.Play => "Play",
            HubAction.Watch => "Watch",
            HubAction.NextMatchday => "Next matchday",
            _ => "Next season",
        };

        // The hub's lines from the session: CareerHub says them.
        private void RefreshHub()
        {
            CareerSession s = Career();
            if (s == null) return;
            _hubPage.Title.text = CareerHub.Header(s);
            _hubStanding.text = CareerHub.Standing(s);
            _hubNext.text = CareerHub.NextLine(s);
            string summary = CareerHub.Summary(s);
            _hubSummary.text = summary;
            _hubSummary.style.display = summary.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            List<string> results = CareerHub.ResultLines(s, 3);
            _hubResults.text = results.Count > 0 ? string.Join("\n", results) : "None yet.";

            _hubTable.Clear();
            foreach (HubRow row in CareerHub.TableRows(s)) _hubTable.Add(TableRow(row));

            HubAction action = CareerHub.Action(s);
            Relabel(_hubPrimary, ActionName(action));
            bool fixture = action == HubAction.Play || action == HubAction.Watch;
            _hubSimulate.Element.style.display = fixture ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // A table row's cells, at fixed widths so the columns line up: place, club, played,
        // difference and points. The human's row is bold and marked, not only coloured.
        private VisualElement TableRow(HubRow row)
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            line.style.minHeight = 20 * UiRoot.TextScale;
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.style.width = mark.style.height = Spacing.S;
            mark.style.marginRight = Spacing.Xs;
            UiKit.Radius(mark, Spacing.S / 2f);
            mark.style.backgroundColor = row.Mine ? _theme[Role.TextHeading].ToColor() : new Color(0, 0, 0, 0);
            line.Add(mark);
            string[] cells = row.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float[] widths = { 24, 52, 28, 36, 32 };
            for (int i = 0; i < cells.Length && i < widths.Length; i++)
            {
                Label cell = UiKit.Text(_theme, TypeScale.Small, cells[i], Role.Text);
                cell.style.width = widths[i] * UiRoot.TextScale;
                cell.style.unityTextAlign = i == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                cell.style.marginRight = Spacing.Xs;
                if (row.Mine) cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                line.Add(cell);
            }

            return line;
        }

        private static void Relabel(UiKit.Action action, string text)
        {
            if (action.Text == text) return;
            action.Text = text;
            action.Name = text;
            action.Element.name = text;
            action.Label.text = UiKit.Display(text, TypeScale.Label);
        }

        private void HubPrimary()
        {
            CareerSession s = Career();
            if (s == null) return;
            HubAction action = CareerHub.Action(s);
            Log($"ELEVEN hub action {action}");
            switch (action)
            {
                case HubAction.Play:
                case HubAction.Watch:
                    Close();
                    CareerPlay();
                    return;
                case HubAction.NextMatchday:
                    CareerSimulate();
                    break;
                default:
                    CareerNextSeason();
                    break;
            }

            ShowHub(null);
        }

        private void HubSimulate()
        {
            Log("ELEVEN hub action Simulate");
            CareerSimulate();
            ShowHub(null);
        }

        private List<VisualElement> CareerFocusOrder()
        {
            var order = new List<VisualElement>();
            if (_page == Shown.NewCareer)
            {
                order.Add(_careerPicker.Previous.Element);
                order.Add(_careerPicker.Next.Element);
                foreach (UiKit.Raised role in _roles.Segments) order.Add(role.Element);
                order.Add(_careerBack.Element);
                order.Add(_careerStart.Element);
                return order;
            }

            order.Add(_hubHome.Element);
            if (_hubSimulate.Element.resolvedStyle.display != DisplayStyle.None) order.Add(_hubSimulate.Element);
            order.Add(_hubPrimary.Element);
            return order;
        }

        // Where the shown career page's actions are, and what it says, for the smoke test.
        private void ReportCareer(Func<UiKit.Raised, string> at)
        {
            if (_page == Shown.NewCareer)
            {
                Log(FormattableString.Invariant($"ELEVEN home shown page=newcareer clubs={_careerClubs.Count} club={_careerClub} role={_careerRole} start={at(_careerStart)}"));
                return;
            }

            CareerSession s = Career();
            if (s == null) return;
            int played = s.Table().Find(r => r.Club == s.Club)?.Played ?? 0;
            Log(FormattableString.Invariant(
                $"ELEVEN hub shown via={_via} season={s.Season.Number} matchday={s.Season.Matchday + 1} played={played} action={CareerHub.Action(s)} primary={at(_hubPrimary)} simulate={at(_hubSimulate)} home={at(_hubHome)}"));
        }
    }
}
