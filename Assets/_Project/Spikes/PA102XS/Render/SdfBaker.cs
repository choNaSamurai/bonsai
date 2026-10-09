using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Persistent per-vertex bake results read by the mesh vertex shaders (one texel per vertex, 1024 per row):
    /// Snap (n, t) RGBA16F, Light (shd, folT, ao, folIn) RGBA8, Mat (tr, h, pot, 0) RGBA8 — 16 B per texel (C3).
    /// RenderTexture objects live for the renderer's lifetime; GPU memory is freed with Release() (no managed allocs).
    /// </summary>
    public sealed class BakeOutputs : IDisposable
    {
        public const int Width = 1024;
        private static readonly int IdSnap = Shader.PropertyToID("_SkBakeSnap");
        private static readonly int IdLight = Shader.PropertyToID("_SkBakeLight");
        private static readonly int IdMat = Shader.PropertyToID("_SkBakeMat");

        public readonly RenderTexture Snap, Light, Mat;
        public int Rows { get; private set; }

        public BakeOutputs(string name)
        {
            Snap = NewRT(name + "_Snap", RenderTextureFormat.ARGBHalf);
            Light = NewRT(name + "_Light", RenderTextureFormat.ARGB32);
            Mat = NewRT(name + "_Mat", RenderTextureFormat.ARGB32);
        }

        public bool Allocated => Rows > 0;
        public long GpuBytes => (long)Width * Rows * 16;

        public static int RowsFor(int vertexCount)
        {
            int need = Math.Max(1, (vertexCount + Width - 1) / Width);
            return (need + 7) / 8 * 8;
        }

        /// <summary>(Re)allocates GPU storage for <paramref name="rows"/> rows if the size differs.</summary>
        public void Ensure(int rows)
        {
            if (rows == Rows && Snap.IsCreated()) return;
            Release();
            Resize(Snap, rows);
            Resize(Light, rows);
            Resize(Mat, rows);
            Rows = rows;
        }

        public void Release()
        {
            Snap.Release();
            Light.Release();
            Mat.Release();
            Rows = 0;
        }

        private static void Resize(RenderTexture rt, int rows)
        {
            rt.width = Width;
            rt.height = rows;
            rt.Create();
        }

        public void Bind(MaterialPropertyBlock mpb)
        {
            mpb.SetTexture(IdSnap, Snap);
            mpb.SetTexture(IdLight, Light);
            mpb.SetTexture(IdMat, Mat);
        }

        private static RenderTexture NewRT(string name, RenderTextureFormat f)
        {
            return new RenderTexture(Width, 8, 0, f, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave, name = name
            };
        }

        public void Dispose()
        {
            Release();
            UnityEngine.Object.DestroyImmediate(Snap);
            UnityEngine.Object.DestroyImmediate(Light);
            UnityEngine.Object.DestroyImmediate(Mat);
        }
    }

    /// <summary>
    /// Transient inputs of a bake job: Pos (xyz, tmax) / Nrm (xyz, kind) RGBA32F, Mask R8 (cut re-bake), Surf (q, t)
    /// RGBA32F. CPU arrays are filled by the worker thread; Upload() runs on the main thread; ReleaseGpu() frees the
    /// GPU memory after the job (C3). Texture objects are reused (Reinitialize), so no managed allocations per job.
    /// </summary>
    public sealed class BakeInputs : IDisposable
    {
        public const int Width = BakeOutputs.Width;
        public readonly Texture2D Pos, Nrm, Mask;
        public readonly RenderTexture Surf;
        public int Rows { get; private set; }
        public int VertexCount { get; private set; }
        public float[] PosData = new float[4], NrmData = new float[4];
        public byte[] MaskData = new byte[1];
        // Per-row relative costs of the 4 passes for slicing (filled with the arrays).
        public float[][] RowCost = { new float[1], new float[1], new float[1], new float[1] };
        private int _gpuRows;

        public BakeInputs(string name)
        {
            Pos = NewTex(name + "_Pos", TextureFormat.RGBAFloat);
            Nrm = NewTex(name + "_Nrm", TextureFormat.RGBAFloat);
            Mask = NewTex(name + "_Mask", TextureFormat.R8);
            Surf = new RenderTexture(Width, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave, name = name + "_Surf"
            };
        }

        public long GpuBytes => (long)Width * _gpuRows * (16 + 16 + 1 + 16);

        private static Texture2D NewTex(string name, TextureFormat f)
        {
            return new Texture2D(1, 1, f, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = name };
        }

        /// <summary>CPU part (worker-safe): fills Pos/Nrm arrays and the per-row costs of a full bake. Mask = all 2.</summary>
        public void Fill(GeoBuffer g)
        {
            int rows = BakeOutputs.RowsFor(g.VCount);
            EnsureArrays(rows);
            Rows = rows;
            VertexCount = g.VCount;
            for (int v = 0; v < g.VCount; v++)
            {
                int o = v * 4;
                Vector3 p = g.Pos[v], n = g.Nrm[v];
                PosData[o] = p.x; PosData[o + 1] = p.y; PosData[o + 2] = p.z; PosData[o + 3] = g.TMax[v];
                NrmData[o] = n.x; NrmData[o + 1] = n.y; NrmData[o + 2] = n.z; NrmData[o + 3] = g.Kind[v];
            }
            for (int i = g.VCount; i < Width * rows; i++)
            {
                int o = i * 4;
                PosData[o] = 0; PosData[o + 1] = 0; PosData[o + 2] = 0; PosData[o + 3] = 0;
                NrmData[o] = 0; NrmData[o + 1] = 1; NrmData[o + 2] = 0; NrmData[o + 3] = -1;
            }
            for (int r = 0; r < rows; r++)
            {
                float surf = 0, other = 0;
                int v0 = r * Width, v1 = Math.Min(g.VCount, v0 + Width);
                for (int v = v0; v < v1; v++)
                {
                    surf += g.TMax[v] > 0f ? g.Cost[v] : 0.6f;
                    other += 1f;
                }
                RowCost[0][r] = surf + 1f;
                RowCost[1][r] = other * 0.4f + 1f;
                RowCost[2][r] = other * 3f + 1f;
                RowCost[3][r] = other * 0.4f + 1f;
            }
        }

        /// <summary>Per-row costs of a masked re-bake (cut): 2 = full, 1 = light only, 0 = copy.</summary>
        public void SetMaskCosts(byte[] mask, int vcount, float[] cost)
        {
            for (int r = 0; r < Rows; r++)
            {
                float s = 1f, n = 1f, l = 1f, m = 1f;
                int v0 = r * Width, v1 = Math.Min(vcount, v0 + Width);
                for (int v = v0; v < v1; v++)
                {
                    byte k = mask[v];
                    s += k == 2 ? Math.Max(cost[v], 5f) : 0.02f;
                    n += k == 2 ? 0.4f : 0.02f;
                    l += k >= 1 ? 3f : 0.02f;
                    m += k == 2 ? 0.4f : 0.02f;
                }
                RowCost[0][r] = s; RowCost[1][r] = n; RowCost[2][r] = l; RowCost[3][r] = m;
            }
        }

        private void EnsureArrays(int rows)
        {
            int n = Width * rows;
            // Exact sizes for Texture2D.SetPixelData; reallocated only when the row count changes (daily rebuild).
            if (PosData.Length != n * 4)
            {
                PosData = new float[n * 4];
                NrmData = new float[n * 4];
            }
            if (MaskData.Length != n) MaskData = new byte[n];
            if (RowCost[0].Length < rows)
                for (int p = 0; p < 4; p++) RowCost[p] = new float[rows];
        }

        /// <summary>Main thread: GPU upload of Pos/Nrm (and the mask when <paramref name="withMask"/>), Surf allocation.</summary>
        public void Upload(bool withMask)
        {
            if (_gpuRows != Rows)
            {
                Pos.Reinitialize(Width, Rows);
                Nrm.Reinitialize(Width, Rows);
                Mask.Reinitialize(Width, Rows);
                Surf.Release();
                Surf.width = Width;
                Surf.height = Rows;
                _gpuRows = Rows;
            }
            if (!Surf.IsCreated()) Surf.Create();
            Pos.SetPixelData(PosData, 0);
            Pos.Apply(false, false);
            Nrm.SetPixelData(NrmData, 0);
            Nrm.Apply(false, false);
            if (withMask)
            {
                Mask.SetPixelData(MaskData, 0);
                Mask.Apply(false, false);
            }
        }

        public void UploadMask()
        {
            Mask.SetPixelData(MaskData, 0);
            Mask.Apply(false, false);
        }

        /// <summary>Frees the GPU memory of the inputs (C3); CPU arrays stay for the next job / cut.</summary>
        public void ReleaseGpu()
        {
            if (_gpuRows == 0) return;
            Pos.Reinitialize(1, 1); Pos.Apply(false, false);
            Nrm.Reinitialize(1, 1); Nrm.Apply(false, false);
            Mask.Reinitialize(1, 1); Mask.Apply(false, false);
            Surf.Release();
            _gpuRows = 0;
        }

        public void Dispose()
        {
            Surf.Release();
            UnityEngine.Object.DestroyImmediate(Surf);
            UnityEngine.Object.DestroyImmediate(Pos);
            UnityEngine.Object.DestroyImmediate(Nrm);
            UnityEngine.Object.DestroyImmediate(Mask);
        }
    }

    /// <summary>
    /// B1 bake: the sketch's SDF (DAT/CB/GM/PE) in one RGBA32F data texture (no uniform arrays); four full-screen
    /// passes of the same HLSL port (TreeSdf.hlsl): 0 Surf (projection once, only flagged vertices), 1 Snap, 2 Light,
    /// 3 Mat. Full bake now (<see cref="BakeFull"/>) or sliced over N frames in horizontal bands with per-row costs
    /// (<see cref="BeginSliced"/> / <see cref="Step"/>). Masked mode (cut, C1): only masked texels are recomputed,
    /// the others are copied from the previous outputs (ping-pong into the back outputs).
    /// </summary>
    public sealed class SdfBaker : IDisposable
    {
        public const int Width = BakeOutputs.Width;

        private static readonly int IdSdf = Shader.PropertyToID("_SkSdf");
        private static readonly int IdPos = Shader.PropertyToID("_SkBakePos");
        private static readonly int IdNrm = Shader.PropertyToID("_SkBakeNrm");
        private static readonly int IdSurf = Shader.PropertyToID("_SkBakeSurf");
        private static readonly int IdSnap = Shader.PropertyToID("_SkBakeSnap");
        private static readonly int IdMask = Shader.PropertyToID("_SkBakeMask");
        private static readonly int IdMasked = Shader.PropertyToID("_SkMasked");
        private static readonly int IdPrevSnap = Shader.PropertyToID("_SkPrevSnap");
        private static readonly int IdPrevLight = Shader.PropertyToID("_SkPrevLight");
        private static readonly int IdPrevMat = Shader.PropertyToID("_SkPrevMat");

        private struct Slice { public int Pass, Row0, RowN, Frame; }

        private readonly Material _mat;
        private readonly Texture2D _sdf;
        private readonly float[] _sdfData = new float[Width * 2 * 4];
        private readonly CommandBuffer _cb = new CommandBuffer { name = "PA102XS SdfBake" };
        private readonly List<Slice> _slices = new List<Slice>(128);
        private BakeInputs _in;
        private BakeOutputs _out, _prev;
        private int _jobFrame, _jobNext;

        public SdfBaker(Shader bake)
        {
            _mat = new Material(bake) { hideFlags = HideFlags.HideAndDontSave };
            _sdf = new Texture2D(Width, 2, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = "PA102XS_Sdf"
            };
        }

        public bool Busy => _in != null;
        public long SdfBytes => Width * 2 * 16;
        public int SliceCount => _slices.Count;

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

        private void SetInputs(BakeInputs i, BakeOutputs o, BakeOutputs prev)
        {
            _cb.SetGlobalTexture(IdPos, i.Pos);
            _cb.SetGlobalTexture(IdNrm, i.Nrm);
            _cb.SetGlobalTexture(IdSurf, i.Surf);
            _cb.SetGlobalTexture(IdMask, i.Mask);
            _cb.SetGlobalTexture(IdSnap, o.Snap);
            _cb.SetGlobalFloat(IdMasked, prev != null ? 1f : 0f);
            if (prev != null)
            {
                _cb.SetGlobalTexture(IdPrevSnap, prev.Snap);
                _cb.SetGlobalTexture(IdPrevLight, prev.Light);
                _cb.SetGlobalTexture(IdPrevMat, prev.Mat);
            }
        }

        private static RenderTargetIdentifier PassTarget(BakeInputs i, BakeOutputs o, int pass)
        {
            return pass == 0 ? i.Surf : pass == 1 ? o.Snap : pass == 2 ? o.Light : o.Mat;
        }

        /// <summary>All four passes now (inputs must be uploaded). Returns CPU ms incl. GPU sync when requested.</summary>
        public double BakeFull(BakeInputs i, BakeOutputs o, bool syncGpu)
        {
            var sw = Stopwatch.StartNew();
            o.Ensure(i.Rows);
            _cb.Clear();
            SetInputs(i, o, null);
            for (int p = 0; p < 4; p++)
            {
                _cb.SetRenderTarget(PassTarget(i, o, p));
                _cb.DrawProcedural(Matrix4x4.identity, _mat, p, MeshTopology.Triangles, 3);
            }
            Graphics.ExecuteCommandBuffer(_cb);
            if (syncGpu) AsyncGPUReadback.Request(o.Mat, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Starts a bake of <paramref name="i"/> into <paramref name="o"/> spread over <paramref name="frames"/> Step() calls,
        /// with bands balanced by <see cref="BakeInputs.RowCost"/>. <paramref name="prev"/> != null → masked re-bake.
        /// </summary>
        public void BeginSliced(BakeInputs i, BakeOutputs o, BakeOutputs prev, int frames)
        {
            frames = Math.Max(1, frames);
            o.Ensure(i.Rows);
            _slices.Clear();
            float total = 0f;
            for (int p = 0; p < 4; p++)
                for (int r = 0; r < i.Rows; r++) total += i.RowCost[p][r];
            // Ramp-up: the first three frames get 1/4, 1/2, 3/4 of a regular frame's share, so the GPU governor (DVFS)
            // can raise clocks before the full per-frame bake load arrives.
            float wsum = 0f;
            for (int k = 0; k < frames; k++) wsum += Ramp(k);
            float acc = 0f, edge = total * Ramp(0) / wsum;
            int f = 0;
            for (int p = 0; p < 4; p++)
            {
                int r0 = 0;
                for (int r = 0; r < i.Rows; r++)
                {
                    acc += i.RowCost[p][r];
                    bool boundary = acc >= edge - 1e-3f && f < frames - 1;
                    if (boundary || r == i.Rows - 1)
                    {
                        _slices.Add(new Slice { Pass = p, Row0 = r0, RowN = r - r0 + 1, Frame = f });
                        r0 = r + 1;
                        if (boundary)
                        {
                            f++;
                            edge += total * Ramp(f) / wsum;
                        }
                    }
                }
            }
            _in = i;
            _out = o;
            _prev = prev;
            _jobFrame = 0;
            _jobNext = 0;
        }

        private static float Ramp(int frame) => frame < 3 ? (frame + 1) * 0.25f : 1f;

        /// <summary>Issues this frame's bands. Returns true when the job has completed (all bands issued).</summary>
        public bool Step()
        {
            if (_in == null) return true;
            _cb.Clear();
            SetInputs(_in, _out, _prev);
            int issued = 0;
            while (_jobNext < _slices.Count && _slices[_jobNext].Frame <= _jobFrame)
            {
                Slice s = _slices[_jobNext++];
                _cb.SetRenderTarget(PassTarget(_in, _out, s.Pass));
                _cb.EnableScissorRect(new Rect(0, s.Row0, Width, s.RowN));
                _cb.DrawProcedural(Matrix4x4.identity, _mat, s.Pass, MeshTopology.Triangles, 3);
                _cb.DisableScissorRect();
                issued++;
            }
            if (issued > 0) Graphics.ExecuteCommandBuffer(_cb);
            _jobFrame++;
            if (_jobNext >= _slices.Count)
            {
                _in = null;
                _out = null;
                _prev = null;
                return true;
            }
            return false;
        }

        /// <summary>GPU wall time of one pass over the whole target (synced before and after) — device profiling.</summary>
        public double TimePass(BakeInputs i, BakeOutputs o, int pass)
        {
            AsyncGPUReadback.Request(o.Mat, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            var sw = Stopwatch.StartNew();
            _cb.Clear();
            SetInputs(i, o, null);
            _cb.SetRenderTarget(PassTarget(i, o, pass));
            _cb.DrawProcedural(Matrix4x4.identity, _mat, pass, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_cb);
            AsyncGPUReadback.Request(o.Mat, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            return sw.Elapsed.TotalMilliseconds;
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
