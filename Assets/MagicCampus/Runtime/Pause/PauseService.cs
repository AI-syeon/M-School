using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    /// <summary>Supplied pause policy configuration. Default: scene pauses Calendar/Ambient/Player/World but not Scene; menu pauses everything.</summary>
    public sealed class PausePolicyConfig
    {
        public static readonly PausePolicyConfig Default = new PausePolicyConfig(new PauseSet(true, true, true, true, false), PauseSet.All);

        public PauseSet Scene { get; }
        public PauseSet Menu { get; }

        public PausePolicyConfig(IPausePolicy scene, IPausePolicy menu)
        {
            Scene = PauseSet.Copy(scene);
            Menu = PauseSet.Copy(menu);
        }
    }

    /// <summary>Effective policy is the per-channel union of all live tokens. Tokens are never saved.</summary>
    public sealed class PauseService : IPauseService
    {
        private readonly Dictionary<string, PauseToken> tokens = new Dictionary<string, PauseToken>(StringComparer.Ordinal);
        private readonly PausePolicyConfig config;
        private PauseSet effective = PauseSet.None;
        private long nextToken;

        public PauseService() : this(PausePolicyConfig.Default) { }

        public PauseService(PausePolicyConfig config)
        {
            this.config = RuntimeGuard.Required(config, nameof(config));
        }

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
            return Acquire(owner, config.Scene);
        }

        public PauseToken AcquireMenu(string owner)
        {
            return Acquire(owner, config.Menu);
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
