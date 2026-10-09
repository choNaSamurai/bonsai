using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// B1 bake: the sketch's SDF (DAT/CB/GM/PE) goes into one RGBA32F data texture (no uniform arrays);
    /// vertex positions/normals go into float textures; three full-screen passes of the same HLSL port
    /// (TreeSdf.hlsl) write per-vertex results that the mesh vertex shaders read with texel fetches:
    ///   pass 0 Snap  : (normal from SDF gradient, displacement along the original normal) — smin fillets;
    ///   pass 1 Light : (shd, folT, ao, folIn);
    ///   pass 2 Mat   : (trunk maturity tr, wood/soil blend h, pot flag, 0).
    /// Synchronous (Graphics.ExecuteCommandBuffer) when the scene changes.
    /// </summary>
    public sealed class SdfBaker : IDisposable
    {
        public const int Width = 1024;

        private static readonly int IdSdf = Shader.PropertyToID("_SkSdf");
        private static readonly int IdPos = Shader.PropertyToID("_SkBakePos");
        private static readonly int IdNrm = Shader.PropertyToID("_SkBakeNrm");
        private static readonly int IdSnap = Shader.PropertyToID("_SkBakeSnap");
        private static readonly int IdLight = Shader.PropertyToID("_SkBakeLight");
        private static readonly int IdMat = Shader.PropertyToID("_SkBakeMat");

        private readonly Material _mat;
        private readonly Texture2D _sdf;
        private readonly float[] _sdfData = new float[Width * 2 * 4];
        private readonly CommandBuffer _cb = new CommandBuffer { name = "PA102XS SdfBake" };
        private Texture2D _pos, _nrm;
        private float[] _posData = new float[4], _nrmData = new float[4];
        private RenderTexture _snap, _light, _mt;
        private int _rows;

        public SdfBaker(Shader bake)
        {
            _mat = new Material(bake) { hideFlags = HideFlags.HideAndDontSave };
            _sdf = new Texture2D(Width, 2, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = "PA102XS_Sdf"
            };
        }

        public RenderTexture SnapRT => _snap;
        public RenderTexture LightRT => _light;
        public RenderTexture MatRT => _mt;

        /// <summary>Packs the frame's SDF arrays into the data texture and sets all scene globals.</summary>
        public void UploadScene(SketchFrame f, SpeciesLook look)
        {
            Array.Clear(_sdfData, 0, _sdfData.Length);
            Array.Copy(f.Dat, 0, _sdfData, 0, SketchConst.TW * 4);
            int row1 = Width * 4;
            Array.Copy(f.Cb, 0, _sdfData, row1 + SketchConst.RowCb * 4, f.Cb.Length);
            Array.Copy(f.Gm, 0, _sdfData, row1 + SketchConst.RowGm * 4, f.Gm.Length);
            Array.Copy(f.Pe, 0, _sdfData, row1 + SketchConst.RowPe * 4, f.Pe.Length);
            _sdf.SetPixelData(_sdfData, 0);
            _sdf.Apply(false, false);
            Shader.SetGlobalTexture(IdSdf, _sdf);
            SketchGlobals.Set(f, look);
        }

        /// <summary>Bakes all vertices of the given buffers (texels must be assigned in this order). Returns ms (CPU wall, GPU synced if requested).</summary>
        public double LastFillMs { get; private set; }
        public double LastGpuMs { get; private set; }

        public double Bake(GeoBuffer a, GeoBuffer b, bool syncGpu)
        {
            var sw = Stopwatch.StartNew();
            int total = (a != null ? a.VCount : 0) + (b != null ? b.VCount : 0);
            int rows = Mathf.Max(1, (total + Width - 1) / Width);
            EnsureTargets(rows);
            int filled = 0;
            if (a != null) filled = Fill(a, filled);
            if (b != null) filled = Fill(b, filled);
            for (int i = filled; i < Width * _rows; i++)
            {
                int o = i * 4;
                _posData[o] = 0; _posData[o + 1] = 0; _posData[o + 2] = 0; _posData[o + 3] = 0;
                _nrmData[o] = 0; _nrmData[o + 1] = 1; _nrmData[o + 2] = 0; _nrmData[o + 3] = -1;
            }
            _pos.SetPixelData(_posData, 0);
            _pos.Apply(false, false);
            _nrm.SetPixelData(_nrmData, 0);
            _nrm.Apply(false, false);

            LastFillMs = sw.Elapsed.TotalMilliseconds;
            _cb.Clear();
            _cb.SetGlobalTexture(IdPos, _pos);
            _cb.SetGlobalTexture(IdNrm, _nrm);
            _cb.SetRenderTarget(_snap);
            _cb.DrawProcedural(Matrix4x4.identity, _mat, 0, MeshTopology.Triangles, 3);
            _cb.SetGlobalTexture(IdSnap, _snap);
            _cb.SetRenderTarget(_light);
            _cb.DrawProcedural(Matrix4x4.identity, _mat, 1, MeshTopology.Triangles, 3);
            _cb.SetRenderTarget(_mt);
            _cb.DrawProcedural(Matrix4x4.identity, _mat, 2, MeshTopology.Triangles, 3);
            _cb.SetGlobalTexture(IdLight, _light);
            _cb.SetGlobalTexture(IdMat, _mt);
            Graphics.ExecuteCommandBuffer(_cb);
            if (syncGpu)
            {
                AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(_mt);
                req.WaitForCompletion();
            }
            LastGpuMs = sw.Elapsed.TotalMilliseconds - LastFillMs;
            return sw.Elapsed.TotalMilliseconds;
        }

        private int Fill(GeoBuffer g, int at)
        {
            for (int v = 0; v < g.VCount; v++)
            {
                int o = (at + v) * 4;
                Vector3 p = g.Pos[v], n = g.Nrm[v];
                _posData[o] = p.x; _posData[o + 1] = p.y; _posData[o + 2] = p.z; _posData[o + 3] = g.TMax[v];
                _nrmData[o] = n.x; _nrmData[o + 1] = n.y; _nrmData[o + 2] = n.z; _nrmData[o + 3] = g.Kind[v];
            }
            return at + g.VCount;
        }

        private void EnsureTargets(int rows)
        {
            if (_snap != null && rows <= _rows) return;
            ReleaseTargets();
            _rows = Mathf.NextPowerOfTwo(rows);
            _posData = new float[Width * _rows * 4];
            _nrmData = new float[Width * _rows * 4];
            _pos = NewDataTex("PA102XS_BakePos");
            _nrm = NewDataTex("PA102XS_BakeNrm");
            _snap = NewRT("PA102XS_Snap");
            _light = NewRT("PA102XS_Light");
            _mt = NewRT("PA102XS_Mat");
        }

        private Texture2D NewDataTex(string name)
        {
            return new Texture2D(Width, _rows, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = name
            };
        }

        private RenderTexture NewRT(string name)
        {
            var rt = new RenderTexture(Width, _rows, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave, name = name
            };
            rt.Create();
            return rt;
        }

        private void ReleaseTargets()
        {
            if (_snap != null) { _snap.Release(); UnityEngine.Object.DestroyImmediate(_snap); }
            if (_light != null) { _light.Release(); UnityEngine.Object.DestroyImmediate(_light); }
            if (_mt != null) { _mt.Release(); UnityEngine.Object.DestroyImmediate(_mt); }
            if (_pos != null) UnityEngine.Object.DestroyImmediate(_pos);
            if (_nrm != null) UnityEngine.Object.DestroyImmediate(_nrm);
            _snap = _light = _mt = null;
            _pos = _nrm = null;
        }

        public void Dispose()
        {
            ReleaseTargets();
            if (_sdf != null) UnityEngine.Object.DestroyImmediate(_sdf);
            if (_mat != null) UnityEngine.Object.DestroyImmediate(_mat);
            _cb.Release();
        }
    }

    /// <summary>Scene uniforms of the sketch FS/LFS as shader globals (_Sk*). Spike: one tree at a time.</summary>
    public static class SketchGlobals
    {
        private static readonly int L = Shader.PropertyToID("_SkL"), SunC = Shader.PropertyToID("_SkSunC"), Amb = Shader.PropertyToID("_SkAmb");
        private static readonly int Pot = Shader.PropertyToID("_SkPot"), PotC = Shader.PropertyToID("_SkPotC");
        private static readonly int Sun = Shader.PropertyToID("_SkSun"), SI = Shader.PropertyToID("_SkSI"), Kr = Shader.PropertyToID("_SkKr");
        private static readonly int NR = Shader.PropertyToID("_SkNR"), NG = Shader.PropertyToID("_SkNG"), NP = Shader.PropertyToID("_SkNP");
        private static readonly int FD = Shader.PropertyToID("_SkFD"), Fol = Shader.PropertyToID("_SkFol"), PotT = Shader.PropertyToID("_SkPotT");
        private static readonly int SpB = Shader.PropertyToID("_SkSpB"), Age = Shader.PropertyToID("_SkAge"), Rb = Shader.PropertyToID("_SkRb");
        private static readonly int Bump = Shader.PropertyToID("_SkBump"), Moss = Shader.PropertyToID("_SkMoss"), Tr = Shader.PropertyToID("_SkTr");

        public static void Set(SketchFrame f, SpeciesLook s)
        {
            Shader.SetGlobalVector(L, new Vector4(f.LightDir.x, f.LightDir.y, f.LightDir.z, 0f));
            Shader.SetGlobalVector(SunC, new Vector4(f.SunColor.x, f.SunColor.y, f.SunColor.z, 0f));
            Shader.SetGlobalVector(Amb, new Vector4(f.Ambient.x, f.Ambient.y, f.Ambient.z, 0f));
            Shader.SetGlobalVector(Pot, new Vector4(s.PotHw, s.PotHd, s.PotShape, s.PotDep));
            Shader.SetGlobalVector(PotC, new Vector4(s.PotColor.x, s.PotColor.y, s.PotColor.z, 0f));
            Shader.SetGlobalFloat(Sun, f.Sun);
            Shader.SetGlobalFloat(SI, f.SunIntensity);
            Shader.SetGlobalFloat(Kr, f.Kr);
            Shader.SetGlobalFloat(NR, f.Nr);
            Shader.SetGlobalFloat(NG, f.Ng);
            Shader.SetGlobalFloat(NP, f.Np);
            Shader.SetGlobalFloat(FD, s.Fd * f.Foliage);
            Shader.SetGlobalFloat(Fol, f.Foliage);
            Shader.SetGlobalFloat(PotT, s.PotGlaze);
            Shader.SetGlobalFloat(SpB, s.Id);
            Shader.SetGlobalFloat(Age, f.Age);
            Shader.SetGlobalFloat(Rb, f.R);
            Shader.SetGlobalFloat(Bump, s.Bump);
            Shader.SetGlobalFloat(Moss, s.Moss);
            Shader.SetGlobalFloat(Tr, s.Tr);
        }
    }
}
