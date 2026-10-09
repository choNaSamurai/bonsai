using System;
using System.Diagnostics;
using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// B* spike renderer: wood (+soil) mesh, pot mesh, leaves mesh — three MeshRenderers with custom HLSL,
    /// depth-tested in the URP 2D Renderer. Root has localScale z = -1, so all mesh data and shaders stay
    /// in sketch space (right-handed, z toward the viewer).
    /// </summary>
    public sealed class SketchTreeRenderer : IDisposable
    {
        public const string WoodShader = "PocketArbor/Spike/Wood";
        public const string PotShader = "PocketArbor/Spike/Pot";
        public const string LeafShader = "PocketArbor/Spike/Leaves";
        public const string BakeShader = "Hidden/PocketArbor/Spike/SdfBake";

        public struct Stats
        {
            public double WoodMs, PotMs, LeafMs, UploadMs, BakeMs, BakeFillMs, BakeGpuMs;
            public int WoodVerts, WoodTris, PotVerts, PotTris, Leaves, Limbs;
            public int TrunkVerts, RootVerts, LimbVerts, SoilVerts;
        }

        public readonly GameObject Root;
        public readonly MeshRenderer Wood, Pot, Leaves;
        private readonly Mesh _woodMesh, _potMesh, _leafMesh;
        private readonly Material _woodMat, _potMat, _leafMat;
        private readonly WoodMeshBuilder _wood = new WoodMeshBuilder();
        private readonly LeafMeshBuilder _leaf = new LeafMeshBuilder();
        private readonly GeoBuffer _woodBuf = new GeoBuffer(1 << 16, 1 << 18);
        private readonly GeoBuffer _potBuf = new GeoBuffer(1 << 16, 1 << 18);
        private readonly SdfBaker _baker;
        private string _potKey;

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
            _woodMesh = NewMesh("PA102XS_Wood");
            _potMesh = NewMesh("PA102XS_Pot");
            _leafMesh = NewMesh("PA102XS_Leaves");
            Wood = NewPart("Wood", _woodMesh, _woodMat, 0, flags);
            Pot = NewPart("Pot", _potMesh, _potMat, 0, flags);
            Leaves = NewPart("Leaves", _leafMesh, _leafMat, 1, flags);
            _baker = new SdfBaker(FindShader(BakeShader));
        }

        public SdfBaker Baker => _baker;
        public WoodMeshBuilder WoodBuilder => _wood;

        public static Shader FindShader(string name)
        {
            Shader s = Shader.Find(name);
            if (s == null) throw new InvalidOperationException("Shader not found: " + name);
            return s;
        }

        private static Material NewMat(string shader)
        {
            return new Material(FindShader(shader)) { hideFlags = HideFlags.HideAndDontSave };
        }

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

        public Stats Show(SketchFrame f)
        {
            var st = new Stats();
            SpeciesLook look = SpeciesLook.For(f.Species, f.Style);
            var sw = Stopwatch.StartNew();

            if (_potKey != look.PotKey)
            {
                BuildPot(look);
                _potKey = look.PotKey;
                st.PotMs = sw.Elapsed.TotalMilliseconds;
            }
            sw.Restart();
            _wood.Build(f, look, _woodBuf);
            _woodBuf.AssignBakeTexels(_potBuf.VCount, SdfBaker.Width);
            _woodBuf.ApplyTo(_woodMesh, 8f);
            st.WoodMs = sw.Elapsed.TotalMilliseconds;
            st.Limbs = _wood.LimbCount;
            st.TrunkVerts = _wood.TrunkVerts;
            st.RootVerts = _wood.RootVerts;
            st.LimbVerts = _wood.LimbVerts;
            st.SoilVerts = _wood.SoilVerts;

            sw.Restart();
            st.Leaves = _leaf.Build(f, _leafMesh, FrontToBackLeaves);
            st.LeafMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            _baker.UploadScene(f, look);
            st.UploadMs = sw.Elapsed.TotalMilliseconds;
            st.BakeMs = _baker.Bake(_potBuf, _woodBuf, true);
            st.BakeFillMs = _baker.LastFillMs;
            st.BakeGpuMs = _baker.LastGpuMs;

            st.WoodVerts = _woodBuf.VCount;
            st.WoodTris = _woodBuf.TriangleCount;
            st.PotVerts = _potBuf.VCount;
            st.PotTris = _potBuf.TriangleCount;
            return st;
        }

        private void BuildPot(SpeciesLook look)
        {
            var sdf = new PotSdf(look);
            _potBuf.Clear();
            float hw = look.PotHw + 8f, hd = look.PotHd + 8f;
            Vector3 min = new Vector3(-hw, -look.PotDep - 9f, -hd), max = new Vector3(hw, 6.5f, hd);
            // Cavity walls and bottom below the soil (y < -1) are never visible.
            SurfaceNets.Build(sdf, min, max, PotVoxel, _potBuf, 2f, p => p.y < -1f && sdf.Inner(p.x, p.y, p.z) < 0.6f);
            _potBuf.AssignBakeTexels(0, SdfBaker.Width);
            _potBuf.ApplyTo(_potMesh, 1f);
        }

        public void SetOrders(int wood, int pot, int leaves)
        {
            Wood.sortingOrder = wood;
            Pot.sortingOrder = pot;
            Leaves.sortingOrder = leaves;
        }

        public void SetSortingLayer(MeshRenderer r, int layerId)
        {
            r.sortingLayerID = layerId;
        }

        public void Dispose()
        {
            _baker.Dispose();
            Destroy(_woodMesh);
            Destroy(_potMesh);
            Destroy(_leafMesh);
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
    }
}
