using System;
using System.Collections.Generic;
using System.Globalization;
using MagicCampus.Contracts;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MagicCampus.UnityAdapters.Maps
{
    public enum MapTileKind { Floor, Wall, DoorOpen, DoorClosed }

    /// <summary>
    /// Optional hooks. TileFactory injects tile art later (a null result leaves the visual cell empty); collision tiles never need art.
    /// LoadVerifier runs on the built, still inactive root and returns a failure text, or null when the root is acceptable.
    /// </summary>
    public sealed class MapAdapterConfig
    {
        public static MapAdapterConfig Default => new MapAdapterConfig(null, null);
        public Func<MapTileKind, TileBase> TileFactory { get; }
        public Func<string, GameObject, string> LoadVerifier { get; }

        public MapAdapterConfig(Func<MapTileKind, TileBase> tileFactory, Func<string, GameObject, string> loadVerifier)
        {
            TileFactory = tileFactory;
            LoadVerifier = loadVerifier;
        }
    }

    /// <summary>
    /// IMapPort over MapLayout. Load builds a temporary inactive Grid/Tilemap root immediately and commits it on the next Tick (IRuntimeTick),
    /// which is the window in which cancellation is possible. Failure, missing map and cancellation destroy the temporary root and keep the
    /// previous valid map and root. A new Load supersedes (cancels) a still pending one. No GameObject reference leaves this class.
    /// Colliders: a TilemapCollider2D over a Collision tilemap that holds one Grid-collider tile per logically blocked cell (static blocked and closed doors).
    /// </summary>
    public sealed class MapAdapter : IMapPort, IMapGeometry, IRuntimeTick, IDisposable
    {
        public const string ActiveRootPrefix = "MagicCampus.Map.Active:";
        public const string LoadingRootPrefix = "MagicCampus.Map.Loading:";

        private sealed class Loaded
        {
            public string MapId;
            public GameObject Root;
            public Tilemap Collision;
            public TilemapCollider2D Collider;
            public Tilemap Visual;
            public long BuiltRevision;
        }

        private sealed class Pending
        {
            public IHandleController<string> Handle;
            public Loaded Map;
            public string Owner;
        }

        private readonly MapLayout layout;
        private readonly IHandleFactory handles;
        private readonly MapAdapterConfig config;
        private readonly Tile colliderTile;
        private Loaded active;
        private Pending pending;
        private long operations;
        private bool disposed;

        public MapAdapter(MapLayout layout, IHandleFactory handles, MapAdapterConfig config)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            this.handles = handles ?? throw new ArgumentNullException(nameof(handles));
            this.config = config ?? MapAdapterConfig.Default;
            colliderTile = ScriptableObject.CreateInstance<Tile>();
            colliderTile.hideFlags = HideFlags.HideAndDontSave;
            colliderTile.colliderType = Tile.ColliderType.Grid;
            layout.Changed += OnLayoutChanged;
        }

        /// <summary>Last valid map; empty before the first successful load.</summary>
        public string ActiveMapId => active == null ? string.Empty : active.MapId;
        public long NavRevision => layout.NavRevision;
        public GameObject ActiveRoot => active == null ? null : active.Root;
        public GameObject PendingRoot => pending == null || pending.Map == null ? null : pending.Map.Root;
        public Tilemap CollisionTilemap => active == null ? null : active.Collision;

        public MapGeometry Find(string mapId) => layout.Find(mapId);
        public bool TryGetPortal(string portalId, out Portal portal) => layout.TryGetPortal(portalId, out portal);
        public IReadOnlyList<Portal> PortalsOn(string mapId) => layout.PortalsOn(mapId);

        public IHandle Load(string mapId, string ownerToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MapAdapter));
            if (!ValidId(mapId)) throw new ArgumentException("invalid map id", nameof(mapId));
            if (!ValidId(ownerToken)) throw new ArgumentException("invalid owner token", nameof(ownerToken));

            var handle = handles.Create<string>("map-load-" + (++operations).ToString(CultureInfo.InvariantCulture));
            handle.Start();
            if (!layout.Contains(mapId))
            {
                handle.Fail(ResultCodes.MapLoadFailed, layout.NavRevision, new[] { Failure(mapId, "map is not defined") });
                return handle;
            }

            if (pending != null)
            {
                var old = pending;
                pending = null;
                Discard(old);
                old.Handle.Cancelled("superseded by " + ownerToken, layout.NavRevision);
            }

            var load = new Pending { Handle = handle, Owner = ownerToken };
            handle.SetCancellationHandler(reason => CancelPending(load, reason));
            try
            {
                load.Map = Build(mapId);
            }
            catch (Exception e)
            {
                Discard(load);
                handle.Fail(ResultCodes.MapLoadFailed, layout.NavRevision, new[] { Failure(mapId, e.GetType().Name + ": " + e.Message) });
                return handle;
            }
            pending = load;
            return handle;
        }

        /// <summary>Commits the pending load, if any. Terminal state is published only after the scene objects are settled.</summary>
        public void Tick(double simulationSeconds, double unpausedSeconds)
        {
            if (disposed || pending == null) return;
            var load = pending;
            pending = null;
            if (load.Handle.Status.IsTerminal())
            {
                Discard(load);
                return;
            }

            var mapId = load.Map.MapId;
            string failure;
            try
            {
                if (load.Map.BuiltRevision != layout.NavRevision) Populate(load.Map);
                failure = config.LoadVerifier == null ? null : config.LoadVerifier(mapId, load.Map.Root);
                if (failure == null)
                {
                    load.Map.Root.name = ActiveRootPrefix + mapId;
                    load.Map.Root.SetActive(true);
                    Refresh(load.Map);
                }
            }
            catch (Exception e)
            {
                failure = e.GetType().Name + ": " + e.Message;
            }

            if (failure != null)
            {
                Discard(load);
                load.Handle.Fail(ResultCodes.MapLoadFailed, layout.NavRevision, new[] { Failure(mapId, failure) });
                return;
            }

            var previous = active;
            active = load.Map;
            load.Map = null;
            if (previous != null) DestroyObject(previous.Root);
            load.Handle.Succeed(mapId, layout.NavRevision);
        }

        /// <summary>
        /// Compares the logical grid with the rendered side for every cell of the active map: Tilemap content and Physics2D overlap at the cell centre.
        /// Returns one text per disagreement; empty means they agree.
        /// </summary>
        public IReadOnlyList<string> FindColliderMismatches()
        {
            var list = new List<string>();
            var map = active;
            if (map == null) return list;
            var geometry = layout.Find(map.MapId);
            Refresh(map);
            for (var y = 0; y < geometry.Height; y++)
                for (var x = 0; x < geometry.Width; x++)
                {
                    var logical = layout.IsBlocked(map.MapId, x, y);
                    var hasTile = map.Collision.GetTile(new Vector3Int(x, y, 0)) != null;
                    if (hasTile != logical) list.Add("tilemap " + x + "," + y + " logical=" + logical + " tile=" + hasTile);
                    var hit = HasColliderAt(map, new Vector2(x + 0.5f, y + 0.5f));
                    if (hit != logical) list.Add("collider " + x + "," + y + " logical=" + logical + " physics=" + hit);
                }
            return list;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            layout.Changed -= OnLayoutChanged;
            var load = pending;
            pending = null;
            if (load != null)
            {
                Discard(load);
                load.Handle.Cancelled("adapter disposed", layout.NavRevision);
            }
            if (active != null)
            {
                DestroyObject(active.Root);
                active = null;
            }
            if (colliderTile != null) DestroyObject(colliderTile);
        }

        // ------------------------------------------------------------ internals

        private void CancelPending(Pending load, string reason)
        {
            if (ReferenceEquals(pending, load)) pending = null;
            Discard(load);
            load.Handle.Cancelled(string.IsNullOrEmpty(reason) ? "cancelled" : reason, layout.NavRevision);
        }

        private void Discard(Pending load)
        {
            if (load.Map == null) return;
            DestroyObject(load.Map.Root);
            load.Map = null;
        }

        private void OnLayoutChanged(string mapId)
        {
            if (active != null && active.MapId == mapId) Populate(active);
        }

        private Loaded Build(string mapId)
        {
            var root = new GameObject(LoadingRootPrefix + mapId);
            root.SetActive(false);
            try
            {
                root.AddComponent<Grid>();
                var collision = NewTilemap(root, "Collision");
                var collider = collision.gameObject.AddComponent<TilemapCollider2D>();
                var visual = NewTilemap(root, "Visual");
                visual.gameObject.AddComponent<TilemapRenderer>();
                var map = new Loaded { MapId = mapId, Root = root, Collision = collision, Collider = collider, Visual = visual };
                Populate(map);
                return map;
            }
            catch
            {
                DestroyObject(root);
                throw;
            }
        }

        private static Tilemap NewTilemap(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go.AddComponent<Tilemap>();
        }

        private void Populate(Loaded map)
        {
            var geometry = layout.Find(map.MapId);
            var cp = new List<Vector3Int>();
            var ct = new List<TileBase>();
            var vp = new List<Vector3Int>();
            var vt = new List<TileBase>();
            for (var y = 0; y < geometry.Height; y++)
                for (var x = 0; x < geometry.Width; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (layout.IsBlocked(map.MapId, x, y))
                    {
                        cp.Add(cell);
                        ct.Add(colliderTile);
                    }
                    if (config.TileFactory == null) continue;
                    var kind = layout.IsStaticBlocked(map.MapId, x, y) ? MapTileKind.Wall
                        : layout.IsDoorTile(map.MapId, x, y) ? (layout.IsDoorClosed(map.MapId, x, y) ? MapTileKind.DoorClosed : MapTileKind.DoorOpen)
                        : MapTileKind.Floor;
                    var tile = config.TileFactory(kind);
                    if (tile == null) continue;
                    vp.Add(cell);
                    vt.Add(tile);
                }

            map.Collision.ClearAllTiles();
            if (cp.Count > 0) map.Collision.SetTiles(cp.ToArray(), ct.ToArray());
            map.Visual.ClearAllTiles();
            if (vp.Count > 0) map.Visual.SetTiles(vp.ToArray(), vt.ToArray());
            map.BuiltRevision = layout.NavRevision;
            Refresh(map);
        }

        private static void Refresh(Loaded map)
        {
            if (map.Collider == null || !map.Collider.isActiveAndEnabled) return;
            map.Collider.ProcessTilemapChanges();
            Physics2D.SyncTransforms();
        }

        private static bool HasColliderAt(Loaded map, Vector2 point)
        {
            foreach (var c in Physics2D.OverlapPointAll(point))
                if (c != null && c.transform.IsChildOf(map.Root.transform)) return true;
            return false;
        }

        private static Diagnostic Failure(string mapId, string actual) =>
            new Diagnostic(ResultCodes.MapLoadFailed, "map load failed", new SourceLocation(string.Empty, string.Empty, mapId, "Load", 0), "map loaded", actual);

        private static bool ValidId(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            foreach (var c in v)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            return true;
        }

        internal static void DestroyObject(UnityEngine.Object o)
        {
            if (o == null) return;
            var go = o as GameObject;
            if (UnityEngine.Application.isPlaying)
            {
                if (go != null) go.SetActive(false);
                UnityEngine.Object.Destroy(o);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
