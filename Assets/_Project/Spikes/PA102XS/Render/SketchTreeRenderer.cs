using System;
using System.Diagnostics;
using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// B* spike renderer: wood (+soil) mesh, pot mesh, leaves mesh — three MeshRenderers with custom HLSL, depth-tested
    /// in the URP 2D Renderer. Root has localScale z = -1, so all mesh data and shaders stay in sketch space.
    /// Phase 2a: wood geometry, its bake target and the leaves are double-buffered — a rebuild bakes into the back
    /// buffers over N frames and swaps; a cut removes the branch's indices on the displayed mesh at once and
    /// re-bakes the light of the same vertices over N frames; the pot is baked once per pot.
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

        public struct Stats
        {
            public double WoodMs, PotMs, LeafMs, UploadMs, BakeMs, BakeFillMs, BakeGpuMs;
            public int WoodVerts, WoodTris, PotVerts, PotTris, Leaves, Limbs;
            public int TrunkVerts, RootVerts, LimbVerts, SoilVerts;
        }

        private enum Pending { None, Rebuild, Rebake }

        public readonly GameObject Root;
        public readonly MeshRenderer Wood, Pot, Leaves;
        private readonly MeshFilter _woodMf, _leafMf;
        private readonly Mesh[] _woodMesh = new Mesh[2], _leafMesh = new Mesh[2];
        private readonly GeoBuffer[] _woodGeo = { new GeoBuffer(1 << 16, 1 << 18), new GeoBuffer(1 << 16, 1 << 18) };
        private readonly BakeTarget[] _woodBake = { new BakeTarget("PA102XS_WoodA"), new BakeTarget("PA102XS_WoodB") };
        private readonly LeafMeshBuilder[] _leaf = { new LeafMeshBuilder(), new LeafMeshBuilder() };
        private readonly Mesh _potMesh;
        private readonly GeoBuffer _potGeo = new GeoBuffer(1 << 16, 1 << 18);
        private readonly BakeTarget _potBake = new BakeTarget("PA102XS_Pot");
        private readonly Material _woodMat, _potMat, _leafMat;
        private readonly WoodMeshBuilder _wood = new WoodMeshBuilder();
        private readonly SdfBaker _baker;
        private readonly MaterialPropertyBlock _woodMpb = new MaterialPropertyBlock(), _potMpb = new MaterialPropertyBlock();
        private readonly SketchFrame _cur = new SketchFrame();
        private int _dispMesh, _dispBake, _pendMesh, _pendBake;
        private Pending _pending;
        private string _potKey;
        private int[] _cutIdx, _cutLeafIdx;
        private bool[] _leafRemoved = new bool[SketchConst.MAXLEAF];
        private int[] _leafGroup = new int[SketchConst.MAXLEAF];

        public bool FrontToBackLeaves = true;
        public float PotVoxel = 1.5f;

        public SketchTreeRenderer(Transform parent, HideFlags flags = HideFlags.DontSave)
        {
            Root = new GameObject("PA102XS_Tree") { hideFlags = flags };
            if (parent != null) Root.transform.SetParent(parent, false);
            Root.transform.localScale = new Vector3(1f, 1f, -1f);
            _woodMat = NewMat(WoodShader);
            _potMat = NewMat(PotShader);
            _leafMat = NewMat(LeafShader);
            for (int i = 0; i < 2; i++)
            {
                _woodMesh[i] = NewMesh("PA102XS_Wood" + i);
                _leafMesh[i] = NewMesh("PA102XS_Leaves" + i);
            }
            _potMesh = NewMesh("PA102XS_Pot");
            Wood = NewPart("Wood", _woodMesh[0], _woodMat, 0, flags, out _woodMf);
            Pot = NewPart("Pot", _potMesh, _potMat, 0, flags, out _);
            Leaves = NewPart("Leaves", _leafMesh[0], _leafMat, 1, flags, out _leafMf);
            _baker = new SdfBaker(FindShader(BakeShader));
        }

        public SdfBaker Baker => _baker;
        public BakeTarget DisplayedBake => _woodBake[_dispBake];
        public WoodMeshBuilder WoodBuilder => _wood;
        public SketchFrame Current => _cur;
        public bool Busy => _pending != Pending.None;
        public int DisplayedLeafCount => _leaf[_dispMesh].Count;

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

        private MeshRenderer NewPart(string name, Mesh mesh, Material mat, int order, HideFlags flags, out MeshFilter mf)
        {
            var go = new GameObject(name) { hideFlags = flags };
            go.transform.SetParent(Root.transform, false);
            mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = order;
            return mr;
        }

        // ------------------------------------------------------------------ synchronous (editor harness, first show)

        /// <summary>Builds and bakes everything now and displays it.</summary>
        public Stats Show(SketchFrame f)
        {
            Stats st = BuildBack(f);
            var sw = Stopwatch.StartNew();
            st.BakeFillMs = _woodBake[_pendBake].Fill(_woodGeo[_pendMesh]);
            double gpu = _baker.BakeFull(_woodBake[_pendBake], true);
            st.BakeGpuMs = gpu;
            st.BakeMs = sw.Elapsed.TotalMilliseconds;
            _pending = Pending.Rebuild;
            Swap();
            return st;
        }

        // ------------------------------------------------------------------ sliced (device)

        /// <summary>Daily growth: builds geometry/leaves into the back buffers now, bakes over <paramref name="frames"/> Tick()s, then swaps.</summary>
        public Stats BeginRebuild(SketchFrame f, int frames)
        {
            Stats st = BuildBack(f);
            var sw = Stopwatch.StartNew();
            st.BakeFillMs = _woodBake[_pendBake].Fill(_woodGeo[_pendMesh]);
            _baker.BeginSliced(_woodBake[_pendBake], frames);
            _pending = Pending.Rebuild;
            st.BakeMs = sw.Elapsed.TotalMilliseconds;
            return st;
        }

        /// <summary>Call once per frame. Returns true on the frame the new light/geometry became visible.</summary>
        public bool Tick()
        {
            if (_pending == Pending.None) return false;
            if (!_baker.Step()) return false;
            Swap();
            return true;
        }

        /// <summary>
        /// Cut: the displayed tree loses branch group <paramref name="group"/> (wood limbs + its leaves) in this call
        /// (visible next frame); the SDF drops the group and its pad; light of the same vertices is re-baked into the
        /// back target over <paramref name="frames"/> Tick()s. Returns CPU ms of the immediate part.
        /// <see cref="PrepareCut"/> must have been called for the displayed tree (leaf → branch assignment).
        /// </summary>
        public double Cut(int group, int frames)
        {
            var sw = Stopwatch.StartNew();
            GeoBuffer g = _woodGeo[_dispMesh];
            int n = _wood.IndicesWithoutGroup(g, group, ref _cutIdx);
            Mesh wm = _woodMesh[_dispMesh];
            wm.SetIndices(_cutIdx, 0, n, MeshTopology.Triangles, 0, false);
            LeafMeshBuilder lb = _leaf[_dispMesh];
            for (int i = 0; i < _cur.Nl; i++) _leafRemoved[i] = _leafGroup[i] == group;
            int ln = lb.IndicesWithout(_leafRemoved, ref _cutLeafIdx);
            _leafMesh[_dispMesh].SetIndices(_cutLeafIdx, 0, ln, MeshTopology.Triangles, 0, false);
            _cur.RemoveGroup(group);
            SpeciesLook look = SpeciesLook.For(_cur.Species, _cur.Style);
            _baker.UploadScene(_cur, look);
            _pendBake = 1 - _dispBake;
            _pendMesh = _dispMesh;
            _woodBake[_pendBake].CopyInputFrom(_woodBake[_dispBake]);
            _baker.BeginSliced(_woodBake[_pendBake], frames);
            _pending = Pending.Rebake;
            return sw.Elapsed.TotalMilliseconds;
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
        }

        /// <summary>Chronicle / scrubbing: geometry and leaves rebuilt and shown at once, no bake (_SkNoBake).</summary>
        public Stats ShowNoBake(SketchFrame f)
        {
            Stats st = BuildBack(f);
            _woodMpb.SetFloat(IdNoBake, 1f);
            _dispMesh = _pendMesh;
            _woodMf.sharedMesh = _woodMesh[_dispMesh];
            _leafMf.sharedMesh = _leafMesh[_dispMesh];
            Wood.SetPropertyBlock(_woodMpb);
            _pending = Pending.None;
            return st;
        }

        // ------------------------------------------------------------------ internals

        private Stats BuildBack(SketchFrame f)
        {
            if (_pending != Pending.None) FinishPendingNow();
            var st = new Stats();
            _cur.CopyFrom(f);
            SpeciesLook look = SpeciesLook.For(f.Species, f.Style);
            var sw = Stopwatch.StartNew();
            _baker.UploadScene(f, look);
            st.UploadMs = sw.Elapsed.TotalMilliseconds;

            if (_potKey != look.PotKey)
            {
                sw.Restart();
                BuildPot(look);
                _potKey = look.PotKey;
                st.PotMs = sw.Elapsed.TotalMilliseconds;
            }

            _pendMesh = 1 - _dispMesh;
            _pendBake = 1 - _dispBake;
            GeoBuffer g = _woodGeo[_pendMesh];
            sw.Restart();
            _wood.Build(f, look, g);
            g.AssignBakeTexels(0, SdfBaker.Width);
            g.ApplyTo(_woodMesh[_pendMesh], 8f);
            st.WoodMs = sw.Elapsed.TotalMilliseconds;
            st.Limbs = _wood.LimbCount;
            st.TrunkVerts = _wood.TrunkVerts;
            st.RootVerts = _wood.RootVerts;
            st.LimbVerts = _wood.LimbVerts;
            st.SoilVerts = _wood.SoilVerts;

            sw.Restart();
            st.Leaves = _leaf[_pendMesh].Build(f, _leafMesh[_pendMesh], FrontToBackLeaves);
            st.LeafMs = sw.Elapsed.TotalMilliseconds;

            st.WoodVerts = g.VCount;
            st.WoodTris = g.TriangleCount;
            st.PotVerts = _potGeo.VCount;
            st.PotTris = _potGeo.TriangleCount;
            return st;
        }

        private void FinishPendingNow()
        {
            while (!_baker.Step()) { }
            Swap();
        }

        private void Swap()
        {
            if (_pending == Pending.Rebuild)
            {
                _dispMesh = _pendMesh;
                _woodMf.sharedMesh = _woodMesh[_dispMesh];
                _leafMf.sharedMesh = _leafMesh[_dispMesh];
            }
            _dispBake = _pendBake;
            _woodBake[_dispBake].Bind(_woodMpb);
            _woodMpb.SetFloat(IdNoBake, 0f);
            Wood.SetPropertyBlock(_woodMpb);
            _pending = Pending.None;
        }

        private void BuildPot(SpeciesLook look)
        {
            var sdf = new PotSdf(look);
            _potGeo.Clear();
            float hw = look.PotHw + 8f, hd = look.PotHd + 8f;
            Vector3 min = new Vector3(-hw, -look.PotDep - 9f, -hd), max = new Vector3(hw, 6.5f, hd);
            // Cavity walls and bottom below the soil (y < -1) are never visible.
            SurfaceNets.Build(sdf, min, max, PotVoxel, _potGeo, 2f, p => p.y < -1f && sdf.Inner(p.x, p.y, p.z) < 0.6f);
            _potGeo.AssignBakeTexels(0, SdfBaker.Width);
            _potGeo.ApplyTo(_potMesh, 1f);
            // Baked once per pot (with the tree of that moment), not on every tree change.
            _potBake.Fill(_potGeo);
            _baker.BakeFull(_potBake, false);
            _potBake.Bind(_potMpb);
            _potMpb.SetFloat(IdNoBake, 0f);
            Pot.SetPropertyBlock(_potMpb);
        }

        public void SetOrders(int wood, int pot, int leaves)
        {
            Wood.sortingOrder = wood;
            Pot.sortingOrder = pot;
            Leaves.sortingOrder = leaves;
        }

        /// <summary>Estimated GPU memory of the tree: meshes (both buffers), bake targets, SDF texture.</summary>
        public long GpuBytesEstimate()
        {
            long b = 0;
            for (int i = 0; i < 2; i++)
            {
                b += MeshBytes(_woodMesh[i], 36);
                b += MeshBytes(_leafMesh[i], 72);
                b += _woodBake[i].GpuBytes;
            }
            b += MeshBytes(_potMesh, 36) + _potBake.GpuBytes + _baker.SdfBytes;
            return b;
        }

        private static long MeshBytes(Mesh m, int stride)
        {
            long idx = 0;
            for (int s = 0; s < m.subMeshCount; s++) idx += (long)m.GetIndexCount(s);
            return (long)m.vertexCount * stride + idx * (m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
        }

        public void Dispose()
        {
            _baker.Dispose();
            for (int i = 0; i < 2; i++)
            {
                _woodBake[i].Dispose();
                Destroy(_woodMesh[i]);
                Destroy(_leafMesh[i]);
            }
            _potBake.Dispose();
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
