using System;

namespace MagicCampus.World.Movement
{
    /// <summary>Bounds and thresholds of MoveService. All values are configuration, none is tied to a map, NPC or event.</summary>
    public sealed class MoveOptions
    {
        public static readonly MoveOptions Default = new MoveOptions();

        public int MaxReplans { get; }
        public double StuckSeconds { get; }
        public double ReplanCooldownSeconds { get; }
        public double PortalReachTolerance { get; }
        public double MinProgress { get; }

        public MoveOptions(int maxReplans = 32, double stuckSeconds = 3.0, double replanCooldownSeconds = 0.25,
            double portalReachTolerance = 0.1)
        {
            if (maxReplans < 0) throw new ArgumentOutOfRangeException(nameof(maxReplans));
            if (!(stuckSeconds > 0) || double.IsInfinity(stuckSeconds)) throw new ArgumentOutOfRangeException(nameof(stuckSeconds));
            if (!(replanCooldownSeconds >= 0) || double.IsInfinity(replanCooldownSeconds)) throw new ArgumentOutOfRangeException(nameof(replanCooldownSeconds));
            if (!(portalReachTolerance >= 0) || double.IsInfinity(portalReachTolerance)) throw new ArgumentOutOfRangeException(nameof(portalReachTolerance));
            MaxReplans = maxReplans;
            StuckSeconds = stuckSeconds;
            ReplanCooldownSeconds = replanCooldownSeconds;
            PortalReachTolerance = portalReachTolerance;
            MinProgress = 1e-6;
        }
    }
}
