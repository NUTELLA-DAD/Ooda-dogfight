using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ooda.Flight;
using UnityEngine;
using UnityEngine.UI;

namespace Ooda.Turn
{
    /// <summary>
    /// Film mode, for a trailer: -film &lt;folder&gt; with -dogfightbot. The bot flies fights until one has enough kills
    /// on both sides, then the replay is played through a scripted director (chase, cockpit, flyby, over the shoulder,
    /// a slow orbit round each kill) and every frame is saved as a 1920x1080 JPG at a locked 30 fps, with titles drawn in.
    /// film.csv says per frame what was heard (rounds fired near the camera, hits, kills, cuts) for the sound.
    /// -filmkills N: the fewest kills a fight needs to be filmed (3); -filmtries N: the most fights flown looking (14).
    /// </summary>
    public partial class TurnBattle
    {
        string filmDir;
        bool filming;
        int filmTries, filmMaxTries = 14, filmMinKills = 3;
        RenderTexture filmRt;
        Texture2D filmTex;
        int filmFrame;
        readonly StringBuilder filmCsv = new StringBuilder();

        /// <summary>From BotSetUp: is this a film run?</summary>
        void FilmSetUp(string[] args)
        {
            int s = Array.IndexOf(args, "-film");
            if (s < 0 || s + 1 >= args.Length) return;
            filmDir = args[s + 1];
            Directory.CreateDirectory(filmDir);
            s = Array.IndexOf(args, "-filmkills");
            if (s >= 0 && s + 1 < args.Length) int.TryParse(args[s + 1], out filmMinKills);
            s = Array.IndexOf(args, "-filmtries");
            if (s >= 0 && s + 1 < args.Length) int.TryParse(args[s + 1], out filmMaxTries);
            Application.runInBackground = true;
            botFights = 1000;
        }

        /// <summary>From BotUpdate when a fight is over: true when film mode took it (filming it, or flying another).</summary>
        bool FilmFightOver()
        {
            if (filmDir == null) return false;
            if (filming) return true;
            filmTries++;
            var all = FilmKills();
            int kills = all.Count, blue = all.Count(k => tapeSide[k.shooter] == 0), red = kills - blue;
            Debug.Log($"OODA film: fight {filmTries} over after {tape.Count} turns with {kills} kills ({blue} by blue, {red} by red)");
            if (kills < filmMinKills && filmTries < filmMaxTries) { Restart(); return true; }
            StartCoroutine(Film());
            return true;
        }

        // ---------- what happened, on the tape's clock ----------

        struct FilmKill { public float at; public int shooter, target; public Vector3 pos; }

        float TurnStartAt(int turn) { float a = 0; for (int k = 0; k < turn; k++) a += tape[k].Sec; return a; }

        List<FilmKill> FilmKills()
        {
            var list = new List<FilmKill>();
            for (int k = 0; k < tape.Count; k++)
            {
                var t = tape[k];
                var hp = (double[])t.hpBefore.Clone();
                float t0 = TurnStartAt(k);
                foreach (var h in t.result.hits.OrderBy(h => h.time))
                {
                    if (h.target < 0 || h.target >= hp.Length || hp[h.target] <= 0) continue;
                    hp[h.target] -= h.damage;
                    if (hp[h.target] <= 0 && h.shooter >= 0)
                        list.Add(new FilmKill { at = t0 + (float)(h.time - t.result.startTime), shooter = h.shooter, target = h.target, pos = UnityFrame.Pos(h.pos) });
                }
            }
            return list;
        }

        /// <summary>Is aircraft s still flying at tape time at?</summary>
        bool FilmAlive(int s, float at)
        {
            int turn = 0;
            while (turn < tape.Count - 1 && at > tape[turn].Sec) { at -= tape[turn].Sec; turn++; }
            var t = tape[turn];
            var fr = s < t.result.frames.Length ? t.result.frames[s] : null;
            if (fr == null || tapeViews[s] == null) return false;
            return !fr[Mathf.Clamp(Mathf.RoundToInt(at / (float)t.result.dt), 0, fr.Length - 1)].down;
        }

