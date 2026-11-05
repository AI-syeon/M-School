using MagicCampus.Contracts;

namespace MagicCampus.Timekeeping
{
    public interface IClockService
    {
        GameTime Now { get; }
        /// <summary>1 simulation-second = 1 game-minute. No advance when pause.Calendar is true.</summary>
        ClockAdvance Advance(double simulationSeconds, IPausePolicy pause);
    }
}
