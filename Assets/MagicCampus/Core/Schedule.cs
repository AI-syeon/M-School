using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCampus.Core
{
    /// <summary>
    /// One step of an NPC's day, as written in routines.json: from At on, the NPC is at Place doing Action. Place is a destination id or
    /// "home" (the NPC's own home destination). Facing is optional; without it the NPC faces the way its place says.
    /// </summary>
    public sealed class RoutineStep
    {
        public const string Home = "home";

        public string At, Place, Action = Actor.Idle, Facing, Note;
        /// <summary>At as minutes past the day's midnight, -1 when At is not a time. Set when the content is read.</summary>
        [Newtonsoft.Json.JsonIgnore] public int Minute = -1;
    }

    /// <summary>
    /// NPC routines (R7). The step in force at a minute is the latest one at or before it; before the day's first step it is the last step
    /// of the list, which is where the NPC spent the night. At a step's minute the NPC walks to its place; on arrival it faces the
    /// step's way and starts the step's action. A load and a new day put every NPC straight into the step in force, action already on.
    /// </summary>
    public static class Schedule
    {
        /// <summary>The step in force. An NPC with no routine stands at home doing nothing.</summary>
        public static RoutineStep StepAt(Content content, string npcId, int minute)
        {
            var steps = content.Routine(npcId);
            if (steps.Count == 0) return new RoutineStep { Place = RoutineStep.Home, Minute = 0 };
            return steps.LastOrDefault(s => s.Minute <= minute) ?? steps[steps.Count - 1];
        }

        public static DestinationDef PlaceOf(Content content, string npcId, RoutineStep step)
        {
            return content.Destination(step.Place == RoutineStep.Home ? content.Npc(npcId).Home : step.Place);
        }

        public static void PlaceAll(Content content, IEnumerable<Actor> npcs, int minute)
        {
            foreach (var npc in npcs)
            {
                var step = StepAt(content, npc.Id, minute);
                var place = PlaceOf(content, npc.Id, step);
                World.Place(npc, place.Position, step.Facing ?? place.Facing);
                npc.Action = step.Action;
            }
        }

        /// <summary>Starts the walk to the last step whose minute lies in (after, upTo]; an earlier one in the same window is already over.</summary>
        public static void Run(Content content, IEnumerable<Actor> npcs, int after, int upTo)
        {
            if (upTo <= after) return;
            foreach (var npc in npcs)
            {
                var due = content.Routine(npc.Id).LastOrDefault(s => s.Minute > after && s.Minute <= upTo);
                if (due != null) Head(content, npc, due);
            }
        }

        /// <summary>After an event every NPC heads back to the step in force now.</summary>
        public static void Resume(Content content, IEnumerable<Actor> npcs, int minute)
        {
            foreach (var npc in npcs) Head(content, npc, StepAt(content, npc.Id, minute));
        }

        /// <summary>Called after the NPC walked: when its walk to a step has ended, it faces the step's way and the action starts.</summary>
        public static void Arrive(Content content, Actor npc)
        {
            if (npc.Goal == null || npc.Path.Count > 0) return;
            var step = npc.Goal;
            var place = PlaceOf(content, npc.Id, step);
            npc.Goal = null;
            npc.Facing = step.Facing ?? place.Facing;
            npc.Action = step.Action;
        }

        static void Head(Content content, Actor npc, RoutineStep step)
        {
            var place = PlaceOf(content, npc.Id, step);
            var there = !npc.Moving && npc.Path.Count == 0 && World.Distance(npc, place.Position) <= 1e-6;
            if (!there) World.WalkTo(content, npc, place);
            npc.Goal = step;
            if (there) Arrive(content, npc);
        }
    }

    /// <summary>Which of an actor's clips shows what it is doing (R7: one rule for the view and for the tests).</summary>
    public static class ActorClips
    {
        /// <summary>
        /// A walking actor plays its walk for its facing. A standing one with an action plays the action's clip for its facing, else
        /// the south clip of that action (actions are drawn facing the player), else its idle. A clip named "idle" or "walk" carries
        /// no facing prefix on actors drawn from the Stage5 sheet, so the bare name is the last try.
        /// </summary>
        public static string KeyFor(Actor actor, Func<string, bool> has)
        {
            string First(params string[] keys) => keys.FirstOrDefault(has) ?? keys[keys.Length - 1];
            if (actor.Moving) return First(actor.Facing + ".walk", "walk");
            if (!string.IsNullOrEmpty(actor.Action) && actor.Action != Actor.Idle)
            {
                var action = new[] { actor.Facing + "." + actor.Action, "south." + actor.Action, actor.Action }.FirstOrDefault(has);
                if (action != null) return action;
            }
            return First(actor.Facing + ".idle", "idle");
        }
    }
}
