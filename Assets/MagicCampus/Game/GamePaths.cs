using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MagicCampus.Game
{
    /// <summary>
    /// Where the game reads its read-only files (the build's StreamingAssets). Everywhere but WebGL that is Application.streamingAssetsPath.
    /// On WebGL StreamingAssets is only reachable over HTTP, so <see cref="Download"/> first copies every file listed in
    /// streaming-index.json (written by GameBuild.BuildWeb) into a folder of the in-memory file system, and the game reads that copy
    /// with plain System.IO like on every other platform. Save files stay under persistentDataPath (IndexedDB-backed on WebGL).
    /// </summary>
    public static class GamePaths
    {
        public const string IndexFile = "streaming-index.json";
        const int Parallel = 12;

        [Serializable]
        sealed class StreamingIndex
        {
            public string[] files = new string[0];
        }

        public static bool IsWeb => UnityEngine.Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>True when the files must be fetched over HTTP before the game can start.</summary>
        public static bool NeedsDownload => IsWeb;

        static string mirror;

        /// <summary>The folder the game reads StreamingAssets files from (a local mirror on WebGL; call Download first).</summary>
        public static string StreamingAssets => IsWeb
            ? mirror ?? (mirror = Path.Combine(UnityEngine.Application.temporaryCachePath, "StreamingAssets"))
            : UnityEngine.Application.streamingAssetsPath;

        /// <summary>
        /// WebGL only. Downloads every file of the index into the mirror, reporting (done, total) files. On failure calls
        /// <paramref name="failed"/> with the reason and stops; on success the mirror holds the whole tree.
        /// </summary>
        public static IEnumerator Download(Action<int, int> progress, Action<string> failed)
        {
            var baseUrl = UnityEngine.Application.streamingAssetsPath.TrimEnd('/');
            var version = "?v=" + Uri.EscapeDataString(UnityEngine.Application.buildGUID);
            StreamingIndex index = null;
            using (var request = UnityWebRequest.Get(baseUrl + "/" + IndexFile + version))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed(IndexFile + ": " + request.error);
                    yield break;
                }
                var parseError = Parse(request.downloadHandler.text, out index);
                if (parseError != null)
                {
                    failed(IndexFile + ": " + parseError);
                    yield break;
                }
            }

            var root = StreamingAssets;
            var files = index.files;
            var next = 0;
            var done = 0;
            string error = null;
            var active = new List<KeyValuePair<UnityWebRequest, string>>();
            progress(0, files.Length);
            while (error == null && (next < files.Length || active.Count > 0))
            {
                while (next < files.Length && active.Count < Parallel)
                {
                    var rel = files[next++];
                    var request = UnityWebRequest.Get(baseUrl + "/" + EscapePath(rel) + version);
                    request.SendWebRequest();
                    active.Add(new KeyValuePair<UnityWebRequest, string>(request, rel));
                }
                yield return null;
                for (var i = active.Count - 1; i >= 0 && error == null; i--)
                {
                    var request = active[i].Key;
                    if (!request.isDone) continue;
                    error = Store(request, active[i].Value, root);
                    request.Dispose();
                    active.RemoveAt(i);
                    done++;
                    progress(done, files.Length);
                }
            }
            foreach (var pending in active) pending.Key.Dispose();
            if (error != null) failed(error);
        }

        static string Parse(string json, out StreamingIndex index)
        {
            index = null;
            try
            {
                index = JsonConvert.DeserializeObject<StreamingIndex>(json);
                return index?.files == null ? "no file list" : null;
            }
            catch (Exception e) { return e.Message; }
        }

        static string Store(UnityWebRequest request, string rel, string root)
        {
            if (request.result != UnityWebRequest.Result.Success) return rel + ": " + request.error;
            try
            {
                var path = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, request.downloadHandler.data);
                return null;
            }
            catch (Exception e) { return rel + ": " + e.Message; }
        }

        static string EscapePath(string rel)
        {
            var parts = rel.Split('/');
            for (var i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        // ------------------------------------------------------------------ saves

        /// <summary>
        /// WebGL: pushes the in-memory persistentDataPath to IndexedDB so a save survives closing the tab. Call after a save was written.
        /// Does nothing elsewhere.
        /// </summary>
        public static void FlushSaves()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { MagicCampus_SyncFs(); }
            catch (Exception e) { Debug.LogWarning("save sync failed: " + e.Message); }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void MagicCampus_SyncFs();
#endif
    }
}
