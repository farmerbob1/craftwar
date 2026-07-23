namespace Craftwar.Sim.Ai
{
    /// <summary>A standing-army target the AI trains toward while mustering.</summary>
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
    /// One step of the linear personality script. Unlock entries are cumulative —
    /// each occurrence of a role across phases 0..N raises that role's desired
    /// count by one, so "2nd Barracks" is simply Barracks appearing again in a
    /// later phase. Keep/Castle/GuardTower occurrences are hall/tower tier
    /// upgrades, not new sites.
    /// </summary>
    public struct AiPhase
    {
        public byte WorkerTarget;
        public byte WaveSize;
        public AiUnit[] Unlock;
        public AiUpgrade[] ResearchGoals; // cumulative, like Unlock
        public AiWant[] Army;             // standing-army targets while mustering
    }

    /// <summary>
    /// A computer opponent's strategy as data — the "desired-state" layer the
    /// executor (AiPlayer.Economy/Build/Military) reads and turns into commands.
    /// This replaces the old hardcoded <c>AiScript</c> static tables. It is
    /// integer-only so it lives inside Craftwar.Sim and is covered by
    /// SimPurityTests, and it is authored as readable text (see
    /// <see cref="AiStrategyParser"/>) then reduced here to arrays.
    ///
    /// The original WC2 "ICE" engine had exactly this shape: a tiny desired-state
    /// script (build order + unit wants + wave pacing + conditions) driving
    /// autonomous native engines. Because the AI emits recorded commands through
    /// the lockstep driver, retuning a strategy never invalidates a replay; the
    /// canonical binary form (<see cref="Write"/>/<see cref="ToBytes"/>) yields a
    /// stable <see cref="Hash"/> used for match provenance.
    /// </summary>
    public sealed class AiStrategy
    {
        public string Name = "";
        public AiTier DefaultTier = AiTier.Normal;

        public AiPhase[] Phases = System.Array.Empty<AiPhase>();
        public AiPhase Endgame;

        // --- Economy thresholds (AI.H facts) ---
        public int MinGold = 500;
        public int LowGold = 1000;
        public int LowTree = 500;
        public int PlentyTree = 2000;

        // --- Emergency rules (PEON.C / STRAT.C facts) ---
        /// <summary>Below both: build/train nothing except the hall.</summary>
        public int RebuildOnlyGold = 200;
        public int RebuildOnlyLumber = 100;
        /// <summary>All-in on the strongest enemy when the base is nearly gone.</summary>
        public int SuicideBuildingCount = 3;

        public int PostWaveSleepTicks = 500;

        /// <summary>
        /// Liveness rule (ours, not the original's): when the economy is dead — no
        /// gold mine left on the map — waves can never grow to the muster size, so
        /// after this long without launching one, attack with whatever exists.
        /// Guarantees a dry map still resolves to a victor.
        /// </summary>
        public int DryWaveTicks = 1500;

        /// <summary>Phase N of the script, or the endgame phase past the end.</summary>
        public AiPhase Phase(int index) => index < Phases.Length ? Phases[index] : Endgame;

        // ------------------------------------------------------------------
        // Canonical binary form. Little-endian via the shared ByteWriter, so the
        // hash is identical on every platform. This is what a replay records and
        // what M10 would ship on the wire.
        // ------------------------------------------------------------------

        public void Write(ref ByteWriter w)
        {
            WriteString(ref w, Name);
            w.WriteByte((byte)DefaultTier);
            w.WriteInt(MinGold);
            w.WriteInt(LowGold);
            w.WriteInt(LowTree);
            w.WriteInt(PlentyTree);
            w.WriteInt(RebuildOnlyGold);
            w.WriteInt(RebuildOnlyLumber);
            w.WriteInt(SuicideBuildingCount);
            w.WriteInt(PostWaveSleepTicks);
            w.WriteInt(DryWaveTicks);
            w.WriteByte((byte)Phases.Length);
            for (int i = 0; i < Phases.Length; i++)
                WritePhase(ref w, Phases[i]);
            WritePhase(ref w, Endgame);
        }

        public static AiStrategy Read(ref ByteReader r)
        {
            var s = new AiStrategy
            {
                Name = ReadString(ref r),
                DefaultTier = (AiTier)r.ReadByte(),
                MinGold = r.ReadInt(),
                LowGold = r.ReadInt(),
                LowTree = r.ReadInt(),
                PlentyTree = r.ReadInt(),
                RebuildOnlyGold = r.ReadInt(),
                RebuildOnlyLumber = r.ReadInt(),
                SuicideBuildingCount = r.ReadInt(),
                PostWaveSleepTicks = r.ReadInt(),
                DryWaveTicks = r.ReadInt(),
            };
            int n = r.ReadByte();
            s.Phases = new AiPhase[n];
            for (int i = 0; i < n; i++)
                s.Phases[i] = ReadPhase(ref r);
            s.Endgame = ReadPhase(ref r);
            return s;
        }

        public byte[] ToBytes()
        {
            var buffer = new byte[4096];
            var w = new ByteWriter(buffer);
            Write(ref w);
            var result = new byte[w.Position];
            System.Array.Copy(buffer, result, w.Position);
            return result;
        }

        public static AiStrategy FromBytes(byte[] data)
        {
            var r = new ByteReader(data);
            return Read(ref r);
        }

        /// <summary>FNV-1a over the canonical binary form. Match provenance only —
        /// two edits that produce the same bytes are the same strategy.</summary>
        public uint Hash()
        {
            var bytes = ToBytes();
            var h = StateHash.Begin();
            for (int i = 0; i < bytes.Length; i++)
                h.Add(bytes[i]);
            return h.Value;
        }

        static void WritePhase(ref ByteWriter w, in AiPhase p)
        {
            w.WriteByte(p.WorkerTarget);
            w.WriteByte(p.WaveSize);
            var unlock = p.Unlock ?? System.Array.Empty<AiUnit>();
            w.WriteByte((byte)unlock.Length);
            for (int i = 0; i < unlock.Length; i++)
                w.WriteByte((byte)unlock[i]);
            var research = p.ResearchGoals ?? System.Array.Empty<AiUpgrade>();
            w.WriteByte((byte)research.Length);
            for (int i = 0; i < research.Length; i++)
                w.WriteByte((byte)research[i]);
            var army = p.Army ?? System.Array.Empty<AiWant>();
            w.WriteByte((byte)army.Length);
            for (int i = 0; i < army.Length; i++)
            {
                w.WriteByte((byte)army[i].Unit);
                w.WriteByte(army[i].Count);
            }
        }

        static AiPhase ReadPhase(ref ByteReader r)
        {
            var p = new AiPhase
            {
                WorkerTarget = r.ReadByte(),
                WaveSize = r.ReadByte(),
            };
            int nu = r.ReadByte();
            p.Unlock = new AiUnit[nu];
            for (int i = 0; i < nu; i++)
                p.Unlock[i] = (AiUnit)r.ReadByte();
            int nr = r.ReadByte();
            p.ResearchGoals = new AiUpgrade[nr];
            for (int i = 0; i < nr; i++)
                p.ResearchGoals[i] = (AiUpgrade)r.ReadByte();
            int na = r.ReadByte();
            p.Army = new AiWant[na];
            for (int i = 0; i < na; i++)
                p.Army[i] = new AiWant((AiUnit)r.ReadByte(), r.ReadByte());
            return p;
        }

        static void WriteString(ref ByteWriter w, string s)
        {
            s ??= "";
            int len = s.Length < 255 ? s.Length : 255;
            w.WriteByte((byte)len);
            for (int i = 0; i < len; i++)
                w.WriteByte((byte)s[i]); // strategy names are ASCII
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