        int FilmPick(int side, float at, int skip = -1)
        {
            for (int s = 0; s < tapeViews.Count; s++) if (s != skip && (side < 0 || tapeSide[s] == side) && FilmAlive(s, at)) return s;
            return FirstFollow();
        }

        // ---------- the shots ----------

        enum Cam { Chase, Cockpit, Flyby, Shoulder, Orbit, Wide, Side }

        class Shot
        {
            public float from, to, speed = 1;
            public Cam cam;
            public int a, b = -1;            // the aircraft it is on, and the other one (the target)
            public string title, caption;    // a big word in the middle, a line along the bottom
        }

        List<Shot> FilmShots(List<FilmKill> kills)
        {
            var shots = new List<Shot>();
            float end = TapeSec;
            int blue = FilmPick(0, 0), red = FilmPick(1, 0);
            // the opening: both flights on their way in, a word on each cut
            shots.Add(new Shot { from = 0.0f, to = 1.4f, cam = Cam.Side, a = blue, title = "OBSERVE" });
            shots.Add(new Shot { from = 0.4f, to = 1.8f, cam = Cam.Chase, a = red, title = "ORIENT" });
            shots.Add(new Shot { from = 1.0f, to = 2.4f, cam = Cam.Cockpit, a = blue, b = red, title = "DECIDE" });
            shots.Add(new Shot { from = 1.6f, to = 3.0f, cam = Cam.Flyby, a = FilmPick(0, 1.6f, blue), title = "ACT" });

            // the build-up: quick cuts through the merge before the first kill
            float firstKill = kills.Count > 0 ? kills[0].at : end * 0.4f;
            float t0 = 3.2f, span = Mathf.Max(2f, firstKill - 3.5f - t0);
            var fill = new[] { Cam.Wide, Cam.Chase, Cam.Cockpit, Cam.Flyby, Cam.Shoulder, Cam.Side };
            int n = Mathf.Clamp(Mathf.RoundToInt(span / 2.5f), 2, 5);
            for (int k = 0; k < n; k++)
            {
                float a = t0 + span * k / n;
                int side = k % 2;
                int me = FilmPick(side, a + 1), them = FilmNearestEnemy(me, a + 1);
                shots.Add(new Shot { from = a, to = a + 1.3f, cam = fill[k % fill.Length], a = me, b = them, caption = k == 0 ? "4  v  4" : null });
            }

            // the kills: up to five, each a few cuts and a slow look at the moment
            var picked = kills.Count <= 5 ? kills : Enumerable.Range(0, 5).Select(i => kills[i * (kills.Count - 1) / 4]).ToList();
            string[] counts = { "SPLASH ONE", "SPLASH TWO", "SPLASH THREE", "SPLASH FOUR", "SPLASH FIVE" };
            for (int k = 0; k < picked.Count; k++)
            {
                var kl = picked[k];
                bool lastOne = k == picked.Count - 1;
                string who = $"{CraftName(kl.shooter)} downs {CraftName(kl.target)}".ToUpperInvariant();
                switch (k % 3)
                {
                    case 0:
                        shots.Add(new Shot { from = kl.at - 3.0f, to = kl.at - 1.1f, cam = Cam.Chase, a = kl.shooter, b = kl.target });
                        shots.Add(new Shot { from = kl.at - 1.1f, to = kl.at - 0.15f, cam = Cam.Cockpit, a = kl.shooter, b = kl.target });
                        break;
                    case 1:
                        shots.Add(new Shot { from = kl.at - 2.6f, to = kl.at - 1.0f, cam = Cam.Flyby, a = kl.target, b = kl.shooter });
                        shots.Add(new Shot { from = kl.at - 1.0f, to = kl.at - 0.15f, cam = Cam.Shoulder, a = kl.shooter, b = kl.target });
                        break;
                    default:
                        shots.Add(new Shot { from = kl.at - 2.8f, to = kl.at - 1.3f, cam = Cam.Side, a = kl.shooter, b = kl.target });
                        shots.Add(new Shot { from = kl.at - 1.3f, to = kl.at - 0.15f, cam = Cam.Cockpit, a = kl.shooter, b = kl.target });
                        break;
                }
                // the hit itself, slowed right down, going round the target
                shots.Add(new Shot { from = kl.at - 0.15f, to = kl.at + (lastOne ? 0.75f : 0.5f), speed = lastOne ? 0.18f : 0.25f, cam = Cam.Orbit, a = kl.target, b = kl.shooter, caption = $"{counts[k]}  ·  {who}" });
                // and down it goes
                shots.Add(new Shot { from = kl.at + 0.5f, to = kl.at + 2.1f, cam = k % 2 == 0 ? Cam.Flyby : Cam.Chase, a = kl.target, b = kl.shooter });
            }

            // the end: the winners flying on, then the name
            float last = picked.Count > 0 ? picked[picked.Count - 1].at + 2.2f : end - 3;
            last = Mathf.Min(last, Mathf.Max(0, end - 2.6f));
            int win = picked.Count > 0 ? picked[picked.Count - 1].shooter : blue;
            shots.Add(new Shot { from = last, to = last + 2.4f, cam = Cam.Chase, a = win });
            foreach (var s in shots) { s.from = Mathf.Clamp(s.from, 0, end); s.to = Mathf.Clamp(s.to, s.from, end); }
            return shots.Where(s => s.to - s.from > 0.1f).ToList();
        }

