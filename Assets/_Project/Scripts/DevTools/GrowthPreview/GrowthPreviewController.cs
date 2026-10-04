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
        [SerializeField, Tooltip("PA-102S: PruneToStyle on days 20/30/…/70")] bool _styled;
        [SerializeField] float _framePadding = 0.12f;

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

        /// <summary>PA-102S: Wild (no pruning) or Styled (PruneToStyle on GrowthPreviewModel.StyleDays).</summary>
        public void SetStyled(bool styled)
        {
            _styled = styled;
            _paramsDirty = true;
            Refresh();
        }

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
            if (_shownDay == _day && _shownCuts == _model.CutCount) return;

            TreeGraph tree = _model.GetTree(_day);
            _nodeCount = tree.Count;
            Bounds bounds = _builder.Build(tree, _mesh, _model.Config, _model.SpeciesConfig);
            FrameCamera(bounds);
            _shownDay = _day;
            _shownCuts = _model.CutCount;
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

        void OnDestroy()
        {
            DestroySafely(_mesh);
            DestroySafely(_material);
        }

        static void DestroySafely(UnityEngine.Object target)
        {
            if (target == null) return;
            if (UnityEngine.Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        void FrameCamera(Bounds bounds)
        {
            if (_camera == null) return;
            float aspect = _camera.pixelHeight > 0 ? _camera.pixelWidth / (float)_camera.pixelHeight : 1f;
            // Reserve the left 30% of the screen for the control panel when it is visible.
            float panelShare = UnityEngine.Application.isPlaying ? 0.3f : 0f;
            float usableAspect = aspect * (1f - panelShare);
            float halfHeight = Mathf.Max(bounds.extents.y, bounds.extents.x / Mathf.Max(0.1f, usableAspect)) * (1f + _framePadding);
            _camera.orthographic = true;
            _camera.orthographicSize = Mathf.Max(10f, halfHeight);
            float shiftX = panelShare * aspect * _camera.orthographicSize; // centre the tree in the free area
            _camera.transform.position = new Vector3(bounds.center.x - shiftX, bounds.center.y, -100f);
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
