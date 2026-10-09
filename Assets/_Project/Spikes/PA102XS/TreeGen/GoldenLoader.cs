// PA-102X-S spike: loader of golden data exported from sketch_v11 (.agent-workspace/pa102xs/golden/<key>.json + .bin).
using System;
using System.Collections.Generic;
using System.IO;

namespace PocketArbor.Spike.TreeGen
{
    /// <summary>One golden configuration: the scene as the renderer sees it + raw build data for diagnostics.</summary>
    public sealed class GoldenData
    {
        public string Key;
        /// <summary>Scene with arrays copied into full-capacity buffers; counters from the json.</summary>
        public SketchScene Scene;
        /// <summary>Trunk axis polyline, (N+1) x 2 interleaved x,y (JS M.P), f64.</summary>
        public double[] P;
        /// <summary>Trunk angle per sample, N+1 (JS M.T), f64.</summary>
        public double[] T;
        /// <summary>Raw json "build" (n, Rmax, cx, bends, roots, br, apex, apj).</summary>
        public JsonNode Build;
        /// <summary>Raw json "env" (m, h, L, sunC, amb, sI, LL).</summary>
        public JsonNode Env;
        /// <summary>Whole json document.</summary>
        public JsonNode Json;
        /// <summary>Exported lengths (elements) of the arrays in .bin.</summary>
        public Dictionary<string, int> ArrayLengths = new Dictionary<string, int>();
    }

    public static class GoldenLoader
    {
        public const string DefaultDir = "/Users/pavlyshyn/Developing/games/bonsai/.agent-workspace/pa102xs/golden";
        public const string EnvVar = "PA102XS_GOLDEN";

        /// <summary>Golden directory: $PA102XS_GOLDEN or <see cref="DefaultDir"/>.</summary>
        public static string Dir
        {
            get
            {
                string d = Environment.GetEnvironmentVariable(EnvVar);
                return string.IsNullOrEmpty(d) ? DefaultDir : d;
            }
        }

        /// <summary>All golden keys in the directory (sorted ordinal).</summary>
        public static string[] Keys(string dir = null)
        {
            dir = dir ?? Dir;
            if (!Directory.Exists(dir)) return new string[0];
            string[] files = Directory.GetFiles(dir, "*.json");
            var keys = new string[files.Length];
            for (int i = 0; i < files.Length; i++) keys[i] = Path.GetFileNameWithoutExtension(files[i]);
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }

        /// <summary>Shortcut: the renderer-facing scene only.</summary>
        public static SketchScene LoadScene(string key, string dir = null)
        {
            return Load(key, dir).Scene;
        }

        public static GoldenData Load(string key, string dir = null)
        {
            dir = dir ?? Dir;
            string jsonPath = Path.Combine(dir, key + ".json");
            string binPath = Path.Combine(dir, key + ".bin");
            JsonNode j = MiniJson.Parse(File.ReadAllText(jsonPath));
            byte[] bin = File.ReadAllBytes(binPath);

            var g = new GoldenData { Key = key, Json = j, Build = j["build"], Env = j["env"] };
            JsonNode arrays = j["arrays"];
            foreach (KeyValuePair<string, JsonNode> kv in arrays.Fields) g.ArrayLengths[kv.Key] = kv.Value["length"].I;

            g.P = ReadF64(bin, arrays["P"]);
            g.T = ReadF64(bin, arrays["T"]);

            string species = j["species"].S;
            string style = j["style"].S;
            var s = new SketchScene
            {
                SpeciesKey = species,
                Style = style,
                Seed = j["seed"].I,
                T = j["t"].D,
                Species = SketchSpecies.ForStyle(species, style),
                LightDir = j["env"]["LL"].ToD3()
            };
            s.Pot = s.Species.Pot;

            JsonNode sc = j["scene"];
            s.L = sc["L"].D;
            s.R = sc["R"].D;
            s.Nr = sc["nr"].I;
            s.Ng = sc["ng"].I;
            s.Nb = sc["nb"].I;
            s.Nl = sc["nl"].I;
            s.Nx = sc["nx"].I;
            s.Nc = j["nc"].I;
            s.Np = j["np"].I;

            ReadF32Into(bin, arrays["DAT"], s.Dat);
            ReadF32Into(bin, arrays["CB"], s.Cb);
            ReadF32Into(bin, arrays["GM"], s.Gm);
            ReadF32Into(bin, arrays["PE"], s.Pe);
            ReadF32Into(bin, arrays["LEAF"], s.Leaf);
            ReadF32Into(bin, arrays["XLEAF"], s.XLeaf);

            JsonNode pads = j["pads"];
            if (pads.Count > s.Pads.Length) throw new InvalidDataException("Too many pads in " + key);
            for (int i = 0; i < pads.Count; i++)
            {
                JsonNode p = pads[i];
                s.Pads[i] = new Pad
                {
                    C = p["c"].ToD3(),
                    A = p["a"].D,
                    B = p["b"].D,
                    Yb = p["yb"].D,
                    Hx = p["hx"].ToD3(),
                    Pp = p["pp"].ToD3(),
                    Dome = p["dome"].B,
                    Key = p["key"].I,
                    Gp = p["gp"].D
                };
            }

            g.Scene = s;
            return g;
        }

        private static double[] ReadF64(byte[] bin, JsonNode a)
        {
            if (a["type"].S != "f64") throw new InvalidDataException("Expected f64");
            int off = a["offset"].I, len = a["length"].I;
            var r = new double[len];
            Buffer.BlockCopy(bin, off, r, 0, len * 8);
            return r;
        }

        private static void ReadF32Into(byte[] bin, JsonNode a, float[] dst)
        {
            if (a["type"].S != "f32") throw new InvalidDataException("Expected f32");
            int off = a["offset"].I, len = a["length"].I;
            if (len > dst.Length) throw new InvalidDataException("Array longer than capacity: " + len + " > " + dst.Length);
            Buffer.BlockCopy(bin, off, dst, 0, len * 4);
        }
    }
}
