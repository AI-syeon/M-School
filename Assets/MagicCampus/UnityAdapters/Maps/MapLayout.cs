using System;
using System.Collections.Generic;
using MagicCampus.Contracts;
using UnityEngine;

namespace MagicCampus.UnityAdapters.Maps
{
    /// <summary>Tile (x, y) covers [x, x+1) x [y, y+1) in tile units; world units equal tiles (Grid cell size 1, map root at the origin).</summary>
    public static class MapCoordinates
    {
        public static int Cell(double v) => (int)Math.Max(-1e9, Math.Min(1e9, Math.Floor(v)));
        public static Vector3 ToWorld(Position p) => new Vector3((float)p.X, (float)p.Y, 0f);
        public static Vector3Int ToCell(Position p) => new Vector3Int(Cell(p.X), Cell(p.Y), 0);
        public static Vector3 CellCenter(int x, int y) => new Vector3(x + 0.5f, y + 0.5f, 0f);
    }

    public readonly struct TileCoord
    {
        public int X { get; }
        public int Y { get; }
        public TileCoord(int x, int y) { X = x; Y = y; }
    }

    public sealed class PortalDefinition
    {
        public string PortalId { get; }
        public int TileX { get; }
        public int TileY { get; }
        public double X { get; }
        public double Y { get; }
        public string ToMapId { get; }
        public double ToX { get; }
        public double ToY { get; }
        public bool Open { get; }

        internal PortalDefinition(string portalId, int tileX, int tileY, double x, double y, string toMapId, double toX, double toY, bool open)
        {
            PortalId = portalId; TileX = tileX; TileY = tileY; X = x; Y = y; ToMapId = toMapId; ToX = toX; ToY = toY; Open = open;
        }
    }

    /// <summary>Immutable map definition in the original runner shape {id,width,height,blocked:[[x,y]],portals:[{id,x,y,to:{mapId,x,y},open}]}.</summary>
    public sealed class MapDefinition
    {
        public string Id { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<TileCoord> Blocked { get; }
        public IReadOnlyList<PortalDefinition> Portals { get; }

        internal MapDefinition(string id, int width, int height, IEnumerable<TileCoord> blocked, IEnumerable<PortalDefinition> portals)
        {
            Id = id; Width = width; Height = height;
            Blocked = Array.AsReadOnly(new List<TileCoord>(blocked).ToArray());
            Portals = Array.AsReadOnly(new List<PortalDefinition>(portals).ToArray());
        }
    }

    /// <summary>
    /// Logical source of truth for every known map (active or not): static blocked tiles plus portal doors. A closed portal blocks the tile it sits on;
    /// an open portal is passable. Outside the map is always blocked. SetBlocked / SetPortalOpen are dynamic hooks (no event logic); each real change
    /// bumps NavRevision and raises Changed so rendered colliders can follow. Main thread only.
    /// </summary>
    public sealed class MapLayout
    {
        public const int MaxSize = 4096;

        private sealed class PortalState
        {
            public readonly PortalDefinition Def;
            public readonly string MapId;
            public bool Open;
            public PortalState(string mapId, PortalDefinition def) { MapId = mapId; Def = def; Open = def.Open; }
        }

        private sealed class MapState
        {
            public readonly MapDefinition Def;
            public readonly MapGeometry Geometry;
            public readonly bool[] Blocked;
            public readonly PortalState[] Door;
            public readonly List<PortalState> Portals = new List<PortalState>();

            public MapState(MapDefinition def)
            {
                Def = def;
                Geometry = new MapGeometry(def.Id, def.Width, def.Height);
                Blocked = new bool[def.Width * def.Height];
                Door = new PortalState[def.Width * def.Height];
                foreach (var t in def.Blocked) Blocked[t.Y * def.Width + t.X] = true;
                foreach (var p in def.Portals)
                {
                    var state = new PortalState(def.Id, p);
                    Portals.Add(state);
                    Door[p.TileY * def.Width + p.TileX] = state;
                }
            }
        }

