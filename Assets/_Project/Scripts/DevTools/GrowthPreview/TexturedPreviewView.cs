using System;
using System.Collections.Generic;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-103S: scene objects of the Textured style — child renderers created once (HideAndDontSave, nothing is saved into
    /// the scene), meshes/materials reused across rebuilds. Draw order (sortingOrder, ADR-0004 §3 SoilFront):
    /// washi background → soil strip (behind the pot, seen through its opening) → pot → bark (roots, trunk, branches) →
    /// soil-front moss band over the trunk base and roots (faded edges) → pads → reference overlay.
    /// </summary>
    public sealed class TexturedPreviewView : IDisposable
    {
        public const string BarkShaderName = "PocketArbor/DevTools/PreviewBark";
        public const string SpriteShaderName = "PocketArbor/DevTools/PreviewSprite";

        const int OrderBackground = -100;
        const int OrderSoil = -60;
        const int OrderPot = -50;
        const int OrderBark = 0;
        const int OrderSoilFront = 10;
        const int OrderPads = 30;
        const int OrderOverlay = 100;
        const float BackgroundHalfSize = 3000f;

        static readonly int BarkTrunkId = Shader.PropertyToID("_BarkTrunk");
        static readonly int BarkYoungId = Shader.PropertyToID("_BarkYoung");
        static readonly int TrunkTintId = Shader.PropertyToID("_TrunkTint");
        static readonly int YoungTintId = Shader.PropertyToID("_YoungTint");
        static readonly int BarkTileId = Shader.PropertyToID("_BarkTileWorld");
        static readonly int LightDirId = Shader.PropertyToID("_LightDir");
        static readonly int ShadeDarkId = Shader.PropertyToID("_ShadeDark");
        static readonly int ShadeLightId = Shader.PropertyToID("_ShadeLight");
        static readonly int InkColorId = Shader.PropertyToID("_InkColor");
        static readonly int InkPxId = Shader.PropertyToID("_InkPx");
        static readonly int InkShareId = Shader.PropertyToID("_InkMaxShare");
        static readonly int AAExpandId = Shader.PropertyToID("_AAExpandPx");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly GameObject _root;
        readonly Part _background, _pot, _bark, _soil, _soilFront, _pads, _overlay;
        readonly List<Vector3> _quadPos = new List<Vector3>(4);
        readonly List<Vector2> _quadUv = new List<Vector2>(4);
        readonly Color32[] _quadColors = { Color.white, Color.white, Color.white, Color.white };
        readonly List<Vector2> _quadUv1 = new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero }; // brightness offset 0 (missing streams are undefined on Metal)
        readonly int[] _quadIdx = { 0, 1, 2, 0, 2, 3 };
        readonly List<Vector3> _bandPos = new List<Vector3>(8);
        readonly List<Vector2> _bandUv = new List<Vector2>(8);
        readonly List<Vector2> _bandUv1 = new List<Vector2>(96);
        readonly List<Color32> _bandColors = new List<Color32>(96);
        readonly List<int> _bandIdxList = new List<int>(288);

        bool _visible = true;
        bool _overlayOn;
        float _overlayAlpha = 0.5f;
        Rect _overlayRect;

        sealed class Part
        {
            public GameObject Go;
            public Mesh Mesh;
            public Material Material;
            public MeshRenderer Renderer;
        }

        public TexturedPreviewView(Transform parent, TexturedStyleConfig style)
        {
            Style = style;
            Shader bark = Shader.Find(BarkShaderName);
            Shader sprite = Shader.Find(SpriteShaderName);
            ShadersFound = bark != null && sprite != null;
            if (bark == null) bark = Shader.Find("Sprites/Default");
            if (sprite == null) sprite = Shader.Find("Sprites/Default");

            _root = Own(new GameObject("TexturedPreview (generated)"));
            _root.transform.SetParent(parent, false);
            _background = CreatePart("Washi", sprite, OrderBackground);
            _soil = CreatePart("Soil", sprite, OrderSoil);
            _pot = CreatePart("Pot", sprite, OrderPot);
            _bark = CreatePart("Bark", bark, OrderBark);
            _soilFront = CreatePart("SoilFront", sprite, OrderSoilFront);
            _pads = CreatePart("Pads", sprite, OrderPads);
            _overlay = CreatePart("ReferenceOverlay", sprite, OrderOverlay);
            ApplyBarkStyle();
            SetVisible(false);
        }

        public TexturedStyleConfig Style { get; }
        public bool ShadersFound { get; }
        public Mesh BarkMesh => _bark.Mesh;
        public Mesh PadMesh => _pads.Mesh;
        public bool OverlayVisible => _overlayOn && _visible && _overlay.Renderer.enabled;

        /// <summary>World rect covered by the reference image (aligned by the pot) after the last rebuild.</summary>
        public Rect OverlayRect => _overlayRect;

        /// <summary>World rect of the pot sprite after the last rebuild (yMax = top of the back rim, the y = 1068 px of the reference).</summary>
        public Rect PotRect { get; private set; }

        /// <summary>Shows only the tree layers (bark, pads) — for the AC mask harness (transparent clear).</summary>
        public void SetTreeOnly(bool treeOnly, bool showBark = true, bool showPads = true)
        {
            _background.Go.SetActive(!treeOnly);
            _soil.Go.SetActive(!treeOnly);
            _pot.Go.SetActive(!treeOnly);
            _soilFront.Go.SetActive(!treeOnly);
            _bark.Go.SetActive(!treeOnly || showBark);
            _pads.Go.SetActive(!treeOnly || showPads);
            if (treeOnly) _overlay.Renderer.enabled = false;
            else SetOverlay(_overlayOn, _overlayAlpha);
        }

        /// <summary>Harness: only the SoilFront moss layer (root visibility, BALANCE §1.4.0).</summary>
        public void SetSoilFrontOnly(bool on)
        {
            _background.Go.SetActive(!on);
            _soil.Go.SetActive(!on);
            _pot.Go.SetActive(!on);
            _bark.Go.SetActive(!on);
            _pads.Go.SetActive(!on);
            _soilFront.Go.SetActive(true);
            if (on) _overlay.Renderer.enabled = false;
            else SetOverlay(_overlayOn, _overlayAlpha);
        }

        /// <summary>Pot outer rim in world units (x-extent) after the last rebuild.</summary>
        public float PotOuterLeft { get; private set; }
        public float PotOuterRight { get; private set; }

        Part CreatePart(string name, Shader shader, int order)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(_root.transform, false);
            var part = new Part
            {
                Go = go,
                Mesh = Own(new Mesh { name = "TexturedPreview." + name }),
                Material = Own(new Material(shader) { name = "TexturedPreview." + name }),
            };
            part.Mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            part.Renderer = go.AddComponent<MeshRenderer>();
            part.Renderer.sharedMaterial = part.Material;
            part.Renderer.sortingOrder = order;
            part.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            part.Renderer.receiveShadows = false;
            return part;
        }

        T Own<T>(T obj) where T : UnityEngine.Object
        {
            obj.hideFlags = HideFlags.HideAndDontSave;
            _owned.Add(obj);
            return obj;
        }

        void ApplyBarkStyle()
        {
            Material m = _bark.Material;
            m.SetColor(TrunkTintId, Style.TrunkTint);
            m.SetColor(YoungTintId, Style.YoungTint);
            m.SetFloat(BarkTileId, Style.BarkTileWorld);
            m.SetVector(LightDirId, Style.LightDir);
            m.SetFloat(ShadeDarkId, Style.ShadeDark);
            m.SetFloat(ShadeLightId, Style.ShadeLight);
            m.SetColor(InkColorId, Style.InkColor);
            m.SetFloat(InkPxId, Style.InkPx);
            m.SetFloat(InkShareId, Style.InkMaxShare);
            m.SetFloat(AAExpandId, Style.AAExpandPx);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            _root.SetActive(visible);
            _overlay.Renderer.enabled = visible && _overlayOn && _overlay.Material.GetTexture(MainTexId) != null;
        }

        public void SetOverlay(bool on, float alpha)
        {
            _overlayOn = on;
            _overlayAlpha = Mathf.Clamp01(alpha);
            _overlay.Material.SetColor(ColorId, new Color(1f, 1f, 1f, _overlayAlpha));
            _overlay.Renderer.enabled = _visible && on && _overlay.Material.GetTexture(MainTexId) != null;
        }

        /// <summary>Rebuilds tree meshes and re-lays pot/soil/background/overlay. Returns bounds of the tree + pot.</summary>
        public Bounds Rebuild(TreeGraph tree, GrowthPreviewModel model, TreePreviewMeshBuilder builder, PreviewArtLibrary art)
        {
            if (!art.Loaded) art.Load();
            SpeciesId species = tree.Genome.Species;
            _bark.Material.SetTexture(BarkTrunkId, art.BarkTrunk);
            _bark.Material.SetTexture(BarkYoungId, art.BarkYoung);
            PadAtlas atlas = art.Atlas(species);
            _pads.Material.SetTexture(MainTexId, atlas.Texture);

            Bounds bounds = builder.BuildTextured(tree, _bark.Mesh, _pads.Mesh, model.Config, model.SpeciesConfig, Style, atlas);

            // Pot: ⚑P BALANCE §1.3.0 — the outer front rim is P = TreeRenderConfig.PotWidthUnits (fixed 92 units, like the 566 px
            // of the reference); the trunk base (y = 0) sits TreeBaseInOpening of the way from the front to the back inner edge of the
            // opening; centred on the opening.
            PotArt pot = art.Pot;
            float upp = builder.LastPotWidth / Mathf.Max(1f, pot.OuterRight - pot.OuterLeft); // world units per pot pixel
            float baseY = pot.BaseY(Style.TreeBaseInOpening);
            float left = -0.5f * (pot.InnerLeft + pot.InnerRight) * upp;
            var potRect = new Rect(left, -baseY * upp, pot.Width * upp, pot.Height * upp);
            _pot.Material.SetTexture(MainTexId, pot.Texture);
            SetQuad(_pot.Mesh, potRect, new Rect(0f, 0f, 1f, 1f));
            PotOuterLeft = left + pot.OuterLeft * upp;
            PotOuterRight = left + pot.OuterRight * upp;

            // Soil strip behind the pot: ground line on the back inner edge (no opening → just above the Pot Lip).
            SoilArt soil = model.Moisture >= Style.MoistSoilThreshold ? art.SoilMoist : art.SoilDry;
            float soilUpp = Mathf.Max(upp, builder.LastPotWidth * Style.SoilWidthRatio / Mathf.Max(1f, soil.Width));
            float ground = pot.HasOpening ? (pot.InnerTopY - baseY) * upp : Style.SoilSurfaceY;
            float soilTop = ground + soil.GroundFromTop * soilUpp;
            _soil.Material.SetTexture(MainTexId, soil.Texture);
            SetQuad(_soil.Mesh, new Rect(-0.5f * soil.Width * soilUpp, soilTop - soil.Height * soilUpp, soil.Width * soilUpp, soil.Height * soilUpp),
                new Rect(0f, 0f, 1f, 1f));

            // Soil front (ADR-0004 §3 SoilFront): the top band of the same strip over the trunk base and roots, ground line
            // slightly below y = 0, as wide as the opening at the trunk base, edges faded into the soil behind.
            // ⚑Q BALANCE §1.4.2: the moss above the ground line is fitted into SoilFrontMossHeightUnits (≤ 2.5 units) — the texture's
            // moss rows are squashed, not cut; roots lie on the moss line (ADR-0004 rev.4) and dive under it only with their last 25%,
            // where a moss tuft (same texture) covers the entry point.
            TreeRenderConfig render = builder.Render;
            float bandWidth = pot.OpeningWidth(Style.TreeBaseInOpening) * upp * Style.SoilFrontWidthRatio;
            float bandGround = render.SoilFrontGroundY;
            float groundV = 1f - Mathf.Clamp01(soil.GroundFromTop / Mathf.Max(1f, soil.Height));
            float bandBottomV = 1f - Mathf.Clamp01((soil.GroundFromTop + Style.SoilFrontBandPx(soil.Height)) / Mathf.Max(1f, soil.Height));
            float mossHeight = render.SoilFrontMossHeightUnits; // = the moss line of the root geometry (TreeRenderConfig.MossLineY)
            float bandBottom = bandGround - (groundV - bandBottomV) * soil.Height * soilUpp;
            float uSpan = Mathf.Clamp01(bandWidth / (soil.Width * soilUpp));
            _soilFront.Material.SetTexture(MainTexId, soil.Texture);
            ClearBands();
            AddBand(-0.5f * bandWidth, 0.5f * bandWidth, bandBottom, bandGround, bandGround + mossHeight, 0.5f - 0.5f * uSpan, 0.5f + 0.5f * uSpan,
                bandBottomV, groundV, 1f, Style.SoilFrontEdgeFade, true);
            MossTopY = bandGround + mossHeight;
            IReadOnlyList<Vector3> tips = builder.LastRootTips;
            for (int k = 0; k < tips.Count; k++)
            {
                Vector3 tip = tips[k];
                float w = Mathf.Max(1.5f, render.NebariTipTuftWidthRatio * tip.z);
                float u0 = Mathf.Clamp01(0.5f + tip.x / (soil.Width * soilUpp)) - 0.5f * w / (soil.Width * soilUpp);
                float uw = w / (soil.Width * soilUpp);
                AddBand(tip.x - 0.5f * w, tip.x + 0.5f * w, tip.y - 0.8f * tip.z, tip.y - 0.1f, tip.y + render.NebariTipTuftHeight,
                    u0, u0 + uw, Mathf.Lerp(groundV, bandBottomV, 0.3f), groundV, 1f, 0.3f, false);
            }
            FlushBands(_soilFront.Mesh);

            // Washi background: one big quad, UV in world units / tile (texture wrap Repeat).
            float tile = Mathf.Max(1f, Style.WashiTileWorld);
            _background.Material.SetTexture(MainTexId, art.Washi);
            SetQuad(_background.Mesh, new Rect(-BackgroundHalfSize, -BackgroundHalfSize, 2f * BackgroundHalfSize, 2f * BackgroundHalfSize),
                new Rect(-BackgroundHalfSize / tile, -BackgroundHalfSize / tile, 2f * BackgroundHalfSize / tile, 2f * BackgroundHalfSize / tile));

            // Reference overlay aligned by the pot: outer rim (x, scale) and the top edge of the pot (y).
            if (art.Reference != null)
            {
                float refUpp = (PotOuterRight - PotOuterLeft) / Mathf.Max(1f, Style.RefPotOuterRightPx - Style.RefPotOuterLeftPx);
                float w = art.Reference.width * refUpp, h = art.Reference.height * refUpp;
                float x0 = PotOuterLeft - Style.RefPotOuterLeftPx * refUpp;
                float y1 = potRect.yMax + Style.RefPotTopPx * refUpp;
                _overlayRect = new Rect(x0, y1 - h, w, h);
                _overlay.Material.SetTexture(MainTexId, art.Reference);
                SetQuad(_overlay.Mesh, _overlayRect, new Rect(0f, 0f, 1f, 1f));
            }
            else _overlayRect = new Rect(0f, 0f, 0f, 0f);
            SetOverlay(_overlayOn, _overlayAlpha);

            PotRect = potRect;
            bounds.Encapsulate(new Vector3(potRect.xMin, potRect.yMin, 0f));
            bounds.Encapsulate(new Vector3(potRect.xMax, potRect.yMax, 0f));
            return bounds;
        }

        /// <summary>World y of the top of the SoilFront moss band after the last rebuild (moss line, harness).</summary>
        public float MossTopY { get; private set; }

        void ClearBands()
        {
            _bandPos.Clear();
            _bandUv.Clear();
            _bandUv1.Clear();
            _bandColors.Clear();
            _bandIdxList.Clear();
        }

        /// <summary>
        /// 4 columns × 3 rows (bottom y0 / ground y1 / top y2 with texture v0 / v1 / v2): alpha 0 on the outer columns and along the bottom
        /// edge (soft edges), 1 inside on the ground and top rows (the top row keeps the texture's own moss alpha).
        /// <paramref name="opaqueBottom"/> = false fades the ground row too (a tuft blends into the band below it).
        /// </summary>
        void AddBand(float x0, float x1, float y0, float y1, float y2, float u0, float u1, float v0, float v1, float v2, float edgeFade, bool opaqueBottom)
        {
            int start = _bandPos.Count;
            float fade = Mathf.Clamp(edgeFade, 0f, 0.45f) * (x1 - x0);
            float fu = Mathf.Clamp(edgeFade, 0f, 0.45f) * (u1 - u0);
            float[] xs = { x0, x0 + fade, x1 - fade, x1 };
            float[] us = { u0, u0 + fu, u1 - fu, u1 };
            for (int c = 0; c < 4; c++)
            {
                byte inner = (byte)(c == 0 || c == 3 ? 0 : 255);
                byte ground = opaqueBottom ? inner : (byte)(inner / 2);
                _bandPos.Add(new Vector3(xs[c], y0, 0f)); _bandUv.Add(new Vector2(us[c], v0)); _bandColors.Add(new Color32(255, 255, 255, 0));
                _bandPos.Add(new Vector3(xs[c], y1, 0f)); _bandUv.Add(new Vector2(us[c], v1)); _bandColors.Add(new Color32(255, 255, 255, ground));
                _bandPos.Add(new Vector3(xs[c], y2, 0f)); _bandUv.Add(new Vector2(us[c], v2)); _bandColors.Add(new Color32(255, 255, 255, inner));
                for (int k = 0; k < 3; k++) _bandUv1.Add(Vector2.zero); // brightness offset 0 (missing streams are undefined on Metal)
            }
            for (int c = 0; c < 3; c++)
            {
                for (int row = 0; row < 2; row++)
                {
                    int a = start + c * 3 + row, b = a + 3;
                    _bandIdxList.Add(a); _bandIdxList.Add(a + 1); _bandIdxList.Add(b + 1);
                    _bandIdxList.Add(a); _bandIdxList.Add(b + 1); _bandIdxList.Add(b);
                }
            }
        }

        void FlushBands(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_bandPos);
            mesh.SetUVs(0, _bandUv);
            mesh.SetUVs(1, _bandUv1);
            mesh.SetColors(_bandColors);
            mesh.SetTriangles(_bandIdxList, 0, true);
        }

        void SetQuad(Mesh mesh, Rect rect, Rect uv)
        {
            _quadPos.Clear();
            _quadUv.Clear();
            _quadPos.Add(new Vector3(rect.xMin, rect.yMin, 0f)); _quadUv.Add(new Vector2(uv.xMin, uv.yMin));
            _quadPos.Add(new Vector3(rect.xMin, rect.yMax, 0f)); _quadUv.Add(new Vector2(uv.xMin, uv.yMax));
            _quadPos.Add(new Vector3(rect.xMax, rect.yMax, 0f)); _quadUv.Add(new Vector2(uv.xMax, uv.yMax));
            _quadPos.Add(new Vector3(rect.xMax, rect.yMin, 0f)); _quadUv.Add(new Vector2(uv.xMax, uv.yMin));
            mesh.Clear();
            mesh.SetVertices(_quadPos);
            mesh.SetUVs(0, _quadUv);
            mesh.SetUVs(1, _quadUv1);
            mesh.SetColors(_quadColors);
            mesh.SetTriangles(_quadIdx, 0, true);
        }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] == null) continue;
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_owned[i]);
                else UnityEngine.Object.DestroyImmediate(_owned[i]);
            }
            _owned.Clear();
        }
    }
}
