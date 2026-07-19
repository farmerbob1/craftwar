using System.Collections.Generic;
using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Bottom-centre feed of the last few sim events for the local player.
    /// Lines fade out on a scheduled class toggle rather than a per-frame
    /// timer, so an idle feed costs nothing.
    /// </summary>
    public sealed class NotificationFeedView
    {
        const int MaxLines = 4;
        const long LineLifetimeMs = 6000;

        readonly VisualElement _root;
        readonly List<Label> _lines = new List<Label>();
        readonly byte _player;

        public NotificationFeedView(VisualElement notifyLayer, byte player)
        {
            _player = player;
            _root = new VisualElement { name = "notification-feed", pickingMode = PickingMode.Ignore };
            _root.AddToClassList("notify-feed");
            notifyLayer.Add(_root);
        }

        /// <summary>Non-null when the last batch denied a command, so the resource
        /// strip can flash the resource that was short.</summary>
        public DenyReason LastDeny { get; private set; }

        public void Handle(List<SimEvent> events)
        {
            LastDeny = DenyReason.None;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Player != _player)
                    continue;
                string text = Describe(ref e);
                if (text == null)
                    continue;
                if (e.Kind == SimEventKind.CommandDenied)
                    LastDeny = (DenyReason)e.A;
                Push(text, e.Kind == SimEventKind.CommandDenied
                    || e.Kind == SimEventKind.UnderAttack
                    || e.Kind == SimEventKind.BuildSiteBlocked);
            }
        }

        static string Describe(ref SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.CommandDenied:
                    return (DenyReason)e.A switch
                    {
                        DenyReason.NotEnoughGold => "Not enough gold",
                        DenyReason.NotEnoughLumber => "Not enough lumber",
                        DenyReason.NotEnoughOil => "Not enough oil",
                        DenyReason.NotEnoughFood => "Not enough food — build more farms",
                        DenyReason.TechUnavailable => "You must build more structures first",
                        DenyReason.Busy => "That building is already busy",
                        DenyReason.SiteBlocked => "Cannot build there",
                        _ => null,
                    };
                case SimEventKind.TrainComplete:
                    return UnitNames.Of((UnitTypeId)e.B) + " ready";
                case SimEventKind.ResearchComplete:
                    return UnitNames.Of((UpgradeId)e.B) + " complete";
                case SimEventKind.ConstructionComplete:
                    return UnitNames.Of((UnitTypeId)e.B) + " complete";
                case SimEventKind.UpgradeComplete:
                    return "Upgraded to " + UnitNames.Of((UnitTypeId)e.B);
                case SimEventKind.BuildSiteBlocked:
                    return "Cannot build there";
                case SimEventKind.UnderAttack:
                    return "You are under attack!";
                case SimEventKind.MineCollapsed:
                    return "Your gold mine has collapsed";
                default:
                    return null;
            }
        }

        void Push(string text, bool warn)
        {
            var line = new Label(text) { pickingMode = PickingMode.Ignore };
            line.AddToClassList("notify-feed__line");
            if (warn)
                line.AddToClassList("notify-feed__line--warn");
            _root.Add(line);
            _lines.Add(line);

            while (_lines.Count > MaxLines)
            {
                _lines[0].RemoveFromHierarchy();
                _lines.RemoveAt(0);
            }

            line.schedule.Execute(() =>
            {
                line.RemoveFromHierarchy();
                _lines.Remove(line);
            }).StartingIn(LineLifetimeMs);
        }
    }
}
