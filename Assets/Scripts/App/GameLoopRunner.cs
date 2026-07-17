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

        void Update()
        {
            if (Sim == null)
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
                _accumulator -= TickSeconds;
            }
            Alpha = Mathf.Clamp01(_accumulator / TickSeconds);
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

        void OnDestroy()
        {
            if (_replay == null || _replay.Entries.Count == 0)
                return;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "Replays");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "last-session.cwrp");
                File.WriteAllBytes(path, _replay.ToBytes());
                Debug.Log($"[Craftwar] Replay saved: {path} ({_replay.Entries.Count} commands)");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Craftwar] Replay save failed: {e.Message}");
            }
        }
    }
}
