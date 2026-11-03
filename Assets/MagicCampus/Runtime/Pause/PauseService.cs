using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>
    /// Effective policy is the per-channel union of all live tokens. Tokens are never saved.
    /// A scene pauses Calendar/Ambient/Player/World but not Scene; a menu pauses everything.
    /// </summary>
    public sealed class PauseService : IPauseService
    {
        private static readonly PauseSet ScenePolicy = new PauseSet(true, true, true, true, false);
        private static readonly PauseSet MenuPolicy = PauseSet.All;

        private readonly Dictionary<string, PauseToken> tokens = new Dictionary<string, PauseToken>(StringComparer.Ordinal);
        private PauseSet effective = PauseSet.None;
        private long nextToken;

        public IPausePolicy Effective => effective;
        public int LiveTokenCount => tokens.Count;

        public List<string> LiveOwners()
        {
            var owners = new List<string>();
            foreach (var t in tokens.Values) owners.Add(t.Owner);
            owners.Sort(StringComparer.Ordinal);
            return owners;
        }

        public PauseToken Acquire(string owner, IPausePolicy requested)
        {
            var token = new PauseToken("pause-" + (nextToken + 1), owner, requested);
            nextToken++;
            tokens.Add(token.TokenId, token);
            Recompute();
            return token;
        }

        public PauseToken AcquireScene(string owner)
        {
            return Acquire(owner, ScenePolicy);
        }

        public PauseToken AcquireMenu(string owner)
        {
            return Acquire(owner, MenuPolicy);
        }

        public void Release(PauseToken token)
        {
            RuntimeGuard.Required(token, nameof(token));
            PauseToken held;
            if (tokens.TryGetValue(token.TokenId, out held) && ReferenceEquals(held, token))
            {
                tokens.Remove(token.TokenId);
                Recompute();
            }
        }

        private void Recompute()
        {
            bool calendar = false, ambient = false, player = false, world = false, scene = false;
            foreach (var t in tokens.Values)
            {
                calendar |= t.Policy.Calendar;
                ambient |= t.Policy.Ambient;
                player |= t.Policy.Player;
                world |= t.Policy.World;
                scene |= t.Policy.Scene;
            }
            effective = new PauseSet(calendar, ambient, player, world, scene);
        }
    }
}
