using System.Collections.Generic;
using Craftwar.View;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Stand-in audio: short synthesized tones, one per SoundId, generated at
    /// startup. Nothing here is derived from Blizzard data, so it is safe to
    /// commit and ship.
    ///
    /// This exists to prove the seam. At M8 a War2 sound decoder replaces it as
    /// the IAudioProvider and every call site stays untouched — the same shape
    /// UnitSpriteBank has for art.
    /// </summary>
    public sealed class PlaceholderAudioBank : IAudioProvider
    {
        const int SampleRate = 22050;

        readonly Dictionary<SoundId, AudioClip> _clips = new Dictionary<SoundId, AudioClip>();

        /// <summary>(frequency Hz, seconds, falling) per sound — chosen so the
        /// categories are distinguishable by ear while placeholder.</summary>
        static readonly Dictionary<SoundId, (float hz, float seconds, bool falling)> Spec =
            new Dictionary<SoundId, (float, float, bool)>
            {
                { SoundId.OrderMove,        (660f, 0.07f, false) },
                { SoundId.OrderAttack,      (520f, 0.09f, false) },
                { SoundId.OrderSelect,      (880f, 0.05f, false) },
                { SoundId.WorkComplete,     (740f, 0.18f, false) },
                { SoundId.TrainComplete,    (620f, 0.16f, false) },
                { SoundId.ResearchComplete, (830f, 0.22f, false) },
                { SoundId.UnderAttack,      (300f, 0.30f, true)  },
                { SoundId.MineCollapsed,    (180f, 0.35f, true)  },
                { SoundId.Denied,           (220f, 0.16f, true)  },
                { SoundId.PlacementBlocked, (240f, 0.12f, true)  },
            };

        /// <summary>
        /// No synthesized voices: a tone per unit type would be noise, not a
        /// stand-in. Reporting zero variants makes callers skip barks entirely
        /// until real audio is available.
        /// </summary>
        public int UnitSoundVariants(Craftwar.Sim.UnitTypeId type, Craftwar.Sim.Race race,
                                     UnitSoundKind kind) => 0;

        public AudioClip GetUnitSound(Craftwar.Sim.UnitTypeId type, Craftwar.Sim.Race race,
                                      UnitSoundKind kind, int variant) => null;

        public AudioClip Get(SoundId id)
        {
            if (id == SoundId.None)
                return null;
            if (_clips.TryGetValue(id, out var cached))
                return cached;
            if (!Spec.TryGetValue(id, out var spec))
                return null;

            var clip = Synthesize(id.ToString(), spec.hz, spec.seconds, spec.falling);
            _clips[id] = clip;
            return clip;
        }

        /// <summary>
        /// A sine with a short attack and an exponential decay, so it reads as a
        /// blip rather than a click. Falling sounds slide down a fifth, which is
        /// what makes the negative cues (denied, under attack) obvious.
        /// </summary>
        static AudioClip Synthesize(string name, float hz, float seconds, bool falling)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));
            var data = new float[count];
            float phase = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float freq = falling ? Mathf.Lerp(hz, hz * 0.66f, t) : hz;
                phase += 2f * Mathf.PI * freq / SampleRate;

                float attack = Mathf.Min(1f, t / 0.05f);
                float decay = Mathf.Exp(-4f * t);
                data[i] = Mathf.Sin(phase) * attack * decay * 0.25f;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, stream: false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
