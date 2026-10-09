using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PocketArbor.Spike.Device;
using PocketArbor.Spike.Render;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PocketArbor.Spike.Editor
{
    /// <summary>
    /// PA-102X-S phase 2a: builds the standalone spike app (com.pocketarbor.spike.pa102xs) for Android.
    /// Runs ONLY on the project copy via unity-run.sh (-buildTarget Android); PlayerSettings are set here in code and
    /// are reset by the wrapper's rsync from the spike worktree on the next run. Variants: vulkan, gles3, vulkan-dev.
    ///   unity-run.sh exec PocketArbor.Spike.Editor.SpikeAndroidBuild.Build -buildTarget Android [-spikeVariants vulkan,gles3,vulkan-dev]
    /// </summary>
    public static class SpikeAndroidBuild
    {
        private const string SceneDir = "Assets/PA102XS_DeviceBuild";
        private const string ScenePath = SceneDir + "/PA102XS_Device.unity";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Build()
        {
            string ws = GoldenFrameLoader.WorkspaceFolder() ?? throw new DirectoryNotFoundException("pa102xs workspace");
            string outDir = Path.Combine(ws, "device", "builds");
            Directory.CreateDirectory(outDir);
            string variantsArg = Arg("-spikeVariants") ?? "vulkan,gles3,vulkan-dev";
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            MakeScene();
            ConfigurePlayer();
            if (Arg("-spikeAllowVulkan") != "0") AllowVulkanOnAllDevices();
            var summary = new StringBuilder();
            bool failed = false;
            foreach (string v in variantsArg.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool dev = v.EndsWith("-dev", StringComparison.Ordinal);
                bool empty = v.EndsWith("-empty", StringComparison.Ordinal);
                bool gles = v.StartsWith("gles", StringComparison.Ordinal);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { gles ? GraphicsDeviceType.OpenGLES3 : GraphicsDeviceType.Vulkan });
                EditorUserBuildSettings.development = dev;
                string apk = Path.Combine(outDir, "PA102XS_" + v.Replace('-', '_') + ".apk");
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { empty ? MakeEmptyScene() : ScenePath },
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = dev ? BuildOptions.Development : BuildOptions.None
                };
                Debug.Log("SPIKE-BUILD| building " + v + " -> " + apk);
                BuildReport r = BuildPipeline.BuildPlayer(opts);
                string md = Describe(v, r, apk);
                File.WriteAllText(Path.Combine(outDir, "build_" + v + ".md"), md);
                summary.AppendLine(md);
                Debug.Log("SPIKE-BUILD| " + v + " " + r.summary.result + " " + r.summary.totalSize + " B, " + r.summary.totalTime);
                if (r.summary.result != BuildResult.Succeeded) failed = true;
            }
            File.WriteAllText(Path.Combine(outDir, "builds.md"), summary.ToString());
            if (failed) throw new InvalidOperationException("spike Android build failed (see SPIKE-BUILD lines)");
        }

        private static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        private static void MakeScene()
        {
            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("SketchCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            SketchCamera.Apply(cam);
            var runner = new GameObject("PA102XS_DeviceRunner").AddComponent<SpikeDeviceRunner>();
            runner.cam = cam;
            string[] names = { SketchTreeRenderer.WoodShader, SketchTreeRenderer.PotShader, SketchTreeRenderer.LeafShader, SketchTreeRenderer.BakeShader };
            runner.shaders = new Shader[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                runner.shaders[i] = Shader.Find(names[i]);
                if (runner.shaders[i] == null) throw new InvalidOperationException("shader missing: " + names[i]);
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        /// <summary>K10 baseline: same project and settings, a scene with only a camera (spike code/shaders unreferenced, stripped).</summary>
        private static string MakeEmptyScene()
        {
            const string path = SceneDir + "/PA102XS_Empty.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
            return path;
        }

        private static void ConfigurePlayer()
        {
            NamedBuildTarget nbt = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(nbt, "com.pocketarbor.spike.pa102xs");
            PlayerSettings.productName = "PA102XS Spike";
            PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Il2CppCompilerConfiguration.Release);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            EditorUserBuildSettings.buildAppBundle = false;
            Debug.Log("SPIKE-BUILD| stripping " + PlayerSettings.GetManagedStrippingLevel(nbt) + ", minSdk " + PlayerSettings.Android.minSdkVersion + ", targetSdk " + PlayerSettings.Android.targetSdkVersion);
        }

        /// <summary>
        /// Unity 6 denies Vulkan on Google Pixel 6 by default (built-in Vulkan device filter). For the spike's K11 Vulkan
        /// measurements the copy's PlayerSettings get a VulkanDeviceFilterLists asset with EMPTY lists. Reflection, because
        /// the type lives in an Android editor assembly; every step is logged with SPIKE-VK.
        /// </summary>
        private static void AllowVulkanOnAllDevices()
        {
            try
            {
                var lists = new VulkanDeviceFilterLists();
                Debug.Log("SPIKE-VK| new lists: deny " + (lists.vulkanDeviceDenyFilters?.Length ?? -1) + ", allow " + (lists.vulkanDeviceAllowFilters?.Length ?? -1));
                lists.vulkanDeviceDenyFilters = new VulkanDeviceFilterData[0];
                // The Pixel 6 family deny rule is applied even with an empty deny list, so allow this device explicitly.
                var allow = new VulkanDeviceFilterData();
                allow.brandName = "(G|g)oogle";
                allow.productName = "(oriole|raven|bluejay)";
                allow.vulkanApiVersionString = "1.0.0";
                allow.driverVersionString = "0.0.0";
                lists.vulkanDeviceAllowFilters = new[] { allow };
                const string path = SceneDir + "/PA102XS_VulkanAllowAll.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(lists, path);
                AssetDatabase.SaveAssets();
                System.Reflection.PropertyInfo p = typeof(PlayerSettings.Android).GetProperty("androidVulkanDeviceFilterListAsset",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (p == null) { Debug.LogWarning("SPIKE-VK| PlayerSettings.Android.androidVulkanDeviceFilterListAsset not found"); return; }
                p.SetValue(null, AssetDatabase.LoadAssetAtPath<VulkanDeviceFilterLists>(path));
                var cur = p.GetValue(null) as VulkanDeviceFilterLists;
                Debug.Log("SPIKE-VK| assigned " + path + ": deny " + (cur != null ? cur.vulkanDeviceDenyFilters.Length : -1) + ", allow " + (cur != null ? cur.vulkanDeviceAllowFilters.Length : -1));
            }
            catch (Exception e)
            {
                Debug.LogWarning("SPIKE-VK| failed: " + e);
            }
        }

        private static string Describe(string variant, BuildReport r, string apk)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## " + variant);
            sb.AppendLine("- result: " + r.summary.result + ", time " + r.summary.totalTime + ", errors " + r.summary.totalErrors + ", warnings " + r.summary.totalWarnings);
            long apkSize = File.Exists(apk) ? new FileInfo(apk).Length : -1;
            sb.AppendLine("- APK: `" + Path.GetFileName(apk) + "` " + Mb(apkSize) + " (BuildReport totalSize " + Mb((long)r.summary.totalSize) + ")");
            sb.AppendLine("- PlayerSettings: IL2CPP Release, ARM64, stripping " + PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android) + ", APIs " +
                          string.Join("/", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)) + ", development " + EditorUserBuildSettings.development);
            // Spike shaders and other spike assets inside the build (uncompressed packed sizes).
            long spikeShaders = 0, allShaders = 0;
            var rows = new List<string>();
            foreach (PackedAssets pa in r.packedAssets)
            foreach (PackedAssetInfo c in pa.contents)
            {
                bool isShader = c.type == typeof(Shader);
                if (isShader) allShaders += (long)c.packedSize;
                if (c.sourceAssetPath != null && c.sourceAssetPath.Contains("Spikes/PA102XS"))
                {
                    if (isShader) spikeShaders += (long)c.packedSize;
                    rows.Add("  - " + c.sourceAssetPath + " (" + c.type.Name + ") " + Kb((long)c.packedSize));
                }
            }
            sb.AppendLine("- shaders in build (packed, uncompressed): spike " + Kb(spikeShaders) + ", all " + Kb(allShaders));
            foreach (string row in rows) sb.AppendLine(row);
            long so = 0;
            foreach (BuildFile f in r.GetFiles())
                if (f.path.EndsWith("libil2cpp.so", StringComparison.Ordinal)) so = Math.Max(so, (long)f.size);
            sb.AppendLine("- libil2cpp.so (arm64, whole app): " + Mb(so));
            // Generated C++ of the spike assemblies (proxy for the code share; not a binary size).
            long cpp = 0, cppAll = 0;
            string bee = Path.Combine("Library", "Bee", "artifacts", "Android");
            if (Directory.Exists(bee))
            {
                foreach (string f in Directory.GetFiles(bee, "*.cpp", SearchOption.AllDirectories))
                {
                    if (!f.Contains("il2cppOutput")) continue;
                    long len = new FileInfo(f).Length;
                    cppAll += len;
                    if (Path.GetFileName(f).StartsWith("PocketArbor.Spike.", StringComparison.Ordinal)) cpp += len;
                }
            }
            sb.AppendLine("- generated C++ (il2cppOutput): spike assemblies " + Kb(cpp) + " of " + Mb(cppAll) + " total");
            return sb.ToString();
        }

        private static string Kb(long b) => b < 0 ? "?" : (b / 1024.0).ToString("0.0", Inv) + " KB";
        private static string Mb(long b) => b < 0 ? "?" : (b / 1048576.0).ToString("0.00", Inv) + " MB";
    }
}
