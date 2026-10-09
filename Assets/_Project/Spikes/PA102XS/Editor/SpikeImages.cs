using System;
using System.IO;
using UnityEngine;

namespace PocketArbor.Spike.Editor
{
    /// <summary>Pixel helpers for the harness: SSAA downsample, PNG IO, alpha IoU, diff, sheets. Rows are bottom-up (Unity).</summary>
    public static class SpikeImages
    {
        public static Color32[] LoadPng(string path, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new IOException("bad png " + path);
            w = tex.width;
            h = tex.height;
            Color32[] px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        public static void SavePng(string path, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>Box filter d x d with alpha-weighted colour (output is straight alpha).</summary>
        public static Color32[] Downsample(Color32[] src, int sw, int sh, int d)
        {
            int w = sw / d, h = sh / d;
            var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < d; j++)
                for (int i = 0; i < d; i++)
                {
                    Color32 c = src[(y * d + j) * sw + x * d + i];
                    r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                }
                dst[y * w + x] = a == 0
                    ? new Color32(0, 0, 0, 0)
                    : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)((a + d * d / 2) / (d * d)));
            }
            return dst;
        }

        /// <summary>IoU of alpha &gt;= 128 masks.</summary>
        public static float AlphaIoU(Color32[] a, Color32[] b, out int inter, out int union, out int onlyA, out int onlyB)
        {
            inter = union = onlyA = onlyB = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                bool ia = a[i].a >= 128, ib = b[i].a >= 128;
                if (ia && ib) inter++;
                if (ia || ib) union++;
                if (ia && !ib) onlyA++;
                if (ib && !ia) onlyB++;
            }
            return union == 0 ? 1f : (float)inter / union;
        }

        /// <summary>Mask diff: grey = both, red = reference only, blue = Unity only.</summary>
        public static Color32[] MaskDiff(Color32[] refPx, Color32[] uni)
        {
            var o = new Color32[refPx.Length];
            for (int i = 0; i < o.Length; i++)
            {
                bool a = refPx[i].a >= 128, b = uni[i].a >= 128;
                o[i] = a && b ? new Color32(150, 150, 150, 255) : a ? new Color32(230, 40, 40, 255) : b ? new Color32(40, 80, 230, 255) : new Color32(255, 255, 255, 255);
            }
            return o;
        }

        public static int CountDiff(Color32[] a, Color32[] b, int tol)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (Math.Abs(a[i].r - b[i].r) > tol || Math.Abs(a[i].g - b[i].g) > tol || Math.Abs(a[i].b - b[i].b) > tol || Math.Abs(a[i].a - b[i].a) > tol) n++;
            }
            return n;
        }

        /// <summary>A canvas filled with a background colour; Blit places images by their TOP-left corner.</summary>
        public sealed class Canvas
        {
            public readonly int W, H;
            public readonly Color32[] Px;

            public Canvas(int w, int h, Color32 bg)
            {
                W = w;
                H = h;
                Px = new Color32[w * h];
                for (int i = 0; i < Px.Length; i++) Px[i] = bg;
            }

            /// <summary>Downscale src by d (box, alpha-weighted) and alpha-over at (x, top). Optional source crop in src pixels (top-left based).</summary>
            public void Blit(Color32[] src, int sw, int sh, int x0, int top, int d, int cx = 0, int cyTop = 0, int cw = -1, int chh = -1, int up = 1)
            {
                if (cw < 0) cw = sw;
                if (chh < 0) chh = sh;
                int ow = cw / d, oh = chh / d;
                for (int oy = 0; oy < oh; oy++)
                for (int ox = 0; ox < ow; ox++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < d; j++)
                    for (int i = 0; i < d; i++)
                    {
                        int sxTop = cx + ox * d + i, syTop = cyTop + oy * d + j;
                        int sy = sh - 1 - syTop;
                        if (sxTop < 0 || sxTop >= sw || sy < 0 || sy >= sh) continue;
                        Color32 c = src[sy * sw + sxTop];
                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                    }
                    float al = a / (255f * d * d);
                    for (int uy = 0; uy < up; uy++)
                    for (int ux = 0; ux < up; ux++)
                    {
                        int dx = x0 + ox * up + ux, dyTop = top + oy * up + uy;
                        int dy = H - 1 - dyTop;
                        if (dx < 0 || dx >= W || dy < 0 || dy >= H) continue;
                        Color32 bg = Px[dy * W + dx];
                        if (a == 0) continue;
                        float cr = r / (float)a, cg = g / (float)a, cb = b / (float)a;
                        Px[dy * W + dx] = new Color32(
                            (byte)(cr * al + bg.r * (1f - al)),
                            (byte)(cg * al + bg.g * (1f - al)),
                            (byte)(cb * al + bg.b * (1f - al)), 255);
                    }
                }
            }

            public void Fill(int x0, int top, int w, int h, Color32 c)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int dx = x0 + x, dy = H - 1 - (top + y);
                    if (dx < 0 || dx >= W || dy < 0 || dy >= H) continue;
                    Px[dy * W + dx] = c;
                }
            }
        }
    }
}
