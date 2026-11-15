using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MagicCampus.Game.Editor
{
    /// <summary>
    /// Batch builds. Windows: -batchmode -quit -executeMethod MagicCampus.Game.Editor.GameBuild.Build [--stage5-build-name Stage5-x].
    /// WebGL: -batchmode -quit -buildTarget WebGL -executeMethod MagicCampus.Game.Editor.GameBuild.BuildWeb [--web-build-dir Web-x].
    /// Both copy fixtures, Stage5 art and the CampusGrid runtime files (layouts, cell PNGs, packed atlases) into StreamingAssets,
    /// regenerate the scene, build (Builds/&lt;name&gt;/MagicCampus.exe or Builds/Web/index.html) and write build-result.json.
    /// </summary>
    public static class GameBuild
    {
        const string ScenePath = "Assets/MagicCampus/Scenes/FirstConnection.unity";
        const string CampusGrid = "Assets/MagicCampus/CampusGrid";

        [Serializable]
        sealed class BuildResultFile
        {
            public string unityVersion, target, result, playerPath, exception;
            public int errors, warnings;
            public long totalSize;
        }

        [Serializable]
        sealed class IndexFile
        {
            public string[] files;
        }

        [MenuItem("MagicCampus/Build Stage5")]
        public static void Build()
        {
            var root = ProjectRoot();
            var name = "Stage5";
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--stage5-build-name") name = args[i + 1];
            if (!Regex.IsMatch(name, @"^Stage5(?:-[A-Za-z0-9]+)*$")) throw new ArgumentException("build name must be Stage5 or Stage5-<name>");
            var outDir = Path.Combine(root, "Builds", name);
            Run(root, outDir, Path.Combine(outDir, "MagicCampus.exe"), BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, () =>
            {
                PlayerSettings.defaultScreenWidth = 960;
                PlayerSettings.defaultScreenHeight = 640;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            }, () => File.WriteAllLines(Path.Combine(outDir, "README.md"), new[]
            {
                "# MagicCampus Stage5 프로토타입 빌드",
                "",
                "실행: MagicCampus.exe (960x640 창 모드)",
                "",
                "- 이동: WASD 또는 방향키",
                "- E: 상호작용(초대), J: 일지",
                "- Enter/Space: 대사 넘기기·선택 확인, 방향키 또는 숫자키: 선택지",
                "- X/Backspace: 이벤트 취소·창 닫기, Esc: 메뉴(불러오기), 침대에서 E: 잠자기(하루 마치고 저장), 새벽 2시: 기절",
                "- F9: 마지막 저장 불러오기. 저장은 하루를 마칠 때(메뉴 또는 자정)만 한다.",
            }));
        }

        /// <summary>
        /// WebGL build for static hosting (GitHub Pages). Builds Builds/Web (override: --web-build-dir Web-x) and writes
        /// StreamingAssets/streaming-index.json next to it, the file list the game downloads at start-up (see GamePaths).
        /// Gzip with the JavaScript decompression fallback, because GitHub Pages sends no Content-Encoding header.
        /// </summary>
        [MenuItem("MagicCampus/Build Web")]
        public static void BuildWeb()
        {
            var root = ProjectRoot();
            var name = "Web";
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--web-build-dir") name = args[i + 1];
            if (!Regex.IsMatch(name, @"^Web(?:-[A-Za-z0-9]+)*$")) throw new ArgumentException("web build dir must be Web or Web-<name>");
            var outDir = Path.Combine(root, "Builds", name);
            Run(root, outDir, Path.Combine(outDir, "index.html"), BuildTarget.WebGL, BuildTargetGroup.WebGL, () =>
            {
                PlayerSettings.defaultWebScreenWidth = 960;
                PlayerSettings.defaultWebScreenHeight = 640;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.dataCaching = true;
                PlayerSettings.WebGL.nameFilesAsHashes = false;
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            }, () => WriteStreamingIndex(outDir));
        }

        static string ProjectRoot() => Directory.GetParent(UnityEngine.Application.dataPath).FullName;

        /// <summary>The setup both builds share: copy runtime files into StreamingAssets, regenerate the scene, apply common and per-platform settings, build, write build-result.json.</summary>
        static void Run(string root, string outDir, string playerPath, BuildTarget target, BuildTargetGroup group, Action configure, Action afterBuild)
        {
            var r = new BuildResultFile
            {
                unityVersion = UnityEngine.Application.unityVersion,
                target = target.ToString(),
                result = "NotRun",
                playerPath = playerPath,
            };
            try
            {
                CopyStreamingAssets(root);
                CreateScene();
                PlayerSettings.companyName = "MagicCampus";
                PlayerSettings.productName = "MagicCampus";
                PlayerSettings.runInBackground = true;
                configure();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var s = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    // WebGL takes the output folder, a standalone player its exe path.
                    locationPathName = target == BuildTarget.WebGL ? outDir : playerPath,
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.None,
                }).summary;
                r.result = s.result.ToString();
                r.errors = s.totalErrors;
                r.warnings = s.totalWarnings;
                r.totalSize = (long)s.totalSize;
                if (s.result != BuildResult.Succeeded || s.totalErrors > 0) throw new InvalidOperationException("build failed: result=" + s.result + ", errors=" + s.totalErrors);
                afterBuild();
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

        static void CopyStreamingAssets(string root)
        {
            Copy(new DirectoryInfo(Path.Combine(root, "Content", "Fixtures", "FirstConnection", "generated")), Path.Combine(root, "Assets", "StreamingAssets", "FirstConnection"));
            Copy(new DirectoryInfo(Path.Combine(root, "Art", "Stage5-v1")), Path.Combine(root, "Assets", "StreamingAssets", "Stage5Art"));
            CopyGridRuntime(new DirectoryInfo(Path.Combine(root, CampusGrid)), Path.Combine(root, "Assets", "StreamingAssets", "CampusGrid"));
            foreach (var kit in new[] { "UI", "Items", "Characters" })
            {
                var src = new DirectoryInfo(Path.Combine(root, "Assets", "MagicCampus", "Art", kit));
                if (src.Exists) CopyGridRuntime(src, Path.Combine(root, "Assets", "StreamingAssets", "UiArt", kit));
            }
        }

        /// <summary>Files the web player never reads (art tooling, docs, previews): left out of the download list and deleted from the output.</summary>
        static readonly string[] WebSkipped = { ".py", ".md", ".gif", ".meta" };

        /// <summary>Lists every runtime file of the built StreamingAssets (relative, forward slashes) in streaming-index.json for GamePaths.Download.</summary>
        static void WriteStreamingIndex(string outDir)
        {
            var streaming = Path.Combine(outDir, "StreamingAssets");
            if (!Directory.Exists(streaming)) throw new DirectoryNotFoundException("build has no StreamingAssets: " + streaming);
            var files = new List<string>();
            foreach (var path in Directory.GetFiles(streaming, "*", SearchOption.AllDirectories))
            {
                var rel = path.Substring(streaming.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (rel == GamePaths.IndexFile) continue;
                if (Array.IndexOf(WebSkipped, Path.GetExtension(rel).ToLowerInvariant()) >= 0) File.Delete(path);
                else files.Add(rel);
            }
            files.Sort(StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(streaming, GamePaths.IndexFile), JsonUtility.ToJson(new IndexFile { files = files.ToArray() }), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, ".nojekyll"), string.Empty);
        }

        static void Copy(DirectoryInfo src, string dst)
        {
            if (!src.Exists) throw new DirectoryNotFoundException("missing source: " + src.FullName);
            Directory.CreateDirectory(dst);
            foreach (var f in src.GetFiles()) f.CopyTo(Path.Combine(dst, f.Name), true);
            foreach (var d in src.GetDirectories()) Copy(d, Path.Combine(dst, d.Name));
        }

        /// <summary>What the game reads of CampusGrid at run time; Unity-only folders (Generated) and .meta files stay behind.</summary>
        static void CopyGridRuntime(DirectoryInfo src, string dst)
        {
            if (!src.Exists) throw new DirectoryNotFoundException("missing source: " + src.FullName);
            Directory.CreateDirectory(dst);
            foreach (var f in src.GetFiles())
                if (f.Extension == ".json" || f.Extension == ".png" || f.Extension == ".campusatlas") f.CopyTo(Path.Combine(dst, f.Name), true);
            foreach (var d in src.GetDirectories())
                if (d.Name != "Generated") CopyGridRuntime(d, Path.Combine(dst, d.Name));
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            CreateWorldArtAssets();
            new GameObject("Game").AddComponent<GameRoot>();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("failed to save scene " + ScenePath);
        }

        /// <summary>The scene object that carries the CampusGrid shaders, canopy mask and a daylight with its preview controls off.</summary>
        static void CreateWorldArtAssets()
        {
            T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("missing asset " + path);
            var assets = new GameObject("World Art").AddComponent<WorldArtAssets>();
            assets.CanopyShader = Load<Shader>(CampusGrid + "/CourtyardCanopy.shader");
            assets.ContactShadowShader = Load<Shader>(CampusGrid + "/CourtyardShadow.shader");
            assets.GradeShader = Load<Shader>(CampusGrid + "/CourtyardGrade.shader");
            assets.NormalShader = Load<Shader>(CampusGrid + "/CourtyardGround.shader");
            assets.CanopyMask = Load<Texture2D>(CampusGrid + "/CanopyShadow.png");
            var template = new GameObject("Daylight Template");
            template.SetActive(false);
            template.transform.SetParent(assets.transform, false);
            var daylight = template.AddComponent<CampusDaylight>();
            var serialized = new SerializedObject(daylight);
            serialized.FindProperty("showControls").boolValue = false;
            serialized.FindProperty("previewSecondsPerDay").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            assets.DaylightTemplate = daylight;
        }
    }
}
