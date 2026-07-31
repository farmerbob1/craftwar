using System;
using System.IO;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// Player-facing gameplay options. Strictly presentation-side: revealing
    /// the map only stops the VIEW from hiding things (the sim's hashed fog
    /// grids keep computing and still gate targeting), and game speed only
    /// scales how fast wall-clock time feeds the fixed 50 Hz tick accumulator.
    /// Neither can touch determinism — replays record ticks, not seconds.
    ///
    /// Persisted as JSON in persistentDataPath so choices survive restarts.
    /// </summary>
    [Serializable]
    public sealed class GameplaySettings
    {
        /// <summary>Draw everything as visible. View-only; hashed fog state
        /// and sim-side gating (e.g. submarine detection) are unchanged.</summary>
        public bool revealMap;

        /// <summary>Index into <see cref="SpeedLabels"/>. The original offered
        /// a six-step speed setting; 1.0x is the original's fixed 50 Hz.</summary>
        public int speedIndex = NormalSpeedIndex;

        public const int NormalSpeedIndex = 2;

        // --- audio -------------------------------------------------------------
        // Stored 0..1 and applied multiplicatively: every AudioSource ends up at
        // master * its own channel. Presentation-only, like everything else here.

        public float masterVolume = 1f;
        public float musicVolume = 0.6f;
        public float effectsVolume = 1f;

        public float EffectiveMusic => Mathf.Clamp01(masterVolume) * Mathf.Clamp01(musicVolume);
        public float EffectiveEffects => Mathf.Clamp01(masterVolume) * Mathf.Clamp01(effectsVolume);

        /// <summary>Raised whenever a volume changes, so the live directors can
        /// re-read it without polling.</summary>
        public static event Action VolumesChanged;

        public static void RaiseVolumesChanged() => VolumesChanged?.Invoke();

        public static readonly string[] SpeedLabels =
            { "Slowest", "Slow", "Normal", "Fast", "Faster", "Fastest" };

        static readonly float[] SpeedMultipliers = { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f };

        public float SpeedMultiplier => SpeedMultipliers[ClampedIndex];
        public string SpeedLabel => SpeedLabels[ClampedIndex];

        int ClampedIndex => Mathf.Clamp(speedIndex, 0, SpeedMultipliers.Length - 1);

        /// <summary>Same lookup as the instance SpeedMultiplier property, for
        /// a host-chosen index that arrived over the wire (LobbyPayload.
        /// SpeedIndex) rather than this client's own saved preference.</summary>
        public static float MultiplierForIndex(int index) =>
            SpeedMultipliers[Mathf.Clamp(index, 0, SpeedMultipliers.Length - 1)];

        /// <summary>Wraps — a single button that walks the whole list.</summary>
        public void CycleSpeed(int delta) =>
            speedIndex = (ClampedIndex + delta + SpeedMultipliers.Length)
                % SpeedMultipliers.Length;

        /// <summary>
        /// Clamps — the in-game +/- keys, where wrapping from Fastest round to
        /// Slowest on one extra keypress would be a nasty surprise.
        /// </summary>
        public void StepSpeed(int delta) =>
            speedIndex = Mathf.Clamp(ClampedIndex + delta, 0, SpeedMultipliers.Length - 1);

        // --- Persistence -------------------------------------------------------

        static GameplaySettings _current;

        public static GameplaySettings Current => _current ??= Load();

        static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        static GameplaySettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonUtility.FromJson<GameplaySettings>(
                        File.ReadAllText(FilePath));
                    if (loaded != null)
                        return loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Craftwar] Could not read settings: {e.Message}");
            }
            return new GameplaySettings();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Craftwar] Could not save settings: {e.Message}");
            }
        }
    }
}
