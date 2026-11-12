using System;
using UnityEngine;

namespace Ooda.Audio
{
    public enum Sfx { Gun, Hit, Blast, MissileLaunch, Click, Commit, Warning, Win, Lose }

    /// <summary>
    /// Every sound the game plays. The sounds are made by Synth when first asked for, so there are no audio assets. One
    /// object that outlives the scenes holds a handful of voices for the short sounds and three loops: your engine (piston
    /// or jet, faster and louder with the throttle), the wind, and nothing else. Sounds out in the world are placed by
    /// hand, not by Unity's 3D audio: fights are hundreds of metres from the camera, so a sound only grows somewhat quieter
    /// with distance and leans left or right. The volume is kept between runs. The self-tests (the bot, the shots) are silent.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        static GameAudio instance;
        static bool quitting;

        const string VolumeKey = "Audio.Volume";
        const int Voices = 14;

        /// <summary>The master volume, 0 to 1, kept between runs.</summary>
        public static float Volume
        {
            get => volume < 0 ? volume = PlayerPrefs.GetFloat(VolumeKey, 0.8f) : volume;
            set { volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(VolumeKey, volume); }
        }
        static float volume = -1;

        AudioClip[] clips;
        AudioClip piston, jet, wind;
        AudioSource[] voices;
        AudioSource engine, air, ui;
        readonly float[] lastPlayed = new float[Enum.GetValues(typeof(Sfx)).Length];
        float engineLevel, engineThrottle, airLevel;
        bool engineJet, silent;
        int hotBefore;

        public static GameAudio Get()
        {
            if (instance != null || quitting) return instance;
            var go = new GameObject("Game audio");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameAudio>();
            return instance;
        }

