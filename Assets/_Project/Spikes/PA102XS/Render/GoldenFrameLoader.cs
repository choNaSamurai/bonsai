using System;
using System.IO;
using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Reads golden &lt;key&gt;.json + &lt;key&gt;.bin (exported from sketch_v11) straight into a <see cref="SketchFrame"/>.
    /// Independent of the generator port, so rendering can be validated against the sketch on its own.
    /// </summary>
    public static class GoldenFrameLoader
    {
        [Serializable] private sealed class JScene { public float L; public float R; public int nr, ng, nb, nl, nx; }
        [Serializable] private sealed class JEnv { public float m, h; public float[] L; public float[] sunC; public float[] amb; public float sI; }
        [Serializable] private sealed class JArr { public string type; public int offset; public int length; }
        [Serializable] private sealed class JArrays { public JArr P, T, DAT, CB, GM, PE, LEAF, XLEAF; }
        [Serializable] private sealed class JRoot
        {
            public string key, species, style;
            public int seed;
            public float t;
            public JScene scene;
            public int nc, np;
            public JEnv env;
            public JArrays arrays;
        }

        /// <summary>Default golden folder: .agent-workspace/pa102xs/golden, found from the project copy or the main project.</summary>
        public static string DefaultFolder()
        {
            string ws = WorkspaceFolder();
            return ws == null ? null : Path.Combine(ws, "golden");
        }

        /// <summary>.agent-workspace/pa102xs (works both from .agent-workspace/proj and from the main project).</summary>
        public static string WorkspaceFolder()
        {
            string data = Application.dataPath;
            string a = Path.GetFullPath(Path.Combine(data, "..", "..", "pa102xs"));
            if (Directory.Exists(a)) return a;
            string b = Path.GetFullPath(Path.Combine(data, "..", ".agent-workspace", "pa102xs"));
            if (Directory.Exists(b)) return b;
            return null;
        }

        public static bool TryLoad(string folder, string key, SketchFrame f)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string json = Path.Combine(folder, key + ".json");
            if (!File.Exists(json)) return false;
            Load(json, f);
            return true;
        }

        public static void Load(string jsonPath, SketchFrame f)
        {
            JRoot j = JsonUtility.FromJson<JRoot>(File.ReadAllText(jsonPath));
            byte[] bin = File.ReadAllBytes(Path.ChangeExtension(jsonPath, ".bin"));
            f.Key = j.key;
            f.Species = j.species;
            f.Style = j.style;
            f.Seed = j.seed;
            f.T = j.t;
            f.L = j.scene.L;
            f.R = j.scene.R;
            f.Nr = j.scene.nr;
            f.Ng = j.scene.ng;
            f.Nb = j.scene.nb;
            f.Nl = j.scene.nl;
            f.Nx = j.scene.nx;
            f.Nc = j.nc;
            f.Np = j.np;
            if (j.env != null && j.env.L != null && j.env.L.Length == 3)
            {
                f.LightDir = new Vector3(j.env.L[0], j.env.L[1], j.env.L[2]);
                if (j.env.sunC != null && j.env.sunC.Length == 3) f.SunColor = new Vector3(j.env.sunC[0], j.env.sunC[1], j.env.sunC[2]);
                if (j.env.amb != null && j.env.amb.Length == 3) f.Ambient = new Vector3(j.env.amb[0], j.env.amb[1], j.env.amb[2]);
                f.SunIntensity = j.env.sI;
            }
            CopyF32(bin, j.arrays.DAT, f.Dat);
            CopyF32(bin, j.arrays.CB, f.Cb);
            CopyF32(bin, j.arrays.GM, f.Gm);
            CopyF32(bin, j.arrays.PE, f.Pe);
            CopyF32(bin, j.arrays.LEAF, f.Leaf);
        }

        private static void CopyF32(byte[] bin, JArr a, float[] dst)
        {
            Array.Clear(dst, 0, dst.Length);
            if (a == null || a.length == 0) return;
            if (a.type != "f32") throw new InvalidDataException("expected f32 array, got " + a.type);
            int n = Math.Min(a.length, dst.Length);
            Buffer.BlockCopy(bin, a.offset, dst, 0, n * 4);
        }
    }
}
