using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.World.Movement
{
    /// <summary>
    /// Authoritative simulation actor positions. Readers get the latest immutable snapshot; writers need a valid owner token
    /// (optionally also accepted by the injected writer policy). A rejected write keeps the last valid position.
    /// Position validity here is geometric (known map, inside bounds); standability is judged by the mover through collision.
    /// </summary>
    public sealed class ActorWorld : IActorWorld
    {
        private sealed class View
        {
            private readonly Dictionary<string, ActorState> byId = new Dictionary<string, ActorState>(StringComparer.Ordinal);

            public View(ActorState[] ordered)
            {
                foreach (var actor in ordered) byId.Add(actor.ActorId, actor);
                All = Array.AsReadOnly(ordered);
            }

            public IReadOnlyList<ActorState> All { get; }

            public ActorState Find(string id)
            {
                ActorState actor;
                return id != null && byId.TryGetValue(id, out actor) ? actor : null;
            }
        }

        private readonly object gate = new object();
        private readonly IMapGeometry geometry;
        private readonly Func<string, bool> writerPolicy;
        private volatile View view;
        private long version;

        /// <param name="writerPolicy">Optional extra check of the owner token; null accepts any well-formed token.</param>
        /// <exception cref="ArgumentException">Duplicate actor ids, unknown maps or positions outside their map.</exception>
        public ActorWorld(IMapGeometry geometry, IEnumerable<ActorState> initial, Func<string, bool> writerPolicy = null)
        {
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            this.writerPolicy = writerPolicy;
            var problems = new List<Diagnostic>();
            var actors = Validate(initial, problems);
            if (actors == null) throw new ArgumentException(Describe(problems), nameof(initial));
            view = new View(actors);
        }

        /// <summary>Count of accepted writes and restores.</summary>
        public long Version
        {
            get { lock (gate) return version; }
        }

        public IReadOnlyList<ActorState> All => view.All;

        public ActorState Find(string id)
        {
            return view.Find(id);
        }

        public Result SetPosition(string actorId, Position position, string ownerToken)
        {
            lock (gate)
            {
                if (!ValidToken(ownerToken)) return Refuse(ResultCodes.ContentInvalid, "OWNER_TOKEN_INVALID", "a non-empty owner token without whitespace is required");
                if (writerPolicy != null && !writerPolicy(ownerToken)) return Refuse(ResultCodes.OutOfScope, "WRITER_NOT_AUTHORIZED", "owner token is not allowed to write positions");
                var current = view.Find(actorId);
                if (current == null) return Refuse(ResultCodes.RefNotFound, "ACTOR_UNKNOWN", "actor is not in the world");
                var problem = CheckPosition(position, actorId);
                if (problem != null) return Failed(ResultCodes.ContentInvalid, new[] { problem });

                var next = new List<ActorState>(view.All.Count);
                foreach (var actor in view.All)
                    next.Add(string.Equals(actor.ActorId, actorId, StringComparison.Ordinal) ? actor.WithPosition(position) : actor);
                view = new View(next.ToArray());
                version++;
                return new Result(OperationStatus.Succeeded, ResultCodes.Ok, version, null);
            }
        }

        public Result Restore(IReadOnlyList<ActorState> actors)
        {
            lock (gate)
            {
                var problems = new List<Diagnostic>();
                var validated = Validate(actors, problems);
                if (validated == null) return Failed(ResultCodes.ContentInvalid, problems);
                view = new View(validated);
                version++;
                return new Result(OperationStatus.Succeeded, ResultCodes.Ok, version, null);
            }
        }

        private ActorState[] Validate(IEnumerable<ActorState> source, List<Diagnostic> problems)
        {
            if (source == null)
            {
                problems.Add(Diag("ACTORS_REQUIRED", "an actor list is required"));
                return null;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<ActorState>();
            foreach (var actor in source)
            {
                if (actor == null)
                {
                    problems.Add(Diag("ACTOR_NULL", "actor list contains a null entry"));
                    continue;
                }
                if (!seen.Add(actor.ActorId))
                {
                    problems.Add(Diag("ACTOR_DUPLICATE", "duplicate actor id " + actor.ActorId));
                    continue;
                }
                var problem = CheckPosition(actor.Position, actor.ActorId);
                if (problem != null)
                {
                    problems.Add(problem);
                    continue;
                }
                list.Add(actor);
            }
            if (problems.Count > 0) return null;
            list.Sort((a, b) => string.CompareOrdinal(a.ActorId, b.ActorId));
            return list.ToArray();
        }

        private Diagnostic CheckPosition(Position p, string actorId)
        {
            if (p.MapId == null) return Diag("POSITION_UNPLACED", "actor " + actorId + " has no placed position");
            var map = geometry.Find(p.MapId);
            if (map == null) return Diag("MAP_UNKNOWN", "actor " + actorId + " is on unknown map " + p.MapId);
            if (!map.Contains(p.X, p.Y)) return Diag("POSITION_OUT_OF_MAP", "actor " + actorId + " at " + p + " is outside its map");
            return null;
        }

        private static bool ValidToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            foreach (var c in token)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            return true;
        }

        private Result Refuse(string code, string diagnosticCode, string message)
        {
            return Failed(code, new[] { Diag(diagnosticCode, message) });
        }

        private Result Failed(string code, IEnumerable<Diagnostic> details)
        {
            return new Result(OperationStatus.Failed, code, version, details);
        }

        private static Diagnostic Diag(string code, string message)
        {
            return new Diagnostic(code, message, null, string.Empty, string.Empty);
        }

        private static string Describe(List<Diagnostic> problems)
        {
            var parts = new List<string>();
            foreach (var p in problems) parts.Add(p.Code + ": " + p.Message);
            return string.Join("; ", parts);
        }
    }
}
