namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// The strategies that ship with the game, embedded as text so the sim never
    /// depends on a file at runtime (StreamingAssets carries an identical copy for
    /// players to read and clone — <c>AiStrategyDriftTests</c> guards the two
    /// against each other). Player-authored strategies are discovered separately
    /// from persistentDataPath by the app and parsed with the same
    /// <see cref="AiStrategyParser"/>.
    /// </summary>
    public static class BuiltinAiStrategies
    {
        public const string LandAttackName = "land-attack";

        /// <summary>Kept byte-for-byte in sync with
        /// Assets/StreamingAssets/Ai/land-attack.ai.txt.</summary>
        public const string LandAttackText = @"# Craftwar built-in AI strategy: land attack.
# Numbers are transcribed as FACTS from the original WC2 VLAND/COMMON AI scripts
# (worker ramp, wave sizes, build order, economy thresholds). This is only the
# desired-state layer; the executor (AiPlayer) decides how to carry it out.
#
# To make your own AI: copy this file into <persistentDataPath>/Ai/, rename the
# strategy, and edit the numbers. It is picked up in the skirmish setup screen.

strategy land-attack
defaultTier normal

thresholds minGold=500 lowGold=1000 lowTree=500 plentyTree=2000
rebuildOnly gold=200 lumber=100
suicideBuildingCount 3
postWaveSleep 500
dryWave 1500

phase   workers=9  wave=3  build=Hall,LumberMill,Barracks               army=Soldier:3
phase   workers=9  wave=5  build=Blacksmith  research=Weapon1,Armor1     army=Soldier:4,Archer:2
phase   workers=12 wave=6  build=Barracks    research=Weapon2,Armor2,Missile1  army=Soldier:6,Archer:3
phase   workers=15 wave=7  research=Missile2                            army=Soldier:6,Archer:3,Siege:1
phase   workers=15 wave=7  build=Keep                                   army=Soldier:6,Archer:3,Siege:1
phase   workers=15 wave=9  build=CavalryHall  research=RangedUnlock     army=Soldier:2,Archer:4,Cavalry:8,Siege:2
phase   workers=19 wave=9  build=ScoutTower,GuardTower,Castle           army=Soldier:2,Archer:4,Cavalry:8,Siege:2
phase   workers=19 wave=9  build=Church  research=CavalryUnlock         army=Soldier:2,Archer:4,Cavalry:8,Siege:2
phase   workers=22 wave=9  build=ScoutTower,GuardTower,MageHall         army=Soldier:4,Archer:4,Cavalry:8,Siege:2
endgame workers=25 wave=11                                              army=Soldier:4,Archer:4,Cavalry:8,Siege:2
";

        /// <summary>The default land-attack strategy, parsed once. Treat as
        /// read-only — every AiPlayer without an explicit strategy shares it.</summary>
        public static readonly AiStrategy LandAttack = AiStrategyParser.Parse(LandAttackText);

        public static AiStrategy Default => LandAttack;

        /// <summary>Look up a built-in by name, or null if there is no such
        /// built-in (the app then tries player strategies).</summary>
        public static AiStrategy Get(string name) =>
            name == LandAttackName ? LandAttack : null;
    }
}
