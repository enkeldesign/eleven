using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eleven.Host.Editor
{
    /// <summary>
    /// Project settings as code. Applied by the build script before every build, so a fresh
    /// clone builds identically with no settings made by hand in the Editor's interface.
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>PlayerSettings.activeInputHandler: 0 Input Manager, 1 Input System package, 2 both.</summary>
        private const int InputSystemOnly = 1;

        public static void Apply(string buildId)
        {
            PlayerSettings.companyName = "Enkel Design";
            PlayerSettings.productName = "ELEVEN";
            PlayerSettings.bundleVersion = buildId;
            PlayerSettings.runInBackground = false;

            // Gzip with a JavaScript fallback runs from any static file server, compressed
            // headers or not. Hosting and its headers are #4's concern.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            SetActiveInputHandler(InputSystemOnly);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Creates the build's only scene: empty, because the host builds itself at runtime.</summary>
        public static void CreateBootstrapScene(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException($"Could not save the bootstrap scene to {path}.");
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        }

        // Unity has no public API for this setting, so it is set on the serialised PlayerSettings.
        private static void SetActiveInputHandler(int value)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets.Length == 0) throw new InvalidOperationException("ProjectSettings.asset not found.");

            var settings = new SerializedObject(assets[0]);
            SerializedProperty handler = settings.FindProperty("activeInputHandler");
            if (handler == null) throw new InvalidOperationException("PlayerSettings has no activeInputHandler property.");

            Debug.Log($"ELEVEN setup activeInputHandler {handler.intValue} -> {value}");
            handler.intValue = value;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
