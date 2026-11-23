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

        /// <summary>Start a career: its world, seed, club, role and slot (#171).</summary>
        public Action<World, ulong, int, CareerRole, int> StartCareer = (world, seed, club, role, slot) => { };

        /// <summary>A slot as the list shows it (#171).</summary>
        public Func<int, Eleven.Career.SlotSummary> SlotSummary = slot => new Eleven.Career.SlotSummary();

        /// <summary>Continue the career in a slot: loaded, at its hub.</summary>
        public Action<int> ContinueSlot = slot => { };

        /// <summary>The slot of the career in play, or −1 with none.</summary>
        public Func<int> CareerSlot = () => -1;

        /// <summary>Save now, on the hub: what happened, in words.</summary>
        public Func<string> CareerSaveNow = () => "";

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
        private Label _hubOut;
        private Label _hubResults;
        private Label _hubSummary;
        private VisualElement _hubTable;
        private List<Club> _careerClubs = new List<Club>();
        private World _careerWorld;
        private ulong _careerSeed;
        private int _careerClub;
        private int _careerRole;

        // The slots (#171): five rows, each its words and its action; the slot a new career goes into.
        private UiKit.Surface _slotsPage;
        private readonly Label[] _slotText = new Label[Eleven.Career.SaveSlots.Count];
        private readonly UiKit.Action[] _slotAction = new UiKit.Action[Eleven.Career.SaveSlots.Count];
        private UiKit.Action _slotsBack;
        private UiKit.Action _hubSave;
        private Label _hubStatus;
        private int _newSlot;

        private void BuildCareerPages()
        {
            // New career: a new world's clubs, the role, then Start.
            _newCareerPage = Page("New career page", "New career");
            ScrollView list = UiKit.MakeScroll();
            list.style.flexShrink = 1;
            list.style.minHeight = 0;
            _newCareerPage.Content.Add(list);
            Label about = UiKit.Text(_theme, TypeScale.Body, "A new world of 36 clubs in three divisions, saved on this device after every fixture.", Role.Text);
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
            _careerBack = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, "Back", OpenCareer);
            _careerStart = UiKit.MakeAction(_ui, _theme, ActionRole.Primary, "Start", StartPressed);

            // The slots (#171): each slot's career, or Empty, with Continue or New career.
            _slotsPage = Page("Slots page", "Career");
            ScrollView slots = UiKit.MakeScroll();
            slots.style.flexShrink = 1;
            slots.style.minHeight = 0;
            _slotsPage.Content.Add(slots);
            Label saved = UiKit.Text(_theme, TypeScale.Body, "Five careers, saved on this device after every fixture.", Role.Text);
            saved.style.marginBottom = Spacing.M;
            slots.Add(saved);
            for (int i = 0; i < Eleven.Career.SaveSlots.Count; i++)
            {
                int slot = i;
                VisualElement row = UiKit.Row(Spacing.S);
                row.style.alignItems = Align.Center;
                row.style.marginBottom = Spacing.S;
                _slotText[i] = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
                _slotText[i].style.flexGrow = 1;
                _slotText[i].style.flexShrink = 1;
                _slotAction[i] = UiKit.MakeAction(_ui, _theme, ActionRole.Supporting, $"Slot {i + 1}", () => SlotPressed(slot));
                row.Add(_slotText[i]);
                row.Add(_slotAction[i].Element);
                UiKit.Gaps(row);
                slots.Add(row);
            }

            _slotsBack = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, "Back", ShowHome);
            _slotsPage.Actions.Add(_slotsBack.Element);
            UiKit.Gaps(_slotsPage.Actions);
            _slotsPage.Element.style.display = DisplayStyle.None;
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
            // Who's out (#176): injured or banned, shown only when someone is.
            _hubOut = UiKit.Text(_theme, TypeScale.Small, "", Role.Text);
            _hubOut.style.marginBottom = Spacing.M;
            _hubOut.style.display = DisplayStyle.None;
            hub.Add(_hubOut);
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
            _hubStatus = UiKit.Text(_theme, TypeScale.Small, "", Role.TextMuted);
            _hubStatus.style.marginTop = Spacing.M;
            hub.Add(_hubStatus);
            _hubHome = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, "Home", ShowHome);
            _hubSave = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, "Save", HubSave);
            _hubSimulate = UiKit.MakeAction(_ui, _theme, ActionRole.Supporting, "Simulate", HubSimulate);
            _hubPrimary = UiKit.MakeAction(_ui, _theme, ActionRole.Primary, "Play", HubPrimary);
            _hubPage.Actions.Add(_hubHome.Element);
            _hubPage.Actions.Add(_hubSave.Element);
            _hubPage.Actions.Add(_hubSimulate.Element);
            _hubPage.Actions.Add(_hubPrimary.Element);
            _hubPage.Actions.style.flexWrap = Wrap.Wrap;
            UiKit.Gaps(_hubPage.Actions);
            _hubPage.Element.style.display = DisplayStyle.None;

            _scrim.Add(_newCareerPage.Element);
            _scrim.Add(_hubPage.Element);
            _scrim.Add(_slotsPage.Element);
            _newCareerPage.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
            _hubPage.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
            _slotsPage.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
        }

        // Career on Home: the five slots (#171).
        private void OpenCareer()
        {
            RefreshSlots();
            ShowPage(Shown.Slots);
        }

        private void RefreshSlots()
        {
            for (int i = 0; i < Eleven.Career.SaveSlots.Count; i++)
            {
                Eleven.Career.SlotSummary s = SlotSummary(i);
                _slotText[i].text = CareerHub.SlotLine(i, s, Career(), CareerSlot());
                Relabel(_slotAction[i], CareerHub.SlotOffer(i, s, Career(), CareerSlot()) == SlotChoice.New ? "New career" : "Continue");
            }
        }

        private void SlotPressed(int slot)
        {
            SlotChoice offer = CareerHub.SlotOffer(slot, SlotSummary(slot), Career(), CareerSlot());
            Log(FormattableString.Invariant($"ELEVEN home slot={slot} {offer.ToString().ToLowerInvariant()}"));
            if (offer == SlotChoice.New) NewCareer(slot);
            else if (offer == SlotChoice.Resume) ShowHub("resume");
            else ContinueSlot(slot);
        }

        // New career into a slot: a new world's clubs, from a seed taken now.
        private void NewCareer(int slot)
        {
            _newSlot = slot;
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
            StartCareer(_careerWorld, _careerSeed, _careerClub, (CareerRole)_careerRole, _newSlot);
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
            string outs = CareerHub.OutLine(s);
            _hubOut.text = outs;
            _hubOut.style.display = outs.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
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

        private void HubSave()
        {
            Log("ELEVEN hub action Save");
            SetCareerStatus(CareerSaveNow());
        }

        /// <summary>What the hub says about the last save or load.</summary>
        public void SetCareerStatus(string text) => _hubStatus.text = text;

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

            if (_page == Shown.Slots)
            {
                foreach (UiKit.Action a in _slotAction) order.Add(a.Element);
                order.Add(_slotsBack.Element);
                return order;
            }

            order.Add(_hubHome.Element);
            order.Add(_hubSave.Element);
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

            if (_page == Shown.Slots)
            {
                int empty = 0;
                for (int i = 0; i < Eleven.Career.SaveSlots.Count; i++) empty += SlotSummary(i).Empty ? 1 : 0;
                Log(FormattableString.Invariant($"ELEVEN home shown page=slots slots={Eleven.Career.SaveSlots.Count} empty={empty} first={at(_slotAction[0])} firstAction={_slotAction[0].Text.Replace(' ', '_')} firstOffer={CareerHub.SlotOffer(0, SlotSummary(0), Career(), CareerSlot())}"));
                return;
            }

            CareerSession s = Career();
            if (s == null) return;
            int played = s.Table().Find(r => r.Club == s.Club)?.Played ?? 0;
            Log(FormattableString.Invariant(
                $"ELEVEN hub shown via={_via} season={s.Season.Number} matchday={s.Season.Matchday + 1} played={played} action={CareerHub.Action(s)} primary={at(_hubPrimary)} simulate={at(_hubSimulate)} home={at(_hubHome)} save={at(_hubSave)}"));
        }
    }
}
