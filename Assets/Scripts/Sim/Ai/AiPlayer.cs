using System.Collections.Generic;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// One computer opponent. Runs OUTSIDE GameSim.Advance as a command-emitting
    /// player: Think() reads sim state and fills the caller's buffer with
    /// GameCommands stamped with this slot, which the app submits through the
    /// lockstep driver exactly like the human's input. Replays therefore record
    /// the AI's actual commands, and playback never constructs an AiPlayer.
    ///
    /// Everything is a pure integer function of (slot, sim state at think
    /// ticks): ordered unit scans, squared distances, no randomness. It must
    /// never touch GameState.Rng — mutating hashed sim state from outside
    /// Advance would break replay verification.
    /// </summary>
    public sealed partial class AiPlayer
    {
        /// <summary>Think cadence in ticks, now set by the difficulty tier
        /// (AiTierTable: Dumb 50 / Normal 25 / Smart 18 / God 12). Normal keeps
        /// the M9 value. Tick-gated, so frame pacing can never affect behavior.</summary>
        public int ThinkPeriodTicks => _tier.ThinkPeriodTicks;

        /// <summary>Per-slot phase offset — spreads up to 8 AI scans across
        /// distinct ticks.</summary>
        public const int SlotStaggerTicks = 7;

        /// <summary>Commands per think, so a backlog never bursts.</summary>
        public const int MaxCommandsPerThink = 4;

        /// <summary>Drop (and cancel) a build order that has not landed in 30 s.</summary>
        public const int PendingBuildTimeoutTicks = 1500;

        /// <summary>Consecutive site-search failures before the search radius
        /// doubles; twice this before the blocking script entry is skipped.</summary>
        public const int StallRelaxThinks = 80;

        public byte Slot { get; }
        public AiBehavior Behavior { get; }

        /// <summary>The desired-state script this opponent plays. Read-only; a
        /// strategy instance may be shared across AIs.</summary>
        public AiStrategy Strategy { get; }

        /// <summary>Difficulty tier. Its SKILL params (cadence + competence
        /// toggles) drive this out-of-sim executor; its HANDICAP knobs are the
        /// app's to bake into hashed PlayerState — the AI never applies them.</summary>
        public AiTier Tier { get; }
        readonly AiTierParams _tier;

        struct PendingBuild
        {
            public uint BuilderPacked;
            public ushort TypeId;
            public ushort X, Y;
            public int IssuedTick;
        }

        int _phase;
        int _sleepUntilTick;
        int _stallThinks;
        int _maxBuildingsSeen;
        int _nextAllInTick;
        int _lastWaveTick;
        readonly List<PendingBuild> _pending = new List<PendingBuild>();
        readonly List<int> _skippedGoals = new List<int>();
        readonly List<int> _blacklistedSites = new List<int>();
        readonly List<uint> _scratchIds = new List<uint>();
        readonly List<uint> _claimed = new List<uint>(); // units tasked this think

        // Per-think working set, valid only inside Think().
        GameSim _sim;
        GameState _s;
        List<GameCommand> _out;
        int _budget;
        Race _race;
        int _anchorX, _anchorY;
        bool _emergency;

        public AiPlayer(byte slot, AiBehavior behavior, AiStrategy strategy = null,
            AiTier tier = AiTier.Normal)
        {
            Slot = slot;
            Behavior = behavior;
            Strategy = strategy ?? BuiltinAiStrategies.Default;
            Tier = tier;
            _tier = AiTierTable.For(tier);
        }

        /// <summary>Current script phase, exposed for tests and the debug overlay.</summary>
        public int Phase => _phase;

        /// <summary>Jump the script to a phase — tests and debugging only; the
        /// game never calls this.</summary>
        public void ForcePhase(int phase) => _phase = phase;

        /// <summary>
        /// Called once per sim tick, before the driver drains that tick, so
        /// commands land on exactly the tick that was observed.
        /// </summary>
        public void Think(GameSim sim, List<GameCommand> output)
        {
            if (Behavior == AiBehavior.Passive)
                return;
            var s = sim.State;
            if (s.Players[Slot].Outcome != PlayerOutcome.Playing)
                return;
            if ((s.Tick + Slot * SlotStaggerTicks) % ThinkPeriodTicks != 0)
                return;

            _sim = sim;
            _s = s;
            _out = output;
            _budget = MaxCommandsPerThink;
            _race = s.Players[Slot].Race;
            _emergency = s.Players[Slot].Gold < Strategy.RebuildOnlyGold
                && s.Players[Slot].Lumber < Strategy.RebuildOnlyLumber;
            if (!AiQueries.FindBaseAnchor(s, Slot, out _anchorX, out _anchorY))
            {
                _sim = null;
                _s = null;
                _out = null;
                return; // nothing left; victory will resolve it
            }

            _claimed.Clear();
            ReconcilePending();
            // Construction claims its builder before the harvest balancer can
            // re-task it — the original's peon dispatcher has the same bias.
            ThinkBuild();
            // Higher-tier extras, after the core build order: a second base by a
            // fresh mine, and a one-time early scout. Gated so Dumb/Normal skip them.
            if (_tier.Expansion && _budget > 0)
                ThinkExpansion();
            if (_tier.Scouting && _budget > 0)
                TryScout();
            if (_budget > 0)
                ThinkEconomy();
            if (_budget > 0)
                ThinkTrain();
            if (_budget > 0)
                ThinkResearch();
            if (_budget > 0)
                ThinkMilitary();

            _sim = null;
            _s = null;
            _out = null;
        }

        unsafe void Emit(in GameCommand cmd)
        {
            _out.Add(cmd);
            _budget--;
            // Commands land after ALL of this think's decisions, so a unit
            // ordered once must not be re-tasked by a later manager this think.
            var c = cmd; // local copy: fixed buffers need an addressable home
            for (int i = 0; i < c.SelectionCount; i++)
                _claimed.Add(c.Selection.Ids[i]);
        }

        bool IsClaimed(uint packed) => _claimed.Contains(packed);

        // ------------------------------------------------------------------
        // Effective resources: what is really spendable once the in-flight
        // Build orders (whose cost the sim deducts only on builder ARRIVAL)
        // have taken their cut. Train/Research deduct immediately and need no
        // reservation.
        // ------------------------------------------------------------------

        int EffectiveGold()
        {
            int g = _s.Players[Slot].Gold;
            for (int i = 0; i < _pending.Count; i++)
                g -= _s.Rules.Units[_pending[i].TypeId].GoldCost;
            return g;
        }

        int EffectiveLumber()
        {
            int l = _s.Players[Slot].Lumber;
            for (int i = 0; i < _pending.Count; i++)
                l -= _s.Rules.Units[_pending[i].TypeId].LumberCost;
            return l;
        }

        bool CanAfford(int gold, int lumber, int oil) =>
            EffectiveGold() >= gold && EffectiveLumber() >= lumber
            && _s.Players[Slot].Oil >= oil;
    }
}
