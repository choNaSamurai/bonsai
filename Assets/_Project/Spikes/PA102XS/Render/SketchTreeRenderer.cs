using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// B* spike renderer: wood (+soil) mesh, pot mesh, leaves mesh — three MeshRenderers with custom HLSL, depth-tested
    /// in the URP 2D Renderer. Root has localScale z = -1, so all mesh data and shaders stay in sketch space.
    /// Phase 2c:
    ///  * C2 — rebuilds (growth, first show) run the generator, meshes, snap classification and bake-input filling on a
    ///    worker thread; the main thread only uploads and issues bake slices; meshes are uploaded on the swap frame;
    ///  * C1 — a cut removes the branch's indices at once and re-bakes only masked vertices (joint zone / light zone),
    ///    ping-pong between two output sets; the Surf pass projects only flagged vertices (near joints);
    ///  * C3 — single meshes, compact outputs (16 B/vertex), transient bake inputs freed after each job, budget preset;
    ///  * the pot is built (worker) and baked once per pot.
    /// C6 (TA): the pot is a parametric mesh (<see cref="ParametricPotBuilder"/>, exact potD zero set, ≈ 4.5–7 k triangles)
    /// by default; <see cref="ParametricPot"/> = false keeps the Surface Nets pot (<see cref="PotVoxel"/>) for comparison.
    /// <see cref="LowEndBark"/> switches the wood material to the SK_BARK_LOW variant (one Voronoi, analytic relief).
    /// </summary>
    public sealed class SketchTreeRenderer : IDisposable
    {
        public const string WoodShader = "PocketArbor/Spike/Wood";
        public const string PotShader = "PocketArbor/Spike/Pot";
        public const string LeafShader = "PocketArbor/Spike/Leaves";
        public const string BakeShader = "Hidden/PocketArbor/Spike/SdfBake";

        /// <summary>Optional shader source for players (no Shader.Find): set by the device runner.</summary>
        public static Func<string, Shader> ShaderLookup;

        private static readonly int IdNoBake = Shader.PropertyToID("_SkNoBake");
        private static readonly int IdPotMeshNrm = Shader.PropertyToID("_SkPotMeshNrm");
        public const string BarkLowKeyword = "SK_BARK_LOW";

        public struct Stats
        {
            public double WoodMs, PotMs, LeafMs, UploadMs, BakeMs, BakeFillMs, BakeGpuMs, ClassifyMs, GenMs;
            public int WoodVerts, WoodTris, PotVerts, PotTris, Leaves, Limbs, SnapVerts;
            public int TrunkVerts, RootVerts, LimbVerts, SoilVerts;
        }

        public struct CutStats
        {
            public double CpuMs, MaskMs, UploadMs;
            public int Joint, LightOnly, Vertices;
        }

        private enum Job { None, PotBake, PotDone, WoodBake, CutBake }

        public readonly GameObject Root;
        public readonly MeshRenderer Wood, Pot, Leaves;
        private readonly Mesh _woodMesh, _leafMesh, _potMesh;
        private readonly Material _woodMat, _potMat, _leafMat;
        private readonly SdfBaker _baker;
        private readonly MaterialPropertyBlock _woodMpb = new MaterialPropertyBlock(), _potMpb = new MaterialPropertyBlock();

        // Displayed state.
        private GeoBuffer _geo = new GeoBuffer(1 << 15, 1 << 17), _geoNext = new GeoBuffer(1 << 15, 1 << 17);
        private WoodMeshBuilder _wood = new WoodMeshBuilder(), _woodNext = new WoodMeshBuilder();
        private LeafMeshBuilder _leaf = new LeafMeshBuilder(), _leafNext = new LeafMeshBuilder();
        private SketchFrame _cur = new SketchFrame(), _next = new SketchFrame();
        private readonly BakeOutputs[] _out = { new BakeOutputs("PA102XS_WoodA"), new BakeOutputs("PA102XS_WoodB") };
        private int _disp;
        private readonly BakeOutputs _potOut = new BakeOutputs("PA102XS_Pot");
        private readonly BakeInputs _in = new BakeInputs("PA102XS_In");
        private readonly BakeInputs _potIn = new BakeInputs("PA102XS_PotIn");
        private readonly GeoBuffer _potGeo = new GeoBuffer(1 << 15, 1 << 17);
        private SpeciesLook _potLook, _potLookNext;
        private bool _potDirty;
        private readonly ParametricPotBuilder _potBuilder = new ParametricPotBuilder();
        private bool _parametricPot = true, _potParamNext, _potShownParam;

        // Worker (C2).
        private readonly Thread _worker;
        private readonly AutoResetEvent _go = new AutoResetEvent(false);
        private volatile bool _workerBusy, _quit;
        private volatile Exception _workerError;
        private bool _workPending;
        private string _reqSpecies, _reqStyle;
        private int _reqSeed;
        private float _reqT;
        private bool _reqGenerate;
        private Stats _jobStats;
        private enum WorkMode { Rebuild, CutMask }
        private WorkMode _workMode;
        private int _cutGroupReq, _cutFramesReq, _maskJoint, _maskLight;
        private double _maskMs;
        public double LastCutMaskMs => _maskMs;
        public int LastMaskJoint => _maskJoint;
        public int LastMaskLight => _maskLight;

        // Jobs.
        private Job _job;
        private int _jobFrames;
        private int[] _cutIdx, _cutLeafIdx;
        private readonly bool[] _leafRemoved = new bool[SketchConst.MAXLEAF];
        private readonly int[] _leafGroup = new int[SketchConst.MAXLEAF];
        private long _peakBytes;

        public bool FrontToBackLeaves = true;
        public float PotVoxel = 2.0f;

        /// <summary>C6: parametric pot mesh (default) or Surface Nets (comparison). Set between rebuilds; forces a pot rebuild.</summary>
        public bool ParametricPot
        {
            get => _parametricPot;
            set
            {
                if (_parametricPot == value) return;
                FinishAllNow();
                _parametricPot = value;
                _potLook = null;
            }
        }

        /// <summary>C6: shade the parametric pot with the interpolated mesh normal (default) instead of the per-pixel potD gradient.</summary>
        public bool PotMeshNormals = true;

        public ParametricPotBuilder PotBuilder => _potBuilder;

        /// <summary>Re-applies <see cref="PotMeshNormals"/> to the displayed pot (no rebuild, no re-bake).</summary>
        public void RefreshPotShading()
        {
            _potMpb.SetFloat(IdPotMeshNrm, _potShownParam && PotMeshNormals ? 1f : 0f);
            Pot.SetPropertyBlock(_potMpb);
        }

        /// <summary>C6: LowEnd bark (SK_BARK_LOW): one Voronoi per pixel for Kuromatsu, analytic relief for all species.</summary>
        public bool LowEndBark
        {
            get => _woodMat.IsKeywordEnabled(BarkLowKeyword);
            set
            {
                if (value) _woodMat.EnableKeyword(BarkLowKeyword);
                else _woodMat.DisableKeyword(BarkLowKeyword);
            }
        }
        public int RebuildFrames = 60;
        /// <summary>Main-thread ms of the last "worker done" Tick: scene upload, inputs upload, slicing setup.</summary>
        public double LastUploadSceneMs, LastUploadInputsMs, LastBeginSliceMs;

        public SketchTreeRenderer(Transform parent, HideFlags flags = HideFlags.DontSave)
        {
            Root = new GameObject("PA102XS_Tree") { hideFlags = flags };
            if (parent != null) Root.transform.SetParent(parent, false);
            Root.transform.localScale = new Vector3(1f, 1f, -1f);
            _woodMat = NewMat(WoodShader);
            _potMat = NewMat(PotShader);
            _leafMat = NewMat(LeafShader);
            _woodMesh = NewMesh("PA102XS_Wood");
            _leafMesh = NewMesh("PA102XS_Leaves");
            _potMesh = NewMesh("PA102XS_Pot");
            Wood = NewPart("Wood", _woodMesh, _woodMat, 0, flags);
            Pot = NewPart("Pot", _potMesh, _potMat, 0, flags);
            Leaves = NewPart("Leaves", _leafMesh, _leafMat, 1, flags);
            _baker = new SdfBaker(FindShader(BakeShader));
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "PA102XS Rebuild", Priority = System.Threading.ThreadPriority.BelowNormal };
            _worker.Start();
        }

        public SdfBaker Baker => _baker;
        public WoodMeshBuilder WoodBuilder => _wood;
        public SketchFrame Current => _cur;
        public bool Busy => _job != Job.None || _workPending;
        public int DisplayedLeafCount => _leaf.Count;
        public Stats LastJobStats => _jobStats;
        public long PeakGpuBytes => _peakBytes;

        /// <summary>Sets the tessellation of both builders (displayed and next).</summary>
        public void UseQualityPreset() { _wood.UseQualityPreset(); _woodNext.UseQualityPreset(); }
        public void UseBudgetPreset() { _wood.UseBudgetPreset(); _woodNext.UseBudgetPreset(); }

        public static Shader FindShader(string name)
        {
            Shader s = ShaderLookup != null ? ShaderLookup(name) : null;
            if (s == null) s = Shader.Find(name);
            if (s == null) throw new InvalidOperationException("Shader not found: " + name);
            return s;
        }

        private static Material NewMat(string shader) => new Material(FindShader(shader)) { hideFlags = HideFlags.HideAndDontSave };

        private static Mesh NewMesh(string name)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            m.MarkDynamic();
            return m;
        }

        private MeshRenderer NewPart(string name, Mesh mesh, Material mat, int order, HideFlags flags)
        {
            var go = new GameObject(name) { hideFlags = flags };
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = order;
            return mr;
        }

        // ------------------------------------------------------------------ synchronous (editor harness, tools)

        /// <summary>Builds and bakes everything now and displays it (main thread only).</summary>
        public Stats Show(SketchFrame f)
        {
            FinishAllNow();
            _next.CopyFrom(f);
            _reqGenerate = false;
            BuildNext();
            Stats st = _jobStats;
            var sw = Stopwatch.StartNew();
            SpeciesLook look = SpeciesLook.For(_next.Species, _next.Style);
            _baker.UploadScene(_next, look);
            st.UploadMs = sw.Elapsed.TotalMilliseconds;
            if (_potDirty)
            {
                sw.Restart();
                _potIn.Upload(false);
                _baker.BakeFull(_potIn, _potOut, false);
                FinishPot();
                st.PotMs += sw.Elapsed.TotalMilliseconds;
            }
            sw.Restart();
            _in.Upload(false);
            st.BakeFillMs += sw.Elapsed.TotalMilliseconds;
            BakeOutputs back = _out[1 - _disp];
            st.BakeGpuMs = _baker.BakeFull(_in, back, true);
            st.BakeMs = st.BakeFillMs + st.BakeGpuMs;
            TrackPeak();
            SwapRebuild();
            _jobStats = st;
            return st;
        }

        // ------------------------------------------------------------------ asynchronous (device)

        /// <summary>Daily growth / first show: everything heavy on the worker, then sliced bake, then swap.</summary>
        public void BeginRebuild(SketchFrame f, int frames)
        {
            FinishAllNow();
            _next.CopyFrom(f);
            _reqGenerate = false;
            KickWorker(frames);
        }

        /// <summary>Same, but the worker also runs the generator (TreeGen) — nothing heavy on the main thread.</summary>
        public void BeginRebuild(string species, string style, int seed, float t, int frames)
        {
            FinishAllNow();
            _reqSpecies = species;
            _reqStyle = style;
            _reqSeed = seed;
            _reqT = t;
            _reqGenerate = true;
            KickWorker(frames);
        }

        private void KickWorker(int frames)
        {
            _workMode = WorkMode.Rebuild;
            _jobFrames = frames;
            _workPending = true;
            _workerBusy = true;
            _go.Set();
        }

        /// <summary>Call once per frame. Returns true on the frame the rebuilt tree / re-baked light became visible.</summary>
        public bool Tick()
        {
            if (_workPending)
            {
                if (_workerBusy) return false;
                _workPending = false;
                if (_workerError != null)
                {
                    Exception e = _workerError;
                    _workerError = null;
                    throw new InvalidOperationException("rebuild worker failed", e);
                }
                if (_workMode == WorkMode.CutMask)
                {
                    // Mask ready (worker): drop the group from the SDF, upload, re-bake masked texels into the back outputs.
                    _cur.RemoveGroup(_cutGroupReq);
                    _wood.OnGroupRemoved(_cutGroupReq);
                    _baker.UploadScene(_cur, SpeciesLook.For(_cur.Species, _cur.Style));
                    _in.Upload(true);
                    _baker.BeginSliced(_in, _out[1 - _disp], _out[_disp], Math.Max(1, _cutFramesReq - 1));
                    _job = Job.CutBake;
                    return false;
                }
                // Worker done: upload scene + inputs, start the slices (pot first if it changed).
                double k = 1000.0 / Stopwatch.Frequency;
                long a0 = Stopwatch.GetTimestamp();
                SpeciesLook look = SpeciesLook.For(_next.Species, _next.Style);
                _baker.UploadScene(_next, look);
                long a1 = Stopwatch.GetTimestamp();
                LastUploadSceneMs = (a1 - a0) * k;
                if (_potDirty)
                {
                    _potIn.Upload(false);
                    long a2 = Stopwatch.GetTimestamp();
                    _baker.BeginSliced(_potIn, _potOut, null, Math.Max(4, _jobFrames / 4));
                    LastUploadInputsMs = (a2 - a1) * k;
                    LastBeginSliceMs = (Stopwatch.GetTimestamp() - a2) * k;
                    _job = Job.PotBake;
                }
                else StartWoodJob();
                return false;
            }
            if (_job == Job.None) return false;
            TrackPeak();
            if (_job == Job.PotDone)
            {
                // Separate frame from the pot mesh upload.
                StartWoodJob();
                return false;
            }
            if (!_baker.Step()) return false;
            switch (_job)
            {
                case Job.PotBake:
                    FinishPot();
                    _potIn.ReleaseGpu();
                    _job = Job.PotDone;
                    return false;
                case Job.WoodBake:
                    _in.ReleaseGpu();
                    SwapRebuild();
                    _job = Job.None;
                    return true;
                case Job.CutBake:
                    _in.ReleaseGpu();
                    _out[_disp].Release();
                    _disp = 1 - _disp;
                    BindWood();
                    _job = Job.None;
                    return true;
            }
            return false;
        }

        private void StartWoodJob()
        {
            _in.Upload(false);
            _baker.BeginSliced(_in, _out[1 - _disp], null, _jobFrames);
            _job = Job.WoodBake;
        }

        /// <summary>Leaf → branch group (nearest limb segment); in the game leaves would carry their branch id.</summary>
        public void PrepareCut()
        {
            int[] cg = _wood.ChunkGroup;
            for (int i = 0; i < _cur.Nl; i++)
            {
                int o = i * 16;
                var p = new Vector3(_cur.Leaf[o], _cur.Leaf[o + 1], _cur.Leaf[o + 2]);
                float best = 1e9f;
                int bg = -1;
                for (int c = 0; c < _cur.Nc; c++)
                {
                    int cb = c * 4;
                    float rad = Mathf.Abs(_cur.Cb[cb + 3]);
                    rad -= 1000f * Mathf.Floor(rad / 1000f);
                    var cc = new Vector3(_cur.Cb[cb], _cur.Cb[cb + 1], _cur.Cb[cb + 2]);
                    if ((p - cc).magnitude - rad > best) continue;
                    int b0 = SketchConst.OBR + c * 5;
                    for (int s = 0; s < 4; s++)
                    {
                        int a = (b0 + s) * 4, bb = (b0 + s + 1) * 4;
                        var A = new Vector3(_cur.Dat[a], _cur.Dat[a + 1], _cur.Dat[a + 2]);
                        var B = new Vector3(_cur.Dat[bb], _cur.Dat[bb + 1], _cur.Dat[bb + 2]);
                        Vector3 ab = B - A;
                        float t = Mathf.Clamp01(Vector3.Dot(p - A, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                        float d = (A + ab * t - p).magnitude;
                        if (d < best) { best = d; bg = cg[c]; }
                    }
                }
                _leafGroup[i] = bg;
            }
            // Pre-size the cut buffers (K3: the cut itself must not allocate).
            if (_cutIdx == null || _cutIdx.Length < _geo.ICount) _cutIdx = new int[_geo.ICount];
            if (_cutLeafIdx == null || _cutLeafIdx.Length < _leaf.Count * 6) _cutLeafIdx = new int[Math.Max(6, _leaf.Count * 6)];
        }

        /// <summary>
        /// Cut (C1): the displayed tree loses branch group <paramref name="group"/> (limbs + its leaves) in this call
        /// (visible next frame). Only vertices whose bake can change are re-baked (mask), over <paramref name="frames"/>
        /// Tick()s, into the back outputs; the rest is copied. <see cref="PrepareCut"/> first.
        /// </summary>
        public CutStats Cut(int group, int frames)
        {
            FinishAllNow();
            var st = new CutStats();
            long t0 = Stopwatch.GetTimestamp();
            int n = _wood.IndicesWithoutGroup(_geo, group, ref _cutIdx);
            _woodMesh.SetIndices(_cutIdx, 0, n, MeshTopology.Triangles, 0, false);
            for (int i = 0; i < _cur.Nl; i++)
                _leafRemoved[i] = _leafRemoved[i] || _leafGroup[i] == group || _leafGroup[i] == WoodMeshBuilder.RemovedGroup;
            int ln = _leaf.IndicesWithout(_leafRemoved, ref _cutLeafIdx);
            _leafMesh.SetIndices(_cutLeafIdx, 0, ln, MeshTopology.Triangles, 0, false);
            // Mask on the worker (C1/K4: the cut frame only swaps index buffers); light = 1 worker frame + slices.
            _cutGroupReq = group;
            _cutFramesReq = frames;
            _workMode = WorkMode.CutMask;
            _workPending = true;
            _workerBusy = true;
            _go.Set();
            st.Vertices = _geo.VCount;
            st.CpuMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            return st;
        }

        private void ComputeCutMask()
        {
            long t0 = Stopwatch.GetTimestamp();
            _wood.CutMask(_geo, _cur, _cutGroupReq, _cur.LightDir, _in.MaskData, out _maskJoint, out _maskLight);
            _in.SetMaskCosts(_in.MaskData, _geo.VCount, _geo.Cost);
            _maskMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Chronicle / scrubbing: geometry and leaves rebuilt and shown at once, no bake (_SkNoBake).</summary>
        public Stats ShowNoBake(SketchFrame f)
        {
            FinishAllNow();
            var st = new Stats();
            long t0 = Stopwatch.GetTimestamp();
            _cur.CopyFrom(f);
            SpeciesLook look = SpeciesLook.For(f.Species, f.Style);
            SketchGlobals.Set(f, look);
            _wood.Build(f, look, _geo);
            _geo.AssignBakeTexels(0, SdfBaker.Width);
            _geo.ApplyTo(_woodMesh, 8f);
            long t1 = Stopwatch.GetTimestamp();
            _leaf.Fill(f, FrontToBackLeaves);
            _leaf.Apply(_leafMesh);
            long t2 = Stopwatch.GetTimestamp();
            _woodMpb.SetFloat(IdNoBake, 1f);
            Wood.SetPropertyBlock(_woodMpb);
            double ms = 1000.0 / Stopwatch.Frequency;
            st.WoodMs = (t1 - t0) * ms;
            st.LeafMs = (t2 - t1) * ms;
            st.Leaves = _leaf.Count;
            st.WoodTris = _geo.TriangleCount;
            return st;
        }

        // ------------------------------------------------------------------ worker

        private void WorkerLoop()
        {
            while (true)
            {
                _go.WaitOne();
                if (_quit) return;
                try
                {
                    if (_workMode == WorkMode.CutMask) ComputeCutMask();
                    else BuildNext();
                }
                catch (Exception e) { _workerError = e; }
                _workerBusy = false;
            }
        }

        /// <summary>Everything CPU-heavy for the next tree (worker thread, or main thread in Show). No Unity API.</summary>
        private void BuildNext()
        {
            var st = new Stats();
            double ms = 1000.0 / Stopwatch.Frequency;
            long t = Stopwatch.GetTimestamp();
            if (_reqGenerate) TreeGenAdapter.Generate(_reqSpecies, _reqStyle, _reqSeed, _reqT, _next);
            long t0 = Stopwatch.GetTimestamp();
            st.GenMs = (t0 - t) * ms;
            SpeciesLook look = SpeciesLook.For(_next.Species, _next.Style);
            if (!look.SamePot(_potLook))
            {
                _potGeo.Clear();
                _potParamNext = _parametricPot;
                if (_parametricPot) _potBuilder.Build(look, _potGeo);
                else
                {
                    var sdf = new PotSdf(look);
                    float hw = look.PotHw + 8f, hd = look.PotHd + 8f;
                    Vector3 min = new Vector3(-hw, -look.PotDep - 9f, -hd), max = new Vector3(hw, 6.5f, hd);
                    SurfaceNets.Build(sdf, min, max, PotVoxel, _potGeo, 2f, p => p.y < -1f && sdf.Inner(p.x, p.y, p.z) < 0.6f);
                }
                _potGeo.AssignBakeTexels(0, SdfBaker.Width);
                _potIn.Fill(_potGeo);
                _potLookNext = look;
                _potDirty = true;
            }
            long t1 = Stopwatch.GetTimestamp();
            _woodNext.Build(_next, look, _geoNext);
            long t2 = Stopwatch.GetTimestamp();
            st.SnapVerts = _woodNext.ClassifySnap(_geoNext, _next);
            _geoNext.AssignBakeTexels(0, SdfBaker.Width);
            long t3 = Stopwatch.GetTimestamp();
            _leafNext.Fill(_next, FrontToBackLeaves);
            long t4 = Stopwatch.GetTimestamp();
            _in.Fill(_geoNext);
            long t5 = Stopwatch.GetTimestamp();
            st.PotMs = (t1 - t0) * ms;
            st.WoodMs = (t2 - t1) * ms;
            st.ClassifyMs = (t3 - t2) * ms;
            st.LeafMs = (t4 - t3) * ms;
            st.BakeFillMs = (t5 - t4) * ms;
            st.WoodVerts = _geoNext.VCount;
            st.WoodTris = _geoNext.TriangleCount;
            st.Leaves = _leafNext.Count;
            st.Limbs = _woodNext.LimbCount;
            st.TrunkVerts = _woodNext.TrunkVerts;
            st.RootVerts = _woodNext.RootVerts;
            st.LimbVerts = _woodNext.LimbVerts;
            st.SoilVerts = _woodNext.SoilVerts;
            st.PotVerts = _potGeo.VCount;
            st.PotTris = _potGeo.TriangleCount;
            _jobStats = st;
        }

        // ------------------------------------------------------------------ swap & helpers

        private void FinishPot()
        {
            _potGeo.ApplyTo(_potMesh, 1f);
            _potOut.Bind(_potMpb);
            _potMpb.SetFloat(IdNoBake, 0f);
            _potShownParam = _potParamNext;
            _potMpb.SetFloat(IdPotMeshNrm, _potShownParam && PotMeshNormals ? 1f : 0f);
            Pot.SetPropertyBlock(_potMpb);
            _potLook = _potLookNext;
            _potDirty = false;
        }

        /// <summary>Swap frame of a rebuild: upload the new meshes (single buffers), flip outputs, adopt the next state.</summary>
        private void SwapRebuild()
        {
            _geoNext.ApplyTo(_woodMesh, 8f);
            _leafNext.Apply(_leafMesh);
            Swap(ref _geo, ref _geoNext);
            Swap(ref _wood, ref _woodNext);
            Swap(ref _leaf, ref _leafNext);
            Swap(ref _cur, ref _next);
            Array.Clear(_leafRemoved, 0, _leafRemoved.Length);
            _out[_disp].Release();
            _disp = 1 - _disp;
            BindWood();
            // Display globals (age, species, pot) follow the displayed tree.
            SketchGlobals.Set(_cur, SpeciesLook.For(_cur.Species, _cur.Style));
        }

        private void BindWood()
        {
            _out[_disp].Bind(_woodMpb);
            _woodMpb.SetFloat(IdNoBake, 0f);
            Wood.SetPropertyBlock(_woodMpb);
        }

        private static void Swap<T>(ref T a, ref T b)
        {
            T t = a;
            a = b;
            b = t;
        }

        /// <summary>Finishes the pending rebuild / cut now (waits for the worker, runs the remaining bake slices).</summary>
        public void CompleteNow() => FinishAllNow();

        private void FinishAllNow()
        {
            while (_workPending || _job != Job.None)
            {
                if (_workPending && _workerBusy) { Thread.Sleep(1); continue; }
                Tick();
            }
        }

        private void TrackPeak()
        {
            long b = GpuBytes();
            if (b > _peakBytes) _peakBytes = b;
        }

        public void ResetPeak() { _peakBytes = GpuBytes(); }

        /// <summary>GPU memory of the tree (meshes + bake outputs + transient inputs + SDF texture), from buffer sizes.</summary>
        public long GpuBytes()
        {
            return MeshBytes(_woodMesh, 36) + MeshBytes(_leafMesh, 72) + MeshBytes(_potMesh, 36)
                 + _out[0].GpuBytes + _out[1].GpuBytes + _potOut.GpuBytes + _in.GpuBytes + _potIn.GpuBytes + _baker.SdfBytes;
        }

        private static long MeshBytes(Mesh m, int stride)
        {
            long idx = 0;
            for (int s = 0; s < m.subMeshCount; s++) idx += (long)m.GetIndexCount(s);
            return (long)m.vertexCount * stride + idx * (m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
        }

        public void SetOrders(int wood, int pot, int leaves)
        {
            Wood.sortingOrder = wood;
            Pot.sortingOrder = pot;
            Leaves.sortingOrder = leaves;
        }

        /// <summary>Bake profiling on device: one pass over the displayed geometry (re-uploads the inputs).</summary>
        public double TimeBakePass(int pass)
        {
            _in.Fill(_geo);
            _in.Upload(false);
            double ms = _baker.TimePass(_in, _out[_disp], pass);
            return ms;
        }

        public void ReleaseProfilingInputs() { _in.ReleaseGpu(); }

        public void Dispose()
        {
            _quit = true;
            _go.Set();
            _worker.Join(1000);
            _baker.Dispose();
            _out[0].Dispose();
            _out[1].Dispose();
            _potOut.Dispose();
            _in.Dispose();
            _potIn.Dispose();
            Destroy(_woodMesh);
            Destroy(_leafMesh);
            Destroy(_potMesh);
            Destroy(_woodMat);
            Destroy(_potMat);
            Destroy(_leafMat);
            Destroy(Root);
        }

        private static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }

    /// <summary>The sketch's tilted orthographic camera: sx = 340 + x, sy = 400 - (y*CE - z*SE), 680x480 frame.</summary>
    public static class SketchCamera
    {
        public static void Apply(Camera cam)
        {
            // Unity world = sketch with z negated.
            Vector3 fwd = new Vector3(0f, -SketchConst.SE, SketchConst.CE);
            Vector3 up = new Vector3(0f, SketchConst.CE, SketchConst.SE);
            cam.orthographic = true;
            cam.orthographicSize = SketchConst.FrameH * 0.5f;
            cam.transform.SetPositionAndRotation(up * (SketchConst.GY - SketchConst.FrameH * 0.5f) - fwd * 400f, Quaternion.LookRotation(fwd, up));
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowMSAA = false;
            cam.allowHDR = false;
        }

        /// <summary>Fits the 680-unit frame width to any screen aspect (portrait phones), centre unchanged.</summary>
        public static void FitWidth(Camera cam, float aspect)
        {
            cam.orthographicSize = Mathf.Max(SketchConst.FrameH * 0.5f, SketchConst.FrameW * 0.5f / Mathf.Max(aspect, 0.01f));
        }
    }
}
