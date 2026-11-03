namespace MagicCampus.World.Movement
{
    /// <summary>Names of the frozen world.project plan format shared by WorldStateProjector and WorldCommitParticipant.</summary>
    public static class WorldProjectionProtocol
    {
        public const string ParticipantId = "world";
        public const string Operation = "project";
        public const string PlayerKey = "player";
        public const string NpcPositionsKey = "npcPositions";
        public const string NpcIdKey = "npcId";
        public const string MapIdKey = "mapId";
        public const string XKey = "x";
        public const string YKey = "y";

        public const string PlayerMapPointer = "/player/mapId";
        public const string PlayerXPointer = "/player/x";
        public const string PlayerYPointer = "/player/y";
        public const string NpcPositionsPointer = "/npcPositions";
    }
}