        private readonly Dictionary<string, MapState> maps = new Dictionary<string, MapState>(StringComparer.Ordinal);
        private readonly Dictionary<string, PortalState> portals = new Dictionary<string, PortalState>(StringComparer.Ordinal);
        private readonly List<string> ids = new List<string>();

        public long NavRevision { get; private set; }
        public event Action<string> Changed;

        private MapLayout(IEnumerable<MapDefinition> defs)
        {
            foreach (var d in defs)
            {
                var state = new MapState(d);
                maps.Add(d.Id, state);
                ids.Add(d.Id);
                foreach (var p in state.Portals) portals.Add(p.Def.PortalId, p);
            }
        }

        public IReadOnlyList<string> MapIds => ids.AsReadOnly();
        public bool Contains(string mapId) => mapId != null && maps.ContainsKey(mapId);

        /// <summary>Null when the map is unknown.</summary>
        public MapGeometry Find(string mapId) => mapId != null && maps.TryGetValue(mapId, out var m) ? m.Geometry : null;

        public MapDefinition Definition(string mapId) => mapId != null && maps.TryGetValue(mapId, out var m) ? m.Def : null;

        public bool IsStaticBlocked(string mapId, int x, int y)
        {
            if (mapId == null || !maps.TryGetValue(mapId, out var m) || !Inside(m, x, y)) return false;
            return m.Blocked[y * m.Geometry.Width + x];
        }

        public bool IsDoorTile(string mapId, int x, int y)
        {
            if (mapId == null || !maps.TryGetValue(mapId, out var m) || !Inside(m, x, y)) return false;
            return m.Door[y * m.Geometry.Width + x] != null;
        }

        public bool IsDoorClosed(string mapId, int x, int y)
        {
            if (mapId == null || !maps.TryGetValue(mapId, out var m) || !Inside(m, x, y)) return false;
            var d = m.Door[y * m.Geometry.Width + x];
            return d != null && !d.Open;
        }

        /// <summary>True for static blocked tiles, closed doors, cells outside the map and unknown maps.</summary>
        public bool IsBlocked(string mapId, int x, int y)
        {
            if (mapId == null || !maps.TryGetValue(mapId, out var m) || !Inside(m, x, y)) return true;
            var i = y * m.Geometry.Width + x;
            if (m.Blocked[i]) return true;
            var d = m.Door[i];
            return d != null && !d.Open;
        }

        /// <summary>Null when free; otherwise tile:x,y / portal:id / bounds / map:id.</summary>
        public string BlockerAt(string mapId, int x, int y)
        {
            if (mapId == null || !maps.TryGetValue(mapId, out var m)) return "map:" + mapId;
            if (!Inside(m, x, y)) return "bounds";
            var i = y * m.Geometry.Width + x;
            if (m.Blocked[i]) return "tile:" + x + "," + y;
            var d = m.Door[i];
            return d != null && !d.Open ? "portal:" + d.Def.PortalId : null;
        }

        public Result SetBlocked(string mapId, int x, int y, bool blocked)
        {
            if (mapId == null || !maps.TryGetValue(mapId, out var m)) return Refuse(ResultCodes.RefNotFound, "unknown map", mapId, "known map");
            if (!Inside(m, x, y)) return Refuse(ResultCodes.OutOfScope, "tile outside map", mapId, "tile inside " + m.Geometry.Width + "x" + m.Geometry.Height);
            var i = y * m.Geometry.Width + x;
            if (m.Blocked[i] != blocked)
            {
                m.Blocked[i] = blocked;
                NavRevision++;
                Changed?.Invoke(mapId);
            }
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, NavRevision, null);
        }

        public Result SetPortalOpen(string portalId, bool open)
        {
            if (portalId == null || !portals.TryGetValue(portalId, out var p)) return Refuse(ResultCodes.RefNotFound, "unknown portal", portalId, "known portal");
            if (p.Open != open)
            {
                p.Open = open;
                NavRevision++;
                Changed?.Invoke(p.MapId);
            }
            return new Result(OperationStatus.Succeeded, ResultCodes.Ok, NavRevision, null);
        }

