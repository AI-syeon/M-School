using System;
using System.Collections.Generic;
using MagicCampus.Contracts;

namespace MagicCampus.Content.Compilation
{
    public enum FunctionRole { Trigger, Command, Condition, Effect, ScheduleEntry, Goal, Hint, Behavior, Weakness, ReactionPath, EncounterEnemy, ItemQuantity, ItemGrant, Substitute, FinalStage }

    public enum ParamType { Bool, Int, Number, Word, Text, Ref, Enum, HandleOpen, HandleUse, ConditionList, Any }

    public sealed class ParamDef
    {
        public string Name { get; }
        public ParamType Type { get; }
        public bool Required { get; }
        public bool Variadic { get; }
        public string RefKind { get; }                  // for Ref
        public IReadOnlyList<string> EnumValues { get; }
        public DataNode Default { get; }                // null when none
        public double? Min { get; }

        public ParamDef(string name, ParamType type, bool required, bool variadic, string refKind, IEnumerable<string> enumValues, DataNode defaultValue, double? min)
        {
            Name = Ids.Require(name, nameof(name)); Type = type; Required = required; Variadic = variadic; RefKind = Ids.Opt(refKind);
            EnumValues = PortGuard.Opt(enumValues, nameof(enumValues)); Default = defaultValue;
            if (min.HasValue) PortGuard.Finite(min.Value, nameof(min));
            Min = min;
        }
    }

    public sealed class FunctionDef
    {
        public DataNode Raw { get; }
        public string Name { get; }
        public FunctionRole Role { get; }
        public string Owner { get; }   // owning service id
        public IReadOnlyList<ParamDef> Params { get; }   // positional order

        public FunctionDef(string name, FunctionRole role, string owner, IEnumerable<ParamDef> parameters, DataNode raw = null)
        {
            Name = Ids.Require(name, nameof(name)); Role = role;
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("owner label is required", nameof(owner));
            Owner = owner; // functions.json includes human-readable labels such as "도메인 서비스".
            Params = PortGuard.Opt(parameters, nameof(parameters)); Raw = raw ?? DataNode.Null;
        }
    }
}
