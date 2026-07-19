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
            var voice = _voices[_next];
            _next = (_next + 1) % _voices.Length;
            voice.clip = clip;
            voice.Play();
        }

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
