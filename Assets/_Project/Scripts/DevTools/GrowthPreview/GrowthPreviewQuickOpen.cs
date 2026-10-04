#if UNITY_EDITOR
using PocketArbor.Domain.Tree;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// One-click entry points for the owner: open the GrowthPreview scene with a preset and enter Play mode,
    /// optionally playing a day 0 → N timelapse.
    /// </summary>
    [InitializeOnLoad]
    static class GrowthPreviewQuickOpen
    {
        const string ScenePath = "Assets/_Project/Scenes/Dev/GrowthPreview.unity";
        const string TimelapseEndKey = "PocketArbor.GrowthPreview.TimelapseEnd";
        const float SecondsPerDay = 0.12f;
        const int TimelapseEndDay = 100;

        static double _nextStepTime;

        static GrowthPreviewQuickOpen()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Pocket Arbor/Show Bonsai/Momiji — Styled, day 100", priority = 0)]
        static void ShowMomiji() => Show(SpeciesId.Momiji, styled: true, day: 100, overlay: false);

        [MenuItem("Pocket Arbor/Show Bonsai/Momiji — Styled vs reference overlay", priority = 1)]
        static void ShowMomijiOverlay() => Show(SpeciesId.Momiji, styled: true, day: 100, overlay: true);

        [MenuItem("Pocket Arbor/Show Bonsai/Momiji — Wild, day 80", priority = 2)]
        static void ShowMomijiWild() => Show(SpeciesId.Momiji, styled: false, day: 80, overlay: false);

        [MenuItem("Pocket Arbor/Show Bonsai/Kuromatsu — Styled, day 100", priority = 20)]
        static void ShowKuromatsu() => Show(SpeciesId.Kuromatsu, styled: true, day: 100, overlay: false);

        [MenuItem("Pocket Arbor/Show Bonsai/Yamazakura — Styled, day 100", priority = 21)]
        static void ShowYamazakura() => Show(SpeciesId.Yamazakura, styled: true, day: 100, overlay: false);

        [MenuItem("Pocket Arbor/Timelapse/Momiji — Styled, day 0 → 100", priority = 40)]
        static void TimelapseMomijiStyled() => Show(SpeciesId.Momiji, styled: true, day: 0, overlay: false, timelapse: true);

        [MenuItem("Pocket Arbor/Timelapse/Momiji — Wild, day 0 → 100", priority = 41)]
        static void TimelapseMomijiWild() => Show(SpeciesId.Momiji, styled: false, day: 0, overlay: false, timelapse: true);

        [MenuItem("Pocket Arbor/Timelapse/Kuromatsu — Styled, day 0 → 100", priority = 42)]
        static void TimelapseKuromatsu() => Show(SpeciesId.Kuromatsu, styled: true, day: 0, overlay: false, timelapse: true);

        [MenuItem("Pocket Arbor/Timelapse/Yamazakura — Styled, day 0 → 100", priority = 43)]
        static void TimelapseYamazakura() => Show(SpeciesId.Yamazakura, styled: true, day: 0, overlay: false, timelapse: true);

        static void Show(SpeciesId species, bool styled, int day, bool overlay, bool timelapse = false)
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var controller = Object.FindFirstObjectByType<GrowthPreviewController>();
            if (controller == null)
            {
                Debug.LogError($"GrowthPreviewController not found in {ScenePath}.");
                return;
            }

            var so = new SerializedObject(controller);
            so.FindProperty("_species").intValue = (int)species;
            so.FindProperty("_styled").boolValue = styled;
            so.FindProperty("_textured").boolValue = true;
            so.FindProperty("_day").intValue = day;
            so.FindProperty("_referenceOverlay").boolValue = overlay;
            so.FindProperty("_referenceAlpha").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // SessionState survives the domain reload that happens when entering Play mode.
            SessionState.SetInt(TimelapseEndKey, timelapse ? TimelapseEndDay : -1);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            EditorApplication.update -= StepTimelapse;
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetInt(TimelapseEndKey, -1) > 0)
            {
                _nextStepTime = EditorApplication.timeSinceStartup + 0.5;
                EditorApplication.update += StepTimelapse;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                SessionState.SetInt(TimelapseEndKey, -1);
            }
        }

        static void StepTimelapse()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextStepTime)
            {
                return;
            }

            _nextStepTime = EditorApplication.timeSinceStartup + SecondsPerDay;

            var controller = Object.FindFirstObjectByType<GrowthPreviewController>();
            int end = SessionState.GetInt(TimelapseEndKey, -1);
            if (controller == null || end < 0 || controller.Day >= end)
            {
                EditorApplication.update -= StepTimelapse;
                SessionState.SetInt(TimelapseEndKey, -1);
                return;
            }

            controller.SetDay(controller.Day + 1);
        }
    }
}
#endif
