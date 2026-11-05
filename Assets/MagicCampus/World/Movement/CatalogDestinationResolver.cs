using System;
using MagicCampus.Content.Catalog;
using MagicCampus.Contracts;

namespace MagicCampus.World.Movement
{
    /// <summary>
    /// Resolves destination ids from catalog definitions. Destination rows carry mapId, x, y and radius (radius becomes the tolerance);
    /// the word home resolves the actor NPC row field home, which is a destination id or an inline destination object.
    /// The catalog is read on every call, so replaced rows take effect without code changes.
    /// </summary>
    public sealed class CatalogDestinationResolver : IDestinationResolver
    {
        public const string HomeKeyword = "home";
        public const string DestinationKind = "destination";
        public const string NpcKind = "npc";

        private readonly ICatalogProvider catalogs;
        private readonly double defaultTolerance;

        /// <param name="defaultTolerance">Tolerance in tiles used when a row has no radius.</param>
        public CatalogDestinationResolver(ICatalogProvider catalogs, double defaultTolerance = 0.5)
        {
            this.catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
            if (double.IsNaN(defaultTolerance) || double.IsInfinity(defaultTolerance) || defaultTolerance < 0) throw new ArgumentOutOfRangeException(nameof(defaultTolerance));
            this.defaultTolerance = defaultTolerance;
        }

        public Outcome<Destination> Resolve(string actorId, string destinationId)
        {
            if (string.IsNullOrEmpty(actorId)) return Fail(ResultCodes.ContentInvalid, "ACTOR_ID_REQUIRED", "an actor id is required");
            if (string.IsNullOrEmpty(destinationId)) return Fail(ResultCodes.RefNotFound, "DESTINATION_ID_REQUIRED", "a destination id is required");
            var catalog = catalogs.Current;
            if (catalog == null) return Fail(ResultCodes.ContentInvalid, "CATALOG_UNAVAILABLE", "no catalog is loaded");

            if (string.Equals(destinationId, HomeKeyword, StringComparison.Ordinal)) return ResolveHome(catalog, actorId);

            var row = catalog.FindDefinition(DestinationKind, destinationId);
            if (row == null) return Fail(ResultCodes.RefNotFound, "DESTINATION_UNKNOWN", "destination " + destinationId + " is not defined");
            return FromRow(row, destinationId);
        }

        private Outcome<Destination> ResolveHome(ICatalog catalog, string actorId)
        {
            var npc = catalog.FindDefinition(NpcKind, actorId);
            if (npc == null || npc.Kind != DataKind.Object) return Fail(ResultCodes.RefNotFound, "NPC_UNKNOWN", "actor " + actorId + " has no npc definition");
            DataNode home;
            if (!npc.TryGet("home", out home) || home.Kind == DataKind.Null) return Fail(ResultCodes.RefNotFound, "HOME_UNDEFINED", "npc " + actorId + " has no home");
            if (home.Kind == DataKind.String)
            {
                var row = catalog.FindDefinition(DestinationKind, home.Text);
                if (row == null) return Fail(ResultCodes.RefNotFound, "HOME_DESTINATION_UNKNOWN", "home destination " + home.Text + " is not defined");
                return FromRow(row, HomeKeyword);
            }
            if (home.Kind == DataKind.Object) return FromRow(home, HomeKeyword);
            return Fail(ResultCodes.ContentInvalid, "HOME_INVALID", "npc home must be a destination id or object");
        }

        private Outcome<Destination> FromRow(DataNode row, string destinationId)
        {
            if (row.Kind != DataKind.Object) return Fail(ResultCodes.ContentInvalid, "DESTINATION_NOT_OBJECT", "destination row must be an object");
            var spot = row;
            DataNode probe;
            if (!row.TryGet("x", out probe))
            {
                DataNode nested;
                if (row.TryGet("position", out nested) && nested.Kind == DataKind.Object) spot = nested;
            }

            var mapNode = Lookup(spot, row, "mapId") ?? Lookup(spot, row, "map");
            if (mapNode == null || mapNode.Kind != DataKind.String) return Fail(ResultCodes.ContentInvalid, "MAP_ID_MISSING", "destination needs a string mapId");
            double x;
            double y;
            var xNode = Lookup(spot, row, "x");
            var yNode = Lookup(spot, row, "y");
            if (xNode == null || yNode == null || !TryValue(xNode, out x) || !TryValue(yNode, out y))
                return Fail(ResultCodes.ContentInvalid, "COORDINATE_INVALID", "destination needs numeric x and y");

            var tolerance = defaultTolerance;
            var radius = Lookup(row, spot, "radius") ?? Lookup(row, spot, "tolerance");
            if (radius != null && (!TryValue(radius, out tolerance) || tolerance < 0))
                return Fail(ResultCodes.ContentInvalid, "RADIUS_INVALID", "radius must be a non-negative number");

            try
            {
                return Outcome<Destination>.Ok(new Destination(destinationId, new Position(mapNode.Text, x, y), tolerance), 0);
            }
            catch (ArgumentException ex)
            {
                return Fail(ResultCodes.ContentInvalid, "DESTINATION_REJECTED", ex.Message);
            }
        }

        private static DataNode Lookup(DataNode first, DataNode second, string key)
        {
            DataNode node;
            if (first.TryGet(key, out node)) return node;
            if (second.TryGet(key, out node)) return node;
            return null;
        }

        private static bool TryValue(DataNode node, out double value)
        {
            value = 0;
            if (node.Kind == DataKind.Integer) { value = node.Integer; return true; }
            if (node.Kind == DataKind.Number) { value = node.Number; return true; }
            return false;
        }

        private static Outcome<Destination> Fail(string code, string diagnosticCode, string message)
        {
            return Outcome<Destination>.Fail(code, 0, new Diagnostic(diagnosticCode, message, null, string.Empty, string.Empty));
        }
    }
}
