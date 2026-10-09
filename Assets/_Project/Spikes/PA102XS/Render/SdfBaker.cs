using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Inputs and outputs of one per-vertex bake (one texel per vertex, 1024 texels per row):
    /// Pos (xyz, tmax) / Nrm (xyz, kind) — RGBA32F inputs; Surf (q, t) — RGBA32F; Snap (n, t), Light, Mat — RGBA16F outputs
    /// read by the mesh vertex shaders (bound with a MaterialPropertyBlock, so two targets can be double-buffered).
    /// </summary>
    public sealed class BakeTarget : IDisposable
    {
        public const int Width = 1024;
        private static readonly int IdSnap = Shader.PropertyToID("_SkBakeSnap");
        private static readonly int IdLight = Shader.PropertyToID("_SkBakeLight");
        private static readonly int IdMat = Shader.PropertyToID("_SkBakeMat");

        public readonly string Name;
        public Texture2D Pos, Nrm;
        public RenderTexture Surf, Snap, Light, Mat;
        public int Rows;
        public int VertexCount;
        private float[] _posData = new float[4], _nrmData = new float[4];

        public BakeTarget(string name) { Name = name; }

        /// <summary>GPU bytes of all textures of this target.</summary>
        public long GpuBytes => Rows == 0 ? 0 : (long)Width * Rows * (16 + 16 + 16 + 8 + 8 + 8);

        /// <summary>Fills and uploads Pos/Nrm from the geometry (texels must be assigned 0..VCount-1). Returns CPU ms.</summary>
        public double Fill(GeoBuffer g)
        {
            var sw = Stopwatch.StartNew();
            Ensure(g.VCount);
            VertexCount = g.VCount;
            for (int v = 0; v < g.VCount; v++)
            {
                int o = v * 4;
                Vector3 p = g.Pos[v], n = g.Nrm[v];
                _posData[o] = p.x; _posData[o + 1] = p.y; _posData[o + 2] = p.z; _posData[o + 3] = g.TMax[v];
                _nrmData[o] = n.x; _nrmData[o + 1] = n.y; _nrmData[o + 2] = n.z; _nrmData[o + 3] = g.Kind[v];
            }
            for (int i = g.VCount; i < Width * Rows; i++)
            {
                int o = i * 4;
                _posData[o] = 0; _posData[o + 1] = 0; _posData[o + 2] = 0; _posData[o + 3] = 0;
                _nrmData[o] = 0; _nrmData[o + 1] = 1; _nrmData[o + 2] = 0; _nrmData[o + 3] = -1;
            }
            Pos.SetPixelData(_posData, 0);
            Pos.Apply(false, false);
            Nrm.SetPixelData(_nrmData, 0);
            Nrm.Apply(false, false);
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Same vertex set as another target (e.g. re-bake after a cut): GPU copy of the inputs.</summary>
        public void CopyInputFrom(BakeTarget o)
        {
            Ensure(o.VertexCount);
            VertexCount = o.VertexCount;
            if (Rows == o.Rows)
            {
                Graphics.CopyTexture(o.Pos, Pos);
                Graphics.CopyTexture(o.Nrm, Nrm);
            }
            else
            {
                Array.Copy(o._posData, _posData, Math.Min(o._posData.Length, _posData.Length));
                Array.Copy(o._nrmData, _nrmData, Math.Min(o._nrmData.Length, _nrmData.Length));
                for (int i = o.Width4(); i < _nrmData.Length; i += 4) _nrmData[i + 3] = -1;
                Pos.SetPixelData(_posData, 0);
                Pos.Apply(false, false);
                Nrm.SetPixelData(_nrmData, 0);
                Nrm.Apply(false, false);
            }
        }

        private int Width4() => Width * Rows * 4;

        public void Bind(MaterialPropertyBlock mpb)
        {
            mpb.SetTexture(IdSnap, Snap);
            mpb.SetTexture(IdLight, Light);
            mpb.SetTexture(IdMat, Mat);
        }

        /// <summary>Capacity in rows: needed rows rounded up to 8; re-allocated only when it grows.</summary>
        private void Ensure(int vcount)
        {
            int need = Math.Max(1, (vcount + Width - 1) / Width);
            need = (need + 7) / 8 * 8;
            if (Snap != null && need <= Rows) return;
            Release();
            Rows = need;
            _posData = new float[Width * Rows * 4];
            _nrmData = new float[Width * Rows * 4];
            Pos = NewTex(Name + "_Pos");
            Nrm = NewTex(Name + "_Nrm");
            Surf = NewRT(Name + "_Surf", RenderTextureFormat.ARGBFloat);
            Snap = NewRT(Name + "_Snap", RenderTextureFormat.ARGBHalf);
            Light = NewRT(Name + "_Light", RenderTextureFormat.ARGBHalf);
            Mat = NewRT(Name + "_Mat", RenderTextureFormat.ARGBHalf);
        }

        private Texture2D NewTex(string name)
        {
            return new Texture2D(Width, Rows, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = name
            };
        }

        private RenderTexture NewRT(string name, RenderTextureFormat f)
        {
            var rt = new RenderTexture(Width, Rows, 0, f, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave, name = name
            };
            rt.Create();
            return rt;
        }

        private void Release()
        {
            foreach (RenderTexture rt in new[] { Surf, Snap, Light, Mat })
            {
                if (rt == null) continue;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            if (Pos != null) UnityEngine.Object.DestroyImmediate(Pos);
            if (Nrm != null) UnityEngine.Object.DestroyImmediate(Nrm);
            Surf = Snap = Light = Mat = null;
            Pos = Nrm = null;
            Rows = 0;
        }

        public void Dispose() { Release(); }
    }

    /// <summary>
    /// B1 bake: the sketch's SDF (DAT/CB/GM/PE) goes into one RGBA32F data texture (no uniform arrays); four full-screen
    /// passes of the same HLSL port (TreeSdf.hlsl) bake a <see cref="BakeTarget"/>: 0 Surf (projection once), 1 Snap,
    /// 2 Light, 3 Mat. Either all at once (<see cref="BakeFull"/>) or sliced over N frames in horizontal bands
    /// (scissor) — <see cref="BeginSliced"/> / <see cref="Step"/>.
    /// </summary>
    public sealed class SdfBaker : IDisposable
    {
        public const int Width = BakeTarget.Width;
        // Relative GPU cost per row of the passes (Surf: march + projection; Light: 12-step shadow + AO + canopy).
        private static readonly float[] PassCost = { 0.42f, 0.08f, 0.42f, 0.08f };

        private static readonly int IdSdf = Shader.PropertyToID("_SkSdf");
        private static readonly int IdPos = Shader.PropertyToID("_SkBakePos");
        private static readonly int IdNrm = Shader.PropertyToID("_SkBakeNrm");
        private static readonly int IdSurf = Shader.PropertyToID("_SkBakeSurf");
        private static readonly int IdSnap = Shader.PropertyToID("_SkBakeSnap");

        private struct Slice { public int Pass, Row0, RowN, Frame; }

        private readonly Material _mat;
        private readonly Texture2D _sdf;
        private readonly float[] _sdfData = new float[Width * 2 * 4];
        private readonly CommandBuffer _cb = new CommandBuffer { name = "PA102XS SdfBake" };
        private readonly List<Slice> _slices = new List<Slice>(64);
        private BakeTarget _job;
        private int _jobFrame, _jobNext, _jobFrames;

        public SdfBaker(Shader bake)
        {
            _mat = new Material(bake) { hideFlags = HideFlags.HideAndDontSave };
            _sdf = new Texture2D(Width, 2, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = "PA102XS_Sdf"
            };
        }

        public bool Busy => _job != null;
        public BakeTarget Job => _job;
        public long SdfBytes => Width * 2 * 16;

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

        private void SetInputs(BakeTarget t)
        {
            _cb.SetGlobalTexture(IdPos, t.Pos);
            _cb.SetGlobalTexture(IdNrm, t.Nrm);
            _cb.SetGlobalTexture(IdSurf, t.Surf);
            _cb.SetGlobalTexture(IdSnap, t.Snap);
        }

        private static RenderTexture PassTarget(BakeTarget t, int pass)
        {
            return pass == 0 ? t.Surf : pass == 1 ? t.Snap : pass == 2 ? t.Light : t.Mat;
        }

        /// <summary>All four passes now. Returns CPU ms incl. GPU sync when requested.</summary>
        public double BakeFull(BakeTarget t, bool syncGpu)
        {
            var sw = Stopwatch.StartNew();
            _cb.Clear();
            SetInputs(t);
            for (int p = 0; p < 4; p++)
            {
                _cb.SetRenderTarget(PassTarget(t, p));
                _cb.DrawProcedural(Matrix4x4.identity, _mat, p, MeshTopology.Triangles, 3);
            }
            Graphics.ExecuteCommandBuffer(_cb);
            if (syncGpu) AsyncGPUReadback.Request(t.Mat).WaitForCompletion();
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>GPU wall time of one pass over the whole target (synced before and after) — device profiling.</summary>
        public double TimePass(BakeTarget t, int pass)
        {
            AsyncGPUReadback.Request(t.Mat, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            var sw = Stopwatch.StartNew();
            _cb.Clear();
            SetInputs(t);
            _cb.SetRenderTarget(PassTarget(t, pass));
            _cb.DrawProcedural(Matrix4x4.identity, _mat, pass, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_cb);
            AsyncGPUReadback.Request(PassTarget(t, pass), 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Per-row relative cost of the passes used for slicing (set from device measurements).</summary>
        public static void SetPassCost(float surf, float snap, float light, float mat)
        {
            PassCost[0] = surf; PassCost[1] = snap; PassCost[2] = light; PassCost[3] = mat;
        }

        /// <summary>Starts a bake of <paramref name="t"/> spread over <paramref name="frames"/> calls of <see cref="Step"/>.</summary>
        public void BeginSliced(BakeTarget t, int frames)
        {
            frames = Math.Max(1, frames);
            _slices.Clear();
            float total = 0f;
            for (int p = 0; p < 4; p++) total += PassCost[p];
            float acc = 0f;
            for (int p = 0; p < 4; p++)
            {
                int bands = Math.Max(1, Mathf.CeilToInt(frames * PassCost[p] / total));
                for (int b = 0; b < bands; b++)
                {
                    int r0 = t.Rows * b / bands, r1 = t.Rows * (b + 1) / bands;
                    float cost = PassCost[p] / bands;
                    int frame = Math.Min(frames - 1, (int)((acc + cost * 0.5f) / total * frames));
                    acc += cost;
                    if (r1 > r0) _slices.Add(new Slice { Pass = p, Row0 = r0, RowN = r1 - r0, Frame = frame });
                }
            }
            _job = t;
            _jobFrame = 0;
            _jobNext = 0;
            _jobFrames = frames;
        }

        /// <summary>Issues this frame's bands. Returns true when the job has completed (all bands issued).</summary>
        public bool Step()
        {
            if (_job == null) return true;
            _cb.Clear();
            SetInputs(_job);
            int issued = 0;
            while (_jobNext < _slices.Count && _slices[_jobNext].Frame <= _jobFrame)
            {
                Slice s = _slices[_jobNext++];
                _cb.SetRenderTarget(PassTarget(_job, s.Pass));
                _cb.EnableScissorRect(new Rect(0, s.Row0, Width, s.RowN));
                _cb.DrawProcedural(Matrix4x4.identity, _mat, s.Pass, MeshTopology.Triangles, 3);
                _cb.DisableScissorRect();
                issued++;
            }
            if (issued > 0) Graphics.ExecuteCommandBuffer(_cb);
            _jobFrame++;
            if (_jobNext >= _slices.Count)
            {
                _job = null;
                return true;
            }
            return false;
        }

        public void Dispose()
        {
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
        private static readonly int Stiff = Shader.PropertyToID("_SkStiff");
        public static readonly int Wind = Shader.PropertyToID("_SkWind"), Time = Shader.PropertyToID("_SkTime");

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
            Shader.SetGlobalFloat(Stiff, s.Stiff);
        }

        /// <summary>Per-frame wind (0 B GC).</summary>
        public static void SetWind(float wind, float time)
        {
            Shader.SetGlobalFloat(Wind, wind);
            Shader.SetGlobalFloat(Time, time);
        }
    }
}
