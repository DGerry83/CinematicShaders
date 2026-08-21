using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Central audio manager for CinematicShaders mod.
    /// Supports per-group volume/mute scalars plus a master volume/mute,
    /// one-shot playback, and named looping sounds.
    /// </summary>
    public static class ModAudioManager
    {
        // ------------------------------------------------------------------------
        // Volume / Mute State
        // ------------------------------------------------------------------------
        private static readonly Dictionary<AudioGroup, float> _groupVolumes = new Dictionary<AudioGroup, float>();
        private static readonly Dictionary<AudioGroup, bool> _groupMuted = new Dictionary<AudioGroup, bool>();
        private static float _masterVolume = 1.0f;
        private static bool _masterMuted = false;

        // ------------------------------------------------------------------------
        // Loop Tracking
        // ------------------------------------------------------------------------
        private static readonly Dictionary<string, AudioSource> _activeLoops = new Dictionary<string, AudioSource>();
        private static readonly Dictionary<AudioSource, Coroutine> _activeFades = new Dictionary<AudioSource, Coroutine>();
        private static GameObject _audioRoot;

        private const float DefaultFadeOutSeconds = 0.025f;

        // ------------------------------------------------------------------------
        // Coroutine Host (for fade-outs from static context)
        // ------------------------------------------------------------------------
        private class AudioCoroutineHost : MonoBehaviour { }

        // ------------------------------------------------------------------------
        // Clip Cache
        // ------------------------------------------------------------------------
        private static readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        // ------------------------------------------------------------------------
        // Initialization
        // ------------------------------------------------------------------------
        static ModAudioManager()
        {
            // Default all groups to full volume, unmuted
            foreach (AudioGroup group in System.Enum.GetValues(typeof(AudioGroup)))
            {
                _groupVolumes[group] = 1.0f;
                _groupMuted[group] = false;
            }
            
            // Star Console default is 50% (less jarring than full volume)
            _groupVolumes[AudioGroup.StarConsole] = 0.5f;
        }

        private static GameObject EnsureAudioRoot()
        {
            if (_audioRoot == null)
            {
                _audioRoot = new GameObject("CinematicShaders_AudioRoot");
                Object.DontDestroyOnLoad(_audioRoot);
                _audioRoot.AddComponent<AudioCoroutineHost>();
            }
            return _audioRoot;
        }

        // ------------------------------------------------------------------------
        // Volume / Mute API (for future UI integration)
        // ------------------------------------------------------------------------
        public static void SetGroupVolume(AudioGroup group, float volume)
        {
            _groupVolumes[group] = Mathf.Clamp01(volume);
            UpdateLoopVolumes();
        }

        public static float GetGroupVolume(AudioGroup group)
        {
            return _groupVolumes.TryGetValue(group, out float vol) ? vol : 1.0f;
        }

        public static void SetGroupMuted(AudioGroup group, bool muted)
        {
            _groupMuted[group] = muted;
            UpdateLoopVolumes();
        }

        public static bool GetGroupMuted(AudioGroup group)
        {
            return _groupMuted.TryGetValue(group, out bool muted) && muted;
        }

        public static void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            UpdateLoopVolumes();
        }

        public static float GetMasterVolume() => _masterVolume;

        public static void SetMasterMuted(bool muted)
        {
            _masterMuted = muted;
            UpdateLoopVolumes();
        }

        public static bool GetMasterMuted() => _masterMuted;

        // ------------------------------------------------------------------------
        // Effective volume calculation
        // ------------------------------------------------------------------------
        private static float GetEffectiveVolume(AudioGroup group)
        {
            if (_masterMuted) return 0f;
            if (GetGroupMuted(group)) return 0f;
            return _masterVolume * GetGroupVolume(group);
        }

        // ------------------------------------------------------------------------
        // Clip Loading
        // ------------------------------------------------------------------------
        private static AudioClip LoadClip(string path)
        {
            if (_clipCache.TryGetValue(path, out AudioClip cached))
                return cached;

            AudioClip clip = null;
            if (GameDatabase.Instance != null)
            {
                clip = GameDatabase.Instance.GetAudioClip(path);
            }

            if (clip != null)
            {
                _clipCache[path] = clip;
            }
            else
            {
                ModFileLogger.Log($"[ModAudioManager] Failed to load clip: {path}");
            }

            return clip;
        }

        // ------------------------------------------------------------------------
        // One-Shot Playback
        // ------------------------------------------------------------------------
        /// <summary>
        /// Plays a one-shot UI sound.
        /// </summary>
        /// <param name="group">Audio group for volume/mute control</param>
        /// <param name="path">GameDatabase path (without extension)</param>
        /// <param name="predicate">Optional gate. If false, playback is silently skipped.</param>
        /// <param name="volumeScale">Additional per-play volume multiplier</param>
        public static void PlayOneShot(AudioGroup group, string path, bool predicate = true, float volumeScale = 1.0f)
        {
            if (!predicate) return;

            float effectiveVolume = GetEffectiveVolume(group) * Mathf.Clamp01(volumeScale);
            if (effectiveVolume <= 0.001f) return;

            AudioClip clip = LoadClip(path);
            if (clip == null) return;

            GameObject go = new GameObject($"CS_Audio_{group}_OneShot");
            go.transform.SetParent(EnsureAudioRoot().transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f; // 2D UI sound
            src.volume = effectiveVolume;
            src.Play();

            Object.Destroy(go, clip.length);
        }

        // ------------------------------------------------------------------------
        // Named Loop Playback
        // ------------------------------------------------------------------------
        /// <summary>
        /// Starts a named looping sound if not already playing.
        /// </summary>
        public static void PlayLoop(AudioGroup group, string path, string loopId, float volumeScale = 1.0f)
        {
            if (_activeLoops.TryGetValue(loopId, out AudioSource existing) && existing != null)
            {
                // Already playing — just refresh volume in case settings changed
                float effectiveVolume = GetEffectiveVolume(group) * Mathf.Clamp01(volumeScale);
                existing.volume = effectiveVolume;
                if (effectiveVolume > 0.001f)
                {
                    StopFade(existing);
                    if (!existing.isPlaying)
                    {
                        existing.Play();
                    }
                }
                else if (existing.isPlaying)
                {
                    StartFade(existing, DefaultFadeOutSeconds, killOnComplete: false);
                }
                return;
            }

            float effectiveVol = GetEffectiveVolume(group) * Mathf.Clamp01(volumeScale);
            if (effectiveVol <= 0.001f) return;

            AudioClip clip = LoadClip(path);
            if (clip == null) return;

            GameObject go = new GameObject($"CS_AudioLoop_{loopId}");
            go.transform.SetParent(EnsureAudioRoot().transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.loop = true;
            src.volume = effectiveVol;
            src.Play();

            _activeLoops[loopId] = src;
        }

        /// <summary>
        /// Stops a named looping sound and destroys its GameObject.
        /// </summary>
        /// <param name="loopId">The loop identifier.</param>
        /// <param name="fadeOutSeconds">Optional fade-out duration in seconds. Zero = immediate hard stop.</param>
        public static void StopLoop(string loopId, float fadeOutSeconds = 0f)
        {
            if (!_activeLoops.TryGetValue(loopId, out AudioSource src) || src == null)
            {
                _activeLoops.Remove(loopId);
                return;
            }

            // Remove from tracking immediately so a new loop with the same ID can start
            _activeLoops.Remove(loopId);

            if (fadeOutSeconds > 0.001f && src.isPlaying)
            {
                StartFade(src, fadeOutSeconds, killOnComplete: true);
                return;
            }

            src.Stop();
            Object.Destroy(src.gameObject);
        }

        private static void StartFade(AudioSource src, float duration, bool killOnComplete)
        {
            if (src == null) return;

            var host = EnsureAudioRoot().GetComponent<AudioCoroutineHost>();
            if (host == null) return;

            StopFade(src);
            _activeFades[src] = host.StartCoroutine(FadeCoroutine(src, duration, killOnComplete));
        }

        private static void StopFade(AudioSource src)
        {
            if (src == null) return;

            if (_activeFades.TryGetValue(src, out Coroutine routine) && routine != null)
            {
                var host = _audioRoot?.GetComponent<AudioCoroutineHost>();
                if (host != null) host.StopCoroutine(routine);
            }
            _activeFades.Remove(src);
        }

        private static System.Collections.IEnumerator FadeCoroutine(AudioSource src, float duration, bool killOnComplete)
        {
            float startVol = src.volume;
            float timer = 0f;
            while (timer < duration)
            {
                if (src == null)
                {
                    _activeFades.Remove(src);
                    yield break;
                }

                timer += Time.deltaTime;
                src.volume = Mathf.Lerp(startVol, 0f, timer / duration);
                yield return null;
            }

            if (src != null)
            {
                src.Stop();
                if (killOnComplete) Object.Destroy(src.gameObject);
            }
            _activeFades.Remove(src);
        }

        /// <summary>
        /// Returns true if the named loop is currently playing.
        /// </summary>
        public static bool IsLoopPlaying(string loopId)
        {
            return _activeLoops.TryGetValue(loopId, out AudioSource src) && src != null && src.isPlaying;
        }

        // ------------------------------------------------------------------------
        // Internal Helpers
        // ------------------------------------------------------------------------
        private static void UpdateLoopVolumes()
        {
            // Re-apply effective volume to all active loops.
            // Since we don't store the original group/volumeScale per loop in this basic
            // implementation, we simply refresh any loops that happen to be active.
            // Future expansion: store group + volumeScale metadata alongside each loop.
            foreach (var kvp in _activeLoops)
            {
                AudioSource src = kvp.Value;
                if (src == null) continue;
                float vol = GetMasterMuted() ? 0f : _masterVolume;
                if (vol <= 0.001f)
                {
                    StartFade(src, DefaultFadeOutSeconds, killOnComplete: false);
                }
                else
                {
                    StopFade(src);
                    src.volume = vol;
                    if (!src.isPlaying) src.Play();
                }
            }
        }
    }
}
