using System.Collections.Generic;
using Craftwar.Sim.Ai.Spatial;
using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Ai
{
    /// <summary>
    /// One computer opponent. Runs OUTSIDE GameSim.Advance as a command-emitting
    /// player: Think() reads sim state and fills the caller's buffer with
    /// GameCommands stamped with this slot, which the app submits through the
    /// lockstep driver exactly like the human's input. Replays therefore record the
    /// AI's actual commands, and playback never constructs an AiPlayer.
    ///
    /// The brain is a modern utility stack: every think, per-domain generators emit
    /// scored candidate actions (weight × response curves), an arbiter executes the
    /// best that fit the command budget, and all spatial reasoning runs through
    /// integer influence maps + a connectivity gate so a placement can never wall
    /// the base in. It is a pure integer function of (slot, sim state at think
    /// ticks) — ordered scans, squared distances, no randomness — and must never
    /// touch GameState.Rng, so it stays reproducible for lockstep.
    /// </summary>
    public sealed partial class AiPlayer
    {
        public const int SlotStaggerTicks = 7;
        public const int MaxCommandsPerThink = 4;
        public const int PendingBuildTimeoutTicks = 1500;
        public const int StallRelaxThinks = 80;
        /// <summary>Influence maps are recomputed at most this often (ticks).</summary>
        public const int InfluenceRefreshTicks = 25;

        public int ThinkPeriodTicks => _tier.ThinkPeriodTicks;

        public byte Slot { get; }
        public AiBehavior Behavior { get; }

        /// <summary>The tunable personality this opponent plays. Read-only; may be
        /// shared across AIs.</summary>
        public AiProfile Profile { get; }

        public AiTier Tier { get; }
        readonly AiTierParams _tier;

        struct PendingBuild
        {
            public uint BuilderPacked;
            public ushort TypeId;
            public ushort X, Y;
            public int IssuedTick;
        }

        // Persistent across thinks.
        int _sleepUntilTick;
        int _stallThinks;
        int _maxBuildingsSeen;
        int _nextAllInTick;
        int _lastWaveTick;
        int _waveTargetX, _waveTargetY;
        bool _waveActive;
        bool _scouted;
        int _lastInfluenceTick = int.MinValue;
        readonly List<PendingBuild> _pending = new List<PendingBuild>();
        readonly List<int> _skippedGoals = new List<int>();
        readonly List<int> _blacklistedSites = new List<int>();
        readonly List<uint> _scratchIds = new List<uint>();
        readonly List<uint> _claimed = new List<uint>();
        // Workers a proposed build/expand/scout candidate has earmarked THIS think,
        // so two candidates never pick the same builder and the harvest balancer
        // leaves those workers alone — the original's "construction claims its
        // builder before the harvest balancer" bias, in the candidate model.
        readonly List<uint> _genReserved = new List<uint>();
        readonly List<UtilityAction> _candidates = new List<UtilityAction>();
        readonly AiSitePlanner _planner = new AiSitePlanner();
        readonly InfluenceField _threat = new InfluenceField();

        // Per-think working set, valid only inside Think().
        GameSim _sim;
        GameState _s;
        List<GameCommand> _out;
        int _budget;
        int _seq;
        // Resources already committed by actions executed THIS think (train /
        // research / upgrade spend immediately; builds are tracked via _pending).
        // Keeps a burst of candidates from ordering more than the treasury holds.
        int _spentGold, _spentLumber, _spentOil;
        Race _race;
        int _anchorX, _anchorY;
        bool _emergency;

        public AiPlayer(byte slot, AiBehavior behavior, AiProfile profile = null,
            AiTier tier = AiTier.Normal)
        {
            Slot = slot;
            Behavior = behavior;
            Profile = profile ?? BuiltinAiProfiles.Default;
            Tier = tier;
            _tier = AiTierTable.For(tier);
        }

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
            _emergency = s.Players[Slot].Gold < Profile.RebuildOnlyGold
                && s.Players[Slot].Lumber < Profile.RebuildOnlyLumber;
            if (!AiQueries.FindBaseAnchor(s, Slot, out _anchorX, out _anchorY))
            {
                ClearWorkingSet();
                return; // nothing left; victory will resolve it
            }

            _claimed.Clear();
            _genReserved.Clear();
            _spentGold = _spentLumber = _spentOil = 0;
            ReconcilePending();
            UpdateInfluence();

            _candidates.Clear();
            _seq = 0;
            GenerateActions();
            Util.SortByScore(_candidates);
            Arbitrate();

            ClearWorkingSet();
        }

        void ClearWorkingSet()
        {
            _sim = null;
            _s = null;
            _out = null;
        }

        /// <summary>Run every domain generator; each appends its scored candidates.
        /// Tier competences gate the "smart" ones so lower difficulties stay simple
        /// and cheap.</summary>
        void GenerateActions()
        {
            // Worker-consuming actions first, so each reserves a distinct builder
            // before the harvest balancer runs (which then leaves those workers be).
            GenBuild();
            if (_tier.Expansion)
                GenExpand();
            if (_tier.Scouting)
                GenScout();
            GenEconomy();
            GenTrain();
            GenResearch();
            GenMilitary();
        }

        /// <summary>Execute candidates best-first while the command budget lasts,
        /// skipping any whose actor units are already committed this think (so two
        /// actions never fight over the same worker or squad).</summary>
        void Arbitrate()
        {
            for (int i = 0; i < _candidates.Count && _budget > 0; i++)
            {
                var a = _candidates[i];
                if (a.Score <= 0)
                    break; // nothing with positive utility remains
                if (ActorsClaimed(in a.Command))
                    continue;
                ExecuteAction(in a);
            }
        }

        unsafe bool ActorsClaimed(in GameCommand cmd)
        {
            var c = cmd;
            for (int i = 0; i < c.SelectionCount; i++)
                if (IsClaimed(c.Selection.Ids[i]))
                    return true;
            return false;
        }

        void ExecuteAction(in UtilityAction a)
        {
            // Re-validate cost against what earlier actions THIS think already
            // committed, so a burst of candidates never orders past the treasury
            // (the sim would bounce it as a wasted "not enough gold" deny).
            bool spends = CostOf(in a, out int cg, out int cl, out int co);
            if (spends && !CanAfford(cg, cl, co))
                return;

            // The all-in commits the whole army in as many 18-unit chunks as it
            // takes, so it is emitted specially rather than as a single command.
            if (a.Kind == AiActionKind.AllIn)
            {
                LaunchAllIn(a.SiteX, a.SiteY);
                return;
            }

            Emit(a.Command);
            if (a.PendingType != 0)
                // Builds are tracked in the ledger (cost deducted on arrival);
                // EffectiveGold already subtracts them, so not added to _spent.
                _pending.Add(new PendingBuild
                {
                    BuilderPacked = SelectionActor(in a.Command),
                    TypeId = a.PendingType,
                    X = a.SiteX,
                    Y = a.SiteY,
                    IssuedTick = _s.Tick,
                });
            else if (spends)
            {
                _spentGold += cg;
                _spentLumber += cl;
                _spentOil += co;
            }

            if (a.Kind == AiActionKind.LaunchWave)
                OnWaveLaunched(a.SiteX, a.SiteY);
            else if (a.Kind == AiActionKind.Scout)
                _scouted = true;
        }

        /// <summary>The resource cost of an action, and whether it spends at all.
        /// Build family cost is looked up but flows through the pending ledger;
        /// train/research/upgrade cost is charged against the per-think tally.</summary>
        bool CostOf(in UtilityAction a, out int gold, out int lumber, out int oil)
        {
            gold = lumber = oil = 0;
            switch (a.Kind)
            {
                case AiActionKind.BuildFarm:
                case AiActionKind.Build:
                case AiActionKind.Expand:
                case AiActionKind.UpgradeBuilding:
                case AiActionKind.TrainUnit:
                case AiActionKind.TrainWorker:
                {
                    ref UnitTypeData row = ref _s.Rules.Units[a.Command.Param];
                    gold = row.GoldCost;
                    lumber = row.LumberCost;
                    oil = row.OilCost;
                    return true;
                }
                case AiActionKind.Research:
                {
                    ref UpgradeData row = ref _s.Rules.Upgrades[a.Command.Param];
                    gold = row.Gold;
                    lumber = row.Lumber;
                    oil = row.Oil;
                    return true;
                }
                default:
                    return false;
            }
        }

        unsafe uint SelectionActor(in GameCommand cmd)
        {
            var c = cmd;
            return c.SelectionCount > 0 ? c.Selection.Ids[0] : 0u;
        }

        void AddCandidate(AiActionKind kind, int score, in GameCommand cmd,
            ushort pendingType = 0, ushort sx = 0, ushort sy = 0)
        {
            if (score <= 0)
                return;
            _candidates.Add(new UtilityAction
            {
                Kind = kind,
                Score = score,
                Command = cmd,
                PendingType = pendingType,
                SiteX = sx,
                SiteY = sy,
                Seq = _seq++,
            });
        }

        unsafe void Emit(in GameCommand cmd)
        {
            _out.Add(cmd);
            _budget--;
            var c = cmd; // local copy: fixed buffers need an addressable home
            for (int i = 0; i < c.SelectionCount; i++)
                _claimed.Add(c.Selection.Ids[i]);
        }

        bool IsClaimed(uint packed) => _claimed.Contains(packed);

        // ---- Cost reservation: what is really spendable once in-flight Builds
        // (deducted only on builder arrival) have taken their cut. ----

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
            EffectiveGold() - _spentGold >= gold
            && EffectiveLumber() - _spentLumber >= lumber
            && _s.Players[Slot].Oil - _spentOil >= oil;

        // ---- Pending build ledger ----

        void ReconcilePending()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pb = _pending[i];
                if (!_s.TryGetUnitIndex(UnitId.FromPacked(pb.BuilderPacked), out int bi))
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                ref Unit b = ref _s.Units[bi];
                if (b.Order != OrderType.Build || b.BuildType != pb.TypeId + 1
                    || (b.Flags & UnitFlags.Hidden) != 0)
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                if (_s.Tick - pb.IssuedTick > PendingBuildTimeoutTicks)
                {
                    Emit(AiQueries.Command(CommandOp.Stop, Slot, pb.BuilderPacked));
                    _blacklistedSites.Add(pb.Y * _s.Terrain.Width + pb.X);
                    _pending.RemoveAt(i);
                }
            }
        }

        int PendingOfType(ushort typeId)
        {
            int n = 0;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].TypeId == typeId)
                    n++;
            return n;
        }
    }
}
