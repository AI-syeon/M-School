using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Core;
using Newtonsoft.Json.Linq;

static partial class Program
{
    const string ItemStart = "start-items.json";
    static readonly string[] ItemCategories = { "material", "seed", "food", "potion", "tool", "furniture", "key" };

    // ------------------------------------------------------------------ bundle rules for items

    static List<string> ItemProblems(Content c)
    {
        var p = new List<string>();
        void Rule(bool ok, string what) { if (!ok) p.Add(what); }
        Rule(c.Items.Select(i => i.Id).Distinct().Count() == c.Items.Count, "item ids are unique");
        foreach (var i in c.Items)
        {
            Rule(!string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Name), (i.Id ?? "item") + " has an id and a name");
            Rule(ItemCategories.Contains(i.Category), i.Id + " category " + i.Category + " is known");
            Rule(i.StackMax >= 1, i.Id + " stackMax >= 1");
        }
        return p;
    }

    // ------------------------------------------------------------------ helpers

    static int Slots(Game g) => g.State.Bag.Count(l => l != null);

    /// <summary>Fills free bag slots with single lenses (stackMax 1) until leaveFree slots remain.</summary>
    static void FillBag(Game g, int leaveFree = 0)
    {
        var n = g.Inventory.FreeSlots - leaveFree;
        if (n > 0) g.Inventory.Add("item.lens", n);
    }

    static Game NewEventGame(Action<Content> edit)
    {
        var g = NewGame("start-accepted.json");
        edit(g.Content);
        return g;
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (Exception) { return true; }
    }

    // ------------------------------------------------------------------ tests

    static void InventoryTests()
    {
        ItemRules();
        AddRules();
        RemoveRules();
        MoveRules();
        MailboxRules();
        ItemEffect();
        ItemRequirement();
        NormalizeRules();
        InventorySaves();
        VisitedMaps();
        CellRules();
    }

    static void ItemRules()
    {
        string Broken(Action<Content> edit)
        {
            var copy = Content.Load(Base);
            edit(copy);
            return string.Join("; ", Problems(copy));
        }
        var c = Content.Load(Base);
        Check("item bundle: 9 items, 2 key items, stackMax 1 lens, quality only on food and potions",
            c.Items.Count == 9 && c.Items.Count(i => i.IsKey) == 2 && c.Item("item.lens").StackMax == 1
            && c.Items.Where(i => i.HasQuality).Select(i => i.Id).OrderBy(x => x).SequenceEqual(new[] { "item.focus_potion", "item.honey_bread" }) && ItemProblems(c).Count == 0);
        Check("item rules catch a duplicate id", Broken(x => x.Items.Add(new ItemDef { Id = "item.lens", Name = "복제" })).Contains("item ids are unique"));
        Check("item rules catch a missing name or id", Broken(x => x.Items[0].Name = " ").Contains("has an id and a name") && Broken(x => x.Items[0].Id = null).Contains("has an id and a name"));
        Check("item rules catch an unknown category", Broken(x => x.Items[0].Category = "weapon").Contains("category weapon is known"));
        Check("item rules catch stackMax below 1", Broken(x => x.Items[1].StackMax = 0).Contains("stackMax >= 1"));
        Check("effect rules accept item.add on a known item",
            Broken(x => x.Events[0].Effects += " / item.add item.frost_mint 2").Length == 0
            && Broken(x => x.Events[0].Effects += " / item.add item.honey_bread 1 fine").Length == 0);
        Check("effect rules catch item.add with an unknown item, a bad count or a bad quality",
            Broken(x => x.Events[0].Effects += " / item.add item.nope 1").Contains("item.add")
            && Broken(x => x.Events[0].Effects += " / item.add item.lens 0").Contains("item.add")
            && Broken(x => x.Events[0].Effects += " / item.add item.lens -2").Contains("item.add")
            && Broken(x => x.Events[0].Effects += " / item.add item.lens").Contains("item.add")
            && Broken(x => x.Events[0].Effects += " / item.add item.lens 1 shiny").Contains("item.add"));
        Check("content: playable maps are named, unnamed or unknown maps get the fallback",
            c.MapName("map.region_1") == "이름 없는 구역" && c.MapName("map.courtyard") == "중앙 안뜰" && c.MapName("map.lounge") == "학생 휴게실" && c.MapName("map.nowhere") == "이름 없는 구역");
    }

    static void AddRules()
    {
        var g = NewGame(ItemStart);
        var inv = g.Inventory;
        Check("start-items: 8 lots in the bag, a key item, a mailbox row",
            Slots(g) == 8 && inv.FreeSlots == 16 && g.State.KeyItems.SequenceEqual(new[] { "item.student_card" }) && g.State.Mailbox.Count == 1);
        Check("count: sums standard and fine, ignores the mailbox, a key item counts 1",
            inv.Count("item.honey_bread") == 6 && inv.Count("item.crystal_dust") == 99 && inv.Count("item.student_card") == 1 && inv.Count("item.torn_note") == 0 && inv.Count("item.nope") == 0);

        var r = inv.Add("item.rune_chalk", 4);
        Check("add: merges into the existing stack without taking a slot",
            r.ToBag == 4 && r.ToMailbox == 0 && !r.ToNotebook && g.State.Bag[1].Count == 7 && Slots(g) == 8 && inv.Received.Contains(r));

        r = inv.Add("item.crystal_dust", 5);
        Check("add: a full stack opens a new slot",
            r.ToBag == 5 && g.State.Bag[7].Count == 99 && g.State.Bag[8]?.ItemId == "item.crystal_dust" && g.State.Bag[8].Count == 5 && Slots(g) == 9);

        r = inv.Add("item.crystal_dust", 200);
        Check("add: more than one stack spreads over slots (top up 94, then 99, then 7)",
            r.ToBag == 200 && g.State.Bag[8].Count == 99 && g.State.Bag[9].Count == 99 && g.State.Bag[10].Count == 7 && Slots(g) == 11 && inv.Count("item.crystal_dust") == 99 + 5 + 200);

        var f = NewGame(ItemStart);
        FillBag(f, 1);
        Check("overflow setup: 23 slots filled, 1 free", Slots(f) == 23 && f.Inventory.FreeSlots == 1);
        var before = f.State.Mailbox.Sum(l => l.Count);
        r = f.Inventory.Add("item.lens", 3);
        var lensRow = f.State.Mailbox.Single(l => l.ItemId == "item.lens");
        Check("overflow: 3 into 1 free slot sends 1 to the bag and 2 to the mailbox",
            r.ToBag == 1 && r.ToMailbox == 2 && f.Inventory.FreeSlots == 0 && lensRow.Count == 2 && f.State.Mailbox.Sum(l => l.Count) == before + 2);
        r = f.Inventory.Add("item.lens", 1);
        Check("overflow: a full bag sends everything to the mailbox and merges into the existing row",
            r.ToBag == 0 && r.ToMailbox == 1 && lensRow.Count == 3 && f.State.Mailbox.Count(l => l.ItemId == "item.lens") == 1);
        r = f.Inventory.Add("item.crystal_dust", 4);
        Check("overflow: a full stack in a full bag also goes to the existing mailbox row",
            r.ToBag == 0 && r.ToMailbox == 4 && f.State.Mailbox.Single(l => l.ItemId == "item.crystal_dust").Count == 24);

        var full = NewGame(ItemStart);
        FillBag(full);
        var rn = full.Inventory.Add("item.torn_note", 1);
        Check("key item: goes to the notebook with a full bag and uses no slot",
            rn.ToNotebook && rn.ToBag == 0 && rn.ToMailbox == 0 && full.State.KeyItems.SequenceEqual(new[] { "item.student_card", "item.torn_note" }) && full.Inventory.FreeSlots == 0 && full.State.Mailbox.Count == 1);
        full.Inventory.Add("item.torn_note", 3);
        Check("key item: adding it again keeps one copy", full.State.KeyItems.Count(id => id == "item.torn_note") == 1 && full.Inventory.Count("item.torn_note") == 1);
        var held = full.State.Bag[0];
        full.State.Bag[0] = new Lot { ItemId = "item.student_card", Count = 1 };
        var thrown = full.Inventory.Discard(0, 1);
        Check("key item: Remove and Discard refuse it",
            !full.Inventory.Remove("item.student_card", 1) && !full.Inventory.Remove("item.torn_note", 1) && thrown == 0 && full.State.Bag[0] != null
            && full.State.KeyItems.Contains("item.student_card") && full.State.KeyItems.Contains("item.torn_note"));
        full.State.Bag[0] = held;

        var lens = NewGame(ItemStart);
        r = lens.Inventory.Add("item.lens", 3);
        Check("stackMax 1: every lens takes its own slot", r.ToBag == 3 && lens.Inventory.FreeSlots == 13 && lens.State.Bag.Count(l => l != null && l.ItemId == "item.lens" && l.Count == 1) == 4);

        var q = NewGame(ItemStart);
        r = q.Inventory.Add("item.honey_bread", 2, Inventory.Fine);
        Check("quality: fine bread merges into the fine stack and keeps its quality",
            r.ToBag == 2 && q.State.Bag[3].Count == 3 && q.State.Bag[3].Quality == Inventory.Fine && q.State.Bag[2].Count == 5 && q.Inventory.FreeSlots == 16);
        q.Inventory.Add("item.rune_chalk", 2, Inventory.Fine);
        Check("quality: fine on an item without quality is forced to standard",
            q.State.Bag[1].Count == 5 && q.State.Bag.All(l => l == null || l.ItemId != "item.rune_chalk" || l.Quality == Inventory.Standard) && q.Inventory.FreeSlots == 16);
        q.Inventory.Add("item.honey_bread", 1, "shiny");
        Check("quality: an unknown quality becomes standard", q.State.Bag[2].Count == 6);
        q.Inventory.Add("item.moonbean_seed", 1, Inventory.Fine);
        Check("quality: fine seeds also stay standard", q.State.Bag[5].Count == 13 && q.Inventory.FreeSlots == 16);

        var snapshot = SaveFile.ToJson(q.State);
        Check("add: a non-positive count or an unknown item throws and changes nothing",
            Throws(() => q.Inventory.Add("item.lens", 0)) && Throws(() => q.Inventory.Add("item.lens", -1)) && Throws(() => q.Inventory.Add("item.nope", 1)) && SaveFile.ToJson(q.State) == snapshot);
    }

    static void RemoveRules()
    {
        var g = NewGame(ItemStart);
        var inv = g.Inventory;
        var before = SaveFile.ToJson(g.State);
        Check("remove: asking for more than the bag holds is refused and changes nothing",
            !inv.Remove("item.rune_chalk", 4) && !inv.Remove("item.nope", 1) && !inv.Remove("item.rune_chalk", 0) && !inv.Remove("item.rune_chalk", -1)
            && !inv.Remove("item.crystal_dust", 100) && SaveFile.ToJson(g.State) == before);
        Check("remove: exactly what is held empties the slot", inv.Remove("item.rune_chalk", 3) && g.State.Bag[1] == null && inv.FreeSlots == 17);

        var bread = NewGame(ItemStart);
        Check("remove: standard goes before fine",
            bread.Inventory.Remove("item.honey_bread", 5) && bread.State.Bag[2] == null && bread.State.Bag[3].Count == 1 && bread.State.Bag[3].Quality == Inventory.Fine);
        Check("remove: fine goes after the standard is gone", bread.Inventory.Remove("item.honey_bread", 1) && bread.State.Bag[3] == null && bread.Inventory.Count("item.honey_bread") == 0);
        var mixed = NewGame(ItemStart);
        Check("remove: one call can take standard then fine", mixed.Inventory.Remove("item.honey_bread", 6) && mixed.Inventory.Count("item.honey_bread") == 0);
        var some = NewGame(ItemStart);
        Check("remove: part of the standard stack leaves the fine one alone", some.Inventory.Remove("item.honey_bread", 2) && some.State.Bag[2].Count == 3 && some.State.Bag[3].Count == 1);

        var spread = NewGame(ItemStart);
        spread.Inventory.Add("item.crystal_dust", 10);
        Check("remove: across stacks takes the later slot first", spread.Inventory.Remove("item.crystal_dust", 105) && spread.State.Bag[8] == null && spread.State.Bag[7].Count == 4);

        var d = NewGame(ItemStart);
        Check("discard: takes part of a stack", d.Inventory.Discard(5, 5) == 5 && d.State.Bag[5].Count == 7);
        Check("discard: asking for more throws away the whole stack", d.Inventory.Discard(5, 50) == 7 && d.State.Bag[5] == null);
        var snapshot = SaveFile.ToJson(d.State);
        Check("discard: an empty slot, a bad slot or a non-positive count does nothing",
            d.Inventory.Discard(5, 1) == 0 && d.Inventory.Discard(-1, 1) == 0 && d.Inventory.Discard(24, 1) == 0 && d.Inventory.Discard(0, 0) == 0 && d.Inventory.Discard(0, -2) == 0 && SaveFile.ToJson(d.State) == snapshot);
    }

    static void MoveRules()
    {
        var g = NewGame(ItemStart);
        var inv = g.Inventory;
        Check("move: onto a different stack swaps the two slots", inv.Move(0, 1) && g.State.Bag[0].ItemId == "item.rune_chalk" && g.State.Bag[1].ItemId == "item.lens");
        Check("move: onto an empty slot moves the stack", inv.Move(0, 12) && g.State.Bag[0] == null && g.State.Bag[12].ItemId == "item.rune_chalk" && g.State.Bag[12].Count == 3);
        Check("move: fine and standard of one item do not merge, they swap",
            inv.Move(3, 2) && g.State.Bag[2].Quality == Inventory.Fine && g.State.Bag[3].Quality == Inventory.Standard && g.State.Bag[2].Count == 1 && g.State.Bag[3].Count == 5);
        var snapshot = SaveFile.ToJson(g.State);
        Check("move: an empty source, the same slot or an out-of-range slot is refused",
            !inv.Move(20, 1) && !inv.Move(1, 1) && !inv.Move(-1, 1) && !inv.Move(1, 24) && !inv.Move(1, -1) && !inv.Move(24, 1) && SaveFile.ToJson(g.State) == snapshot);

        var m = NewGame(ItemStart);
        m.State.Bag[10] = new Lot { ItemId = "item.rune_chalk", Count = 5 };
        Check("move: the same stack merges into the target and empties the source", m.Inventory.Move(10, 1) && m.State.Bag[1].Count == 8 && m.State.Bag[10] == null);
        m.State.Bag[10] = new Lot { ItemId = "item.crystal_dust", Count = 5 };
        var full = SaveFile.ToJson(m.State);
        Check("move: a full target takes nothing and the move is refused", !m.Inventory.Move(10, 7) && SaveFile.ToJson(m.State) == full);
        m.State.Bag[7].Count = 95;
        Check("move: merging stops at 99 and leaves the rest in the source", m.Inventory.Move(10, 7) && m.State.Bag[7].Count == 99 && m.State.Bag[10].Count == 1);
        m.State.Bag[11] = new Lot { ItemId = "item.lens", Count = 1 };
        Check("move: stackMax 1 never merges", !m.Inventory.Move(11, 0) && m.State.Bag[11] != null && m.State.Bag[0].Count == 1);
    }

    static void MailboxRules()
    {
        var g = NewGame(ItemStart);
        var before = SaveFile.ToJson(g.State);
        Check("mailbox: away from the bed nothing moves", !g.NearBed && g.Inventory.TakeFromMailbox(0) == 0 && SaveFile.ToJson(g.State) == before);

        World.Place(g.Player, g.Content.Bed);
        Check("mailbox: near the bed the row moves into the bag",
            g.NearBed && g.Inventory.TakeFromMailbox(0) == 20 && g.State.Mailbox.Count == 0 && g.State.Bag[8]?.ItemId == "item.crystal_dust" && g.State.Bag[8].Count == 20 && g.State.Bag[7].Count == 99);
        Check("mailbox: taking from the mailbox is not a new delivery", g.Inventory.Received.Count == 0);
        Check("mailbox: a missing row moves nothing", g.Inventory.TakeFromMailbox(0) == 0 && g.Inventory.TakeFromMailbox(-1) == 0);

        var p = NewGame(ItemStart);
        World.Place(p.Player, p.Content.Bed);
        FillBag(p);
        Check("mailbox: a full bag takes nothing", p.Inventory.TakeFromMailbox(0) == 0 && p.State.Mailbox[0].Count == 20);
        p.State.Bag[7].Count = 94;
        Check("mailbox: only what fits moves and the rest stays", p.Inventory.TakeFromMailbox(0) == 5 && p.State.Bag[7].Count == 99 && p.State.Mailbox.Single().Count == 15);
        Check("mailbox: nothing is lost (bag + mailbox total unchanged)",
            p.State.Bag.Where(l => l != null && l.ItemId == "item.crystal_dust").Sum(l => l.Count) + p.State.Mailbox.Sum(l => l.Count) == 94 + 20);

        var near = NewGame(ItemStart);
        World.Place(near.Player, new Spot { MapId = near.Content.Bed.MapId, X = near.Content.Bed.X + 0.9, Y = near.Content.Bed.Y });
        Check("mailbox: within a tile of the bed counts as near", near.NearBed && near.Inventory.TakeFromMailbox(0) == 20);

        var ev = NewGame(ItemStart);
        ev.Accept("event.snack");
        ev.StartEvent("event.snack");
        ev.Update(1 / 30.0);
        World.Place(ev.Player, ev.Content.Bed);
        Check("mailbox: refused while an event runs", ev.Event != null && ev.Inventory.TakeFromMailbox(0) == 0 && ev.State.Mailbox.Count == 1);
    }

    static void ItemEffect()
    {
        var g = NewEventGame(c => c.Events.First(e => e.Id == "event.snack").Effects += " / item.add item.frost_mint 2 / item.add item.honey_bread 1 fine");
        Check("item.add effect: nothing is given before the event ends", g.Inventory.Count("item.frost_mint") == 0);
        g.StartEvent("event.snack");
        Play(g, 0);
        var s = g.State;
        Check("item.add effect: applied once when the event completes, beside the other effects",
            g.Inventory.Count("item.frost_mint") == 2 && g.Inventory.Count("item.honey_bread") == 1 && s.Bag.Single(l => l != null && l.ItemId == "item.honey_bread").Quality == Inventory.Fine
            && s.Wallet == 220 && g.Flag("memory.snack_done") == "true" && s.Completed.SequenceEqual(new[] { "event.snack" }));
        Check("item.add effect: reported to the UI through Received", g.Inventory.Received.Select(r => r.ItemId).SequenceEqual(new[] { "item.frost_mint", "item.honey_bread" }));
        g.StartEvent("event.snack");
        Check("item.add effect: a refused replay gives nothing more", g.Inventory.Count("item.frost_mint") == 2);

        var cancel = NewEventGame(c => c.Events.First(e => e.Id == "event.snack").Effects += " / item.add item.frost_mint 2");
        cancel.StartEvent("event.snack");
        Run(cancel, 10, () => cancel.Dialogue != null);
        cancel.CancelEvent();
        Check("item.add effect: a cancelled event gives nothing", cancel.Inventory.Count("item.frost_mint") == 0 && cancel.Inventory.Received.Count == 0);

        var full = NewEventGame(c => c.Events.First(e => e.Id == "event.snack").Effects += " / item.add item.lens 2");
        FillBag(full, 1);
        full.StartEvent("event.snack");
        Play(full, 1);
        Check("item.add effect: a full bag sends the rest of the reward to the mailbox",
            full.Inventory.Count("item.lens") == 24 && full.State.Mailbox.Single(l => l.ItemId == "item.lens").Count == 1 && full.Inventory.Received.Last().ToMailbox == 1);
    }

    static void ItemRequirement()
    {
        var lens = new RequirementDef { Kind = "itemCountAtLeast", Id = "item.lens", Label = "실습용 렌즈", Min = 1 };
        var g = MagicGame("dark");
        g.Content.Spell("spell.lantern").Requirements.Add(lens);
        var miss = CastSpell(g, "lantern");
        Check("item requirement: the default provider reads the bag (0 lenses fails with the item message)",
            miss.Failure == CastFailure.Requirement && miss.Unmet.Single().Have == 0 && miss.Unmet.Single().Text == "실습용 렌즈 1개 필요 · 가방 0개");
        g.State.Mailbox.Add(new Lot { ItemId = "item.lens", Count = 5 });
        Check("item requirement: the mailbox does not count", CastSpell(g, "lantern").Failure == CastFailure.Requirement);
        g.Inventory.Add("item.lens", 1);
        Check("item requirement: one lens in the bag passes", CastSpell(g, "lantern").Ok);
        g.Inventory.Remove("item.lens", 1);
        Check("item requirement: using the lens up fails again", CastSpell(g, "lantern").Failure == CastFailure.Requirement);

        var many = MagicGame("dark");
        many.Content.Spell("spell.lantern").Requirements.Add(new RequirementDef { Kind = "itemCountAtLeast", Id = "item.rune_chalk", Label = "룬 분필", Min = 3 });
        many.Inventory.Add("item.rune_chalk", 2);
        var low = CastSpell(many, "lantern");
        Check("item requirement: one below the minimum fails", low.Failure == CastFailure.Requirement && low.Unmet.Single().Have == 2);
        many.Inventory.Add("item.rune_chalk", 1);
        Check("item requirement: exactly the minimum passes", CastSpell(many, "lantern").Ok);

        var key = MagicGame("dark");
        key.Content.Spell("spell.lantern").Requirements.Add(new RequirementDef { Kind = "itemCountAtLeast", Id = "item.student_card", Label = "학생증", Min = 1 });
        var noCard = CastSpell(key, "lantern").Failure == CastFailure.Requirement;
        key.Inventory.Add("item.student_card", 1);
        Check("item requirement: a key item in the notebook counts as 1", noCard && CastSpell(key, "lantern").Ok);

        var stat = MagicGame("dark");
        stat.Content.Spell("spell.lantern").Requirements.Add(new RequirementDef { Kind = "statAtLeast", Id = "stat.focus", Label = "집중", Min = 1 });
        Check("item requirement: other kinds still have no provider", CastSpell(stat, "lantern").Unmet.Single().Have == null);
    }

    static void NormalizeRules()
    {
        var state = SaveFile.Read(Path.Combine(Base, "start-offered.json"));
        state.Bag = null;
        state.KeyItems = null;
        state.Mailbox = null;
        state.VisitedMaps = null;
        var old = new Game(Content.Load(Base), state, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        Check("normalize: an old save without bag fields gets 24 empty slots and empty lists",
            old.State.Bag.Length == Inventory.BagSize && old.State.Bag.All(l => l == null) && old.State.KeyItems.Count == 0 && old.State.Mailbox.Count == 0 && old.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard" }));

        var json = JObject.Parse(File.ReadAllText(Path.Combine(Base, "start-offered.json")));
        Check("normalize: a file without bag fields reads with the default bag", SaveFile.FromJson(json.ToString()).Bag.Length == Inventory.BagSize);
        json["bag"] = JValue.CreateNull();
        json["mailbox"] = JValue.CreateNull();
        json["keyItems"] = JValue.CreateNull();
        json["visitedMaps"] = JValue.CreateNull();
        var fromNull = new Game(Content.Load(Base), SaveFile.FromJson(json.ToString()), Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        Check("normalize: explicit nulls in the file are replaced", fromNull.State.Bag.Length == Inventory.BagSize && fromNull.State.Mailbox != null && fromNull.State.KeyItems != null);

        var messy = SaveFile.Read(Path.Combine(Base, "start-offered.json"));
        messy.Bag = new[]
        {
            new Lot { ItemId = "item.student_card", Count = 1 },
            new Lot { ItemId = "item.rune_chalk", Count = 0 },
            null,
            new Lot { ItemId = "item.honey_bread", Quality = Inventory.Fine, Count = 2 },
            new Lot { ItemId = "item.rune_chalk", Quality = Inventory.Fine, Count = 3 },
            new Lot { ItemId = "item.mystery", Count = 1 },
            new Lot { ItemId = "", Count = 4 },
        };
        messy.Mailbox = new List<Lot> { new Lot { ItemId = "item.torn_note", Count = 1 }, new Lot { ItemId = "item.lens", Count = -3 }, new Lot { ItemId = "item.lens", Count = 2 } };
        messy.KeyItems = new List<string> { "item.student_card", "", "item.student_card" };
        messy.VisitedMaps = new List<string> { "map.courtyard", "map.courtyard", "" };
        var g = new Game(Content.Load(Base), messy, Path.Combine(Temp, Guid.NewGuid().ToString("N"), "save.json"));
        var b = g.State.Bag;
        Check("normalize: the bag becomes 24 slots", b.Length == Inventory.BagSize);
        Check("normalize: a key item found in the bag moves to the notebook once and frees its slot", b[0] == null && g.State.KeyItems.Count(id => id == "item.student_card") == 1);
        Check("normalize: a zero-count lot and a lot without an id are dropped", b[1] == null && b[6] == null && b.Count(l => l != null) == 3);
        Check("normalize: a key item in the mailbox moves to the notebook and bad rows are dropped",
            g.State.KeyItems.SequenceEqual(new[] { "item.student_card", "item.torn_note" }) && g.State.Mailbox.Count == 1 && g.State.Mailbox[0].ItemId == "item.lens" && g.State.Mailbox[0].Count == 2);
        Check("normalize: fine is kept for food and cleared for other items", b[3].Quality == Inventory.Fine && b[4].Quality == Inventory.Standard);
        Check("normalize: an unknown item id is kept", b[5]?.ItemId == "item.mystery" && b[5].Count == 1);
        Check("normalize: visited maps drop duplicates and blanks", g.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard" }));
    }

    static void InventorySaves()
    {
        var g = NewGame(ItemStart);
        var json = SaveFile.ToJson(g.State);
        var back = SaveFile.FromJson(json);
        Check("save: ToJson/FromJson/ToJson is stable", SaveFile.ToJson(back) == json);
        Check("save: the round trip keeps bag, notebook, mailbox and visited maps",
            back.Bag.Length == 24 && back.Bag[7].ItemId == "item.crystal_dust" && back.Bag[7].Count == 99 && back.Bag[3].Quality == Inventory.Fine && back.Bag[8] == null
            && back.KeyItems.SequenceEqual(new[] { "item.student_card" }) && back.Mailbox.Single().Count == 20 && back.VisitedMaps.SequenceEqual(new[] { "map.courtyard" }));

        FillBag(g);
        g.Inventory.Add("item.torn_note", 1);
        g.Inventory.Add("item.moonbean_seed", 400);
        World.Place(g.Player, g.Content.Bed);
        Check("save: ending the day writes the file", g.EndDay(DayEnd.Slept) && File.Exists(g.SavePath));
        var reloaded = Game.Open(Base, Path.Combine(Base, ItemStart), g.SavePath);
        Check("save: a reloaded game has the same bag, notebook, mailbox and visited maps",
            SaveFile.ToJson(reloaded.State) == SaveFile.ToJson(g.State) && reloaded.Inventory.Count("item.lens") == 17 && reloaded.Inventory.Count("item.torn_note") == 1
            && reloaded.State.Mailbox.Any(l => l.ItemId == "item.moonbean_seed") && reloaded.State.Mailbox.Any(l => l.ItemId == "item.crystal_dust" && l.Count == 20));
    }

    static void VisitedMaps()
    {
        var g = NewGame("start-offered.json");
        Check("visited: the starting map is marked when the game opens", g.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard" }));
        World.Place(g.Player, new Spot { MapId = "map.courtyard", X = 17.5, Y = 7.5 });
        g.SetPlayerInput(0, 1);
        Run(g, 1, () => g.MapId == "map.lounge");
        g.SetPlayerInput(0, 0);
        g.Update(1 / 30.0);
        Check("visited: walking through the portal adds the lounge after the courtyard", g.MapId == "map.lounge" && g.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard", "map.lounge" }));
        g.SetPlayerInput(0, 1);
        Run(g, 0.4);
        g.SetPlayerInput(0, -1);
        Run(g, 1, () => g.MapId == "map.courtyard");
        g.SetPlayerInput(0, 0);
        g.Update(1 / 30.0);
        Check("visited: going back does not duplicate", g.MapId == "map.courtyard" && g.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard", "map.lounge" }));
        World.Place(g.Player, g.Content.Bed);
        g.EndDay(DayEnd.Slept);
        var reloaded = Game.Open(Base, Path.Combine(Base, "start-offered.json"), g.SavePath);
        Check("visited: a reloaded save keeps the lounge, and the dorm it woke up in", reloaded.State.VisitedMaps.SequenceEqual(new[] { "map.courtyard", "map.lounge", "map.dorm" }));

        var ev = NewGame(ItemStart);
        ev.Accept("event.snack");
        ev.StartEvent("event.snack");
        ev.Update(1 / 30.0);
        ev.Update(1 / 30.0);
        Check("visited: staging an event in the lounge marks it too", ev.MapId == "map.lounge" && ev.State.VisitedMaps.Contains("map.lounge"));
    }

    static void CellRules()
    {
        var c = Content.Load(Base);
        var yard = c.Map(c.Bed.MapId);
        var lounge = c.Map("map.lounge");
        var portal = yard.Portals[0];
        var wall = yard.Blocked[0];
        var bedX = (int)Math.Floor(c.Bed.X);
        var bedY = (int)Math.Floor(c.Bed.Y);
        Check("cell: blocked cells and cells outside the map are Wall",
            World.CellAt(yard, c.Bed, wall[0], wall[1]) == MapCell.Wall && World.CellAt(yard, c.Bed, -1, 0) == MapCell.Wall && World.CellAt(yard, c.Bed, 0, -1) == MapCell.Wall
            && World.CellAt(yard, c.Bed, yard.Width, 0) == MapCell.Wall && World.CellAt(yard, c.Bed, 0, yard.Height) == MapCell.Wall);
        Check("cell: a portal cell is a Door", World.CellAt(yard, c.Bed, (int)Math.Floor(portal.X), (int)Math.Floor(portal.Y)) == MapCell.Door);
        Check("cell: the bed cell is a Bed on the bed's map only", World.CellAt(yard, c.Bed, bedX, bedY) == MapCell.Bed && World.CellAt(lounge, c.Bed, bedX, bedY) != MapCell.Bed);
        Check("cell: an ordinary passable cell is Floor, with or without a bed",
            World.CellAt(yard, c.Bed, 5, 2) == MapCell.Floor && World.CellAt(yard, null, 5, 2) == MapCell.Floor && World.CellAt(yard, null, bedX, bedY) == MapCell.Floor);
        var counts = Enumerable.Range(0, yard.Width).SelectMany(x => Enumerable.Range(0, yard.Height).Select(y => World.CellAt(yard, c.Bed, x, y)))
            .GroupBy(k => k).ToDictionary(k => k.Key, k => k.Count());
        Check("cell: every cell of the bed's map is exactly one of wall, door, bed or floor",
            counts[MapCell.Wall] == yard.Blocked.Count + yard.Furnished.Count && counts[MapCell.Door] == yard.Portals.Count && counts[MapCell.Bed] == 1
            && counts[MapCell.Floor] == yard.Width * yard.Height - yard.Blocked.Count - yard.Furnished.Count - yard.Portals.Count - 1);
    }
}
