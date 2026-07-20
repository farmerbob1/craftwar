using System.Collections.Generic;
using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// Plays the game's sound effects: a small AudioSource pool plus a
    /// per-sound cooldown so a burst of identical events (ten peasants finishing
    /// at once, a building taking a volley) makes one noise, not ten.
    ///
    /// Sim events arrive through the existing derived-output channel, so audio
    /// can never influence the simulation. Order acknowledgements are raised by
    /// local input only — in lockstep every peer would otherwise hear every
    /// player's clicks.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        const int VoiceCount = 8;
        /// <summary>Default gap between two plays of the same sound.</summary>
        const float DefaultCooldown = 0.12f;

        IAudioProvider _provider;
        byte _player;
        AudioSource[] _voices;
        int _next;
        readonly Dictionary<SoundId, float> _lastPlayed = new Dictionary<SoundId, float>();

        /// <summary>Sounds that are noisy by nature get a longer guard.</summary>
        static float CooldownFor(SoundId id) => id switch
        {
            SoundId.UnderAttack => 4f,   // the sim already throttles this
            SoundId.Denied => 0.5f,
            SoundId.WorkComplete => 0.3f,
            SoundId.TrainComplete => 0.3f,
            _ => DefaultCooldown,
        };

        public void Init(IAudioProvider provider, byte localPlayer)
        {
            _provider = provider;
            _player = localPlayer;

            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;   // 2D: the camera is not a listener position
                _voices[i] = src;
            }

            // UI-only scenes have no listener; without one nothing is audible.
            // FindAny, not FindFirst: we only care whether one exists, and
            // FindFirst is deprecated for depending on instance-ID ordering.
            if (FindAnyObjectByType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();
        }

        public void Play(SoundId id)
        {
            if (_provider == null || id == SoundId.None)
                return;

            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(id, out float last) && now - last < CooldownFor(id))
                return;

            var clip = _provider.Get(id);
            if (clip == null)
                return;

            _lastPlayed[id] = now;
            PlayClip(clip);
        }

        /// <summary>
        /// Take a voice. Prefers an idle one and only steals if all eight are
        /// busy — plain round-robin would cut a half-finished line off mid-word
        /// as soon as eight sounds overlap, which real voice lines (1-3 seconds)
        /// make far likelier than the old blips did.
        /// </summary>
        void PlayClip(AudioClip clip)
        {
            AudioSource voice = null;
            for (int i = 0; i < _voices.Length; i++)
            {
                var candidate = _voices[(_next + i) % _voices.Length];
                if (!candidate.isPlaying)
                {
                    voice = candidate;
                    _next = (_next + i + 1) % _voices.Length;
                    break;
                }
            }
            if (voice == null)
            {
                voice = _voices[_next];
                _next = (_next + 1) % _voices.Length;
            }
            voice.clip = clip;
            voice.Play();
        }

        /// <summary>
        /// A unit's voice line, picking a take at random.
        ///
        /// The draw uses UnityEngine.Random, never GameState.Rng: this is
        /// presentation, and consuming sim randomness from the view would make
        /// the world depend on what the local player happened to click.
        ///
        /// Only one line per event, not one per unit — selecting twelve footmen
        /// makes one of them answer, as in the original.
        /// </summary>
        /// <summary>
        /// Returns false when this unit has no such line, so the caller can fall
        /// back to a generic cue. True also when the bark was suppressed by the
        /// cooldown — the unit *does* speak, we simply chose not to repeat it,
        /// and playing a tone instead would be worse than staying quiet.
        /// </summary>
        public bool TryPlayUnitSound(UnitTypeId type, Race race, UnitSoundKind kind)
        {
            if (_provider == null)
                return false;
            if (_provider.UnitSoundVariants(type, race, kind) <= 0)
                return false;

            float now = Time.unscaledTime;
            // Barks share one cooldown across all units and kinds: the original's
            // ACK_SND_MIN_TIME is a single global gate (GAMESND.C), which is what
            // stops rapid clicking from producing a chorus.
            if (now - _lastBark < BarkCooldown)
                return true;

            int count = _provider.UnitSoundVariants(type, race, kind);
            var clip = _provider.GetUnitSound(type, race, kind, UnityEngine.Random.Range(0, count));
            if (clip == null)
                return false;

            _lastBark = now;
            PlayClip(clip);
            return true;
        }

        float _lastBark = -999f;

        /// <summary>ACK_SND_MIN_TIME in the original: one second between barks.</summary>
        const float BarkCooldown = 1f;

        /// <summary>
        /// Turn this frame's sim events into sounds. Only events belonging to
        /// the local player are audible.
        /// </summary>
        public void HandleSimEvents(List<SimEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Player != _player)
                    continue;

                switch (e.Kind)
                {
                    case SimEventKind.ConstructionComplete:
                    case SimEventKind.UpgradeComplete:
                        Play(SoundId.WorkComplete);
                        break;
                    case SimEventKind.TrainComplete:
                        Play(SoundId.TrainComplete);
                        break;
                    case SimEventKind.ResearchComplete:
                        Play(SoundId.ResearchComplete);
                        break;
                    case SimEventKind.UnderAttack:
                        Play(SoundId.UnderAttack);
                        break;
                    case SimEventKind.MineCollapsed:
                        Play(SoundId.MineCollapsed);
                        break;
                    case SimEventKind.CommandDenied:
                        Play(SoundId.Denied);
                        break;
                    case SimEventKind.BuildSiteBlocked:
                        Play(SoundId.PlacementBlocked);
                        break;
                }
            }
        }
    }
}
