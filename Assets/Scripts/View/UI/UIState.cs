using System.Collections.Generic;

namespace Craftwar.View
{
    /// <summary>
    /// An order that has been chosen from the command card but still needs a
    /// world click to resolve its target. Build is the original placement mode;
    /// the rest arrived with the unit action card.
    /// </summary>
    public enum PendingOrderKind : byte
    {
        None = 0,
        Build,
        Move,
        Attack,
        Patrol,
        Harvest,
        Repair,
        Unload,
    }

    /// <summary>
    /// Frame-rate UI/input state shared between world input and UI panels.
    /// View-only: never read by the sim, never serialized, never hashed.
    /// </summary>
    public sealed class UIState
    {
        /// <summary>Set while the player is choosing a target for a card order.</summary>
        public PendingOrderKind PendingOrder;

        /// <summary>Which building to place; only meaningful while
        /// <see cref="PendingOrder"/> is Build.</summary>
        public ushort PendingBuildType;

        public bool HasPendingOrder => PendingOrder != PendingOrderKind.None;

        public void BeginOrder(PendingOrderKind kind, ushort buildType = 0)
        {
            PendingOrder = kind;
            PendingBuildType = kind == PendingOrderKind.Build ? buildType : (ushort)0;
        }

        /// <summary>Cancels targeting. Safe to call when nothing is pending.</summary>
        public void ClearPendingOrder()
        {
            PendingOrder = PendingOrderKind.None;
            PendingBuildType = 0;
        }

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
