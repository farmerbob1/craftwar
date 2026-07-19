using System.Collections.Generic;
using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// The classic numbered control groups. Purely view-side: a group is a
    /// remembered selection, and only the orders it produces ever become
    /// GameCommands — the sim has no concept of a group.
    ///
    /// Membership is stored as packed UnitIds, so the generation counter in
    /// UnitId retires dead units for free: a slot reused by a new unit fails
    /// TryGetUnitIndex and is dropped on recall.
    /// </summary>
    public sealed class ControlGroups
    {
        /// <summary>Second press of the same group within this window centers
        /// the camera, as in the original.</summary>
        const float DoubleTapSeconds = 0.4f;

        readonly ISimHost _host;
        readonly SelectionState _selection;
        readonly CameraRig _camera;
        readonly int _mapHeight;

        readonly List<uint>[] _groups;
        readonly List<uint> _scratch = new List<uint>();
        int _lastRecalled = -1;
        float _lastRecallTime = -1f;

        public ControlGroups(ISimHost host, SelectionState selection, CameraRig camera,
            int mapHeight, int groupCount)
        {
            _host = host;
            _selection = selection;
            _camera = camera;
            _mapHeight = mapHeight;
            _groups = new List<uint>[groupCount];
            for (int i = 0; i < groupCount; i++)
                _groups[i] = new List<uint>();
        }

        /// <summary>Ctrl+N assigns the current selection, plain N recalls it.</summary>
        public void HandleKey(int group, bool assign)
        {
            if (group < 0 || group >= _groups.Length)
                return;
            if (assign)
                Assign(group);
            else
                Recall(group);
        }

        void Assign(int group)
        {
            var list = _groups[group];
            list.Clear();
            foreach (uint packed in _selection)
            {
                if (list.Count >= GameCommand.MaxSelection)
                    break;
                list.Add(packed);
            }
        }

        void Recall(int group)
        {
            var state = _host?.Sim?.State;
            if (state == null)
                return;

            var list = _groups[group];
            // Drop anything that died since the group was set.
            _scratch.Clear();
            for (int i = 0; i < list.Count; i++)
                if (state.TryGetUnitIndex(UnitId.FromPacked(list[i]), out _))
                    _scratch.Add(list[i]);
            list.Clear();
            list.AddRange(_scratch);

            if (list.Count == 0)
                return;

            _selection.Clear();
            for (int i = 0; i < list.Count; i++)
                _selection.Add(list[i]);

            // Second tap centers on the group.
            float now = Time.unscaledTime;
            if (_lastRecalled == group && now - _lastRecallTime <= DoubleTapSeconds)
                CenterOn(list, state);
            _lastRecalled = group;
            _lastRecallTime = now;
        }

        void CenterOn(List<uint> list, GameState state)
        {
            if (_camera == null)
                return;
            long sumX = 0, sumY = 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!state.TryGetUnitIndex(UnitId.FromPacked(list[i]), out int idx))
                    continue;
                sumX += state.Units[idx].TileX;
                sumY += state.Units[idx].TileY;
                n++;
            }
            if (n == 0)
                return;
            float tileX = (float)sumX / n;
            float tileY = (float)sumY / n;
            _camera.CenterOn(tileX + 0.5f, _mapHeight - tileY - 0.5f);
        }
    }
}
