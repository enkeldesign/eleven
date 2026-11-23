using System;
using System.Collections.Generic;
using System.Globalization;
using Eleven.Presentation;
using Eleven.Simulation.Harness;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    using Club = Eleven.Career.Club;

    /// <summary>
    /// Home (#160): the game's front door, over everything. Two pages:
    /// <list type="bullet">
    /// <item>Home: Quick match, Practice (the test bench, as before), the last result and the build.</item>
    /// <item>Quick match: your club, the opponent, the size and the half's length, then Kick off.</item>
    /// </list>
    /// The clubs and the choices come from <see cref="QuickMatch"/>; the host plays the match.
    /// The clubs' kits are the world's tokens, resolved through the design system's kit tier.
    /// Play waits while Home is open, and its page covers the pitch.
    /// </summary>
    internal sealed partial class HomeSheet
    {
        /// <summary>A quick match's choices: the two clubs' ids, and indices into the sizes and the halves.</summary>
        public struct Choice
        {
            public int Home;
            public int Away;
            public int Size;
            public int Half;

            public string ToText() => FormattableString.Invariant($"home={Home},away={Away},size={Size},half={Half}");

            /// <summary>The choices kept on an earlier visit, each checked; the first two clubs, 5v5 and 2 min otherwise.</summary>
            public static Choice Parse(string text, IReadOnlyList<Club> clubs)
            {
                var c = new Choice { Home = clubs[0].Id, Away = clubs[1].Id, Size = 0, Half = 1 };
                if (string.IsNullOrEmpty(text)) return c;
                int home = c.Home, away = c.Away, size = c.Size, half = c.Half;
                foreach (string part in text.Split(','))
                {
                    string[] kv = part.Split('=');
                    if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) continue;
                    if (kv[0] == "home") home = v;
                    else if (kv[0] == "away") away = v;
                    else if (kv[0] == "size") size = v;
                    else if (kv[0] == "half") half = v;
                }

                bool Known(int id)
                {
                    foreach (Club club in clubs)
                    {
                        if (club.Id == id) return true;
                    }

                    return false;
                }

                if (Known(home) && Known(away) && home != away)
                {
                    c.Home = home;
                    c.Away = away;
                }

                if (size >= 0 && size < QuickMatch.Sizes.Length) c.Size = size;
                if (half >= 0 && half < QuickMatch.Halves.Length) c.Half = half;
                return c;
            }
        }

        // A club's picker: the previous and next clubs either side of its name, short name, division and kit.
        private sealed class Picker
        {
            public VisualElement Element;
            public UiKit.Action Previous;
            public UiKit.Action Next;
            public Label Name;
            public Label Meta;
            public VisualElement Shirt;
            public VisualElement Shorts;
        }

        private readonly UiRoot _ui;
        private readonly bool _trace;
        private readonly Theme _theme = Theme.Ordinary;
        private readonly Func<string> _build;
        private readonly Action<string> _keep;
        private readonly Action<Choice> _kickOff;
        private readonly System.Action _practise;
        private readonly VisualElement _scrim;
        private readonly UiKit.Surface _homePage;
        private readonly UiKit.Surface _quickPage;
        private readonly Label _last;
        private readonly Label _buildLine;
        private readonly UiKit.Action _quick;
        private readonly UiKit.Action _practice;
        private readonly UiKit.Action _back;
        private readonly UiKit.Action _kick;
        private readonly Picker _mine;
        private readonly Picker _theirs;
        private readonly UiKit.Segmented _sizes;
        private readonly UiKit.Segmented _halves;
        private Choice _choice;
        private string _via = "";
        private bool _reported;
        private readonly UiKit.Action _career;

        // The pages (#160, #168): Home, Quick match, New career and the career's hub.
        private enum Shown
        {
            Home,
            Quick,
            NewCareer,
            Hub,
            Slots,
            Squad,
        }

        private Shown _page;

        public HomeSheet(UiRoot ui, bool trace, IReadOnlyList<Club> clubs, Func<string> build, string kept, Action<string> keep,
            Action<Choice> kickOff, System.Action practise)
        {
            _ui = ui;
            _trace = trace;
            Clubs = clubs;
            _build = build;
            _keep = keep;
            _kickOff = kickOff;
            _practise = practise;
            _choice = Choice.Parse(kept, clubs);

            // The page behind everything: Home covers the pitch, so nothing reads through it.
            _scrim = new VisualElement { name = "Home", pickingMode = PickingMode.Position };
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = _scrim.style.top = _scrim.style.right = _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = _theme[Role.SurfacePage].ToColor();
            _scrim.style.justifyContent = Justify.Center;
            _scrim.style.alignItems = Align.Center;
            _scrim.style.paddingLeft = _scrim.style.paddingRight = _scrim.style.paddingTop = _scrim.style.paddingBottom = Spacing.M;
            _scrim.style.display = DisplayStyle.None;

            // Home: the name, what it is, the last result, then the two ways in.
            _homePage = Page("Home page", null);
            Label name = UiKit.Text(_theme, TypeScale.Display, "Eleven", Role.TextHeading);
            name.style.marginBottom = Spacing.S;
            _homePage.Content.Add(name);
            Label about = UiKit.Text(_theme, TypeScale.Body, "Pick two clubs and play them, or practise on the test bench.", Role.Text);
            about.style.marginBottom = Spacing.M;
            _homePage.Content.Add(about);
            _last = UiKit.Text(_theme, TypeScale.Body, "", Role.Text);
            _last.style.unityFontStyleAndWeight = FontStyle.Bold;
            _last.style.marginBottom = Spacing.M;
            _last.style.display = DisplayStyle.None;
            _homePage.Content.Add(_last);
            _buildLine = UiKit.Text(_theme, TypeScale.Small, "", Role.TextMuted);
            _homePage.Content.Add(_buildLine);
            _practice = UiKit.MakeAction(ui, _theme, ActionRole.Supporting, "Practice", Practise);
            _career = UiKit.MakeAction(ui, _theme, ActionRole.Supporting, "Career", OpenCareer);
            _quick = UiKit.MakeAction(ui, _theme, ActionRole.Primary, "Quick match", ShowQuick);
            _homePage.Actions.Add(_practice.Element);
            _homePage.Actions.Add(_career.Element);
            _homePage.Actions.Add(_quick.Element);
            _homePage.Actions.style.flexWrap = Wrap.Wrap;
            UiKit.Gaps(_homePage.Actions);

            // Quick match: the two clubs, the size and the half, then Kick off.
            _quickPage = Page("Quick match page", "Quick match");
            ScrollView list = UiKit.MakeScroll();
            list.style.flexShrink = 1;
            list.style.minHeight = 0;
            _quickPage.Content.Add(list);
            _mine = MakePicker("Your club", step => Step(ref _choice.Home, _choice.Away, step, "mine"));
            _theirs = MakePicker("Opponent", step => Step(ref _choice.Away, _choice.Home, step, "theirs"));
            list.Add(_mine.Element);
            list.Add(_theirs.Element);
            list.Add(Heading("Size"));
            _sizes = UiKit.MakeSegmented(ui, _theme, QuickMatch.SizeNames, _choice.Size, index =>
            {
                _choice.Size = index;
                Kept();
                Log($"ELEVEN home pick size={index}");
            });
            _sizes.Element.style.marginBottom = Spacing.M;
            list.Add(_sizes.Element);
            list.Add(Heading("Each half"));
            _halves = UiKit.MakeSegmented(ui, _theme, QuickMatch.HalfNames, _choice.Half, index =>
            {
                _choice.Half = index;
                Kept();
                Log($"ELEVEN home pick half={index}");
            });
            list.Add(_halves.Element);
            _back = UiKit.MakeAction(ui, _theme, ActionRole.Utility, "Back", ShowHome);
            _kick = UiKit.MakeAction(ui, _theme, ActionRole.Primary, "Kick off", KickOff);
            _quickPage.Actions.Add(_back.Element);
            _quickPage.Actions.Add(_kick.Element);
            UiKit.Gaps(_quickPage.Actions);
            _quickPage.Element.style.display = DisplayStyle.None;

            _scrim.Add(_homePage.Element);
            _scrim.Add(_quickPage.Element);
            BuildCareerPages();
            ui.Root.Add(_scrim);
            _homePage.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
            _quickPage.Body.RegisterCallback<GeometryChangedEvent>(_ => Report());
            ShowChoice();
        }

        /// <summary>The clubs, in the picker's order.</summary>
        public IReadOnlyList<Club> Clubs { get; }

        public bool Open { get; private set; }

        /// <summary>Whether the Quick match page is the one shown.</summary>
        public bool QuickShown => _page == Shown.Quick;

        /// <summary>Called with true when Home opens and false when it closes: the HUD behind it takes neither focus nor presses.</summary>
        public Action<bool> OpenChanged;

        /// <summary>The screen's unsafe edges, in points: the pages keep clear of them.</summary>
        public void Inset(float top, float right, float bottom, float left)
        {
            _scrim.style.paddingTop = Spacing.M + top;
            _scrim.style.paddingRight = Spacing.M + right;
            _scrim.style.paddingBottom = Spacing.M + bottom;
            _scrim.style.paddingLeft = Spacing.M + left;
        }

        /// <summary>Opens Home on its first page. <paramref name="via"/> says how, for the log: address or match.</summary>
        public void Show(string via)
        {
            _via = via;
            _buildLine.text = $"Build {_build()}";
            OpenSheet();
            ShowHome();
        }

        /// <summary>The last quick match's result, under the name.</summary>
        public void SetLast(string result)
        {
            _last.text = $"Last match: {result}";
            _last.style.display = DisplayStyle.Flex;
        }

        /// <summary>Escape: from any other page back to Home; on Home, nothing.</summary>
        public void Back()
        {
            if (Open && _page != Shown.Home) ShowHome();
        }

        /// <summary>The shown page's controls in order, for Tab.</summary>
        public List<VisualElement> FocusOrder()
        {
            var order = new List<VisualElement>();
            if (_page == Shown.NewCareer || _page == Shown.Hub || _page == Shown.Slots || _page == Shown.Squad) return CareerFocusOrder();
            if (_page == Shown.Home)
            {
                order.Add(_practice.Element);
                order.Add(_career.Element);
                order.Add(_quick.Element);
                return order;
            }

            order.Add(_mine.Previous.Element);
            order.Add(_mine.Next.Element);
            order.Add(_theirs.Previous.Element);
            order.Add(_theirs.Next.Element);
            foreach (UiKit.Raised size in _sizes.Segments) order.Add(size.Element);
            foreach (UiKit.Raised half in _halves.Segments) order.Add(half.Element);
            order.Add(_back.Element);
            order.Add(_kick.Element);
            return order;
        }

        private void Close()
        {
            if (!Open) return;
            Open = false;
            _scrim.style.display = DisplayStyle.None;
            OpenChanged?.Invoke(false);
        }

        private void ShowHome() => ShowPage(Shown.Home);

        private void ShowQuick()
        {
            ShowChoice();
            ShowPage(Shown.Quick);
        }

        // One page at a time, and its places said once it has its layout.
        private void ShowPage(Shown page)
        {
            _page = page;
            _homePage.Element.style.display = page == Shown.Home ? DisplayStyle.Flex : DisplayStyle.None;
            _quickPage.Element.style.display = page == Shown.Quick ? DisplayStyle.Flex : DisplayStyle.None;
            _newCareerPage.Element.style.display = page == Shown.NewCareer ? DisplayStyle.Flex : DisplayStyle.None;
            _hubPage.Element.style.display = page == Shown.Hub ? DisplayStyle.Flex : DisplayStyle.None;
            _slotsPage.Element.style.display = page == Shown.Slots ? DisplayStyle.Flex : DisplayStyle.None;
            _squadPage.Element.style.display = page == Shown.Squad ? DisplayStyle.Flex : DisplayStyle.None;
            _reported = false;

            // Said once laid out: a page already shown may have changed its size (the hub after a
            // matchday), and a newly shown one reports on its first layout too.
            _scrim.schedule.Execute(Report).StartingIn(100);
        }

        // Opens the sheet, if it isn't, without choosing a page.
        private void OpenSheet()
        {
            if (Open) return;
            Open = true;
            _scrim.style.display = DisplayStyle.Flex;
            OpenChanged?.Invoke(true);
        }

        private void Practise()
        {
            Close();
            Log("ELEVEN home practice");
            _practise();
        }

        private void KickOff()
        {
            Close();
            _kickOff(_choice);
        }

        // One club on or back, round the list, never the other side's club: a club doesn't play itself.
        private void Step(ref int club, int other, int step, string side)
        {
            int at = IndexOf(club);
            for (int i = 0; i < Clubs.Count; i++)
            {
                at = (at + step + Clubs.Count) % Clubs.Count;
                if (Clubs[at].Id != other) break;
            }

            club = Clubs[at].Id;
            ShowChoice();
            Kept();
            Log($"ELEVEN home pick {side}={at} club={club}");
        }

        private int IndexOf(int id)
        {
            for (int i = 0; i < Clubs.Count; i++)
            {
                if (Clubs[i].Id == id) return i;
            }

            return 0;
        }

        private void Kept() => _keep(_choice.ToText());

        private void ShowChoice()
        {
            ShowClub(_mine, Clubs[IndexOf(_choice.Home)]);
            ShowClub(_theirs, Clubs[IndexOf(_choice.Away)]);
            UiKit.Select(_theme, _sizes, _choice.Size);
            UiKit.Select(_theme, _halves, _choice.Half);
        }

        private void ShowClub(Picker p, Club club)
        {
            p.Name.text = club.Name;
            p.Meta.text = FormattableString.Invariant($"{club.ShortName} · Division {club.Division}");
            p.Shirt.style.backgroundColor = KitOf(club.Shirt).ToColor();
            p.Shorts.style.backgroundColor = KitOf(club.Shorts).ToColor();
        }

        // A kit token's colour, from the design system's kit tier (#158).
        private static Rgba KitOf(Eleven.Career.KitColour token)
        {
            string name = token.ToString();
            foreach ((string Name, Rgba Colour) kit in Kit.All)
            {
                if (kit.Name == name) return kit.Colour;
            }

            return Kit.Grey;
        }

        private UiKit.Surface Page(string name, string title)
        {
            UiKit.Surface page = UiKit.MakeSheet(_theme, title);
            page.Element.name = name;
            page.Element.style.width = Length.Percent(100);
            page.Element.style.maxWidth = 560;
            page.Element.style.maxHeight = Length.Percent(100);
            page.Body.style.flexShrink = 1;
            page.Content.style.flexShrink = 1;
            page.Content.style.minHeight = 0;
            page.Actions.style.flexShrink = 0;
            page.Actions.style.justifyContent = Justify.FlexEnd;
            if (page.Title != null) page.Title.style.flexShrink = 0;
            return page;
        }

        private Label Heading(string text)
        {
            Label l = UiKit.Text(_theme, TypeScale.Body, text, Role.Text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = Spacing.Xs;
            return l;
        }

        private Picker MakePicker(string what, Action<int> step)
        {
            var p = new Picker { Element = new VisualElement { name = what } };
            p.Element.style.marginBottom = Spacing.M;
            p.Element.Add(Heading(what));
            VisualElement row = UiKit.Row(Spacing.S);
            row.style.alignItems = Align.Center;
            p.Previous = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, $"Previous: {what}", () => step(-1), "<");
            p.Next = UiKit.MakeAction(_ui, _theme, ActionRole.Utility, $"Next: {what}", () => step(1), ">");

            // The kit: shirt over shorts, each outlined so a white kit shows on the sheet.
            var kit = new VisualElement { pickingMode = PickingMode.Ignore };
            p.Shirt = Swatch(16);
            p.Shorts = Swatch(10);
            kit.Add(p.Shirt);
            kit.Add(p.Shorts);

            var words = new VisualElement { pickingMode = PickingMode.Ignore };
            words.style.flexGrow = 1;
            words.style.flexShrink = 1;
            p.Name = UiKit.Text(_theme, TypeScale.Title, "", Role.TextHeading);
            p.Meta = UiKit.Text(_theme, TypeScale.Small, "", Role.TextMuted);
            words.Add(p.Name);
            words.Add(p.Meta);

            row.Add(p.Previous.Element);
            row.Add(kit);
            row.Add(words);
            row.Add(p.Next.Element);
            UiKit.Gaps(row);
            p.Element.Add(row);
            return p;
        }

        private VisualElement Swatch(float height)
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.style.width = 24;
            s.style.height = height;
            s.style.marginBottom = 2;
            UiKit.Border(s, 2);
            UiKit.BorderColour(s, _theme[Role.Outline]);
            UiKit.Radius(s, 3);
            return s;
        }

        // Once the shown page has its layout: where its actions are, for the smoke test.
        private void Report()
        {
            if (!Open || _reported) return;
            UiKit.Surface page = _page == Shown.Quick ? _quickPage : _page == Shown.NewCareer ? _newCareerPage : _page == Shown.Hub ? _hubPage : _page == Shown.Slots ? _slotsPage : _page == Shown.Squad ? _squadPage : _homePage;
            if (!(page.Body.layout.width > 0)) return;
            _reported = true;
            float s = _ui.PixelsPerPoint;
            string At(UiKit.Raised r)
            {
                Vector2 c = r.Element.worldBound.center * s;
                return FormattableString.Invariant($"{c.x:F0},{c.y:F0}");
            }

            if (_page == Shown.Home)
            {
                Log($"ELEVEN home shown page=home via={_via} quick={At(_quick)} practice={At(_practice)} career={At(_career)}");
                return;
            }

            if (_page == Shown.NewCareer || _page == Shown.Hub || _page == Shown.Slots || _page == Shown.Squad)
            {
                ReportCareer(At);
                return;
            }

            Log(FormattableString.Invariant(
                $"ELEVEN home shown page=quick clubs={Clubs.Count} mine={_choice.Home} theirs={_choice.Away} size={_choice.Size} half={_choice.Half} theirsNext={At(_theirs.Next)} size1={At(_sizes.Segments[1])} kickoff={At(_kick)}"));
        }

        private static void Log(string message) => Debug.Log(message);
    }
}
