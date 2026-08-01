using System.Collections.Generic;
using System.IO;
using Craftwar.App;
using Craftwar.Import;
using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Bakes one <see cref="MusicTable"/> covering every track
    /// <see cref="BakedMusicLibrary"/> can ask for. Prefers an already-converted
    /// Ogg sitting in Assets/GameData/Extracted/Music (see
    /// Tools/convert_music.py — left as a separate manual step since this
    /// project has no in-process Vorbis encoder) and only falls back to
    /// copying the source WAV when no such conversion has been run.
    /// </summary>
    public static class MusicBaker
    {
        const string MusicDir = "Assets/GameData/Extracted/Music";
        const string TableDir = "Assets/GameData/Extracted/Resources/Music";

        // Every stem BakedMusicLibrary.Resolve can ask for, redbook ("_r") only.
        static readonly string[] Stems =
        {
            "HUMAN1", "HUMAN2", "HUMAN3", "HUMAN4", "HUMAN5", "HUMAN6",
            "ORC1", "ORC2", "ORC3", "ORC4", "ORC5", "ORC6",
            "HWARROOM", "OWARROOM", "HVICTORY", "OVICTORY", "HDEFEAT", "ODEFEAT",
        };

        public static void Bake(IAssetSource source)
        {
            var entries = new List<MusicTable.Entry>(Stems.Length);
            int found = 0;

            foreach (string stem in Stems)
            {
                string key = stem + "_r";
                var clip = ResolveOrImport(source, key);
                if (clip != null)
                    found++;
                entries.Add(new MusicTable.Entry { stem = key, clip = clip });
            }

            string tablePath = $"{TableDir}/MusicTable.asset";
            if (AssetDatabase.LoadAssetAtPath<MusicTable>(tablePath) != null)
                AssetDatabase.DeleteAsset(tablePath);
            var table = BakeUtil.CreateOrLoadAsset<MusicTable>(tablePath);
            table.entries = entries.ToArray();
            EditorUtility.SetDirty(table);

            Debug.Log($"[Craftwar] Baked {found}/{Stems.Length} music tracks -> {tablePath}");
        }

        static AudioClip ResolveOrImport(IAssetSource source, string key)
        {
            string oggPath = $"{MusicDir}/{key}.ogg";
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(oggPath);
            if (existing != null)
                return existing; // already converted by Tools/convert_music.py; leave its import settings alone

            if (!source.TryRead($"music/{key}.wav", out var bytes))
            {
                Debug.LogWarning($"[Craftwar] Music track not found: {key}");
                return null;
            }

            string wavPath = $"{MusicDir}/{key}.wav";
            BakeUtil.EnsureFolder(MusicDir);
            File.WriteAllBytes(wavPath, bytes);
            AssetDatabase.ImportAsset(wavPath, ImportAssetOptions.ForceUpdate);

            // Long streamed tracks, not one-shot clips: decode-on-load would spike memory.
            var importer = (AudioImporter)AssetImporter.GetAtPath(wavPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<AudioClip>(wavPath);
        }
    }
}