        public bool TryGetPortal(string portalId, out Portal portal)
        {
            portal = null;
            if (portalId == null || !portals.TryGetValue(portalId, out var p)) return false;
            portal = ToPortal(p);
            return true;
        }

        public IReadOnlyList<Portal> PortalsOn(string mapId)
        {
            var list = new List<Portal>();
            if (mapId != null && maps.TryGetValue(mapId, out var m))
                foreach (var p in m.Portals) list.Add(ToPortal(p));
            return list;
        }

        private static Portal ToPortal(PortalState p) =>
            new Portal(p.Def.PortalId, new Position(p.MapId, p.Def.X, p.Def.Y), new Position(p.Def.ToMapId, p.Def.ToX, p.Def.ToY), p.Open);

        private static bool Inside(MapState m, int x, int y) => x >= 0 && y >= 0 && x < m.Geometry.Width && y < m.Geometry.Height;

        private Result Refuse(string code, string message, string entity, string expected) =>
            new Result(OperationStatus.Failed, code, NavRevision, new[] { Diag(code, message, string.Empty, string.Empty, entity, expected, "") });

        // ------------------------------------------------------------ parsing

        /// <summary>Parses an immutable DataNode list of map objects. Any error rejects the whole list with pointer-addressed diagnostics.</summary>
        public static Outcome<MapLayout> Parse(DataNode maps, string file)
        {
            file = file ?? string.Empty;
            var diags = new List<Diagnostic>();
            if (maps == null || maps.Kind != DataKind.List)
            {
                diags.Add(Diag(ResultCodes.ContentInvalid, "map definitions must be a list", file, string.Empty, string.Empty, "list of map objects", maps == null ? "null" : maps.Kind.ToString()));
                return Outcome<MapLayout>.Fail(ResultCodes.ContentInvalid, 0, diags.ToArray());
            }

            var defs = new List<MapDefinition>();
            var sourceIndex = new List<int>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < maps.Items.Count; i++)
            {
                var def = ParseMap(maps.Items[i], file, "/" + i, diags);
                if (def == null) continue;
                if (!seen.Add(def.Id))
                {
                    diags.Add(Diag(ResultCodes.ContentInvalid, "duplicate map id", file, "/" + i + "/id", def.Id, "unique id", def.Id));
                    continue;
                }
                defs.Add(def);
                sourceIndex.Add(i);
            }

            var byId = new Dictionary<string, MapDefinition>(StringComparer.Ordinal);
            foreach (var d in defs) byId[d.Id] = d;
            var portalIds = new HashSet<string>(StringComparer.Ordinal);
            for (var m = 0; m < defs.Count; m++)
            {
                var def = defs[m];
                for (var k = 0; k < def.Portals.Count; k++)
                {
                    var p = def.Portals[k];
                    var ptr = "/" + sourceIndex[m] + "/portals/" + k;
                    if (!portalIds.Add(p.PortalId))
                        diags.Add(Diag(ResultCodes.ContentInvalid, "duplicate portal id", file, ptr + "/id", def.Id, "unique id", p.PortalId));
                    if (!byId.TryGetValue(p.ToMapId, out var target))
                    {
                        diags.Add(Diag(ResultCodes.RefNotFound, "portal arrival map is not defined", file, ptr + "/to/mapId", def.Id, "defined map id", p.ToMapId));
                        continue;
                    }
                    var ax = MapCoordinates.Cell(p.ToX);
                    var ay = MapCoordinates.Cell(p.ToY);
                    if (ax < 0 || ay < 0 || ax >= target.Width || ay >= target.Height)
                        diags.Add(Diag(ResultCodes.ContentInvalid, "portal arrival outside target map", file, ptr + "/to", def.Id, "inside " + target.Width + "x" + target.Height, p.ToX + "," + p.ToY));
                }
            }

            if (diags.Count > 0) return Outcome<MapLayout>.Fail(ResultCodes.ContentInvalid, 0, diags.ToArray());
            return Outcome<MapLayout>.Ok(new MapLayout(defs), 0);
        }

