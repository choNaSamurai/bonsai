using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Spike scene component: pick species / style / seed / age in the inspector, the tree is rebuilt and baked.
    /// Data source: the C# generator port (when <see cref="SketchFrameSources.Generator"/> is registered), else the
    /// nearest golden configuration (seed 3/27, t 0.3/0.6/1.0). Edit-mode friendly ([ExecuteAlways]).
    /// </summary>
    [ExecuteAlways]
    public sealed class SketchTreeView : MonoBehaviour
    {
        public enum Species { momiji, yamazakura, kuromatsu }
        public enum Style { moyogi, chokkan, shakan, bunjin, fukinagashi }

        public Species species = Species.momiji;
        public Style style = Style.moyogi;
        public int seed = 3;
        [Range(0f, 1f)] public float age = 1f;
        [Tooltip("Folder with golden <key>.json/.bin; empty = .agent-workspace/pa102xs/golden")]
        public string goldenFolder = "";
        [Tooltip("Camera to configure with the sketch's tilted orthographic projection")]
        public Camera sketchCamera;
        [Tooltip("Camera clear colour in the spike scene (harness renders use transparent)")]
        public Color background = new Color(0.93f, 0.92f, 0.9f, 1f);

        [Header("Last build (read-only)")]
        [SerializeField] private string _source = "";
        [SerializeField] private string _stats = "";

        public string LastSource => _source;
        public string LastStats => _stats;

        private SketchTreeRenderer _r;
        private readonly SketchFrame _frame = new SketchFrame();
        private bool _dirty = true;

        private void OnEnable()
        {
            _dirty = true;
        }

        private void OnValidate()
        {
            _dirty = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            Rebuild();
        }

        private void OnDisable()
        {
            if (_r != null) _r.Dispose();
            _r = null;
        }

        public void Rebuild()
        {
            if (sketchCamera != null)
            {
                SketchCamera.Apply(sketchCamera);
                sketchCamera.backgroundColor = background;
            }
            string sp = species.ToString(), st = style.ToString();
            if (!SketchFrameSources.TryGet(sp, st, seed, age, goldenFolder, _frame, out _source))
            {
                _stats = "no data for " + SketchFrame.MakeKey(sp, st, seed, age);
                return;
            }
            if (_r == null) _r = new SketchTreeRenderer(transform);
            SketchTreeRenderer.Stats s = _r.Show(_frame);
            _stats = string.Format("wood {0:0.0} ms, {1} tris | pot {2} tris | leaves {3} ({4:0.0} ms) | bake {5:0.0} ms",
                s.WoodMs, s.WoodTris, s.PotTris, s.Leaves, s.LeafMs, s.BakeMs);
        }
    }

    /// <summary>Where frames come from: generator port (registered by an adapter) or golden files.</summary>
    public static class SketchFrameSources
    {
        public delegate bool GeneratorFn(string species, string style, int seed, float t, SketchFrame into);

        /// <summary>The C# generator port (TreeGen, G1); set to null to force golden files.</summary>
        public static GeneratorFn Generator = TreeGenAdapter.Generate;

        private static readonly float[] GoldenT = { 0.3f, 0.6f, 1f };

        public static bool TryGet(string species, string style, int seed, float t, string goldenFolder, SketchFrame into, out string source)
        {
            if (Generator != null && Generator(species, style, seed, t, into))
            {
                source = "generator " + SketchFrame.MakeKey(species, style, seed, t);
                return true;
            }
            string folder = string.IsNullOrEmpty(goldenFolder) ? GoldenFrameLoader.DefaultFolder() : goldenFolder;
            float best = GoldenT[0];
            for (int i = 1; i < GoldenT.Length; i++)
                if (Mathf.Abs(GoldenT[i] - t) < Mathf.Abs(best - t)) best = GoldenT[i];
            int gs = seed == 27 ? 27 : 3;
            string key = SketchFrame.MakeKey(species, style, gs, best);
            if (GoldenFrameLoader.TryLoad(folder, key, into))
            {
                source = "golden " + key;
                return true;
            }
            source = "missing " + key;
            return false;
        }
    }
}
