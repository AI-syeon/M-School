using System;

namespace MagicCampus.World.Navigation
{
    public sealed class NavigationOptions
    {
        public static readonly NavigationOptions Default = new NavigationOptions(250000, 4000000);

        public int MaxExpansions { get; }
        public int MaxCells { get; }

        public NavigationOptions(int maxExpansions, int maxCells)
        {
            if (maxExpansions < 1) throw new ArgumentOutOfRangeException(nameof(maxExpansions));
            if (maxCells < 1) throw new ArgumentOutOfRangeException(nameof(maxCells));
            MaxExpansions = maxExpansions;
            MaxCells = maxCells;
        }
    }
}