        private static MapDefinition ParseMap(DataNode n, string file, string ptr, List<Diagnostic> diags)
        {
            if (n == null || n.Kind != DataKind.Object)
            {
                diags.Add(Diag(ResultCodes.ContentInvalid, "map must be an object", file, ptr, string.Empty, "object", n == null ? "null" : n.Kind.ToString()));
                return null;
            }

            var start = diags.Count;
            var id = ReadId(n, "id", file, ptr, string.Empty, diags);
            var entity = id ?? string.Empty;
            var width = ReadSize(n, "width", file, ptr, entity, diags);
            var height = ReadSize(n, "height", file, ptr, entity, diags);

            var blocked = new List<TileCoord>();
            var seenBlocked = new HashSet<long>();
            if (n.TryGet("blocked", out var list))
            {
                if (list.Kind != DataKind.List)
                    diags.Add(Diag(ResultCodes.ContentInvalid, "blocked must be a list", file, ptr + "/blocked", entity, "list of [x,y]", list.Kind.ToString()));
                else
                    for (var i = 0; i < list.Items.Count; i++)
                    {
                        var p = ptr + "/blocked/" + i;
                        var e = list.Items[i];
                        int x, y;
                        if (e.Kind != DataKind.List || e.Items.Count != 2 || !ReadInt(e.Items[0], out x) || !ReadInt(e.Items[1], out y))
                        {
                            diags.Add(Diag(ResultCodes.ContentInvalid, "blocked entry must be [x,y] integers", file, p, entity, "[int,int]", e.Kind.ToString()));
                            continue;
                        }
                        if (width > 0 && height > 0 && (x < 0 || y < 0 || x >= width || y >= height))
                        {
                            diags.Add(Diag(ResultCodes.ContentInvalid, "blocked tile outside map", file, p, entity, "inside " + width + "x" + height, x + "," + y));
                            continue;
                        }
                        if (seenBlocked.Add(((long)y << 32) | (uint)x)) blocked.Add(new TileCoord(x, y));
                    }
            }

            var defs = new List<PortalDefinition>();
            var doorTiles = new HashSet<long>();
            if (n.TryGet("portals", out var plist))
            {
                if (plist.Kind != DataKind.List)
                    diags.Add(Diag(ResultCodes.ContentInvalid, "portals must be a list", file, ptr + "/portals", entity, "list of portal objects", plist.Kind.ToString()));
                else
                    for (var k = 0; k < plist.Items.Count; k++)
                    {
                        var pd = ParsePortal(plist.Items[k], file, ptr + "/portals/" + k, entity, width, height, diags);
                        if (pd == null) continue;
                        if (!doorTiles.Add(((long)pd.TileY << 32) | (uint)pd.TileX))
                        {
                            diags.Add(Diag(ResultCodes.ContentInvalid, "two portals share one tile", file, ptr + "/portals/" + k, entity, "one portal per tile", pd.TileX + "," + pd.TileY));
                            continue;
                        }
                        defs.Add(pd);
                    }
            }

            if (diags.Count > start || id == null || width <= 0 || height <= 0) return null;
            return new MapDefinition(id, width, height, blocked, defs);
        }

