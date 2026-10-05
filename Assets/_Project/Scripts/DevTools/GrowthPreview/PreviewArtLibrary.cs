using System;
using System.Collections.Generic;
using System.IO;
using PocketArbor.Domain.Tree;
using UnityEditor;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>Pad sprite size class of the atlas JSON ("S" | "M" | "L").</summary>
    public enum PadSizeClass
    {
        S = 0,
        M = 1,
        L = 2,
    }

    /// <summary>One pad sprite of an atlas: normalized UV rect (bottom-left origin), pivot (fraction from the bottom-left), h/w aspect.</summary>
    public readonly struct PadSprite
    {
        public readonly Rect Uv;
        public readonly Vector2 Pivot;
        public readonly float Aspect;

        public PadSprite(Rect uv, Vector2 pivot, float aspect)
        {
            Uv = uv;
            Pivot = pivot;
            Aspect = aspect;
        }
    }

    /// <summary>Pad atlas of one species: texture + sprites grouped by size class (a missing class falls back to the nearest one).</summary>
    public sealed class PadAtlas
    {
        readonly List<PadSprite>[] _bySize = { new List<PadSprite>(), new List<PadSprite>(), new List<PadSprite>() };

        public PadAtlas(Texture2D texture, bool isFallback)
        {
            Texture = texture;
            IsFallback = isFallback;
        }

        public Texture2D Texture { get; }
        public bool IsFallback { get; }
        public int Count => _bySize[0].Count + _bySize[1].Count + _bySize[2].Count;

        readonly List<PadSprite> _dome = new List<PadSprite>();

        public void Add(PadSizeClass size, PadSprite sprite) => _bySize[(int)size].Add(sprite);

        /// <summary>PA-102P (TA): the dome-shaped sprite (m0 / k0 / y0 — a crown with branches under it) is reserved for the apex
        /// dome; side pads never get it (it would read as a second dome).</summary>
        public void AddDome(PadSprite sprite) => _dome.Add(sprite);

        public bool HasDome => _dome.Count > 0;

        /// <summary>Dome sprite picked by <paramref name="hash01"/>; falls back to <see cref="Pick"/> with L.</summary>
        public PadSprite PickDome(float hash01) =>
            _dome.Count > 0 ? _dome[Mathf.Min(_dome.Count - 1, (int)(hash01 * _dome.Count))] : Pick(PadSizeClass.L, hash01);

        /// <summary>Sprite of the requested class (or the nearest non-empty one) picked by <paramref name="hash01"/> ∈ [0, 1).</summary>
        public PadSprite Pick(PadSizeClass size, float hash01)
        {
            int wanted = (int)size;
            for (int distance = 0; distance < 3; distance++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int c = wanted + sign * distance;
                    if (c < 0 || c > 2 || _bySize[c].Count == 0) continue;
                    List<PadSprite> list = _bySize[c];
                    return list[Mathf.Min(list.Count - 1, (int)(hash01 * list.Count))];
                }
            }
            return new PadSprite(new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.3f), 0.6f);
        }

        // JSON contract of Technical Artist: {"pads":[{"name","x","y","w","h","pivotX","pivotY","size":"S|M|L"}]}, pixels, x/y = top-left.
        [Serializable]
        sealed class AtlasJson
        {
            public PadJson[] pads;
        }

        [Serializable]
        sealed class PadJson
        {
            public string name;
            public float x;
            public float y;
            public float w;
            public float h;
            public float pivotX = 0.5f;
            public float pivotY = 0.3f;
            public string size;
            public string role;
        }

        /// <summary>Parses the TA atlas JSON; <paramref name="width"/>/<paramref name="height"/> = source pixel size of the atlas PNG.</summary>
        public static PadAtlas Parse(Texture2D texture, string json, int width, int height)
        {
            var atlas = new PadAtlas(texture, false);
            AtlasJson data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<AtlasJson>(json);
            if (data?.pads == null || width <= 0 || height <= 0) return atlas;
            for (int i = 0; i < data.pads.Length; i++)
            {
                PadJson p = data.pads[i];
                if (p == null || p.w <= 0f || p.h <= 0f) continue;
                var uv = new Rect(p.x / width, 1f - (p.y + p.h) / height, p.w / width, p.h / height);
                var sprite = new PadSprite(uv, new Vector2(Mathf.Clamp01(p.pivotX), Mathf.Clamp01(p.pivotY)), p.h / p.w);
                // TA naming: the first L sprite "<letter>0" is the dome; an explicit "role":"dome" also works.
                bool dome = string.Equals(p.role, "dome", StringComparison.OrdinalIgnoreCase)
                    || (string.IsNullOrEmpty(p.role) && ParseSize(p.size) == PadSizeClass.L && p.name != null && p.name.EndsWith("0", StringComparison.Ordinal));
                if (dome) atlas.AddDome(sprite);
                else atlas.Add(ParseSize(p.size), sprite);
            }
            return atlas;
        }

        static PadSizeClass ParseSize(string size)
        {
            if (string.Equals(size, "S", StringComparison.OrdinalIgnoreCase)) return PadSizeClass.S;
            if (string.Equals(size, "L", StringComparison.OrdinalIgnoreCase)) return PadSizeClass.L;
            return PadSizeClass.M;
        }
    }

    /// <summary>
    /// Pot sprite layout — Technical Artist's pot JSON (pixels; <b>y from the bottom edge</b>, x from the left): the opening is an
    /// alpha = 0 hole between the front inner edge (<see cref="LipY"/>, <see cref="InnerLeft"/>…<see cref="InnerRight"/>) and the
    /// back inner edge (<see cref="InnerTopY"/>, <see cref="InnerTopLeft"/>…<see cref="InnerTopRight"/>); the soil strip goes
    /// behind the pot. A pot without the back edge (InnerTopY ≤ LipY) has no opening.
    /// </summary>
    public sealed class PotArt
    {
        public Texture2D Texture;
        public bool IsFallback;
        public int Width;
        public int Height;

        public float LipY;
        public float InnerLeft;
        public float InnerRight;
        public float InnerTopY;
        public float InnerTopLeft;
        public float InnerTopRight;

        /// <summary>Opaque x-extent of the sprite (px) — used to align the reference overlay by the pot's outer rim.</summary>
        public float OuterLeft;
        public float OuterRight;

        public bool HasOpening => InnerTopY > LipY;

        /// <summary>Height (px from the bottom) where the trunk base sits: <paramref name="t"/> of the way from the front to the back edge.</summary>
        public float BaseY(float t) => HasOpening ? Mathf.Lerp(LipY, InnerTopY, t) : LipY;

        /// <summary>Opening width (px) at <see cref="BaseY"/>.</summary>
        public float OpeningWidth(float t) => HasOpening
            ? Mathf.Lerp(InnerRight - InnerLeft, InnerTopRight - InnerTopLeft, t)
            : InnerRight - InnerLeft;

        [Serializable]
        sealed class PotJson
        {
            public float lipY;
            public float innerLeft;
            public float innerRight;
            public float innerTopY;
            public float innerTopLeft;
            public float innerTopRight;
        }

        public static PotArt Parse(Texture2D texture, string json, int width, int height)
        {
            PotJson data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PotJson>(json);
            var art = new PotArt { Texture = texture, Width = width, Height = height };
            bool inner = data != null && data.innerRight > data.innerLeft;
            art.LipY = data != null && data.lipY > 0f ? data.lipY : height * 0.88f;
            art.InnerLeft = inner ? data.innerLeft : width * 0.06f;
            art.InnerRight = inner ? data.innerRight : width * 0.94f;
            art.InnerTopY = data != null && data.innerTopY > art.LipY ? data.innerTopY : art.LipY;
            bool top = data != null && data.innerTopRight > data.innerTopLeft;
            art.InnerTopLeft = top ? data.innerTopLeft : art.InnerLeft;
            art.InnerTopRight = top ? data.innerTopRight : art.InnerRight;
            art.OuterLeft = 0f;
            art.OuterRight = width;
            return art;
        }
    }

    /// <summary>Soil strip (RGBA, transparent top). Ground line = <see cref="GroundFromTop"/> px from the top (soil.json or alpha scan).</summary>
    public sealed class SoilArt
    {
        public Texture2D Texture;
        public bool IsFallback;
        public int Width;
        public int Height;
        public float GroundFromTop;

        [Serializable]
        sealed class SoilJson
        {
            public float groundFromTop = -1f;
        }

        /// <summary>groundFromTop from the shared soil.json, or a negative value when absent.</summary>
        public static float ParseGround(string json)
        {
            if (string.IsNullOrEmpty(json)) return -1f;
            SoilJson data = JsonUtility.FromJson<SoilJson>(json);
            return data != null ? data.groundFromTop : -1f;
        }
    }

    /// <summary>
    /// PA-103S: Technical Artist's preview art from <see cref="DefaultRoot"/> (AssetDatabase, Editor-only) with procedural
    /// fallbacks for every missing file, so the Textured style never fails. Generated textures are owned and destroyed here.
    /// </summary>
    public sealed class PreviewArtLibrary : IDisposable
    {
        public const string DefaultRoot = "Assets/_Project/Art/Runtime/Preview";
        public const string ReferencePath = "Assets/_Project/Art/Source/Section_0/realism_l2_painterly.jpg";

        /// <summary>PA-102T / TD-073: default pot skin (pot_tokoname — the one with an opening, matched to the reference).</summary>
        public const string DefaultPotSkin = "tokoname";

        /// <summary>Pot skin: pot_&lt;skin&gt;.png / .json (fallback pot_tokoname, then the procedural pot).</summary>
        public string PotSkin { get; set; } = DefaultPotSkin;

        static readonly string[] SpeciesKeys = { "kuromatsu", "momiji", "yamazakura" };

        readonly List<UnityEngine.Object> _generated = new List<UnityEngine.Object>();
        readonly PadAtlas[] _atlases = new PadAtlas[3];

        public PreviewArtLibrary(string root = DefaultRoot)
        {
            Root = root;
        }

        public string Root { get; }
        public Texture2D BarkTrunk { get; private set; }
        public Texture2D BarkYoung { get; private set; }
        public bool BarkIsFallback { get; private set; }
        public Texture2D Washi { get; private set; }
        public bool WashiIsFallback { get; private set; }
        public PotArt Pot { get; private set; }
        public SoilArt SoilMoist { get; private set; }
        public SoilArt SoilDry { get; private set; }
        public Texture2D Reference { get; private set; }
        public bool Loaded { get; private set; }

        public PadAtlas Atlas(SpeciesId species) => _atlases[Mathf.Clamp((int)species, 0, 2)];

        readonly Texture2D[] _speciesTrunk = new Texture2D[3];
        readonly Texture2D[] _speciesYoung = new Texture2D[3];

        /// <summary>⚑T BALANCE §1.6.6: bark textures of <paramref name="species"/> from <see cref="Root"/> (bark_&lt;species&gt;_trunk/young.png, TA runtime copies);
        /// null when missing — the caller falls back to <see cref="BarkTrunk"/> / <see cref="BarkYoung"/> with the placeholder tone.</summary>
        public Texture2D SpeciesBarkTrunk(SpeciesId species) => _speciesTrunk[Mathf.Clamp((int)species, 0, 2)];
        public Texture2D SpeciesBarkYoung(SpeciesId species) => _speciesYoung[Mathf.Clamp((int)species, 0, 2)];

        /// <summary>Number of TA art sets found (of 9: 2 bark, washi, 3 pad atlases with JSON, pot, 2 soils).</summary>
        public int FoundCount { get; private set; }

        /// <summary>Short status for the panel: which parts are real art and which are fallbacks.</summary>
        public string Summary { get; private set; } = "not loaded";

        public void Load()
        {
            Dispose();
            int found = 0;
            var missing = new System.Text.StringBuilder();

            BarkTrunk = LoadTexture("bark_trunk.png");
            BarkYoung = LoadTexture("bark_young.png");
            found += (BarkTrunk != null ? 1 : 0) + (BarkYoung != null ? 1 : 0);
            BarkIsFallback = BarkTrunk == null || BarkYoung == null;
            if (BarkTrunk == null) { BarkTrunk = Own(PreviewArtFallbacks.Bark(new Color32(0x5A, 0x42, 0x33, 0xFF), 11u)); missing.Append(" bark_trunk"); }
            if (BarkYoung == null) { BarkYoung = Own(PreviewArtFallbacks.Bark(new Color32(0x6A, 0x4B, 0x36, 0xFF), 23u)); missing.Append(" bark_young"); }

            for (int sp = 0; sp < 3; sp++)
            {
                SpeciesRenderProfile profile = SpeciesRenderProfile.For((SpeciesId)sp);
                _speciesTrunk[sp] = LoadTexture(profile.BarkTrunkTexture);
                _speciesYoung[sp] = LoadTexture(profile.BarkYoungTexture);
                if (_speciesTrunk[sp] == null || _speciesYoung[sp] == null) missing.Append(" bark_").Append(SpeciesKeys[sp]).Append("(tone)");
            }

            Washi = LoadTexture("washi_bg.png");
            WashiIsFallback = Washi == null;
            if (Washi != null) found++;
            else { Washi = Own(PreviewArtFallbacks.Washi()); missing.Append(" washi_bg"); }

            for (int s = 0; s < 3; s++)
            {
                string file = "pads_" + SpeciesKeys[s];
                Texture2D tex = LoadTexture(file + ".png");
                string json = LoadText(file + ".json");
                PadAtlas atlas = null;
                if (tex != null && json != null)
                {
                    SourceSize(tex, out int w, out int h);
                    atlas = PadAtlas.Parse(tex, json, w, h);
                    if (atlas.Count == 0) atlas = null;
                }
                if (atlas != null) found++;
                else
                {
                    missing.Append(' ').Append(file);
                    Color32 leaf = TreePreviewMeshBuilder.LeafColor((SpeciesId)s);
                    atlas = PreviewArtFallbacks.PadAtlas(leaf, 101u + (uint)s, out Texture2D generated);
                    Own(generated);
                }
                _atlases[s] = atlas;
            }

            string potFile = "pot_" + (string.IsNullOrEmpty(PotSkin) ? DefaultPotSkin : PotSkin);
            Texture2D potTex = LoadTexture(potFile + ".png");
            string potJson = potTex != null ? LoadText(potFile + ".json") : null;
            if (potTex == null)
            {
                potTex = LoadTexture("pot_tokoname.png");
                potJson = LoadText("pot_tokoname.json");
            }
            if (potTex != null)
            {
                found++;
                SourceSize(potTex, out int w, out int h);
                Pot = PotArt.Parse(potTex, potJson, w, h);
                MeasureOpaqueColumns(Pot);
                if (potJson == null) missing.Append(" pot_tokoname.json");
            }
            else
            {
                missing.Append(" pot_tokoname");
                Pot = PreviewArtFallbacks.Pot(out Texture2D generated);
                Own(generated);
            }

            float ground = SoilArt.ParseGround(LoadText("soil.json"));
            SoilMoist = LoadSoil("soil_moist.png", new Color32(0x4F, 0x5E, 0x2E, 0xFF), ground, missing, ref found);
            SoilDry = LoadSoil("soil_dry.png", new Color32(0x7A, 0x63, 0x48, 0xFF), ground, missing, ref found);

            Reference = LoadReference();

            FoundCount = found;
            Summary = found + "/9 art sets" + (missing.Length > 0 ? "; fallback:" + missing : "") + (Reference == null ? "; no reference jpg" : "");
            Loaded = true;
        }

        SoilArt LoadSoil(string file, Color32 color, float groundFromTop, System.Text.StringBuilder missing, ref int found)
        {
            Texture2D tex = LoadTexture(file);
            if (tex == null)
            {
                missing.Append(' ').Append(Path.GetFileNameWithoutExtension(file));
                SoilArt fallback = PreviewArtFallbacks.Soil(color, out Texture2D generated);
                Own(generated);
                return fallback;
            }
            found++;
            SourceSize(tex, out int w, out int h);
            var soil = new SoilArt { Texture = tex, Width = w, Height = h };
            soil.GroundFromTop = groundFromTop >= 0f ? groundFromTop : DetectSurface(tex) * h;
            return soil;
        }

        /// <summary>First row from the top whose mean alpha ≥ 0.5 (texture must be readable — set by the postprocessor).</summary>
        static float DetectSurface(Texture2D tex)
        {
            if (!tex.isReadable) return 0.4f;
            Color32[] pixels = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            for (int row = h - 1; row >= 0; row--)
            {
                int sum = 0;
                for (int x = 0; x < w; x++) sum += pixels[row * w + x].a;
                if (sum >= w * 128) return (h - 1 - row) / (float)h;
            }
            return 0.4f;
        }

        static void MeasureOpaqueColumns(PotArt pot)
        {
            Texture2D tex = pot.Texture;
            if (!tex.isReadable) return;
            Color32[] pixels = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            int left = -1, right = -1;
            for (int x = 0; x < w && left < 0; x++)
                for (int y = 0; y < h; y++)
                    if (pixels[y * w + x].a > 128) { left = x; break; }
            for (int x = w - 1; x >= 0 && right < 0; x--)
                for (int y = 0; y < h; y++)
                    if (pixels[y * w + x].a > 128) { right = x + 1; break; }
            if (left < 0 || right <= left) return;
            float scale = pot.Width / (float)w; // imported size may differ from the source size
            pot.OuterLeft = left * scale;
            pot.OuterRight = right * scale;
        }

        Texture2D LoadTexture(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + file);

        string LoadText(string file)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/" + file);
            if (asset != null) return asset.text;
            string path = Path.Combine(Root, file);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        static void SourceSize(Texture2D tex, out int width, out int height)
        {
            width = tex.width;
            height = tex.height;
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter importer)
            {
                importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                if (w > 0 && h > 0)
                {
                    width = w;
                    height = h;
                }
            }
        }

        /// <summary>The reference JPG is read from disk (independent of its import settings; Art/Source is read-only for us).</summary>
        Texture2D LoadReference()
        {
            if (!File.Exists(ReferencePath)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "ReferenceOverlay", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            if (!tex.LoadImage(File.ReadAllBytes(ReferencePath)))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }
            return Own(tex);
        }

        T Own<T>(T obj) where T : UnityEngine.Object
        {
            obj.hideFlags = HideFlags.HideAndDontSave;
            _generated.Add(obj);
            return obj;
        }

        public void Dispose()
        {
            for (int i = 0; i < _generated.Count; i++)
            {
                if (_generated[i] != null) UnityEngine.Object.DestroyImmediate(_generated[i]);
            }
            _generated.Clear();
            Loaded = false;
        }
    }
}
