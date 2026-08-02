using System;
using System.Collections.Generic;
using System.IO;
using Craftwar.App;
using Craftwar.Import;
using Craftwar.Sim;
using Craftwar.View;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Bakes every sound effect <c>Wc2SoundCatalog</c> can resolve into real
    /// AudioClip assets plus one <see cref="SoundTable"/>. WC2's WAVs are
    /// already plain PCM RIFF, so this is a copy-and-catalog step, not a
    /// decode — Unity's own audio importer does the rest.
    /// </summary>
    public static class SoundBaker
    {
        const string AudioDir = "Assets/GameData/Extracted/Audio";
        const string TableDir = "Assets/GameData/Extracted/Resources/Audio";

        static readonly Race[] VoicedRaces = { Race.Human, Race.Orc };

        public static void Bake(IAssetSource source)
        {
            var clipCache = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

            var globals = new List<SoundTable.GlobalEntry>
            {
                new() { id = SoundId.ResearchComplete, clip = Import(source, Wc2SoundCatalog.MiscConstruct, clipCache) },
                new() { id = SoundId.MineCollapsed, clip = Import(source, Wc2SoundCatalog.BldgMineCollapse, clipCache) },
                new() { id = SoundId.Denied, clip = Import(source, Wc2SoundCatalog.SfxError, clipCache) },
                new() { id = SoundId.PlacementBlocked, clip = Import(source, Wc2SoundCatalog.SfxError, clipCache) },
                new() { id = SoundId.SpellHeal, clip = Import(source, Wc2SoundCatalog.SpellHeal, clipCache) },
                new() { id = SoundId.SpellExorcism, clip = Import(source, Wc2SoundCatalog.SpellExorcism, clipCache) },
                new() { id = SoundId.SpellBloodlust, clip = Import(source, Wc2SoundCatalog.SpellBloodlust, clipCache) },
                new() { id = SoundId.SpellRunes, clip = Import(source, Wc2SoundCatalog.SpellRunes, clipCache) },
                new() { id = SoundId.SpellSlow, clip = Import(source, Wc2SoundCatalog.SpellSlow, clipCache) },
                new() { id = SoundId.SpellHaste, clip = Import(source, Wc2SoundCatalog.SpellHaste, clipCache) },
                new() { id = SoundId.SpellInvisibility, clip = Import(source, Wc2SoundCatalog.SpellInvisibility, clipCache) },
                new() { id = SoundId.SpellPolymorph, clip = Import(source, Wc2SoundCatalog.SpellPolymorph, clipCache) },
                new() { id = SoundId.SpellFlameShield, clip = Import(source, Wc2SoundCatalog.SpellFlameShield, clipCache) },
                new() { id = SoundId.SpellUnholyArmor, clip = Import(source, Wc2SoundCatalog.SpellUnholyArmor, clipCache) },
                new() { id = SoundId.SpellRaiseDead, clip = Import(source, Wc2SoundCatalog.SpellRaiseDead, clipCache) },
                new() { id = SoundId.SpellBlizzard, clip = Import(source, Wc2SoundCatalog.SpellBlizzard, clipCache) },
                new() { id = SoundId.SpellWhirlwind, clip = Import(source, Wc2SoundCatalog.SpellWhirlwind, clipCache) },
                new() { id = SoundId.SpellDeathAndDecay, clip = Import(source, Wc2SoundCatalog.SpellDeathAndDecay, clipCache) },
                new() { id = SoundId.RuneExplode, clip = Import(source, Wc2SoundCatalog.MiscExplode, clipCache) },
            };

            var variants = new List<SoundTable.VariantEntry>();
            foreach (UnitTypeId type in Enum.GetValues(typeof(UnitTypeId)))
            {
                foreach (Race race in VoicedRaces)
                {
                    foreach (UnitSoundKind kind in Enum.GetValues(typeof(UnitSoundKind)))
                    {
                        var paths = Wc2SoundCatalog.Find(source, type, race, kind);
                        if (paths.Count == 0)
                            continue;

                        var clips = new AudioClip[paths.Count];
                        for (int i = 0; i < paths.Count; i++)
                            clips[i] = Import(source, paths[i], clipCache);
                        variants.Add(new SoundTable.VariantEntry { type = type, race = race, kind = kind, clips = clips });
                    }
                }
            }

            string tablePath = $"{TableDir}/SoundTable.asset";
            if (AssetDatabase.LoadAssetAtPath<SoundTable>(tablePath) != null)
                AssetDatabase.DeleteAsset(tablePath);
            var table = BakeUtil.CreateOrLoadAsset<SoundTable>(tablePath);
            table.globals = globals.ToArray();
            table.variants = variants.ToArray();
            EditorUtility.SetDirty(table);

            Debug.Log($"[Craftwar] Baked {clipCache.Count} sound clips, " +
                      $"{variants.Count} unit-voice entries -> {tablePath}");
        }

        /// <summary>Copies one logical WAV into the project once and caches the
        /// resulting AudioClip, since many units share the same generic-race
        /// lines and would otherwise re-copy the same file per unit.</summary>
        static AudioClip Import(IAssetSource source, string logicalPath, Dictionary<string, AudioClip> cache)
        {
            if (cache.TryGetValue(logicalPath, out var cached))
                return cached;

            AudioClip clip = null;
            if (source.TryRead(logicalPath, out var bytes))
            {
                string assetPath = $"{AudioDir}/{logicalPath}";
                BakeUtil.EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
                File.WriteAllBytes(assetPath, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                if (clip == null)
                    Debug.LogWarning($"[Craftwar] Imported {assetPath} but Unity produced no AudioClip.");
            }
            else
            {
                Debug.LogWarning($"[Craftwar] Sound not found: {logicalPath}");
            }

            cache[logicalPath] = clip;
            return clip;
        }
    }
}
