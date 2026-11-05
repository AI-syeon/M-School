using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MagicCampus.Core
{
    /// <summary>
    /// One running event (R1: at most one, held by Game.Event). Runs the script commands in order; a choice splices the chosen
    /// fork in after itself. Nothing is written until the last command finishes, so cancel only puts the actors back.
    /// </summary>
    public sealed class EventRun
    {
        public readonly EventDef Def;
        readonly List<Command> commands;
        readonly List<Actor> startActors;
        int next;
        bool placed;
        Actor mover;
        DestinationDef moveTo;
        List<string> forks;

        public EventRun(Game game, EventDef def)
        {
            Def = def;
            commands = Command.Parse(def.Script);
            startActors = game.Actors.Select(a => a.Clone()).ToList();
        }

        public static bool Holds(Game game, string when)
        {
            foreach (var c in Command.Parse(when))
            {
                if (c.Name != "eq") throw new InvalidOperationException("unknown condition " + c.Name);
                if ((game.Flag(c.Args[0]) == c.Args[1]) == c.Negated) return false;
            }
            return true;
        }

        /// <summary>Returns true when the event finished and its effects are applied.</summary>
        public bool Update(Game game, double dt)
        {
            if (!placed)
            {
                foreach (var join in game.Content.Recipe(Def.Id).Join) World.Place(game.Actor(join.ActorId), join.JoinAt);
                placed = true;
                return false;
            }
            game.Hidden = false;
            while (game.Dialogue == null)
            {
                if (mover != null)
                {
                    World.Walk(game.Content, mover, dt);
                    dt = 0;
                    if (mover.Path.Count > 0) return false;
                    if (World.Distance(mover, moveTo.Position) > moveTo.Radius) throw new InvalidOperationException(mover.Id + " stopped short of " + moveTo.Id);
                    mover.Facing = moveTo.Facing;
                    mover = null;
                }
                if (next == commands.Count)
                {
                    Complete(game);
                    return true;
                }
                Execute(game, commands[next++]);
            }
            return false;
        }

        void Execute(Game game, Command c)
        {
            switch (c.Name)
            {
                case "move":
                    mover = game.Actor(c.Args[0]);
                    moveTo = game.Content.Destination(c.Args[1]);
                    World.WalkTo(game.Content, mover, moveTo);
                    break;
                case "say":
                    game.Dialogue = new Dialogue { Speaker = c.Args[0], Text = c.Args[1] };
                    break;
                case "choice":
                    forks = c.Args.Skip(1).Where((_, i) => i % 2 == 0).ToList();
                    game.Dialogue = new Dialogue { Text = c.Args[0], Choices = c.Args.Skip(1).Where((_, i) => i % 2 == 1).ToList() };
                    break;
                default:
                    throw new InvalidOperationException("unknown command " + c.Name);
            }
        }

        public void Choose(Game game, int index)
        {
            commands.InsertRange(next, Command.Parse(Def.Forks[forks[index]]));
            game.Dialogue = null;
        }

        void Complete(Game game)
        {
            var state = game.State;
            foreach (var effect in Command.Parse(Def.Effects))
            {
                switch (effect.Name)
                {
                    case "set": state.Flags[effect.Args[0]] = JToken.Parse(effect.Args[1]); break;
                    case "currency.add": state.Wallet += int.Parse(effect.Args[0], CultureInfo.InvariantCulture); break;
                    default: throw new InvalidOperationException("unknown effect " + effect.Name);
                }
            }
            state.Completed.Add(Def.Id);
            state.Minute += Def.CostMinutes;
            state.Appointments.Remove(Def.Id);
        }

        /// <summary>Puts every actor back where it stood at the start. The clock and state are untouched until Complete.</summary>
        public void Restore(Game game)
        {
            foreach (var saved in startActors) game.Actor(saved.Id).CopyFrom(saved);
        }
    }
}
