using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    /// <summary>A standing-army target the AI trains toward.</summary>
    public struct AiWant
    {
        public AiUnit Unit;
        public byte Count;

        public AiWant(AiUnit unit, byte count)
        {
            Unit = unit;
            Count = count;
        }
    }

    /// <summary>
    /// A computer opponent as tunable data — the moddable replacement for the old
    /// phase-script <c>AiStrategy</c>. It carries a personality (weight scalars), a
    /// build/army/research priority, economy + military knobs, and the response
    /// curves the utility layer scores against. Authored as readable text (see
    /// <see cref="AiProfileParser"/>); everything is integer, so it lives inside
    /// Craftwar.Sim, is SimPurity-safe, and hashes to a stable value for match
    /// provenance and lockstep. A modder ships an .ai file — no engine recompile.
    /// </summary>
    public sealed class AiProfile
    {
        public string Name = "";
        public AiTier DefaultTier = AiTier.Normal;

        // --- Personality: 0..100 dials, folded into weights at parse time. Kept
        // for introspection / debug overlay. ---
        public byte Aggression = 50;
        public byte Greed = 50;
        public byte Defensiveness = 50;
        public byte Expansiveness = 50;

        // --- Priorities (race-neutral roles; cumulative, walked in order) ---
        public AiUnit[] BuildOrder = System.Array.Empty<AiUnit>();
        public AiWant[] Army = System.Array.Empty<AiWant>();
        public AiUpgrade[] Research = System.Array.Empty<AiUpgrade>();

        // --- Economy knobs (AI.H facts) ---
        public int WorkerTarget = 18;   // per active hall
        public int MinGold = 500;
        public int LowGold = 1000;
        public int LowTree = 500;
        public int PlentyTree = 2000;
        public int RebuildOnlyGold = 200;
        public int RebuildOnlyLumber = 100;

        // --- Military knobs ---
        public int WaveSize = 8;
        public int SuicideBuildingCount = 3;
        public int PostWaveSleepTicks = 500;
        public int DryWaveTicks = 1500;

        // --- Domain base weights (Q16.16 priority multipliers) ---
        public int WeightFarm = AiMath.FromInt(3);
        public int WeightBuild = AiMath.One;
        public int WeightWorker = AiMath.One;
        public int WeightArmy = AiMath.One;
        public int WeightResearch = AiMath.Half;
        public int WeightExpand = AiMath.Half;
        public int WeightWave = AiMath.One;
        public int WeightDefend = AiMath.FromInt(4);
        public int WeightHarvest = AiMath.FromInt(2);
        public int WeightScout = AiMath.One / 8;

        // --- Response curves the generators score against ---
        public ResponseCurve Affordability = ResponseCurve.Logistic(AiMath.FromInt(3), AiMath.Half);
        public ResponseCurve ThreatSafety = ResponseCurve.Linear(-AiMath.One, AiMath.One);
        public ResponseCurve WaveReadiness = ResponseCurve.Logistic(AiMath.FromInt(3), AiMath.Half);
        public ResponseCurve RelativeStrength =
            ResponseCurve.Logistic(AiMath.FromInt(3), AiMath.One * 62 / 100);
        public ResponseCurve MineDepletion = ResponseCurve.Linear(-AiMath.One, AiMath.One);
        public ResponseCurve FoodSafety = ResponseCurve.Step(AiMath.One); // 1 only when headroom

        // ------------------------------------------------------------------
        // Canonical binary form + FNV-1a hash, mirroring AiStrategy. Little-endian
        // via ByteWriter so the hash is identical on every platform; a replay
        // records it and M10 would ship it on the wire.
        // ------------------------------------------------------------------

        public void Write(ref ByteWriter w)
        {
            WriteString(ref w, Name);
            w.WriteByte((byte)DefaultTier);
            w.WriteByte(Aggression);
            w.WriteByte(Greed);
            w.WriteByte(Defensiveness);
            w.WriteByte(Expansiveness);

            w.WriteByte((byte)BuildOrder.Length);
            for (int i = 0; i < BuildOrder.Length; i++) w.WriteByte((byte)BuildOrder[i]);
            w.WriteByte((byte)Army.Length);
            for (int i = 0; i < Army.Length; i++)
            {
                w.WriteByte((byte)Army[i].Unit);
                w.WriteByte(Army[i].Count);
            }
            w.WriteByte((byte)Research.Length);
            for (int i = 0; i < Research.Length; i++) w.WriteByte((byte)Research[i]);

            w.WriteInt(WorkerTarget);
            w.WriteInt(MinGold);
            w.WriteInt(LowGold);
            w.WriteInt(LowTree);
            w.WriteInt(PlentyTree);
            w.WriteInt(RebuildOnlyGold);
            w.WriteInt(RebuildOnlyLumber);
            w.WriteInt(WaveSize);
            w.WriteInt(SuicideBuildingCount);
            w.WriteInt(PostWaveSleepTicks);
            w.WriteInt(DryWaveTicks);

            w.WriteInt(WeightFarm);
            w.WriteInt(WeightBuild);
            w.WriteInt(WeightWorker);
            w.WriteInt(WeightArmy);
            w.WriteInt(WeightResearch);
            w.WriteInt(WeightExpand);
            w.WriteInt(WeightWave);
            w.WriteInt(WeightDefend);
            w.WriteInt(WeightHarvest);
            w.WriteInt(WeightScout);

            WriteCurve(ref w, Affordability);
            WriteCurve(ref w, ThreatSafety);
            WriteCurve(ref w, WaveReadiness);
            WriteCurve(ref w, RelativeStrength);
            WriteCurve(ref w, MineDepletion);
            WriteCurve(ref w, FoodSafety);
        }

        public static AiProfile Read(ref ByteReader r)
        {
            var p = new AiProfile
            {
                Name = ReadString(ref r),
                DefaultTier = (AiTier)r.ReadByte(),
                Aggression = r.ReadByte(),
                Greed = r.ReadByte(),
                Defensiveness = r.ReadByte(),
                Expansiveness = r.ReadByte(),
            };
            int nb = r.ReadByte();
            p.BuildOrder = new AiUnit[nb];
            for (int i = 0; i < nb; i++) p.BuildOrder[i] = (AiUnit)r.ReadByte();
            int na = r.ReadByte();
            p.Army = new AiWant[na];
            for (int i = 0; i < na; i++) p.Army[i] = new AiWant((AiUnit)r.ReadByte(), r.ReadByte());
            int nr = r.ReadByte();
            p.Research = new AiUpgrade[nr];
            for (int i = 0; i < nr; i++) p.Research[i] = (AiUpgrade)r.ReadByte();

            p.WorkerTarget = r.ReadInt();
            p.MinGold = r.ReadInt();
            p.LowGold = r.ReadInt();
            p.LowTree = r.ReadInt();
            p.PlentyTree = r.ReadInt();
            p.RebuildOnlyGold = r.ReadInt();
            p.RebuildOnlyLumber = r.ReadInt();
            p.WaveSize = r.ReadInt();
            p.SuicideBuildingCount = r.ReadInt();
            p.PostWaveSleepTicks = r.ReadInt();
            p.DryWaveTicks = r.ReadInt();

            p.WeightFarm = r.ReadInt();
            p.WeightBuild = r.ReadInt();
            p.WeightWorker = r.ReadInt();
            p.WeightArmy = r.ReadInt();
            p.WeightResearch = r.ReadInt();
            p.WeightExpand = r.ReadInt();
            p.WeightWave = r.ReadInt();
            p.WeightDefend = r.ReadInt();
            p.WeightHarvest = r.ReadInt();
            p.WeightScout = r.ReadInt();

            p.Affordability = ReadCurve(ref r);
            p.ThreatSafety = ReadCurve(ref r);
            p.WaveReadiness = ReadCurve(ref r);
            p.RelativeStrength = ReadCurve(ref r);
            p.MineDepletion = ReadCurve(ref r);
            p.FoodSafety = ReadCurve(ref r);
            return p;
        }

        public byte[] ToBytes()
        {
            var w = new ByteWriter(4096); // hint; the writer grows if a profile outgrows it
            Write(ref w);
            return w.ToArray();
        }

        public static AiProfile FromBytes(byte[] data)
        {
            var r = new ByteReader(data);
            return Read(ref r);
        }

        /// <summary>FNV-1a over the canonical binary form. Match provenance only.</summary>
        public uint Hash()
        {
            var bytes = ToBytes();
            var h = StateHash.Begin();
            for (int i = 0; i < bytes.Length; i++)
                h.Add(bytes[i]);
            return h.Value;
        }

        static void WriteCurve(ref ByteWriter w, in ResponseCurve c)
        {
            w.WriteByte((byte)c.Kind);
            w.WriteInt(c.A);
            w.WriteInt(c.B);
        }

        static ResponseCurve ReadCurve(ref ByteReader r) =>
            new ResponseCurve((CurveKind)r.ReadByte(), r.ReadInt(), r.ReadInt());

        static void WriteString(ref ByteWriter w, string s)
        {
            s ??= "";
            int len = s.Length < 255 ? s.Length : 255;
            w.WriteByte((byte)len);
            for (int i = 0; i < len; i++)
                w.WriteByte((byte)s[i]);
        }

        static string ReadString(ref ByteReader r)
        {
            int len = r.ReadByte();
            var chars = new char[len];
            for (int i = 0; i < len; i++)
                chars[i] = (char)r.ReadByte();
            return new string(chars);
        }
    }
}
