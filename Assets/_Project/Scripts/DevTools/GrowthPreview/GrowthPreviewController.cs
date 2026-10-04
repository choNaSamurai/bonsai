using System;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-102V growth preview: IMGUI controls + debug mesh. Editor-only (asmdef define constraint UNITY_EDITOR).
    /// Open Assets/_Project/Scenes/Dev/GrowthPreview.unity and press Play.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class GrowthPreviewController : MonoBehaviour
    {
        static readonly string[] SpeciesNames = { "Kuromatsu", "Momiji", "Yamazakura" };
        static readonly string[] ModeNames = { "Wild", "Styled" };
        static readonly string[] StyleNames = { "Flat", "Textured" };
        static readonly string[] ShaderCandidates =
        {
            "Universal Render Pipeline/2D/Sprite-Unlit-Default",
            "Sprites/Default",
        };

        [SerializeField] Camera _camera;
        [SerializeField, Range(0, GrowthPreviewModel.MaxPreviewDay)] int _day = 40;
        [SerializeField] SpeciesId _species = SpeciesId.Kuromatsu;
        [SerializeField] uint _seed = 20261004u;
        [SerializeField, Range(0.5f, 1.5f)] float _vigor = 1f;
        [SerializeField, Range(-0.6f, 0.6f)] float _bendBias;
        [SerializeField, Range(-10f, 45f)] float _temperatureC = 25f;
        [SerializeField, Range(0f, 1f)] float _moisture = 0.5f;
        [SerializeField] bool _onboardingCut;
        [SerializeField, Tooltip("PA-102P: PruneToStyle S1–S7 on days 20/30/…/120")] bool _styled;
        [SerializeField, Tooltip("PA-103S: Textured = painted bark, atlas pads, pot/soil/washi art")] bool _textured;
        [SerializeField, Tooltip("PA-103S: realism_l2_painterly.jpg over the preview, aligned by the pot")] bool _referenceOverlay;
        [SerializeField, Range(0f, 1f)] float _referenceAlpha = 0.5f;

        GrowthPreviewModel _model;
        TreePreviewMeshBuilder _builder;
        Mesh _mesh;
        Material _material;
        string _seedText;
        string _status = "";
        int _shownDay = -1;
        int _shownCuts = -1;
        int _nodeCount;
        bool _paramsDirty = true;
        bool _shownTextured;
        PreviewArtLibrary _art;
        string _artRoot = PreviewArtLibrary.DefaultRoot;
        TexturedPreviewView _view;
        readonly TexturedStyleConfig _style = new TexturedStyleConfig();

        public GrowthPreviewModel Model
        {
            get
            {
                EnsureInitialized();
                return _model;
            }
        }

        public int Day => _day;
        public int NodeCount => _nodeCount;
        public Mesh PreviewMesh => _mesh;
        public Camera TargetCamera => _camera;

        public void SetDay(int day)
        {
            _day = Mathf.Clamp(day, 0, GrowthPreviewModel.MaxPreviewDay);
            Refresh();
        }

        public void SetSpecies(SpeciesId species)
        {
            _species = species;
            _paramsDirty = true;
            Refresh();
        }

        public void SetSeed(uint seed)
        {
            _seed = seed;
            _seedText = seed.ToString();
            _paramsDirty = true;
            Refresh();
        }

        public bool Styled => _styled;

        /// <summary>PA-102P harness: Styled = planting → onboarding cut → styling (AC notation).</summary>
        public void SetOnboardingCut(bool cut)
        {
            _onboardingCut = cut;
            _paramsDirty = true;
            Refresh();
        }

        /// <summary>PA-102S: Wild (no pruning) or Styled (PruneToStyle S1–S7 on StyleConfig days).</summary>
        public void SetStyled(bool styled)
        {
            _styled = styled;
            _paramsDirty = true;
            Refresh();
        }

        public bool Textured => _textured;

        /// <summary>PA-103S: Flat (PA-102S vertex colours) or Textured (painted bark, atlas pads, pot/soil/washi).</summary>
        public void SetTextured(bool textured)
        {
            _textured = textured;
            _shownDay = -1;
            Refresh();
        }

        /// <summary>PA-103S: reference overlay (realism_l2_painterly.jpg aligned by the pot) and its alpha.</summary>
        public void SetReferenceOverlay(bool visible, float alpha)
        {
            _referenceOverlay = visible;
            _referenceAlpha = Mathf.Clamp01(alpha);
            _view?.SetOverlay(_referenceOverlay, _referenceAlpha);
        }

        /// <summary>Re-reads the art folder (after Technical Artist adds files). Tests may point it to another folder.</summary>
        public void ReloadArt(string root = null)
        {
            if (root != null) _artRoot = root;
            _art?.Dispose();
            _art = new PreviewArtLibrary(_artRoot);
            _art.Load();
            _shownDay = -1;
            Refresh();
        }

        public PreviewArtLibrary Art => _art;
        public TexturedPreviewView TexturedView => _view;
        public TexturedStyleConfig Style => _style;

        public float TaperRatio => _builder != null ? _builder.LastTaperRatio : 0f;
        public int PadCount => _builder != null ? _builder.LastPadCount : 0;
        public TreePreviewMeshBuilder Builder => _builder;

        public void SetWeather(float temperatureC, float moisture)
        {
            _temperatureC = temperatureC;
            _moisture = moisture;
            _paramsDirty = true;
            Refresh();
        }

        public PruneResult CutRandomBranch()
        {
            EnsureInitialized();
            PruneResult result = _model.CutRandomBranch(_day);
            _status = result.Succeeded
                ? "Cut " + result.RemovedCount + " node(s)" + (result.PhoenixSpawned ? " -> Phoenix bud" : "")
                : "Cut: " + result.Status;
            Refresh();
            return result;
        }

        /// <summary>Rebuilds the mesh if anything changed and frames the camera.</summary>
        public void Refresh()
        {
            EnsureInitialized();
            if (_paramsDirty)
            {
                _model.SetParameters(_species, _seed, _vigor, _bendBias, _temperatureC, _moisture, _onboardingCut);
                _model.SetStyled(_styled);
                _paramsDirty = false;
                _shownDay = -1;
            }
            if (_shownDay == _day && _shownCuts == _model.CutCount && _shownTextured == _textured) return;

            TreeGraph tree = _model.GetTree(_day);
            _nodeCount = tree.Count;
            Bounds bounds;
            if (_textured)
            {
                EnsureTextured();
                bounds = _view.Rebuild(tree, _model, _builder, _art);
            }
            else bounds = _builder.Build(tree, _mesh, _model.Config, _model.SpeciesConfig);
            GetComponent<MeshRenderer>().enabled = !_textured;
            _view?.SetVisible(_textured);
            FrameCamera(bounds);
            _shownDay = _day;
            _shownCuts = _model.CutCount;
            _shownTextured = _textured;
        }

        void EnsureTextured()
        {
            if (_art == null)
            {
                _art = new PreviewArtLibrary(_artRoot);
                _art.Load();
            }
            if (_view == null)
            {
                _view = new TexturedPreviewView(transform, _style);
                _view.SetOverlay(_referenceOverlay, _referenceAlpha);
            }
        }

        void DisposeTextured()
        {
            _view?.Dispose();
            _view = null;
            _art?.Dispose();
            _art = null;
            _shownDay = -1;
        }

        /// <summary>PA-103S compare frame: camera exactly over the reference image rect (pot-aligned), no panel offset.</summary>
        public bool FrameReference(Camera camera)
        {
            if (_view == null || _view.OverlayRect.width <= 0f) return false;
            Rect r = _view.OverlayRect;
            camera.orthographic = true;
            camera.orthographicSize = 0.5f * r.height;
            camera.transform.position = new Vector3(r.center.x, r.center.y, -100f);
            return true;
        }

        void EnsureInitialized()
        {
            if (_model != null) return;
            _model = new GrowthPreviewModel();
            _builder = new TreePreviewMeshBuilder();
            _mesh = new Mesh { name = "GrowthPreviewMesh", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            Shader shader = null;
            for (int i = 0; i < ShaderCandidates.Length && shader == null; i++) shader = Shader.Find(ShaderCandidates[i]);
            _material = new Material(shader) { name = "GrowthPreviewMaterial", hideFlags = HideFlags.DontSave };
            GetComponent<MeshRenderer>().sharedMaterial = _material;

            if (_camera == null) _camera = Camera.main;
            _seedText = _seed.ToString();
            _paramsDirty = true;
        }

        void OnEnable()
        {
            _shownDay = -1;
            Refresh();
        }

        void OnValidate()
        {
            _paramsDirty = true;
            _shownDay = -1;
        }

        void Update()
        {
            Refresh();
        }

        void OnDisable()
        {
            DisposeTextured();
        }

        void OnDestroy()
        {
            DisposeTextured();
            DestroySafely(_mesh);
            DestroySafely(_material);
        }

        static void DestroySafely(UnityEngine.Object target)
        {
            if (target == null) return;
            if (UnityEngine.Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        /// <summary>Zoom z of the pot-based frame chosen by the last refresh (TD-030 prototype, ZoomMin 0.78 — the pot is never cut).</summary>
        public float FramedZoom { get; private set; } = 1f;

        /// <summary>Top of the pot in world units (Textured: the sprite's back rim; Flat: the Pot Lip y = 0).</summary>
        float PotTopY => _textured && _view != null ? _view.PotRect.yMax : 0f;

        /// <summary>
        /// PA-102P / TD-030: the camera frames the pot first (fixed 92 units) and zooms out only as far as the tree needs
        /// (<see cref="PotFraming"/>); the whole pot always stays in view. <paramref name="bounds"/> is unused (kept for the Flat path).
        /// </summary>
        void FrameCamera(Bounds bounds)
        {
            if (_camera == null) return;
            float zoom = PotFraming.Zoom(_builder.LastPotWidth, PotTopY, _builder.LastTreeRect);
            FramedZoom = zoom;
            Rect frame = PotFraming.FrameAt(zoom, _builder.LastPotWidth, PotTopY);
            float aspect = _camera.pixelHeight > 0 ? _camera.pixelWidth / (float)_camera.pixelHeight : 1f;
            // Reserve the left 30% of the screen for the control panel when it is visible.
            float panelShare = UnityEngine.Application.isPlaying ? 0.3f : 0f;
            float usableAspect = Mathf.Max(0.1f, aspect * (1f - panelShare));
            float halfHeight = Mathf.Max(0.5f * frame.height, 0.5f * frame.width / usableAspect);
            _camera.orthographic = true;
            _camera.orthographicSize = halfHeight;
            float shiftX = panelShare * aspect * halfHeight; // centre the frame in the free area
            // Keep the frame's bottom (the pot) at the bottom of the view when the window is taller than the frame.
            float centerY = frame.yMin + halfHeight;
            _camera.transform.position = new Vector3(frame.center.x - shiftX, centerY, -100f);
        }

        void OnGUI()
        {
            if (!UnityEngine.Application.isPlaying) return;
            EnsureInitialized();
            float scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Mathf.Min(320f, Screen.width / scale * 0.3f);

            GUILayout.BeginArea(new Rect(10f, 10f, width, Screen.height / scale - 20f), GUI.skin.box);
            GUILayout.Label("Growth Preview (PA-102V/102S, dev only)");

            int day = Mathf.RoundToInt(LabeledSlider("Day", _day, 0f, GrowthPreviewModel.MaxPreviewDay, "0"));
            if (day != _day) _day = day;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-1")) _day = Mathf.Max(0, _day - 1);
            if (GUILayout.Button("+1")) _day = Mathf.Min(GrowthPreviewModel.MaxPreviewDay, _day + 1);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            int species = GUILayout.Toolbar((int)_species, SpeciesNames);
            if (species != (int)_species)
            {
                _species = (SpeciesId)species;
                _paramsDirty = true;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", GUILayout.Width(40f));
            string seedText = GUILayout.TextField(_seedText ?? "");
            if (seedText != _seedText)
            {
                _seedText = seedText;
                if (uint.TryParse(seedText, out uint parsed) && parsed != _seed)
                {
                    _seed = parsed;
                    _paramsDirty = true;
                }
            }
            if (GUILayout.Button("Random", GUILayout.Width(70f)))
            {
                _seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
                _seedText = _seed.ToString();
                _paramsDirty = true;
            }
            GUILayout.EndHorizontal();

            ParamSlider("Temperature °C", ref _temperatureC, -10f, 45f, "0.0");
            ParamSlider("Moisture W", ref _moisture, 0f, 1f, "0.00");
            ParamSlider("Vigor", ref _vigor, 0.5f, 1.5f, "0.00");
            ParamSlider("Bend bias", ref _bendBias, -0.6f, 0.6f, "0.00");
            bool cut = GUILayout.Toggle(_onboardingCut, " Onboarding cut (remove wild twig at day 0)");
            if (cut != _onboardingCut)
            {
                _onboardingCut = cut;
                _paramsDirty = true;
            }

            int mode = GUILayout.Toolbar(_styled ? 1 : 0, ModeNames);
            if (mode != (_styled ? 1 : 0))
            {
                _styled = mode == 1;
                _paramsDirty = true;
            }

            GUILayout.Space(6f);
            GUILayout.Label("Style (PA-103S)");
            int style = GUILayout.Toolbar(_textured ? 1 : 0, StyleNames);
            if (style != (_textured ? 1 : 0)) _textured = style == 1;
            if (_textured)
            {
                bool overlay = GUILayout.Toggle(_referenceOverlay, " Reference overlay (L2 painterly)");
                float alpha = _referenceAlpha;
                if (overlay) alpha = LabeledSlider("Overlay alpha", _referenceAlpha, 0f, 1f, "0.00");
                if (overlay != _referenceOverlay || !Mathf.Approximately(alpha, _referenceAlpha)) SetReferenceOverlay(overlay, alpha);
                if (GUILayout.Button("Reload art (Art/Runtime/Preview)"))
                {
                    UnityEditor.AssetDatabase.Refresh();
                    ReloadArt();
                }
                if (_art != null) GUILayout.Label("Art: " + _art.Summary);
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Cut random branch (at this day)")) CutRandomBranch();

            Refresh();
            GUILayout.Space(6f);
            GUILayout.Label("Nodes: " + _nodeCount + " / " + _model.Config.MaxNodes);
            int reach = _model.DayReachingMaxNodes();
            GUILayout.Label(reach >= 0 ? "Reaches " + _model.Config.MaxNodes + " nodes on day " + reach
                : "Does not reach " + _model.Config.MaxNodes + " within " + GrowthPreviewModel.Reach200SearchDays + " days");
            TreeSnapshot snapshot = _model.GetDay(_day);
            GUILayout.Label("Trunk thickness: " + snapshot.Nodes[0].Thickness.ToString("0.00") + "  ShootCredit: " + snapshot.Growth.ShootCredit.ToString("0.00"));
            GUILayout.Label("Recorded cuts: " + _model.CutCount);
            GUILayout.Label("Pads: " + _builder.LastPadCount + "  Taper (Nebari base / 2/3 h): " + _builder.LastTaperRatio.ToString("0.0"));
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);
            GUILayout.EndArea();
        }

        void ParamSlider(string label, ref float value, float min, float max, string format)
        {
            float next = LabeledSlider(label, value, min, max, format);
            if (!Mathf.Approximately(next, value))
            {
                value = next;
                _paramsDirty = true;
            }
        }

        static float LabeledSlider(string label, float value, float min, float max, string format)
        {
            GUILayout.Label(label + ": " + value.ToString(format));
            return GUILayout.HorizontalSlider(value, min, max);
        }
    }
}
