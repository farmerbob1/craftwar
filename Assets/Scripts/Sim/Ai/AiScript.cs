namespace Craftwar.Sim.Ai
{
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
    /// One step of the linear personality script. Unlock entries are
    /// cumulative — each occurrence of a role across phases 0..N raises that
    /// role's desired count by one, so "2nd Barracks" is simply Barracks
    /// appearing again in a later phase. Keep/Castle/GuardTower occurrences
    /// are hall/tower tier upgrades, not new sites.
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
    /// The skirmish "land attack" personality (PUD AIPL $00). The shape and
    /// the pinned numbers — worker targets 1→9→15→19→25, wave sizes
    /// 3,5,6,7,7,9…, the 500-tick post-wave sleep, the build-order backbone
    /// and the economy thresholds — are facts transcribed from the original's
    /// VLAND/COMMON AI script data. Values not pinned there interpolate
    /// between the known rows and are an explicit tuning surface: because the
    /// AI emits commands through the lockstep driver, retuning this table
    /// never invalidates a recorded replay.
    /// </summary>
    public static class AiScript
    {
        static readonly AiUnit[] NoUnits = System.Array.Empty<AiUnit>();
        static readonly AiUpgrade[] NoUpgrades = System.Array.Empty<AiUpgrade>();

        public static readonly AiPhase[] LandAttack =
        {
            new AiPhase
            {
                WorkerTarget = 9,
                WaveSize = 3,
                Unlock = new[] { AiUnit.Hall, AiUnit.LumberMill, AiUnit.Barracks },
                ResearchGoals = NoUpgrades,
                Army = new[] { new AiWant(AiUnit.Soldier, 3) },
            },
            new AiPhase
            {
                WorkerTarget = 9,
                WaveSize = 5,
                Unlock = new[] { AiUnit.Blacksmith },
                ResearchGoals = new[] { AiUpgrade.Weapon1, AiUpgrade.Armor1 },
                Army = new[] { new AiWant(AiUnit.Soldier, 4), new AiWant(AiUnit.Archer, 2) },
            },
            new AiPhase
            {
                WorkerTarget = 12,
                WaveSize = 6,
                Unlock = new[] { AiUnit.Barracks },
                ResearchGoals = new[] { AiUpgrade.Weapon2, AiUpgrade.Armor2, AiUpgrade.Missile1 },
                Army = new[] { new AiWant(AiUnit.Soldier, 6), new AiWant(AiUnit.Archer, 3) },
            },
            new AiPhase
            {
                WorkerTarget = 15,
                WaveSize = 7,
                Unlock = NoUnits,
                ResearchGoals = new[] { AiUpgrade.Missile2 },
                Army = new[]
                {
                    new AiWant(AiUnit.Soldier, 6), new AiWant(AiUnit.Archer, 3),
                    new AiWant(AiUnit.Siege, 1),
                },
            },
            new AiPhase
            {
                WorkerTarget = 15,
                WaveSize = 7,
                Unlock = new[] { AiUnit.Keep },
                ResearchGoals = NoUpgrades,
                Army = new[]
                {
                    new AiWant(AiUnit.Soldier, 6), new AiWant(AiUnit.Archer, 3),
                    new AiWant(AiUnit.Siege, 1),
                },
            },
            new AiPhase
            {
                WorkerTarget = 15,
                WaveSize = 9,
                Unlock = new[] { AiUnit.CavalryHall },
                ResearchGoals = new[] { AiUpgrade.RangedUnlock },
                Army = new[]
                {
                    new AiWant(AiUnit.Soldier, 2), new AiWant(AiUnit.Archer, 4),
                    new AiWant(AiUnit.Cavalry, 8), new AiWant(AiUnit.Siege, 2),
                },
            },
            new AiPhase
            {
                WorkerTarget = 19,
                WaveSize = 9,
                Unlock = new[] { AiUnit.ScoutTower, AiUnit.GuardTower, AiUnit.Castle },
                ResearchGoals = NoUpgrades,
                Army = new[]
                {
                    new AiWant(AiUnit.Soldier, 2), new AiWant(AiUnit.Archer, 4),
                    new AiWant(AiUnit.Cavalry, 8), new AiWant(AiUnit.Siege, 2),
                },
            },
            new AiPhase
            {
                WorkerTarget = 19,
                WaveSize = 9,
                Unlock = new[] { AiUnit.Church },
                ResearchGoals = new[] { AiUpgrade.CavalryUnlock },
                Army = new[]
                {
                    new AiWant(AiUnit.Soldier, 2), new AiWant(AiUnit.Archer, 4),
                    new AiWant(AiUnit.Cavalry, 8), new AiWant(AiUnit.Siege, 2),
                },
            },
            new AiPhase
            {
                WorkerTarget = 22,
                WaveSize = 9,
                Unlock = new[] { AiUnit.ScoutTower, AiUnit.GuardTower, AiUnit.MageHall },
                ResearchGoals = NoUpgrades,
                Army = new[]
                {
                    new AiWant(AiUnit.Soldier, 4), new AiWant(AiUnit.Archer, 4),
                    new AiWant(AiUnit.Cavalry, 8), new AiWant(AiUnit.Siege, 2),
                },
            },
        };

        /// <summary>Repeats forever once the script runs out.</summary>
        public static readonly AiPhase Endgame = new AiPhase
        {
            WorkerTarget = 25,
            WaveSize = 11,
            Unlock = NoUnits,
            ResearchGoals = NoUpgrades,
            Army = new[]
            {
                new AiWant(AiUnit.Soldier, 4), new AiWant(AiUnit.Archer, 4),
                new AiWant(AiUnit.Cavalry, 8), new AiWant(AiUnit.Siege, 2),
            },
        };

        public static AiPhase Phase(int index) =>
            index < LandAttack.Length ? LandAttack[index] : Endgame;

        // --- Economy thresholds (AI.H facts) ---
        public const int MinGold = 500;
        public const int LowGold = 1000;
        public const int LowTree = 500;
        public const int PlentyTree = 2000;

        // --- Emergency rules (PEON.C / STRAT.C facts) ---
        /// <summary>Below both: build/train nothing except the hall.</summary>
        public const int RebuildOnlyGold = 200;
        public const int RebuildOnlyLumber = 100;
        /// <summary>All-in on the strongest enemy when the base is nearly gone.</summary>
        public const int SuicideBuildingCount = 3;

        public const int PostWaveSleepTicks = 500;

        /// <summary>
        /// Liveness rule (ours, not the original's): when the economy is dead
        /// — no gold mine left on the map — waves can never grow to the
        /// muster size, so after this long without launching one, attack with
        /// whatever exists. Guarantees a dry map still resolves to a victor.
        /// </summary>
        public const int DryWaveTicks = 1500;
    }
}
