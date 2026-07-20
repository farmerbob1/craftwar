using System.Collections;
using System.Collections.Generic;
using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.View
{
    /// <summary>
    /// Plays background music: one track at a time, crossfaded, with the in-game
    /// score shuffled so a long match does not loop the same piece.
    ///
    /// Survives scene loads (DontDestroyOnLoad) so the menu music does not cut
    /// out mid-bar when a match starts loading. That is also why it owns its own
    /// AudioSources rather than borrowing AudioDirector's voice pool — those are
    /// short-lived and per-match.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        const float FadeSeconds = 1.5f;

        static MusicDirector _instance;

        IMusicProvider _provider;
        AudioSource _current, _next;
        MusicCue _cue = MusicCue.None;
        Race _race = Race.Human;

        readonly List<string> _bag = new List<string>();
        int _bagIndex;
        Coroutine _transition;
        float _volume = 0.6f;

        /// <summary>
        /// The one director, created on first use. A second scene asking for
        /// music reuses it rather than starting a competing track.
        /// </summary>
        public static MusicDirector Ensure(IMusicProvider provider)
        {
            if (_instance == null)
            {
                var go = new GameObject("MusicDirector");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<MusicDirector>();
                _instance.Build();
            }
            if (provider != null)
                _instance._provider = provider;
            return _instance;
        }

        void Build()
        {
            _current = CreateSource();
            _next = CreateSource();
        }

        AudioSource CreateSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;      // the bag advances instead; see Update
            src.spatialBlend = 0f; // music is never positional
            src.volume = 0f;
            return src;
        }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                if (_current != null && _current.isPlaying && _transition == null)
                    _current.volume = _volume;
            }
        }

        /// <summary>
        /// Switch to a cue. Re-requesting the current cue is ignored, so calling
        /// this every frame from a state machine is safe.
        /// </summary>
        public void Play(MusicCue cue, Race race = Race.Human)
        {
            if (_provider == null || (cue == _cue && race == _race))
                return;
            _cue = cue;
            _race = race;

            _bag.Clear();
            _bagIndex = 0;
            _bag.AddRange(_provider.TracksFor(cue, race));
            Shuffle(_bag);

            if (_bag.Count == 0)
            {
                Stop();
                return;
            }
            StartTrack(_bag[0]);
        }

        public void Stop()
        {
            if (_transition != null)
                StopCoroutine(_transition);
            _transition = StartCoroutine(FadeOut());
        }

        void StartTrack(string track)
        {
            if (_transition != null)
                StopCoroutine(_transition);
            _transition = StartCoroutine(SwapTo(track));
        }

        IEnumerator SwapTo(string track)
        {
            AudioClip clip = null;
            yield return _provider.Load(track, c => clip = c);
            if (clip == null)
            {
                _transition = null;
                yield break;
            }

            _next.clip = clip;
            _next.volume = 0f;
            _next.Play();

            // Crossfade rather than cut: victory and defeat stings interrupt
            // in-game music, and a hard cut there is jarring.
            float t = 0f;
            float from = _current.volume;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FadeSeconds);
                _next.volume = _volume * k;
                _current.volume = from * (1f - k);
                yield return null;
            }

            _current.Stop();
            _current.clip = null;
            (_current, _next) = (_next, _current);
            _current.volume = _volume;
            _transition = null;
        }

        IEnumerator FadeOut()
        {
            float t = 0f;
            float from = _current.volume;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                _current.volume = from * (1f - Mathf.Clamp01(t / FadeSeconds));
                yield return null;
            }
            _current.Stop();
            _current.clip = null;
            _transition = null;
        }

        void Update()
        {
            // Advance the bag when a track ends. Stings (single-track cues) are
            // left to finish and stay silent afterwards, as in the original.
            if (_transition != null || _bag.Count <= 1)
                return;
            if (_current.clip == null || _current.isPlaying)
                return;

            _bagIndex++;
            if (_bagIndex >= _bag.Count)
            {
                // Reshuffle only once the whole score has played, so no track
                // repeats until every other one has been heard.
                Shuffle(_bag);
                _bagIndex = 0;
            }
            StartTrack(_bag[_bagIndex]);
        }

        /// <summary>
        /// Fisher-Yates on UnityEngine.Random. Never GameState.Rng — this is
        /// presentation, and drawing from the sim's PRNG would make the world
        /// depend on what music happened to play.
        /// </summary>
        static void Shuffle(List<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