        void Awake()
        {
            Application.quitting += () => quitting = true;
            // the self-tests fly at thirty times the speed with nobody listening
            foreach (string a in Environment.GetCommandLineArgs())
                if (a == "-dogfightbot" || a == "-netbot" || a == "-batchmode" || a == "-nosound" || a.EndsWith("shots", StringComparison.Ordinal)) silent = true;

            clips = new AudioClip[lastPlayed.Length];
            clips[(int)Sfx.Gun] = Clip("gun", Synth.Gun());
            clips[(int)Sfx.Hit] = Clip("hit", Synth.Hit());
            clips[(int)Sfx.Blast] = Clip("blast", Synth.Blast());
            clips[(int)Sfx.MissileLaunch] = Clip("missile", Synth.MissileLaunch());
            clips[(int)Sfx.Click] = Clip("click", Synth.Click());
            clips[(int)Sfx.Commit] = Clip("commit", Synth.Commit());
            clips[(int)Sfx.Warning] = Clip("warning", Synth.Warning());
            clips[(int)Sfx.Win] = Clip("win", Synth.Win());
            clips[(int)Sfx.Lose] = Clip("lose", Synth.Lose());
            piston = Clip("piston", Synth.PistonLoop());
            jet = Clip("jet", Synth.JetLoop());
            wind = Clip("wind", Synth.WindLoop());

            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++) voices[i] = Source(false);
            ui = Source(false);
            ui.ignoreListenerPause = true;   // the menu's buttons still tick while the battle is paused
            engine = Source(true);
            air = Source(true);
            for (int i = 0; i < lastPlayed.Length; i++) lastPlayed[i] = -10;
        }

        static AudioClip Clip(string name, float[] samples)
        {
            var c = AudioClip.Create(name, samples.Length, 1, Synth.Rate, false);
            c.SetData(samples, 0);
            return c;
        }

        AudioSource Source(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0;   // placed by hand (see the summary)
            s.volume = loop ? 0 : 1;
            return s;
        }

        // ---------- the short sounds ----------

        /// <summary>The least time between two of the same sound, s: a burst of forty packets is not forty bursts.</summary>
        static float Gap(Sfx s) => s == Sfx.Gun ? 0.07f : s == Sfx.Hit ? 0.05f : s == Sfx.Blast ? 0.12f : s == Sfx.Warning ? 1.2f : 0.03f;

        /// <summary>A sound with no place: a button, a fanfare, a warning.</summary>
        public static void Play(Sfx s, float level = 1f, float pitch = 1f) => Get()?.PlayNow(s, level, pitch, 0f, true);

        /// <summary>A sound out in the world: quieter the farther it is from the camera (never silent), and to its side.</summary>
        public static void PlayAt(Sfx s, Vector3 at, float level = 1f, float pitch = 1f)
        {
            var a = Get();
            if (a == null) return;
            var cam = Camera.main;
            float pan = 0;
            if (cam != null)
            {
                Vector3 d = at - cam.transform.position;
                float m = d.magnitude;
                level *= Mathf.Max(0.18f, 1f / (1f + m / 350f));
                if (m > 1f) pan = Mathf.Clamp(Vector3.Dot(cam.transform.right, d / m), -1f, 1f) * 0.7f;
            }
            a.PlayNow(s, level, pitch, pan, false);
        }

        void PlayNow(Sfx s, float level, float pitch, float pan, bool flat)
        {
            if (silent || level <= 0.001f) return;
            float now = Time.unscaledTime;
            if (now - lastPlayed[(int)s] < Gap(s)) return;
            lastPlayed[(int)s] = now;
            AudioSource v = null;
            if (flat) v = ui;
            else
            {
                // a free voice, or failing that the one nearest its end
                float least = float.MaxValue;
                foreach (var c in voices)
                {
                    if (!c.isPlaying) { v = c; break; }
                    float left = c.clip != null ? c.clip.length - c.time : 0;
                    if (left < least) { least = left; v = c; }
                }
            }
            v.pitch = pitch;
            v.panStereo = pan;
            if (flat) v.PlayOneShot(clips[(int)s], level);
            else { v.clip = clips[(int)s]; v.volume = Mathf.Clamp01(level); v.Play(); }
        }

        // ---------- the loops ----------

        /// <summary>
        /// Your engine and the air, set every frame by whoever is on screen: level 0 to 1 (how loud: full while the turn
        /// plays, low while you plan, 0 for none), the throttle 0 to 1 (it runs faster and louder with it), and whether it
        /// is a jet. Left alone for a moment it fades out, so a scene that sets nothing is quiet.
        /// </summary>
        public static void Engine(float level, float throttle, bool isJet, float airLevel = 0f)
        {
            var a = Get();
            if (a == null) return;
            a.engineLevel = level; a.engineThrottle = Mathf.Clamp01(throttle); a.engineJet = isJet; a.airLevel = airLevel;
            a.engineSetFrame = Time.frameCount;
        }
        int engineSetFrame = -10;

        void Update()
        {
            AudioListener.volume = silent ? 0 : Volume;
            AudioListener.pause = Time.timeScale == 0;   // the pause menu stops the engine and the fight's sounds
            if (FindListener() == null) gameObject.AddComponent<AudioListener>();

            // a tick for any button, slider or switch the mouse takes hold of (IMGUI's hot control), wherever it is drawn
            int hot = GUIUtility.hotControl;
            if (hot != 0 && hotBefore == 0) PlayNow(Sfx.Click, 0.35f, 1f, 0f, true);
            hotBefore = hot;

            float dt = Time.unscaledDeltaTime;
            bool set = Time.frameCount - engineSetFrame <= 2;
            float want = set ? engineLevel * (0.45f + 0.55f * engineThrottle) * 0.5f : 0f;
            var clip = engineJet ? jet : piston;
            if (engine.clip != clip) { engine.clip = clip; engine.volume = 0; }
            engine.volume = Mathf.MoveTowards(engine.volume, want, dt * 0.8f);
            engine.pitch = Mathf.MoveTowards(engine.pitch, (engineJet ? 0.8f : 0.72f) + 0.42f * engineThrottle, dt * 0.5f);
            Run(engine);
            air.clip = wind;
            air.volume = Mathf.MoveTowards(air.volume, set ? airLevel * 0.35f : 0f, dt * 0.5f);
            Run(air);
        }

        static void Run(AudioSource s)
        {
            if (s.volume > 0.001f) { if (!s.isPlaying) s.Play(); }
            else if (s.isPlaying) s.Stop();
        }

        AudioListener listener;
        AudioListener FindListener()
        {
            if (listener == null || !listener.isActiveAndEnabled) listener = FindFirstObjectByType<AudioListener>();
            return listener;
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}
