using System;
using System.Text;
using Craftwar.Sim;

namespace Craftwar.View
{
    /// <summary>
    /// Display names and 2-letter initials for sim enums, built once at first
    /// use. Placeholder art until the WC2 icons land at M8 — the card renders
    /// initials in a colored box, so every button is readable without assets.
    /// Absorbs the old HudController.NameOf.
    /// </summary>
    public static class UnitNames
    {
        static string[] _units, _upgrades;
        static string[] _unitInitials, _upgradeInitials;

        public static string Of(UnitTypeId id)
        {
            EnsureUnits();
            int i = (int)id;
            return (uint)i < (uint)_units.Length && _units[i] != null ? _units[i] : id.ToString();
        }

        public static string Of(UpgradeId id)
        {
            EnsureUpgrades();
            int i = (int)id;
            return (uint)i < (uint)_upgrades.Length && _upgrades[i] != null ? _upgrades[i] : id.ToString();
        }

        public static string InitialsOf(UnitTypeId id)
        {
            EnsureUnits();
            int i = (int)id;
            return (uint)i < (uint)_unitInitials.Length && _unitInitials[i] != null
                ? _unitInitials[i] : "?";
        }

        public static string InitialsOf(UpgradeId id)
        {
            EnsureUpgrades();
            int i = (int)id;
            return (uint)i < (uint)_upgradeInitials.Length && _upgradeInitials[i] != null
                ? _upgradeInitials[i] : "?";
        }

        static void EnsureUnits()
        {
            if (_units != null)
                return;
            Build<UnitTypeId>(out _units, out _unitInitials);
        }

        static void EnsureUpgrades()
        {
            if (_upgrades != null)
                return;
            Build<UpgradeId>(out _upgrades, out _upgradeInitials);
        }

        static void Build<T>(out string[] names, out string[] initials) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int max = 0;
            foreach (var v in values)
            {
                int i = Convert.ToInt32(v);
                if (i > max)
                    max = i;
            }
            names = new string[max + 1];
            initials = new string[max + 1];
            foreach (var v in values)
            {
                int i = Convert.ToInt32(v);
                if (names[i] != null)
                    continue; // enum aliases: first name wins
                string spaced = Spaced(v.ToString());
                names[i] = spaced;
                initials[i] = Initials(spaced);
            }
        }

        /// <summary>"ElvenLumberMill" -> "Elven Lumber Mill".</summary>
        static string Spaced(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>First letters of the first two words ("Elven Lumber Mill" -> "EL").</summary>
        static string Initials(string spaced)
        {
            char a = '\0', b = '\0';
            bool atWordStart = true;
            for (int i = 0; i < spaced.Length; i++)
            {
                char c = spaced[i];
                if (c == ' ')
                {
                    atWordStart = true;
                    continue;
                }
                if (atWordStart)
                {
                    if (a == '\0')
                        a = c;
                    else if (b == '\0')
                    {
                        b = c;
                        break;
                    }
                    atWordStart = false;
                }
            }
            if (a == '\0')
                return "?";
            // Single-word names keep their second letter ("Farm" -> "Fa").
            if (b == '\0')
                b = spaced.Length > 1 ? char.ToLowerInvariant(spaced[1]) : ' ';
            return b == ' ' ? a.ToString() : string.Concat(a.ToString(), b.ToString());
        }
    }
}
