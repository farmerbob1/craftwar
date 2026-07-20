using System.Collections.Generic;
using System.IO;
using Craftwar.Net;
using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Drives the deterministic sim at a fixed 50 Hz from Unity's render
    /// loop, snapshots positions for interpolation, and records the command
    /// log as a replay (saved on exit).
    /// </summary>
    public sealed class GameLoopRunner : MonoBehaviour, View.ISimHost
    {
        public GameSim Sim { get; private set; }
        public ILockstepDriver Driver { get; private set; }

        /// <summary>Interpolation factor between the previous and current tick.</summary>
        public float Alpha { get; private set; }

        public int[] PrevPixX { get; private set; }
        public int[] PrevPixY { get; private set; }

        readonly List<GameCommand> _tickCommands = new List<GameCommand>();
        Replay _replay;
        float _accumulator;
        const float TickSeconds = SimConstants.MsPerTick / 1000f;

        /// <summary>Tile mutations accumulated across the ticks run this frame; drained by the view.</summary>
        public readonly List<(ushort x, ushort y, ushort tile)> PendingTileChanges
            = new List<(ushort, ushort, ushort)>();

        /// <summary>Presentation events accumulated across the ticks run this frame; drained by the view.</summary>
        public readonly List<SimEvent> PendingSimEvents = new List<SimEvent>();

        public void Init(GameSim sim, ILockstepDriver driver, Replay replay)
        {
            Sim = sim;
            Driver = driver;
            _replay = replay;
            PrevPixX = new int[SimConstants.MaxUnits];
            PrevPixY = new int[SimConstants.MaxUnits];
            SnapshotPositions();
        }

        public void SubmitCommand(in GameCommand cmd) => Driver.SubmitLocalCommand(cmd);

        /// <summary>
        /// Single-player pause. The sim simply stops being advanced, so the
        /// state hash and the replay are untouched — a replay recorded across a
        /// paused session still verifies. Zeroing the accumulator on pause stops
        /// the wall-clock gap from being spent as catch-up ticks on resume, and
        /// freezing Alpha holds units mid-stride instead of snapping them.
        /// Networked lockstep (M10) cannot pause this way: every peer would
        /// have to agree, so that becomes a driver-level concern.
        /// </summary>
        public bool Paused { get; private set; }

        public void SetPaused(bool paused)
        {
            if (Paused == paused)
                return;
            Paused = paused;
            if (paused)
                _accumulator = 0f;
        }

        void Update()
        {
            if (Sim == null || Paused)
                return;

            _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            int safety = 8; // don't spiral after a hitch
            while (_accumulator >= TickSeconds && safety-- > 0)
            {
                if (!Driver.TryGetTickCommands(Sim.State.Tick, _tickCommands))
                    break; // waiting on network turn (M10+)

                SnapshotPositions();
                if (_replay != null)
                    foreach (var c in _tickCommands)
                        _replay.Record(Sim.State.Tick, c);
                Sim.Advance(_tickCommands);
                PendingTileChanges.AddRange(Sim.State.TileChanges);
                PendingSimEvents.AddRange(Sim.State.Events);
                ReconcileTeleports();
                _accumulator -= TickSeconds;
            }
            Alpha = Mathf.Clamp01(_accumulator / TickSeconds);
        }

        /// <summary>
        /// A unit whose position jumped further than any legal per-tick move
        /// (mine/depot exit, training spawn into a recycled or fresh slot)
        /// must not interpolate across the gap — snap its previous position
        /// so it renders at the new spot immediately. Fastest units move
        /// under 1 px per tick, so 4 px cleanly separates walk from teleport.
        /// </summary>
        void ReconcileTeleports()
        {
            var units = Sim.State.Units;
            for (int i = 0; i < Sim.State.HighestUnitIndex; i++)
            {
                int dx = units[i].PixX - PrevPixX[i];
                int dy = units[i].PixY - PrevPixY[i];
                if (dx > 4 || dx < -4 || dy > 4 || dy < -4)
                {
                    PrevPixX[i] = units[i].PixX;
                    PrevPixY[i] = units[i].PixY;
                }
            }
        }

        void SnapshotPositions()
        {
            var units = Sim.State.Units;
            for (int i = 0; i < Sim.State.HighestUnitIndex; i++)
            {
                PrevPixX[i] = units[i].PixX;
                PrevPixY[i] = units[i].PixY;
            }
        }

        public static string ReplayDir => Path.Combine(Application.persistentDataPath, "Replays");

        /// <summary>
        /// Write the command log. Explicit saves are timestamped by the caller;
        /// without one, every return to the menu would overwrite the same
        /// last-session file, which only worked while quitting the app was the
        /// sole way a match ended.
        /// </summary>
        public bool SaveReplay(string path)
        {
            if (_replay == null || _replay.Entries.Count == 0)
                return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, _replay.ToBytes());
                Debug.Log($"[Craftwar] Replay saved: {path} ({_replay.Entries.Count} commands)");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Craftwar] Replay save failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Safety net for crashes and alt-F4; a clean end-of-match has
        /// already written its own timestamped copy.</summary>
        void OnDestroy() => SaveReplay(Path.Combine(ReplayDir, "last-session.cwrp"));
    }
}
