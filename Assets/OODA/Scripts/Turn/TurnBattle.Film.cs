using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ooda.Flight;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ooda.Turn
{
    /// <summary>
    /// Film mode, for a trailer: -film &lt;folder&gt; with -dogfightbot. The bot flies fights until one has enough kills,
    /// then the replay is played through a scripted director (chase, cockpit, the pilot's face, flyby, over the shoulder,
    /// close on the wing and the tail, along the guns, from the ground and from high above, a slow orbit round each kill)
    /// and every frame is saved as a 1920x1080 JPG at a locked 30 fps, with titles drawn in.
    /// film.csv says per frame what was heard (rounds fired near the camera, hits, kills, rockets, missiles, cuts) for the sound.
    /// -filmkills N: the fewest kills a fight needs to be filmed (3); -filmtries N: the most fights flown looking (14).
    /// -filmkits Rocket,Missiles: every fighter's loadout. -filmintro WORD: open on this word instead of the four OODA words.
    /// -filmend 0: no end card (for a part that another part follows).
    /// </summary>
    public partial class TurnBattle
    {
        string filmDir;
        bool filming;
        int filmTries, filmMaxTries = 14, filmMinKills = 3;
        string filmIntro;
        bool filmEnd = true;
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
            s = Array.IndexOf(args, "-filmintro");
            if (s >= 0 && s + 1 < args.Length) filmIntro = args[s + 1].Replace('_', ' ');
            s = Array.IndexOf(args, "-filmturns");
            if (s >= 0 && s + 1 < args.Length) int.TryParse(args[s + 1], out botTurnLimit);
            s = Array.IndexOf(args, "-filmend");
            if (s >= 0 && s + 1 < args.Length) filmEnd = args[s + 1] != "0";
            s = Array.IndexOf(args, "-filmkits");
            if (s >= 0 && s + 1 < args.Length)
            {
                var kits = args[s + 1].Split(',').Select(x => Enum.TryParse(x.Trim(), true, out Kit k) ? k : Kit.Missiles).ToArray();
                var l = new Loadout(kits[0], kits.Length > 1 ? kits[1] : kits[0]);
                dogSetup.arcade = true;
                dogSetup.oursLoadouts = Enumerable.Repeat(l, dogSetup.ours.Length).ToArray();
                dogSetup.theirsLoadouts = Enumerable.Repeat(l, dogSetup.theirs.Length).ToArray();
                dogSetup.FitLoadouts();
            }
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
            Debug.Log($"OODA film: fight {filmTries} over after {tape.Count} turns with {kills} kills ({blue} by blue, {red} by red), {FilmRockets().Count} rocket burns, {FilmMissiles().Count} missiles");
            if (kills < filmMinKills && filmTries < filmMaxTries) { Restart(); return true; }
            StartCoroutine(Film());
            return true;
        }

        // ---------- what happened, on the tape's clock ----------

        struct FilmKill { public float at; public int shooter, target; public Vector3 pos; }
        struct FilmEvent { public float at; public int who; }

        float TurnStartAt(int turn) { float a = 0; for (int k = 0; k < turn; k++) a += tape[k].Sec; return a; }

        List<FilmKill> FilmKills()
        {
            var list = new List<FilmKill>();
            for (int k = 0; k < tape.Count; k++)
            {
                var t = tape[k];
                float t0 = TurnStartAt(k);
                foreach (var h in t.result.hits.OrderBy(h => h.time))
                    if (h.downed && h.shooter >= 0 && h.shooter < tapeViews.Count && h.target >= 0 && h.target < tapeViews.Count && !list.Any(x => x.target == h.target))
                        list.Add(new FilmKill { at = t0 + (float)(h.time - t.result.startTime), shooter = h.shooter, target = h.target, pos = UnityFrame.Pos(h.pos) });
            }
            return list;
        }

        /// <summary>Each time an aircraft lights its rocket.</summary>
        List<FilmEvent> FilmRockets()
        {
            var list = new List<FilmEvent>();
            float t0 = 0;
            for (int k = 0; k < tape.Count; k++)
            {
                var r = tape[k].result;
                for (int s = 0; s < r.frames.Length; s++)
                {
                    var fr = r.frames[s];
                    if (fr == null) continue;
                    for (int i = 1; i < fr.Length; i++)
                        if (fr[i].rocket && !fr[i - 1].rocket && !fr[i].down)
                            list.Add(new FilmEvent { at = t0 + i * (float)r.dt, who = s });
                }
                t0 += tape[k].Sec;
            }
            return list.OrderBy(e => e.at).ToList();
        }

        /// <summary>Each missile fired.</summary>
        List<FilmEvent> FilmMissiles()
        {
            var list = new List<FilmEvent>();
            float t0 = 0;
            foreach (var t in tape)
            {
                foreach (var tr in t.result.traces)
                    if (tr.missile && tr.owner >= 0 && tr.owner < tapeViews.Count && tr.points.Count > 0)
                        list.Add(new FilmEvent { at = t0 + tr.startFrame * (float)t.result.dt, who = tr.owner });
                t0 += t.Sec;
            }
            return list.OrderBy(e => e.at).ToList();
        }

        /// <summary>Is aircraft s still flying at tape time at?</summary>
        bool FilmAlive(int s, float at)
        {
            if (s < 0 || s >= tapeViews.Count || tapeViews[s] == null) return false;
            int turn = 0;
            while (turn < tape.Count - 1 && at > tape[turn].Sec) { at -= tape[turn].Sec; turn++; }
            var t = tape[turn];
            var fr = s < t.result.frames.Length ? t.result.frames[s] : null;
            if (fr == null) return false;
            return !fr[Mathf.Clamp(Mathf.RoundToInt(at / (float)t.result.dt), 0, fr.Length - 1)].down;
        }

        bool FilmFiring(int s, float at)
        {
            if (s < 0) return false;
            int turn = 0;
            while (turn < tape.Count - 1 && at > tape[turn].Sec) { at -= tape[turn].Sec; turn++; }
            var fr = s < tape[turn].result.frames.Length ? tape[turn].result.frames[s] : null;
            return fr != null && fr[Mathf.Clamp(Mathf.RoundToInt(at / (float)tape[turn].result.dt), 0, fr.Length - 1)].firing;
        }

        int FilmPick(int side, float at, int skip = -1)
        {
            for (int s = 0; s < tapeViews.Count; s++) if (s != skip && (side < 0 || tapeSide[s] == side) && !tapeBomber[s] && FilmAlive(s, at)) return s;
            for (int s = 0; s < tapeViews.Count; s++) if (s != skip && (side < 0 || tapeSide[s] == side) && FilmAlive(s, at)) return s;
            return FirstFollow();
        }

        // ---------- the shots ----------

        enum Cam { Chase, Cockpit, Flyby, Shoulder, Orbit, Wide, Side, Pilot, Wing, Tail, Gun, Ground, High }

        class Shot
        {
            public float from, to, speed = 1;
            public Cam cam;
            public int a, b = -1;            // the aircraft it is on, and the other one (the target)
            public string title, caption;    // a big word in the middle, a line along the bottom
            public bool key;                 // part of a kill or a rocket: other shots give way to it
        }

        List<Shot> FilmShots(List<FilmKill> kills)
        {
            var intro = new List<Shot>();
            var shots = new List<Shot>();
            float end = TapeSec;
            int blue = FilmPick(0, 0), red = FilmPick(1, 0);
            int blue2 = FilmPick(0, 1.5f, blue), red2 = FilmPick(1, 1.5f, red);
            // the opening: both flights on their way in, a word on each cut
            if (filmIntro == null)
            {
                intro.Add(new Shot { from = 0.0f, to = 1.4f, cam = Cam.Side, a = blue, title = "OBSERVE" });
                intro.Add(new Shot { from = 0.4f, to = 1.8f, cam = Cam.Pilot, a = red, title = "ORIENT" });
                intro.Add(new Shot { from = 1.0f, to = 2.4f, cam = Cam.Cockpit, a = blue, b = red, title = "DECIDE" });
                intro.Add(new Shot { from = 1.6f, to = 3.0f, cam = Cam.Flyby, a = blue2, title = "ACT" });
            }
            else
            {
                intro.Add(new Shot { from = 0.0f, to = 1.8f, cam = Cam.High, a = blue, title = filmIntro });
                intro.Add(new Shot { from = 0.6f, to = 2.2f, cam = Cam.Pilot, a = blue, caption = CraftName(blue).ToUpperInvariant() });
                intro.Add(new Shot { from = 1.2f, to = 2.8f, cam = Cam.Flyby, a = red });
            }
            // the aircraft up close: the pilots, the paint, the control surfaces working
            intro.Add(new Shot { from = 2.6f, to = 4.0f, cam = Cam.Wing, a = red, caption = CraftName(red).ToUpperInvariant() });
            intro.Add(new Shot { from = 3.4f, to = 4.8f, cam = Cam.Pilot, a = blue2, caption = CraftName(blue2).ToUpperInvariant() });
            intro.Add(new Shot { from = 4.2f, to = 5.6f, cam = Cam.Tail, a = red2 });
            intro.Add(new Shot { from = 5.0f, to = 6.6f, cam = Cam.Ground, a = blue, caption = $"{dogSetup.ours.Length}  v  {dogSetup.theirs.Length}" });

            // the build-up: quick cuts through the merge before the first kill
            float firstKill = kills.Count > 0 ? kills[0].at : end * 0.4f;
            float t0 = 6.8f, span = Mathf.Max(2f, firstKill - 3.5f - t0);
            var fill = new[] { Cam.Chase, Cam.Gun, Cam.Flyby, Cam.Wing, Cam.Shoulder, Cam.High, Cam.Pilot, Cam.Tail, Cam.Side, Cam.Wide };
            int n = Mathf.Clamp(Mathf.RoundToInt(span / 2.0f), 2, 6);
            for (int k = 0; k < n; k++)
            {
                float a = t0 + span * k / n;
                int side = k % 2;
                int me = FilmPick(side, a + 1, k % 4 >= 2 ? FilmPick(side, a + 1) : -1), them = FilmNearestEnemy(me, a + 1);
                var cam = fill[k % fill.Length];
                if (cam == Cam.Gun && !FilmFiring(me, a + 0.6f)) cam = Cam.Chase;
                shots.Add(new Shot { from = a, to = a + 1.4f, cam = cam, a = me, b = them });
            }

            // the kills: up to six, each a few cuts and a slow look at the moment
            var picked = kills.Count <= 6 ? kills : Enumerable.Range(0, 6).Select(i => kills[i * (kills.Count - 1) / 5]).ToList();
            string[] counts = { "SPLASH ONE", "SPLASH TWO", "SPLASH THREE", "SPLASH FOUR", "SPLASH FIVE", "SPLASH SIX" };
            for (int k = 0; k < picked.Count; k++)
            {
                var kl = picked[k];
                bool lastOne = k == picked.Count - 1;
                string who = $"{CraftName(kl.shooter)} downs {CraftName(kl.target)}".ToUpperInvariant();
                switch (k % 4)
                {
                    case 0:
                        shots.Add(new Shot { key = true, from = kl.at - 3.0f, to = kl.at - 1.1f, cam = Cam.Chase, a = kl.shooter, b = kl.target });
                        shots.Add(new Shot { key = true, from = kl.at - 1.1f, to = kl.at - 0.15f, cam = Cam.Cockpit, a = kl.shooter, b = kl.target });
                        break;
                    case 1:
                        shots.Add(new Shot { key = true, from = kl.at - 2.6f, to = kl.at - 1.0f, cam = Cam.Flyby, a = kl.target, b = kl.shooter });
                        shots.Add(new Shot { key = true, from = kl.at - 1.0f, to = kl.at - 0.15f, cam = Cam.Gun, a = kl.shooter, b = kl.target });
                        break;
                    case 2:
                        shots.Add(new Shot { key = true, from = kl.at - 2.8f, to = kl.at - 1.3f, cam = Cam.Side, a = kl.shooter, b = kl.target });
                        shots.Add(new Shot { key = true, from = kl.at - 1.3f, to = kl.at - 0.15f, cam = Cam.Shoulder, a = kl.shooter, b = kl.target });
                        break;
                    default:
                        shots.Add(new Shot { key = true, from = kl.at - 2.8f, to = kl.at - 1.4f, cam = Cam.Wing, a = kl.shooter, b = kl.target });
                        shots.Add(new Shot { key = true, from = kl.at - 1.4f, to = kl.at - 0.15f, cam = Cam.Cockpit, a = kl.shooter, b = kl.target });
                        break;
                }
                // the hit itself, slowed right down, going round the target
                shots.Add(new Shot { key = true, from = kl.at - 0.15f, to = kl.at + (lastOne ? 0.75f : 0.5f), speed = lastOne ? 0.18f : 0.25f, cam = Cam.Orbit, a = kl.target, b = kl.shooter, caption = $"{counts[k]}  ·  {who}" });
                // and down it goes, or the pilot who got it
                if (k % 2 == 0) shots.Add(new Shot { key = true, from = kl.at + 0.5f, to = kl.at + 1.9f, cam = Cam.Pilot, a = kl.shooter, b = kl.target });
                else shots.Add(new Shot { key = true, from = kl.at + 0.5f, to = kl.at + 2.1f, cam = k % 4 == 1 ? Cam.Ground : Cam.Flyby, a = kl.target, b = kl.shooter });
            }

            // rockets and missiles, where they don't tread on a kill
            bool Free(float a, float b) => !shots.Any(s => s.key && s.from < b && s.to > a);
            int used = 0;
            foreach (var r in FilmRockets())
            {
                if (used >= 3 || r.at < 3 || !Free(r.at - 0.4f, r.at + 2.4f)) continue;
                shots.Add(new Shot { key = true, from = r.at - 0.4f, to = r.at + 0.9f, cam = Cam.Side, a = r.who, caption = used == 0 ? "ROCKET BOOST" : null });
                shots.Add(new Shot { key = true, from = r.at + 0.9f, to = r.at + 2.4f, cam = used % 2 == 0 ? Cam.Pilot : Cam.Chase, a = r.who });
                used++;
            }
            used = 0;
            foreach (var m in FilmMissiles())
            {
                if (used >= 2 || m.at < 3 || !Free(m.at - 0.3f, m.at + 2.0f)) continue;
                shots.Add(new Shot { key = true, from = m.at - 0.3f, to = m.at + 2.0f, cam = used == 0 ? Cam.Chase : Cam.Wing, a = m.who, b = FilmNearestEnemy(m.who, m.at), caption = "FOX TWO" });
                used++;
            }
            // fill shots that a kill or a rocket now overlaps are dropped
            shots.RemoveAll(s => !s.key && !Free(s.from, s.to));

            // the end: the winners flying on
            float last = picked.Count > 0 ? picked[picked.Count - 1].at + 2.2f : end - 3;
            last = Mathf.Min(last, Mathf.Max(0, end - 3.2f));
            int win = picked.Count > 0 ? picked[picked.Count - 1].shooter : blue;
            shots.Add(new Shot { from = last, to = last + 1.3f, cam = Cam.Wing, a = win });
            shots.Add(new Shot { from = last + 1.3f, to = last + 3.0f, cam = Cam.Flyby, a = win });

            // play the middle in tape order without going back in time: the slow orbits on the kills keep their moment,
            // everything else is trimmed to fit round them, and what's left too short is dropped
            var ordered = shots.OrderBy(s => s.from).ThenBy(s => s.cam == Cam.Orbit ? 0 : 1).ToList();
            var orbits = ordered.Where(s => s.cam == Cam.Orbit).ToList();
            for (int k = 1; k < orbits.Count; k++) if (orbits[k].from < orbits[k - 1].to) orbits[k].from = orbits[k - 1].to;
            var middle = new List<Shot>(orbits);
            float cursor = 0;
            foreach (var s in ordered)
            {
                if (s.cam == Cam.Orbit) continue;
                s.from = Mathf.Max(s.from, cursor);
                foreach (var o in orbits) if (s.from >= o.from - 1e-3f && s.from < o.to) s.from = o.to;   // (orbits are in order)
                foreach (var o in orbits) if (o.from > s.from && o.from < s.to) { s.to = o.from; break; }
                if (s.to - s.from < 0.5f) continue;
                middle.Add(s);
                cursor = s.to;
            }
            middle = middle.Where(s => s.to - s.from > 0.1f).OrderBy(s => s.from).ToList();
            var all = intro.Concat(middle).ToList();
            foreach (var s in all) { s.from = Mathf.Clamp(s.from, 0, end); s.to = Mathf.Clamp(s.to, s.from, end); }
            return all.Where(s => s.to - s.from > 0.1f && s.a >= 0).ToList();
        }

        string CraftName(int s) => s >= 0 && s < tapePlanes.Count ? tapePlanes[s].numbers.name : "?";

        int FilmNearestEnemy(int me, float at)
        {
            int best = -1; float bd = float.PositiveInfinity;
            if (me < 0) return best;
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
        float orbitAngle, shotSpan, shotTail;
        bool shotMirror;
        Text filmTitle, filmCaption, filmEndBig, filmEndSmall;
        Image filmBlack;
        Canvas filmCanvas;

        IEnumerator Film()
        {
            filming = true;
            var kills = FilmKills();
            var shots = FilmShots(kills);
            Debug.Log($"OODA film: filming a fight of {tape.Count} turns ({TapeSec:F1} s), {kills.Count} kills, {shots.Count} shots");
            File.WriteAllText(Path.Combine(filmDir, "kills.txt"), string.Join("\n", kills.Select(k => $"{k.at:F2} {TapeName(k.shooter)} -> {TapeName(k.target)}")));
            File.WriteAllText(Path.Combine(filmDir, "shots.txt"), string.Join("\n", shots.Select(s => $"{s.from:F2}-{s.to:F2} {s.cam} {TapeName(s.a)} {(s.b >= 0 ? TapeName(s.b) : "")} x{s.speed} {s.title}{s.caption}")));

            StartReplay();
            if (view != null) view.enabled = false;
            if (gauge != null) gauge.gameObject.SetActive(false);
            var cam = view.Cam;
            filmRt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            filmTex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            cam.targetTexture = filmRt;
            MakeFilmCanvas(cam);
            Time.captureFramerate = 30;
            filmCsv.AppendLine("frame,shot,cam,speed,guns,hits,kills,title,rockets,missiles");

            for (int k = 0; k < shots.Count; k++)
            {
                shot = shots[k];
                yield return StartCoroutine(FilmShot(k));
            }
            if (filmEnd)
            {
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
                    SaveFrame(-1, "end", 1, 0, 0, 0, 0, 0, f == 8 ? "endcard" : "");
                    yield return null;
                }
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
            Ooda.Fx.ToonFx.ClearAll();
            SeekTape(Mathf.Max(0, shot.from - 0.8f));
            replayPaused = false;
            Follow(shot.a);
            if (view != null) view.focus = shot.cam == Cam.Cockpit ? TurnCamera.Focus.Cockpit : TurnCamera.Focus.Player;
            FilmMeasure(FilmT(shot.a));
            shotMirror = ((index * 7) % 3) == 0;
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
                yield return new WaitForEndOfFrame();
                float now = TapeAt;
                FilmSounds(prevAt, now, out int guns, out int hits, out int kills, out int rockets, out int missiles);
                prevAt = now;
                SaveFrame(index, shot.cam.ToString(), s, guns, hits, kills, rockets, missiles, shotFrame == 0 && shot.title != null ? shot.title : "");
                shotFrame++;
                yield return null;
            }
            Time.timeScale = 1;
        }

        void SaveFrame(int shotNo, string camName, float speed, int guns, int hits, int kills, int rockets, int missiles, string title)
        {
            var was = RenderTexture.active;
            RenderTexture.active = filmRt;
            filmTex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false);
            RenderTexture.active = was;
            File.WriteAllBytes(Path.Combine(filmDir, $"f{filmFrame:D5}.jpg"), filmTex.EncodeToJPG(93));
            filmCsv.AppendLine($"{filmFrame},{shotNo},{camName},{speed:F3},{guns},{hits},{kills},{title.Replace(',', ' ')},{rockets},{missiles}");
            filmFrame++;
        }

        /// <summary>What the camera would hear between two moments: rounds fired near it, hits, kills, rockets lit and missiles fired (for the sound track).</summary>
        void FilmSounds(float from, float to, out int guns, out int hits, out int kills, out int rockets, out int missiles)
        {
            guns = hits = kills = rockets = missiles = 0;
            if (to <= from) return;
            Vector3 ear = view.transform.position;
            float t0 = 0;
            for (int k = 0; k < tape.Count; k++)
            {
                var t = tape[k];
                float t1 = t0 + t.Sec;
                if (t1 >= from && t0 <= to)
                {
                    float dt = (float)t.result.dt;
                    foreach (var tr in t.result.traces)
                    {
                        if (tr.points.Count == 0) continue;
                        float at = t0 + tr.startFrame * dt;
                        if (at <= from || at > to) continue;
                        if (tr.missile) { if (Vector3.Distance(UnityFrame.Pos(tr.points[0]), ear) < 1500) missiles++; }
                        else if (Vector3.Distance(UnityFrame.Pos(tr.points[0]), ear) < 900) guns++;
                    }
                    foreach (var h in t.result.hits)
                    {
                        float at = t0 + (float)(h.time - t.result.startTime);
                        if (at <= from || at > to) continue;
                        hits++;
                        if (h.downed) kills++;
                    }
                    for (int s = 0; s < t.result.frames.Length; s++)
                    {
                        var fr = t.result.frames[s];
                        if (fr == null) continue;
                        int i0 = Mathf.Max(1, Mathf.CeilToInt((from - t0) / dt)), i1 = Mathf.Min(fr.Length - 1, Mathf.FloorToInt((to - t0) / dt));
                        for (int i = i0; i <= i1; i++)
                            if (fr[i].rocket && !fr[i - 1].rocket && Vector3.Distance(UnityFrame.Pos(fr[i].pos), ear) < 1500) rockets++;
                    }
                }
                t0 = t1;
            }
        }

        // ---------- the camera ----------

        void FilmLateUpdate()
        {
            if (filming && shot != null) FilmCamera();
        }

        Transform FilmT(int s) => s >= 0 && s < tapeViews.Count && tapeViews[s] != null ? tapeViews[s].transform : null;

        /// <summary>The aircraft's half span and how far its tail reaches, in its own frame (for the close-ups).</summary>
        void FilmMeasure(Transform me)
        {
            shotSpan = 6; shotTail = 5;
            if (me == null) return;
            float x = 0, zMin = 0;
            bool any = false;
            foreach (var r in me.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled) continue;
                var b = r.bounds;
                if (b.size.magnitude > 60) continue;
                for (int c = 0; c < 8; c++)
                {
                    var w = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var l = me.InverseTransformPoint(w);
                    x = Mathf.Max(x, Mathf.Abs(l.x)); zMin = Mathf.Min(zMin, l.z);
                    any = true;
                }
            }
            if (!any) return;
            // (an AABB swells as the aircraft banks: keep it in reason)
            shotSpan = Mathf.Clamp(x * 0.85f, 4, 12); shotTail = Mathf.Clamp(-zMin * 0.9f, 3, 14);
        }

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
            float side = shotMirror ? -1 : 1;

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
                case Cam.Pilot:
                {
                    // close on the canopy from the side, a little ahead, on the pilot's face
                    var eye = FindDeep(me, "CockpitCamera");
                    if (eye == null) goto case Cam.Chase;
                    cam.nearClipPlane = 0.05f;
                    filmCanvas.planeDistance = 0.06f;
                    Vector3 head = eye.position;
                    Vector3 pos = head + me.right * (2.6f * side) + me.forward * 1.1f + me.up * 0.5f;
                    cam.fieldOfView = 46f;
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(head - me.up * 0.2f - pos, me.up));
                    return;
                }
                case Cam.Wing:
                {
                    // riding on the fuselage behind the wing root, out along the wing to the aileron
                    cam.nearClipPlane = 0.1f;
                    filmCanvas.planeDistance = 0.11f;
                    Vector3 pos = p + me.right * (shotSpan * 0.18f * side) - me.forward * (shotTail * 0.45f) + me.up * (shotSpan * 0.28f);
                    Vector3 at = p + me.right * (shotSpan * 0.7f * side) - me.forward * (shotSpan * 0.08f);
                    cam.fieldOfView = 58f;
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(at - pos, me.up));
                    return;
                }
                case Cam.Tail:
                {
                    // above and beside the tail, looking back at the elevators and rudder
                    cam.nearClipPlane = 0.1f;
                    filmCanvas.planeDistance = 0.11f;
                    Vector3 pos = p - me.forward * (shotTail * 0.25f) + me.up * (shotSpan * 0.35f) + me.right * (shotSpan * 0.32f * side);
                    Vector3 at = p - me.forward * shotTail * 0.95f + me.up * 0.6f;
                    cam.fieldOfView = 55f;
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(at - pos, me.up));
                    return;
                }
                case Cam.Gun:
                {
                    // under the wing, looking forward along the guns, the tracers streaming away
                    cam.nearClipPlane = 0.1f;
                    filmCanvas.planeDistance = 0.11f;
                    Vector3 pos = p + me.right * (shotSpan * 0.42f * side) - me.up * (shotSpan * 0.12f) - me.forward * (shotSpan * 0.2f);
                    Vector3 at = p + me.forward * 120f;
                    if (other != null && Vector3.Dot((other.position - p).normalized, fwd) > 0.7f) at = Vector3.Lerp(at, other.position, 0.4f);
                    cam.fieldOfView = 62f;
                    view.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(at - pos, me.up));
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
                        Vector3 sd = Vector3.Cross(Vector3.up, vel).normalized;
                        camFix = p + vel * 1.1f + sd * (16f * side) + Vector3.up * -4f;
                    }
                    float d = Vector3.Distance(camFix, p);
                    cam.fieldOfView = Mathf.Clamp(2 * Mathf.Atan(22f / Mathf.Max(1, d)) * Mathf.Rad2Deg, 14f, 70f);
                    view.transform.SetPositionAndRotation(camFix, Quaternion.LookRotation(p - camFix, Vector3.up));
                    return;
                }
                case Cam.Side:
                {
                    Vector3 sd = Vector3.Cross(Vector3.up, camDir).normalized;
                    Vector3 pos = p + sd * (18f * side) + camDir * 6f + Vector3.up * 2f;
                    cam.fieldOfView = 40f;
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
                case Cam.Ground:
                {
                    // from down on the ground, the fight overhead against the sky and the hills
                    if (first)
                    {
                        Vector3 vel = UnityFrame.Pos(tapeViews[shot.a].Model.state.vel);
                        Vector3 ahead = p + vel * 1.4f;
                        float ground = p.y - 400f;
                        if (Physics.Raycast(ahead + Vector3.up * 50f, Vector3.down, out var hit, 5000f)) ground = hit.point.y;
                        else if (Terrain.activeTerrain != null) ground = Terrain.activeTerrain.SampleHeight(ahead) + Terrain.activeTerrain.transform.position.y;
                        float h = p.y - ground;
                        if (h > 900f) { shot.cam = Cam.High; goto case Cam.High; }
                        Vector3 sd = Vector3.Cross(Vector3.up, vel).normalized;
                        camFix = new Vector3(ahead.x, ground + 6f, ahead.z) + sd * (Mathf.Max(40f, h * 0.25f) * side);
                    }
                    float d = Vector3.Distance(camFix, p);
                    cam.fieldOfView = Mathf.Clamp(2 * Mathf.Atan(28f / Mathf.Max(1, d)) * Mathf.Rad2Deg, 7f, 60f);
                    view.transform.SetPositionAndRotation(camFix, Quaternion.LookRotation(p - camFix, Vector3.up));
                    return;
                }
                case Cam.High:
                {
                    // high above and behind, the world laid out below and the fight in it
                    Vector3 c = FilmCentre(p, out float spread);
                    float dist = Mathf.Clamp(spread * 1.2f + 180f, 220f, 900f);
                    Vector3 sd = Vector3.Cross(Vector3.up, camDir).normalized;
                    Vector3 pos = c - camDir * dist * 0.7f + Vector3.up * dist * 0.75f + sd * (dist * 0.3f * side);
                    if (first) camPos = pos;
                    camPos = Vector3.Lerp(camPos, pos, 1 - Mathf.Exp(-2f * dt));
                    cam.fieldOfView = 55f;
                    view.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(c - camPos, Vector3.up));
                    return;
                }
                case Cam.Wide:
                {
                    // the fight from out to the side, everyone near flying on it
                    Vector3 c = FilmCentre(p, out float spread);
                    Vector3 sd = Vector3.Cross(Vector3.up, camDir).normalized;
                    float dist = Mathf.Clamp(spread * 1.3f + 50f, 70f, 600f);
                    Vector3 pos = c - camDir * dist * 0.5f + sd * (dist * 0.8f * side) + Vector3.up * dist * 0.2f;
                    if (first) camPos = pos;
                    camPos = Vector3.Lerp(camPos, pos, 1 - Mathf.Exp(-3f * dt));
                    cam.fieldOfView = 50f;
                    view.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(c - camPos, Vector3.up));
                    return;
                }
            }
        }

        Vector3 FilmCentre(Vector3 p, out float spread)
        {
            Vector3 c = Vector3.zero; int n = 0;
            for (int s = 0; s < tapeViews.Count; s++)
                if (tapeViews[s] != null && !ReplayDown(s) && (tapeViews[s].transform.position - p).magnitude < 800) { c += tapeViews[s].transform.position; n++; }
            c = n > 0 ? c / n : p;
            spread = 0;
            for (int s = 0; s < tapeViews.Count; s++)
                if (tapeViews[s] != null && !ReplayDown(s) && (tapeViews[s].transform.position - p).magnitude < 800) spread = Mathf.Max(spread, (tapeViews[s].transform.position - c).magnitude);
            return c;
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
            filmCaption = MakeText("Caption", 60, TextAnchor.LowerLeft, new Vector2(0.05f, 0.07f), new Vector2(0.95f, 0.2f), Color.white);
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