        private static PortalDefinition ParsePortal(DataNode n, string file, string ptr, string entity, int width, int height, List<Diagnostic> diags)
        {
            if (n == null || n.Kind != DataKind.Object)
            {
                diags.Add(Diag(ResultCodes.ContentInvalid, "portal must be an object", file, ptr, entity, "object", n == null ? "null" : n.Kind.ToString()));
                return null;
            }

            var start = diags.Count;
            var id = ReadId(n, "id", file, ptr, entity, diags);
            double x = 0, y = 0, tx = 0, ty = 0;
            string toMap = null;
            if (!n.TryGet("x", out var xn) || !ReadNum(xn, out x))
                diags.Add(Diag(ResultCodes.ContentInvalid, "portal x must be a finite number", file, ptr + "/x", entity, "number", ""));
            if (!n.TryGet("y", out var yn) || !ReadNum(yn, out y))
                diags.Add(Diag(ResultCodes.ContentInvalid, "portal y must be a finite number", file, ptr + "/y", entity, "number", ""));
            if (!n.TryGet("to", out var to) || to.Kind != DataKind.Object)
                diags.Add(Diag(ResultCodes.ContentInvalid, "portal to must be an object", file, ptr + "/to", entity, "object {mapId,x,y}", ""));
            else
            {
                toMap = ReadId(to, "mapId", file, ptr + "/to", entity, diags);
                if (!to.TryGet("x", out var txn) || !ReadNum(txn, out tx))
                    diags.Add(Diag(ResultCodes.ContentInvalid, "arrival x must be a finite number", file, ptr + "/to/x", entity, "number", ""));
                if (!to.TryGet("y", out var tyn) || !ReadNum(tyn, out ty))
                    diags.Add(Diag(ResultCodes.ContentInvalid, "arrival y must be a finite number", file, ptr + "/to/y", entity, "number", ""));
            }

            var open = true;
            if (n.TryGet("open", out var openNode))
            {
                if (openNode.Kind != DataKind.Bool)
                    diags.Add(Diag(ResultCodes.ContentInvalid, "open must be a bool", file, ptr + "/open", entity, "bool", openNode.Kind.ToString()));
                else open = openNode.Bool;
            }

            if (diags.Count > start) return null;
            var tileX = MapCoordinates.Cell(x);
            var tileY = MapCoordinates.Cell(y);
            if (width > 0 && height > 0 && (tileX < 0 || tileY < 0 || tileX >= width || tileY >= height))
            {
                diags.Add(Diag(ResultCodes.ContentInvalid, "portal tile outside map", file, ptr, entity, "inside " + width + "x" + height, x + "," + y));
                return null;
            }
            return new PortalDefinition(id, tileX, tileY, x, y, toMap, tx, ty, open);
        }

        private static string ReadId(DataNode n, string key, string file, string ptr, string entity, List<Diagnostic> diags)
        {
            if (n.TryGet(key, out var v) && v.Kind == DataKind.String && IsId(v.Text)) return v.Text;
            diags.Add(Diag(ResultCodes.ContentInvalid, key + " must be a non-empty id without whitespace", file, ptr + "/" + key, entity, "id string", v == null ? "missing" : v.Kind.ToString()));
            return null;
        }

        private static int ReadSize(DataNode n, string key, string file, string ptr, string entity, List<Diagnostic> diags)
        {
            if (n.TryGet(key, out var v) && ReadInt(v, out var size) && size >= 1 && size <= MaxSize) return size;
            diags.Add(Diag(ResultCodes.ContentInvalid, key + " must be an integer in 1.." + MaxSize, file, ptr + "/" + key, entity, "integer 1.." + MaxSize, v == null ? "missing" : v.Kind.ToString()));
            return 0;
        }

        private static bool IsId(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            foreach (var c in v)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            return true;
        }

        private static bool ReadInt(DataNode n, out int v)
        {
            v = 0;
            if (n == null) return false;
            if (n.Kind == DataKind.Integer)
            {
                if (n.Integer < int.MinValue || n.Integer > int.MaxValue) return false;
                v = (int)n.Integer;
                return true;
            }
            if (n.Kind == DataKind.Number && Math.Floor(n.Number) == n.Number && Math.Abs(n.Number) <= int.MaxValue)
            {
                v = (int)n.Number;
                return true;
            }
            return false;
        }

        private static bool ReadNum(DataNode n, out double v)
        {
            v = 0;
            if (n == null) return false;
            if (n.Kind == DataKind.Integer) { v = n.Integer; return true; }
            if (n.Kind == DataKind.Number) { v = n.Number; return true; }
            return false;
        }

        private static Diagnostic Diag(string code, string message, string file, string pointer, string entity, string expected, string actual) =>
            new Diagnostic(code, message, new SourceLocation(file, pointer, entity, string.Empty, 0), expected, actual);
    }
}
