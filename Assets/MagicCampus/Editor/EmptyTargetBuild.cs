using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MagicCampus.Editor
{
    // Stage 1 empty-target build.
    // Usage: Unity -batchmode -quit -executeMethod MagicCampus.Editor.EmptyTargetBuild.Build
    // The method never exits the Editor; the caller passes -quit. Failure is reported by throwing.
    public static class EmptyTargetBuild
    {
        const string ScenePath = "Assets/MagicCampus/Scenes/Empty.unity";
        const string OutputDirectory = "Builds/Stage1";
        const string PlayerPath = OutputDirectory + "/MagicCampus.exe";
        const string ResultPath = OutputDirectory + "/build-result.json";

        [Serializable]
        class BuildResultFile
        {
            public string unityVersion;
            public string target;
            public string result;
            public string playerPath;
            public bool development;
            public string scriptingBackend;
            public int totalErrors;
            public int totalWarnings;
            public List<string> errors = new List<string>();
        }

        public static void Build()
        {
            var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            var outputDirectory = Path.Combine(projectRoot, OutputDirectory);
            var resultFile = Path.Combine(projectRoot, ResultPath);
            var playerFile = Path.Combine(projectRoot, PlayerPath);

            Directory.CreateDirectory(outputDirectory);
            if (File.Exists(resultFile))
            {
                File.Delete(resultFile);
            }

            var record = new BuildResultFile
            {
                unityVersion = UnityEngine.Application.unityVersion,
                target = BuildTarget.StandaloneWindows64.ToString(),
                result = "Failed",
                playerPath = PlayerPath,
                development = true,
                scriptingBackend = ScriptingImplementation.Mono2x.ToString(),
            };

            var namedTarget = NamedBuildTarget.Standalone;
            var previousBackend = PlayerSettings.GetScriptingBackend(namedTarget);
            Exception failure = null;

            try
            {
                CreateEmptyScene(projectRoot);
                PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.Mono2x);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = playerFile,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                record.result = summary.result.ToString();
                record.totalErrors = summary.totalErrors;
                record.totalWarnings = summary.totalWarnings;
                CollectErrors(report, record.errors);

                if (summary.result == BuildResult.Succeeded && !File.Exists(playerFile))
                {
                    record.errors.Add("Build reported Succeeded but player not found: " + PlayerPath);
                }
            }
            catch (Exception e)
            {
                failure = e;
                record.errors.Add(e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(namedTarget, previousBackend);
            }

            var failed = failure != null
                || record.result != BuildResult.Succeeded.ToString()
                || record.totalErrors > 0
                || record.errors.Count > 0;

            if (failed && record.errors.Count == 0)
            {
                record.errors.Add("Build result: " + record.result);
            }

            File.WriteAllText(resultFile, JsonUtility.ToJson(record, true), new UTF8Encoding(false));

            if (failed)
            {
                throw new InvalidOperationException(
                    "MagicCampus Stage1 build failed: result=" + record.result + ", errors=" + record.errors.Count,
                    failure);
            }
        }

        static void CreateEmptyScene(string projectRoot)
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, Path.GetDirectoryName(ScenePath)));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            cameraObject.AddComponent<AudioListener>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("Failed to save scene: " + ScenePath);
            }

            AssetDatabase.Refresh();
        }

        static void CollectErrors(BuildReport report, List<string> errors)
        {
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error
                        || message.type == LogType.Exception
                        || message.type == LogType.Assert)
                    {
                        errors.Add(message.content);
                    }
                }
            }
        }
    }
}
