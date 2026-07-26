using System;
using Craftwar.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// End-of-match score screen, in the spirit of the original's results panel:
    /// a large (~80% screen) table with one row per player — controller, race,
    /// result, a race-flavoured rank, and the match tally (kills, razings, losses,
    /// resources gathered) with a derived score. Modal; pauses the sim on push.
    ///
    /// Driven by the hashed <see cref="PlayerState.Outcome"/> rather than by
    /// catching the one-frame event, and reads all figures live from
    /// <see cref="ISimHost.Sim"/> state, so no extra plumbing is needed.
    /// </summary>
    public sealed class VictoryScreen : UIScreen
    {
        public override bool IsModal => true;

        readonly UIManager _manager;
        readonly ISimHost _host;
        readonly PlayerOutcome _outcome;
        readonly Action _onRestart;
        readonly Action _onQuitToMenu;

        // WC2 team colours, indexed by slot.
        static readonly Color[] TeamColors =
        {
            new Color(0.77f, 0.16f, 0.16f), // red
            new Color(0.16f, 0.16f, 0.77f), // blue
            new Color(0.00f, 0.58f, 0.00f), // green
            new Color(0.60f, 0.28f, 0.69f), // violet
            new Color(0.88f, 0.47f, 0.09f), // orange
            new Color(0.16f, 0.16f, 0.16f), // black
            new Color(0.88f, 0.88f, 0.88f), // white
            new Color(0.88f, 0.88f, 0.09f), // yellow
        };

        static readonly string[] HumanRanks =
            { "Peasant", "Squire", "Footman", "Knight", "Paladin", "Grand Admiral" };
        static readonly string[] OrcRanks =
            { "Peon", "Grunt", "Ogre", "Death Knight", "Chieftain", "War Chief" };
        static readonly int[] RankScore = { 0, 500, 1500, 3000, 6000, 12000 };

        public VictoryScreen(UIManager manager, ISimHost host, PlayerOutcome outcome,
                             Action onRestart, Action onQuitToMenu)
        {
            _manager = manager;
            _host = host;
            _outcome = outcome;
            _onRestart = onRestart;
            _onQuitToMenu = onQuitToMenu;
        }

        public override void Attach(VisualElement layerRoot, UIAssetCatalog assets)
        {
            var scrim = new VisualElement { name = "scrim" };
            scrim.AddToClassList("screen-scrim");
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0;
            scrim.style.top = 0;
            scrim.style.right = 0;
            scrim.style.bottom = 0;

            var panel = new VisualElement { name = "scorescreen" };
            panel.AddToClassList("menu");
            panel.style.width = Length.Percent(80);
            panel.style.maxWidth = 1000;
            panel.style.maxHeight = Length.Percent(85);
            scrim.Add(panel);

            bool won = _outcome == PlayerOutcome.Victorious;
            var title = new Label { text = won ? "Victory!" : "Defeat" };
            title.AddToClassList("menu__title");
            title.pickingMode = PickingMode.Ignore;
            panel.Add(title);

            panel.Add(HeaderRow());

            var state = _host?.Sim?.State;
            if (state != null)
            {
                for (int p = 0; p < SimConstants.MaxPlayers; p++)
                {
                    ref PlayerState ps = ref state.Players[p];
                    if (ps.Controller == Controller.None)
                        continue; // scenery/rescue slots aren't scored
                    panel.Add(PlayerRow(p, ref ps, p == HudScreen.LocalPlayer));
                }
            }

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 12 } };
            AddButton(buttons, "continue", won ? "Keep Playing" : "Watch On", () => _manager.Pop());
            AddButton(buttons, "restart", "Play Again", () => _onRestart?.Invoke());
            AddButton(buttons, "menu", "Main Menu", () => _onQuitToMenu?.Invoke());
            panel.Add(buttons);

            layerRoot.Add(scrim);
            Root = scrim;
        }

        // --- rows -----------------------------------------------------------

        static readonly (string text, int width)[] Columns =
        {
            ("Player", 0), ("Result", 90), ("Rank", 120), ("Killed", 70),
            ("Razed", 70), ("Lost", 70), ("Gold", 90), ("Lumber", 90),
            ("Oil", 80), ("Score", 90),
        };

        VisualElement HeaderRow()
        {
            var row = Row();
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(1, 1, 1, 0.2f);
            row.style.marginTop = 8;
            row.style.paddingBottom = 4;
            for (int c = 0; c < Columns.Length; c++)
            {
                var cell = Cell(Columns[c].text, Columns[c].width, c == 0);
                cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                cell.style.opacity = 0.85f;
                row.Add(cell);
            }
            return row;
        }

        VisualElement PlayerRow(int slot, ref PlayerState ps, bool isLocal)
        {
            int lost = ps.UnitsLost + ps.BuildingsLost;
            int score = ps.UnitsKilled * 50 + ps.BuildingsRazed * 100
                + (ps.GoldGathered + ps.LumberGathered + ps.OilGathered) / 20;

            var row = Row();
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            if (isLocal)
                row.style.backgroundColor = new Color(1, 1, 1, 0.08f);

            // Player: colour swatch + name + race/controller.
            var who = new VisualElement();
            who.style.flexGrow = 1;
            who.style.flexDirection = FlexDirection.Row;
            who.style.alignItems = Align.Center;
            var swatch = new VisualElement();
            swatch.style.width = 12;
            swatch.style.height = 12;
            swatch.style.marginRight = 8;
            swatch.style.backgroundColor = TeamColors[slot % TeamColors.Length];
            who.Add(swatch);
            string ctrl = ps.Controller == Controller.Human ? "Human" : "Computer";
            string race = ps.Race == Race.Orc ? "Orc" : "Human";
            var name = new Label { text = $"Player {slot + 1}{(isLocal ? " (You)" : "")}  ·  {race} {ctrl}" };
            name.AddToClassList("text");
            who.Add(name);
            row.Add(who);

            row.Add(Cell(ResultText(ps.Outcome), 90, false, ResultColor(ps.Outcome)));
            row.Add(Cell(RankTitle(ps.Race, score), 120));
            row.Add(Num(ps.UnitsKilled, 70));
            row.Add(Num(ps.BuildingsRazed, 70));
            row.Add(Num(lost, 70));
            row.Add(Num(ps.GoldGathered, 90));
            row.Add(Num(ps.LumberGathered, 90));
            row.Add(Num(ps.OilGathered, 80));
            row.Add(Num(score, 90));
            return row;
        }

        // --- cell helpers ---------------------------------------------------

        static VisualElement Row() => new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, alignItems = Align.Center },
        };

        static Label Cell(string text, int width, bool first = false, Color? color = null)
        {
            var l = new Label { text = text };
            l.AddToClassList("text");
            l.pickingMode = PickingMode.Ignore;
            if (first)
                l.style.flexGrow = 1;
            else
            {
                l.style.width = width;
                l.style.unityTextAlign = TextAnchor.MiddleRight;
            }
            if (color.HasValue)
                l.style.color = color.Value;
            return l;
        }

        static Label Num(int value, int width) => Cell(value.ToString(), width);

        static string ResultText(PlayerOutcome o) => o switch
        {
            PlayerOutcome.Victorious => "Victory",
            PlayerOutcome.Defeated => "Defeated",
            _ => "—",
        };

        static Color ResultColor(PlayerOutcome o) => o switch
        {
            PlayerOutcome.Victorious => new Color(0.55f, 0.85f, 0.4f),
            PlayerOutcome.Defeated => new Color(0.85f, 0.45f, 0.4f),
            _ => new Color(0.8f, 0.8f, 0.8f),
        };

        static string RankTitle(Race race, int score)
        {
            var ladder = race == Race.Orc ? OrcRanks : HumanRanks;
            int i = 0;
            for (int r = RankScore.Length - 1; r >= 0; r--)
                if (score >= RankScore[r]) { i = r; break; }
            return ladder[i];
        }

        static void AddButton(VisualElement parent, string name, string text, Action onClick)
        {
            var button = new Button(() => onClick?.Invoke()) { name = name, text = text };
            button.AddToClassList("menu__button");
            button.style.flexGrow = 1;
            button.style.marginLeft = 4;
            button.style.marginRight = 4;
            parent.Add(button);
        }

        // Single player freezes behind the score table. A networked match must
        // NOT: a defeated player keeps simulating as an observer and keeps
        // feeding the turn schedule, or the first elimination in a 4v4 stalls
        // everyone still playing.
        public override void OnPush()
        {
            if (_host != null && _host.CanPauseLocally)
                _host.SetPaused(true);
        }

        public override void OnPop()
        {
            if (_host != null && _host.CanPauseLocally)
                _host.SetPaused(false);
        }

        /// <summary>Escape dismisses back to the board rather than closing the match.</summary>
        public override bool HandleEscape()
        {
            _manager.Pop();
            return true;
        }
    }
}
