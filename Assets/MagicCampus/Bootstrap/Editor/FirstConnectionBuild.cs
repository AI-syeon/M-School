using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MagicCampus.Bootstrap.Editor
{
    public static class FirstConnectionBuild
    {
        const string ScenePath = "Assets/MagicCampus/Scenes/FirstConnection.unity";
        const string GameTypeName = "MagicCampus.Bootstrap.FirstConnection.FirstConnectionGame";

        [Serializable]
        sealed class BuildResultFile
        {
            public string unityVersion;
            public string target;
            public string result;
            public int errors;
            public int warnings;
            public long totalSize;
            public string playerPath;
            public string exception;
        }

        // Invoked via: -batchmode -quit -executeMethod MagicCampus.Bootstrap.Editor.FirstConnectionBuild.Build
        public static void Build()
        {
            var root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var buildName = "Stage5";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--stage5-build-name") buildName = args[i + 1];
            if (!System.Text.RegularExpressions.Regex.IsMatch(buildName, @"^Stage5(?:-[A-Za-z0-9]+)*$"))
                throw new ArgumentException("Stage5 build name must stay inside Builds and start with Stage5");
            var outDir = Path.Combine(root, "Builds", buildName);
            var exe = Path.Combine(outDir, "MagicCampus.exe");
            var r = new BuildResultFile
            {
                unityVersion = UnityEngine.Application.unityVersion,
                target = BuildTarget.StandaloneWindows64.ToString(),
                result = "NotRun",
                playerPath = exe,
            };

            try
            {
                Directory.CreateDirectory(outDir);
                CopyDir(Path.Combine(root, "Content", "Fixtures", "FirstConnection", "generated"),
                    Path.Combine(root, "Assets", "StreamingAssets", "FirstConnection"));
                CopyDir(Path.Combine(root, "Art", "Stage5-v1"),
                    Path.Combine(root, "Assets", "StreamingAssets", "Stage5Art"));
                CreateScene(root);
                ConfigurePlayer();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = exe,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });

                var s = report.summary;
                r.result = s.result.ToString();
                r.errors = s.totalErrors;
                r.warnings = s.totalWarnings;
                r.totalSize = (long)s.totalSize;
                if (!string.IsNullOrEmpty(s.outputPath)) r.playerPath = s.outputPath;

                if (s.result != BuildResult.Succeeded || s.totalErrors > 0)
                    throw new InvalidOperationException(
                        "Stage5 build failed: result=" + s.result + ", errors=" + s.totalErrors);

                WriteReadme(outDir);
            }
            catch (Exception e)
            {
                r.exception = e.ToString();
                if (r.result == "NotRun") r.result = "Failed";
                if (r.errors == 0) r.errors = 1;
                throw;
            }
            finally
            {
                try
                {
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(Path.Combine(outDir, "build-result.json"), JsonUtility.ToJson(r, true));
                }
                catch (Exception e)
                {
                    Debug.LogError("Failed to write build-result.json: " + e);
                }
            }
        }

        static void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(src)) throw new DirectoryNotFoundException("Missing source: " + src);
            Copy(new DirectoryInfo(src), dst);
        }

        static void Copy(DirectoryInfo src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in src.GetFiles()) f.CopyTo(Path.Combine(dst, f.Name), true);
            foreach (var d in src.GetDirectories()) Copy(d, Path.Combine(dst, d.Name));
        }

        static void CreateScene(string root)
        {
            var type = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                .FirstOrDefault(t => t.FullName == GameTypeName);
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                throw new InvalidOperationException("MonoBehaviour not found: " + GameTypeName);

            Directory.CreateDirectory(Path.Combine(root, "Assets", "MagicCampus", "Scenes"));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            new GameObject("FirstConnectionGame").AddComponent(type);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Failed to save scene: " + ScenePath);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "MagicCampus";
            PlayerSettings.productName = "MagicCampus";
            PlayerSettings.defaultScreenWidth = 960;
            PlayerSettings.defaultScreenHeight = 640;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        static void WriteReadme(string outDir)
        {
            var lines = new[]
            {
                "# MagicCampus Stage5 프로토타입 빌드",
                "",
                "이 빌드는 Stage5 프로토타입이며 전체 게임이나 모든 도메인을 완성한 버전이 아닙니다. 통과(pass) 판정을 의미하지 않습니다.",
                "",
                "실행: MagicCampus.exe (960x640 창 모드, 백그라운드 실행 유지)",
                "",
                "## 조작법 (게임 쪽에서 구현)",
                "- 이동: WASD 또는 방향키",
                "- E: 초대(invitation) 상호작용",
                "- Enter: 확인",
                "- 방향키: 선택지 이동",
                "- X 또는 Backspace: 취소",
                "- Escape: 메뉴",
                "- F5: 저장",
                "- F9: 불러오기",
                "",
                "## 참고",
                "- 빌드 결과 요약: build-result.json",
                "- 네트워크 및 인증 기능은 사용하지 않습니다.",
                "",
            };
            File.WriteAllText(Path.Combine(outDir, "README.md"), string.Join(Environment.NewLine, lines));
        }
    }
}
