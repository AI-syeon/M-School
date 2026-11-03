using MagicCampus.Contracts;
using MagicCampus.World.Movement;

namespace MagicCampus.Tests.Movement
{
    internal static class ResolverSpecs
    {
        public static void Run()
        {
            ResolvesDestinationsAndHome();
            RowChangeMovesTheDestinationWithoutCodeChange();
        }

        private static DataNode Row(string map, DataNode x, DataNode y, DataNode radius)
        {
            return radius == null
                ? Dn.O(Dn.P("mapId", Dn.S(map)), Dn.P("x", x), Dn.P("y", y))
                : Dn.O(Dn.P("mapId", Dn.S(map)), Dn.P("x", x), Dn.P("y", y), Dn.P("radius", radius));
        }

        private static void ResolvesDestinationsAndHome()
        {
            var catalog = new TestCatalog();
            catalog.Set("destination", "gate", Row("hall", Dn.I(5), Dn.N(2.5), Dn.N(0.2)));
            catalog.Set("destination", "bare", Row("hall", Dn.I(2), Dn.I(2), null));
            catalog.Set("destination", "ben-home", Row("hall", Dn.N(3.5), Dn.N(3.5), Dn.N(0.5)));
            catalog.Set("destination", "nomap", Dn.O(Dn.P("x", Dn.I(1)), Dn.P("y", Dn.I(1))));
            catalog.Set("destination", "negative", Row("hall", Dn.I(1), Dn.I(1), Dn.N(-1)));
            catalog.Set("npc", "ben", Dn.O(Dn.P("home", Dn.S("ben-home"))));
            catalog.Set("npc", "cy", Dn.O(Dn.P("home", Row("yard", Dn.I(2), Dn.I(2), Dn.N(0.4)))));
            catalog.Set("npc", "lost", Dn.O(Dn.P("home", Dn.S("missing"))));
            var resolver = new CatalogDestinationResolver(catalog);

            var gate = resolver.Resolve("player", "gate");
            Expect.True(gate.Succeeded && gate.Value.DestinationId == "gate", "destination resolved");
            Expect.True(gate.Value.Target.Equals(Rig.Pos("hall", 5, 2.5)), "position from row");
            Expect.Near(gate.Value.Tolerance, 0.2, 1e-12, "radius becomes tolerance");
            Expect.Near(resolver.Resolve("player", "bare").Value.Tolerance, 0.5, 1e-12, "default tolerance");

            var ben = resolver.Resolve("ben", "home");
            Expect.True(ben.Succeeded && ben.Value.Target.Equals(Rig.Pos("hall", 3.5, 3.5)), "home through destination id");
            var cy = resolver.Resolve("cy", "home");
            Expect.True(cy.Succeeded && cy.Value.Target.MapId == "yard" && cy.Value.Tolerance == 0.4, "inline home");

            Expect.True(resolver.Resolve("player", "nope").Result.Code == ResultCodes.RefNotFound, "unknown destination");
            Expect.True(resolver.Resolve("zed", "home").Result.Code == ResultCodes.RefNotFound, "npc without definition");
            Expect.True(resolver.Resolve("lost", "home").Result.Code == ResultCodes.RefNotFound, "home pointing nowhere");
            Expect.True(resolver.Resolve("player", "nomap").Result.Code == ResultCodes.ContentInvalid, "row without map");
            Expect.True(resolver.Resolve("player", "negative").Result.Code == ResultCodes.ContentInvalid, "negative radius");
            Expect.True(!resolver.Resolve("player", "nope").Succeeded && resolver.Resolve("player", "nope").Value == null, "failure carries no value");
        }

        private static void RowChangeMovesTheDestinationWithoutCodeChange()
        {
            var catalog = new TestCatalog();
            catalog.Set("destination", "gate", Row("hall", Dn.I(5), Dn.N(2.5), Dn.N(0.2)));
            var resolver = new CatalogDestinationResolver(catalog);
            var rig = new Rig("hall", new[] { Rig.Actor("player", "hall", 1.5, 2.5, 0.3, 4.0) });

            var first = resolver.Resolve("player", "gate").Value;
            var h = rig.Moves.Move(new MoveRequest("player", first, "owner.test", 30, true));
            rig.RunUntil(h, 0.1, 100);
            Expect.Succeeded(h, "move to the first row");
            Expect.Near(h.Value.Actual.X, 5.0, 0.2, "arrived at the first row position");

            catalog.Set("destination", "gate", Row("hall", Dn.N(7.5), Dn.N(3.5), Dn.N(0.2)));
            var second = resolver.Resolve("player", "gate").Value;
            var g = rig.Moves.Move(new MoveRequest("player", second, "owner.test", 30, true));
            rig.RunUntil(g, 0.1, 100);
            Expect.Succeeded(g, "move to the replaced row");
            Expect.Near(g.Value.Actual.X, 7.5, 0.2, "arrived at the new row x");
            Expect.Near(g.Value.Actual.Y, 3.5, 0.2, "arrived at the new row y");
            rig.AssertClean();
        }
    }
}
