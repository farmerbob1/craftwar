using System.Collections.Generic;
using Craftwar.Net;
using Craftwar.Sim;
using Craftwar.Sim.Ai;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Host-side handling for a peer that goes quiet: detect the stall
    /// ourselves rather than waiting on the transport's own (much slower —
    /// ~30s for UTP) disconnect callback, give it a real-time grace window
    /// with a "Waiting for &lt;player&gt;..." overlay, and only then hand the
    /// seat to an AI so the match keeps moving.
    ///
    /// Kept in its own file because it is a different job from match setup —
    /// and because it owns the one piece of per-frame state (the grace
    /// timers) that has no sim-tick equivalent.
    /// </summary>
    public sealed partial class GameLoopRunner
    {
        /// <summary>How long a stalled slot gets before the AI takes over.
        /// Deliberately shorter than UTP's own ~30s disconnect timeout — this
        /// is what lets a drop be noticed and handled promptly instead of
        /// freezing the match for half a minute first.</summary>
        const float DropGraceSeconds = 10f;

        const AiTier TakeoverAiTier = AiTier.Normal;

        HostTurnExchange _hostExchange;

        /// <summary>Seats currently in their grace window, and how much of it
        /// is left. Read by the HUD to draw the "Waiting for &lt;player&gt;…"
        /// overlay.</summary>
        readonly Dictionary<byte, float> _dropGraceRemaining = new Dictionary<byte, float>();

        /// <summary>Seats an on-behalf AI now drives, keyed by slot.</summary>
        readonly Dictionary<byte, AiPlayer> _substituteAis = new Dictionary<byte, AiPlayer>();

        readonly List<byte> _blockingScratch = new List<byte>();
        readonly List<byte> _graceScratchKeys = new List<byte>();
        readonly List<GameCommand> _substituteScratch = new List<GameCommand>();

        /// <summary>Seats currently showing the "waiting" overlay. Exposed for
        /// the HUD; empty outside a networked match.</summary>
        public IReadOnlyCollection<byte> SeatsAwaitingDrop => _dropGraceRemaining.Keys;

        /// <summary>Seats an on-behalf AI is currently driving.</summary>
        public IReadOnlyCollection<byte> SubstitutedSeats => _substituteAis.Keys;

        void InitDropHandling(HostTurnExchange hostExchange)
        {
            _hostExchange = hostExchange;
            if (_hostExchange != null)
                // The transport has already substituted the slot at the net
                // layer by the time this fires (HostTurnExchange.Poll does
                // that immediately on a confirmed disconnect) — this just
                // makes sure an AI exists to actually drive it. If our own
                // grace timer beat the transport to it, StartTakeoverAi is a
                // no-op the second time.
                _hostExchange.PeerDropped += StartTakeoverAi;
        }

        /// <summary>Run once per frame, independent of the sim tick — a stuck
        /// turn means the sim clock itself is frozen, so this cannot be
        /// gated on ticks the way AI Think() is.</summary>
        void UpdateDropDetection()
        {
            if (_hostExchange == null)
                return;

            bool anyBlocked = _hostExchange.TryGetOldestBlockedTurn(out _, _blockingScratch);

            if (anyBlocked)
                foreach (byte slot in _blockingScratch)
                    if (!_substituteAis.ContainsKey(slot) && !_dropGraceRemaining.ContainsKey(slot))
                        _dropGraceRemaining[slot] = DropGraceSeconds;

            if (_dropGraceRemaining.Count == 0)
                return;

            _graceScratchKeys.Clear();
            _graceScratchKeys.AddRange(_dropGraceRemaining.Keys);
            foreach (byte slot in _graceScratchKeys)
            {
                // A slot that stops blocking caught up (or the turn it was
                // blocking resolved some other way) — clear its clock rather
                // than pausing it, so a merely-slow peer never accumulates
                // credit toward a takeover it did not actually trigger.
                if (!anyBlocked || !_blockingScratch.Contains(slot))
                {
                    _dropGraceRemaining.Remove(slot);
                    continue;
                }

                float remaining = _dropGraceRemaining[slot] - Time.unscaledDeltaTime;
                if (remaining <= 0f)
                {
                    _dropGraceRemaining.Remove(slot);
                    _hostExchange.SubstitutePeer(slot);
                    StartTakeoverAi(slot);
                }
                else
                {
                    _dropGraceRemaining[slot] = remaining;
                }
            }
        }

        /// <summary>Construct and reconcile the on-behalf AI for a substituted
        /// seat. Idempotent — safe to call from both the grace-timer path and
        /// the transport-drop path without double-constructing.</summary>
        void StartTakeoverAi(byte slot)
        {
            if (_substituteAis.ContainsKey(slot))
                return;

            var ai = new AiPlayer(slot, AiBehavior.LandAttack, AiProfileLibrary.Resolve(null), TakeoverAiTier);
            ai.ReconcileFromState(Sim);
            _substituteAis[slot] = ai;
            Debug.Log($"[craftwar-net] seat {slot} dropped — an AI is now playing it");
        }

        /// <summary>Think every substitute AI and submit its output for the
        /// substituted slot — a completely separate path from the normal
        /// computer players (whose commands ride inside THIS peer's own local
        /// slot): a substituted seat's input must land under its OWN slot, and
        /// TurnRelay accepts at most one submission per slot per turn, so it
        /// is submitted for the next turn that has not been touched yet
        /// rather than batched like the driver's own local buffer.</summary>
        void ThinkSubstituteAis()
        {
            if (_substituteAis.Count == 0)
                return;
            foreach (var pair in _substituteAis)
            {
                _substituteScratch.Clear();
                pair.Value.Think(Sim, _substituteScratch);
                if (_substituteScratch.Count > 0)
                    _hostExchange.SubmitSubstituteInput(pair.Key, _net.CurrentTurn + 1, _substituteScratch);
            }
        }
    }
}
