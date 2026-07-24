using System.Collections.Generic;
using System.Globalization;
using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    public sealed class AiProfileParseException : System.Exception
    {
        public AiProfileParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Parses the readable .ai profile text into an integer-only
    /// <see cref="AiProfile"/>. Lives in Craftwar.Sim (no floats, no reflection over
    /// unordered collections, no wall-clock) so it is SimPurity-safe and available
    /// to the standalone test harness and the app alike. All numeric knobs are
    /// authored as integers; weights and curve parameters are integer PERCENTS
    /// (100 = 1.0) converted to Q16.16 here, so no float ever appears in a profile.
    ///
    /// Directives (line-oriented, '#' starts a comment):
    ///   profile &lt;name&gt;
    ///   defaultTier dumb|normal|smart|god
    ///   personality aggression=.. greed=.. defensiveness=.. expansiveness=..
    ///   economy workerTarget=.. minGold=.. lowGold=.. lowTree=.. plentyTree=..
    ///   rebuildOnly gold=.. lumber=..
    ///   military waveSize=.. suicideBuildingCount=.. postWaveSleep=.. dryWave=..
    ///   build   Role,Role,...            (cumulative build order)
    ///   army    Role:Count,Role:Count    (standing-army target)
    ///   research Upgrade,Upgrade,...
    ///   weights build=100 worker=.. army=.. research=.. expand=.. wave=.. defend=..
    ///           harvest=.. scout=.. farm=..
    ///   curve   &lt;name&gt; &lt;kind&gt; &lt;a&gt; [b]   (percents; kind = constant|linear|quadratic|logistic|step)
    /// </summary>
    public static class AiProfileParser
    {
        static readonly char[] Whitespace = { ' ', '\t' };

        public static AiProfile Parse(string text)
        {
            if (text == null)
                throw new AiProfileParseException("null profile text");

            var p = new AiProfile();
            bool named = false;

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
                    case "profile":
                        p.Name = tok.Length > 1 ? tok[1] : "";
                        named = true;
                        break;
                    case "defaultTier":
                        if (tok.Length < 2 || !AiTiers.TryParse(tok[1], out var tier))
                            throw Err(li, "defaultTier expects dumb|normal|smart|god");
                        p.DefaultTier = tier;
                        break;
                    case "personality":
                        ParsePersonality(tok, li, p);
                        break;
                    case "economy":
                        ParseEconomy(tok, li, p);
                        break;
                    case "rebuildOnly":
                        ParseRebuild(tok, li, p);
                        break;
                    case "military":
                        ParseMilitary(tok, li, p);
                        break;
                    case "build":
                        p.BuildOrder = ParseUnitList(Rest(tok), li);
                        break;
                    case "army":
                        p.Army = ParseArmyList(Rest(tok), li);
                        break;
                    case "research":
                        p.Research = ParseUpgradeList(Rest(tok), li);
                        break;
                    case "weights":
                        ParseWeights(tok, li, p);
                        break;
                    case "curve":
                        ParseCurve(tok, li, p);
                        break;
                    default:
                        throw Err(li, $"unknown directive '{tok[0]}'");
                }
            }

            if (!named)
                throw new AiProfileParseException("profile has no 'profile <name>' line");
            return p;
        }

        // 'build'/'army'/'research' take a single comma-list token but tolerate
        // spaces after commas, so re-join the remaining tokens.
        static string Rest(string[] tok)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < tok.Length; i++)
                sb.Append(tok[i]);
            return sb.ToString();
        }

        static void ParsePersonality(string[] tok, int li, AiProfile p)
        {
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                byte v = (byte)AiMath.Clamp(ParseInt(val, li), 0, 100);
                switch (key)
                {
                    case "aggression": p.Aggression = v; break;
                    case "greed": p.Greed = v; break;
                    case "defensiveness": p.Defensiveness = v; break;
                    case "expansiveness": p.Expansiveness = v; break;
                    default: throw Err(li, $"unknown personality dial '{key}'");
                }
            }
        }

        static void ParseEconomy(string[] tok, int li, AiProfile p)
        {
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                int v = ParseInt(val, li);
                switch (key)
                {
                    case "workerTarget": p.WorkerTarget = v; break;
                    case "minGold": p.MinGold = v; break;
                    case "lowGold": p.LowGold = v; break;
                    case "lowTree": p.LowTree = v; break;
                    case "plentyTree": p.PlentyTree = v; break;
                    default: throw Err(li, $"unknown economy key '{key}'");
                }
            }
        }

        static void ParseRebuild(string[] tok, int li, AiProfile p)
        {
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                int v = ParseInt(val, li);
                switch (key)
                {
                    case "gold": p.RebuildOnlyGold = v; break;
                    case "lumber": p.RebuildOnlyLumber = v; break;
                    default: throw Err(li, $"unknown rebuildOnly key '{key}'");
                }
            }
        }

        static void ParseMilitary(string[] tok, int li, AiProfile p)
        {
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                int v = ParseInt(val, li);
                switch (key)
                {
                    case "waveSize": p.WaveSize = v; break;
                    case "suicideBuildingCount": p.SuicideBuildingCount = v; break;
                    case "postWaveSleep": p.PostWaveSleepTicks = v; break;
                    case "dryWave": p.DryWaveTicks = v; break;
                    default: throw Err(li, $"unknown military key '{key}'");
                }
            }
        }

        static void ParseWeights(string[] tok, int li, AiProfile p)
        {
            for (int i = 1; i < tok.Length; i++)
            {
                SplitKeyValue(tok[i], li, out string key, out string val);
                int q = Pct(ParseInt(val, li));
                switch (key)
                {
                    case "farm": p.WeightFarm = q; break;
                    case "build": p.WeightBuild = q; break;
                    case "worker": p.WeightWorker = q; break;
                    case "army": p.WeightArmy = q; break;
                    case "research": p.WeightResearch = q; break;
                    case "expand": p.WeightExpand = q; break;
                    case "wave": p.WeightWave = q; break;
                    case "defend": p.WeightDefend = q; break;
                    case "harvest": p.WeightHarvest = q; break;
                    case "scout": p.WeightScout = q; break;
                    default: throw Err(li, $"unknown weight '{key}'");
                }
            }
        }

        static void ParseCurve(string[] tok, int li, AiProfile p)
        {
            if (tok.Length < 4)
                throw Err(li, "curve expects: curve <name> <kind> <a> [b]");
            string name = tok[1];
            var curve = BuildCurve(tok, li);
            switch (name)
            {
                case "affordability": p.Affordability = curve; break;
                case "threatSafety": p.ThreatSafety = curve; break;
                case "waveReadiness": p.WaveReadiness = curve; break;
                case "relativeStrength": p.RelativeStrength = curve; break;
                case "mineDepletion": p.MineDepletion = curve; break;
                case "foodSafety": p.FoodSafety = curve; break;
                default: throw Err(li, $"unknown curve '{name}'");
            }
        }

        static ResponseCurve BuildCurve(string[] tok, int li)
        {
            string kind = tok[2];
            int a = Pct(ParseInt(tok[3], li));
            int b = tok.Length > 4 ? Pct(ParseInt(tok[4], li)) : 0;
            switch (kind)
            {
                case "constant": return new ResponseCurve(CurveKind.Constant, 0, a);
                case "step": return new ResponseCurve(CurveKind.Step, 0, a);
                case "linear": return new ResponseCurve(CurveKind.Linear, a, b);
                case "quadratic": return new ResponseCurve(CurveKind.Quadratic, a, b);
                case "logistic": return new ResponseCurve(CurveKind.Logistic, a, b);
                default: throw Err(li, $"unknown curve kind '{kind}'");
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
                    throw Err(li, $"unknown upgrade '{name}'");
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

        static int Pct(int percent) => percent * AiMath.One / 100;

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
            if (int.TryParse(s, NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int v))
                return v;
            throw Err(li, $"expected an integer, got '{s}'");
        }

        static string StripComment(string line)
        {
            int hash = line.IndexOf('#');
            return hash < 0 ? line : line.Substring(0, hash);
        }

        static AiProfileParseException Err(int lineIndex, string message) =>
            new AiProfileParseException($"line {lineIndex + 1}: {message}");
    }
}
