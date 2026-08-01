using System.Collections.Generic;
using Craftwar.Sim.Pud;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Reads a pre-baked <see cref="UnitSpriteTable"/> instead of decoding GRP
    /// banks from a live install every session. Replaces <c>UnitSpriteBank</c>
    /// — see <c>Craftwar/Setup/Import Warcraft II Assets</c>. Team colour is
    /// applied by the caller's renderer (see <see cref="MaskFor"/> and the
    /// <c>Craftwar/UnitTeamColor</c> shader), not baked per player, so every
    /// <c>player</c> parameter below is accepted only to satisfy
    /// <see cref="IUnitSpriteProvider"/> and otherwise unused.
    /// </summary>
    public sealed class BakedUnitSpriteBank : IUnitSpriteProvider
    {
        readonly PudEra _era;
        readonly Dictionary<string, UnitSpriteTable.FileEntry> _files;
        readonly Dictionary<(ushort type, PudEra era), string> _typeFile;
        readonly Dictionary<(ushort type, byte carry, PudEra era), string> _carryFile;
        Sprite[] _foundationFrames;
        Sprite[] _corpseFrames;

        public static string ResourcePath => "Sprites/UnitSpriteTable";

        public static BakedUnitSpriteBank Load(PudEra era)
        {
            var table = Resources.Load<UnitSpriteTable>(ResourcePath);
            if (table == null)
            {
                Debug.LogError("[Craftwar] No baked unit sprite table. Run Craftwar/Setup/Import Warcraft II Assets.");
                return null;
            }
            return new BakedUnitSpriteBank(table, era);
        }

        BakedUnitSpriteBank(UnitSpriteTable table, PudEra era)
        {
            _era = era;

            _files = new Dictionary<string, UnitSpriteTable.FileEntry>(table.files.Length);
            foreach (var f in table.files)
                _files[f.fileKey] = f;

            _typeFile = new Dictionary<(ushort, PudEra), string>(table.types.Length);
            foreach (var t in table.types)
                _typeFile[((ushort)t.type, t.era)] = t.file;

            _carryFile = new Dictionary<(ushort, byte, PudEra), string>(table.carries.Length);
            foreach (var c in table.carries)
                _carryFile[((ushort)c.type, c.carry, c.era)] = c.file;

            foreach (var s in table.foundations)
                if (s.era == era)
                    _foundationFrames = s.frames;
            foreach (var s in table.corpses)
                if (s.era == era)
                    _corpseFrames = s.frames;
        }

        static string FileKey(string file, PudEra era) => $"{file}#{era}";

        public bool Has(ushort typeId) => _typeFile.ContainsKey((typeId, _era));

        string ResolveFile(ushort typeId, byte carry)
        {
            if (carry != 0 && _carryFile.TryGetValue((typeId, carry, _era), out var carryFile))
                return carryFile;
            return _typeFile.TryGetValue((typeId, _era), out var file) ? file : null;
        }

        public AnimLayout LayoutFor(ushort typeId, byte carry)
        {
            string file = ResolveFile(typeId, carry);
            return UnitAnimTable.ForFile(file);
        }

        bool IsAnimated(ushort typeId, byte carry = 0) => LayoutFor(typeId, carry).IsValid;

        public int BlockCount(ushort typeId, byte player)
        {
            var frames = GetFrames(typeId);
            return frames == null || !IsAnimated(typeId) ? 0 : frames.Length / 5;
        }

        public Texture2D MaskFor(ushort typeId, byte carry)
        {
            string file = ResolveFile(typeId, carry);
            if (file == null || !_files.TryGetValue(FileKey(file, _era), out var entry))
                return null;
            return entry.maskAtlas;
        }

        Sprite[] GetFrames(ushort typeId, byte carry = 0)
        {
            string file = ResolveFile(typeId, carry);
            if (file == null || !_files.TryGetValue(FileKey(file, _era), out var entry))
                return null;
            return entry.color;
        }

        public Sprite Get(ushort typeId, byte player, byte facing, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId);
            if (frames == null || frames.Length == 0)
                return null;

            if (!LayoutFor(typeId, 0).IsValid)
                return frames[0]; // single-pose bank: buildings, scenery

            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = spriteDir < frames.Length ? spriteDir : 0;
            return frames[index];
        }

        public Sprite GetAnimFrame(ushort typeId, byte player, byte facing, int block, byte carry, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId, carry);
            if (frames == null || frames.Length == 0)
                return null;
            if (!IsAnimated(typeId, carry))
                return frames[0];
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = block * 5 + spriteDir;
            if (index >= frames.Length)
                index = spriteDir;
            return frames[index];
        }

        public int BuildingFrameCount(ushort typeId, byte player)
        {
            var frames = GetFrames(typeId);
            return frames == null || IsAnimated(typeId) ? 0 : frames.Length;
        }

        public Sprite GetBuildingFrame(ushort typeId, byte player, int frameIndex, out bool flipX)
        {
            flipX = false;
            var frames = GetFrames(typeId);
            if (frames == null || frames.Length == 0)
                return null;
            int idx = frameIndex < 0 ? 0
                : frameIndex >= frames.Length ? frames.Length - 1
                : frameIndex;
            return frames[idx];
        }

        public Sprite GetFoundationFrame(int stage)
        {
            if (_foundationFrames == null || _foundationFrames.Length == 0)
                return null;
            int i = stage < 0 ? 0 : stage >= _foundationFrames.Length ? _foundationFrames.Length - 1 : stage;
            return _foundationFrames[i];
        }

        public int CorpseBlockCount => _corpseFrames == null ? 0 : _corpseFrames.Length / 5;

        public Sprite GetCorpseFrame(int block, byte facing, out bool flipX)
        {
            flipX = false;
            if (_corpseFrames == null || _corpseFrames.Length == 0)
                return null;
            int spriteDir = facing <= 4 ? facing : 8 - facing;
            flipX = facing > 4;
            int index = block * 5 + spriteDir;
            if (index < 0 || index >= _corpseFrames.Length)
                index = spriteDir < _corpseFrames.Length ? spriteDir : 0;
            return _corpseFrames[index];
        }
    }
}
