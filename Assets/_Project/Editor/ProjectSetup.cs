using System;
using System.Collections.Generic;
using System.IO;
using PocketArbor.Bootstrap;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PocketArbor.Editor
{
    /// <summary>
    /// Idempotent project configuration (PA-101, build-size settings PA-101b). Safe to re-run at any time:
    ///   Unity -batchmode -nographics -projectPath . -executeMethod PocketArbor.Editor.ProjectSetup.RunBatch -quit
    /// or via menu Pocket Arbor/Setup/Apply Project Settings.
    /// </summary>
    public static class ProjectSetup
    {
        public const string CompanyName = "Pocket Arbor";
        public const string ProductName = "Pocket Arbor";
        public const string BundleId = "app.pocketarbor.bonsai"; // Confirmed by owner 2026-10-04 (TD-009). iOS team account is separate.
        public const string IosMinVersion = "16.0";

        const string RenderingDir = "Assets/_Project/Settings/Rendering";
        const string Renderer2DPath = RenderingDir + "/Renderer2D.asset";
        const string UrpDefaultPath = RenderingDir + "/URP_Default.asset";
        const string UrpLowEndPath = RenderingDir + "/URP_LowEnd.asset";
        const string UrpGlobalSettingsPath = RenderingDir + "/UniversalRenderPipelineGlobalSettings.asset";
        const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        const string UrpPackagePath = "Packages/com.unity.render-pipelines.universal";

        [MenuItem("Pocket Arbor/Setup/Apply Project Settings")]
        public static void Apply()
        {
            ConfigureEditorSettings();
            ConfigurePlayerSettings();
            ConfigureAudio();
            var urp = ConfigureRenderPipeline();
            ConfigureUrpShaderStripping();
            ConfigureQuality(urp.defaultAsset, urp.lowEndAsset);
            ConfigureMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[PA-101] ProjectSetup.Apply completed.");
        }

        /// <summary>Batchmode entry point: exits with code 1 on any exception.</summary>
        public static void RunBatch()
        {
            try
            {
                Apply();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // ---------------------------------------------------------------- Editor / VCS

        static void ConfigureEditorSettings()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";
        }

        // ---------------------------------------------------------------- Player

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            var mobileTargets = new[] { NamedBuildTarget.iOS, NamedBuildTarget.Android };
            foreach (var target in mobileTargets)
            {
                PlayerSettings.SetApplicationIdentifier(target, BundleId);
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApiCompatibilityLevel(target, ApiCompatibilityLevel.NET_Standard);
                PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);
                // PA-101b / ARCHITECTURE §10: OptimizeSpeed costs +5.9 MB Android download (PA-003b);
                // Master (LTO) is +1.3 MB and much slower to build, so Release explicitly.
                PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
                PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
            }
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);

            // iOS: arm64-only device SDK; Metal only.
            PlayerSettings.iOS.targetOSVersionString = IosMinVersion;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });

            // Android: ARM64 only; Vulkan first, GLES3 fallback.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel37;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            // R8 for release builds (-2.6 MB, PA-003b). Debug builds stay unminified for readable stack traces.
            // Keep rules for our own JNI-facing Java/Kotlin code (ADR-0007) -> Custom Proguard File
            // Assets/Plugins/Android/proguard-user.txt when such code appears.
            PlayerSettings.Android.minifyRelease = true;
            PlayerSettings.Android.minifyDebug = false;

            // Unity 6 allows hiding the Unity splash on Personal; the app shows its own first frame instead.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            SetActiveInputHandlingToInputSystemPackage();
        }

        /// <summary>
        /// No public API exists for "Active Input Handling"; 0 = Input Manager (old), 1 = Input System Package, 2 = Both.
        /// </summary>
        static void SetActiveInputHandlingToInputSystemPackage()
        {
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (playerSettings == null || playerSettings.Length == 0)
            {
                throw new InvalidOperationException("ProjectSettings/ProjectSettings.asset not found.");
            }
            var so = new SerializedObject(playerSettings[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null)
            {
                throw new InvalidOperationException("PlayerSettings.activeInputHandler property not found.");
            }
            if (prop.intValue != 1)
            {
                prop.intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[PA-101] Active Input Handling set to Input System Package (editor restart required).");
            }
        }

        // ---------------------------------------------------------------- Audio (ARCHITECTURE §7.2)

        static void ConfigureAudio()
        {
            var audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (audioManager == null || audioManager.Length == 0) return;
            var so = new SerializedObject(audioManager[0]);
            SetInt(so, "m_RequestedDSPBufferSize", 256); // "Best latency" (m_DSPBufferSize is the runtime value)
            SetInt(so, "m_RealVoiceCount", 32);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- URP 2D

        static (UniversalRenderPipelineAsset defaultAsset, UniversalRenderPipelineAsset lowEndAsset) ConfigureRenderPipeline()
        {
            EnsureFolder(RenderingDir);

            // URP auto-creates these in the Assets root on first import; keep them with the rest (GUIDs preserved).
            MoveAssetIfExists("Assets/UniversalRenderPipelineGlobalSettings.asset", RenderingDir + "/UniversalRenderPipelineGlobalSettings.asset");
            MoveAssetIfExists("Assets/DefaultVolumeProfile.asset", RenderingDir + "/DefaultVolumeProfile.asset");

            var renderer2D = AssetDatabase.LoadAssetAtPath<Renderer2DData>(Renderer2DPath);
            if (renderer2D == null)
            {
                renderer2D = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer2D, Renderer2DPath);
                ResourceReloader.ReloadAllNullIn(renderer2D, UrpPackagePath);
                EditorUtility.SetDirty(renderer2D);
            }

            var defaultAsset = LoadOrCreateUrpAsset(UrpDefaultPath, renderer2D);
            defaultAsset.renderScale = 1.0f;
            defaultAsset.msaaSampleCount = 1;
            defaultAsset.supportsHDR = false;
            EditorUtility.SetDirty(defaultAsset);

            var lowEndAsset = LoadOrCreateUrpAsset(UrpLowEndPath, renderer2D);
            lowEndAsset.renderScale = 0.8f;
            lowEndAsset.msaaSampleCount = 1;
            lowEndAsset.supportsHDR = false;
            EditorUtility.SetDirty(lowEndAsset);

            GraphicsSettings.defaultRenderPipeline = defaultAsset;
            return (defaultAsset, lowEndAsset);
        }

        /// <summary>
        /// URP shader stripping (ARCHITECTURE §10, PA-003b: -1.8 MB Android download). Writes both the
        /// IRenderPipelineGraphicsSettings container and the legacy serialized fields of the global settings asset
        /// so the asset stays consistent whichever copy a URP version reads.
        /// </summary>
        static void ConfigureUrpShaderStripping()
        {
            var globalSettings = AssetDatabase.LoadMainAssetAtPath(UrpGlobalSettingsPath);
            if (globalSettings == null)
            {
                throw new InvalidOperationException(UrpGlobalSettingsPath + " not found.");
            }

            if (!GraphicsSettings.TryGetRenderPipelineSettings<URPShaderStrippingSetting>(out var stripping))
            {
                throw new InvalidOperationException("URPShaderStrippingSetting is not available (URP is not the active pipeline?).");
            }
            stripping.stripUnusedVariants = true;
            stripping.stripScreenCoordOverrideVariants = true;
            stripping.stripUnusedPostProcessingVariants = true;
            stripping.strip2DUnusedVariants = true;

            var so = new SerializedObject(globalSettings);
            SetBoolIfExists(so, "m_URPShaderStrippingSetting.m_StripUnusedPostProcessingVariantsAndResources", true);
            SetBoolIfExists(so, "m_URPShaderStrippingSetting.m_Strip2DUnusedVariants", true);
            SetBoolIfExists(so, "m_StripUnusedPostProcessingVariants", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(globalSettings);
        }

        static UniversalRenderPipelineAsset LoadOrCreateUrpAsset(string path, ScriptableRendererData rendererData)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset != null) return asset;
            asset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---------------------------------------------------------------- Quality (ARCHITECTURE §6.5)

        static void ConfigureQuality(RenderPipelineAsset defaultAsset, RenderPipelineAsset lowEndAsset)
        {
            var qualityObjects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qualityObjects == null || qualityObjects.Length == 0)
            {
                throw new InvalidOperationException("QualitySettings.asset not found.");
            }

            var so = new SerializedObject(qualityObjects[0]);
            var levels = so.FindProperty("m_QualitySettings");
            levels.arraySize = 2;

            ConfigureQualityLevel(levels.GetArrayElementAtIndex(0), "Default", defaultAsset, anisotropic: 1);
            ConfigureQualityLevel(levels.GetArrayElementAtIndex(1), "LowEnd", lowEndAsset, anisotropic: 0);

            SetInt(so, "m_CurrentQuality", 0);

            var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform != null)
            {
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    var second = perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                    if (second != null) second.intValue = 0;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureQualityLevel(SerializedProperty level, string name, RenderPipelineAsset pipeline, int anisotropic)
        {
            level.FindPropertyRelative("name").stringValue = name;
            SetRelativeObject(level, "customRenderPipeline", pipeline);
            SetRelativeInt(level, "vSyncCount", 0);        // frame pacing via Application.targetFrameRate
            SetRelativeInt(level, "antiAliasing", 0);      // MSAA lives in the URP asset
            SetRelativeInt(level, "anisotropicTextures", anisotropic);
            SetRelativeInt(level, "realtimeReflectionProbes", 0);
            SetRelativeInt(level, "softParticles", 0);
            SetRelativeInt(level, "skinWeights", 2);
            SetRelativeInt(level, "globalTextureMipmapLimit", 0);
            var excluded = level.FindPropertyRelative("excludedTargetPlatforms");
            if (excluded != null) excluded.arraySize = 0;
        }

        // ---------------------------------------------------------------- Main scene

        static void ConfigureMainScene()
        {
            EnsureFolder(Path.GetDirectoryName(MainScenePath)?.Replace('\\', '/'));

            Scene scene = File.Exists(MainScenePath)
                ? EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = FindOrCreateRoot(scene, "Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            var camera = GetOrAdd<Camera>(cameraGo);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.137f, 0.153f, 0.141f, 1f); // #232724, neutral placeholder
            GetOrAdd<UniversalAdditionalCameraData>(cameraGo);
            GetOrAdd<AudioListener>(cameraGo);

            var lightGo = FindOrCreateRoot(scene, "Global Light 2D");
            var light = GetOrAdd<Light2D>(lightGo);
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            light.color = Color.white;

            var scopeGo = FindOrCreateRoot(scene, "RootLifetimeScope");
            GetOrAdd<RootLifetimeScope>(scopeGo);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, MainScenePath))
            {
                throw new InvalidOperationException("Failed to save " + MainScenePath);
            }

            var buildScenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MainScenePath, true) };
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path != MainScenePath) buildScenes.Add(existing);
            }
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }

        // ---------------------------------------------------------------- helpers

        static GameObject FindOrCreateRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
            }
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void MoveAssetIfExists(string from, string to)
        {
            if (AssetDatabase.LoadMainAssetAtPath(from) == null || AssetDatabase.LoadMainAssetAtPath(to) != null) return;
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("MoveAsset failed: " + error);
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value;
            else Debug.LogWarning("[PA-101] Property not found: " + name);
        }

        /// <summary>For legacy/optional fields: absent in a future URP version is not an error.</summary>
        static void SetBoolIfExists(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value;
        }

        static void SetRelativeInt(SerializedProperty parent, string name, int value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.intValue = value;
            else Debug.LogWarning("[PA-101] Quality property not found: " + name);
        }

        static void SetRelativeObject(SerializedProperty parent, string name, UnityEngine.Object value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning("[PA-101] Quality property not found: " + name);
        }
    }
}
