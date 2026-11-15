using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCampus.Core
{
    /// <summary>An item as content defines it. Category is material, seed, food, potion, tool, furniture or key.</summary>
    public sealed class ItemDef
    {
        public string Id, Name, Description;
        public string Category = "material";
        public int StackMax = Inventory.DefaultStackMax;
        /// <summary>Only food and potions come in fine quality; every other item is always standard.</summary>
        public bool HasQuality;

        public bool IsKey => Category == Inventory.KeyCategory;
    }

    /// <summary>One bag slot or mailbox row: a count of one item at one quality.</summary>
    public sealed class Lot
    {
        public string ItemId;
        public string Quality = Inventory.Standard;
        public int Count;

        public bool Stacks(string itemId, string quality) => ItemId == itemId && Quality == quality;
    }

    /// <summary>Where one Add put the items. A key item goes to the notebook and uses no slot.</summary>
    public sealed class AddResult
    {
        public string ItemId;
        public int ToBag, ToMailbox;
        public bool ToNotebook;
    }

    /// <summary>
    /// The bag, the notebook's key items and the dorm mailbox (design/systems/full-design/domains/domain-02.md §1). Add fills
    /// matching stacks, then empty slots, then sends the rest to the mailbox, so nothing is ever lost. Remove is all or nothing.
    /// Every change goes through these methods; the state lives in GameState and is saved when the day ends.
    /// </summary>
    public sealed class Inventory
    {
        public const int BagSize = 24, DefaultStackMax = 99;
        public const string Standard = "standard", Fine = "fine", KeyCategory = "key";

        readonly Game game;

        public Inventory(Game game) => this.game = game;

        GameState S => game.State;

        /// <summary>Adds since the UI last took them, so it can tell the player once about items sent to the mailbox.</summary>
        public List<AddResult> Received { get; } = new List<AddResult>();

        public AddResult Add(string itemId, int count, string quality = Standard)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be positive");
            var def = game.Content.Item(itemId) ?? throw new InvalidOperationException("unknown item " + itemId);
            var result = new AddResult { ItemId = itemId };
            if (def.IsKey)
            {
                if (!S.KeyItems.Contains(itemId)) S.KeyItems.Add(itemId);
                result.ToNotebook = true;
                Received.Add(result);
                return result;
            }
            quality = def.HasQuality && quality == Fine ? Fine : Standard;
            var max = Math.Max(1, def.StackMax);
            var left = count;
            foreach (var lot in S.Bag)
            {
                if (left == 0) break;
                if (lot == null || !lot.Stacks(itemId, quality) || lot.Count >= max) continue;
                var take = Math.Min(left, max - lot.Count);
                lot.Count += take;
                left -= take;
            }
            for (var i = 0; i < S.Bag.Length && left > 0; i++)
            {
                if (S.Bag[i] != null) continue;
                var take = Math.Min(left, max);
                S.Bag[i] = new Lot { ItemId = itemId, Quality = quality, Count = take };
                left -= take;
            }
            result.ToBag = count - left;
            if (left > 0)
            {
                var row = S.Mailbox.FirstOrDefault(l => l.Stacks(itemId, quality));
                if (row == null) S.Mailbox.Add(new Lot { ItemId = itemId, Quality = quality, Count = left });
                else row.Count += left;
                result.ToMailbox = left;
            }
            Received.Add(result);
            return result;
        }

        /// <summary>How many the player carries: bag stacks of every quality, or 1 when a key item is in the notebook. The mailbox does not count.</summary>
        public int Count(string itemId)
        {
            if (S.KeyItems.Contains(itemId)) return 1;
            return S.Bag.Where(l => l != null && l.ItemId == itemId).Sum(l => l.Count);
        }

        /// <summary>
        /// Takes count of an item out of the bag: standard before fine, later slots first. Refused, with nothing changed, when the
        /// bag holds fewer or the item is a key item (key items are never used up, sold or thrown away).
        /// </summary>
        public bool Remove(string itemId, int count)
        {
            if (count <= 0 || IsKey(itemId) || Count(itemId) < count) return false;
            var left = count;
            foreach (var quality in new[] { Standard, Fine })
                for (var i = S.Bag.Length - 1; i >= 0 && left > 0; i--)
                {
                    var lot = S.Bag[i];
                    if (lot == null || !lot.Stacks(itemId, quality)) continue;
                    var take = Math.Min(left, lot.Count);
                    lot.Count -= take;
                    left -= take;
                    if (lot.Count == 0) S.Bag[i] = null;
                }
            return true;
        }

        /// <summary>Throws away up to count from one slot. Returns how many were thrown away.</summary>
        public int Discard(int slot, int count)
        {
            if (slot < 0 || slot >= S.Bag.Length || count <= 0) return 0;
            var lot = S.Bag[slot];
            if (lot == null || IsKey(lot.ItemId)) return 0;
            var take = Math.Min(count, lot.Count);
            lot.Count -= take;
            if (lot.Count == 0) S.Bag[slot] = null;
            return take;
        }

        /// <summary>Moves the stack in slot from onto slot to: merges when both hold the same stack (up to the stack limit), otherwise swaps.</summary>
        public bool Move(int from, int to)
        {
            if (from < 0 || to < 0 || from >= S.Bag.Length || to >= S.Bag.Length || from == to || S.Bag[from] == null) return false;
            var a = S.Bag[from];
            var b = S.Bag[to];
            if (b != null && b.Stacks(a.ItemId, a.Quality))
            {
                var max = Math.Max(1, game.Content.Item(a.ItemId)?.StackMax ?? DefaultStackMax);
                var take = Math.Min(a.Count, max - b.Count);
                if (take <= 0) return false;
                b.Count += take;
                a.Count -= take;
                if (a.Count == 0) S.Bag[from] = null;
                return true;
            }
            S.Bag[from] = b;
            S.Bag[to] = a;
            return true;
        }

        /// <summary>
        /// Moves one mailbox row into the bag as far as it fits; the rest stays in the mailbox. The mailbox is in the dorm, so this
        /// works only next to the bed. Returns how many moved.
        /// </summary>
        public int TakeFromMailbox(int row)
        {
            if (!game.NearBed || game.Event != null || row < 0 || row >= S.Mailbox.Count) return 0;
            var lot = S.Mailbox[row];
            var max = Math.Max(1, game.Content.Item(lot.ItemId)?.StackMax ?? DefaultStackMax);
            var room = S.Bag.Sum(l => l == null ? max : l.Stacks(lot.ItemId, lot.Quality) ? Math.Max(0, max - l.Count) : 0);
            var take = Math.Min(room, lot.Count);
            if (take == 0) return 0;
            lot.Count -= take;
            if (lot.Count == 0) S.Mailbox.RemoveAt(row);
            var placed = Add(lot.ItemId, take, lot.Quality);
            Received.Remove(placed);
            return take;
        }

        public int FreeSlots => S.Bag.Count(l => l == null);

        bool IsKey(string itemId) => game.Content.Item(itemId)?.IsKey ?? false;
    }
}
