using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using PocketArbor.Spike.Render;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace PocketArbor.Spike.Device
{
    /// <summary>
    /// PA-102X-S device harness (one scene, runs once at start, writes JSON + PNG into persistentDataPath/pa102xs, then
    /// "done.txt"). Scenario "2c" (default): K9 first show (async rebuild, PSO warm-up), K8 memory, K4 cut (masked
    /// re-bake), K5 daily rebuild (worker thread + slices), short steady/Chronicle sanity. Scenario "2a": the full
    /// phase-2a set (steady 60 s, GPU loop, cut, growth, Chronicle) for the C7 re-measure.
    /// <see cref="tracePso"/> (Development build): records a GraphicsStateCollection of everything drawn and saves it.
    /// Per-frame measuring loops allocate nothing.
    /// </summary>
    public sealed class SpikeDeviceRunner : MonoBehaviour
    {
        public Shader[] shaders;
        public Camera cam;
        public float wind = 0.5f;
        public int sliceFrames = 8;
        public int rebuildFrames = 60;
        public int growthFrames = 120;
        public string scenario = "2c";
        public GraphicsStateCollection pso;      // traced on Vulkan
        public GraphicsStateCollection psoGles;  // traced on OpenGLES3 (warm-up = shader compile on GLES)
        public bool tracePso;

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
        private float _time, _windNow;
        private readonly Color _screenBg = new Color(0.93f, 0.92f, 0.9f, 1f);
        private GraphicsStateCollection _trace;
        private double[] _passMs;

        private bool Dev => Debug.isDebugBuild;
        private float SteadySeconds => Dev ? 15f : 60f;

        private void Awake()
        {
            if (tracePso)
            {
                _trace = new GraphicsStateCollection();
                _trace.BeginTrace();
            }
        }

        private IEnumerator Start()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
            SketchTreeRenderer.ShaderLookup = FindShader;
            string launch = LaunchScenario();
            if (!string.IsNullOrEmpty(launch)) scenario = launch;
            if (scenario == "interactive" || scenario == "selftest")
            {
                yield return StartInteractive(scenario == "selftest");
                yield break;
            }
            _dir = Path.Combine(Application.persistentDataPath, "pa102xs");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            _recGcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _recGfxUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            _recGcUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            _recSysUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            ApplyScreenCamera();
            Log("start " + SystemInfo.deviceModel + " " + SystemInfo.graphicsDeviceType + " dev=" + Dev + " scenario=" + scenario + " trace=" + tracePso + " pso=" + (pso != null));

            _json.Append("{\n");
            Field("device", SystemInfo.deviceModel);
            Field("gpu", SystemInfo.graphicsDeviceName);
            Field("api", SystemInfo.graphicsDeviceType.ToString());
            Field("apiVersion", SystemInfo.graphicsDeviceVersion);
            Field("os", SystemInfo.operatingSystem);
            Field("scenario", scenario);
            Field("development", Dev ? "true" : "false", true);
            Field("screen", Screen.width + "x" + Screen.height);
            Field("refreshRate", Screen.currentResolution.refreshRateRatio.value.ToString("0.##", Inv), true);
            Field("quality", QualitySettings.names[QualitySettings.GetQualityLevel()]);
            Field("frameTimingGpu", FrameTimingManager.IsFeatureEnabled() ? "true" : "false", true);
            Field("recGcAllocInFrame", _recGcAlloc.Valid ? "true" : "false", true);
            Field("supportsParallelPSOCreation", SystemInfo.supportsParallelPSOCreation ? "true" : "false", true);
            Field("psoTrace", tracePso ? "true" : "false", true);

            // K9: PSO warm-up while the app "loads" (blocking here = loading screen).
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) pso = psoGles;
            if (pso != null && !tracePso)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                pso.WarmUp().Complete();
                Field("psoWarmUpMs", sw.Elapsed.TotalMilliseconds.ToString("0.0", Inv), true);
                Field("psoStates", pso.totalGraphicsStateCount.ToString(Inv), true);
                Field("psoVariants", pso.variantCount.ToString(Inv), true);
                Field("psoWarmedUp", pso.isWarmedUp ? "true" : "false", true);
                Log("PSO warm-up " + sw.Elapsed.TotalMilliseconds.ToString("0.0", Inv) + " ms, states " + pso.totalGraphicsStateCount);
            }
            _json.Append("  \"sections\": [\n");

            for (int i = 0; i < 30; i++) yield return null;
            _tree = new SketchTreeRenderer(null, HideFlags.None) { RebuildFrames = rebuildFrames };

            if (scenario == "2a") yield return Scenario2a();
            else if (scenario == "c6") yield return ScenarioC6();
            else if (scenario == "c7") yield return ScenarioC7();
            else if (scenario == "k9") yield return ScenarioK9();
            else yield return Scenario2c();

            _json.Append("\n  ]\n}\n");
            string name = "results_" + scenario + "_" + SystemInfo.graphicsDeviceType + (Dev ? "_dev" : "") + (tracePso ? "_trace" : "") + ".json";
            File.WriteAllText(Path.Combine(_dir, name), _json.ToString());
            if (_trace != null)
            {
                _trace.EndTrace();
                bool ok = _trace.SaveToFile(Path.Combine(_dir, "PA102XS_" + SystemInfo.graphicsDeviceType + ".graphicsstate"));
                Log("PSO trace saved=" + ok + " states " + _trace.totalGraphicsStateCount + " variants " + _trace.variantCount);
            }
            File.WriteAllText(Path.Combine(_dir, "done.txt"), name);
            Log("PA102XS_DONE " + name);
        }

        /// <summary>Scenario from the launch intent (am start ... -e scenario 2c); null when launched from the icon.</summary>
        private static string LaunchScenario()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    string s = intent.Call<string>("getStringExtra", "scenario");
                    Log("launch scenario extra: " + (s ?? "(none)"));
                    return s;
                }
            }
            catch (Exception e)
            {
                Log("launch scenario extra unavailable: " + e.Message);
            }
