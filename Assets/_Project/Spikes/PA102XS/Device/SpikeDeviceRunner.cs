using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using PocketArbor.Spike.Render;
using Unity.Profiling;
using UnityEngine;

namespace PocketArbor.Spike.Device
{
    /// <summary>
    /// PA-102X-S phase 2a device harness (one scene, runs once at start and writes JSON + PNG into
    /// persistentDataPath/pa102xs, then "done.txt"). Scenarios: empty baseline, cold first show (K9), steady-state
    /// with wind per configuration (K1 GPU ms, K2 FPS, K3 GC, K8 memory), cut (K4), daily rebuild (K5),
    /// Chronicle scrubbing without bake (K7), screenshots for K11. Per-frame measuring loops allocate nothing.
    /// </summary>
    public sealed class SpikeDeviceRunner : MonoBehaviour
    {
        public Shader[] shaders;
        public Camera cam;
        public float wind = 0.5f;
        public int sliceFrames = 8;

        private const int MaxFrames = 8192;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly float[] _dt = new float[MaxFrames], _gpu = new float[MaxFrames], _cpu = new float[MaxFrames], _cpuMain = new float[MaxFrames], _cpuRender = new float[MaxFrames];
        private readonly long[] _gcAllocFrame = new long[MaxFrames], _gcHeapDelta = new long[MaxFrames];
        private readonly float[] _work = new float[MaxFrames];
        private readonly FrameTiming[] _ft = new FrameTiming[1];
        private int _n;
        private long _lastHeap;
        private ProfilerRecorder _recGcAlloc, _recGfxUsed, _recGcUsed, _recSysUsed;
        private readonly StringBuilder _json = new StringBuilder(1 << 16);
        private bool _firstSection = true;
        private SketchTreeRenderer _tree;
        private readonly SketchFrame _frame = new SketchFrame();
        private string _dir;
        private float _time;
        private Color _screenBg = new Color(0.93f, 0.92f, 0.9f, 1f);

        private bool Dev => Debug.isDebugBuild;
        private float SteadySeconds => Dev ? 15f : 60f;

        private IEnumerator Start()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
            SketchTreeRenderer.ShaderLookup = FindShader;
            _dir = Path.Combine(Application.persistentDataPath, "pa102xs");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            _recGcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _recGfxUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            _recGcUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            _recSysUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            ApplyScreenCamera();
            Log("start " + SystemInfo.deviceModel + " " + SystemInfo.graphicsDeviceType + " dev=" + Dev);

            _json.Append("{\n");
            Field("device", SystemInfo.deviceModel);
            Field("gpu", SystemInfo.graphicsDeviceName);
            Field("api", SystemInfo.graphicsDeviceType.ToString());
            Field("apiVersion", SystemInfo.graphicsDeviceVersion);
            Field("os", SystemInfo.operatingSystem);
            Field("development", Dev ? "true" : "false", true);
            Field("screen", Screen.width + "x" + Screen.height);
            Field("refreshRate", Screen.currentResolution.refreshRateRatio.value.ToString("0.##", Inv), true);
            Field("quality", QualitySettings.names[QualitySettings.GetQualityLevel()]);
            Field("pipeline", UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "builtin");
            Field("frameTimingGpu", FrameTimingManager.IsFeatureEnabled() ? "true" : "false", true);
            Field("recGcAllocInFrame", _recGcAlloc.Valid ? "true" : "false", true);
            Field("recGfxUsed", _recGfxUsed.Valid ? "true" : "false", true);
            Field("maxTextureSize", SystemInfo.maxTextureSize.ToString(Inv), true);
            Field("supportsRGBAFloatRT", SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ? "true" : "false", true);
            Field("supportsRGBAHalfRT", SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? "true" : "false", true);
            _json.Append("  \"sections\": [\n");

            for (int i = 0; i < 30; i++) yield return null;

            // 1. Empty baseline (camera clear only).
            yield return Measure("baseline_empty", 10f, false, null);

            // 2. Cold first show (K9): shaders/pipelines are created on first draw.
            yield return ColdFirstShow();

