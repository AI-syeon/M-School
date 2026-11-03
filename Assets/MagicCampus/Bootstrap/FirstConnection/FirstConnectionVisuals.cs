using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MagicCampus.Contracts;
using MagicCampus.Presentation.Cutscene;
using MagicCampus.UnityAdapters.Maps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MagicCampus.Bootstrap.FirstConnection
{
    /// <summary>
    /// Pixel world presentation for the first connection stage: tile art, actor sprites and the integer-scale camera.
    /// Reads art-manifest.json from the art root (schema 1, origin top-left, 16 pixels per unit, point filter):
    ///   tiles: floor, wall, door.open, door.closed -> image plus frames [x,y,w,h] of 16x16 (several frames are per-cell variants).
    ///   actors: player, npc.serin, npc.mira -> image, pivot [0.5,0.0625], animations idle and walk (fps, loop, rectangles) of 16x32.
    /// When art-manifest.json is absent the Art/Prototype runtime-manifest.json shape is read instead.
    /// Owns no gameplay state: logical actor roots only mirror IActorWorld; rendering is snapped on a separate child.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class FirstConnectionVisuals : MonoBehaviour, IActorVisualResolver, IDisposable
    {
        public const string ArtManifestFileName = "art-manifest.json";
        public const string PrototypeManifestFileName = "runtime-manifest.json";
        public const int PixelsPerUnit = 16;
        public const string PlayerId = "player";
        public const string SerinId = "npc.serin";
        public const string MiraId = "npc.mira";
        public const string IdleClip = "idle";
        public const string WalkClip = "walk";

        private const float WalkGraceSeconds = 0.15f;
        private const double MoveEpsilon = 0.0001;
        private const double TeleportDistance = 1.5;
        private const int SortBase = 2000;
        private const int ActorWidthPixels = 16;
        private const int ActorHeightPixels = 32;

        private static readonly string[] RequiredActors = { PlayerId, SerinId, MiraId };
        private static readonly MapTileKind[] TileKinds = { MapTileKind.Floor, MapTileKind.Wall, MapTileKind.DoorOpen, MapTileKind.DoorClosed };

        private sealed class ClipArt
        {
            public float Fps;
            public bool Loop;
            public Sprite[] Frames;
        }

        private sealed class ActorArt
        {
            public string Id;
            public readonly Dictionary<string, ClipArt> Clips = new Dictionary<string, ClipArt>(StringComparer.Ordinal);
        }

        private sealed class ArtSet
        {
            public string Root;
            public string ManifestPath;
            public bool Prototype;
            public readonly List<Texture2D> Textures = new List<Texture2D>();
            public readonly List<Sprite> Sprites = new List<Sprite>();
            public readonly Dictionary<string, Texture2D> TextureByPath = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<MapTileKind, Sprite[]> Tiles = new Dictionary<MapTileKind, Sprite[]>();
            public readonly Dictionary<string, Sprite[]> MapFloors = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
            public readonly Dictionary<string, ActorArt> Actors = new Dictionary<string, ActorArt>(StringComparer.Ordinal);
        }

        private sealed class ActorView
        {
            public string Id;
            public Transform Root;
            public Transform Visual;
            public Transform SpriteTransform;
            public SpriteRenderer Renderer;
            public bool HasLast;
            public string LastMapId;
            public double LastX;
            public double LastY;
            public float WalkUntil;
            public string Clip;
            public float ClipTime;
            public string Direction = "south";
        }

        private sealed class PixelTile : TileBase
        {
            public Sprite[] Sprites = new Sprite[0];
            public string Label = string.Empty;
            [NonSerialized] public Dictionary<string, Sprite[]> MapFloors;
            public Color Tint = Color.white;
            private bool reported;

            public void Assign(Sprite[] sprites)
            {
                Sprites = sprites ?? new Sprite[0];
                reported = false;
            }

            public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
            {
                tileData.color = Tint;
                tileData.transform = Matrix4x4.identity;
                tileData.flags = TileFlags.LockAll;
                tileData.colliderType = Tile.ColliderType.None;
                if (Sprites.Length == 0)
                {
                    if (!reported)
                    {
                        reported = true;
                        Debug.LogError("FirstConnectionVisuals: tile art for " + Label + " is not loaded; call LoadArt or Configure with a valid art directory.");
                    }
                    tileData.sprite = null;
                    return;
                }
                unchecked
                {
                    var h = (uint)(position.x * 73856093) ^ (uint)(position.y * 19349663);
                    h *= 2654435761u;
                    h ^= h >> 15;
                    var selected = Sprites;
                    if (MapFloors != null)
                    {
                        var transform = tilemap.GetComponent<Transform>();
                        var parentName = transform != null && transform.parent != null ? transform.parent.name : "";
                        var marker = parentName.IndexOf(":map.", StringComparison.Ordinal);
                        if (marker >= 0) MapFloors.TryGetValue("floor." + parentName.Substring(marker + 5), out selected);
                        if (selected == null || selected.Length == 0) selected = Sprites;
                    }
                    tileData.sprite = selected[(int)(h % (uint)selected.Length)];
                }
            }
        }

        private readonly Dictionary<MapTileKind, PixelTile> tiles = new Dictionary<MapTileKind, PixelTile>();
        private readonly Dictionary<string, ActorView> views = new Dictionary<string, ActorView>(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> reportedMissing = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> scratch = new List<string>();

        private ArtSet art;
        private IActorWorld worldPort;
        private IMapPort mapPort;
        private IMapGeometry geometry;
        private ICatalog catalogPort;
        private Camera targetCamera;
        private Camera letterboxCamera;
        private Transform actorsRoot;

        private bool hasRestore;
        private Rect restoreRect;
        private bool restoreOrthographic;
        private float restoreSize;
        private CameraClearFlags restoreClear;
        private Color restoreBackground;
        private Vector3 restorePosition;
        private Quaternion restoreRotation;

        private bool hidden;
        private bool disposed;
        private bool configured;
        private bool verifyImageHashes;
        private int marginPixels = 32;
        private int topInset;
        private int bottomInset;
        private Vector2Int cameraOffset;
        private int pixelScale;
        private Rect viewport;
        private bool layoutDirty = true;
        private string layoutMapId;
        private int layoutW;
        private int layoutH;
        private bool reportedNoFit;

        // ------------------------------------------------------------ public surface

        public bool Hidden => hidden;
        public bool Paused { get; set; }
        private float animationTime;
        public bool IsConfigured => configured;
        public bool IsArtLoaded => art != null;
        public bool UsingPrototypeArt => art != null && art.Prototype;
        public string ArtManifestPath => art == null ? string.Empty : art.ManifestPath;
        public ICatalog Catalog => catalogPort;

        /// <summary>Integer camera scale (screen pixels per world pixel); 0 until a map is laid out.</summary>
        public int PixelScale => pixelScale;

        /// <summary>Measured from the camera: screen pixels per world unit. Equals 16 * PixelScale when exact.</summary>
        public float ActualPixelsPerUnit =>
            targetCamera == null || targetCamera.orthographicSize <= 0f ? 0f : targetCamera.pixelHeight / (2f * targetCamera.orthographicSize);

        public Rect ViewportPixelRect => viewport;
        public int ActorViewCount => views.Count;
        public int MarginPixels => marginPixels;

        /// <summary>When true, a sha256 given for an image in the manifest must match the file bytes.</summary>
        public bool VerifyImageHashes
        {
            get { return verifyImageHashes; }
            set { verifyImageHashes = value; }
        }

        public void SetHidden(bool value)
        {
            hidden = value;
        }

        /// <summary>Total margin in pixels around the map inside the camera view (kept even, headroom for actor heads).</summary>
        public void SetMarginPixels(int pixels)
        {
            marginPixels = (Mathf.Max(0, pixels) + 1) & ~1;
            layoutDirty = true;
        }

        /// <summary>Screen pixels reserved for UI; ignored automatically when the map would not fit at scale 1.</summary>
        public void SetUiInsets(int top, int bottom)
        {
            topInset = Mathf.Max(0, top);
            bottomInset = Mathf.Max(0, bottom);
            layoutDirty = true;
        }

        /// <summary>Camera shift in whole world pixels from the centred map.</summary>
        public void SetCameraOffsetPixels(Vector2Int offset)
        {
            cameraOffset = offset;
            layoutDirty = true;
        }

        /// <summary>Stable tile per kind. Usable before Configure; its sprites appear once the art is loaded.</summary>
        public TileBase TileFor(MapTileKind kind)
        {
            ThrowIfDisposed();
            EnsureTiles();
            PixelTile tile;
            if (!tiles.TryGetValue(kind, out tile)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "no tile art slot for this kind");
            return tile;
        }

        /// <summary>
        /// Loads PNG tiles and actor sheets listed by art-manifest.json (or the prototype runtime-manifest.json) under root.
        /// A relative root is resolved against the Unity project folder. Throws with an explicit message on any missing or invalid asset;
        /// previously loaded art stays in use when loading fails.
        /// </summary>
        public void LoadArt(string root)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("art root is required", nameof(root));
            var next = BuildArt(root);
            ReleaseArt();
            art = next;
            EnsureTiles();
            foreach (var kind in TileKinds) tiles[kind].Assign(next.Tiles[kind]);
            tiles[MapTileKind.Floor].MapFloors = next.MapFloors;
            reportedMissing.Clear();
            RefreshTilemaps();
        }

        /// <summary>Binds the runtime ports and the camera. Loads art from assetDirectory unless LoadArt already succeeded.</summary>
        public void Configure(IActorWorld world, IMapPort maps, ICatalog catalog, string assetDirectory, Camera camera)
        {
            ThrowIfDisposed();
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (maps == null) throw new ArgumentNullException(nameof(maps));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var geo = maps as IMapGeometry;
            if (geo == null) throw new ArgumentException("the map port must also implement IMapGeometry to size the camera", nameof(maps));
            if (art == null)
            {
                if (string.IsNullOrWhiteSpace(assetDirectory)) throw new ArgumentException("art has not been loaded and no asset directory was given", nameof(assetDirectory));
                LoadArt(assetDirectory);
            }

            DestroyActorViews();
            RestoreCamera();
            worldPort = world;
            mapPort = maps;
            geometry = geo;
            catalogPort = catalog;
            BindCamera(camera);
            configured = true;
            layoutDirty = true;
            SyncActors();
            UpdateCamera(true);
        }

        public bool TryResolveVisual(string actorId, out Transform visual)
        {
            visual = null;
            if (disposed || !configured || string.IsNullOrEmpty(actorId)) return false;
            ActorView view;
            if (!views.TryGetValue(actorId, out view))
            {
                var state = worldPort.Find(actorId);
                if (state == null) return false;
                view = CreateView(state);
                UpdateView(view, state, mapPort.ActiveMapId, Time.unscaledTime, 0f);
            }
            visual = view.Visual;
            return visual != null;
        }

        /// <summary>Logical root (follows IActorWorld exactly, never rounded).</summary>
        public bool TryGetActorRoot(string actorId, out Transform root)
        {
            root = null;
            ActorView view;
            if (disposed || string.IsNullOrEmpty(actorId) || !views.TryGetValue(actorId, out view)) return false;
            root = view.Root;
            return root != null;
        }

        /// <summary>Snapped SpriteRenderer child of the visual (debug lookup).</summary>
        public bool TryGetActorRenderer(string actorId, out SpriteRenderer renderer)
        {
            renderer = null;
            ActorView view;
            if (disposed || string.IsNullOrEmpty(actorId) || !views.TryGetValue(actorId, out view)) return false;
            renderer = view.Renderer;
            return renderer != null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            configured = false;
            DestroyActorViews();
            Discard(actorsRoot == null ? null : actorsRoot.gameObject);
            actorsRoot = null;
            RestoreCamera();
            ReleaseArt();
            foreach (var tile in tiles.Values) Discard(tile);
            tiles.Clear();
            worldPort = null;
            mapPort = null;
            geometry = null;
            catalogPort = null;
            targetCamera = null;
        }

        // ------------------------------------------------------------ unity

        private void LateUpdate()
        {
            if (disposed || !configured) return;
            SyncActors();
            UpdateCamera(false);
        }

        private void OnDestroy()
        {
            Dispose();
        }

        // ------------------------------------------------------------ actors

        private void SyncActors()
        {
            var active = mapPort.ActiveMapId;
            var all = worldPort.All;
            var dt = Paused ? 0f : Time.unscaledDeltaTime;
            animationTime += dt;
            var now = animationTime;
            seen.Clear();
            for (var i = 0; i < all.Count; i++)
            {
                var state = all[i];
                seen.Add(state.ActorId);
                ActorView view;
                if (!views.TryGetValue(state.ActorId, out view)) view = CreateView(state);
                UpdateView(view, state, active, now, dt);
            }

            scratch.Clear();
            foreach (var id in views.Keys)
                if (!seen.Contains(id)) scratch.Add(id);
            foreach (var id in scratch) DestroyView(id);
        }

        private ActorView CreateView(ActorState state)
        {
            if (actorsRoot == null)
            {
                var holder = new GameObject("MagicCampus.Actors");
                actorsRoot = holder.transform;
            }
            var root = new GameObject("Actor:" + state.ActorId);
            root.transform.SetParent(actorsRoot, false);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var sprite = new GameObject("Sprite");
            sprite.transform.SetParent(visual.transform, false);
            var renderer = sprite.AddComponent<SpriteRenderer>();
            renderer.enabled = false;

            var view = new ActorView
            {
                Id = state.ActorId,
                Root = root.transform,
                Visual = visual.transform,
                SpriteTransform = sprite.transform,
                Renderer = renderer,
            };
            views[state.ActorId] = view;
            return view;
        }

        private void UpdateView(ActorView view, ActorState state, string activeMapId, float now, float dt)
        {
            var p = state.Position;
            view.Root.position = new Vector3((float)p.X, (float)p.Y, 0f);

            var moved = false;
            if (view.HasLast && string.Equals(view.LastMapId, p.MapId, StringComparison.Ordinal))
            {
                var dx = p.X - view.LastX;
                var dy = p.Y - view.LastY;
                var d2 = dx * dx + dy * dy;
                moved = d2 > MoveEpsilon * MoveEpsilon && d2 < TeleportDistance * TeleportDistance;
                if (moved) view.Direction = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? "east" : "west") : (dy > 0 ? "north" : "south");
            }
            else
            {
                view.WalkUntil = 0f;
            }
            view.HasLast = true;
            view.LastMapId = p.MapId;
            view.LastX = p.X;
            view.LastY = p.Y;
            if (moved) view.WalkUntil = now + WalkGraceSeconds;

            ActorArt actorArt;
            if (art == null || !art.Actors.TryGetValue(view.Id, out actorArt))
            {
                if (reportedMissing.Add(view.Id))
                    Debug.LogError("FirstConnectionVisuals: no art for actor '" + view.Id + "' in " + (art == null ? "(art not loaded)" : art.ManifestPath));
                view.Renderer.enabled = false;
                return;
            }

            var visible = !hidden && string.Equals(p.MapId, activeMapId, StringComparison.Ordinal);
            view.Renderer.enabled = visible;
            if (!visible) return;

            var baseClip = now < view.WalkUntil ? WalkClip : IdleClip;
            var clipName = actorArt.Clips.ContainsKey(view.Direction + "." + baseClip) ? view.Direction + "." + baseClip : baseClip;
            if (!string.Equals(clipName, view.Clip, StringComparison.Ordinal))
            {
                view.Clip = clipName;
                view.ClipTime = 0f;
            }
            else
            {
                view.ClipTime += dt;
            }

            ClipArt clip;
            if (!actorArt.Clips.TryGetValue(clipName, out clip) || clip.Frames.Length == 0) return;
            var count = clip.Frames.Length;
            if (clip.Loop)
            {
                var duration = count / clip.Fps;
                if (view.ClipTime >= duration) view.ClipTime %= duration;
            }
            var index = Mathf.Clamp((int)(view.ClipTime * clip.Fps), 0, count - 1);
            var frame = clip.Frames[index];
            if (view.Renderer.sprite != frame) view.Renderer.sprite = frame;

            view.Renderer.sortingOrder = Mathf.Clamp(SortBase - Mathf.RoundToInt((float)p.Y * PixelsPerUnit), 1, 32000);

            // Only the render child is snapped; the visual child stays free for presentation clips.
            var vp = view.Visual.position;
            view.SpriteTransform.position = new Vector3(Snap(vp.x), Snap(vp.y), vp.z);
        }

        private static float Snap(float v)
        {
            return Mathf.Round(v * PixelsPerUnit) / PixelsPerUnit;
        }

        private void DestroyView(string id)
        {
            ActorView view;
            if (!views.TryGetValue(id, out view)) return;
            views.Remove(id);
            Discard(view.Root == null ? null : view.Root.gameObject);
        }

        private void DestroyActorViews()
        {
            scratch.Clear();
            foreach (var id in views.Keys) scratch.Add(id);
            foreach (var id in scratch) DestroyView(id);
            views.Clear();
        }

        // ------------------------------------------------------------ camera

        private void BindCamera(Camera camera)
        {
            targetCamera = camera;
            hasRestore = true;
            restoreRect = camera.rect;
            restoreOrthographic = camera.orthographic;
            restoreSize = camera.orthographicSize;
            restoreClear = camera.clearFlags;
            restoreBackground = camera.backgroundColor;
            restorePosition = camera.transform.position;
            restoreRotation = camera.transform.rotation;

            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            if (letterboxCamera == null)
            {
                var go = new GameObject("MagicCampus.Letterbox");
                letterboxCamera = go.AddComponent<Camera>();
                letterboxCamera.clearFlags = CameraClearFlags.SolidColor;
                letterboxCamera.backgroundColor = Color.black;
                letterboxCamera.cullingMask = 0;
                letterboxCamera.orthographic = true;
                letterboxCamera.allowHDR = false;
                letterboxCamera.allowMSAA = false;
                letterboxCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }
            letterboxCamera.depth = camera.depth - 1f;
            letterboxCamera.targetDisplay = camera.targetDisplay;
        }

        private void RestoreCamera()
        {
            if (targetCamera != null && hasRestore)
            {
                targetCamera.rect = restoreRect;
                targetCamera.orthographic = restoreOrthographic;
                targetCamera.orthographicSize = restoreSize;
                targetCamera.clearFlags = restoreClear;
                targetCamera.backgroundColor = restoreBackground;
                targetCamera.transform.SetPositionAndRotation(restorePosition, restoreRotation);
            }
            hasRestore = false;
            if (letterboxCamera != null) Discard(letterboxCamera.gameObject);
            letterboxCamera = null;
            pixelScale = 0;
            viewport = new Rect();
        }

        private void UpdateCamera(bool force)
        {
            if (targetCamera == null || geometry == null) return;
            var mapId = mapPort.ActiveMapId;
            if (string.IsNullOrEmpty(mapId)) return;
            var sw = Screen.width;
            var sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;
            if (!force && !layoutDirty && sw == layoutW && sh == layoutH && string.Equals(mapId, layoutMapId, StringComparison.Ordinal)) return;
            var map = geometry.Find(mapId);
            if (map == null) return;

            var baseW = map.Width * PixelsPerUnit + marginPixels;
            var baseH = map.Height * PixelsPerUnit + marginPixels;
            var top = topInset;
            var bottom = bottomInset;
            var availH = sh - top - bottom;
            var scale = availH > 0 ? Math.Min(sw / baseW, availH / baseH) : 0;
            if (scale < 1 && (top > 0 || bottom > 0))
            {
                top = 0;
                bottom = 0;
                availH = sh;
                scale = Math.Min(sw / baseW, availH / baseH);
            }

            int vw;
            int vh;
            if (scale < 1)
            {
                scale = 1;
                vw = Math.Min(baseW, sw);
                vh = Math.Min(baseH, availH);
                if (!reportedNoFit)
                {
                    reportedNoFit = true;
                    Debug.LogError("FirstConnectionVisuals: map '" + mapId + "' needs " + baseW + "x" + baseH + " pixels but the screen is " + sw + "x" + sh + "; it is cropped at scale 1.");
                }
            }
            else
            {
                vw = baseW * scale;
                vh = baseH * scale;
            }

            var x0 = (sw - vw) / 2;
            var y0 = bottom + (availH - vh) / 2;
            targetCamera.pixelRect = new Rect(x0, y0, vw, vh);
            targetCamera.orthographic = true;
            targetCamera.orthographicSize = vh / (2f * PixelsPerUnit * scale);
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = Color.black;

            var cx = map.Width * PixelsPerUnit / 2 + cameraOffset.x;
            var cy = map.Height * PixelsPerUnit / 2 + cameraOffset.y;
            targetCamera.transform.SetPositionAndRotation(new Vector3(cx / (float)PixelsPerUnit, cy / (float)PixelsPerUnit, -10f), Quaternion.identity);

            pixelScale = scale;
            viewport = new Rect(x0, y0, vw, vh);
            layoutDirty = false;
            layoutW = sw;
            layoutH = sh;
            layoutMapId = mapId;
        }

        // ------------------------------------------------------------ tiles

        private void EnsureTiles()
        {
            if (tiles.Count > 0) return;
            foreach (var kind in TileKinds)
            {
                var tile = ScriptableObject.CreateInstance<PixelTile>();
                tile.hideFlags = HideFlags.HideAndDontSave;
                tile.name = "FirstConnection." + kind;
                tile.Label = kind.ToString();
                if (kind == MapTileKind.DoorClosed) tile.Tint = new Color(0.65f, 0.5f, 0.5f);
                tiles[kind] = tile;
            }
        }

        private static void RefreshTilemaps()
        {
            if (!UnityEngine.Application.isPlaying) return;
            foreach (var map in FindObjectsByType<Tilemap>())
                if (map != null) map.RefreshAllTiles();
        }

        private static string TileKey(MapTileKind kind, bool prototype)
        {
            switch (kind)
            {
                case MapTileKind.Floor: return prototype ? "floor.courtyard" : "floor";
                case MapTileKind.Wall: return prototype ? "wall.school" : "wall";
                case MapTileKind.DoorOpen: return prototype ? "door" : "door.open";
                default: return prototype ? "door" : "door.closed";
            }
        }

        // ------------------------------------------------------------ art loading

        private ArtSet BuildArt(string root)
        {
            var rootFull = ResolveRoot(root);
            if (!Directory.Exists(rootFull)) throw new DirectoryNotFoundException("Art directory not found: " + rootFull);

            var set = new ArtSet { Root = rootFull, Prototype = false };
            set.ManifestPath = Path.Combine(rootFull, ArtManifestFileName);
            if (!File.Exists(set.ManifestPath))
            {
                set.ManifestPath = Path.Combine(rootFull, PrototypeManifestFileName);
                set.Prototype = true;
                if (!File.Exists(set.ManifestPath))
                    throw new FileNotFoundException("Missing " + ArtManifestFileName + " (or fallback " + PrototypeManifestFileName + ") in " + rootFull, set.ManifestPath);
            }

            try
            {
                var name = Path.GetFileName(set.ManifestPath);
                var doc = Obj(JsonReader.Parse(File.ReadAllText(set.ManifestPath, Encoding.UTF8), name), name);
                object ppu;
                if (doc.TryGetValue("pixelsPerUnit", out ppu) && ppu != null && Int(ppu, name + ".pixelsPerUnit") != PixelsPerUnit)
                    throw Bad(name + ".pixelsPerUnit", "must be " + PixelsPerUnit);

                var tilesObj = Obj(Req(doc, "tiles", name), name + ".tiles");
                foreach (var kind in TileKinds)
                {
                    var key = TileKey(kind, set.Prototype);
                    var ctx = name + ".tiles[" + key + "]";
                    var entry = Obj(Req(tilesObj, key, ctx), ctx);
                    var tex = LoadTexture(set, entry, ctx);
                    set.Tiles[kind] = ReadSprites(set, tex, Arr(Req(entry, "frames", ctx), ctx + ".frames"), new Vector2(0.5f, 0.5f), PixelsPerUnit, PixelsPerUnit, ctx + ".frames", "tile." + kind);
                }

                var actors = Obj(Req(doc, "actors", name), name + ".actors");
                foreach (var pair in tilesObj)
                {
                    if (!pair.Key.StartsWith("floor.", StringComparison.Ordinal)) continue;
                    var ctx = name + ".tiles[" + pair.Key + "]";
                    var entry = Obj(pair.Value, ctx);
                    var tex = LoadTexture(set, entry, ctx);
                    set.MapFloors[pair.Key] = ReadSprites(set, tex, Arr(Req(entry, "frames", ctx), ctx + ".frames"), new Vector2(.5f,.5f),16,16,ctx,pair.Key);
                }
                foreach (var pair in actors)
                {
                    var ctx = name + ".actors[" + pair.Key + "]";
                    set.Actors[pair.Key] = ReadActor(set, pair.Key, Obj(pair.Value, ctx), ctx);
                }
                return set;
            }
            catch
            {
                DiscardArt(set);
                throw;
            }
        }

        private ActorArt ReadActor(ArtSet set, string id, Dictionary<string, object> entry, string ctx)
        {
            var tex = LoadTexture(set, entry, ctx);
            var pivot = new Vector2(0.5f, 0.0625f);
            object pv;
            if (entry.TryGetValue("pivot", out pv) && pv != null)
            {
                var pa = Arr(pv, ctx + ".pivot");
                if (pa.Count != 2) throw Bad(ctx + ".pivot", "expected [x,y]");
                pivot = new Vector2((float)Num(pa[0], ctx + ".pivot[0]"), (float)Num(pa[1], ctx + ".pivot[1]"));
            }

            var anims = Obj(Req(entry, "animations", ctx), ctx + ".animations");
            if (!anims.ContainsKey(IdleClip)) throw Bad(ctx + ".animations", "missing '" + IdleClip + "'");
            if (!anims.ContainsKey(WalkClip)) throw Bad(ctx + ".animations", "missing '" + WalkClip + "'");

            var result = new ActorArt { Id = id };
            foreach (var pair in anims)
            {
                var cc = ctx + ".animations[" + pair.Key + "]";
                var clip = Obj(pair.Value, cc);
                var fps = pair.Key == IdleClip ? 5f : pair.Key == WalkClip ? 9f : 8f;
                object fv;
                if (clip.TryGetValue("fps", out fv) && fv != null) fps = (float)Num(fv, cc + ".fps");
                if (fps <= 0f) throw Bad(cc + ".fps", "must be positive");
                var loop = true;
                object lv;
                if (clip.TryGetValue("loop", out lv) && lv is bool) loop = (bool)lv;
                var frames = ReadSprites(set, tex, Arr(Req(clip, "rectangles", cc), cc + ".rectangles"), pivot, ActorWidthPixels, ActorHeightPixels, cc + ".rectangles", id + "." + pair.Key);
                result.Clips[pair.Key] = new ClipArt { Fps = fps, Loop = loop, Frames = frames };
            }
            object directions;
            if (entry.TryGetValue("directionAnimations", out directions))
                foreach (var direction in Obj(directions, ctx + ".directionAnimations"))
                    foreach (var pair in Obj(direction.Value, ctx + "." + direction.Key))
                    {
                        var cc = ctx + "." + direction.Key + "." + pair.Key;
                        var clip = Obj(pair.Value, cc);
                        var frames = ReadSprites(set, tex, Arr(Req(clip,"rectangles",cc),cc),pivot,16,32,cc,id+direction.Key+pair.Key);
                        result.Clips[direction.Key + "." + pair.Key] = new ClipArt { Fps = (float)Num(Req(clip,"fps",cc),cc), Loop=true, Frames=frames };
                    }
            return result;
        }

        // Rectangles are stored top-left in the manifest; Unity sprite rects are bottom-left.
        private static Sprite[] ReadSprites(ArtSet set, Texture2D tex, List<object> rects, Vector2 pivot, int w, int h, string ctx, string name)
        {
            if (rects.Count == 0) throw Bad(ctx, "no frames listed");
            var result = new Sprite[rects.Count];
            for (var i = 0; i < rects.Count; i++)
            {
                var fc = ctx + "[" + i + "]";
                var r = Obj(rects[i], fc);
                var x = Int(Req(r, "x", fc), fc + ".x");
                var y = Int(Req(r, "y", fc), fc + ".y");
                var rw = Int(Req(r, "w", fc), fc + ".w");
                var rh = Int(Req(r, "h", fc), fc + ".h");
                if (rw != w || rh != h) throw Bad(fc, "frame must be " + w + "x" + h + " but is " + rw + "x" + rh);
                if (x < 0 || y < 0 || x + rw > tex.width || y + rh > tex.height)
                    throw Bad(fc, "rectangle lies outside the " + tex.width + "x" + tex.height + " image");
                var sprite = Sprite.Create(tex, new Rect(x, tex.height - y - rh, rw, rh), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = name + "#" + i;
                set.Sprites.Add(sprite);
                result[i] = sprite;
            }
            return result;
        }

        private Texture2D LoadTexture(ArtSet set, Dictionary<string, object> entry, string ctx)
        {
            var rel = Str(Req(entry, "image", ctx), ctx + ".image");
            var full = Path.GetFullPath(Path.Combine(set.Root, rel));
            if (!IsUnder(set.Root, full)) throw Bad(ctx + ".image", "path leaves the art root: " + rel);
            Texture2D tex;
            if (set.TextureByPath.TryGetValue(full, out tex)) return tex;
            if (!File.Exists(full)) throw new FileNotFoundException("Missing art image for " + ctx + ": " + rel + " (expected at " + full + ")", full);

            var bytes = File.ReadAllBytes(full);
            object sha;
            if (verifyImageHashes && entry.TryGetValue("sha256", out sha))
            {
                var expected = sha as string;
                if (!string.IsNullOrEmpty(expected) && !string.Equals(Hex(bytes), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Art image hash mismatch for " + ctx + ": " + full);
            }

            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.name = Path.GetFileName(full);
            tex.hideFlags = HideFlags.HideAndDontSave;
            set.Textures.Add(tex);
            if (!ImageConversion.LoadImage(tex, bytes, false)) throw new InvalidDataException("Unreadable PNG for " + ctx + ": " + full);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 0;
            set.TextureByPath[full] = tex;
            return tex;
        }

        private static string Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static string ResolveRoot(string root)
        {
            var path = Path.IsPathRooted(root) ? root : Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), root);
            return Path.GetFullPath(path);
        }

        private static bool IsUnder(string root, string full)
        {
            var sep = Path.DirectorySeparatorChar.ToString();
            var prefix = root.EndsWith(sep, StringComparison.Ordinal) ? root : root + sep;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private void ReleaseArt()
        {
            foreach (var tile in tiles.Values) tile.Assign(null);
            foreach (var view in views.Values)
                if (view.Renderer != null) view.Renderer.sprite = null;
            if (art != null) DiscardArt(art);
            art = null;
        }

        private static void DiscardArt(ArtSet set)
        {
            foreach (var sprite in set.Sprites) Discard(sprite);
            foreach (var tex in set.Textures) Discard(tex);
            set.Sprites.Clear();
            set.Textures.Clear();
            set.TextureByPath.Clear();
            set.Tiles.Clear();
            set.Actors.Clear();
        }

        private static void Discard(UnityEngine.Object o)
        {
            if (o == null) return;
            if (UnityEngine.Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FirstConnectionVisuals));
        }

        // ------------------------------------------------------------ manifest json access

        private static InvalidDataException Bad(string ctx, string message)
        {
            return new InvalidDataException("Art manifest " + ctx + ": " + message);
        }

        private static Dictionary<string, object> Obj(object v, string ctx)
        {
            var d = v as Dictionary<string, object>;
            if (d == null) throw Bad(ctx, "object expected");
            return d;
        }

        private static List<object> Arr(object v, string ctx)
        {
            var l = v as List<object>;
            if (l == null) throw Bad(ctx, "array expected");
            return l;
        }

        private static object Req(Dictionary<string, object> d, string key, string ctx)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) throw Bad(ctx + "." + key, "missing");
            return v;
        }

        private static string Str(object v, string ctx)
        {
            var s = v as string;
            if (string.IsNullOrEmpty(s)) throw Bad(ctx, "non-empty string expected");
            return s;
        }

        private static double Num(object v, string ctx)
        {
            if (!(v is double)) throw Bad(ctx, "number expected");
            return (double)v;
        }

        private static int Int(object v, string ctx)
        {
            var n = Num(v, ctx);
            var i = (int)Math.Round(n);
            if (Math.Abs(n - i) > 1e-9) throw Bad(ctx, "integer expected");
            return i;
        }

        private sealed class JsonReader
        {
            private const char Quote = (char)34;
            private const char Backslash = (char)92;
            private readonly string text;
            private readonly string name;
            private int pos;

            private JsonReader(string text, string name)
            {
                this.text = text;
                this.name = name;
            }

            public static object Parse(string text, string name)
            {
                var r = new JsonReader(text, name);
                r.SkipWs();
                var v = r.ReadValue();
                r.SkipWs();
                if (r.pos != text.Length) throw r.Error("unexpected trailing content");
                return v;
            }

            private InvalidDataException Error(string message)
            {
                return new InvalidDataException("JSON error in " + name + " at offset " + pos + ": " + message);
            }

            private char Peek()
            {
                return pos < text.Length ? text[pos] : (char)0;
            }

            private void SkipWs()
            {
                while (pos < text.Length && (char.IsWhiteSpace(text[pos]) || text[pos] == (char)0xFEFF)) pos++;
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) throw Error("expected " + word);
                pos += word.Length;
            }

            private object ReadValue()
            {
                if (pos >= text.Length) throw Error("unexpected end");
                switch (text[pos])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case Quote: return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private object ReadObject()
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                pos++;
                SkipWs();
                if (Peek() == '}')
                {
                    pos++;
                    return d;
                }
                while (true)
                {
                    SkipWs();
                    if (Peek() != Quote) throw Error("object key expected");
                    var key = ReadString();
                    SkipWs();
                    if (Peek() != ':') throw Error("':' expected");
                    pos++;
                    SkipWs();
                    d[key] = ReadValue();
                    SkipWs();
                    var c = Peek();
                    pos++;
                    if (c == ',') continue;
                    if (c == '}') return d;
                    throw Error("',' or '}' expected");
                }
            }

            private object ReadArray()
            {
                var l = new List<object>();
                pos++;
                SkipWs();
                if (Peek() == ']')
                {
                    pos++;
                    return l;
                }
                while (true)
                {
                    SkipWs();
                    l.Add(ReadValue());
                    SkipWs();
                    var c = Peek();
                    pos++;
                    if (c == ',') continue;
                    if (c == ']') return l;
                    throw Error("',' or ']' expected");
                }
            }

            private object ReadNumber()
            {
                var start = pos;
                while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0) pos++;
                double v;
                if (start == pos || !double.TryParse(text.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    throw Error("invalid value");
                return v;
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                pos++;
                while (true)
                {
                    if (pos >= text.Length) throw Error("unterminated string");
                    var c = text[pos++];
                    if (c == Quote) return sb.ToString();
                    if (c != Backslash)
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (pos >= text.Length) throw Error("unterminated escape");
                    var e = text[pos++];
                    switch (e)
                    {
                        case 'n': sb.Append((char)10); break;
                        case 't': sb.Append((char)9); break;
                        case 'r': sb.Append((char)13); break;
                        case 'b': sb.Append((char)8); break;
                        case 'f': sb.Append((char)12); break;
                        case 'u':
                            int code;
                            if (pos + 4 > text.Length || !int.TryParse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw Error("invalid unicode escape");
                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }
        }
    }
}
