using System;
using MagicCampus.Contracts;

namespace MagicCampus.World.Navigation
{
    /// <summary>Shared geometry helpers for planning and movement. Units are tiles, as in Position.</summary>
    public static class NavMath
    {
        public const double Epsilon = 1e-9;

        public static double Distance(Position a, Position b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
