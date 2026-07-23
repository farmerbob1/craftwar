using System.Collections.Generic;
using System.Globalization;

namespace Craftwar.Sim.Ai
{
    public sealed class AiStrategyParseException : System.Exception
    {
        public AiStrategyParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Parses the readable strategy text format into an integer-only
    /// <see cref="AiStrategy"/>. Lives in Craftwar.Sim (no floats, no reflection
    /// over unordered collections, no wall-clock) so it is SimPurity-safe and
    /// available to the standalone test harness as well as the app.
    ///
    /// Format: line-oriented, '#' starts a comment. Directives:
    ///   strategy &lt;name&gt;
    ///   defaultTier dumb|normal|smart|god
    ///   thresholds minGold=.. lowGold=.. lowTree=.. plentyTree=..
    ///   rebuildOnly gold=.. lumber=..
    ///   suicideBuildingCount &lt;n&gt; | postWaveSleep &lt;n&gt; | dryWave &lt;n&gt;
    ///   phase   workers=.. wave=.. build=A,B research=C,D army=Role:n,Role:n
    ///   endgame workers=.. wave=.. [same keys as phase]
    /// Role/upgrade tokens are the <see cref="AiUnit"/> / <see cref="AiUpgrade"/>
    /// enum names.
    /// </summary>
    public static class AiStrategyParser
    {
        static readonly char[] Whitespace = { ' ', '\t' };

        public static AiStrategy Parse(string text)
        {
            if (text == null)
                throw new AiStrategyParseException("null strategy text");

            var s = new AiStrategy();
            var phases = new List<AiPhase>();
            bool haveEndgame = false;

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string line = StripComment(lines[li]).Trim();
                if (line.Length == 0)
                    continue;
                string[] tok = line.Split(Whitespace, System.StringSplitOptions.RemoveEmptyEntries);
                switch (tok[0])
                {
                    case "strategy":
                        s.Name = tok.Length > 1 ? tok[1] : "";
                        break;
                    case "defaultTier":
                        if (tok.Length < 2 || !AiTiers.TryParse(tok[1], out var tier))
                            throw Err(li, "defaultTier expects dumb|normal|smart|god");
                        s.DefaultTier = tier;
                        break;
                    case "thresholds":
                        ParseThresholds(tok, li, s);
                        break;
                    case "rebuildOnly":
                        ParseRebuild(tok, li, s);
                        break;
                    case "suicideBuildingCount":
                        s.SuicideBuildingCount = ReqSingle(tok, li);
                        break;
                    case "postWaveSleep":
                        s.PostWaveSleepTicks = ReqSingle(tok, li);
                        break;
                    case "dryWave":
                        s.DryWaveTicks = ReqSingle(tok, li);
                        break;
                    case "phase":
                        phases.Add(ParsePhase(tok, li));
                        break;
                    case "endgame":
                        s.Endgame = ParsePhase(tok, li);
                        haveEndgame = true;
                        break;
                    default:
                        throw Err(li, $"unknown directive '{tok[0]}'");
                }
            }

            if (phases.Count == 0)
                throw new AiStrategyParseException("strategy has no phase lines");
            if (!haveEndgame)
                throw new AiStrategyParseException("strategy has no endgame line");
            s.Phases = phases.ToArray();
            return s;
        }

        static AiPhase ParsePhase(string[] tok, int li)
        {
            var p = new AiPhase
            {
                Unlock = System.Array.Empty<AiUnit>(),
                ResearchGoals = System.Array.Empty<AiUpgrade>(),
                Army = System.Array.Empty<AiWant>(),
            };
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                switch (key)
                {
                    case "workers": p.WorkerTarget = (byte)ParseInt(val, li); break;
                    case "wave": p.WaveSize = (byte)ParseInt(val, li); break;
                    case "build": p.Unlock = ParseUnitList(val, li); break;
                    case "research": p.ResearchGoals = ParseUpgradeList(val, li); break;
                    case "army": p.Army = ParseArmyList(val, li); break;
                    default: throw Err(li, $"unknown phase key '{key}'");
                }
            }
            return p;
        }

        static void ParseThresholds(string[] tok, int li, AiStrategy s)
        {
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                int v = ParseInt(val, li);
                switch (key)
                {
                    case "minGold": s.MinGold = v; break;
                    case "lowGold": s.LowGold = v; break;
                    case "lowTree": s.LowTree = v; break;
                    case "plentyTree": s.PlentyTree = v; break;
                    default: throw Err(li, $"unknown threshold '{key}'");
                }
            }
        }

        static void ParseRebuild(string[] tok, int li, AiStrategy s)
        {
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                int v = ParseInt(val, li);
                switch (key)
                {
                    case "gold": s.RebuildOnlyGold = v; break;
                    case "lumber": s.RebuildOnlyLumber = v; break;
                    default: throw Err(li, $"unknown rebuildOnly key '{key}'");
                }
            }
        }

        static AiUnit[] ParseUnitList(string val, int li)
        {
            string[] parts = val.Split(',');
            var list = new List<AiUnit>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length == 0)
                    continue;
                if (!System.Enum.TryParse(name, out AiUnit u)
                    || !System.Enum.IsDefined(typeof(AiUnit), u))
                    throw Err(li, $"unknown unit role '{name}'");
                list.Add(u);
            }
            return list.ToArray();
        }

        static AiUpgrade[] ParseUpgradeList(string val, int li)
        {
            string[] parts = val.Split(',');
            var list = new List<AiUpgrade>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length == 0)
                    continue;
                if (!System.Enum.TryParse(name, out AiUpgrade u)
                    || !System.Enum.IsDefined(typeof(AiUpgrade), u))
                    throw Err(li, $"unknown upgrade role '{name}'");
                list.Add(u);
            }
            return list.ToArray();
        }

        static AiWant[] ParseArmyList(string val, int li)
        {
            string[] parts = val.Split(',');
            var list = new List<AiWant>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string pair = parts[i].Trim();
                if (pair.Length == 0)
                    continue;
                int colon = pair.IndexOf(':');
                if (colon <= 0 || colon == pair.Length - 1)
                    throw Err(li, $"army entry '{pair}' must be Role:Count");
                string name = pair.Substring(0, colon).Trim();
                if (!System.Enum.TryParse(name, out AiUnit u)
                    || !System.Enum.IsDefined(typeof(AiUnit), u))
                    throw Err(li, $"unknown army role '{name}'");
                int count = ParseInt(pair.Substring(colon + 1).Trim(), li);
                list.Add(new AiWant(u, (byte)count));
            }
            return list.ToArray();
        }

        static int ReqSingle(string[] tok, int li)
        {
            if (tok.Length < 2)
                throw Err(li, $"'{tok[0]}' expects a value");
            return ParseInt(tok[1], li);
        }

        static void SplitKeyValue(string token, int li, out string key, out string val)
        {
            int eq = token.IndexOf('=');
            if (eq <= 0 || eq == token.Length - 1)
                throw Err(li, $"expected key=value, got '{token}'");
            key = token.Substring(0, eq);
            val = token.Substring(eq + 1);
        }

        static int ParseInt(string s, int li)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            throw Err(li, $"expected an integer, got '{s}'");
        }

        static string StripComment(string line)
        {
            int hash = line.IndexOf('#');
            return hash < 0 ? line : line.Substring(0, hash);
        }

        static AiStrategyParseException Err(int lineIndex, string message) =>
            new AiStrategyParseException($"line {lineIndex + 1}: {message}");
    }
}
