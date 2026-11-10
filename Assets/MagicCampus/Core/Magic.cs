using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCampus.Core
{
    public enum CastFailure { None, NotCastableNow, Unknown, NotLearned, Requirement, NoTarget, Protected }

    /// <summary>What an incantation key did. Cast means Confirm found an exact match and tried it; the result is Magic.Last.</summary>
    public enum IncantationStep { Ignored, Opened, Closed, RuneAdded, Cleared, Incomplete, Cast }

    public enum SlotOutcome { Locked, Empty, Cast }

    public sealed class RequirementStatus
    {
        public RequirementDef Def;
        /// <summary>The current value, or null when no provider can read this kind.</summary>
        public int? Have;
        public bool Met;
        /// <summary>For example "실습용 렌즈 1개 필요 · 가방 0개".</summary>
        public string Text;
    }

    /// <summary>The one answer of Game.TryCastSpell, whether it came from an incantation or a quick slot.</summary>
    public sealed class CastResult
    {
        public bool Ok;
        public CastFailure Failure;
        public string SpellId, TargetId, ReactionId, Observe, Message;
        public ReactionKind? Kind;
        /// <summary>True when this cast opened the quick slots for the first time.</summary>
        public bool UnlockedQuickCast;
        public List<RequirementStatus> Unmet = new List<RequirementStatus>();
    }

    /// <summary>
    /// Spellcasting for the running session: the incantation being typed, the live tags of every magic target, and the one cast
    /// rule. Nothing here is saved except what Cast writes into GameState (quick-cast unlock, situation log). Target tags and the
    /// incantation reset with the room, the day and every load.
    /// </summary>
    public sealed class Magic
    {
        public const int MaxRunes = 5, MinRune = 1, MaxRune = 4, QuickSlotCount = 4, AppliedContexts = 3;
        public static readonly string[] RequirementKinds = { "statAtLeast", "levelAtLeast", "itemCountAtLeast" };
        /// <summary>
        /// Requirement kinds some game system can read today. Empty until stat, level and inventory exist; the bundle check rejects a
        /// shipped spell that asks for any other kind.
        /// </summary>
        public static readonly string[] ProvidedKinds = new string[0];

        readonly Game game;
        readonly Dictionary<string, Dictionary<string, double>> live = new Dictionary<string, Dictionary<string, double>>();
        readonly List<int> runes = new List<int>();

        public Magic(Game game)
        {
            this.game = game;
            ResetTargets();
        }

        public bool Open { get; private set; }
        public IReadOnlyList<int> Runes => runes;
        public CastResult Last { get; private set; }
        /// <summary>How many casts were tried this session, rejected ones included.</summary>
        public int Casts { get; private set; }

        // ------------------------------------------------------------------ incantation

        public IncantationStep Begin()
        {
            if (Open || game.Event != null) return IncantationStep.Ignored;
            Open = true;
            runes.Clear();
            return IncantationStep.Opened;
        }

        /// <summary>Closes and empties the incantation. Every reset (menu, panel, event, portal, day end, load) ends here.</summary>
        public IncantationStep Cancel()
        {
            var was = Open;
            Open = false;
            runes.Clear();
            return was ? IncantationStep.Closed : IncantationStep.Ignored;
        }

        /// <summary>Adds a rune. When no spell starts with the new sequence the runes are emptied and the incantation stays open.</summary>
        public IncantationStep AddRune(int rune)
        {
            if (!Open || rune < MinRune || rune > MaxRune) return IncantationStep.Ignored;
            var next = runes.Concat(new[] { rune }).ToArray();
            if (!game.Content.Spells.Any(s => StartsWith(s.Runes, next)))
            {
                runes.Clear();
                return IncantationStep.Cleared;
            }
            runes.Add(rune);
            return IncantationStep.RuneAdded;
        }

        /// <summary>The spell whose runes equal the entered ones, learned or not; null when nothing matches yet.</summary>
        public SpellDef Exact => runes.Count == 0 ? null : game.Content.Spells.FirstOrDefault(s => s.Runes.SequenceEqual(runes));

        /// <summary>An exact match casts and closes whatever the outcome. Empty or prefix-only input stays open.</summary>
        public IncantationStep Confirm()
        {
            if (!Open) return IncantationStep.Ignored;
            var spell = Exact;
            if (spell == null) return IncantationStep.Incomplete;
            Cancel();
            Cast(spell.Id);
            return IncantationStep.Cast;
        }

        static bool StartsWith(int[] whole, int[] prefix)
        {
            return whole.Length >= prefix.Length && whole.Take(prefix.Length).SequenceEqual(prefix);
        }

        // ------------------------------------------------------------------ cast

        public CastResult Cast(string spellId)
        {
            Casts++;
            return Last = Resolve(spellId);
        }

        CastResult Resolve(string spellId)
        {
            var result = new CastResult { SpellId = spellId };
            CastResult Fail(CastFailure failure, string message)
            {
                result.Failure = failure;
                result.Message = message;
                return result;
            }

            if (game.Event != null) return Fail(CastFailure.NotCastableNow, "지금은 주문을 쓸 수 없습니다.");
            var spell = game.Content.Spell(spellId);
            if (spell == null) return Fail(CastFailure.Unknown, "알 수 없는 주문입니다.");
            if (!game.State.KnownSpells.Contains(spellId)) return Fail(CastFailure.NotLearned, "아직 배우지 않은 주문입니다.");
            result.Unmet = Requirements(spell).Where(r => !r.Met).ToList();
            if (result.Unmet.Count > 0) return Fail(CastFailure.Requirement, spell.Name + " · " + string.Join(" / ", result.Unmet.Select(r => r.Text)));

            if (spell.UnlocksQuickCast)
            {
                result.Ok = true;
                result.TargetId = Actor.PlayerId;
                result.UnlockedQuickCast = !game.State.QuickCastUnlocked;
                game.State.QuickCastUnlocked = true;
                result.Message = spell.Name + " · " + (result.UnlockedQuickCast ? "빠른 시전을 익혔습니다. 주문 수첩(K)에서 슬롯 1-4를 정하세요." : "빠른 시전은 이미 열려 있습니다.");
                return result;
            }

            var target = Aim(spell.Range);
            if (target == null) return Fail(CastFailure.NoTarget, spell.Name + " · 바라보는 쪽 " + spell.Range.ToString("0.#") + "칸 안에 대상이 없습니다.");
            result.TargetId = target.Id;
            var row = Match(spell.Id, Tags(target.Id));
            if (row != null)
            {
                result.ReactionId = row.Id;
                result.Kind = row.Kind;
                result.Observe = row.Observe;
            }
            if (row != null && row.Kind == ReactionKind.Protect) return Fail(CastFailure.Protected, spell.Name + " → " + target.Name + " · " + row.Observe);

            if (row == null) result.Observe = "아무 반응이 없습니다.";
            else Apply(target.Id, row);
            if (row != null && row.Context != null) Log(spell.Id, row.Context);
            result.Ok = true;
            result.Message = spell.Name + " → " + target.Name + " · " + result.Observe;
            return result;
        }

        /// <summary>The first row by kind order Protect, Combo, React, then file order. Null means a plain observation.</summary>
        ReactionDef Match(string spellId, HashSet<string> tags)
        {
            foreach (var kind in new[] { ReactionKind.Protect, ReactionKind.Combo, ReactionKind.React })
                foreach (var row in game.Content.Reactions)
                    if (row.Kind == kind && (row.Spell == "*" || row.Spell == spellId) && row.Needs.All(tags.Contains)) return row;
            return null;
        }

        void Apply(string targetId, ReactionDef row)
        {
            var tags = live[targetId];
            foreach (var tag in row.Remove) tags.Remove(tag);
            foreach (var add in row.Add) tags[add.Tag] = add.Seconds.HasValue ? game.MagicSeconds + add.Seconds.Value : double.PositiveInfinity;
            foreach (var gone in tags.Where(t => t.Value <= game.MagicSeconds).Select(t => t.Key).ToList()) tags.Remove(gone);
        }

        void Log(string spellId, string context)
        {
            var log = game.State.SituationLog;
            if (!log.TryGetValue(spellId, out var contexts)) log[spellId] = contexts = new List<string>();
            if (!contexts.Contains(context)) contexts.Add(context);
        }

        // ------------------------------------------------------------------ requirements and situations

        public List<RequirementStatus> Requirements(SpellDef spell) => spell.Requirements.Select(Status).ToList();

        RequirementStatus Status(RequirementDef r)
        {
            var have = game.RequirementProvider == null ? null : game.RequirementProvider(r);
            var label = string.IsNullOrEmpty(r.Label) ? r.Id : r.Label;
            var status = new RequirementStatus { Def = r, Have = have, Met = have.HasValue && have.Value >= r.Min };
            if (!have.HasValue) status.Text = label + " 조건을 아직 확인할 수 없습니다";
            else if (r.Kind == "itemCountAtLeast") status.Text = label + " " + r.Min + "개 필요 · 가방 " + have.Value + "개";
            else if (r.Kind == "statAtLeast") status.Text = label + " 스탯 " + r.Min + " 필요 · 현재 " + have.Value;
            else if (r.Kind == "levelAtLeast") status.Text = label + " 레벨 " + r.Min + " 필요 · 현재 " + have.Value;
            else status.Text = label + " " + r.Min + " 필요 · 현재 " + have.Value;
            return status;
        }

        public int ContextCount(string spellId) => game.State.SituationLog.TryGetValue(spellId, out var c) ? c.Count : 0;

        /// <summary>Three distinct situations open a spell's applied use. There is no variant-choice screen yet.</summary>
        public bool AppliedUnlocked(string spellId) => ContextCount(spellId) >= AppliedContexts;

        // ------------------------------------------------------------------ quick slots

        public SlotOutcome CastSlot(int slot)
        {
            if (!game.State.QuickCastUnlocked || slot < 0 || slot >= QuickSlotCount) return SlotOutcome.Locked;
            var id = game.State.QuickSlots[slot];
            if (id == null) return SlotOutcome.Empty;
            Cast(id);
            return SlotOutcome.Cast;
        }

        /// <summary>Puts a learned, quick-castable spell in a slot, or empties it with null. Refused while locked or during an event.</summary>
        public bool SetSlot(int slot, string spellId)
        {
            var state = game.State;
            if (!state.QuickCastUnlocked || game.Event != null || slot < 0 || slot >= QuickSlotCount) return false;
            if (spellId != null)
            {
                var spell = game.Content.Spell(spellId);
                if (spell == null || !spell.AllowQuickCast || !state.KnownSpells.Contains(spellId)) return false;
            }
            state.QuickSlots[slot] = spellId;
            return true;
        }

        // ------------------------------------------------------------------ targets

        /// <summary>Restores every target to the tags the content gives it. The stand-in for a room reset until rooms exist.</summary>
        public void ResetTargets()
        {
            live.Clear();
            foreach (var target in game.Content.MagicTargets)
                live[target.Id] = target.Tags.ToDictionary(t => t, t => double.PositiveInfinity);
        }

        /// <summary>The tags a target has right now; windowed tags that ran out are gone.</summary>
        public HashSet<string> Tags(string targetId)
        {
            var set = new HashSet<string>();
            if (live.TryGetValue(targetId, out var tags))
                foreach (var tag in tags)
                    if (tag.Value > game.MagicSeconds) set.Add(tag.Key);
            return set;
        }

        /// <summary>
        /// The target a cast would hit: same map, within range, in front of the player, best of 0.6 facing + 0.4 closeness.
        /// The HUD uses this too, so the preview and the cast always agree.
        /// </summary>
        public MagicTargetDef Aim(double range)
        {
            if (range <= 0) return null;
            var player = game.Player;
            FacingVector(player.Facing, out var fx, out var fy);
            MagicTargetDef best = null;
            var bestScore = double.NegativeInfinity;
            foreach (var target in game.Content.MagicTargets)
            {
                if (target.MapId != player.MapId) continue;
                double dx = target.X - player.X, dy = target.Y - player.Y, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > range) continue;
                var cos = distance < 1e-6 ? 1 : (dx * fx + dy * fy) / distance;
                if (cos < -1e-9) continue;
                var score = 0.6 * (1 + cos) / 2 + 0.4 * (1 - distance / range);
                if (score > bestScore)
                {
                    best = target;
                    bestScore = score;
                }
            }
            return best;
        }

        static void FacingVector(string facing, out double x, out double y)
        {
            x = facing == "east" ? 1 : facing == "west" ? -1 : 0;
            y = facing == "north" ? 1 : facing == "south" ? -1 : 0;
            if (x == 0 && y == 0) y = -1;
        }
    }
}
