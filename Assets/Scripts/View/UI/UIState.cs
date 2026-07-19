using System.Collections.Generic;

namespace Craftwar.View
{
    /// <summary>
    /// Frame-rate UI/input state shared between world input and UI panels.
    /// View-only: never read by the sim, never serialized, never hashed.
    /// Replaces the old HudController handshake object.
    /// </summary>
    public sealed class UIState
    {
        /// <summary>Nonzero while the player is placing a building.</summary>
        public ushort PendingBuildType;

        /// <summary>A modal screen is open; world and camera input are dead.</summary>
        public bool ModalOpen;

        /// <summary>Refreshed once per frame by UIManager from panel.Pick.</summary>
        public bool PointerOverUI;

        public readonly SelectionState Selection = new SelectionState();
    }

    /// <summary>
    /// The local player's selection, as packed <see cref="Sim.UnitId"/>s, with a
    /// version counter so panels can dirty-check without diffing the set.
    /// </summary>
    public sealed class SelectionState
    {
        public readonly HashSet<uint> Ids = new HashSet<uint>();

        /// <summary>Bumped on every mutation that actually changed the set.</summary>
        public int Version { get; private set; }

        public int Count => Ids.Count;

        public bool Contains(uint packed) => Ids.Contains(packed);

        public void Clear()
        {
            if (Ids.Count == 0)
                return;
            Ids.Clear();
            Version++;
        }

        public bool Add(uint packed)
        {
            if (!Ids.Add(packed))
                return false;
            Version++;
            return true;
        }

        public bool Remove(uint packed)
        {
            if (!Ids.Remove(packed))
                return false;
            Version++;
            return true;
        }

        public void SetSingle(uint packed)
        {
            if (Ids.Count == 1 && Ids.Contains(packed))
                return;
            Ids.Clear();
            Ids.Add(packed);
            Version++;
        }

        public HashSet<uint>.Enumerator GetEnumerator() => Ids.GetEnumerator();
    }
}