        string CraftName(int s) => s >= 0 && s < tapePlanes.Count ? tapePlanes[s].numbers.name.Replace(" G-6", "").Replace(" IX", "").Replace("P-51D ", "") : "?";

        int FilmNearestEnemy(int me, float at)
        {
            int best = -1; float bd = float.PositiveInfinity;
            for (int s = 0; s < tapeViews.Count; s++)
            {
                if (s == me || tapeSide[s] == tapeSide[me] || !FilmAlive(s, at)) continue;
                float d = (FilmPos(s, at) - FilmPos(me, at)).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        Vector3 FilmPos(int s, float at)
        {
            int turn = 0;
            while (turn < tape.Count - 1 && at > tape[turn].Sec) { at -= tape[turn].Sec; turn++; }
            var fr = s < tape[turn].result.frames.Length ? tape[turn].result.frames[s] : null;
            if (fr == null) return Vector3.zero;
            return UnityFrame.Pos(fr[Mathf.Clamp(Mathf.RoundToInt(at / (float)tape[turn].result.dt), 0, fr.Length - 1)].pos);
        }

        // ---------- recording ----------

        Shot shot;
        int shotFrame;
        Vector3 camPos, camFix, camDir;
        float orbitAngle;
        Text filmTitle, filmCaption, filmEndBig, filmEndSmall;
        Image filmBlack;
        Canvas filmCanvas;
        Texture2D filmSoft;

        IEnumerator Film()
        {
            filming = true;
            var kills = FilmKills();
            var shots = FilmShots(kills);
            Debug.Log($"OODA film: filming a fight of {tape.Count} turns ({TapeSec:F1} s), {kills.Count} kills, {shots.Count} shots");
            File.WriteAllText(Path.Combine(filmDir, "kills.txt"), string.Join("\n", kills.Select(k => $"{k.at:F2} {TapeName(k.shooter)} -> {TapeName(k.target)}")));

            StartReplay();
            if (view != null) view.enabled = false;
            if (gauge != null) gauge.gameObject.SetActive(false);
            var cam = view.Cam;
            filmRt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            filmTex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            cam.targetTexture = filmRt;
            MakeFilmCanvas(cam);
            Time.captureFramerate = 30;
            filmCsv.AppendLine("frame,shot,cam,speed,guns,hits,kills,title");

            for (int k = 0; k < shots.Count; k++)
            {
                shot = shots[k];
                yield return StartCoroutine(FilmShot(k));
            }
            // the end card: fade to black over the last frame, and the name
            shot = null;
            replayPaused = true;
            for (int f = 0; f < 120; f++)
            {
                filmCanvas.planeDistance = cam.nearClipPlane + 0.01f;
                filmBlack.color = new Color(0, 0, 0, Mathf.Clamp01(f / 10f));
                filmTitle.text = ""; filmCaption.text = "";
                filmEndBig.text = f >= 8 ? "OODA" : "";
                float k = Mathf.Clamp01((f - 8) / 6f);
                filmEndBig.color = new Color(1f, 0.85f, 0.35f, k);
                filmEndBig.transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, 1 - (1 - k) * (1 - k));
                filmEndSmall.text = f >= 30 ? "DOGFIGHT\n\n<size=34>observe  ·  orient  ·  decide  ·  act</size>" : "";
                filmEndSmall.color = new Color(0.85f, 1f, 0.85f, Mathf.Clamp01((f - 30) / 10f));
                yield return new WaitForEndOfFrame();
                SaveFrame(-1, "end", 1, 0, 0, 0, f == 8 ? "endcard" : "");
                yield return null;
            }
            File.WriteAllText(Path.Combine(filmDir, "film.csv"), filmCsv.ToString());
            Debug.Log($"OODA film: {filmFrame} frames written to {filmDir}");
            Time.captureFramerate = 0;
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        IEnumerator FilmShot(int index)
        {
            // a little before the shot, unseen, so the smoke trails are where they should be
            Time.timeScale = 1;
            foreach (var v in tapeViews) if (v != null) foreach (var ps in v.GetComponentsInChildren<ParticleSystem>()) ps.Clear();
            SeekTape(Mathf.Max(0, shot.from - 0.8f));
            replayPaused = false;
            Follow(shot.a);
            if (view != null) view.focus = shot.cam == Cam.Cockpit ? TurnCamera.Focus.Cockpit : TurnCamera.Focus.Player;
            shotFrame = -1;
            while (TapeAt < shot.from - 1e-3f && !replayAtEnd) { FilmCamera(); yield return null; }
            shotFrame = 0;
            FilmCamera();
            float prevAt = TapeAt;
            while (TapeAt < shot.to - 1e-4f && !replayAtEnd)
            {
                // ease into the slow motion over a few frames
                float s = shot.speed < 1 ? Mathf.Lerp(1, shot.speed, Mathf.Clamp01(shotFrame / 5f)) : 1;
                Time.timeScale = s;
                FilmTexts();
                foreach (var fl in flashes) if (fl.t != null) fl.t.gameObject.SetActive(false);   // the sparks stand in for the game's hit balls
                yield return new WaitForEndOfFrame();
                float now = TapeAt;
                FilmSounds(prevAt, now, out int guns, out int hits, out int kills);
                prevAt = now;
                SaveFrame(index, shot.cam.ToString(), s, guns, hits, kills, shotFrame == 0 && shot.title != null ? shot.title : "");
                shotFrame++;
                yield return null;
            }
            Time.timeScale = 1;
        }

        void SaveFrame(int shotNo, string camName, float speed, int guns, int hits, int kills, string title)
        {
            var was = RenderTexture.active;
            RenderTexture.active = filmRt;
            filmTex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false);
            RenderTexture.active = was;
            File.WriteAllBytes(Path.Combine(filmDir, $"f{filmFrame:D5}.jpg"), filmTex.EncodeToJPG(93));
            filmCsv.AppendLine($"{filmFrame},{shotNo},{camName},{speed:F3},{guns},{hits},{kills},{title}");
            filmFrame++;
        }

        /// <summary>What the camera would hear between two moments: rounds fired near it, hits and kills (for the sound track).</summary>
        void FilmSounds(float from, float to, out int guns, out int hits, out int kills)
        {
            guns = hits = kills = 0;
            if (to <= from) return;
            Vector3 ear = view.transform.position;
            float t0 = 0;
            for (int k = 0; k < tape.Count; k++)
            {
                var t = tape[k];
                float t1 = t0 + t.Sec;
                if (t1 >= from && t0 <= to)
                {
                    foreach (var tr in t.result.traces)
                    {
                        if (tr.missile || tr.points.Count == 0) continue;
                        float at = t0 + tr.startFrame * (float)t.result.dt;
                        if (at > from && at <= to && Vector3.Distance(UnityFrame.Pos(tr.points[0]), ear) < 900) guns++;
                    }
                    var hp = (double[])t.hpBefore.Clone();
                    foreach (var h in t.result.hits.OrderBy(h => h.time))
                    {
                        if (h.target < 0 || h.target >= hp.Length) continue;
                        bool alive = hp[h.target] > 0;
                        hp[h.target] -= h.damage;
                        float at = t0 + (float)(h.time - t.result.startTime);
                        if (at <= from || at > to) continue;
                        hits++;
                        if (alive && hp[h.target] <= 0) { kills++; FilmBoom(h.target, UnityFrame.Pos(h.pos), false); }
                        else FilmBoom(-1, UnityFrame.Pos(h.pos), true);
                    }
                }
                t0 = t1;
            }
        }

        /// <summary>A soft round blob, so smoke and fire are puffs rather than squares.</summary>
        Texture2D FilmSoft()
        {
            if (filmSoft != null) return filmSoft;
            const int N = 64;
            filmSoft = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = new Vector2(x - N / 2 + 0.5f, y - N / 2 + 0.5f).magnitude / (N / 2);
                    float a = Mathf.Clamp01(1 - d);
                    filmSoft.SetPixel(x, y, new Color(1, 1, 1, a * a * (3 - 2 * a)));
                }
            filmSoft.Apply();
            return filmSoft;
        }

