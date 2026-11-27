using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Ooda.App
{
    /// <summary>
    /// The frame-time probe: where a frame's time goes while the game plays itself. On the built game's command line,
    /// <c>-perfprobe &lt;report.txt&gt;</c> (with <c>-dogfightbot 1</c> and the usual -dogfight… picks) records every frame
    /// for <c>-perfseconds</c> (90 by default, after 6 s to settle) and writes a report, split by what the battle was
    /// doing (planning with the stick moving, playback, the rest): frame time (mean, 50th, 95th and 99th percentile,
    /// the worst, hitches over 33 ms), the CPU main and render threads and the GPU, the garbage made per frame and
    /// the collections, and the draw calls, batches, set-pass calls, triangles and shadow casters. Add
    /// <c>-perfmarkers</c> to also time every profiler marker and list the costliest (a development build gives the
    /// most; the markers cost a little, so read frame times from a run without it). Then the game quits.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class PerfProbe : MonoBehaviour
    {
        /// <summary>What the game is doing now, for the report's sections (the battle sets it each frame).</summary>
        public static string Section;
        public static bool Active { get; private set; }

        string path;
        float seconds = 90f, settle = 6f, startAt = -1f, upAt = -1f;
        bool markers;
        int gc0;

        ProfilerRecorder mainThread, renderThread, gcAlloc, batches, setPass, drawCalls, tris, verts, shadowCasters;
        readonly List<(string name, ProfilerRecorder rec)> marks = new List<(string, ProfilerRecorder)>();
        readonly FrameTiming[] timing = new FrameTiming[1];

        sealed class Stats
        {
            public readonly List<float> frame = new List<float>(8192), main = new List<float>(8192), render = new List<float>(8192), gpu = new List<float>(8192);
            public double gc, batches, setPass, draws, tris, verts, casters;
            public int frames, gcFrames;
            public double[] markSum;
        }
        readonly Dictionary<string, Stats> sections = new Dictionary<string, Stats>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-perfprobe");
            if (at < 0 || Active) return;
            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<PerfProbe>();
            p.path = at + 1 < args.Length && !args[at + 1].StartsWith("-") ? args[at + 1] : "PerfReport.txt";
            int s = Array.IndexOf(args, "-perfseconds");
            if (s >= 0 && s + 1 < args.Length && float.TryParse(args[s + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec)) p.seconds = Mathf.Max(5f, sec);
            p.markers = Array.IndexOf(args, "-perfmarkers") >= 0;
            Active = true;
        }

        void Update()
        {
            // the battle names its section once it is up; the clock starts then
            if (startAt < 0)
            {
                if (Section == null) return;
                upAt = Time.realtimeSinceStartup;   // the load: launch to the battle up
                startAt = Time.unscaledTime + settle;
                return;
            }
            if (Time.unscaledTime < startAt) return;
            if (!mainThread.Valid) { StartRecorders(); return; }   // record from the next frame on

            FrameTimingManager.CaptureFrameTimings();
            float gpuMs = FrameTimingManager.GetLatestTimings(1, timing) > 0 ? (float)timing[0].gpuFrameTime : -1f;

            var st = Get(Section ?? "other");
            st.frames++;
            st.frame.Add(Time.unscaledDeltaTime * 1000f);
            st.main.Add(mainThread.LastValue * 1e-6f);
            st.render.Add(renderThread.Valid ? renderThread.LastValue * 1e-6f : -1f);
            if (gpuMs > 0) st.gpu.Add(gpuMs);
            long g = gcAlloc.Valid ? gcAlloc.LastValue : 0;
            st.gc += g; if (g > 0) st.gcFrames++;
            st.batches += batches.LastValue; st.setPass += setPass.LastValue; st.draws += drawCalls.LastValue;
            st.tris += tris.LastValue; st.verts += verts.LastValue; st.casters += shadowCasters.LastValue;
            if (markers)
            {
                st.markSum ??= new double[marks.Count];
                for (int k = 0; k < marks.Count; k++) st.markSum[k] += marks[k].rec.LastValue;
            }

            if (Time.unscaledTime >= startAt + seconds) { Write(); Quit(); }
        }

        Stats Get(string name)
        {
            if (!sections.TryGetValue(name, out var s)) sections[name] = s = new Stats();
            return s;
        }

        void StartRecorders()
        {
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1);
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count", 1);
            shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count", 1);
            gc0 = GC.CollectionCount(0);
            if (!markers) return;
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                if ((d.Flags & MarkerFlags.Counter) != 0) continue;
                var r = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
                r.Start();
                marks.Add(($"{d.Name} [{d.Category.Name}]", r));
                if (marks.Count >= 4000) break;
            }
        }

        static float Pct(List<float> v, float p)
        {
            if (v.Count == 0) return 0;
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp(Mathf.RoundToInt(p * (s.Count - 1)), 0, s.Count - 1)];
        }

        static string Line(string what, List<float> v)
        {
            if (v.Count == 0 || v.All(x => x < 0)) return $"  {what,-14} n/a";
            v = v.Where(x => x >= 0).ToList();
            return $"  {what,-14} mean {v.Average(),6:F2}  p50 {Pct(v, 0.5f),6:F2}  p95 {Pct(v, 0.95f),6:F2}  p99 {Pct(v, 0.99f),6:F2}  worst {v.Max(),7:F2} ms";
        }

        void Write()
        {
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine($"Furball frame-time probe, {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"  args: {string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}");
            sb.AppendLine($"  {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {SystemInfo.processorType} x{SystemInfo.processorCount}, {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"  {Screen.width}x{Screen.height} {(Screen.fullScreen ? "full screen" : "windowed")}, vSync {QualitySettings.vSyncCount}, target {Application.targetFrameRate}, graphics {Ooda.Fx.ToonLook.Level}, development {Debug.isDebugBuild}");
            sb.AppendLine(string.Format(ci, "  battle up {0:F1} s after launch; memory {1} MB allocated, {2} MB reserved, {3} MB graphics driver",
                upAt, UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576, UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576,
                UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576));
            sb.AppendLine($"  GC collections (gen 0) during the run: {GC.CollectionCount(0) - gc0}; heap now {GC.GetTotalMemory(false) / 1048576} MB");
            sb.AppendLine();
            foreach (var kv in sections.OrderByDescending(k => k.Value.frames))
            {
                var s = kv.Value; int n = Math.Max(1, s.frames);
                int hitches = s.frame.Count(f => f > 33.4f);
                sb.AppendLine($"[{kv.Key}] {s.frames} frames, {1000.0 / s.frame.Average():F1} fps on average, {hitches} hitches over 33 ms ({100.0 * hitches / n:F1}%)");
                sb.AppendLine(Line("frame", s.frame));
                sb.AppendLine(Line("CPU main", s.main));
                sb.AppendLine(Line("CPU render", s.render));
                sb.AppendLine(Line("GPU", s.gpu));
                sb.AppendLine(string.Format(ci, "  garbage        {0:F1} KB a frame on average, made in {1:F0}% of frames", s.gc / n / 1024, 100.0 * s.gcFrames / n));
                sb.AppendLine(string.Format(ci, "  per frame      {0:F0} draw calls, {1:F0} batches, {2:F0} set-pass calls, {3:F0}k triangles, {4:F0}k vertices, {5:F0} shadow casters",
                    s.draws / n, s.batches / n, s.setPass / n, s.tris / n / 1000, s.verts / n / 1000, s.casters / n));
                if (s.markSum != null)
                {
                    sb.AppendLine("  costliest markers (mean ms a frame, inclusive, any thread):");
                    foreach (var (k, ms) in Enumerable.Range(0, marks.Count).Select(k => (k, s.markSum[k] / n * 1e-6)).OrderByDescending(x => x.Item2).Take(70))
                        if (ms >= 0.02) sb.AppendLine(string.Format(ci, "    {0,8:F3}  {1}", ms, marks[k].name));
                }
                sb.AppendLine();
            }
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString());
                Debug.Log($"OODA perf: report written to {Path.GetFullPath(path)}\n{sb}");
            }
            catch (Exception e) { Debug.LogError($"OODA perf: could not write {path}: {e.Message}\n{sb}"); }
        }

        void Quit()
        {
            enabled = false;
            foreach (var r in new[] { mainThread, renderThread, gcAlloc, batches, setPass, drawCalls, tris, verts, shadowCasters }) r.Dispose();
            foreach (var m in marks) m.rec.Dispose();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