#endif
            return null;
        }

        /// <summary>Interactive mode (owner's manual testing): PSO warm-up as in K9, then the debug-menu controller.</summary>
        private IEnumerator StartInteractive(bool selfTest)
        {
            Log("interactive " + SystemInfo.deviceModel + " " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceVersion + " selftest=" + selfTest);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) pso = psoGles;
            if (pso != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                pso.WarmUp().Complete();
                Log("PSO warm-up " + sw.Elapsed.TotalMilliseconds.ToString("0.0", Inv) + " ms, states " + pso.totalGraphicsStateCount);
            }
            yield return null;
            var it = gameObject.AddComponent<SpikeInteractive>();
            it.cam = cam;
            it.selfTest = selfTest;
            enabled = false; // the runner's Update (fixed wind) is not used in this mode
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

        private SketchFrame Gen(string sp, string style, int seed, float t)
        {
            TreeGenAdapter.Generate(sp, style, seed, t, _frame);
            return _frame;
        }

        // ================================================================== scenario 2c (C1-C4)

        private IEnumerator Scenario2c()
        {
            // K9 + K8: first show of the app, async (worker + slices), with/without PSO warm-up.
            yield return AsyncShow("first_show_momiji", "momiji", 1f, 45);
            AppendMemory("memory_after_first_show_momiji");
            yield return AsyncShow("show_kuromatsu", "kuromatsu", 1f, 30);
            AppendMemory("memory_kuromatsu");
            _windNow = wind;
            yield return Measure("steady_kuromatsu_10s", Dev ? 5f : 10f, null);
            _windNow = 0f;

            // K4: cut with masked re-bake (kuromatsu, then momiji), light in <= sliceFrames frames.
            yield return CutScenario("kuromatsu", sliceFrames);
            yield return AsyncShow("show_momiji", "momiji", 1f, 20);
            yield return CutScenario("momiji", sliceFrames);
            Screenshot("shot_cut_momiji_2c");
            yield return CutScenario("momiji", sliceFrames); // second cut on the same tree (next primary)

            // K5: daily growth on the worker, bake over rebuildFrames, swap.
            yield return AsyncShow("show_momiji_t95", "momiji", 0.95f, 10);
            _tree.ResetPeak();
            yield return Growth("growth_momiji_95_100", "momiji", 1f);
            AppendMemory("memory_after_growth");
            yield return Growth("growth_kuromatsu_switch", "kuromatsu", 1f);

            // K7 sanity after C5: Chronicle 5 s.
            yield return Chronicle("momiji", 5f);
            yield return AsyncShow("show_momiji_end", "momiji", 1f, 10);
            Screenshot("shot_momiji_2c");
        }

        /// <summary>BeginRebuild (generator on the worker) → frames until the swap → <paramref name="after"/> more frames.</summary>
        private IEnumerator AsyncShow(string name, string sp, float t, int after)
        {
            _windNow = 0f;
            ResetFrames();
            _tree.ResetPeak();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _tree.BeginRebuild(sp, "moyogi", 3, t, rebuildFrames);
            _work[0] = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            int frames = 0, swapAt = -1;
            while (frames < 600)
            {
                yield return null;
                Sample();
                frames++;
                long a = System.Diagnostics.Stopwatch.GetTimestamp();
                bool swapped = _tree.Tick();
                if (_n < MaxFrames) _work[_n] = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (swapped) { swapAt = frames; break; }
            }
            for (int i = 0; i < after; i++) { yield return null; Sample(); }
            SketchTreeRenderer.Stats st = _tree.LastJobStats;
            AppendFrames(name, string.Format(Inv,
                "\"uploadSceneMs\": {14:0.0}, \"uploadInputsMs\": {15:0.0}, \"beginSliceMs\": {16:0.0}, \"framesToVisible\": {0}, \"workerGenMs\": {1:0.0}, \"workerPotMs\": {2:0.0}, \"workerWoodMs\": {3:0.0}, \"workerClassifyMs\": {4:0.0}, \"workerLeafMs\": {5:0.0}, \"workerFillMs\": {6:0.0}, \"woodTris\": {7}, \"snapVerts\": {8}, \"woodVerts\": {9}, \"leaves\": {10}, \"potTris\": {11}, \"peakGpuBytes\": {12}, {13}",
                swapAt, st.GenMs, st.PotMs, st.WoodMs, st.ClassifyMs, st.LeafMs, st.BakeFillMs, st.WoodTris, st.SnapVerts, st.WoodVerts, st.Leaves, st.PotTris, _tree.PeakGpuBytes, FrameList(Math.Min(_n, swapAt + after + 1)),
                _tree.LastUploadSceneMs, _tree.LastUploadInputsMs, _tree.LastBeginSliceMs));
        }

        private IEnumerator CutScenario(string sp, int slices)
        {
            _windNow = 0f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _tree.PrepareCut();
            double prep = sw.Elapsed.TotalMilliseconds;
            int leavesBefore = _tree.DisplayedLeafCount;
            for (int i = 0; i < 20; i++) yield return null;
            ResetFrames();
            _tree.ResetPeak();
            SketchTreeRenderer.CutStats cs = _tree.Cut(0, slices);
            _work[0] = (float)cs.CpuMs;
            int frames = 0;
            yield return null;
            Sample();
            while (true)
            {
                long a = System.Diagnostics.Stopwatch.GetTimestamp();
                bool swapped = _tree.Tick();
                if (_n < MaxFrames) _work[_n] = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                frames++;
                yield return null;
                Sample();
                if (swapped || frames > 64) break;
            }
            for (int i = 0; i < 10; i++) { yield return null; Sample(); }
            AppendFrames("cut_" + sp + "_n" + slices, string.Format(Inv,
                "\"prepareLeafGroupsMs\": {0:0.0}, \"cutCpuMs\": {1:0.00}, \"maskMs\": {2:0.00}, \"uploadMs\": {3:0.00}, \"maskJoint\": {4}, \"maskLightOnly\": {5}, \"vertices\": {6}, \"lightFrames\": {7}, \"sliceFramesTarget\": {8}, \"leavesBefore\": {9}, \"peakGpuBytes\": {10}, {11}",
                prep, cs.CpuMs, _tree.LastCutMaskMs, cs.UploadMs, _tree.LastMaskJoint, _tree.LastMaskLight, cs.Vertices, frames, slices, leavesBefore, _tree.PeakGpuBytes, FrameList(Math.Min(_n, 40))));
        }

        private IEnumerator Growth(string name, string sp, float t)
        {
            _windNow = 0f;
            for (int i = 0; i < 10; i++) yield return null;
            ResetFrames();
            _tree.ResetPeak();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _tree.BeginRebuild(sp, "moyogi", 3, t, growthFrames);
            _work[0] = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            int frames = 0;
            while (frames < 600)
            {
                yield return null;
                Sample();
                frames++;
                long a = System.Diagnostics.Stopwatch.GetTimestamp();
                bool swapped = _tree.Tick();
                if (_n < MaxFrames) _work[_n] = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (swapped) break;
            }
            for (int i = 0; i < 10; i++) { yield return null; Sample(); }
            SketchTreeRenderer.Stats st = _tree.LastJobStats;
            AppendFrames(name, string.Format(Inv,
                "\"framesToSwap\": {0}, \"workerGenMs\": {1:0.0}, \"workerPotMs\": {2:0.0}, \"workerWoodMs\": {3:0.0}, \"workerClassifyMs\": {4:0.0}, \"workerLeafMs\": {5:0.0}, \"workerFillMs\": {6:0.0}, \"woodTris\": {7}, \"snapVerts\": {8}, \"woodVerts\": {9}, \"peakGpuBytes\": {10}, {11}",
                frames, st.GenMs, st.PotMs, st.WoodMs, st.ClassifyMs, st.LeafMs, st.BakeFillMs, st.WoodTris, st.SnapVerts, st.WoodVerts, _tree.PeakGpuBytes, FrameList(Math.Min(_n, 60))));
        }

        private IEnumerator Chronicle(string sp, float dur)
        {
            _windNow = 0f;
            ResetFrames();
            float elapsed = 0f;
            double genSum = 0, buildSum = 0, genMax = 0, buildMax = 0;
            int frames = 0;
            while (elapsed < dur)
            {
                float t = Mathf.Clamp01(elapsed / dur);
                long a = System.Diagnostics.Stopwatch.GetTimestamp();
                SketchFrame f = Gen(sp, "moyogi", 3, t);
                long b = System.Diagnostics.Stopwatch.GetTimestamp();
                _tree.ShowNoBake(f);
                long c = System.Diagnostics.Stopwatch.GetTimestamp();
                double k = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double g = (b - a) * k, bl = (c - b) * k;
                genSum += g; buildSum += bl; genMax = Math.Max(genMax, g); buildMax = Math.Max(buildMax, bl);
                if (_n < MaxFrames) _work[_n] = (float)(g + bl);
                frames++;
                yield return null;
                Sample();
                elapsed += Time.unscaledDeltaTime;
            }
            AppendFrames("chronicle_" + sp, string.Format(Inv, "\"chronicleFrames\": {0}, \"genAvgMs\": {1:0.00}, \"genMaxMs\": {2:0.00}, \"buildAvgMs\": {3:0.00}, \"buildMaxMs\": {4:0.00}",
                frames, genSum / Math.Max(1, frames), genMax, buildSum / Math.Max(1, frames), buildMax));
        }

        private void AppendMemory(string name)
        {
            BeginSection(name);
            _json.AppendFormat(Inv, "\"gpuBytes\": {0}, \"peakGpuBytes\": {1}, \"gfxUsedMemory\": {2}, \"gcUsedMemory\": {3}, \"systemUsedMemory\": {4}}}",
                _tree.GpuBytes(), _tree.PeakGpuBytes, _recGfxUsed.Valid ? _recGfxUsed.LastValue : -1, _recGcUsed.Valid ? _recGcUsed.LastValue : -1, _recSysUsed.Valid ? _recSysUsed.LastValue : -1);
            Log(name + " gpu " + (_tree.GpuBytes() / 1048576.0).ToString("0.00", Inv) + " MB peak " + (_tree.PeakGpuBytes / 1048576.0).ToString("0.00", Inv) + " MB");
        }

        // ================================================================== scenario c6 (TA): parametric pot, LowEnd bark

        private static readonly int IdBarkLod = Shader.PropertyToID("_SkBarkLodMat");

        private IEnumerator ScenarioC6()
        {
            // Pot mesh build on this CPU (main thread, IL2CPP): parametric builder vs Surface Nets.
            BeginSection("c6_pot_build");
            var pb = new ParametricPotBuilder();
            var g = new GeoBuffer(1 << 13, 1 << 15);
            var times = new double[50];
            string[,] pots = { { "momiji", "moyogi" }, { "yamazakura", "moyogi" }, { "kuromatsu", "moyogi" }, { "momiji", "bunjin" } };
            var sb = new StringBuilder();
            for (int k = 0; k < pots.GetLength(0); k++)
            {
                SpeciesLook look = SpeciesLook.For(pots[k, 0], pots[k, 1]);
                for (int i = 0; i < 5; i++) { g.Clear(); pb.Build(look, g); }
                long a0 = GC.GetTotalMemory(false);
                for (int i = 0; i < times.Length; i++)
                {
                    g.Clear();
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    pb.Build(look, g);
                    times[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                }
                long heap = GC.GetTotalMemory(false) - a0;
                Array.Sort(times);
                var sn = new GeoBuffer(1 << 15, 1 << 17);
                var sdf = new PotSdf(look);
                float hw = look.PotHw + 8f, hd = look.PotHd + 8f;
                long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                SurfaceNets.Build(sdf, new Vector3(-hw, -look.PotDep - 9f, -hd), new Vector3(hw, 6.5f, hd), 2.0f, sn, 2f, p => p.y < -1f && sdf.Inner(p.x, p.y, p.z) < 0.6f);
                double snMs = (System.Diagnostics.Stopwatch.GetTimestamp() - s0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (k > 0) sb.Append(", ");
                sb.AppendFormat(Inv, "\"{0}_{1}\": {{\"paramTris\": {2}, \"paramVerts\": {3}, \"paramMedianMs\": {4:0.000}, \"paramMaxMs\": {5:0.000}, \"paramHeapGrowth\": {6}, \"sn20Ms\": {7:0.0}, \"sn20Tris\": {8}}}",
                    pots[k, 0], pots[k, 1], pb.Triangles, pb.Vertices, times[times.Length / 2], times[times.Length - 1], heap, snMs, sn.TriangleCount);
                Log("c6 pot " + pots[k, 0] + "/" + pots[k, 1] + ": param " + pb.Triangles + " tris median " + times[times.Length / 2].ToString("0.000", Inv) + " ms, SN 2.0 " + snMs.ToString("0.0", Inv) + " ms " + sn.TriangleCount + " tris");
                yield return null;
            }
            _json.Append(sb).Append('}');

            // First show with the parametric pot (worker: generator + pot + wood + leaves), then the pot GPU cost SN vs parametric.
            _tree.ParametricPot = true;
            yield return AsyncShow("c6_first_show_param_momiji", "momiji", 1f, 20);
            foreach (string sp in new[] { "momiji", "yamazakura" })
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    bool param = mode > 0;
                    _tree.ParametricPot = param;
                    _tree.PotMeshNormals = mode != 2;
                    SketchTreeRenderer.Stats st = _tree.Show(Gen(sp, "moyogi", 3, 1f));
                    _tree.RefreshPotShading();
                    for (int i = 0; i < 10; i++) yield return null;
                    string tag = sp + (mode == 0 ? "_sn20" : mode == 1 ? "_param" : "_param_sdfnrm");
                    Screenshot("shot_c6_pot_" + tag);
                    string loop = GpuLoop(60);
                    BeginSection("c6_pot_gpu_" + tag);
                    _json.AppendFormat(Inv, "\"potTris\": {0}, \"potVerts\": {1}, \"potShowMs\": {2:0.0}, {3}}}", st.PotTris, st.PotVerts, st.PotMs, loop);
                    for (int i = 0; i < 10; i++) yield return null;
                }
            }
            _tree.ParametricPot = true;
            _tree.PotMeshNormals = true;
            _tree.RefreshPotShading();

            // K1 bark: full vs LowEnd (and LowEnd + thin-branch LOD for Kuromatsu).
            foreach (string sp in new[] { "kuromatsu", "momiji", "yamazakura" })
            {
                _tree.Show(Gen(sp, "moyogi", 3, 1f));
                for (int v = 0; v < 3; v++)
                {
                    if (v == 2 && sp != "kuromatsu") break;
                    _tree.LowEndBark = v > 0;
                    Shader.SetGlobalFloat(IdBarkLod, v == 2 ? 0.4f : 0f);
                    for (int i = 0; i < 10; i++) yield return null;
                    string tag = sp + (v == 0 ? "_full" : v == 1 ? "_low" : "_lowlod");
                    Screenshot("shot_c6_bark_" + tag);
                    string loop = GpuLoop(60);
                    BeginSection("c6_bark_gpu_" + tag);
                    _json.Append(loop).Append('}');
                    for (int i = 0; i < 10; i++) yield return null;
                }
                if (sp == "kuromatsu")
                {
                    // Steady 10 s with wind, LowEnd bark (sanity: fps, GC).
                    _tree.LowEndBark = true;
                    Shader.SetGlobalFloat(IdBarkLod, 0f);
                    _windNow = wind;
                    yield return Measure("c6_steady_kuromatsu_low_10s", 10f, null);
                    _windNow = 0f;
                }
            }
            _tree.LowEndBark = false;
            Shader.SetGlobalFloat(IdBarkLod, 0f);
        }

        // ================================================================== scenario c7: full re-measure K1-K11 after C1-C6

        private void SetBark(bool lowEnd)
        {
            _tree.LowEndBark = lowEnd;
            Shader.SetGlobalFloat(IdBarkLod, lowEnd ? 0.4f : 0f);
        }

        private IEnumerator ScenarioC7()
        {
            SetBark(false);
            yield return Measure("baseline_empty", Dev ? 3f : 10f, null);

            // K9: first show of the app (async rebuild, full bark), then the first draw of the LowEnd bark variant.
            yield return AsyncShow("k9_first_show_momiji_full", "momiji", 1f, 45);
            AppendMemory("k8_memory_momiji");
            ResetFrames();
            SetBark(true);
            for (int i = 0; i < 45; i++) { yield return null; Sample(); }
            AppendFrames("k9_first_draw_bark_low", null);
            yield return AsyncShow("k9_show_kuromatsu_low", "kuromatsu", 1f, 30);
            AppendMemory("k8_memory_kuromatsu");
            SetBark(false);
            for (int i = 0; i < 10; i++) yield return null;

            // K1, K2, K3, K8, K11: steady state with wind.
            yield return Steady("momiji", "moyogi", 3, 1f);
            yield return Steady("kuromatsu", "moyogi", 3, 1f);
            SetBark(true);
            yield return SteadyTagged("kuromatsu", "_low");
            SetBark(false);
            yield return Steady("yamazakura", "moyogi", 3, 1f);

            // K4: three cuts per species on a freshly shown tree (spread between repeats).
            for (int r = 0; r < 3; r++)
            {
                yield return AsyncShow("show_momiji_r" + r, "momiji", 1f, 10);
                yield return CutScenario("momiji", sliceFrames);
            }
            for (int r = 0; r < 3; r++)
            {
                yield return AsyncShow("show_kuromatsu_r" + r, "kuromatsu", 1f, 10);
                yield return CutScenario("kuromatsu", sliceFrames);
            }

            // K5: daily growth (worker + 120 bake frames).
            yield return AsyncShow("show_momiji_t95", "momiji", 0.95f, 10);
            _tree.ResetPeak();
            yield return Growth("growth_momiji_95_100", "momiji", 1f);
            AppendMemory("k8_memory_after_growth");
            yield return AsyncShow("show_kuromatsu_t95", "kuromatsu", 0.95f, 10);
            yield return Growth("growth_kuromatsu_95_100", "kuromatsu", 1f);

            // K7: Chronicle scrubbing.
            yield return Chronicle("momiji", Dev ? 5f : 10f);
            yield return Chronicle("kuromatsu", Dev ? 5f : 10f);
            yield return AsyncShow("show_end", "momiji", 1f, 10);
        }

        /// <summary>K9 only (run on a fresh install = cold driver/pipeline caches): first show, first LowEnd draw.</summary>
        private IEnumerator ScenarioK9()
        {
            SetBark(false);
            yield return Measure("baseline_empty", 3f, null);
            yield return AsyncShow("k9_first_show_momiji_full", "momiji", 1f, 45);
            ResetFrames();
            SetBark(true);
            for (int i = 0; i < 45; i++) { yield return null; Sample(); }
            AppendFrames("k9_first_draw_bark_low", null);
            yield return AsyncShow("k9_show_kuromatsu_low", "kuromatsu", 1f, 30);
            SetBark(false);
        }

        /// <summary>Steady state of the currently configured bark mode with a tag suffix (tree shown synchronously).</summary>
        private IEnumerator SteadyTagged(string sp, string suffix)
        {
            _windNow = 0f;
            SketchTreeRenderer.Stats st = _tree.Show(Gen(sp, "moyogi", 3, 1f));
            string tag = sp + "_moyogi_s3_t100" + suffix;
            for (int i = 0; i < 10; i++) yield return null;
            Screenshot("shot_" + tag);
            string loop = GpuLoop(Dev ? 20 : 60);
            for (int i = 0; i < 10; i++) yield return null;
            _windNow = wind;
            float warm = 0f;
            while (warm < 3f) { warm += Time.unscaledDeltaTime; yield return null; }
            yield return Measure("steady_" + tag, SteadySeconds, null);
            BeginSection("steady_" + tag + "_build");
            _json.AppendFormat(Inv, "\"woodTris\": {0}, \"leaves\": {1}, \"potTris\": {2}, \"bakeMs\": {3:0.0}, \"gpuBytes\": {4}, {5}}}",
                st.WoodTris, st.Leaves, st.PotTris, st.BakeMs, _tree.GpuBytes(), loop);
            _windNow = 0f;
        }

        // ================================================================== scenario 2a (full re-measure, C7)

        private IEnumerator Scenario2a()
        {
            yield return Measure("baseline_empty", 10f, null);
            yield return AsyncShow("first_show_momiji", "momiji", 1f, 45);
            string[] species = { "momiji", "kuromatsu", "yamazakura" };
            foreach (string sp in species) yield return Steady(sp, "moyogi", 3, 1f);
            if (_passMs != null)
                Log("bake pass ms: " + _passMs[0].ToString("0.0", Inv) + "/" + _passMs[1].ToString("0.0", Inv) + "/" + _passMs[2].ToString("0.0", Inv) + "/" + _passMs[3].ToString("0.0", Inv));
            yield return AsyncShow("show_momiji", "momiji", 1f, 10);
            yield return CutScenario("momiji", sliceFrames);
            yield return AsyncShow("show_kuromatsu", "kuromatsu", 1f, 10);
            yield return CutScenario("kuromatsu", sliceFrames);
            yield return AsyncShow("show_momiji_t95", "momiji", 0.95f, 10);
            yield return Growth("growth_momiji_95_100", "momiji", 1f);
            yield return Chronicle("momiji", 10f);
            yield return Chronicle("kuromatsu", 10f);
        }

        private IEnumerator Steady(string sp, string style, int seed, float t)
        {
            _windNow = 0f;
            SketchTreeRenderer.Stats st = _tree.Show(Gen(sp, style, seed, t));
            string tag = sp + "_" + style + "_s" + seed + "_t" + Mathf.RoundToInt(t * 100);
            for (int i = 0; i < 10; i++) yield return null;
            Screenshot("shot_" + tag);
            string loop = GpuLoop(Dev ? 20 : 60);
            for (int i = 0; i < 10; i++) yield return null;
            _windNow = wind;
            float warm = 0f;
            while (warm < 3f) { warm += Time.unscaledDeltaTime; yield return null; }
            yield return Measure("steady_" + tag, SteadySeconds, null);
            BeginSection("steady_" + tag + "_build");
            _json.AppendFormat(Inv, "\"woodTris\": {0}, \"leaves\": {1}, \"potTris\": {2}, \"bakeMs\": {3:0.0}, \"gpuBytes\": {4}, {5}}}",
                st.WoodTris, st.Leaves, st.PotTris, st.BakeMs, _tree.GpuBytes(), loop);
            _windNow = 0f;
        }

        /// <summary>
        /// Renders the camera <paramref name="iters"/> times into a screen-sized RT and waits for the GPU once at the end
        /// (after a burn-in to raise GPU clocks): wall time / iters ~ GPU cost when GPU-bound. Median of 3 repetitions.
        /// </summary>
        private string GpuLoop(int iters)
        {
            var rt = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            ApplyScreenCamera();
            cam.targetTexture = rt;
            string[] parts = { "full", "wood", "leaves", "pot", "empty" };
            var res = new double[parts.Length, 3];
            SetParts("full");
            for (int i = 0; i < 90; i++) cam.Render();
            AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            for (int rep = 0; rep < 3; rep++)
            for (int k = 0; k < parts.Length; k++)
            {
                int pi = (k + rep * 2) % parts.Length;
                SetParts(parts[pi]);
                cam.Render();
                AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < iters; i++) cam.Render();
                AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
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
            double p0 = _tree.TimeBakePass(0), p1 = _tree.TimeBakePass(1), p2 = _tree.TimeBakePass(2), p3 = _tree.TimeBakePass(3);
            _tree.ReleaseProfilingInputs();
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

        // ================================================================== measurement

        private void ResetFrames()
        {
            _n = 0;
            for (int i = 0; i < 64; i++) _work[i] = 0f;
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

        private IEnumerator Measure(string name, float seconds, string extra, int frameCount = 0)
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

        // ================================================================== output

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

        private string FrameList(int n)
        {
            var sb = new StringBuilder("\"perFrame\": {\"dtMs\": [");
            for (int i = 0; i < n; i++) sb.Append(i > 0 ? ", " : "").Append(_dt[i].ToString("0.0", Inv));
            sb.Append("], \"workMs\": [");
            for (int i = 0; i < n; i++) sb.Append(i > 0 ? ", " : "").Append(_work[i].ToString("0.0", Inv));
            sb.Append("], \"gcBytes\": [");
            for (int i = 0; i < n; i++) sb.Append(i > 0 ? ", " : "").Append(_gcAllocFrame[i].ToString(Inv));
            sb.Append("]}");
            return sb.ToString();
        }

        private void AppendFrames(string name, string extra)
        {
            int n = _n;
            BeginSection(name);
            _json.AppendFormat(Inv, "\"frames\": {0}, ", n);
            Stat("dtMs", _dt, n);
            Stat("gpuMs", _gpu, n);
            Stat("cpuMainMs", _cpuMain, n);
            Stat("workMs", _work, n);
            float avgDt = Avg(_dt, n);
            float p99 = Percentile(_dt, n, 0.99f);
            int over = 0;
            for (int i = 0; i < n; i++) if (_dt[i] > 17.5f) over++;
            _json.AppendFormat(Inv, "\"fpsAvg\": {0:0.00}, \"fps1Low\": {1:0.00}, \"framesOver16_7\": {2}, ", avgDt > 0 ? 1000f / avgDt : 0f, p99 > 0 ? 1000f / p99 : 0f, over);
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
            Log(string.Format(Inv, "{0} frames {1} fps {2:0.0} dtMax {3:0.0} over16.7 {4} gc {5} heap+ {6}", name, n, avgDt > 0 ? 1000f / avgDt : 0f,
                Percentile(_dt, n, 1f), over, gcSum, heapPos));
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