            // 3. Steady state with wind (K1, K2, K3, K8) + screenshots (K11).
            string[] species = { "momiji", "kuromatsu", "yamazakura" };
            foreach (string sp in species)
                yield return Steady(sp, "moyogi", 3, 1f, false);
            yield return Steady("momiji", "moyogi", 3, 1f, true);
            yield return Steady("kuromatsu", "moyogi", 3, 1f, true);
            _tree.WoodBuilder.RingSpacingK = 0.55f; // back to quality preset
            _tree.WoodBuilder.RingSpacingMin = 0.45f; _tree.WoodBuilder.RingSpacingMax = 1.8f;
            _tree.WoodBuilder.SideLength = 2.0f; _tree.WoodBuilder.MinSides = 6; _tree.WoodBuilder.MaxSides = 44; _tree.WoodBuilder.SoilStep = 1.6f;

            // Slicing weights from the measured pass costs on this device (first steady configuration).
            if (_passMs != null)
            {
                SdfBaker.SetPassCost((float)Math.Max(_passMs[0], .1), (float)Math.Max(_passMs[1], .1), (float)Math.Max(_passMs[2], .1), (float)Math.Max(_passMs[3], .1));
                Log("slice weights from device: " + _passMs[0].ToString("0.0", Inv) + "/" + _passMs[1].ToString("0.0", Inv) + "/" + _passMs[2].ToString("0.0", Inv) + "/" + _passMs[3].ToString("0.0", Inv));
            }

            // 4. Cut (K4) and 5. daily rebuild (K5).
            yield return CutScenario("momiji", sliceFrames);
            yield return CutScenario("momiji", 24);
            yield return CutScenario("kuromatsu", sliceFrames);
            yield return Growth("momiji", 0.95f, 1f);

            // 6. Chronicle scrubbing without bake (K7).
            yield return Chronicle("momiji");
            yield return Chronicle("kuromatsu");

            _json.Append("\n  ]\n}\n");
            string name = "results_" + SystemInfo.graphicsDeviceType + (Dev ? "_dev" : "") + ".json";
            File.WriteAllText(Path.Combine(_dir, name), _json.ToString());
            File.WriteAllText(Path.Combine(_dir, "done.txt"), name);
            Log("PA102XS_DONE " + name);
        }

        private Shader FindShader(string n)
        {
            if (shaders == null) return null;
            foreach (Shader s in shaders) if (s != null && s.name == n) return s;
            return null;
        }

        private void ApplyScreenCamera()
        {
            SketchCamera.Apply(cam);
            SketchCamera.FitWidth(cam, (float)Screen.width / Screen.height);
            cam.backgroundColor = _screenBg;
        }

        private static void Log(string s) { Debug.Log("PA102XS| " + s); }

        private void Update()
        {
            _time += Time.unscaledDeltaTime;
            SketchGlobals.SetWind(_windNow, _time);
        }

        private float _windNow;
        private double[] _passMs;

        // ------------------------------------------------------------------ scenarios

        private SketchFrame Gen(string sp, string style, int seed, float t)
        {
            TreeGenAdapter.Generate(sp, style, seed, t, _frame);
            return _frame;
        }

