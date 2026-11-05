using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MagicCampus.Game.Editor
{
    /// <summary>
    /// Batch build: -batchmode -quit -executeMethod MagicCampus.Game.Editor.GameBuild.Build [--stage5-build-name Stage5-x].
    /// Copies fixtures and art into StreamingAssets, regenerates the scene, builds Builds/&lt;name&gt;/MagicCampus.exe and writes build-result.json.
    /// </summary>
    public static class GameBuild
    {
        const string ScenePath = "Assets/MagicCampus/Scenes/FirstConnection.unity";

        [Serializable]
        sealed class BuildResultFile
        {
            public string unityVersion, target, result, playerPath, exception;
            public int errors, warnings;
            public long totalSize;
        }

        [MenuItem("MagicCampus/Build Stage5")]
        public static void Build()
        {
            var root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var name = "Stage5";
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--stage5-build-name") name = args[i + 1];
            if (!Regex.IsMatch(name, @"^Stage5(?:-[A-Za-z0-9]+)*$")) throw new ArgumentException("build name must be Stage5 or Stage5-<name>");
            var outDir = Path.Combine(root, "Builds", name);
            var r = new BuildResultFile
            {
                unityVersion = UnityEngine.Application.unityVersion,
                target = BuildTarget.StandaloneWindows64.ToString(),
                result = "NotRun",
                playerPath = Path.Combine(outDir, "MagicCampus.exe"),
            };
            try
            {
                Copy(new DirectoryInfo(Path.Combine(root, "Content", "Fixtures", "FirstConnection", "generated")), Path.Combine(root, "Assets", "StreamingAssets", "FirstConnection"));
                Copy(new DirectoryInfo(Path.Combine(root, "Art", "Stage5-v1")), Path.Combine(root, "Assets", "StreamingAssets", "Stage5Art"));
                CreateScene();
                PlayerSettings.companyName = "MagicCampus";
                PlayerSettings.productName = "MagicCampus";
                PlayerSettings.defaultScreenWidth = 960;
                PlayerSettings.defaultScreenHeight = 640;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.runInBackground = true;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var s = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = r.playerPath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                }).summary;
                r.result = s.result.ToString();
                r.errors = s.totalErrors;
                r.warnings = s.totalWarnings;
                r.totalSize = (long)s.totalSize;
                if (s.result != BuildResult.Succeeded || s.totalErrors > 0) throw new InvalidOperationException("build failed: result=" + s.result + ", errors=" + s.totalErrors);
                File.WriteAllLines(Path.Combine(outDir, "README.md"), new[]
                {
                    "# MagicCampus Stage5 프로토타입 빌드",
                    "",
                    "실행: MagicCampus.exe (960x640 창 모드)",
                    "",
                    "- 이동: WASD 또는 방향키",
                    "- E: 상호작용(초대), J: 일지",
                    "- Enter/Space: 대사 넘기기·선택 확인, 방향키 또는 숫자키: 선택지",
                    "- X/Backspace: 이벤트 취소·창 닫기, Esc: 메뉴(하루 마치기, 불러오기)",
                    "- F9: 마지막 저장 불러오기. 저장은 하루를 마칠 때(메뉴 또는 자정)만 한다.",
                });
            }
            catch (Exception e)
            {
                r.exception = e.ToString();
                if (r.result == "NotRun") r.result = "Failed";
                r.errors = Math.Max(r.errors, 1);
                throw;
            }
            finally
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "build-result.json"), JsonUtility.ToJson(r, true));
            }
        }

        static void Copy(DirectoryInfo src, string dst)
        {
            if (!src.Exists) throw new DirectoryNotFoundException("missing source: " + src.FullName);
            Directory.CreateDirectory(dst);
            foreach (var f in src.GetFiles()) f.CopyTo(Path.Combine(dst, f.Name), true);
            foreach (var d in src.GetDirectories()) Copy(d, Path.Combine(dst, d.Name));
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            new GameObject("Game").AddComponent<GameRoot>();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("failed to save scene " + ScenePath);
        }
    }
}
