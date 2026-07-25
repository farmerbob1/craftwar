namespace Craftwar.View
{
    /// <summary>
    /// Which 5-facing frame blocks of a sprite bank belong to which animation.
    ///
    /// WC2 sprite banks store frames in blocks of five facings (N, NE, E, SE, S;
    /// the west side is the east side mirrored), but the blocks are NOT laid out
    /// walk-then-attack-then-die in every bank. The demolition squad, the goblin
    /// sappers and the skeleton interleave theirs — the dwarves' walk lives in
    /// blocks 0, 2, 5, 8, 11 with the death animation woven between. Guessing a
    /// layout from the block count is what made catapults play their firing
    /// animation while rolling along; see <see cref="UnitAnimTable"/> for where
    /// the real indices come from.
    ///
    /// Empty (null) arrays mean the bank has no such animation: a catapult has
    /// no death frames, a submarine has neither death nor attack.
    /// </summary>
    public readonly struct AnimLayout
    {
        public readonly byte[] Walk;
        public readonly byte[] Attack;
        public readonly byte[] Die;

        public AnimLayout(byte[] walk, byte[] attack, byte[] die)
        {
            Walk = walk;
            Attack = attack;
            Die = die;
        }

        /// <summary>False for a bank with no layout data (buildings, unknown art).</summary>
        public bool IsValid => Walk != null && Walk.Length > 0;

        public bool HasAttack => Attack != null && Attack.Length > 0;
        public bool HasDeath => Die != null && Die.Length > 0;

        /// <summary>Block for step <paramref name="i"/> of the walk cycle, wrapped.</summary>
        public int WalkBlock(int i) => Walk[Mod(i, Walk.Length)];

        /// <summary>Block for step <paramref name="i"/> of an attack, wrapped.</summary>
        public int AttackBlock(int i) => Attack[Mod(i, Attack.Length)];

        /// <summary>Block for step <paramref name="i"/> of the death, clamped —
        /// a death animation plays once and holds its last frame.</summary>
        public int DieBlock(int i) =>
            Die[i < 0 ? 0 : i >= Die.Length ? Die.Length - 1 : i];

        public int DieSteps => Die?.Length ?? 0;

        static int Mod(int v, int n) => ((v % n) + n) % n;
    }
}
