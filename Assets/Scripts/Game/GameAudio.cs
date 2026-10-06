using System.Collections.Generic;
using UnityEngine;

namespace FamiconWars.Game
{
    /// <summary>
    /// Sound effects (Resources/Audio/Se/&lt;name&gt;) and background music (Resources/Audio/Bgm/&lt;track&gt;).
    /// Music cross-fades between tracks; a track whose file is missing is simply silence, so music can be
    /// added later by dropping files in. Volumes follow the settings screen.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        static GameAudio inst;
        static float seVolume = 0.75f, bgmVolume = 0.75f;
        static bool bgmOn = true;

        const int Voices = 12;
        const float BgmLevel = 0.55f;            // music sits under the effects
        const float FadeTime = 0.6f;

        readonly AudioSource[] se = new AudioSource[Voices];
        readonly AudioSource[] bgm = new AudioSource[2];
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        int nextVoice, front;
        string track;
        float fade = 1;                          // 0 -> 1 while the front source fades in and the other out

        static GameAudio I
        {
            get
            {
                if (inst == null)
                {
                    var go = new GameObject("GameAudio");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<GameAudio>();
                }
                return inst;
            }
        }

        void Awake()
        {
            // the scene's camera is made in code without a listener: without one nothing is heard
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            for (int i = 0; i < Voices; i++)
            {
                se[i] = gameObject.AddComponent<AudioSource>();
                se[i].playOnAwake = false; se[i].spatialBlend = 0;
            }
            for (int i = 0; i < 2; i++)
            {
                bgm[i] = gameObject.AddComponent<AudioSource>();
                bgm[i].playOnAwake = false; bgm[i].loop = true; bgm[i].spatialBlend = 0; bgm[i].volume = 0;
            }
        }

        /// <summary>Takes the volumes from the settings (0-100) and whether music is on.</summary>
        public static void Apply(GameSettings s)
        {
            seVolume = Mathf.Clamp01(s.SeVolume / 100f);
            bgmVolume = Mathf.Clamp01(s.BgmVolume / 100f);
            bgmOn = s.BgmEnabled;
        }

        /// <summary>Plays an effect. The same effect is not restarted within 40 ms (a volley stays one sound).</summary>
        public static void Se(string name, float volume = 1f, float pitchJitter = 0.03f)
        {
            if (seVolume <= 0 || string.IsNullOrEmpty(name) || NetConfig_IsServer) return;
            I.PlaySe(name, volume, pitchJitter);
        }

        /// <summary>Switches the music (null = fade out). Asking for the current track does nothing.</summary>
        public static void Music(string name)
        {
            if (NetConfig_IsServer) return;
            I.SetTrack(name);
        }

        static bool NetConfig_IsServer => Application.isBatchMode;

        AudioClip Clip(string path)
        {
            if (!clips.TryGetValue(path, out var c)) { c = Resources.Load<AudioClip>(path); clips[path] = c; }
            return c;
        }

        void PlaySe(string name, float volume, float pitchJitter)
        {
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(name, out var t) && now - t < 0.04f) return;
            var clip = Clip("Audio/Se/" + name);
            if (clip == null) return;
            lastPlayed[name] = now;
            // a free voice, else the one that started longest ago
            AudioSource src = null;
            for (int k = 0; k < Voices && src == null; k++) { var s = se[(nextVoice + k) % Voices]; if (!s.isPlaying) src = s; }
            if (src == null) src = se[nextVoice];
            nextVoice = (System.Array.IndexOf(se, src) + 1) % Voices;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume) * seVolume;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.Play();
        }

        void SetTrack(string name)
        {
            if (name == track) return;
            track = name;
            var clip = string.IsNullOrEmpty(name) ? null : Clip("Audio/Bgm/" + name);
            front = 1 - front;
            var src = bgm[front];
            src.Stop();
            src.clip = clip;
            src.volume = 0;
            if (clip != null) src.Play();
            fade = 0;
        }

        void Update()
        {
            float target = bgmOn ? bgmVolume * BgmLevel : 0;
            if (fade < 1) fade = Mathf.Min(1, fade + Time.unscaledDeltaTime / FadeTime);
            var a = bgm[front]; var b = bgm[1 - front];
            a.volume = a.clip != null ? target * fade : 0;
            b.volume = b.clip != null ? target * (1 - fade) : 0;
            if (fade >= 1 && b.isPlaying) b.Stop();
        }
    }
}
