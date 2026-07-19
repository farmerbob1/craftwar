using Craftwar.Sim;
using UnityEngine.UIElements;

namespace Craftwar.View
{
    /// <summary>
    /// Top resource strip. Caches the last written value per field and only
    /// touches Label.text when it actually changed — this runs every frame and
    /// string building here would allocate continuously.
    /// </summary>
    public sealed class ResourcePanelView
    {
        readonly Label _gold, _lumber, _oil, _food;

        int _lastGold = -1, _lastLumber = -1, _lastOil = -1;
        int _lastFoodUsed = -1, _lastFoodMax = -1;
        bool _lastFoodWarn;

        public ResourcePanelView(VisualElement root)
        {
            _gold = root.Q<Label>("gold-value");
            _lumber = root.Q<Label>("lumber-value");
            _oil = root.Q<Label>("oil-value");
            _food = root.Q<Label>("food-value");

            _gold?.AddToClassList("resource-bar__value--gold");
            _lumber?.AddToClassList("resource-bar__value--lumber");
            _oil?.AddToClassList("resource-bar__value--oil");
        }

        /// <summary>
        /// Flash the resource a denied command was short of. Applied as a class
        /// with a scheduled removal so repeated denials keep re-triggering it.
        /// </summary>
        public void FlashShortfall(DenyReason reason)
        {
            Label target = reason switch
            {
                DenyReason.NotEnoughGold => _gold,
                DenyReason.NotEnoughLumber => _lumber,
                DenyReason.NotEnoughOil => _oil,
                DenyReason.NotEnoughFood => _food,
                _ => null,
            };
            if (target == null)
                return;
            target.AddToClassList("resource-bar__value--warn");
            target.schedule.Execute(() =>
            {
                // Food keeps its warn styling while genuinely capped.
                if (target != _food || !_lastFoodWarn)
                    target.RemoveFromClassList("resource-bar__value--warn");
            }).StartingIn(700);
        }

        public void Tick(GameState state, byte player)
        {
            ref var p = ref state.Players[player];

            if (p.Gold != _lastGold)
            {
                _lastGold = p.Gold;
                _gold.text = _lastGold.ToString();
            }
            if (p.Lumber != _lastLumber)
            {
                _lastLumber = p.Lumber;
                _lumber.text = _lastLumber.ToString();
            }
            if (p.Oil != _lastOil)
            {
                _lastOil = p.Oil;
                _oil.text = _lastOil.ToString();
            }

            if (p.FoodUsed != _lastFoodUsed || p.FoodMax != _lastFoodMax)
            {
                _lastFoodUsed = p.FoodUsed;
                _lastFoodMax = p.FoodMax;
                _food.text = _lastFoodUsed + "/" + _lastFoodMax;

                bool warn = _lastFoodUsed >= _lastFoodMax;
                if (warn != _lastFoodWarn)
                {
                    _lastFoodWarn = warn;
                    _food.EnableInClassList("resource-bar__value--warn", warn);
                }
            }
        }
    }
}