        private IEnumerator ColdFirstShow()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _tree = new SketchTreeRenderer(null, HideFlags.None);
            double create = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            SketchTreeRenderer.Stats st = _tree.Show(Gen("momiji", "moyogi", 3, 1f));
            double show = sw.Elapsed.TotalMilliseconds;
            // First frames with the tree: pipeline/shader creation lands here.
            yield return Measure("cold_first_show_momiji", 0f, false, null, 45);
            AppendStats("cold_first_show_momiji_build", st, string.Format(Inv, "\"createMs\": {0:0.0}, \"showSyncMs\": {1:0.0}, \"firstFramesDtMs\": [{2:0.0}, {3:0.0}, {4:0.0}, {5:0.0}]",
                create, show, _dt[0], _dt[1], _dt[2], _dt[3]));
            sw.Restart();
            SketchTreeRenderer.Stats st2 = _tree.Show(Gen("kuromatsu", "moyogi", 3, 1f));
            double show2 = sw.Elapsed.TotalMilliseconds;
            yield return Measure("second_species_show_kuromatsu", 0f, false, null, 45);
            AppendStats("second_species_show_kuromatsu_build", st2, string.Format(Inv, "\"showSyncMs\": {0:0.0}, \"firstFramesDtMs\": [{1:0.0}, {2:0.0}, {3:0.0}, {4:0.0}]",
                show2, _dt[0], _dt[1], _dt[2], _dt[3]));
            // Same species again: no pot rebuild, no new shaders — the steady rebuild cost of a full sync show.
            sw.Restart();
            SketchTreeRenderer.Stats st3 = _tree.Show(Gen("kuromatsu", "moyogi", 3, 1f));
            double show3 = sw.Elapsed.TotalMilliseconds;
            yield return Measure("repeat_show_kuromatsu", 0f, false, null, 20);
            AppendStats("repeat_show_kuromatsu_build", st3, string.Format(Inv, "\"showSyncMs\": {0:0.0}, \"firstFramesDtMs\": [{1:0.0}, {2:0.0}, {3:0.0}, {4:0.0}]",
                show3, _dt[0], _dt[1], _dt[2], _dt[3]));
        }

        private IEnumerator Steady(string sp, string style, int seed, float t, bool budget)
        {
            if (budget) _tree.WoodBuilder.UseBudgetPreset();
            _windNow = 0f;
            SketchTreeRenderer.Stats st = _tree.Show(Gen(sp, style, seed, t));
            string tag = sp + "_" + style + "_s" + seed + "_t" + Mathf.RoundToInt(t * 100) + (budget ? "_budget" : "");
            for (int i = 0; i < 10; i++) yield return null;
            Screenshot("shot_" + tag);
            string loop = GpuLoop(Dev ? 20 : 60);
            for (int i = 0; i < 10; i++) yield return null;
            _windNow = wind;
            float warm = 0f;
            while (warm < 3f) { warm += Time.unscaledDeltaTime; yield return null; }
            yield return Measure("steady_" + tag, SteadySeconds, true, null);
            AppendStats("steady_" + tag + "_build", st, string.Format(Inv, "\"gpuBytesEstimate\": {0}, \"gfxUsedMemory\": {1}, \"gcUsedMemory\": {2}, \"systemUsedMemory\": {3}, {4}",
                _tree.GpuBytesEstimate(), _recGfxUsed.Valid ? _recGfxUsed.LastValue : -1, _recGcUsed.Valid ? _recGcUsed.LastValue : -1, _recSysUsed.Valid ? _recSysUsed.LastValue : -1, loop));
            _windNow = 0f;
        }

        private IEnumerator CutScenario(string sp, int slices)
        {
            _windNow = 0f;
            _tree.Show(Gen(sp, "moyogi", 3, 1f));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _tree.PrepareCut();
            double prep = sw.Elapsed.TotalMilliseconds;
            int leavesBefore = _tree.DisplayedLeafCount;
            for (int i = 0; i < 20; i++) yield return null;
            // Frame 0: cut (geometry gone on the next presented frame), then light over N frames.
            ResetFrames();
            double cutMs = _tree.Cut(0, slices);
            _work[0] = (float)cutMs;
            int frames = 0;
            yield return null;
            Sample();
            while (true)
            {
                var s2 = System.Diagnostics.Stopwatch.StartNew();
                bool swapped = _tree.Tick();
                _work[_n] = (float)s2.Elapsed.TotalMilliseconds;
                frames++;
                yield return null;
                Sample();
                if (swapped || frames > 64) break;
            }
            for (int i = 0; i < 10; i++) { yield return null; Sample(); }
            string perFrame = FrameList(Math.Min(_n, 40));
            if (slices == sliceFrames) Screenshot("shot_cut_" + sp);
            AppendFrames("cut_" + sp + "_n" + slices, string.Format(Inv, "\"prepareLeafGroupsMs\": {0:0.0}, \"cutCpuMs\": {1:0.00}, \"lightFrames\": {2}, \"sliceFramesTarget\": {3}, \"leavesBefore\": {4}, {5}",
                prep, cutMs, frames, slices, leavesBefore, perFrame));
        }

        private IEnumerator Growth(string sp, float t0, float t1)
        {
            _windNow = 0f;
            _tree.Show(Gen(sp, "moyogi", 3, t0));
            for (int i = 0; i < 20; i++) yield return null;
            ResetFrames();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SketchFrame f = Gen(sp, "moyogi", 3, t1);
            double gen = sw.Elapsed.TotalMilliseconds;
            SketchTreeRenderer.Stats st = _tree.BeginRebuild(f, sliceFrames);
            _work[0] = (float)(sw.Elapsed.TotalMilliseconds);
            int frames = 0;
            yield return null;
            Sample();
            while (true)
            {
                var s2 = System.Diagnostics.Stopwatch.StartNew();
                bool swapped = _tree.Tick();
                _work[_n] = (float)s2.Elapsed.TotalMilliseconds;
                frames++;
                yield return null;
                Sample();
                if (swapped || frames > 64) break;
            }
            AppendFrames("growth_" + sp, string.Format(Inv, "\"genMs\": {0:0.0}, \"buildWoodMs\": {1:0.0}, \"buildLeavesMs\": {2:0.0}, \"fillMs\": {3:0.0}, \"uploadMs\": {4:0.0}, \"bakeFrames\": {5}, \"woodTris\": {6}, {7}",
                gen, st.WoodMs, st.LeafMs, st.BakeFillMs, st.UploadMs, frames, st.WoodTris, FrameList(Math.Min(_n, 40))));
        }

        private IEnumerator Chronicle(string sp)
        {
            _windNow = 0f;
            ResetFrames();
            float t = 0f;
            float dur = 10f;
            float elapsed = 0f;
            double genSum = 0, buildSum = 0, genMax = 0, buildMax = 0;
            int frames = 0;
            while (elapsed < dur)
            {
                t = Mathf.Clamp01(elapsed / dur);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                SketchFrame f = Gen(sp, "moyogi", 3, t);
                double g = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                _tree.ShowNoBake(f);
                double b = sw.Elapsed.TotalMilliseconds;
                genSum += g; buildSum += b; genMax = Math.Max(genMax, g); buildMax = Math.Max(buildMax, b);
                if (_n < MaxFrames) _work[_n] = (float)(g + b);
                frames++;
                yield return null;
                Sample();
                elapsed += Time.unscaledDeltaTime;
            }
            AppendFrames("chronicle_" + sp, string.Format(Inv, "\"frames\": {0}, \"genAvgMs\": {1:0.00}, \"genMaxMs\": {2:0.00}, \"buildAvgMs\": {3:0.00}, \"buildMaxMs\": {4:0.00}",
                frames, genSum / frames, genMax, buildSum / frames, buildMax));
            _tree.Show(Gen(sp, "moyogi", 3, 1f));
        }

        // ------------------------------------------------------------------ GPU-bound loop (K1 without GPU timers)

        /// <summary>
        /// Renders the camera <paramref name="iters"/> times into a screen-sized RT and waits for the GPU once at the end:
        /// wall time / iters ~ GPU cost per frame when GPU-bound (CPU submit runs ahead). Parts toggled by renderer.enabled.
        /// </summary>
        private string GpuLoop(int iters)
        {
            var rt = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            ApplyScreenCamera();
            cam.targetTexture = rt;
            string[] parts = { "full", "wood", "leaves", "pot", "empty" };
            var res = new double[parts.Length, 3];
            // Burn-in: let the GPU governor raise clocks before timing.
            SetParts("full");
            for (int i = 0; i < 90; i++) cam.Render();
            UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            for (int rep = 0; rep < 3; rep++)
            for (int k = 0; k < parts.Length; k++)
            {
                int pi = (k + rep * 2) % parts.Length;
                SetParts(parts[pi]);
                cam.Render();
                UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iters; i++) cam.Render();
                UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                res[pi, rep] = sw.Elapsed.TotalMilliseconds / iters;
            }
            SetParts("full");
            cam.targetTexture = null;
            rt.Release();
            Destroy(rt);
            ApplyScreenCamera();
            var sb = new StringBuilder();
            for (int pi = 0; pi < parts.Length; pi++)
            {
                double a = res[pi, 0], b = res[pi, 1], c = res[pi, 2];
                double med = Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
                if (sb.Length > 0) sb.Append(", ");
                sb.AppendFormat(Inv, "\"gpuLoop_{0}_ms\": {1:0.000}, \"gpuLoop_{0}_reps\": [{2:0.00}, {3:0.00}, {4:0.00}]", parts[pi], med, a, b, c);
            }
            // Bake passes over the displayed target (full target each, synced) — informs slicing weights.
            BakeTarget bt = _tree.DisplayedBake;
            double p0 = _tree.Baker.TimePass(bt, 0), p1 = _tree.Baker.TimePass(bt, 1), p2 = _tree.Baker.TimePass(bt, 2), p3 = _tree.Baker.TimePass(bt, 3);
            if (_passMs == null) _passMs = new[] { p0, p1, p2, p3 };
            sb.AppendFormat(Inv, ", \"bakePassMs\": [{0:0.0}, {1:0.0}, {2:0.0}, {3:0.0}]", p0, p1, p2, p3);
            Log("gpuLoop " + sb);
            return sb.ToString();
        }

        private void SetParts(string part)
        {
            _tree.Wood.enabled = part == "full" || part == "wood";
            _tree.Leaves.enabled = part == "full" || part == "leaves";
            _tree.Pot.enabled = part == "full" || part == "pot";
        }

        // ------------------------------------------------------------------ measurement

        private void ResetFrames()
        {
            _n = 0;
            _lastHeap = GC.GetTotalMemory(false);
            FrameTimingManager.CaptureFrameTimings();
        }

        /// <summary>Records the frame that just ended. Allocation-free.</summary>
        private void Sample()
        {
            if (_n >= MaxFrames) return;
            FrameTimingManager.CaptureFrameTimings();
            uint got = FrameTimingManager.GetLatestTimings(1, _ft);
            _dt[_n] = Time.unscaledDeltaTime * 1000f;
            _gpu[_n] = got > 0 ? (float)_ft[0].gpuFrameTime : -1f;
            _cpu[_n] = got > 0 ? (float)_ft[0].cpuFrameTime : -1f;
            _cpuMain[_n] = got > 0 ? (float)_ft[0].cpuMainThreadFrameTime : -1f;
            _cpuRender[_n] = got > 0 ? (float)_ft[0].cpuRenderThreadFrameTime : -1f;
            _gcAllocFrame[_n] = _recGcAlloc.Valid ? _recGcAlloc.LastValue : -1;
            long heap = GC.GetTotalMemory(false);
            _gcHeapDelta[_n] = heap - _lastHeap;
            _lastHeap = heap;
            _n++;
        }

        private IEnumerator Measure(string name, float seconds, bool windOn, string extra, int frameCount = 0)
        {
            ResetFrames();
            for (int i = 0; i < MaxFrames; i++) _work[i] = 0f;
            float el = 0f;
            int f = 0;
            while ((seconds > 0f && el < seconds) || (frameCount > 0 && f < frameCount))
            {
                yield return null;
                Sample();
                el += Time.unscaledDeltaTime;
                f++;
            }
            AppendFrames(name, extra);
        }

        // ------------------------------------------------------------------ output

        private string FrameList(int n)
        {
            var sb = new StringBuilder("\"perFrame\": {\"dtMs\": [");
            for (int i = 0; i < n; i++) sb.Append(i > 0 ? ", " : "").Append(_dt[i].ToString("0.0", Inv));
            sb.Append("], \"gpuFtMs\": [");
            for (int i = 0; i < n; i++) sb.Append(i > 0 ? ", " : "").Append(_gpu[i].ToString("0.0", Inv));
            sb.Append("], \"workMs\": [");
            for (int i = 0; i < n; i++) sb.Append(i > 0 ? ", " : "").Append(_work[i].ToString("0.0", Inv));
            sb.Append("]}");
            return sb.ToString();
        }

        private void Field(string k, string v, bool raw = false)
        {
            _json.Append("  \"").Append(k).Append("\": ");
            if (raw) _json.Append(v);
            else _json.Append('"').Append(v.Replace("\"", "'")).Append('"');
            _json.Append(",\n");
        }

        private void BeginSection(string name)
        {
            if (!_firstSection) _json.Append(",\n");
            _firstSection = false;
            _json.Append("    {\"name\": \"").Append(name).Append("\", ");
        }

        private void AppendStats(string name, SketchTreeRenderer.Stats s, string extra)
        {
            BeginSection(name);
            _json.AppendFormat(Inv, "\"woodMs\": {0:0.00}, \"potMs\": {1:0.0}, \"leafMs\": {2:0.00}, \"uploadMs\": {3:0.00}, \"bakeMs\": {4:0.0}, \"bakeFillMs\": {5:0.0}, \"bakeGpuSyncMs\": {6:0.0}, ",
                s.WoodMs, s.PotMs, s.LeafMs, s.UploadMs, s.BakeMs, s.BakeFillMs, s.BakeGpuMs);
            _json.AppendFormat(Inv, "\"woodVerts\": {0}, \"woodTris\": {1}, \"potVerts\": {2}, \"potTris\": {3}, \"leaves\": {4}, \"limbs\": {5}",
                s.WoodVerts, s.WoodTris, s.PotVerts, s.PotTris, s.Leaves, s.Limbs);
            if (!string.IsNullOrEmpty(extra)) _json.Append(", ").Append(extra);
            _json.Append('}');
            Log(name + " woodTris " + s.WoodTris + " bake " + s.BakeMs.ToString("0.0", Inv));
        }

        private void AppendFrames(string name, string extra)
        {
            int n = _n;
            BeginSection(name);
            _json.AppendFormat(Inv, "\"frames\": {0}, ", n);
            Stat("dtMs", _dt, n);
            Stat("gpuMs", _gpu, n);
            Stat("cpuMs", _cpu, n);
            Stat("cpuMainMs", _cpuMain, n);
            Stat("cpuRenderMs", _cpuRender, n);
            Stat("workMs", _work, n);
            float avgDt = Avg(_dt, n);
            float p99 = Percentile(_dt, n, 0.99f);
            _json.AppendFormat(Inv, "\"fpsAvg\": {0:0.00}, \"fps1Low\": {1:0.00}, ", avgDt > 0 ? 1000f / avgDt : 0f, p99 > 0 ? 1000f / p99 : 0f);
            long gcSum = 0, gcFrames = 0, heapPos = 0, heapFrames = 0;
            for (int i = 0; i < n; i++)
            {
                if (_gcAllocFrame[i] > 0) { gcSum += _gcAllocFrame[i]; gcFrames++; }
                if (_gcHeapDelta[i] > 0) { heapPos += _gcHeapDelta[i]; heapFrames++; }
            }
            _json.AppendFormat(Inv, "\"gcAllocBytes\": {0}, \"gcAllocFrames\": {1}, \"heapGrowthBytes\": {2}, \"heapGrowthFrames\": {3}",
                _recGcAlloc.Valid ? gcSum : -1, _recGcAlloc.Valid ? gcFrames : -1, heapPos, heapFrames);
            if (!string.IsNullOrEmpty(extra)) _json.Append(", ").Append(extra);
            _json.Append('}');
            Log(string.Format(Inv, "{0} frames {1} fps {2:0.0} 1%low {3:0.0} gpu {4:0.00} ms gc {5} heap+ {6}", name, n, avgDt > 0 ? 1000f / avgDt : 0f,
                p99 > 0 ? 1000f / p99 : 0f, Avg(_gpu, n), gcSum, heapPos));
        }

        private void Stat(string key, float[] a, int n)
        {
            _json.AppendFormat(Inv, "\"{0}\": {{\"avg\": {1:0.000}, \"p50\": {2:0.000}, \"p95\": {3:0.000}, \"p99\": {4:0.000}, \"max\": {5:0.000}}}, ",
                key, Avg(a, n), Percentile(a, n, .5f), Percentile(a, n, .95f), Percentile(a, n, .99f), Percentile(a, n, 1f));
        }

        private static float Avg(float[] a, int n)
        {
            if (n == 0) return 0f;
            double s = 0;
            int c = 0;
            for (int i = 0; i < n; i++) if (a[i] >= 0f) { s += a[i]; c++; }
            return c == 0 ? -1f : (float)(s / c);
        }

        private static float Percentile(float[] a, int n, float q)
        {
            if (n == 0) return 0f;
            var tmp = new float[n];
            int c = 0;
            for (int i = 0; i < n; i++) if (a[i] >= 0f) tmp[c++] = a[i];
            if (c == 0) return -1f;
            Array.Sort(tmp, 0, c);
            int k = Mathf.Clamp(Mathf.CeilToInt(q * c) - 1, 0, c - 1);
            return tmp[k];
        }

        /// <summary>1360x960 RGBA PNG with the sketch camera (2x2 SSAA, transparent background, wind off) — as the editor harness.</summary>
        private void Screenshot(string name)
        {
            const int W = 1360, H = 960;
            var rt = RenderTexture.GetTemporary(W * 2, H * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            float w = _windNow;
            SketchGlobals.SetWind(0f, _time);
            SketchCamera.Apply(cam);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var tex = new Texture2D(W * 2, H * 2, TextureFormat.RGBA32, false, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W * 2, H * 2), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] src = tex.GetPixels32();
            var dst = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    Color32 c = src[(y * 2 + j) * W * 2 + x * 2 + i];
                    r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                }
                dst[y * W + x] = a == 0 ? new Color32(0, 0, 0, 0) : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)((a + 2) / 4));
            }
            var outTex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            outTex.SetPixels32(dst);
            outTex.Apply(false);
            File.WriteAllBytes(Path.Combine(_dir, name + "_" + SystemInfo.graphicsDeviceType + ".png"), outTex.EncodeToPNG());
            Destroy(tex);
            Destroy(outTex);
            ApplyScreenCamera();
            SketchGlobals.SetWind(w, _time);
        }

        private void OnDestroy()
        {
            _recGcAlloc.Dispose();
            _recGfxUsed.Dispose();
            _recGcUsed.Dispose();
            _recSysUsed.Dispose();
            if (_tree != null) _tree.Dispose();
        }
    }
}
