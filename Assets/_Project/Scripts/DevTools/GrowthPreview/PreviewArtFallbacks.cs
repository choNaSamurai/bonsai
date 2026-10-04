using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-103S: procedural stand-ins for missing Technical Artist files (same layouts and code path as the real art,
    /// ADR-0004 §2 "До появи арту D5"). Deterministic (fixed hash), created once per <see cref="PreviewArtLibrary.Load"/>.
    /// </summary>
    public static class PreviewArtFallbacks
    {
        const int PadCell = 128;

        /// <summary>Seamless 64² bark: vertical painted streaks around <paramref name="baseColor"/>.</summary>
        public static Texture2D Bark(Color32 baseColor, uint salt)
        {
            const int size = 64;
            var tex = NewTexture("FallbackBark", size, size, TextureWrapMode.Repeat);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Streak value: smooth periodic noise in x, slow wobble along y.
                    float wobble = 2.5f * Mathf.Sin((y / (float)size) * Mathf.PI * 2f + Hash01(salt, x / 8, 7u) * 6.28f);
                    float sx = x + wobble;
                    float streak = Periodic(sx, size, 8, salt) * 0.6f + Periodic(sx, size, 3, salt + 5u) * 0.4f;
                    float grain = Hash01(salt, x + y * size, 13u) * 0.08f;
                    float v = 0.78f + 0.36f * streak + grain;
                    pixels[y * size + x] = Scale(baseColor, v);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Faint washi paper: warm off-white with low-contrast fibres (seamless 128²).</summary>
        public static Texture2D Washi()
        {
            const int size = 128;
            var tex = NewTexture("FallbackWashi", size, size, TextureWrapMode.Repeat);
            var pixels = new Color32[size * size];
            var paper = new Color32(0xF3, 0xEE, 0xE2, 0xFF);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fibre = Periodic(x + 0.5f * y, size, 16, 3u) * 0.5f + Periodic(y - 0.3f * x, size, 32, 9u) * 0.5f;
                    float v = 0.985f + 0.03f * (fibre - 0.5f) + 0.012f * (Hash01(5u, x + y * size, 1u) - 0.5f);
                    pixels[y * size + x] = Scale(paper, v);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>3×3 atlas (rows S/M/L) of lumpy cloud pads: irregular top, flatter bottom, darker underside, soft dark rim.</summary>
        public static PadAtlas PadAtlas(Color32 leaf, uint salt, out Texture2D texture)
        {
            int size = PadCell * 3;
            texture = NewTexture("FallbackPads", size, size, TextureWrapMode.Clamp);
            var pixels = new Color32[size * size];
            var atlas = new PadAtlas(texture, true);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    uint cellSalt = salt * 31u + (uint)(row * 3 + col);
                    int lobes = 3 + row * 2 + col % 2;
                    PaintPad(pixels, size, col * PadCell, row * PadCell, leaf, cellSalt, lobes);
                    var uv = new Rect(col / 3f, row / 3f, 1f / 3f, 1f / 3f);
                    atlas.Add((PadSizeClass)row, new PadSprite(uv, new Vector2(0.5f, 0.3f), 1f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return atlas;
        }

        static void PaintPad(Color32[] pixels, int stride, int ox, int oy, Color32 leaf, uint salt, int lobes)
        {
            // Lobes: circles along a dome; bottom clipped flat at 26% of the cell.
            var cx = new float[lobes];
            var cy = new float[lobes];
            var cr = new float[lobes];
            for (int k = 0; k < lobes; k++)
            {
                float t = lobes == 1 ? 0.5f : k / (float)(lobes - 1);
                cx[k] = Mathf.Lerp(0.2f, 0.8f, t) + 0.05f * (Hash01(salt, k, 1u) - 0.5f);
                cr[k] = Mathf.Lerp(0.16f, 0.22f, Hash01(salt, k, 2u)) * (1f - 0.35f * Mathf.Abs(t - 0.5f));
                cy[k] = 0.36f + 0.16f * (1f - 4f * (t - 0.5f) * (t - 0.5f)) + 0.04f * Hash01(salt, k, 3u);
            }
            const float bottom = 0.26f;
            Color32 dark = Scale(leaf, 0.62f);
            Color32 light = Scale(leaf, 1.22f);
            for (int py = 0; py < PadCell; py++)
            {
                for (int px = 0; px < PadCell; px++)
                {
                    float x = (px + 0.5f) / PadCell, y = (py + 0.5f) / PadCell;
                    float best = -1f; // signed distance inside the union (positive inside)
                    for (int k = 0; k < lobes; k++)
                    {
                        float dx = x - cx[k], dy = (y - cy[k]) * 1.25f;
                        best = Mathf.Max(best, cr[k] - Mathf.Sqrt(dx * dx + dy * dy));
                    }
                    best = Mathf.Min(best, (y - bottom) * 0.8f);
                    // Leafy rim: jitter the edge.
                    best += 0.012f * (Hash01(salt, px * 7 + py * 131, 9u) - 0.5f) + 0.01f * Mathf.Sin(x * 70f + y * 40f);
                    float alpha = Mathf.Clamp01(best * PadCell * 0.7f + 0.5f);
                    if (alpha <= 0f) continue;
                    float h = Mathf.InverseLerp(bottom, 0.62f, y);
                    Color32 c = h < 0.4f ? Color32.Lerp(dark, leaf, h / 0.4f) : Color32.Lerp(leaf, light, (h - 0.4f) / 0.6f);
                    float speck = Hash01(salt, px + py * 977, 17u);
                    c = Scale(c, 0.9f + 0.2f * speck);
                    float rim = Mathf.Clamp01(1f - best * PadCell / 2.5f);
                    c = Color32.Lerp(c, Scale(leaf, 0.45f), rim * 0.7f);
                    c.a = (byte)(alpha * 255f);
                    pixels[(oy + py) * stride + ox + px] = c;
                }
            }
        }

        /// <summary>Terracotta pot 256×96: rim band on top, slightly tapered body, small feet; inner opening = rim interior.</summary>
        public static PotArt Pot(out Texture2D texture)
        {
            const int w = 256, h = 96;
            texture = NewTexture("FallbackPot", w, h, TextureWrapMode.Clamp);
            var pixels = new Color32[w * h];
            var body = new Color32(0xB0, 0x6E, 0x4C, 0xFF);
            var rim = new Color32(0x9A, 0x5C, 0x3F, 0xFF);
            for (int py = 0; py < h; py++)
            {
                int fromTop = h - 1 - py;
                for (int px = 0; px < w; px++)
                {
                    bool inside;
                    Color32 c;
                    if (fromTop < 12) { inside = px >= 2 && px < w - 2; c = rim; }
                    else if (fromTop < 86)
                    {
                        float inset = 8f + (fromTop - 12) * 0.08f;
                        inside = px >= inset && px < w - inset;
                        c = Scale(body, 1.04f - 0.12f * (fromTop - 12) / 74f);
                    }
                    else { inside = (px >= 22 && px < 50) || (px >= w - 50 && px < w - 22); c = Scale(body, 0.8f); }
                    if (!inside) continue;
                    c = Scale(c, 0.96f + 0.08f * Hash01(41u, px + py * w, 3u));
                    pixels[py * w + px] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return new PotArt { Texture = texture, IsFallback = true, Width = w, Height = h, LipY = h - 4f, InnerTopY = h - 4f, InnerLeft = 10f, InnerRight = w - 10f, InnerTopLeft = 10f, InnerTopRight = w - 10f, OuterLeft = 2f, OuterRight = w - 2f };
        }

        /// <summary>Soil strip 256×32: transparent top, bumpy moss/soil edge at ~40% from the top.</summary>
        public static SoilArt Soil(Color32 color, out Texture2D texture)
        {
            const int w = 256, h = 32;
            texture = NewTexture("FallbackSoil", w, h, TextureWrapMode.Clamp);
            var pixels = new Color32[w * h];
            for (int px = 0; px < w; px++)
            {
                float edge = 0.4f * h + 2.2f * Mathf.Sin(px * 0.21f) + 1.6f * Mathf.Sin(px * 0.057f + 1f) + 1.2f * (Hash01(77u, px, 1u) - 0.5f);
                for (int py = 0; py < h; py++)
                {
                    int fromTop = h - 1 - py;
                    float a = Mathf.Clamp01(fromTop - edge + 0.5f);
                    if (a <= 0f) continue;
                    Color32 c = Scale(color, 0.85f + 0.3f * Hash01(78u, px + py * w, 2u));
                    if (Hash01(79u, px + py * w, 3u) > 0.97f) c = new Color32(0xA8, 0xA0, 0x90, 0xFF); // pebble
                    c.a = (byte)(a * 255f);
                    pixels[py * w + px] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return new SoilArt { Texture = texture, IsFallback = true, Width = w, Height = h, GroundFromTop = 0.42f * h };
        }

        static Texture2D NewTexture(string name, int w, int h, TextureWrapMode wrap)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        /// <summary>Smooth value noise periodic in [0, period) with <paramref name="cells"/> lattice cells.</summary>
        static float Periodic(float x, int period, int cells, uint salt)
        {
            float f = (x / period) * cells;
            int i0 = Mathf.FloorToInt(f);
            float t = f - i0;
            t = t * t * (3f - 2f * t);
            int a = ((i0 % cells) + cells) % cells;
            int b = (a + 1) % cells;
            return Mathf.Lerp(Hash01(salt, a, 0u), Hash01(salt, b, 0u), t);
        }

        static Color32 Scale(Color32 c, float f) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * f), 0, 255), c.a);

        static float Hash01(uint seed, int id, uint salt)
        {
            unchecked
            {
                uint h = seed ^ (0x9E3779B9u * (uint)(id + 1)) ^ (salt * 0x85EBCA6Bu);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }
    }
}