        /// <summary>Every smoke trail drawn with the soft blob.</summary>
        void FilmSoften()
        {
            var soft = FilmSoft();
            foreach (var v in tapeViews)
            {
                if (v == null) continue;
                foreach (var r in v.GetComponentsInChildren<ParticleSystemRenderer>())
                {
                    var m = r.sharedMaterial;
                    if (m == null || m.mainTexture == soft) continue;
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", soft);
                    m.mainTexture = soft;
                }
            }
        }

        /// <summary>A fireball where an aircraft is shot down: a burst of flame puffs and dark smoke left hanging.</summary>
        void FilmBoom(int target, Vector3 at, bool spark)
        {
            var t = FilmT(target);
            if (t != null) at = t.position;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");

            void Burst(string name, int count, float life, float size, float speed, Gradient grad, bool additive)
            {
                var go = new GameObject(name);
                go.transform.position = at;
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.duration = 0.2f; main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
                main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.maxParticles = count;
                main.stopAction = ParticleSystemStopAction.Destroy;
                var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1f;
                var col = ps.colorOverLifetime; col.enabled = true; col.color = grad;
                var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.5f, 1, 1.8f));
                var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.15f; lv.limit = 2f;
                var r = go.GetComponent<ParticleSystemRenderer>();
                var mat = new Material(shader);
                mat.SetFloat("_Surface", 1);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", FilmSoft());
                mat.mainTexture = FilmSoft();
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                r.material = mat;
                ps.Play();
            }
            Gradient G(Color a, Color b, Color c, float a0, float a1)
            {
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 0.35f), new GradientColorKey(c, 1) },
                          new[] { new GradientAlphaKey(a0, 0), new GradientAlphaKey(a1, 0.4f), new GradientAlphaKey(0, 1) });
                return g;
            }
            if (spark)
            {
                Burst("FilmSpark", 12, 0.3f, 1.6f, 12f, G(Color.white, new Color(1f, 0.8f, 0.4f), new Color(1f, 0.4f, 0.1f), 1f, 0.8f), true);
                return;
            }
            Burst("FilmSmoke", 30, 4f, 6f, 7f, G(new Color(0.25f, 0.2f, 0.17f), new Color(0.13f, 0.12f, 0.11f), new Color(0.3f, 0.3f, 0.3f), 0.85f, 0.6f), false);
            Burst("FilmFire", 50, 1.0f, 4.5f, 16f, G(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.5f, 0.1f), new Color(0.5f, 0.1f, 0.02f), 1f, 0.8f), true);
            Burst("FilmFlash", 3, 0.2f, 9f, 1f, G(Color.white, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.5f, 0.1f), 0.9f, 0.5f), true);
        }

        // ---------- the camera ----------

        void LateUpdate()
        {
            if (filming && shot != null) FilmCamera();
        }

        Transform FilmT(int s) => s >= 0 && s < tapeViews.Count && tapeViews[s] != null ? tapeViews[s].transform : null;

        void FilmCamera()
        {
            var cam = view.Cam;
            var me = FilmT(shot.a);
            if (me == null) return;
            var other = FilmT(shot.b);
            Vector3 p = me.position;
            Vector3 fwd = me.forward;
            float dt = Time.deltaTime;
            bool first = shotFrame <= 0;
            if (first) camDir = fwd;
            camDir = Vector3.Slerp(camDir, fwd, 1 - Mathf.Exp(-5f * dt)).normalized;
            cam.nearClipPlane = 0.3f;
            filmCanvas.planeDistance = 0.31f;
            FilmSoften();

            switch (shot.cam)
            {
                case Cam.Cockpit:
                {
                    var eye = FindDeep(me, "CockpitCamera");
                    if (eye == null) goto case Cam.Chase;
                    cam.nearClipPlane = 0.05f;
                    filmCanvas.planeDistance = 0.06f;
                    cam.fieldOfView = 72f;
                    var look = me.rotation;
                    if (other != null)
                    {
                        // turn the head a little toward the target when it is ahead
                        Vector3 to = (other.position - eye.position).normalized;
                        if (Vector3.Dot(to, me.forward) > 0.5f) look = Quaternion.Slerp(me.rotation, Quaternion.LookRotation(to, me.up), 0.35f);
                    }
                    view.transform.SetPositionAndRotation(eye.position, look);
                    return;
                }
                case Cam.Chase:
                {
                    Vector3 pos = p - camDir * 24f + Vector3.up * 5.5f;
                    Vector3 at = p + camDir * 40f;
                    if (other != null && Vector3.Dot((other.position - p).normalized, camDir) > 0.3f) at = Vector3.Lerp(at, other.position, 0.25f);
                    cam.fieldOfView = 58f;
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(at - pos, Vector3.up));
                    return;
                }
                case Cam.Shoulder:
                {
                    if (other == null) goto case Cam.Chase;
                    Vector3 to = (other.position - p).normalized;
                    Vector3 pos = p - to * 18f + Vector3.up * 4f + Vector3.Cross(Vector3.up, to).normalized * 4f;
                    cam.fieldOfView = Mathf.Clamp(2 * Mathf.Atan(60f / Mathf.Max(1, Vector3.Distance(pos, other.position))) * Mathf.Rad2Deg, 22f, 60f);
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(other.position - pos, Vector3.up));
                    return;
                }
                case Cam.Flyby:
                {
                    // a camera hung in the sky ahead of it, off to one side: it roars past
                    if (first)
                    {
                        Vector3 vel = UnityFrame.Pos(tapeViews[shot.a].Model.state.vel);
                        Vector3 side = Vector3.Cross(Vector3.up, vel).normalized;
                        camFix = p + vel * 1.1f + side * ((shot.from * 7) % 2 < 1 ? 26f : -26f) + Vector3.up * -6f;
                    }
                    float d = Vector3.Distance(camFix, p);
                    cam.fieldOfView = Mathf.Clamp(2 * Mathf.Atan(22f / Mathf.Max(1, d)) * Mathf.Rad2Deg, 14f, 70f);
                    view.transform.SetPositionAndRotation(camFix, Quaternion.LookRotation(p - camFix, Vector3.up));
                    return;
                }
                case Cam.Side:
                {
                    Vector3 side = Vector3.Cross(Vector3.up, camDir).normalized;
                    Vector3 pos = p + side * 30f + camDir * 6f + Vector3.up * 2f;
                    cam.fieldOfView = 50f;
                    Vector3 at = p + camDir * 8f;
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(at - pos, Vector3.up));
                    return;
                }
                case Cam.Orbit:
                {
                    if (first) orbitAngle = Vector3.SignedAngle(Vector3.forward, -camDir, Vector3.up) - 40f;
                    else orbitAngle += 1.4f;   // per film frame: a steady swing round whatever the slow motion
                    Vector3 pos = p + Quaternion.Euler(12f, orbitAngle, 0) * Vector3.forward * 40f;
                    cam.fieldOfView = 50f;
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(p - pos, Vector3.up));
                    return;
                }
                case Cam.Wide:
                {
                    // the fight from out to the side, everyone near flying on it
                    Vector3 c = Vector3.zero; int n = 0;
                    for (int s = 0; s < tapeViews.Count; s++)
                        if (tapeViews[s] != null && !ReplayDown(s) && (tapeViews[s].transform.position - p).magnitude < 800) { c += tapeViews[s].transform.position; n++; }
                    c = n > 0 ? c / n : p;
                    float spread = 0;
                    for (int s = 0; s < tapeViews.Count; s++)
                        if (tapeViews[s] != null && !ReplayDown(s) && (tapeViews[s].transform.position - p).magnitude < 800) spread = Mathf.Max(spread, (tapeViews[s].transform.position - c).magnitude);
                    Vector3 side = Vector3.Cross(Vector3.up, camDir).normalized;
                    float dist = Mathf.Clamp(spread * 1.3f + 50f, 70f, 600f);
                    Vector3 pos = c - camDir * dist * 0.5f + side * dist * 0.8f + Vector3.up * dist * 0.2f;
                    if (first) camPos = pos;
                    camPos = Vector3.Lerp(camPos, pos, 1 - Mathf.Exp(-3f * dt));
                    cam.fieldOfView = 50f;
                    view.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(c - camPos, Vector3.up));
                    return;
                }
            }
        }

        // ---------- the titles ----------

        void MakeFilmCanvas(Camera cam)
        {
            var go = new GameObject("FilmCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.31f;
            filmCanvas = canvas;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;

            filmBlack = new GameObject("Black").AddComponent<Image>();
            filmBlack.transform.SetParent(go.transform, false);
            filmBlack.rectTransform.anchorMin = Vector2.zero; filmBlack.rectTransform.anchorMax = Vector2.one;
            filmBlack.rectTransform.offsetMin = filmBlack.rectTransform.offsetMax = Vector2.zero;
            filmBlack.color = new Color(0, 0, 0, 0);

            Text MakeText(string name, int size, TextAnchor anchor, Vector2 min, Vector2 max, Color color)
            {
                var t = new GameObject(name).AddComponent<Text>();
                t.transform.SetParent(go.transform, false);
                t.font = Ooda.UI.BriefingStyle.Stencil;
                t.fontSize = size;
                t.alignment = anchor;
                t.color = color;
                t.supportRichText = true;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.rectTransform.anchorMin = min; t.rectTransform.anchorMax = max;
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.8f);
                sh.effectDistance = new Vector2(4, -4);
                return t;
            }
            filmTitle = MakeText("Title", 170, TextAnchor.MiddleCenter, new Vector2(0, 0.3f), new Vector2(1, 0.7f), new Color(1f, 0.88f, 0.4f));
            filmCaption = MakeText("Caption", 46, TextAnchor.LowerLeft, new Vector2(0.05f, 0.07f), new Vector2(0.95f, 0.2f), Color.white);
            filmEndBig = MakeText("EndBig", 260, TextAnchor.MiddleCenter, new Vector2(0, 0.42f), new Vector2(1, 0.82f), new Color(1f, 0.85f, 0.35f));
            filmEndSmall = MakeText("EndSmall", 60, TextAnchor.UpperCenter, new Vector2(0, 0.12f), new Vector2(1, 0.42f), Color.white);
        }

        void FilmTexts()
        {
            filmBlack.color = new Color(0, 0, 0, 0);
            filmEndBig.text = filmEndSmall.text = "";
            if (shot.title != null)
            {
                // slams in big and settles
                float k = Mathf.Clamp01(shotFrame / 5f);
                filmTitle.text = shot.title;
                filmTitle.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, 1 - (1 - k) * (1 - k));
                filmTitle.color = new Color(1f, 0.88f, 0.4f, Mathf.Clamp01(shotFrame / 2f));
            }
            else filmTitle.text = "";
            if (shot.caption != null)
            {
                filmCaption.text = shot.caption;
                filmCaption.color = new Color(1, 1, 1, Mathf.Clamp01(shotFrame / 6f));
            }
            else filmCaption.text = "";
        }
    }
}
