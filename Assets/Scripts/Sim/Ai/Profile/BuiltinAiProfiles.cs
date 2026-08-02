namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// The built-in AI profile, embedded as text and parsed once. The Sim never
    /// depends on a file at runtime — but a byte-identical copy ships in
    /// StreamingAssets/Ai/land-attack.ai so modders have a worked example to fork,
    /// and the app loads player profiles from persistentDataPath with the same
    /// parser (see AiProfileLibrary). A drift test guards the two copies against
    /// divergence.
    /// </summary>
    public static class BuiltinAiProfiles
    {
        public const string LandAttackName = "land-attack";

        /// <summary>A built-in profile by name, or null if none matches.</summary>
        public static AiProfile Get(string name) =>
            name == LandAttackName ? Default : null;

        /// <summary>
        /// The default land-attack personality, ported from the old land-attack
        /// phase-script: the same build order, army composition, research path and
        /// economy thresholds, now expressed as utility priorities + curves.
        /// </summary>
        public const string LandAttackText = @"# Craftwar default AI profile.
# A balanced land-army opponent. Every number is an integer; weights and curve
# parameters are percents (100 = 1.0). Copy this file, retune, drop it in
# <persistentDataPath>/Ai/ and the app hot-loads it — no engine recompile.
profile land-attack
defaultTier normal
personality aggression=50 greed=50 defensiveness=50 expansiveness=40

economy workerTarget=16 minGold=500 lowGold=1000 lowTree=500 plentyTree=2000
rebuildOnly gold=200 lumber=100
military waveSize=8 suicideBuildingCount=3 postWaveSleep=500 dryWave=1500

# Cumulative build order (each entry raises that role's target by one).
# Naval buildings sit at the end: on a landlocked spawn the site search just
# stalls and skips them after a while, at no cost to the land tech above.
build Hall,LumberMill,Barracks,Blacksmith,Barracks,Keep,CavalryHall,ScoutTower,GuardTower,Castle,Church,MageHall,AirHall,Shipyard,Refinery,Foundry
army Soldier:4,Archer:4,Cavalry:8,Siege:2,Caster:2,AirUnit:3,Warship:3,Battleship:1,Tanker:1
research Weapon1,Armor1,Weapon2,Armor2,Missile1,Missile2,RangedUnlock,CavalryUnlock,EliteRanged1,EliteRanged2,EliteRanged3,NavalWeapon1,NavalWeapon2,NavalArmor1,NavalArmor2

weights farm=300 build=100 worker=100 army=100 research=50 expand=60 wave=100 defend=400 harvest=200 scout=12
curve affordability logistic 300 40
curve threatSafety linear -100 100
curve waveReadiness logistic 300 60
curve relativeStrength logistic 300 62
curve mineDepletion linear -100 100
curve foodSafety step 100
";

        static AiProfile _default;

        /// <summary>The shared, read-only default profile.</summary>
        public static AiProfile Default => _default ??= AiProfileParser.Parse(LandAttackText);
    }
}
