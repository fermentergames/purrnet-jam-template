using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Jam.EditorTools
{
    /// <summary>
    /// Makes the Play button always open a specific scene instead of the currently
    /// active one. Useful when testing with ParrelSync clones so both editors start
    /// in the same scene regardless of what's open.
    ///
    /// Toggle via: Tools > Jam > Auto-Play Scene
    /// Change the target scene via: Tools > Jam > Set Auto-Play Scene
    /// </summary>
    [InitializeOnLoad]
    public static class AutoPlayScene
    {
        private const string EnabledKey = "Jam.AutoPlayScene.Enabled";
        private const string SceneKey = "Jam.AutoPlayScene.Scene";
        private const string DefaultScene = "Assets/Scenes/MainGame.unity";

        static AutoPlayScene()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        private static string ScenePath
        {
            get => EditorPrefs.GetString(SceneKey, DefaultScene);
            set => EditorPrefs.SetString(SceneKey, value);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;
            if (!Enabled)
                return;

            var path = ScenePath;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[AutoPlayScene] Target scene not found: {path}. Disabling auto-play scene.");
                Enabled = false;
                return;
            }

            // Save any unsaved changes, then open the target scene before play.
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }

        [MenuItem("Tools/Jam/Auto-Play Scene")]
        private static void ToggleEnabled()
        {
            Enabled = !Enabled;
            Debug.Log($"[AutoPlayScene] Auto-play scene {(Enabled ? "ENABLED" : "disabled")} -> {ScenePath}");
        }

        [MenuItem("Tools/Jam/Auto-Play Scene", true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked("Tools/Jam/Auto-Play Scene", Enabled);
            return true;
        }

        [MenuItem("Tools/Jam/Set Auto-Play Scene...")]
        private static void SetScene()
        {
            var path = EditorUtility.OpenFilePanel("Select scene to auto-open on Play", "Assets/Scenes", "unity");
            if (string.IsNullOrEmpty(path))
                return;

            // Convert to a project-relative path.
            var relative = "Assets" + path.Substring(Application.dataPath.Length);
            ScenePath = relative;
            Debug.Log($"[AutoPlayScene] Auto-play scene set to: {relative}");
        }
    }
}