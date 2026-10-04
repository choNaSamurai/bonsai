using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class Spike
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-" + name) return a[i + 1];
        return def;
    }

    // Step 1: import TMP Essential Resources (separate run so the import finishes).
    public static void ImportTmp()
    {
        TMP_PackageResourceImporter.ImportResources(true, false, false);
        AssetDatabase.Refresh();
    }

    // Step 2: URP 2D + scene + player settings.
    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Settings");
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Art");

        var r2d = ScriptableObject.CreateInstance<Renderer2DData>();
        AssetDatabase.CreateAsset(r2d, "Assets/Settings/Renderer2D.asset");
        var urp = UniversalRenderPipelineAsset.Create(r2d);
        AssetDatabase.CreateAsset(urp, "Assets/Settings/URP2D.asset");
        AssetDatabase.SaveAssets();

        GraphicsSettings.defaultRenderPipeline = urp;
        int cur = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(cur, false);

        // Sprite texture
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var px = new Color32[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)(i % 64 * 4), 160, 90, 255);
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes("Assets/Art/leaf.png", tex.EncodeToPNG());
        AssetDatabase.ImportAsset("Assets/Art/leaf.png");
        var ti = (TextureImporter)AssetImporter.GetAtPath("Assets/Art/leaf.png");
        ti.textureType = TextureImporterType.Sprite;
        ti.SaveAndReimport();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/leaf.png");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 5;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.95f, 0.93f, 0.88f);
        camGo.transform.position = new Vector3(0, 0, -10);
        camGo.AddComponent<UniversalAdditionalCameraData>();
        camGo.AddComponent<StartupProbe>();

        var sp = new GameObject("Leaf").AddComponent<SpriteRenderer>();
        sp.sprite = sprite;

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var txtGo = new GameObject("Title", typeof(RectTransform));
        txtGo.transform.SetParent(canvasGo.transform, false);
        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Pocket Arbor"; tmp.fontSize = 48; tmp.alignment = TextAlignmentOptions.Center;
        ((RectTransform)txtGo.transform).sizeDelta = new Vector2(600, 100);

        var es = new GameObject("EventSystem", typeof(EventSystem));
        es.AddComponent<InputSystemUIInputModule>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        // Input System only (0 = old, 1 = new, 2 = both)
        var psAsset = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
        var so = new SerializedObject(psAsset);
        var prop = so.FindProperty("activeInputHandler");
        if (prop != null) { prop.intValue = 1; so.ApplyModifiedProperties(); }
        else Debug.LogWarning("PA003: activeInputHandler property not found");

        PlayerSettings.companyName = "PocketArbor";
        PlayerSettings.productName = "PA003";
        PlayerSettings.bundleVersion = "0.0.1";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.pocketarbor.pa003");
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.pocketarbor.pa003");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.iOS.targetOSVersionString = "16.0";
        PlayerSettings.colorSpace = ColorSpace.Linear;

        AssetDatabase.SaveAssets();
        Debug.Log("PA003_SETUP_OK input=" + (prop != null ? prop.intValue : -1));
    }

    static void ApplyCommon(NamedBuildTarget nbt)
    {
        PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
        var strip = (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), Arg("strip", "High"));
        PlayerSettings.SetManagedStrippingLevel(nbt, strip);
        PlayerSettings.stripEngineCode = Arg("stripEngine", "1") == "1";
        PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Arg("cfg", "Release") == "Master" ? Il2CppCompilerConfiguration.Master : Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetIl2CppCodeGeneration(nbt, Arg("codegen", "size") == "size"
            ? Il2CppCodeGeneration.OptimizeSize : Il2CppCodeGeneration.OptimizeSpeed);
        PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        EditorUserBuildSettings.development = false;
        PlayerSettings.SplashScreen.show = Arg("splash", "1") == "1";
    }

    public static void BuildAndroid()
    {
        var nbt = NamedBuildTarget.Android;
        ApplyCommon(nbt);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.buildAppBundle = true;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        PlayerSettings.Android.minifyRelease = Arg("minify", "1") == "1";

        var gfx = Arg("gfx", "both");
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            gfx == "gles" ? new[] { GraphicsDeviceType.OpenGLES3 } :
            gfx == "vulkan" ? new[] { GraphicsDeviceType.Vulkan } :
            new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

        var entry = Arg("entry", "default");
        if (entry == "activity") PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        else PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;

        var outPath = Arg("out", "Builds/android.aab");
        Build(BuildTarget.Android, BuildTargetGroup.Android, outPath);
    }

    public static void BuildIOS()
    {
        var nbt = NamedBuildTarget.iOS;
        ApplyCommon(nbt);
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        var outPath = Arg("out", "Builds/ios");
        Build(BuildTarget.iOS, BuildTargetGroup.iOS, outPath);
    }

    static void Build(BuildTarget target, BuildTargetGroup group, string outPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outPath,
            target = target,
            targetGroup = group,
            options = BuildOptions.None
        };
        var t0 = DateTime.Now;
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        var sb = new StringBuilder();
        sb.AppendLine("result=" + s.result + " totalSize=" + s.totalSize + " time=" + (DateTime.Now - t0).TotalSeconds.ToString("F0") + "s");
        sb.AppendLine("graphics=" + string.Join(",", PlayerSettings.GetGraphicsAPIs(target)));
        sb.AppendLine("strip=" + PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.FromBuildTargetGroup(group)) +
                      " stripEngine=" + PlayerSettings.stripEngineCode +
                      " codegen=" + PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.FromBuildTargetGroup(group)) + " splash=" + PlayerSettings.SplashScreen.show + " cfg=" + PlayerSettings.GetIl2CppCompilerConfiguration(NamedBuildTarget.FromBuildTargetGroup(group)));
        if (target == BuildTarget.Android) sb.AppendLine("entry=" + PlayerSettings.Android.applicationEntry + " minify=" + PlayerSettings.Android.minifyRelease);
        sb.AppendLine("--- files (top 25 by size)");
        foreach (var f in report.GetFiles().OrderByDescending(f => f.size).Take(25))
            sb.AppendLine(f.size + "\t" + f.role + "\t" + f.path);
        sb.AppendLine("--- packed assets (top 25 by packedSize)");
        var contents = report.packedAssets.SelectMany(p => p.contents)
            .GroupBy(c => c.sourceAssetPath)
            .Select(g => new { path = g.Key, size = g.Sum(c => (long)c.packedSize), type = g.First().type })
            .OrderByDescending(x => x.size).Take(25);
        foreach (var c in contents) sb.AppendLine(c.size + "\t" + c.type + "\t" + c.path);
        sb.AppendLine("--- stripping: included modules");
        try
        {
            var si = report.strippingInfo;
            if (si != null) foreach (var m in si.includedModules) sb.AppendLine("module " + m);
        }
        catch (Exception e) { sb.AppendLine("stripping info error: " + e.Message); }
        var rep = Arg("report", "Builds/report.txt");
        File.WriteAllText(rep, sb.ToString());
        Debug.Log("PA003_BUILD " + sb);
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
