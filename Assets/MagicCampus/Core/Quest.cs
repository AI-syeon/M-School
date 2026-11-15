using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MagicCampus.Core
{
    public sealed class QuestStepDef
    {
        /// <summary>One check call: <c>tag target.signboard lit</c>, <c>visited map.lounge</c>, <c>item item.lens 1</c>, <c>done event.snack</c> or <c>cast spell.lantern</c>.</summary>
        public string Check, Label;
    }

    public sealed class QuestItemReward
    {
        public string ItemId;
        public int Count;
    }

    public sealed class QuestRewardDef
    {
        public int Crowns;
        public List<QuestItemReward> Items = new List<QuestItemReward>();
    }

    /// <summary>A request as quests.json writes it. All player-facing text lives here, none in C#.</summary>
    public sealed class QuestDef
    {
        public string Id, Title, Description;
        /// <summary>The npc who asks, or null for a request nobody gives.</summary>
        public string Giver;
        /// <summary>The request shows up on this day (State.Day, counted from 0) and stays until done.</summary>
        public int AvailableFromDay;
        public List<QuestStepDef> Steps = new List<QuestStepDef>();
        public QuestRewardDef Reward = new QuestRewardDef();
    }

    public enum CheckKind { Tag, Visited, Item, Done, Cast }

    /// <summary>A step's check call, parsed. Id is the target, map, item, event or spell the check names.</summary>
    public sealed class QuestCheck
    {
        public CheckKind Kind;
        public string Id, Tag;
        public int Count;

        /// <summary>Returns null and says why in error when the text is not exactly one known check with the right arguments.</summary>
        public static QuestCheck Parse(string text, out string error)
        {
            error = null;
            var calls = Command.Parse(text);
            if (calls.Count != 1 || calls[0].Negated) { error = "a check is exactly one call"; return null; }
            var c = calls[0];
            var a = c.Args;
            bool Args(int n) => a.Count == n;
            switch (c.Name)
            {
                case "tag" when Args(2): return new QuestCheck { Kind = CheckKind.Tag, Id = a[0], Tag = a[1] };
                case "visited" when Args(1): return new QuestCheck { Kind = CheckKind.Visited, Id = a[0] };
                case "item" when Args(2):
                    if (!int.TryParse(a[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0) { error = "item count must be a positive integer"; return null; }
                    return new QuestCheck { Kind = CheckKind.Item, Id = a[0], Count = count };
                case "done" when Args(1): return new QuestCheck { Kind = CheckKind.Done, Id = a[0] };
                case "cast" when Args(1): return new QuestCheck { Kind = CheckKind.Cast, Id = a[0] };
                case "tag": case "visited": case "item": case "done": case "cast":
                    error = c.Name + " has the wrong number of arguments";
                    return null;
                default:
                    error = "unknown check " + c.Name;
                    return null;
            }
        }
    }

    public enum NoticeKind { QuestStep, QuestDone, ItemReceived }

    /// <summary>One thing to tell the player, built from content names. The UI shows these in order and takes them with Game.DrainNotices.</summary>
    public sealed class Notice
    {
        public NoticeKind Kind;
        public string Text, QuestId, ItemId;
    }

    /// <summary>What the morning screen shows about the day that just ended.</summary>
    public sealed class DayReport
    {
        /// <summary>The day that ended, counted from 0 like State.Day.</summary>
        public int Day;
        public DayEnd How;
        public List<QuestDef> Quests = new List<QuestDef>();
        /// <summary>Net change of the wallet over the day: quest rewards, event pay and anything else.</summary>
        public int CrownsEarned;
    }

    /// <summary>An open request with which of its steps are done, for the quest list.</summary>
    public sealed class QuestProgress
    {
        public QuestDef Def;
        public List<bool> StepDone = new List<bool>();
        public int DoneCount => StepDone.Count(d => d);
    }

    /// <summary>
    /// Daily requests: the goal that ties the other systems together. A step's check reads state other systems already keep (magic
    /// tags, visited maps, the bag, completed events, spells cast). A step latches once true, because magic targets reset every day.
    /// When every step is done the quest completes and its reward is given once. Progress is saved in GameState; notices are not.
    /// </summary>
    public sealed class Quests
    {
        readonly Game game;
        readonly List<QuestDef> completedToday = new List<QuestDef>();
        int castsSeen;

        public Quests(Game game) => this.game = game;

        GameState S => game.State;

        public bool Available(QuestDef def) => def.AvailableFromDay <= S.Day;
        public bool IsComplete(string questId) => S.CompletedQuests.Contains(questId);
        public bool StepDone(string questId, int step) => S.QuestSteps.TryGetValue(questId, out var done) && done.Contains(step);

        /// <summary>Requests that are available today and not done, in file order.</summary>
        public List<QuestProgress> Active => game.Content.Quests
            .Where(d => Available(d) && !IsComplete(d.Id))
            .Select(d => new QuestProgress { Def = d, StepDone = d.Steps.Select((_, i) => StepDone(d.Id, i)).ToList() })
            .ToList();

        public List<QuestDef> Completed => S.CompletedQuests.Select(game.Content.Quest).Where(d => d != null).ToList();

        /// <summary>Whether a check holds right now. A check that does not parse never holds; Tests/Core rejects such content.</summary>
        public bool Holds(string check)
        {
            var c = QuestCheck.Parse(check, out _);
            if (c == null) return false;
            switch (c.Kind)
            {
                case CheckKind.Tag: return game.Magic.Tags(c.Id).Contains(c.Tag);
                case CheckKind.Visited: return S.VisitedMaps.Contains(c.Id);
                case CheckKind.Item: return game.Inventory.Count(c.Id) >= c.Count;
                case CheckKind.Done: return S.Completed.Contains(c.Id);
                default: return S.SpellsCast.Contains(c.Id);
            }
        }

        /// <summary>
        /// Latches every step whose check holds and completes the quests whose steps are all done. Game.Update calls it each frame,
        /// EndDay before the day turns over and TryCastSpell after a cast; it is cheap, so calling it again is harmless.
        /// </summary>
        public void Evaluate()
        {
            RecordCast();
            foreach (var def in game.Content.Quests)
            {
                if (!Available(def) || IsComplete(def.Id)) continue;
                for (var i = 0; i < def.Steps.Count; i++)
                {
                    if (StepDone(def.Id, i) || !Holds(def.Steps[i].Check)) continue;
                    if (!S.QuestSteps.TryGetValue(def.Id, out var done)) S.QuestSteps[def.Id] = done = new List<int>();
                    done.Add(i);
                    game.Notify(NoticeKind.QuestStep, def.Title + " · " + def.Steps[i].Label, def.Id);
                }
                if (def.Steps.Count > 0 && Enumerable.Range(0, def.Steps.Count).All(i => StepDone(def.Id, i))) Complete(def);
            }
        }

        /// <summary>The cast rule lives in Magic, which this does not change, so a successful cast is read from Magic.Last when Magic.Casts moved.</summary>
        void RecordCast()
        {
            if (game.Magic.Casts == castsSeen) return;
            castsSeen = game.Magic.Casts;
            var last = game.Magic.Last;
            if (last != null && last.Ok && last.SpellId != null && !S.SpellsCast.Contains(last.SpellId)) S.SpellsCast.Add(last.SpellId);
        }

        void Complete(QuestDef def)
        {
            S.CompletedQuests.Add(def.Id);
            completedToday.Add(def);
            var reward = def.Reward ?? new QuestRewardDef();
            S.Wallet += Math.Max(0, reward.Crowns);
            game.Notify(NoticeKind.QuestDone, "의뢰 완료 · " + def.Title + (reward.Crowns > 0 ? " · " + reward.Crowns + " 크라운" : ""), def.Id);
            foreach (var item in reward.Items ?? new List<QuestItemReward>())
            {
                if (item.Count <= 0 || game.Content.Item(item.ItemId) == null) continue;
                var put = game.Inventory.Add(item.ItemId, item.Count);
                var name = game.Content.Item(item.ItemId).Name;
                game.Notify(NoticeKind.ItemReceived, name + " " + item.Count + "개" + (put.ToMailbox > 0 ? " · " + put.ToMailbox + "개는 우편함으로" : ""), def.Id, item.ItemId);
            }
        }

        /// <summary>Takes the quests completed since the last call. EndDay uses it to fill the day report.</summary>
        internal List<QuestDef> TakeCompletedToday()
        {
            var list = completedToday.ToList();
            completedToday.Clear();
            return list;
        }

        /// <summary>One line per broken rule of the quests in a bundle. Tests/Core runs it on every shipped bundle.</summary>
        public static List<string> Problems(Content c)
        {
            var p = new List<string>();
            void Rule(bool ok, string what) { if (!ok) p.Add(what); }
            Rule(c.Quests.Select(q => q.Id).Distinct().Count() == c.Quests.Count, "quest ids are unique");
            foreach (var q in c.Quests)
            {
                var id = q.Id ?? "quest";
                Rule(!string.IsNullOrWhiteSpace(q.Id) && !string.IsNullOrWhiteSpace(q.Title), id + " has an id and a title");
                Rule(q.Giver == null || c.Npcs.Any(n => n.Id == q.Giver), id + " giver is a known npc");
                Rule(q.AvailableFromDay >= 0, id + " availableFromDay >= 0");
                Rule(q.Steps != null && q.Steps.Count > 0, id + " has steps");
                foreach (var step in q.Steps ?? new List<QuestStepDef>())
                {
                    Rule(!string.IsNullOrWhiteSpace(step.Label), id + " step has a label");
                    var check = QuestCheck.Parse(step.Check, out var error);
                    if (check == null) { p.Add(id + " check '" + step.Check + "': " + error); continue; }
                    switch (check.Kind)
                    {
                        case CheckKind.Tag:
                            var target = c.MagicTargets.FirstOrDefault(t => t.Id == check.Id);
                            Rule(target != null, id + " check '" + step.Check + "' names a known target");
                            Rule(target != null && (target.Tags.Contains(check.Tag) || c.Reactions.Any(r => r.Add.Any(a => a.Tag == check.Tag))),
                                id + " check '" + step.Check + "' tag can be reached");
                            break;
                        case CheckKind.Visited: Rule(c.Maps.Any(m => m.Id == check.Id), id + " check '" + step.Check + "' names a known map"); break;
                        case CheckKind.Item: Rule(c.Item(check.Id) != null, id + " check '" + step.Check + "' names a known item"); break;
                        case CheckKind.Done: Rule(c.Events.Any(e => e.Id == check.Id), id + " check '" + step.Check + "' names a known event"); break;
                        case CheckKind.Cast: Rule(c.Spell(check.Id) != null, id + " check '" + step.Check + "' names a known spell"); break;
                    }
                }
                var reward = q.Reward ?? new QuestRewardDef();
                Rule(reward.Crowns >= 0, id + " reward crowns >= 0");
                foreach (var item in reward.Items ?? new List<QuestItemReward>())
                    Rule(c.Item(item.ItemId) != null && item.Count > 0, id + " reward item " + item.ItemId + " is a known item with a positive count");
            }
            return p;
        }
    }
}
